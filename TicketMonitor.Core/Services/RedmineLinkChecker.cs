using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Contracts;

namespace TicketMonitor.Core.Services;

/// <summary>
/// HTTP-клиент проверки наличия связи заявки GLPI с задачей Redmine.
/// Обращается к сервису <c>TicketMonitor.Redmine</c> (порт 8082).
/// Результат кэшируется на <see cref="CacheTtl"/>.
/// </summary>
public sealed class RedmineLinkChecker(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IOptions<RedmineLinkCheckerOptions> options,
    ILogger<RedmineLinkChecker> logger) : IRedmineLinkChecker
{
    /// <summary>Имя именованного HttpClient.</summary>
    public const string HttpClientName = "redmine-link-checker";

    /// <summary>Время жизни кэша (2 минуты).</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly IMemoryCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly RedmineLinkCheckerOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<RedmineLinkChecker> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<bool> IsLinkedAsync(string glpiTicketId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(glpiTicketId);

        var cacheKey = $"redmine-link-{glpiTicketId}";
        if (_cache.TryGetValue<bool>(cacheKey, out var cached))
            return cached;

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("RedmineLinkChecker:BaseUrl не задан — считаю, что связи нет");
            return false;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var url = $"api/redmine/links/{Uri.EscapeDataString(glpiTicketId)}/exists";

            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Redmine-сервис вернул {StatusCode} при проверке связи для заявки #{TicketId}",
                    response.StatusCode, glpiTicketId);
                return false;
            }

            var payload = await response.Content
                .ReadFromJsonAsync<RedmineLinkStatusResponse>(cancellationToken)
                .ConfigureAwait(false);

            var linked = payload?.Linked ?? false;
            _cache.Set(cacheKey, linked, CacheTtl);

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Заявка #{TicketId}: linked={Linked}", glpiTicketId, linked);

            return linked;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Не удалось проверить связь с Redmine для заявки #{TicketId} — считаю, что связи нет",
                glpiTicketId);
            return false;
        }
    }

    /// <summary>Ответ Redmine-сервиса.</summary>
    private sealed record RedmineLinkStatusResponse
    {
        public bool Linked { get; init; }
    }
}