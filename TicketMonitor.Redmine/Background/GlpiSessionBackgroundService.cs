using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Clients;

namespace TicketMonitor.Redmine.Background;

/// <summary>
/// Фоновый сервис инициализации GLPI-сессии.
/// Инициализирует сессию при старте приложения и завершает при остановке.
/// </summary>
public sealed class GlpiSessionBackgroundService(
    GlpiClient glpiClient,
    IConfiguration configuration,
    ILogger<GlpiSessionBackgroundService> logger) : BackgroundService
{
    private readonly GlpiClient _glpiClient = glpiClient ?? throw new ArgumentNullException(nameof(glpiClient));
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly ILogger<GlpiSessionBackgroundService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Инициализация GLPI-сессии для Redmine-сервиса...");

        var userToken = _configuration["GLPI:UserToken"];
        if (string.IsNullOrEmpty(userToken))
        {
            _logger.LogError("GLPI:UserToken не настроен — обновление статусов GLPI будет недоступно");
            return;
        }

        var sessionInitialized = await _glpiClient
            .InitSession(userToken, stoppingToken)
            .ConfigureAwait(false);

        if (!sessionInitialized)
        {
            _logger.LogError("Не удалось инициализировать GLPI-сессию — обновление статусов GLPI будет недоступно");
            return;
        }

        _logger.LogInformation("GLPI-сессия установлена");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка
        }
        finally
        {
            // CancellationToken.None — исходный токен уже отменён,
            // а KillSession должен успеть отправить запрос.
            await _glpiClient.KillSession(CancellationToken.None).ConfigureAwait(false);
            _logger.LogInformation("GLPI-сессия завершена");
        }
    }
}