using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Clients;

/// <summary>Клиент для взаимодействия с GLPI REST API.</summary>
public class GlpiClient(
    IHttpClientFactory httpClientFactory,
    IOptions<GlpiClientOptions> options,
    IOperationRegistry operationRegistry,
    ILogger<GlpiClient> logger)
{
    public const string HttpClientName = "glpi";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly GlpiClientOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly IOperationRegistry _operationRegistry = operationRegistry ?? throw new ArgumentNullException(nameof(operationRegistry));
    private readonly ILogger<GlpiClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private string? _sessionToken;
    private string? _userToken;
    private int _sessionGeneration;
    private readonly SemaphoreSlim _sessionRefreshGate = new(1, 1);

    /// <summary>Инициализирует сессию GLPI API.</summary>
    public async Task<bool> InitSession(string userToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userToken);

        return await _operationRegistry.RunAsync(
            "GLPI:InitSession",
            async ct =>
            {
                _userToken = userToken;

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, "apirest.php/initSession");
                    request.Headers.Add("Authorization", $"user_token {userToken}");

                    using var response = await SendAsync(request, ct).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        _logger.LogError("Ошибка авторизации GLPI: {StatusCode} — {Error}", response.StatusCode, error);
                        return false;
                    }

                    var jsonResponse = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(jsonResponse);

                    if (!doc.RootElement.TryGetProperty("session_token", out var tokenElement))
                    {
                        _logger.LogError("В ответе GLPI отсутствует session_token");
                        return false;
                    }

                    var newToken = tokenElement.GetString();
                    if (string.IsNullOrEmpty(newToken))
                    {
                        _logger.LogError("GLPI вернул пустой session_token");
                        return false;
                    }

                    _sessionToken = newToken;
                    Interlocked.Increment(ref _sessionGeneration);

                    _logger.LogInformation("Сессия GLPI инициализирована");
                    return true;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Инициализация сессии GLPI отменена");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка инициализации сессии GLPI");
                    return false;
                }
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Завершает текущую сессию GLPI.</summary>
    public async Task KillSession(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_sessionToken))
            return;

        await _operationRegistry.RunAsync(
            "GLPI:KillSession",
            async ct =>
            {
                try
                {
                    using var request = CreateRequest(HttpMethod.Post, "apirest.php/killSession");
                    using var response = await SendAsync(request, ct).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Сессия GLPI завершена");
                        _sessionToken = null;
                    }
                    else
                    {
                        _logger.LogWarning("Не удалось корректно завершить сессию GLPI: {StatusCode}", response.StatusCode);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка завершения сессии GLPI");
                }
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Проверяет, что сессия инициализирована.</summary>
    public void EnsureSessionExists()
    {
        if (string.IsNullOrEmpty(_sessionToken))
            throw new InvalidOperationException("Сессия не инициализирована. Сначала вызовите InitSession().");
    }

    /// <summary>Возвращает текущий session-token или пустую строку.</summary>
    public string GetSessionToken() => _sessionToken ?? string.Empty;

    /// <summary>Отправляет HTTP-запрос в GLPI.</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = _httpClientFactory.CreateClient(HttpClientName);
        return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Отправляет запрос к GLPI с автоматической переинициализацией сессии при 401.
    /// </summary>
    public virtual async Task<HttpResponseMessage> SendWithSessionRetryAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        byte[]? contentBytes = null;
        if (request.Content is not null)
            contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.Dispose();

        _logger.LogWarning(
            "GLPI вернул 401 Unauthorized — переинициализация сессии. Ответ: {Body}",
            body);

        if (string.IsNullOrEmpty(_userToken))
        {
            _logger.LogError("Невозможно переинициализировать сессию: UserToken не сохранён (InitSession не вызывался)");
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
        }

        var generationAtStart = Volatile.Read(ref _sessionGeneration);

        await _sessionRefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _sessionGeneration) != generationAtStart)
            {
                _logger.LogInformation("GLPI-сессия уже переинициализирована другим потоком — повторяю запрос");
            }
            else if (!await InitSession(_userToken, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogError("Не удалось переинициализировать GLPI-сессию");
                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                    RequestMessage = request
                };
            }
            else
            {
                _logger.LogInformation("GLPI-сессия успешно переинициализирована");
            }
        }
        finally
        {
            _sessionRefreshGate.Release();
        }

        using var retryRequest = CloneRequest(request, contentBytes);
        return await SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage original, byte[]? contentBytes)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy
        };

        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (contentBytes is not null)
        {
            clone.Content = new ByteArrayContent(contentBytes);

            if (original.Content is not null)
            {
                foreach (var header in original.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    /// <summary>Создаёт HTTP-запрос с автоматической подстановкой <c>Session-Token</c>.</summary>
    public virtual HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        EnsureSessionExists();
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Session-Token", _sessionToken);
        return request;
    }

    /// <summary>Разбирает HTTP-ответ GLPI и возвращает список словарей из поля <c>data</c>.</summary>
    public async Task<List<Dictionary<string, object?>>> ParseResponse(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.PartialContent)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("GLPI API вернул ошибку: {StatusCode} — {Error}", response.StatusCode, error);
            throw new InvalidOperationException($"Ошибка GLPI API: {response.StatusCode} — {error}");
        }

        var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(jsonResponse);

        var result = new List<Dictionary<string, object?>>();

        if (doc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in dataArray.EnumerateArray())
                result.Add(ConvertJsonObject(element));
        }

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("ParseResponse: разобрано {Count} записей", result.Count);

        return result;
    }

    /// <summary>Возвращает полную информацию о заявке.</summary>
    public async Task<Dictionary<string, object?>> GetTicketAsync(string ticketId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);
        EnsureSessionExists();

        return await _operationRegistry.RunAsync(
            "GLPI:FetchTicket",
            async ct =>
            {
                using var request = CreateRequest(HttpMethod.Get, $"apirest.php/Ticket/{ticketId}?expand_dropdowns=true");
                using var response = await SendWithSessionRetryAsync(request, ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    throw new InvalidOperationException($"Ошибка GLPI API: {response.StatusCode} — {error}");
                }

                var jsonResponse = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(jsonResponse);
                return ConvertJsonObject(doc.RootElement);
            },
            context: new { ticketId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public Task<List<Dictionary<string, object?>>> GetTicketFollowupsAsync(string ticketId, CancellationToken cancellationToken = default)
        => GetTicketSubItemsAsync(ticketId, "ITILFollowup", cancellationToken);

    public Task<List<Dictionary<string, object?>>> GetTicketValidationsAsync(string ticketId, CancellationToken cancellationToken = default)
        => GetTicketSubItemsAsync(ticketId, "TicketValidation", cancellationToken);

    public Task<List<Dictionary<string, object?>>> GetTicketSolutionsAsync(string ticketId, CancellationToken cancellationToken = default)
        => GetTicketSubItemsAsync(ticketId, "ITILSolution", cancellationToken);

    public Task<List<Dictionary<string, object?>>> GetTicketTasksAsync(string ticketId, CancellationToken cancellationToken = default)
        => GetTicketSubItemsAsync(ticketId, "TicketTask", cancellationToken);

    private async Task<List<Dictionary<string, object?>>> GetTicketSubItemsAsync(string ticketId, string subItemType, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subItemType);
        EnsureSessionExists();

        return await _operationRegistry.RunAsync(
            $"GLPI:Fetch{subItemType}",
            async ct =>
            {
                using var request = CreateRequest(HttpMethod.Get, $"apirest.php/Ticket/{ticketId}/{subItemType}");
                using var response = await SendWithSessionRetryAsync(request, ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    throw new InvalidOperationException($"Ошибка GLPI API: {response.StatusCode} — {error}");
                }

                var jsonResponse = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(jsonResponse);

                var result = new List<Dictionary<string, object?>>();

                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in doc.RootElement.EnumerateArray())
                        result.Add(ConvertJsonObject(element));
                }
                else if (doc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in dataArray.EnumerateArray())
                        result.Add(ConvertJsonObject(element));
                }

                return result;
            },
            context: new { ticketId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<Dictionary<string, object?>>> GetAllTicketsCompactAsync(string categoryFilter, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryFilter);
        EnsureSessionExists();

        return await _operationRegistry.RunAsync(
            "GLPI:FetchTickets",
            async ct =>
            {
                var allTickets = new List<Dictionary<string, object?>>();
                var start = 0;
                const int batchSize = 100;

                while (true)
                {
                    var url = BuildCompactSearchUrl(categoryFilter, start, batchSize);
                    using var request = CreateRequest(HttpMethod.Get, url);
                    using var response = await SendWithSessionRetryAsync(request, ct).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        throw new InvalidOperationException($"Ошибка GLPI API: {response.StatusCode} — {error}");
                    }

                    var jsonResponse = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(jsonResponse);

                    var batch = new List<Dictionary<string, object?>>();
                    if (doc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in dataArray.EnumerateArray())
                            batch.Add(ConvertJsonObject(element));
                    }

                    allTickets.AddRange(batch);

                    if (batch.Count < batchSize) break;
                    start += batchSize;
                }

                return allTickets;
            },
            context: new { categoryFilter },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<Dictionary<string, object?>>> GetAllTicketsFullAsync(string categoryFilter, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryFilter);
        EnsureSessionExists();

        return await _operationRegistry.RunAsync(
            "GLPI:FetchTicketsFull",
            async ct =>
            {
                var allTickets = new List<Dictionary<string, object?>>();
                var start = 0;
                const int batchSize = 100;

                while (true)
                {
                    var url = BuildFullSearchUrl(categoryFilter, start, batchSize);
                    using var request = CreateRequest(HttpMethod.Get, url);
                    using var response = await SendWithSessionRetryAsync(request, ct).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        throw new InvalidOperationException($"Ошибка GLPI API: {response.StatusCode} — {error}");
                    }

                    var jsonResponse = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(jsonResponse);

                    var batch = new List<Dictionary<string, object?>>();
                    if (doc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in dataArray.EnumerateArray())
                            batch.Add(ConvertJsonObject(element));
                    }

                    allTickets.AddRange(batch);

                    if (batch.Count < batchSize) break;
                    start += batchSize;
                }

                return allTickets;
            },
            context: new { categoryFilter },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string BuildCompactSearchUrl(string categoryFilter, int start, int batchSize)
    {
        return $"apirest.php/search/Ticket?range={start}-{start + batchSize - 1}" +
               $"&order=DESC&sortBy={GlpiFields.LastModified}&expand_dropdowns=true" +
               $"&forcedisplay[0]={GlpiFields.Id}" +
               $"&forcedisplay[1]={GlpiFields.Title}" +
               $"&forcedisplay[2]={GlpiFields.Status}" +
               $"&forcedisplay[3]={GlpiFields.OpenDate}" +
               $"&forcedisplay[4]={GlpiFields.Requester}" +
               $"&forcedisplay[5]={GlpiFields.Assignee}" +
               $"&forcedisplay[6]={GlpiFields.Category}" +
               $"&forcedisplay[7]={GlpiFields.Priority}" +
               $"&forcedisplay[8]={GlpiFields.LastModified}" +
               BuildCategoryCriteria(categoryFilter);
    }

    private static string BuildFullSearchUrl(string categoryFilter, int start, int batchSize)
    {
        return $"apirest.php/search/Ticket?range={start}-{start + batchSize - 1}" +
               $"&order=DESC&sortBy={GlpiFields.LastModified}&expand_dropdowns=true" +
               BuildCategoryCriteria(categoryFilter);
    }

    private static string BuildCategoryCriteria(string categoryFilter)
    {
        return $"&criteria[0][link]=AND" +
               $"&criteria[0][field]={GlpiFields.Category}" +
               $"&criteria[0][searchtype]=contains" +
               $"&criteria[0][value]={categoryFilter}";
    }

    private static Dictionary<string, object?> ConvertJsonObject(JsonElement element)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in element.EnumerateObject())
            dict[prop.Name] = ConvertJsonValue(prop.Value);
        return dict;
    }

    private static object? ConvertJsonValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt32(out var intVal) ? intVal : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => ConvertJsonObject(element),
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToList(),
        _ => element.GetRawText()
    };
}