using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Services;

/// <summary>Реализация детектора изменений заявок.</summary>
public sealed class TicketChangeDetector(
    IMessageBuilderService messageBuilder,
    ILogger<TicketChangeDetector> logger) : ITicketChangeDetector
{
    private readonly IMessageBuilderService _messageBuilder = messageBuilder ?? throw new ArgumentNullException(nameof(messageBuilder));
    private readonly ILogger<TicketChangeDetector> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public IReadOnlyList<TicketChange> Detect(IReadOnlyList<TicketInfo> previous, IReadOnlyList<TicketInfo> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        if (current.Count == 0) return [];

        if (previous.Count > 0 && ComputeHash(previous) == ComputeHash(current))
            return [];

        var previousById = BuildIndex(previous);
        var changes = new List<TicketChange>(capacity: current.Count);

        foreach (var ticket in current)
        {
            if (string.IsNullOrEmpty(ticket.Id))
            {
                _logger.LogWarning("Заявка без Id пропущена при детектировании изменений (Title='{Title}')", ticket.Title);
                continue;
            }

            if (previousById.TryGetValue(ticket.Id, out var oldTicket))
            {
                var deltas = ComputeDeltas(oldTicket, ticket);

                if (deltas.Count == 0) continue;

                changes.Add(new TicketChange
                {
                    Type = ChangeType.Updated,
                    Ticket = ticket,
                    Message = _messageBuilder.BuildForUpdatedTicket(ticket, deltas)
                });
            }
            else
            {
                changes.Add(new TicketChange
                {
                    Type = ClassifyAsNew(ticket) ? ChangeType.New : ChangeType.Updated,
                    Ticket = ticket,
                    Message = _messageBuilder.BuildForNewTicket(ticket)
                });
            }
        }

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Детектировано {Count} изменений из {Total}", changes.Count, current.Count);

        return changes;
    }

    private static Dictionary<string, TicketInfo> BuildIndex(IReadOnlyList<TicketInfo> tickets)
    {
        var index = new Dictionary<string, TicketInfo>(tickets.Count, StringComparer.Ordinal);
        foreach (var ticket in tickets)
        {
            if (string.IsNullOrEmpty(ticket.Id)) continue;
            index.TryAdd(ticket.Id, ticket);
        }
        return index;
    }

    /// <summary>
    /// Сравнивает значимые поля заявки. <see cref="TicketInfo.LastModified"/>
    /// НЕ является самостоятельной дельтой — иначе любое добавление followup
    /// (включая ответ группы поддержки) порождало бы ложное уведомление.
    /// </summary>
    private static List<string> ComputeDeltas(TicketInfo oldTicket, TicketInfo current)
    {
        var deltas = new List<string>(capacity: 4);

        if (!StringEquals(oldTicket.Status, current.Status))
            deltas.Add($"Статус: {oldTicket.Status} → {current.Status}");

        if (!StringEquals(oldTicket.Assignee, current.Assignee))
            deltas.Add($"Исполнитель: {oldTicket.Assignee} → {current.Assignee}");

        if (!StringEquals(oldTicket.Priority, current.Priority))
            deltas.Add($"Приоритет: {oldTicket.Priority} → {current.Priority}");

        return deltas;
    }

    private static bool ClassifyAsNew(TicketInfo ticket)
    {
        return ticket.Status.Contains("Новая", StringComparison.Ordinal)
            || ticket.Status.Contains("назначена", StringComparison.OrdinalIgnoreCase);
    }

    private static bool StringEquals(string? a, string? b)
        => string.Equals(a?.Trim() ?? string.Empty, b?.Trim() ?? string.Empty, StringComparison.Ordinal);

    private static string ComputeHash(IReadOnlyList<TicketInfo> tickets)
    {
        var ordered = tickets
            .Where(t => !string.IsNullOrEmpty(t.Id))
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .Select(t => $"{t.Id}|{t.Status}|{t.Assignee}|{t.Priority}");

        var combined = string.Join(';', ordered);
        var bytes = Encoding.UTF8.GetBytes(combined);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}