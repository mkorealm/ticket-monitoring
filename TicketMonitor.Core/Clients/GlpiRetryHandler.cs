using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Clients;

/// <summary>DelegatingHandler с retry-политикой для GLPI API.</summary>
public sealed class GlpiRetryHandler(
    IOptions<GlpiClientOptions> options,
    ILogger<GlpiRetryHandler> logger) : DelegatingHandler
{
    private readonly GlpiClientOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[]? contentBytes = null;
        if (request.Content is not null)
            contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        for (var attempt = 1; attempt <= _options.MaxRetries; attempt++)
        {
            var attemptRequest = CloneRequest(request, contentBytes);

            try
            {
                var response = await base.SendAsync(attemptRequest, cancellationToken).ConfigureAwait(false);

                if (IsRetryable(response.StatusCode) && attempt < _options.MaxRetries)
                {
                    logger.LogWarning("GLPI вернул {StatusCode} (попытка {Attempt}/{MaxRetries}), повтор через {Delay}с",
                        response.StatusCode, attempt, _options.MaxRetries, _options.RetryDelaySeconds);

                    response.Dispose();
                    await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex) when (attempt < _options.MaxRetries)
            {
                logger.LogWarning(ex, "Сетевая ошибка GLPI (попытка {Attempt}/{MaxRetries}), повтор через {Delay}с: {Message}",
                    attempt, _options.MaxRetries, _options.RetryDelaySeconds, ex.Message);
                await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < _options.MaxRetries)
            {
                logger.LogWarning(ex, "Таймаут GLPI (попытка {Attempt}/{MaxRetries}), повтор через {Delay}с",
                    attempt, _options.MaxRetries, _options.RetryDelaySeconds);
                await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Запрос к GLPI отменён пользователем");
                throw;
            }
        }

        var finalRequest = CloneRequest(request, contentBytes);
        return await base.SendAsync(finalRequest, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsRetryable(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.RequestTimeout => true,
        HttpStatusCode.TooManyRequests => true,
        HttpStatusCode.InternalServerError => true,
        HttpStatusCode.BadGateway => true,
        HttpStatusCode.ServiceUnavailable => true,
        HttpStatusCode.GatewayTimeout => true,
        _ => false
    };

    private Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(_options.RetryDelaySeconds * Math.Pow(2, attempt - 1));
        return Task.Delay(delay, cancellationToken);
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
}