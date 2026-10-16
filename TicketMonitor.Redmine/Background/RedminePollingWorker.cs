using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketMonitor.Redmine.Clients;
using TicketMonitor.Redmine.Models;
using TicketMonitor.Redmine.Services;

namespace TicketMonitor.Redmine.Background;

/// <summary>
/// Фоновый сервис опроса Redmine на закрытые задачи.
/// Раз в N минут обновляет статус GLPI по связанным заявкам.
/// </summary>
public sealed class RedminePollingWorker(
    IRedmineClient redmineClient,
    RedmineLinkRepository linkRepository,
    RedminePollStateRepository stateRepository,
    GlpiStatusUpdater glpiStatusUpdater,
    IOptions<RedmineOptions> options,
    ILogger<RedminePollingWorker> logger) : BackgroundService
{
    private readonly IRedmineClient _redmineClient = redmineClient ?? throw new ArgumentNullException(nameof(redmineClient));
    private readonly RedmineLinkRepository _linkRepository = linkRepository ?? throw new ArgumentNullException(nameof(linkRepository));
    private readonly RedminePollStateRepository _stateRepository = stateRepository ?? throw new ArgumentNullException(nameof(stateRepository));
    private readonly GlpiStatusUpdater _glpiStatusUpdater = glpiStatusUpdater ?? throw new ArgumentNullException(nameof(glpiStatusUpdater));
    private readonly RedmineOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<RedminePollingWorker> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_options.PollingIntervalMinutes);

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Redmine polling worker запущен с интервалом {Interval} мин.", _options.PollingIntervalMinutes);

        // Первый запуск — через 30 секунд после старта (дать сервисам инициализироваться).
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в цикле polling Redmine");
            }

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Redmine polling worker остановлен");
    }

    // -------------------------------------------------------------------------
    // Private
    // -------------------------------------------------------------------------

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var since = ResolveSinceTimestamp();

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Polling Redmine: задачи, обновлённые после {Since}", since);

        var closedIssues = await _redmineClient
            .GetClosedIssuesAsync(since, cancellationToken)
            .ConfigureAwait(false);

        if (closedIssues.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Polling: закрытых задач нет");

            SaveState(DateTime.UtcNow, 0);
            return;
        }

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Polling: получено {Count} закрытых задач", closedIssues.Count);

        var processed = 0;

        foreach (var issue in closedIssues)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (string.IsNullOrEmpty(issue.GlpiTicketId))
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Задача Redmine #{IssueId} пропущена: нет ID заявки GLPI", issue.Id);

                continue;
            }

            var link = _linkRepository.GetByTicketId(issue.GlpiTicketId);
            if (link is null)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Задача Redmine #{IssueId} пропущена: нет связи с GLPI #{TicketId}",
                        issue.Id, issue.GlpiTicketId);

                continue;
            }

            // Проверяем, что связь указывает на эту задачу Redmine.
            if (link.RedmineIssueId != issue.Id)
            {
                _logger.LogWarning(
                    "Связь GLPI #{TicketId} указывает на Redmine #{ExpectedId}, но задача в Redmine #{ActualId}. Пропускаю",
                    issue.GlpiTicketId, link.RedmineIssueId, issue.Id);
                continue;
            }

            var updated = await _glpiStatusUpdater
                .UpdateStatusAsync(issue.GlpiTicketId, issue.StatusId, cancellationToken)
                .ConfigureAwait(false);

            if (updated)
                processed++;
        }

        SaveState(DateTime.UtcNow, processed);

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Polling завершён: обработано {Processed} из {Total}", processed, closedIssues.Count);
    }

    private DateTime ResolveSinceTimestamp()
    {
        var state = _stateRepository.Get();

        if (state is null)
        {
            // Первый запуск — берём «сейчас минус 24 часа», чтобы не тянуть всю историю.
            var initial = DateTime.UtcNow.AddHours(-24);

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Состояние polling отсутствует — начальная точка: {Initial}", initial);

            return initial;
        }

        return state.LastPollTimestamp;
    }

    private void SaveState(DateTime lastPollTimestamp, int processedCount)
    {
        try
        {
            _stateRepository.Save(new RedminePollState
            {
                LastPollTimestamp = lastPollTimestamp,
                LastProcessedCount = processedCount,
                UpdatedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось сохранить состояние polling");
        }
    }
}