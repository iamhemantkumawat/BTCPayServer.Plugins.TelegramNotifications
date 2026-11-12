using System.Globalization;
using System.Net;
using System.Text;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Plugins.TelegramNotifications.Models;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json.Linq;
using InvoiceExceptionStatus = BTCPayServer.Client.Models.InvoiceExceptionStatus;
using InvoiceStatus = BTCPayServer.Client.Models.InvoiceStatus;

namespace BTCPayServer.Plugins.TelegramNotifications.Services;

public sealed class TelegramMessageFormatter
{
    private static readonly string[] SensitiveFragments =
    [
        "password", "passwd", "secret", "token", "authorization", "cookie", "credential",
        "privatekey", "private_key", "seed", "mnemonic", "apikey", "api_key"
    ];

    private static readonly HashSet<string> StandardMetadataKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "orderId", "buyerName", "buyerEmail", "buyerPhone", "buyerAddress1", "buyerAddress2",
        "buyerCity", "buyerState", "buyerZip", "buyerCountry", "orderUrl"
    };

    public FormattedTelegramNotification Format(
        InvoiceEvent invoiceEvent,
        StoreData store,
        TelegramNotificationSettings settings)
    {
        var invoice = invoiceEvent.Invoice;
        var payment = invoiceEvent.Payment ?? invoice.GetPayments(false)
            .OrderByDescending(p => p.ReceivedTime)
            .FirstOrDefault();
        var metadata = invoice.Metadata ?? new InvoiceMetadata();
        var title = GetTitle(invoiceEvent, store.StoreName);
        var status = GetStatus(invoiceEvent);
        var builder = new StringBuilder();

        builder.AppendLine($"{title}");
        builder.AppendLine();
        AddLine(builder, "📄", "Invoice ID", invoice.Id, code: true);
        AddLine(builder, "🏪", "Store", store.StoreName);
        AddLine(builder, "📦", "Order ID", metadata.OrderId);
        AddLine(builder, "👤", "Username", FindUsername(metadata));

        if (settings.IncludeBuyerEmail)
            AddLine(builder, "✉️", "Buyer Email", metadata.BuyerEmail);
        if (settings.IncludeBuyerPhone)
            AddLine(builder, "☎️", "Buyer Phone", metadata.BuyerPhone);
        if (settings.IncludeBuyerAddress)
        {
            var address = string.Join(", ", new[]
                {
                    metadata.BuyerAddress1, metadata.BuyerAddress2, metadata.BuyerCity,
                    metadata.BuyerState, metadata.BuyerZip, metadata.BuyerCountry
                }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            AddLine(builder, "📍", "Buyer Address", address);
        }

        builder.AppendLine();
        AddLine(builder, "🧾", "Requested", FormatAmount(invoice.Price, invoice.Currency));

        if (payment is not null)
        {
            AddLine(builder, "💸", invoiceEvent.EventCode == InvoiceEventCode.ReceivedPayment ? "Paid" : "Latest Payment",
                FormatAmount(payment.Value, payment.Currency, payment.Divisibility));
        }

        var received = invoice.PaidAmount?.Net ?? 0m;
        if (received != 0m || invoice.GetPayments(false).Count != 0)
            AddLine(builder, "💰", "Total Received", FormatAmount(received, invoice.Currency));

        if (invoice.IsUnderPaid || invoice.ExceptionStatus == InvoiceExceptionStatus.PaidPartial)
            AddLine(builder, "📉", "Remaining", FormatAmount(Math.Max(invoice.NetDue, 0m), invoice.Currency));
        if (invoice.IsOverPaid || invoice.ExceptionStatus == InvoiceExceptionStatus.PaidOver)
            AddLine(builder, "📈", "Overpaid", FormatAmount(Math.Max(-invoice.NetDue, 0m), invoice.Currency));

        AddLine(builder, "📌", "Status", status);

        if (payment is not null)
        {
            AddLine(builder, "🔗", "TxID", payment.Id, code: true);
            AddLine(builder, "📬", "To Address", payment.Destination, code: true);
        }

        var customMetadata = GetCustomMetadata(metadata, settings).Take(20).ToArray();
        if (customMetadata.Length != 0)
        {
            builder.AppendLine();
            builder.AppendLine("<b>Metadata</b>");
            foreach (var (key, value) in customMetadata)
                AddLine(builder, "•", key, value);
        }

        builder.AppendLine();
        var eventTime = payment?.ReceivedTime ?? DateTimeOffset.UtcNow;
        AddLine(builder, "⏰", "Time", FormatTime(eventTime, settings.TimeZoneId));

        var html = builder.ToString().Trim();
        if (html.Length > 3900)
            html = html[..3890] + "\n…";

        return new FormattedTelegramNotification(html, GetTransactionUrl(payment));
    }

    private static string GetTitle(InvoiceEvent evt, string storeName)
    {
        var (icon, text) = evt.EventCode switch
        {
            InvoiceEventCode.Created => ("🆕", "Invoice Created"),
            InvoiceEventCode.ReceivedPayment when evt.Invoice.Status is InvoiceStatus.Expired or InvoiceStatus.Invalid =>
                ("⚠️", "Payment Received After Expiry"),
            InvoiceEventCode.ReceivedPayment => ("⏳", "Payment Received"),
            InvoiceEventCode.PaymentSettled => ("✅", "Payment Confirmed"),
            InvoiceEventCode.PaidInFull when evt.Invoice.ExceptionStatus == InvoiceExceptionStatus.PaidOver =>
                ("📈", "Invoice Overpaid"),
            InvoiceEventCode.PaidInFull => ("⏳", "Paid in Full — Awaiting Confirmation"),
            InvoiceEventCode.ExpiredPaidPartial => ("⚠️", "Underpaid Invoice"),
            InvoiceEventCode.Expired when evt.PaidPartial => ("⚠️", "Expired — Paid Partial"),
            InvoiceEventCode.Expired => ("⌛", "Invoice Expired"),
            InvoiceEventCode.PaidAfterExpiration => ("⚠️", "Paid After Expiry"),
            InvoiceEventCode.Confirmed => ("✅", "Invoice Settled"),
            InvoiceEventCode.MarkedCompleted => ("✅", "Manually Marked Settled"),
            InvoiceEventCode.MarkedInvalid => ("⛔", "Manually Marked Invalid"),
            InvoiceEventCode.FailedToConfirm => ("❌", "Invoice Invalid"),
            InvoiceEventCode.Refund => ("↩️", "Refund Created"),
            _ => ("ℹ️", evt.Name)
        };
        return $"{icon} <b>BTCPay [{Escape(storeName)}]: {Escape(text)}</b> {icon}";
    }

    private static string GetStatus(InvoiceEvent evt)
    {
        var state = evt.Invoice.GetInvoiceState().ToString();
        return evt.EventCode switch
        {
            InvoiceEventCode.ReceivedPayment when evt.Invoice.Status is InvoiceStatus.Expired or InvoiceStatus.Invalid =>
                $"{state} — paid after expiration",
            InvoiceEventCode.ReceivedPayment => $"{state} — awaiting confirmation",
            InvoiceEventCode.PaidInFull => $"{state} — awaiting confirmation",
            InvoiceEventCode.ExpiredPaidPartial => "Expired — Paid Partial",
            InvoiceEventCode.PaidAfterExpiration => "Paid Late",
            _ => state
        };
    }

    private static string? FindUsername(InvoiceMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.BuyerName))
            return metadata.BuyerName;

        var source = metadata.ToJObject();
        foreach (var key in new[] { "username", "userName", "customerUsername", "customer", "login" })
        {
            var token = FindProperty(source, key);
            if (token?.Type is JTokenType.String or JTokenType.Integer)
                return token.ToString();
        }
        return null;
    }

    private static JToken? FindProperty(JToken token, string key)
    {
        if (token is JObject obj)
        {
            var direct = obj.Properties().FirstOrDefault(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (direct is not null)
                return direct.Value;
            foreach (var child in obj.Properties().Select(p => p.Value))
            {
                var found = FindProperty(child, key);
                if (found is not null)
                    return found;
            }
        }
        else if (token is JArray array)
        {
            foreach (var child in array)
            {
                var found = FindProperty(child, key);
                if (found is not null)
                    return found;
            }
        }
        return null;
    }

    private static IEnumerable<(string Key, string Value)> GetCustomMetadata(
        InvoiceMetadata metadata,
        TelegramNotificationSettings settings)
    {
        if (!settings.IncludeAllMetadata && settings.MetadataKeys.Count == 0)
            yield break;

        foreach (var (path, value) in Flatten(metadata.ToJObject()))
        {
            var leaf = path.Split('.').Last();
            if (StandardMetadataKeys.Contains(leaf) || IsSensitive(path))
                continue;
            if (!settings.IncludeAllMetadata &&
                !settings.MetadataKeys.Any(key => key.Equals(path, StringComparison.OrdinalIgnoreCase) ||
                                                  key.Equals(leaf, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (string.IsNullOrWhiteSpace(value))
                continue;
            yield return (path, value.Length > 300 ? value[..300] + "…" : value);
        }
    }

    private static IEnumerable<(string Path, string Value)> Flatten(JToken token, string prefix = "")
    {
        switch (token)
        {
            case JObject obj:
                foreach (var property in obj.Properties())
                {
                    var path = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                    foreach (var item in Flatten(property.Value, path))
                        yield return item;
                }
                break;
            case JArray array:
                yield return (prefix, array.ToString(Newtonsoft.Json.Formatting.None));
                break;
            case { Type: not JTokenType.Null and not JTokenType.Undefined }:
                yield return (prefix, token.ToString(Newtonsoft.Json.Formatting.None));
                break;
        }
    }

    private static bool IsSensitive(string key) =>
        SensitiveFragments.Any(fragment => key.Replace("-", string.Empty).Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static string? GetTransactionUrl(PaymentEntity? payment)
    {
        if (payment is null || string.IsNullOrWhiteSpace(payment.Id))
            return null;
        if (payment.Currency.Equals("BTC", StringComparison.OrdinalIgnoreCase))
            return $"https://mempool.space/tx/{Uri.EscapeDataString(payment.Id)}";
        if (payment.Currency.Equals("USDT", StringComparison.OrdinalIgnoreCase) &&
            payment.PaymentMethodId.ToString().Contains("TRON", StringComparison.OrdinalIgnoreCase))
            return $"https://tronscan.org/#/transaction/{Uri.EscapeDataString(payment.Id)}";
        return null;
    }

    private static string FormatAmount(decimal value, string? currency, int? divisibility = null)
    {
        var precision = divisibility ?? (currency?.Equals("BTC", StringComparison.OrdinalIgnoreCase) == true ? 8 : 2);
        precision = Math.Clamp(precision, 0, 18);
        var pattern = precision == 0 ? "0" : "0." + new string('#', precision);
        return $"{value.ToString(pattern, CultureInfo.InvariantCulture)} {currency}".Trim();
    }

    private static string FormatTime(DateTimeOffset time, string timeZoneId)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return TimeZoneInfo.ConvertTime(time, zone).ToString("dd MMM, hh:mm tt", CultureInfo.InvariantCulture);
        }
        catch
        {
            return time.UtcDateTime.ToString("dd MMM, hh:mm tt 'UTC'", CultureInfo.InvariantCulture);
        }
    }

    private static void AddLine(StringBuilder builder, string icon, string label, string? value, bool code = false)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        var encoded = Escape(value);
        builder.Append(icon).Append(' ').Append("<b>").Append(Escape(label)).Append(":</b> ");
        builder.Append(code ? $"<code>{encoded}</code>" : encoded).AppendLine();
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}

public sealed record FormattedTelegramNotification(string Html, string? TransactionUrl);
