using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TicketMonitor.Core.Contracts;

namespace TicketMonitor.Api.HealthChecks;

/// <summary>Агрегированная health-проверка всех внешних сервисов.</summary>
public sealed class ExternalServicesHealthCheck(
    IEnumerable<IExternalServiceProbe> probes,
    ILogger<ExternalServicesHealthCheck> logger) : IHealthCheck
{
    private readonly IReadOnlyList<IExternalServiceProbe> _probes = [.. probes];
    private readonly ILogger<ExternalServicesHealthCheck> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, object>();
        var allHealthy = true;

        foreach (var probe in _probes)
        {
            ProbeResult result;
            try
            {
                result = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Health probe {Service} threw exception", probe.ServiceName);
                result = new ProbeResult { Healthy = false, ErrorMessage = ex.Message };
            }

            results[probe.ServiceName] = new
            {
                result.Healthy,
                result.ResponseTimeMs,
                result.ErrorMessage
            };

            if (!result.Healthy)
                allHealthy = false;
        }

        var data = new Dictionary<string, object>
        {
            ["probes"] = results,
            ["checkedAt"] = DateTime.UtcNow
        };

        return allHealthy
            ? HealthCheckResult.Healthy("Все внешние сервисы доступны", data)
            : HealthCheckResult.Degraded("Некоторые внешние сервисы недоступны", data: data);
    }
}