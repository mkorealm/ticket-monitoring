using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Helpers;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Services.Formatters;

namespace TicketMonitor.Core.Services;

/// <summary>Сервис оркестрации отправки уведомлений.</summary>
public sealed class NotificationDispatcherService(
    INotificationChannelResolver channelResolver,
    TicketDetailsCollector detailsCollector,
    GlpiClient glpiClient,
    IGlpiUserCacheService userCache,
    IMessageIdRepository messageIdRepository,
    EmailHtmlFormatter emailFormatter,
    RocketChatMarkdownFormatter rocketChatFormatter,
    ILogger<NotificationDispatcherService> logger)
{
    /// <summary>Максимум ответов в одном уведомлении (для ручных уведомлений).</summary>
    private const int MaxAnswersInManualNotification = 5;

    private readonly INotificationChannelResolver _channelResolver = channelResolver ?? throw new ArgumentNullException(nameof(channelResolver));
    private readonly TicketDetailsCollector _detailsCollector = detailsCollector ?? throw new ArgumentNullException(nameof(detailsCollector));
    private readonly GlpiClient _glpiClient = glpiClient ?? throw new ArgumentNullException(nameof(glpiClient));
    private readonly IGlpiUserCacheService _userCache = userCache ?? throw new ArgumentNullException(nameof(userCache));
    private readonly IMessageIdRepository _messageIdRepository = messageIdRepository ?? throw new ArgumentNullException(nameof(messageIdRepository));
    private readonly EmailHtmlFormatter _emailFormatter = emailFormatter ?? throw new ArgumentNullException(nameof(emailFormatter));
    private readonly RocketChatMarkdownFormatter _rocketChatFormatter = rocketChatFormatter ?? throw new ArgumentNullException(nameof(rocketChatFormatter));
    private readonly ILogger<NotificationDispatcherService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Отправляет типизированное уведомление во все переданные каналы.</summary>
    public async Task<int> DispatchAsync(
        NotificationMessage notification,
        IReadOnlyList<INotificationService> services,
        string dbFileName,
        string? monitorName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(services);

        if (services.Count == 0)
        {
            _logger.LogWarning("Нет активных каналов для отправки уведомления #{TicketId}", notification.Ticket.Id);
            return 0;
        }

        var successCount = 0;

        foreach (var service in services)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var sent = await DispatchToChannelAsync(
                    service, notification, dbFileName, monitorName, cancellationToken)
                    .ConfigureAwait(false);

                if (sent) successCount++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка отправки уведомления #{TicketId} через канал {Channel}",
                    notification.Ticket.Id, service.GetType().Name);
            }
        }

        return successCount;
    }

    /// <summary>
    /// Отправляет ручное уведомление по заявке с полной историей.
    /// Использует <see cref="TicketDetailsCollector"/> — так же, как автоматические уведомления.
    /// </summary>
    public async Task<bool> SendManualNotificationAsync(
        string ticketId,
        IReadOnlyList<string>? targets = null,
        int supportGroupId = 3,
        MonitorConfig? monitorConfig = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);

        try
        {
            var services = _channelResolver.ResolveByTargets(targets);
            if (services.Count == 0)
            {
                _logger.LogWarning("Не найдены сервисы уведомлений для targets='{Targets}'",
                    targets is null ? "(all)" : string.Join(",", targets));
                return false;
            }

            var ticket = await LoadTicketInfoAsync(ticketId, cancellationToken).ConfigureAwait(false);
            if (ticket is null)
            {
                _logger.LogWarning("Ручное уведомление: заявка #{TicketId} не найдена в GLPI", ticketId);
                return false;
            }

            var description = await _detailsCollector
                .GetDescriptionAsync(ticketId, cancellationToken)
                .ConfigureAwait(false);

            var answersResult = await _detailsCollector
                .GetAnswersAsync(
                    ticketId,
                    supportGroupId,
                    since: null,
                    maxAnswers: MaxAnswersInManualNotification,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var notification = new NotificationMessage
            {
                Subject = $"Ручное уведомление по заявке #{ticketId}: {ticket.Title}",
                Kind = NotificationKind.Manual,
                Ticket = ticket,
                Description = description,
                Answers = answersResult.Answers,
                TotalAnswersCount = answersResult.TotalCount
            };

            var dbFileName = monitorConfig?.DatabaseFileName ?? "tickets_docsvision.db";

            var sent = await DispatchAsync(
                notification, services, dbFileName, monitorConfig?.Name, cancellationToken)
                .ConfigureAwait(false);

            return sent > 0;
        }
        catch (OperationCanceledException)
        {
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Ручное уведомление по заявке #{TicketId} отменено", ticketId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при отправке ручного уведомления по заявке #{TicketId}", ticketId);
            return false;
        }
    }

    /// <summary>Загружает <see cref="TicketInfo"/> из GLPI для ручного уведомления.</summary>
    private async Task<TicketInfo?> LoadTicketInfoAsync(string ticketId, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await _glpiClient.GetTicketAsync(ticketId, cancellationToken).ConfigureAwait(false);
            if (raw.Count == 0)
                return null;

            var statusCode = NotificationHelper.GetInt(raw, "status") ?? 0;
            var statusText = NotificationHelper.StatusMap.TryGetValue(statusCode, out var s)
                ? s
                : statusCode.ToString();

            var priorityCode = NotificationHelper.GetInt(raw, "priority") ?? 0;
            var priorityText = NotificationHelper.PriorityMap.TryGetValue(priorityCode, out var p)
                ? p
                : priorityCode.ToString();

            var initiatorRaw = NotificationHelper.GetString(raw, "users_id_recipient");
            var initiatorName = string.IsNullOrEmpty(initiatorRaw)
                ? string.Empty
                : await _userCache.GetUserNameAsync(initiatorRaw, cancellationToken).ConfigureAwait(false);

            return new TicketInfo
            {
                Id = ticketId,
                Title = NotificationHelper.GetString(raw, "name") ?? string.Empty,
                Status = statusText,
                Priority = priorityText,
                OpenDate = ParseDate(NotificationHelper.GetString(raw, "date")),
                Initiator = initiatorName,
                Assignee = string.Empty,
                Category = NotificationHelper.GetString(raw, "itilcategories_id") ?? string.Empty,
                LastModified = ParseDate(NotificationHelper.GetString(raw, "date_mod")),
                Url = $"https://glpi.example.ru/front/ticket.form.php?id={ticketId}"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось загрузить заявку #{TicketId} из GLPI", ticketId);
            return null;
        }
    }

    private static DateTime ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return default;

        if (DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var exact))
            return exact;

        return DateTime.TryParse(s, out var d) ? d : default;
    }

    private async Task<bool> DispatchToChannelAsync(
        INotificationService service,
        NotificationMessage notification,
        string dbFileName,
        string? monitorName,
        CancellationToken cancellationToken)
    {
        return service switch
        {
            RocketChatNotificationService rc => await SendViaRocketChatAsync(rc, notification, dbFileName, monitorName, cancellationToken).ConfigureAwait(false),
            ExchangeEmailService email => await SendViaEmailAsync(email, notification, cancellationToken).ConfigureAwait(false),
            _ => await SendViaUnknownAsync(service, notification, cancellationToken).ConfigureAwait(false)
        };
    }

    private async Task<bool> SendViaRocketChatAsync(
        RocketChatNotificationService service,
        NotificationMessage notification,
        string dbFileName,
        string? monitorName,
        CancellationToken cancellationToken)
    {
        var ticketId = notification.Ticket.Id;
        var savedMessageId = _messageIdRepository.GetMessageId(ticketId, dbFileName);

        if (!string.IsNullOrEmpty(savedMessageId) && IsClosedStatus(notification.Ticket.Status))
        {
            var newTitle = $"Завершённая заявка #{ticketId}";
            var edited = await service.EditRootMessageAsync(
                savedMessageId, newTitle, monitorName, cancellationToken).ConfigureAwait(false);

            if (!edited)
                _logger.LogWarning(
                    "Не удалось отредактировать корневое сообщение {MessageId} для закрытой заявки #{TicketId}",
                    savedMessageId, ticketId);
        }

        var messageForSend = !string.IsNullOrEmpty(savedMessageId)
            ? notification with { ActionLinks = [] }
            : notification;

        var body = _rocketChatFormatter.Format(messageForSend);

        if (!string.IsNullOrEmpty(savedMessageId))
        {
            var threadedId = await service.SendThreadedAsync(
                notification.Subject, body, monitorName, savedMessageId, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(threadedId))
            {
                _logger.LogWarning("Не удалось отправить сообщение в тред для заявки #{TicketId}", ticketId);
                return false;
            }

            return true;
        }

        var newMessageId = await service.SendThreadedAsync(
            notification.Subject, body, monitorName, tmid: null, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(newMessageId))
        {
            _logger.LogWarning("Не удалось получить MessageId после отправки для заявки #{TicketId}", ticketId);
            return false;
        }

        _messageIdRepository.SaveMessageId(ticketId, newMessageId, dbFileName);
        return true;
    }

    private async Task<bool> SendViaEmailAsync(
        ExchangeEmailService service,
        NotificationMessage notification,
        CancellationToken cancellationToken)
    {
        var html = _emailFormatter.Format(notification);
        var plainText = BuildPlainTextFallback(notification);

        return await service.SendHtmlAsync(notification.Subject, html, plainText, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> SendViaUnknownAsync(
        INotificationService service,
        NotificationMessage notification,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Канал {Channel} не имеет специализированной обработки — используется SendNotificationAsync с plain-text",
            service.GetType().Name);

        var plainText = BuildPlainTextFallback(notification);
        await service.SendNotificationAsync(notification.Subject, plainText, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static bool IsClosedStatus(string? status)
    {
        if (string.IsNullOrEmpty(status)) return false;
        return status.Contains("Решена", StringComparison.Ordinal)
            || status.Contains("Закрыта", StringComparison.Ordinal);
    }

    private static string BuildPlainTextFallback(NotificationMessage notification)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ID: {notification.Ticket.Id}");
        sb.AppendLine($"Заголовок: {notification.Ticket.Title}");
        sb.AppendLine($"Статус: {notification.Ticket.Status}");

        if (!string.IsNullOrEmpty(notification.Ticket.Initiator))
            sb.AppendLine($"Инициатор: {notification.Ticket.Initiator}");

        sb.AppendLine($"Приоритет: {notification.Ticket.Priority}");

        if (notification.Ticket.OpenDate != default)
            sb.AppendLine($"Дата открытия: {notification.Ticket.OpenDate:dd-MM-yyyy HH:mm}");

        sb.AppendLine($"Ссылка: {notification.Ticket.Url}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(notification.Description))
        {
            sb.AppendLine("Описание:");
            sb.AppendLine(notification.Description);
            sb.AppendLine();
        }

        if (notification.Answers.Count > 0)
        {
            sb.AppendLine("Новые ответы:");

            if (notification.TotalAnswersCount > notification.Answers.Count)
                sb.AppendLine($"(всего {notification.TotalAnswersCount}, показаны последние {notification.Answers.Count})");

            foreach (var answer in notification.Answers)
                sb.AppendLine($"— {answer.Author}: {answer.Text}");
        }

        if (notification.ActionLinks.Count > 0)
        {
            sb.AppendLine();
            foreach (var link in notification.ActionLinks)
                sb.AppendLine($"{link.Label}: {link.Url}");
        }

        return sb.ToString().TrimEnd();
    }
}