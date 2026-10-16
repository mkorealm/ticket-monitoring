namespace TicketMonitor.Core.Contracts;

/// <summary>Кэш имён пользователей GLPI.</summary>
public interface IGlpiUserCacheService
{
    Task<string> GetUserNameAsync(object? userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetUserNamesBatchAsync(IEnumerable<object?> userIds, CancellationToken cancellationToken = default);
}