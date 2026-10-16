using System.Text;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Services;

/// <summary>Сервис формирования текстовых сообщений для уведомлений по заявкам.</summary>
public sealed class MessageBuilderService(ILogger<MessageBuilderService> logger) : IMessageBuilderService
{
    private const string DateFormat = "dd-MM-yyyy HH:mm";

    private readonly ILogger<MessageBuilderService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public string BuildForNewTicket(TicketInfo ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var sb = new StringBuilder();
        AppendBaseFields(sb, ticket);
        sb.AppendLine($"Дата открытия: {ticket.OpenDate.ToString(DateFormat)}");
        sb.AppendLine($"Ссылка: {ticket.Url}");

        var result = sb.ToString().TrimEnd();

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Сформировано сообщение для новой заявки #{TicketId}", ticket.Id);

        return result;
    }

    /// <inheritdoc />
    public string BuildForUpdatedTicket(TicketInfo ticket, IReadOnlyList<string> deltas)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(deltas);

        var sb = new StringBuilder();
        AppendBaseFields(sb, ticket);

        foreach (var delta in deltas)
            sb.AppendLine(delta);

        sb.AppendLine($"Ссылка: {ticket.Url}");

        var result = sb.ToString().TrimEnd();

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Сформировано сообщение об обновлении заявки #{TicketId} ({DeltaCount} изменений)",
                ticket.Id, deltas.Count);

        return result;
    }

    private static void AppendBaseFields(StringBuilder sb, TicketInfo ticket)
    {
        sb.AppendLine($"ID: {ticket.Id}");
        sb.AppendLine($"Заголовок: {ticket.Title}");
        sb.AppendLine($"Статус: {ticket.Status}");

        if (!string.IsNullOrEmpty(ticket.Initiator))
            sb.AppendLine($"Инициатор: {ticket.Initiator}");

        sb.AppendLine($"Приоритет: {ticket.Priority}");
    }
}