using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.TelegramNotifications.Models;
using BTCPayServer.Plugins.TelegramNotifications.Services;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json.Linq;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.TelegramNotifications.Tests;

public class PluginTests
{
    private readonly TelegramMessageFormatter _formatter = new();

    [Fact]
    public void FormatsPaymentWithOrderUsernameAndTransactionDetails()
    {
        var invoice = CreateInvoice();
        var payment = new PaymentEntity
        {
            Id = "c461a4110e5f991412dd30f3eaf39bc07cce7f87863c847a665b0a1c230d53a7",
            Value = 0.00147163m,
            Currency = "BTC",
            Divisibility = 8,
            Destination = "bc1q25f5e335w4uhfe0fyftnpav65sql2k0clr7k92",
            ReceivedTime = new DateTimeOffset(2026, 11, 7, 1, 26, 0, TimeSpan.FromHours(5.5)),
            Status = PaymentStatus.Processing
        };
        invoice.Payments = [payment];
        var evt = new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment };

        var result = _formatter.Format(evt, CreateStore(), CreateSettings()).Html;

        Assert.Contains("BTCPay [Portal]: Payment Received", result);
        Assert.Contains("SrGDiNUUhPYmWB3J4jGkfb", result);
        Assert.Contains("7ee9536e-728d-4dd4-ab94-d6803b005cc8", result);
        Assert.Contains("NEEFAO", result);
        Assert.Contains("0.00147163 BTC", result);
        Assert.Contains(payment.Id, result);
        Assert.Contains(payment.Destination, result);
        Assert.Contains("07 Nov, 01:26 AM", result);
    }

    [Fact]
    public void FormatsUnderpaidInvoiceAmounts()
    {
        var invoice = CreateInvoice();
        invoice.Status = InvoiceStatus.Expired;
        invoice.ExceptionStatus = InvoiceExceptionStatus.PaidPartial;
        invoice.Price = 100m;
        invoice.PaidAmount = new Amounts { Currency = "USD", Net = 94.50m, Gross = 94.50m };
        invoice.NetDue = 5.50m;
        invoice.MinimumNetDue = 5.50m;
        var evt = new InvoiceEvent(invoice, InvoiceEvent.ExpiredPaidPartial);

        var result = _formatter.Format(evt, CreateStore(), CreateSettings()).Html;

        Assert.Contains("Underpaid Invoice", result);
        Assert.Contains("100 USD", result);
        Assert.Contains("94.5 USD", result);
        Assert.Contains("5.5 USD", result);
        Assert.Contains("Expired — Paid Partial", result);
    }

    [Fact]
    public void RedactsSecretLikeMetadata()
    {
        var invoice = CreateInvoice();
        invoice.Metadata = InvoiceMetadata.FromJObject(JObject.Parse("""
        {
          "orderId": "ORDER-1",
          "username": "alice",
          "plan": "Gold",
          "apiToken": "must-not-appear",
          "nested": { "privateKey": "also-secret" }
        }
        """));
        var evt = new InvoiceEvent(invoice, InvoiceEvent.Created);

        var result = _formatter.Format(evt, CreateStore(), CreateSettings()).Html;

        Assert.Contains("Gold", result);
        Assert.DoesNotContain("must-not-appear", result);
        Assert.DoesNotContain("also-secret", result);
    }

    [Theory]
    [InlineData("/start")]
    [InlineData("/status")]
    [InlineData("/START")]
    [InlineData("/start@MyBot")]
    [InlineData("/status details")]
    public void RecognizesBotStatusCommands(string command) =>
        Assert.True(TelegramUpdateListener.IsStatusCommand(command));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("/settings")]
    public void IgnoresOtherBotMessages(string? command) =>
        Assert.False(TelegramUpdateListener.IsStatusCommand(command));

    private static InvoiceEntity CreateInvoice() => new()
    {
        Id = "SrGDiNUUhPYmWB3J4jGkfb",
        StoreId = "store-portal",
        Price = 100m,
        Currency = "USD",
        Status = InvoiceStatus.Processing,
        ExceptionStatus = InvoiceExceptionStatus.None,
        PaidAmount = new Amounts { Currency = "USD", Net = 100m, Gross = 100m },
        Metadata = InvoiceMetadata.FromJObject(JObject.Parse("""
        {
          "orderId": "7ee9536e-728d-4dd4-ab94-d6803b005cc8",
          "username": "NEEFAO"
        }
        """))
    };

    private static StoreData CreateStore() => new() { Id = "store-portal", StoreName = "Portal" };

    private static TelegramNotificationSettings CreateSettings() => new()
    {
        IncludeAllMetadata = true,
        TimeZoneId = "Asia/Kolkata"
    };
}
