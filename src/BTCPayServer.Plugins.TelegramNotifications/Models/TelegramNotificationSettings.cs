namespace BTCPayServer.Plugins.TelegramNotifications.Models;

public sealed class TelegramNotificationSettings
{
    public bool Enabled { get; set; }
    public string? ProtectedBotToken { get; set; }
    public List<long> ChatIds { get; set; } = [];
    public List<long> AllowedTelegramUserIds { get; set; } = [];
    public bool AllStores { get; set; } = true;
    public List<string> StoreIds { get; set; } = [];
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "Asia/Kolkata";
    public bool EnableManualActions { get; set; } = true;
    public int ActionExpiryMinutes { get; set; } = 30;
    public bool IncludeAllMetadata { get; set; } = true;
    public List<string> MetadataKeys { get; set; } = [];
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

public sealed record TelegramRuntimeSettings(TelegramNotificationSettings Settings, string BotToken)
{
    public bool IsStoreEnabled(string storeId) =>
        Settings.AllStores || Settings.StoreIds.Contains(storeId, StringComparer.Ordinal);
}
