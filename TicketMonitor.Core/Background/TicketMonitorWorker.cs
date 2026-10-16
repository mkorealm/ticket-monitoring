using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Models.Dto;
using TicketMonitor.Core.Services;

namespace TicketMonitor.Core.Background;

/// <summary>Воркер одного монитора: периодически опрашивает GLPI, детектирует изменения и рассылает уведомления.</summary>
public class TicketMonitorWorker(
    MonitorConfig config,
    ITicketService ticketService,
    ITicketRepository ticketRepository,
    ITicketChangeDetector changeDetector,
    INotificationChannelResolver channelResolver,
    NotificationDispatcherService dispatcherService,
    ChangesStoreService changesStore,
    HealthService healthService,
    TicketDetailsCollector detailsCollector,
    IRedmineLinkChecker redmineLinkChecker,
    ILogger<TicketMonitorBackgroundService> logger) : IDisposable
{
    /// <summary>Максимум ответов в одном уведомлении.</summary>
    private const int MaxAnswersPerNotification = 5;

    /// <summary>Запас (сек) при фильтрации ответов — чтобы не потерять пограничные.</summary>
    private const int AnswersSinceSafetySeconds = 10;

    /// <summary>GLPI-код статуса «Решена» (в текстовом виде).</summary>
    private const string StatusSolved = "Решена";

    /// <summary>GLPI-код статуса «Закрыта» (в текстовом виде).</summary>
    private const string StatusClosed = "Закрыта";

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Lock _statusLock = new();
    private DateTime _lastCheck = DateTime.MinValue;
    private int _ticketsCount;
    private bool _disposed;

    /// <summary>Запускает бесконечный цикл мониторинга.</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["MonitorName"] = config.Name
        });

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка в мониторе '{MonitorName}'", config.Name);
            }

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("[{MonitorName}] Ожидание {Minutes} минут...", config.Name, config.CheckIntervalMinutes);

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(config.CheckIntervalMinutes), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Выполняет одну проверку заявок.</summary>
    public async Task<bool> CheckAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTime previousCheck;
            lock (_statusLock)
            {
                previousCheck = _lastCheck;
            }

            logger.LogInformation("Начало проверки");

            var currentTickets = await ticketService.GetTicketsByCategoryAsync(config.CategoryFilter, cancellationToken).ConfigureAwait(false);

            if (currentTickets.Count == 0)
            {
                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation("Заявок по категории '{Category}' не найдено. Сохраняю пустой снимок", config.CategoryFilter);

                UpdateStatus(ticketsCount: 0);
                healthService.MarkCheckCompleted(0, config.Name, config.CheckIntervalMinutes);
                ticketRepository.SaveTickets(currentTickets, config.DatabaseFileName);
                return true;
            }

            UpdateStatus(ticketsCount: currentTickets.Count);
            healthService.MarkCheckCompleted(currentTickets.Count, config.Name, config.CheckIntervalMinutes);

            var previousTickets = ticketRepository.LoadTickets(config.DatabaseFileName);

            if (previousTickets.Count == 0)
            {
                ticketRepository.SaveTickets(currentTickets, config.DatabaseFileName);

                if (logger.IsEnabled(LogLevel.Information))
                    logger.LogInformation("Первичное сохранение: {Count} заявок", currentTickets.Count);

                return true;
            }

            var changes = changeDetector.Detect(previousTickets, currentTickets);
            ticketRepository.SaveTickets(currentTickets, config.DatabaseFileName);

            if (changes.Count == 0) return true;

            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Обнаружены изменения: {Count}", changes.Count);

            changesStore.AddChanges([.. changes], config.Name);

            var since = previousCheck == DateTime.MinValue
                ? (DateTime?)null
                : previousCheck.AddSeconds(-AnswersSinceSafetySeconds);

            await SendNotificationsAsync(changes, since, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException)
        {
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Проверка монитора '{MonitorName}' отменена", config.Name);
            return false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>Возвращает снимок статуса монитора.</summary>
    public MonitorStatusDto GetStatus()
    {
        DateTime lastCheck;
        int ticketsCount;

        lock (_statusLock)
        {
            lastCheck = _lastCheck;
            ticketsCount = _ticketsCount;
        }

        return new MonitorStatusDto
        {
            Name = config.Name,
            Category = config.CategoryFilter,
            LastCheck = lastCheck,
            TicketsCount = ticketsCount,
            IntervalMinutes = config.CheckIntervalMinutes
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _semaphore.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void UpdateStatus(int ticketsCount)
    {
        lock (_statusLock)
        {
            _lastCheck = DateTime.Now;
            _ticketsCount = ticketsCount;
        }
    }

    private async Task SendNotificationsAsync(
        IReadOnlyList<TicketChange> changes,
        DateTime? since,
        CancellationToken cancellationToken)
    {
        var services = channelResolver.ResolveForMonitor(config);
        if (services.Count == 0)
        {
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Монитор '{MonitorName}': нет активных каналов — уведомления не отправляются", config.Name);
            return;
        }

        foreach (var change in changes)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var notification = await BuildNotificationAsync(change, since, cancellationToken).ConfigureAwait(false);

                if (notification is null)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                        logger.LogDebug("Уведомление по заявке #{TicketId} пропущено (нет значимых изменений)",
                            change.Ticket?.Id);
                    continue;
                }

                await dispatcherService.DispatchAsync(
                    notification, services, config.DatabaseFileName, config.Name, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Не удалось отправить уведомление по заявке #{TicketId} ({MonitorName})", change.Ticket?.Id, config.Name);
            }

            try
            {
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<NotificationMessage?> BuildNotificationAsync(
        TicketChange change,
        DateTime? since,
        CancellationToken cancellationToken)
    {
        var ticket = change.Ticket;
        if (ticket is null || string.IsNullOrEmpty(ticket.Id))
            return null;

        var kind = change.Type == ChangeType.New ? NotificationKind.New : NotificationKind.Updated;

        var answersResult = await detailsCollector
            .GetAnswersAsync(
                ticket.Id,
                config.SupportGroupId,
                since: since,
                maxAnswers: MaxAnswersPerNotification,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var deltas = TicketDetailsCollector.ExtractDeltas(change.Message);

        if (kind == NotificationKind.Updated
            && deltas.Count == 0
            && answersResult.TotalCount == 0)
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug(
                    "Заявка #{TicketId}: изменений нет (ответ только от группы поддержки) — уведомление не отправляется",
                    ticket.Id);
            return null;
        }

        var subject = kind switch
        {
            NotificationKind.New => $"Новая заявка #{ticket.Id}: {ticket.Title}",
            NotificationKind.Updated => $"Обновление заявки #{ticket.Id}: {ticket.Title}",
            _ => $"Уведомление по заявке #{ticket.Id}"
        };

        var description = await detailsCollector
            .GetDescriptionAsync(ticket.Id, cancellationToken)
            .ConfigureAwait(false);

        var actionLinks = await BuildActionLinksAsync(ticket, cancellationToken).ConfigureAwait(false);

        return new NotificationMessage
        {
            Subject = subject,
            Kind = kind,
            Ticket = ticket,
            Description = description,
            Answers = answersResult.Answers,
            TotalAnswersCount = answersResult.TotalCount,
            Deltas = deltas,
            ActionLinks = actionLinks
        };
    }

    /// <summary>
    /// Формирует список ссылок-действий для уведомления: Redmine-кнопка и/или кнопка «Добавить решение».
    /// </summary>
    /// <remarks>
    /// Redmine-кнопка показывается, если в настройках RocketChat-канала задан
    /// <c>RedmineCreateFormUrl</c>. Её лейбл зависит от наличия связи с Redmine
    /// (<c>IRedmineLinkChecker.IsLinkedAsync</c>).
    /// <para>
    /// Кнопка «Добавить решение» показывается, если задан <c>GlpiSolutionFormUrl</c>
    /// и статус заявки не «Решена» и не «Закрыта».
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<TicketActionLink>> BuildActionLinksAsync(
        TicketInfo ticket,
        CancellationToken cancellationToken)
    {
        var redmineFormUrl = GetRocketChatChannelSetting("RedmineCreateFormUrl");
        var glpiSolutionFormUrl = GetRocketChatChannelSetting("GlpiSolutionFormUrl");

        var links = new List<TicketActionLink>(capacity: 2);

        if (!string.IsNullOrWhiteSpace(redmineFormUrl))
        {
            var redmineLink = await BuildRedmineActionLinkAsync(ticket, redmineFormUrl, cancellationToken)
                .ConfigureAwait(false);

            if (redmineLink is not null)
                links.Add(redmineLink);
        }

        if (!string.IsNullOrWhiteSpace(glpiSolutionFormUrl) && IsSolutionButtonVisible(ticket.Status))
        {
            links.Add(new TicketActionLink
            {
                Label = "✅ Добавить решение",
                Url = $"{glpiSolutionFormUrl.TrimEnd('/')}?ticketId={Uri.EscapeDataString(ticket.Id)}"
            });
        }

        return links;
    }

    /// <summary>
    /// Строит Redmine-ссылку. Возвращает <c>null</c>, если URL формы пуст.
    /// </summary>
    private async Task<TicketActionLink?> BuildRedmineActionLinkAsync(
        TicketInfo ticket,
        string redmineFormUrl,
        CancellationToken cancellationToken)
    {
        bool isLinked;
        try
        {
            isLinked = await redmineLinkChecker
                .IsLinkedAsync(ticket.Id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Не удалось проверить связь заявки #{TicketId} с Redmine — считаю, что связи нет",
                ticket.Id);
            isLinked = false;
        }

        var label = isLinked
            ? "🔁 Повторить задачу в Redmine"
            : "➕ Создать задачу в Redmine";

        return new TicketActionLink
        {
            Label = label,
            Url = $"{redmineFormUrl.TrimEnd('/')}?ticketId={Uri.EscapeDataString(ticket.Id)}"
        };
    }

    /// <summary>
    /// Возвращает значение настройки из RocketChat-канала (первого включённого), или <c>null</c>.
    /// </summary>
    private string? GetRocketChatChannelSetting(string key)
    {
        var channels = config.Notification?.Channels;
        if (channels is null) return null;

        foreach (var channel in channels)
        {
            if (!channel.Enabled) continue;
            if (!string.Equals(channel.Type, "RocketChat", StringComparison.OrdinalIgnoreCase)) continue;

            if (channel.Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    /// <summary>
    /// Кнопка «Добавить решение» показывается для всех статусов, кроме «Решена» и «Закрыта».
    /// </summary>
    private static bool IsSolutionButtonVisible(string status)
    {
        if (string.IsNullOrEmpty(status)) return true;
        if (status.Contains(StatusSolved, StringComparison.Ordinal)) return false;
        if (status.Contains(StatusClosed, StringComparison.Ordinal)) return false;
        return true;
    }
}