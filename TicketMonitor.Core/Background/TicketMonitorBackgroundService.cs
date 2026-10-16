using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Background;

/// <summary>Фоновый сервис мониторинга заявок.</summary>
public class TicketMonitorBackgroundService(
    IConfiguration configuration,
    GlpiClient glpiClient,
    IServiceProvider serviceProvider,
    ILogger<TicketMonitorBackgroundService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Сервис мониторинга заявок запущен");

        var userToken = configuration["GLPI:UserToken"];
        if (string.IsNullOrEmpty(userToken))
        {
            logger.LogError("UserToken не настроен");
            return;
        }

        var sessionInitialized = await glpiClient.InitSession(userToken, stoppingToken).ConfigureAwait(false);
        if (!sessionInitialized)
        {
            logger.LogError("Не удалось инициализировать GLPI сессию");
            return;
        }

        logger.LogInformation("GLPI сессия установлена");

        var monitorManagement = serviceProvider.GetRequiredService<IMonitorManagementService>();
        monitorManagement.LoadMonitorsFromDatabase();

        var monitors = configuration.GetSection("Monitors").Get<List<MonitorConfig>>();

        if (monitors != null)
        {
            foreach (var monitorConfig in monitors)
            {
                var added = monitorConfig.IsSystem
                    ? monitorManagement.AddOrUpdateSystemMonitor(monitorConfig)
                    : monitorManagement.AddMonitor(monitorConfig);

                if (added)
                {
                    if (logger.IsEnabled(LogLevel.Information))
                        logger.LogInformation("Монитор '{MonitorName}' добавлен из конфигурации", monitorConfig.Name);
                }
                else
                {
                    logger.LogWarning("Монитор '{MonitorName}' не был добавлен из конфигурации", monitorConfig.Name);
                }
            }
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await glpiClient.KillSession(CancellationToken.None).ConfigureAwait(false);
        }
    }
}