using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BTCPayServer.Plugins.TelegramNotifications.Services;

public sealed class TelegramBotClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public async Task<TelegramUser> GetMeAsync(string token, CancellationToken cancellationToken = default) =>
        await PostAsync<TelegramUser>(token, "getMe", new { }, cancellationToken);

    public async Task<int> SendMessageAsync(
        string token,
        long chatId,
        string html,
        TelegramInlineKeyboard? replyMarkup,
        CancellationToken cancellationToken = default)
    {
        var message = await PostAsync<TelegramMessage>(token, "sendMessage", new
        {
            chat_id = chatId,
            text = html,
            parse_mode = "HTML",
            link_preview_options = new { is_disabled = true },
            reply_markup = replyMarkup
        }, cancellationToken);
        return message.MessageId;
    }

    public Task<TelegramUpdate[]> GetUpdatesAsync(
        string token,
        long offset,
        CancellationToken cancellationToken = default) =>
        PostAsync<TelegramUpdate[]>(token, "getUpdates", new
        {
            offset,
            timeout = 25,
            allowed_updates = new[] { "message", "callback_query" }
        }, cancellationToken);

    public async Task AnswerCallbackAsync(
        string token,
        string callbackQueryId,
        string text,
        bool showAlert = false,
        CancellationToken cancellationToken = default) =>
        _ = await PostAsync<bool>(token, "answerCallbackQuery", new
        {
            callback_query_id = callbackQueryId,
            text,
            show_alert = showAlert
        }, cancellationToken);

    public async Task EditReplyMarkupAsync(
        string token,
        long chatId,
        int messageId,
        TelegramInlineKeyboard? replyMarkup,
        CancellationToken cancellationToken = default) =>
        _ = await PostAsync<TelegramMessage>(token, "editMessageReplyMarkup", new
        {
            chat_id = chatId,
            message_id = messageId,
            reply_markup = replyMarkup ?? new TelegramInlineKeyboard([])
        }, cancellationToken);

    private async Task<T> PostAsync<T>(string token, string method, object payload, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.telegram.org/bot{token}/{method}")
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            TelegramApiResponse<T>? apiResponse = null;
            try
            {
                apiResponse = JsonSerializer.Deserialize<TelegramApiResponse<T>>(body, JsonOptions);
            }
            catch (JsonException)
            {
                // The sanitized exception below intentionally excludes the response body and token.
            }

            if (!response.IsSuccessStatusCode || apiResponse is not { Ok: true, Result: not null })
            {
                var description = apiResponse?.Description ?? $"HTTP {(int)response.StatusCode}";
                throw new TelegramApiException(method, description);
            }

            return apiResponse.Result;
        }
        catch (TelegramApiException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TelegramApiException(method, "Telegram request failed", ex);
        }
    }
}

public sealed class TelegramApiException(string method, string description, Exception? innerException = null)
    : Exception($"Telegram {method} failed: {description}", innerException);

public sealed class TelegramApiResponse<T>
{
    public bool Ok { get; set; }
    public T? Result { get; set; }
    public string? Description { get; set; }
}

public sealed class TelegramUpdate
{
    [JsonPropertyName("update_id")]
    public long UpdateId { get; set; }

    [JsonPropertyName("callback_query")]
    public TelegramCallbackQuery? CallbackQuery { get; set; }

    public TelegramMessage? Message { get; set; }
}

public sealed class TelegramCallbackQuery
{
    public string Id { get; set; } = string.Empty;
    public TelegramUser From { get; set; } = new();
    public TelegramMessage? Message { get; set; }
    public string? Data { get; set; }
}

public sealed class TelegramUser
{
    public long Id { get; set; }
    public string? Username { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }
}

public sealed class TelegramMessage
{
    [JsonPropertyName("message_id")]
    public int MessageId { get; set; }
    public TelegramChat Chat { get; set; } = new();
    public TelegramUser? From { get; set; }
    public string? Text { get; set; }
}

public sealed class TelegramChat
{
    public long Id { get; set; }
}

public sealed record TelegramInlineKeyboard(
    [property: JsonPropertyName("inline_keyboard")] TelegramInlineButton[][] InlineKeyboard);

public sealed record TelegramInlineButton(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("callback_data")] string? CallbackData = null,
    [property: JsonPropertyName("url")] string? Url = null);
