using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Clients;

/// <summary>HTTP-клиент Rocket.Chat через именованный <see cref="HttpClient"/>.</summary>
public sealed class RocketChatClient(
    IHttpClientFactory httpClientFactory,
    IOptions<RocketChatOptions> options,
    IOperationRegistry operationRegistry,
    ILogger<RocketChatClient> logger) : IRocketChatClient
{
    public const string HttpClientName = "rocketchat";
    private const string SendMessagePath = "api/v1/chat.sendMessage";
    private const string UpdateMessagePath = "api/v1/chat.update";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly RocketChatOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly IOperationRegistry _operationRegistry = operationRegistry ?? throw new ArgumentNullException(nameof(operationRegistry));
    private readonly ILogger<RocketChatClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<string?> SendMessageAsync(
        string roomId,
        string text,
        string? threadMessageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (string.IsNullOrEmpty(_options.ServerUrl))
        {
            _logger.LogWarning("RocketChat:ServerUrl не настроен — сообщение не отправлено");
            return null;
        }

        return await _operationRegistry.RunAsync(
            "RocketChat:SendMessage",
            async ct =>
            {
                try
                {
                    var payload = BuildPayload(roomId, text, threadMessageId);
                    var jsonPayload = JsonSerializer.Serialize(payload);

                    using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    using var request = new HttpRequestMessage(HttpMethod.Post, SendMessagePath) { Content = content };

                    var client = _httpClientFactory.CreateClient(HttpClientName);
                    using var response = await client.SendAsync(request, ct).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        _logger.LogError("Rocket.Chat вернул {StatusCode}. Ответ: {Body}", response.StatusCode, body);
                        return null;
                    }

                    var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(responseJson))
                    {
                        _logger.LogWarning("Rocket.Chat вернул {StatusCode} с пустым телом", response.StatusCode);
                        return null;
                    }

                    return ExtractMessageId(responseJson, response.StatusCode);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _logger.LogInformation("Отправка в Rocket.Chat отменена пользователем");
                    throw;
                }
                catch (TaskCanceledException ex)
                {
                    _logger.LogError(ex, "Таймаут при отправке в Rocket.Chat");
                    return null;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "Сетевая ошибка при отправке в Rocket.Chat");
                    return null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Неожиданная ошибка при отправке в Rocket.Chat");
                    return null;
                }
            },
            context: new { roomId, hasThread = !string.IsNullOrEmpty(threadMessageId) },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateMessageAsync(
        string roomId,
        string messageId,
        string newText,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newText);

        if (string.IsNullOrEmpty(_options.ServerUrl))
        {
            _logger.LogWarning("RocketChat:ServerUrl не настроен — сообщение не отредактировано");
            return false;
        }

        return await _operationRegistry.RunAsync(
            "RocketChat:UpdateMessage",
            async ct =>
            {
                try
                {
                    var payload = new
                    {
                        roomId,
                        msgId = messageId,
                        text = newText
                    };

                    var jsonPayload = JsonSerializer.Serialize(payload);

                    using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    using var request = new HttpRequestMessage(HttpMethod.Post, UpdateMessagePath) { Content = content };

                    var client = _httpClientFactory.CreateClient(HttpClientName);
                    using var response = await client.SendAsync(request, ct).ConfigureAwait(false);

                    var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError("Rocket.Chat вернул {StatusCode} при редактировании сообщения. Ответ: {Body}",
                            response.StatusCode, responseJson);
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(responseJson))
                    {
                        _logger.LogWarning("Rocket.Chat вернул {StatusCode} с пустым телом при редактировании сообщения", response.StatusCode);
                        return false;
                    }

                    return CheckSuccess(responseJson, response.StatusCode);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _logger.LogInformation("Редактирование сообщения в Rocket.Chat отменено пользователем");
                    throw;
                }
                catch (TaskCanceledException ex)
                {
                    _logger.LogError(ex, "Таймаут при редактировании сообщения в Rocket.Chat");
                    return false;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "Сетевая ошибка при редактировании сообщения в Rocket.Chat");
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Неожиданная ошибка при редактировании сообщения в Rocket.Chat");
                    return false;
                }
            },
            context: new { roomId, messageId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static object BuildPayload(string roomId, string text, string? threadMessageId)
    {
        var message = new Dictionary<string, object>
        {
            ["rid"] = roomId,
            ["msg"] = text
        };

        if (!string.IsNullOrEmpty(threadMessageId))
            message["tmid"] = threadMessageId;

        return new { message };
    }

    private string? ExtractMessageId(string responseJson, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);

            if (doc.RootElement.TryGetProperty("message", out var messageElement)
                && messageElement.TryGetProperty("_id", out var messageIdElement))
                return messageIdElement.GetString();

            _logger.LogWarning("Rocket.Chat вернул {StatusCode}, но нет message._id. Тело: {Body}", statusCode, responseJson);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Rocket.Chat вернул {StatusCode} с не-JSON телом: {Body}", statusCode, responseJson);
            return null;
        }
    }

    private bool CheckSuccess(string responseJson, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);

            if (doc.RootElement.TryGetProperty("success", out var successElement)
                && successElement.ValueKind == JsonValueKind.True)
                return true;

            _logger.LogWarning("Rocket.Chat вернул {StatusCode}, но success != true. Тело: {Body}", statusCode, responseJson);
            return false;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Rocket.Chat вернул {StatusCode} с не-JSON телом: {Body}", statusCode, responseJson);
            return false;
        }
    }
}