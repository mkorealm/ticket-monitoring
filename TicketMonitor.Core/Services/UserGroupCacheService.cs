using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Contracts;

namespace TicketMonitor.Core.Services;

/// <summary>Сервис кэширования информации о группах пользователей GLPI.</summary>
public class UserGroupCacheService(
    IMemoryCache cache,
    GlpiClient glpiClient,
    IOperationRegistry operationRegistry,
    ILogger<UserGroupCacheService> logger)
{
    private const int CacheHours = 24;

    private readonly IMemoryCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly GlpiClient _glpiClient = glpiClient ?? throw new ArgumentNullException(nameof(glpiClient));
    private readonly IOperationRegistry _operationRegistry = operationRegistry ?? throw new ArgumentNullException(nameof(operationRegistry));
    private readonly ILogger<UserGroupCacheService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Возвращает список пользователей группы.</summary>
    public async Task<HashSet<int>> GetGroupUsersAsync(int groupId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"group_{groupId}_users";
        if (_cache.TryGetValue(cacheKey, out HashSet<int>? cachedUsers) && cachedUsers != null)
            return cachedUsers;

        var users = await FetchGroupUsersFromApi(groupId, cancellationToken);
        _cache.Set(cacheKey, users, TimeSpan.FromHours(CacheHours));

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Группа {GroupId}: загружено {Count} пользователей", groupId, users.Count);

        return users;
    }

    /// <summary>Возвращает список групп, в которых состоит пользователь.</summary>
    public async Task<HashSet<int>> GetUserGroupsAsync(int userId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"user_{userId}_groups";
        if (_cache.TryGetValue(cacheKey, out HashSet<int>? cachedGroups) && cachedGroups != null)
            return cachedGroups;

        var groups = await FetchUserGroupsFromApi(userId, cancellationToken);
        _cache.Set(cacheKey, groups, TimeSpan.FromHours(CacheHours));

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Пользователь {UserId}: загружено {Count} групп", userId, groups.Count);

        return groups;
    }

    /// <summary>Проверяет, входит ли пользователь в группу поддержки.</summary>
    public async Task<bool> IsUserInSupportGroupAsync(int userId, int supportGroupId, CancellationToken cancellationToken = default)
    {
        var userGroups = await GetUserGroupsAsync(userId, cancellationToken);
        return userGroups.Contains(supportGroupId);
    }

    private async Task<HashSet<int>> FetchGroupUsersFromApi(int groupId, CancellationToken cancellationToken = default)
    {
        return await _operationRegistry.RunAsync(
            "GroupCache:FetchUsers",
            async ct =>
            {
                var result = new HashSet<int>();

                var url = $"apirest.php/Group_User?range=0-1000&groups_id={groupId}";
                using var request = _glpiClient.CreateRequest(HttpMethod.Get, url);
                using var response = await _glpiClient.SendWithSessionRetryAsync(request, ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Не удалось получить пользователей группы {GroupId}: {StatusCode}", groupId, response.StatusCode);
                    return result;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync(ct);
                try
                {
                    using var doc = JsonDocument.Parse(jsonResponse);

                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            if (!element.TryGetProperty("groups_id", out var groupIdElement)) continue;
                            if (ParseIntElement(groupIdElement) != groupId) continue;

                            if (element.TryGetProperty("users_id", out var userIdElement))
                            {
                                var parsedUserId = ParseIntElement(userIdElement);
                                if (parsedUserId.HasValue) result.Add(parsedUserId.Value);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при парсинге пользователей группы {GroupId}", groupId);
                }

                return result;
            },
            context: new { groupId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<HashSet<int>> FetchUserGroupsFromApi(int userId, CancellationToken cancellationToken = default)
    {
        return await _operationRegistry.RunAsync(
            "GroupCache:FetchGroups",
            async ct =>
            {
                var result = new HashSet<int>();

                var url = $"apirest.php/Group_User?range=0-1000&users_id={userId}";
                using var request = _glpiClient.CreateRequest(HttpMethod.Get, url);
                using var response = await _glpiClient.SendWithSessionRetryAsync(request, ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Не удалось получить группы пользователя {UserId}: {StatusCode}", userId, response.StatusCode);
                    return result;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync(ct);
                try
                {
                    using var doc = JsonDocument.Parse(jsonResponse);

                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            if (!element.TryGetProperty("users_id", out var userIdElement)) continue;
                            if (ParseIntElement(userIdElement) != userId) continue;

                            if (element.TryGetProperty("groups_id", out var groupsIdElement))
                            {
                                var parsedGroupId = ParseIntElement(groupsIdElement);
                                if (parsedGroupId.HasValue) result.Add(parsedGroupId.Value);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при парсинге групп пользователя {UserId}", userId);
                }

                return result;
            },
            context: new { userId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static int? ParseIntElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt32(out var n) ? n : null,
            JsonValueKind.String when int.TryParse(element.GetString(), out var parsed) => parsed,
            _ => null
        };
    }
}