using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Contracts;

namespace TicketMonitor.Api.HealthChecks;

/// <summary>Проверка доступности GLPI.</summary>
public sealed class GlpiHealthProbe(
    GlpiClient glpiClient,
    ILogger<GlpiHealthProbe> logger) : IExternalServiceProbe
{
    private readonly GlpiClient _glpiClient = glpiClient ?? throw new ArgumentNullException(nameof(glpiClient));
    private readonly ILogger<GlpiHealthProbe> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public string ServiceName => "GLPI";

    /// <inheritdoc />
    public async Task<ProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var request = _glpiClient.CreateRequest(HttpMethod.Get, "apirest.php/getFullSession");
            using var response = await _glpiClient.SendWithSessionRetryAsync(request, cancellationToken).ConfigureAwait(false);

            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new ProbeResult
                {
                    Healthy = true,
                    ResponseTimeMs = sw.Elapsed.TotalMilliseconds
                };
            }

            return new ProbeResult
            {
                Healthy = false,
                ResponseTimeMs = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = $"HTTP {(int)response.StatusCode}"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "GLPI health probe failed");

            return new ProbeResult
            {
                Healthy = false,
                ResponseTimeMs = sw.Elapsed.TotalMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }
}