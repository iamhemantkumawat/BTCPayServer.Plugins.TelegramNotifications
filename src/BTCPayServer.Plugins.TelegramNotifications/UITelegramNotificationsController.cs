using System.Net;
using System.Text.RegularExpressions;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Plugins.TelegramNotifications.Models;
using BTCPayServer.Plugins.TelegramNotifications.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AuthenticationSchemes = BTCPayServer.Abstractions.Constants.AuthenticationSchemes;

namespace BTCPayServer.Plugins.TelegramNotifications;

[Area(Plugin.Area)]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyServerSettings)]
public sealed class UITelegramNotificationsController(
    TelegramSettingsService settingsService,
    TelegramBotClient botClient,
    StoreRepository storeRepository) : Controller
{
    [HttpGet("~/server/services/telegram-notifications")]
    public async Task<IActionResult> Index()
    {
        var settings = await settingsService.GetAsync();
        var model = await ToViewModelAsync(settings);
        await ApplyBotStatusAsync(model, settings);
        SetPageData();
        return View(model);
    }

    [HttpPost("~/server/services/telegram-notifications")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(TelegramSettingsViewModel model, string command = "save")
    {
        var existing = await settingsService.GetAsync();
        var chatIds = ParseIds(model.ChatIds, "Telegram chat IDs", allowNegative: true);
        var userIds = ParseIds(model.AllowedTelegramUserIds, "Allowed Telegram user IDs", allowNegative: false);

        if (!string.IsNullOrWhiteSpace(model.BotToken) &&
            !Regex.IsMatch(model.BotToken.Trim(), @"^\d+:[A-Za-z0-9_-]{30,}$"))
            ModelState.AddModelError(nameof(model.BotToken), "The Telegram bot token format is invalid.");
        if (model.Enabled && string.IsNullOrWhiteSpace(model.BotToken) && string.IsNullOrWhiteSpace(existing.ProtectedBotToken))
            ModelState.AddModelError(nameof(model.BotToken), "A bot token is required when notifications are enabled.");
        if (model.Enabled && chatIds.Count == 0)
            ModelState.AddModelError(nameof(model.ChatIds), "At least one Telegram chat ID is required.");
        if (model.Enabled && userIds.Count == 0)
            ModelState.AddModelError(nameof(model.AllowedTelegramUserIds), "At least one authorized Telegram user ID is required when notifications are enabled.");
        if (!model.AllStores && model.StoreIds.Count == 0)
            ModelState.AddModelError(nameof(model.StoreIds), "Select at least one store or enable all stores.");

        var normalizedBaseUrl = model.PublicBaseUrl.Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(normalizedBaseUrl) &&
            (!Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("https" or "http")))
            ModelState.AddModelError(nameof(model.PublicBaseUrl), "Enter an absolute HTTP or HTTPS URL.");
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(model.TimeZoneId.Trim());
        }
        catch
        {
            ModelState.AddModelError(nameof(model.TimeZoneId), "The time zone ID is not available on this server.");
        }

        var settings = new TelegramNotificationSettings
        {
            Enabled = model.Enabled,
            ProtectedBotToken = existing.ProtectedBotToken,
            ChatIds = chatIds,
            AllowedTelegramUserIds = userIds,
            AllStores = model.AllStores,
            StoreIds = model.StoreIds,
            PublicBaseUrl = normalizedBaseUrl,
            TimeZoneId = model.TimeZoneId.Trim(),
            EnableManualActions = model.EnableManualActions,
            ActionExpiryMinutes = model.ActionExpiryMinutes,
            IncludeAllMetadata = model.IncludeAllMetadata,
            MetadataKeys = SplitValues(model.MetadataKeys),
            IncludeBuyerEmail = model.IncludeBuyerEmail,
            IncludeBuyerPhone = model.IncludeBuyerPhone,
            IncludeBuyerAddress = model.IncludeBuyerAddress,
            NotifyInvoiceCreated = model.NotifyInvoiceCreated,
            NotifyPaymentReceived = model.NotifyPaymentReceived,
            NotifyPaymentSettled = model.NotifyPaymentSettled,
            NotifyProcessing = model.NotifyProcessing,
            NotifyExpired = model.NotifyExpired,
            NotifySettled = model.NotifySettled,
            NotifyInvalid = model.NotifyInvalid,
            NotifyRefunds = model.NotifyRefunds
        };

        if (!ModelState.IsValid)
        {
            model.HasBotToken = !string.IsNullOrWhiteSpace(existing.ProtectedBotToken);
            model.Stores = await GetStoreOptionsAsync();
            model.BotToken = null;
            await ApplyBotStatusAsync(model, existing);
            SetPageData();
            return View(model);
        }

        var token = !string.IsNullOrWhiteSpace(model.BotToken)
            ? model.BotToken.Trim()
            : settingsService.GetToken(existing);

        await settingsService.SaveAsync(settings, model.BotToken);

        if (command.Equals("test", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                    throw new InvalidOperationException("No bot token is configured.");
                var bot = await botClient.GetMeAsync(token, HttpContext.RequestAborted);
                foreach (var chatId in settings.ChatIds)
                {
                    var text = "✅ <b>BTCPay Telegram Notifications test successful</b>\n\n" +
                               $"Bot: @{WebUtility.HtmlEncode(bot.Username ?? bot.FirstName ?? bot.Id.ToString())}\n" +
                               $"Chat ID: <code>{chatId}</code>";
                    await botClient.SendMessageAsync(token, chatId, text, null, HttpContext.RequestAborted);
                }
            }
            catch (Exception ex)
            {
                TempData[WellKnownTempData.ErrorMessage] =
                    $"Telegram settings were saved, but the test message failed: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        TempData[WellKnownTempData.SuccessMessage] = command.Equals("test", StringComparison.OrdinalIgnoreCase)
            ? "Telegram settings saved and the test message was sent."
            : "Telegram notification settings saved.";
        return RedirectToAction(nameof(Index));
    }

    private List<long> ParseIds(string value, string fieldName, bool allowNegative)
    {
        var result = new List<long>();
        foreach (var part in SplitValues(value))
        {
            if (!long.TryParse(part, out var id) || id == 0 || (!allowNegative && id < 0))
                ModelState.AddModelError(fieldName == "Telegram chat IDs" ? nameof(TelegramSettingsViewModel.ChatIds) : nameof(TelegramSettingsViewModel.AllowedTelegramUserIds),
                    $"'{part}' is not a valid {fieldName.TrimEnd('s')}.");
            else
                result.Add(id);
        }
        return result.Distinct().ToList();
    }

    private static List<string> SplitValues(string? value) =>
        (value ?? string.Empty).Split([',', ';', '\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private async Task<TelegramSettingsViewModel> ToViewModelAsync(TelegramNotificationSettings settings) => new()
    {
        Enabled = settings.Enabled,
        HasBotToken = !string.IsNullOrWhiteSpace(settings.ProtectedBotToken),
        ChatIds = string.Join(Environment.NewLine, settings.ChatIds),
        AllowedTelegramUserIds = string.Join(Environment.NewLine, settings.AllowedTelegramUserIds),
        AllStores = settings.AllStores,
        StoreIds = settings.StoreIds,
        Stores = await GetStoreOptionsAsync(),
        PublicBaseUrl = string.IsNullOrWhiteSpace(settings.PublicBaseUrl)
            ? $"{Request.Scheme}://{Request.Host}{Request.PathBase}"
            : settings.PublicBaseUrl,
        TimeZoneId = settings.TimeZoneId,
        EnableManualActions = settings.EnableManualActions,
        ActionExpiryMinutes = settings.ActionExpiryMinutes,
        IncludeAllMetadata = settings.IncludeAllMetadata,
        MetadataKeys = string.Join(", ", settings.MetadataKeys),
        IncludeBuyerEmail = settings.IncludeBuyerEmail,
        IncludeBuyerPhone = settings.IncludeBuyerPhone,
        IncludeBuyerAddress = settings.IncludeBuyerAddress,
        NotifyInvoiceCreated = settings.NotifyInvoiceCreated,
        NotifyPaymentReceived = settings.NotifyPaymentReceived,
        NotifyPaymentSettled = settings.NotifyPaymentSettled,
        NotifyProcessing = settings.NotifyProcessing,
        NotifyExpired = settings.NotifyExpired,
        NotifySettled = settings.NotifySettled,
        NotifyInvalid = settings.NotifyInvalid,
        NotifyRefunds = settings.NotifyRefunds
    };

    private async Task<List<StoreOption>> GetStoreOptionsAsync() =>
        (await storeRepository.GetStores()).OrderBy(s => s.StoreName)
        .Select(s => new StoreOption(s.Id, s.StoreName)).ToList();

    private async Task ApplyBotStatusAsync(
        TelegramSettingsViewModel model,
        TelegramNotificationSettings settings)
    {
        if (!model.HasBotToken)
        {
            model.BotStatusTitle = "Inactive";
            model.BotStatusDetail = "Bot token is not configured.";
            return;
        }

        if (!settings.Enabled)
        {
            model.BotStatusTitle = "Inactive";
            model.BotStatusDetail = "The bot is configured, but invoice notifications are disabled.";
            return;
        }

        if (settings.ChatIds.Count == 0)
        {
            model.BotStatusTitle = "Inactive";
            model.BotStatusDetail = "Add at least one destination chat ID.";
            return;
        }

        var token = settingsService.GetToken(settings);
        if (string.IsNullOrWhiteSpace(token))
        {
            model.BotStatusTitle = "Inactive";
            model.BotStatusDetail = "The stored bot token could not be decrypted.";
            return;
        }

        try
        {
            var bot = await botClient.GetMeAsync(token, HttpContext.RequestAborted);
            model.BotActive = true;
            model.BotStatusTitle = "Active";
            var identity = !string.IsNullOrWhiteSpace(bot.Username)
                ? $"@{bot.Username}"
                : bot.FirstName ?? bot.Id.ToString();
            model.BotStatusDetail =
                $"Connected as {identity}. Monitoring {settings.ChatIds.Count} Telegram chat{(settings.ChatIds.Count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception ex)
        {
            model.BotStatusTitle = "Inactive";
            model.BotStatusDetail = ex.Message;
        }
    }

    private void SetPageData()
    {
        ViewData["Title"] = "Telegram Notifications";
        ViewData["ActivePage"] = nameof(Plugin);
    }
}
