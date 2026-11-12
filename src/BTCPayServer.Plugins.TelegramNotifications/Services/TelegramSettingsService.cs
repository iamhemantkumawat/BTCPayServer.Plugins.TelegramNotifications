using BTCPayServer.Plugins.TelegramNotifications.Models;
using BTCPayServer.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.TelegramNotifications.Services;

public sealed class TelegramSettingsService
{
    private const string SettingsName = "TelegramNotifications.Settings.v1";
    private readonly SettingsRepository _settingsRepository;
    private readonly IDataProtector _protector;
    private readonly ILogger<TelegramSettingsService> _logger;

    public TelegramSettingsService(
        SettingsRepository settingsRepository,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<TelegramSettingsService> logger)
    {
        _settingsRepository = settingsRepository;
        _protector = dataProtectionProvider.CreateProtector("BTCPayServer.Plugins.TelegramNotifications.BotToken.v1");
        _logger = logger;
    }

    public async Task<TelegramNotificationSettings> GetAsync() =>
        await _settingsRepository.GetSettingAsync<TelegramNotificationSettings>(SettingsName) ?? new();

    public async Task<TelegramRuntimeSettings?> GetRuntimeAsync()
    {
        var settings = await GetAsync();
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ProtectedBotToken) || settings.ChatIds.Count == 0)
            return null;

        try
        {
            return new TelegramRuntimeSettings(settings, _protector.Unprotect(settings.ProtectedBotToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram Notifications could not decrypt the configured bot token");
            return null;
        }
    }

    public string? GetToken(TelegramNotificationSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ProtectedBotToken))
            return null;
        try
        {
            return _protector.Unprotect(settings.ProtectedBotToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(TelegramNotificationSettings settings, string? newBotToken)
    {
        if (!string.IsNullOrWhiteSpace(newBotToken))
            settings.ProtectedBotToken = _protector.Protect(newBotToken.Trim());

        settings.ChatIds = settings.ChatIds.Distinct().ToList();
        settings.AllowedTelegramUserIds = settings.AllowedTelegramUserIds.Distinct().ToList();
        settings.StoreIds = settings.StoreIds.Distinct(StringComparer.Ordinal).ToList();
        settings.MetadataKeys = settings.MetadataKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        await _settingsRepository.UpdateSetting(settings, SettingsName);
    }
}
