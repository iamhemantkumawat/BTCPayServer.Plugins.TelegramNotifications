using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.TelegramNotifications.Data;
using BTCPayServer.Plugins.TelegramNotifications.Models;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.TelegramNotifications.Services;

public sealed class TelegramInvoiceEventService(
    EventAggregator eventAggregator,
    TelegramSettingsService settingsService,
    StoreRepository storeRepository,
    TelegramMessageFormatter formatter,
    TelegramActionService actionService,
    TelegramBotClient botClient,
    IDbContextFactory<TelegramNotificationsDbContext> dbContextFactory,
    ILogger<TelegramInvoiceEventService> logger)
    : EventHostedServiceBase(eventAggregator, logger)
{
    protected override void SubscribeToEvents() => Subscribe<InvoiceEvent>();

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is not InvoiceEvent invoiceEvent || !ShouldHandle(invoiceEvent.EventCode))
            return;

        var runtime = await settingsService.GetRuntimeAsync();
        if (runtime is null || !runtime.IsStoreEnabled(invoiceEvent.Invoice.StoreId) ||
            !IsEnabledForEvent(runtime.Settings, invoiceEvent.EventCode))
            return;

        var store = await storeRepository.FindStore(invoiceEvent.Invoice.StoreId);
        if (store is null)
            return;

        var formatted = formatter.Format(invoiceEvent, store, runtime.Settings);
        foreach (var chatId in runtime.Settings.ChatIds)
        {
            var eventKey = BuildEventKey(invoiceEvent, chatId);
            if (!await TryReserveEventAsync(eventKey, cancellationToken))
                continue;

            try
            {
                var prepared = await actionService.CreateKeyboardAsync(runtime, invoiceEvent.Invoice, chatId,
                    formatted.TransactionUrl, cancellationToken);
                var messageId = await botClient.SendMessageAsync(runtime.BotToken, chatId, formatted.Html,
                    prepared.Keyboard, cancellationToken);
                await actionService.SetMessageIdAsync(prepared.ActionTokens, messageId, cancellationToken);
            }
            catch (Exception ex)
            {
                await ReleaseEventAsync(eventKey, cancellationToken);
                logger.LogWarning(ex,
                    "Telegram Notifications failed to send invoice {InvoiceId} event {EventCode} to chat {ChatId}",
                    invoiceEvent.InvoiceId, invoiceEvent.EventCode, chatId);
            }
        }
    }

    private static bool ShouldHandle(InvoiceEventCode code) => code is
        InvoiceEventCode.Created or
        InvoiceEventCode.ReceivedPayment or
        InvoiceEventCode.PaymentSettled or
        InvoiceEventCode.PaidInFull or
        InvoiceEventCode.Expired or
        InvoiceEventCode.Confirmed or
        InvoiceEventCode.MarkedInvalid or
        InvoiceEventCode.FailedToConfirm or
        InvoiceEventCode.PaidAfterExpiration or
        InvoiceEventCode.ExpiredPaidPartial or
        InvoiceEventCode.MarkedCompleted or
        InvoiceEventCode.Refund;

    private static bool IsEnabledForEvent(TelegramNotificationSettings settings, InvoiceEventCode code) => code switch
    {
        InvoiceEventCode.Created => settings.NotifyInvoiceCreated,
        InvoiceEventCode.ReceivedPayment => settings.NotifyPaymentReceived,
        InvoiceEventCode.PaymentSettled => settings.NotifyPaymentSettled,
        InvoiceEventCode.PaidInFull => settings.NotifyProcessing,
        InvoiceEventCode.Expired or InvoiceEventCode.ExpiredPaidPartial or InvoiceEventCode.PaidAfterExpiration => settings.NotifyExpired,
        InvoiceEventCode.Confirmed or InvoiceEventCode.MarkedCompleted => settings.NotifySettled,
        InvoiceEventCode.MarkedInvalid or InvoiceEventCode.FailedToConfirm => settings.NotifyInvalid,
        InvoiceEventCode.Refund => settings.NotifyRefunds,
        _ => false
    };

    private static string BuildEventKey(InvoiceEvent evt, long chatId) =>
        $"{chatId}:{evt.InvoiceId}:{(int)evt.EventCode}:{evt.Payment?.Id ?? "-"}:{evt.Invoice.Status}:{evt.Invoice.ExceptionStatus}";

    private async Task<bool> TryReserveEventAsync(string eventKey, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.NotificationEvents.Add(new NotificationEventRecord
        {
            EventKey = eventKey,
            CreatedAt = DateTimeOffset.UtcNow
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private async Task ReleaseEventAsync(string eventKey, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.NotificationEvents.Where(e => e.EventKey == eventKey).ExecuteDeleteAsync(cancellationToken);
    }
}
