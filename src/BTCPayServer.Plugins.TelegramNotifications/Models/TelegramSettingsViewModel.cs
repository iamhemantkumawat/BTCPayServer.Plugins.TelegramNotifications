using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.TelegramNotifications.Models;

public sealed class TelegramSettingsViewModel
{
    public bool Enabled { get; set; }

    [Display(Name = "Bot token")]
    public string? BotToken { get; set; }
    public bool HasBotToken { get; set; }
    public bool BotActive { get; set; }
    public string BotStatusTitle { get; set; } = "Inactive";
    public string BotStatusDetail { get; set; } = "Bot token is not configured.";

    [Display(Name = "Telegram chat IDs")]
    public string ChatIds { get; set; } = string.Empty;

    [Display(Name = "Allowed Telegram user IDs")]
    public string AllowedTelegramUserIds { get; set; } = string.Empty;

    public bool AllStores { get; set; } = true;
    public List<string> StoreIds { get; set; } = [];
    public List<StoreOption> Stores { get; set; } = [];

    [Display(Name = "Public BTCPay URL")]
    public string PublicBaseUrl { get; set; } = string.Empty;

    [Display(Name = "Time zone")]
    public string TimeZoneId { get; set; } = "Asia/Kolkata";

    public bool EnableManualActions { get; set; } = true;

    [Range(5, 1440)]
    public int ActionExpiryMinutes { get; set; } = 30;

    public bool IncludeAllMetadata { get; set; } = true;
    public string MetadataKeys { get; set; } = string.Empty;
    public bool IncludeBuyerEmail { get; set; }
    public bool IncludeBuyerPhone { get; set; }
    public bool IncludeBuyerAddress { get; set; }

    public bool NotifyInvoiceCreated { get; set; } = true;
    public bool NotifyPaymentReceived { get; set; } = true;
    public bool NotifyPaymentSettled { get; set; } = true;
    public bool NotifyProcessing { get; set; } = true;
    public bool NotifyExpired { get; set; } = true;
    public bool NotifySettled { get; set; } = true;
    public bool NotifyInvalid { get; set; } = true;
    public bool NotifyRefunds { get; set; } = true;
}

public sealed record StoreOption(string Id, string Name);
