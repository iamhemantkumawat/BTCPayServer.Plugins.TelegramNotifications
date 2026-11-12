using System.Net;
using System.Security.Cryptography;
using BTCPayServer.Plugins.TelegramNotifications.Data;
using BTCPayServer.Plugins.TelegramNotifications.Models;
using BTCPayServer.Services.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InvoiceStatus = BTCPayServer.Client.Models.InvoiceStatus;

namespace BTCPayServer.Plugins.TelegramNotifications.Services;

public sealed class TelegramActionService(
    IDbContextFactory<TelegramNotificationsDbContext> dbContextFactory,
    InvoiceRepository invoiceRepository,
    TelegramBotClient botClient,
    ILogger<TelegramActionService> logger)
{
    public async Task<PreparedKeyboard> CreateKeyboardAsync(
        TelegramRuntimeSettings runtime,
        InvoiceEntity invoice,
        long chatId,
        string? transactionUrl,
        CancellationToken cancellationToken)
    {
        var rows = new List<TelegramInlineButton[]>();
        var tokens = new List<string>();
        var firstRow = new List<TelegramInlineButton>();
        var invoiceUrl = GetInvoiceUrl(runtime.Settings.PublicBaseUrl, invoice.StoreId, invoice.Id);
        if (invoiceUrl is not null)
            firstRow.Add(new TelegramInlineButton("View Invoice", Url: invoiceUrl));
        if (Uri.TryCreate(transactionUrl, UriKind.Absolute, out _))
            firstRow.Add(new TelegramInlineButton("Transaction", Url: transactionUrl));
        if (firstRow.Count != 0)
            rows.Add(firstRow.ToArray());

        if (runtime.Settings.EnableManualActions && runtime.Settings.AllowedTelegramUserIds.Count != 0)
        {
            var actionRow = new List<TelegramInlineButton>();
            var state = invoice.GetInvoiceState();
            if (state.CanMarkComplete())
            {
                var token = await CreateActionAsync(invoice, chatId, "settled", runtime.Settings.ActionExpiryMinutes, cancellationToken);
                tokens.Add(token);
                actionRow.Add(new TelegramInlineButton("✅ Mark Settled", $"ta:{token}"));
            }
            if (state.CanMarkInvalid())
            {
                var token = await CreateActionAsync(invoice, chatId, "invalid", runtime.Settings.ActionExpiryMinutes, cancellationToken);
                tokens.Add(token);
                actionRow.Add(new TelegramInlineButton("⛔ Mark Invalid", $"ta:{token}"));
            }
            if (actionRow.Count != 0)
                rows.Add(actionRow.ToArray());
        }

        return new PreparedKeyboard(rows.Count == 0 ? null : new TelegramInlineKeyboard(rows.ToArray()), tokens);
    }

    public async Task SetMessageIdAsync(IEnumerable<string> tokens, int messageId, CancellationToken cancellationToken)
    {
        var tokenList = tokens.ToArray();
        if (tokenList.Length == 0)
            return;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.Actions.Where(a => tokenList.Contains(a.Token))
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.MessageId, messageId), cancellationToken);
    }

    public async Task HandleCallbackAsync(
        TelegramRuntimeSettings runtime,
        TelegramCallbackQuery callback,
        CancellationToken cancellationToken)
    {
        if (callback.Message is null || string.IsNullOrWhiteSpace(callback.Data))
            return;

        var separator = callback.Data.IndexOf(':');
        if (separator <= 0 || separator == callback.Data.Length - 1)
            return;
        var command = callback.Data[..separator];
        var token = callback.Data[(separator + 1)..];
        if (token.Length > 40)
            return;

        if (!runtime.Settings.AllowedTelegramUserIds.Contains(callback.From.Id))
        {
            await botClient.AnswerCallbackAsync(runtime.BotToken, callback.Id,
                "You are not authorized to change invoices.", true, cancellationToken);
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var action = await db.Actions.FirstOrDefaultAsync(a => a.Token == token, cancellationToken);
        if (action is null || action.ChatId != callback.Message.Chat.Id)
        {
            await botClient.AnswerCallbackAsync(runtime.BotToken, callback.Id,
                "This action is not valid for this chat.", true, cancellationToken);
            return;
        }

        if (action.UsedAt is not null || action.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await botClient.AnswerCallbackAsync(runtime.BotToken, callback.Id,
                "This action has expired or was already used.", true, cancellationToken);
            await RemoveButtonsAsync(runtime, callback, action, cancellationToken);
            return;
        }

        if (command == "ta")
        {
            var label = action.Action == "settled" ? "settled" : "invalid";
            var keyboard = new TelegramInlineKeyboard(
            [
                [new TelegramInlineButton($"Confirm: mark {label}", $"tc:{token}")],
                [new TelegramInlineButton("Cancel", $"tx:{token}")]
            ]);
            await botClient.EditReplyMarkupAsync(runtime.BotToken, action.ChatId,
                callback.Message.MessageId, keyboard, cancellationToken);
            await botClient.AnswerCallbackAsync(runtime.BotToken, callback.Id,
                "Confirm this invoice status change.", false, cancellationToken);
            return;
        }

        if (command == "tx")
        {
            action.UsedAt = DateTimeOffset.UtcNow;
            action.TelegramUserId = callback.From.Id;
            action.TelegramUsername = callback.From.Username;
            action.Result = "Cancelled";
            await db.SaveChangesAsync(cancellationToken);
            await RemoveButtonsAsync(runtime, callback, action, cancellationToken);
            await botClient.AnswerCallbackAsync(runtime.BotToken, callback.Id, "Action cancelled.", false, cancellationToken);
            return;
        }

        if (command != "tc")
            return;

        var invoice = await invoiceRepository.GetInvoice(action.InvoiceId);
        if (invoice is null || !invoice.StoreId.Equals(action.StoreId, StringComparison.Ordinal))
        {
            await botClient.AnswerCallbackAsync(runtime.BotToken, callback.Id,
                "Invoice was not found.", true, cancellationToken);
            return;
        }

        var targetStatus = action.Action == "settled" ? InvoiceStatus.Settled : InvoiceStatus.Invalid;
        action.PreviousStatus = invoice.GetInvoiceState().ToString();
        var changed = await invoiceRepository.MarkInvoiceStatus(invoice.Id, targetStatus);
        action.UsedAt = DateTimeOffset.UtcNow;
        action.TelegramUserId = callback.From.Id;
        action.TelegramUsername = callback.From.Username;
        action.Result = changed ? $"Marked {action.Action}" : "Invoice state no longer allows this action";
        await db.SaveChangesAsync(cancellationToken);

        await RemoveButtonsAsync(runtime, callback, action, cancellationToken);
        if (!changed)
        {
            await botClient.AnswerCallbackAsync(runtime.BotToken, callback.Id,
                "Invoice state changed; this action is no longer available.", true, cancellationToken);
            return;
        }

        await botClient.AnswerCallbackAsync(runtime.BotToken, callback.Id,
            $"Invoice marked {action.Action}.", false, cancellationToken);
        var actor = !string.IsNullOrWhiteSpace(callback.From.Username)
            ? $"@{callback.From.Username}"
            : callback.From.Id.ToString();
        var audit = $"🔐 <b>Invoice status changed</b>\n\n" +
                    $"📄 <b>Invoice:</b> <code>{WebUtility.HtmlEncode(invoice.Id)}</code>\n" +
                    $"📌 <b>New status:</b> {WebUtility.HtmlEncode(action.Action)}\n" +
                    $"👤 <b>Changed by:</b> {WebUtility.HtmlEncode(actor)}";
        await botClient.SendMessageAsync(runtime.BotToken, action.ChatId, audit, null, cancellationToken);
    }

    private async Task<string> CreateActionAsync(
        InvoiceEntity invoice,
        long chatId,
        string action,
        int expiryMinutes,
        CancellationToken cancellationToken)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.Actions.Add(new TelegramActionRecord
        {
            Token = token,
            InvoiceId = invoice.Id,
            StoreId = invoice.StoreId,
            Action = action,
            ChatId = chatId,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(expiryMinutes, 5, 1440))
        });
        await db.SaveChangesAsync(cancellationToken);
        return token;
    }

    private async Task RemoveButtonsAsync(
        TelegramRuntimeSettings runtime,
        TelegramCallbackQuery callback,
        TelegramActionRecord action,
        CancellationToken cancellationToken)
    {
        try
        {
            var invoiceUrl = GetInvoiceUrl(runtime.Settings.PublicBaseUrl, action.StoreId, action.InvoiceId);
            TelegramInlineKeyboard? keyboard = invoiceUrl is null
                ? null
                : new TelegramInlineKeyboard([[new TelegramInlineButton("View Invoice", Url: invoiceUrl)]]);
            await botClient.EditReplyMarkupAsync(runtime.BotToken, action.ChatId,
                callback.Message!.MessageId, keyboard, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram Notifications could not remove used action buttons");
        }
    }

    internal static string? GetInvoiceUrl(string baseUrl, string storeId, string invoiceId)
    {
        if (!Uri.TryCreate(baseUrl?.TrimEnd('/'), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http"))
            return null;
        return $"{uri}/stores/{Uri.EscapeDataString(storeId)}/invoices/{Uri.EscapeDataString(invoiceId)}";
    }
}

public sealed record PreparedKeyboard(TelegramInlineKeyboard? Keyboard, IReadOnlyList<string> ActionTokens);
