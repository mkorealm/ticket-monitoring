using System.Text.Json;

namespace TicketMonitor.Core.Contracts;

/// <summary>Резолвер данных о пользователях в GLPI.</summary>
public interface IGlpiUserResolver
{
    /// <summary>Возвращает пользователя GLPI по его ID.</summary>
    Task<JsonElement?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Ищет пользователей по логину.</summary>
    Task<IReadOnlyList<JsonElement>> SearchUsersByLoginAsync(string login, CancellationToken cancellationToken = default);
}