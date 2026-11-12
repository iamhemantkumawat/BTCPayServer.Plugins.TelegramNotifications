using System.Net;
using BTCPayServer.Plugins.TelegramNotifications.Data;
using BTCPayServer.Plugins.TelegramNotifications.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.TelegramNotifications.Services;

public sealed class TelegramUpdateListener(
    TelegramSettingsService settingsService,
    TelegramBotClient botClient,
    TelegramActionService actionService,
    IDbContextFactory<TelegramNotificationsDbContext> dbContextFactory,
    ILogger<TelegramUpdateListener> logger) : BackgroundService
{
    private const string OffsetKey = "LastTelegramUpdateId";
    private DateTimeOffset _nextCleanup = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var offset = await GetOffsetAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var runtime = await settingsService.GetRuntimeAsync();
            if (runtime is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                continue;
            }

            try
            {
                var updates = await botClient.GetUpdatesAsync(runtime.BotToken, offset, stoppingToken);
                foreach (var update in updates.OrderBy(u => u.UpdateId))
                {
                    if (update.Message is not null)
                        await HandleMessageAsync(runtime, update.Message, stoppingToken);
                    if (runtime.Settings.EnableManualActions && update.CallbackQuery is not null)
                        await actionService.HandleCallbackAsync(runtime, update.CallbackQuery, stoppingToken);
                    offset = update.UpdateId + 1;
                    await SetOffsetAsync(offset, stoppingToken);
                }

                if (_nextCleanup <= DateTimeOffset.UtcNow)
                {
                    await CleanupAsync(stoppingToken);
                    _nextCleanup = DateTimeOffset.UtcNow.AddHours(12);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (TelegramApiException ex)
            {
                logger.LogWarning("Telegram Notifications polling error: {Message}. Use a dedicated bot and remove any existing Telegram webhook.", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Telegram Notifications update listener failed");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task HandleMessageAsync(
        TelegramRuntimeSettings runtime,
        TelegramMessage message,
        CancellationToken cancellationToken)
    {
        if (!IsStatusCommand(message.Text))
            return;

        var isAuthorizedUser = message.From is not null &&
                               runtime.Settings.AllowedTelegramUserIds.Contains(message.From.Id);
        var isAuthorizedChat = runtime.Settings.ChatIds.Contains(message.Chat.Id);
        if (!isAuthorizedUser || !isAuthorizedChat)
        {
            const string unauthorized = "⛔ <b>You are not authorized to use this bot.</b>\n\n" +
                                        "Ask the BTCPay administrator to add your Telegram user ID in the plugin settings.";
            await botClient.SendMessageAsync(runtime.BotToken, message.Chat.Id, unauthorized, null, cancellationToken);
            return;
        }

        var storeScope = runtime.Settings.AllStores
            ? "All current and future stores"
            : $"{runtime.Settings.StoreIds.Count} selected store(s)";
        var actions = runtime.Settings.EnableManualActions
            ? "Enabled for authorized administrators"
            : "Disabled";
        var reply = "✅ <b>BTCPay Telegram Notifications is active</b>\n\n" +
                    $"🏪 <b>Stores:</b> {WebUtility.HtmlEncode(storeScope)}\n" +
                    "🔔 <b>Invoice alerts:</b> Enabled\n" +
                    $"🛡 <b>Manual actions:</b> {WebUtility.HtmlEncode(actions)}\n\n" +
                    "I will notify this chat when configured BTCPay invoice events occur.";

        await botClient.SendMessageAsync(runtime.BotToken, message.Chat.Id, reply, null, cancellationToken);
    }

    internal static bool IsStatusCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var command = text.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)[0];
        var suffix = command.IndexOf('@');
        if (suffix >= 0)
            command = command[..suffix];
        return command.Equals("/start", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("/status", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<long> GetOffsetAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var value = await db.State.Where(s => s.Key == OffsetKey).Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        return long.TryParse(value, out var offset) ? offset : 0;
    }

    private async Task SetOffsetAsync(long offset, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var state = await db.State.FirstOrDefaultAsync(s => s.Key == OffsetKey, cancellationToken);
        if (state is null)
            db.State.Add(new TelegramStateRecord { Key = OffsetKey, Value = offset.ToString() });
        else
            state.Value = offset.ToString();
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var actionCutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var eventCutoff = DateTimeOffset.UtcNow.AddDays(-90);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.Actions.Where(a => a.ExpiresAt < actionCutoff).ExecuteDeleteAsync(cancellationToken);
        await db.NotificationEvents.Where(e => e.CreatedAt < eventCutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
