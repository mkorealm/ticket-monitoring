using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Services;

/// <summary>Потокобезопасное хранилище изменений для API.</summary>
public class ChangesStoreService
{
    private readonly Dictionary<string, List<ChangeRecord>> _changesByMonitor = [];
    private readonly Lock _lock = new();

    /// <summary>Добавляет изменения для конкретного монитора.</summary>
    public void AddChanges(List<TicketChange> changes, string monitorName)
    {
        lock (_lock)
        {
            if (!_changesByMonitor.TryGetValue(monitorName, out _))
                _changesByMonitor[monitorName] = [];

            foreach (var change in changes)
            {
                _changesByMonitor[monitorName].Add(new ChangeRecord
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = change.Type.ToString(),
                    TicketId = change.Ticket?.Id ?? string.Empty,
                    Title = change.Ticket?.Title ?? string.Empty,
                    Status = change.Ticket?.Status ?? string.Empty,
                    Initiator = change.Ticket?.Initiator ?? string.Empty,
                    Priority = change.Ticket?.Priority ?? string.Empty,
                    OpenDate = change.Ticket?.OpenDate ?? DateTime.MinValue,
                    LastModified = change.Ticket?.LastModified ?? DateTime.MinValue,
                    Url = change.Ticket?.Url ?? string.Empty,
                    DetectedAt = DateTime.Now
                });
            }
        }
    }

    /// <summary>Возвращает изменения по монитору (без очистки).</summary>
    public List<ChangeRecord> GetChanges(string monitorName)
    {
        lock (_lock)
        {
            return _changesByMonitor.TryGetValue(monitorName, out var changes) ? [.. changes] : [];
        }
    }

    /// <summary>Возвращает изменения и очищает хранилище для монитора.</summary>
    public List<ChangeRecord> ConsumeChanges(string monitorName)
    {
        lock (_lock)
        {
            var changes = GetChanges(monitorName);
            _changesByMonitor.Remove(monitorName);
            return changes;
        }
    }

    /// <summary>Очищает изменения для монитора.</summary>
    public void ClearChanges(string monitorName)
    {
        lock (_lock)
        {
            _changesByMonitor.Remove(monitorName);
        }
    }
}