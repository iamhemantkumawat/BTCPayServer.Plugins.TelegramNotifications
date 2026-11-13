using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.TelegramNotifications.Data;
using BTCPayServer.Plugins.TelegramNotifications.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.TelegramNotifications;

public class Plugin : BaseBTCPayServerPlugin
{
    public const string Area = "TelegramNotifications";
    public const string ViewsDirectory = "/Plugins/" + Area + "/Views";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.4.7 && <=2.4.99" }
    };

    public override void Execute(IServiceCollection services)
    {
        services.AddUIExtension("server-nav", $"{ViewsDirectory}/NavExtension.cshtml");
        services.AddPluginDbContext<TelegramNotificationsDbContext>();
        services.AddHttpClient<TelegramBotClient>();
        services.AddSingleton<TelegramSettingsService>();
        services.AddSingleton<TelegramMessageFormatter>();
        services.AddSingleton<TelegramActionService>();
        services.AddHostedService<TelegramInvoiceEventService>();
        services.AddHostedService<TelegramUpdateListener>();
    }
}
