using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Contracts;

namespace TicketMonitor.Core.Services;

/// <summary>Кэш имён пользователей GLPI с поддержкой batch-резолвинга.</summary>
public sealed class GlpiUserCacheService(
    IMemoryCache cache,
    IGlpiUserResolver glpiUserResolver,
    ILogger<GlpiUserCacheService> logger) : IGlpiUserCacheService
{
    private const int CacheHours = 24;
    private const int NegativeCacheMinutes = 5;
    private const int MaxParallelRequests = 8;

    private readonly IMemoryCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly IGlpiUserResolver _resolver = glpiUserResolver ?? throw new ArgumentNullException(nameof(glpiUserResolver));
    private readonly ILogger<GlpiUserCacheService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly SemaphoreSlim _parallelGate = new(MaxParallelRequests, MaxParallelRequests);
    private readonly ConcurrentDictionary<string, Task<string>> _inFlight = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<string> GetUserNameAsync(object? userId, CancellationToken cancellationToken = default)
    {
        if (userId is System.Collections.IEnumerable list and not string)
        {
            var first = list.Cast<object?>().FirstOrDefault();
            return await GetUserNameAsync(first, cancellationToken).ConfigureAwait(false);
        }

        var normalized = NormalizeUserId(userId);
        if (string.IsNullOrEmpty(normalized)) return string.Empty;

        var cacheKey = GetCacheKey(normalized);

        if (_cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
            return cached;

        var task = _inFlight.GetOrAdd(cacheKey, _ => ResolveAndCacheAsync(normalized, cacheKey, cancellationToken));

        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(cacheKey, out _);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> GetUserNamesBatchAsync(IEnumerable<object?> userIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        var normalizedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in userIds)
        {
            var normalized = NormalizeUserId(id);
            if (!string.IsNullOrEmpty(normalized)) normalizedIds.Add(normalized);
        }

        if (normalizedIds.Count == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var result = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var id in normalizedIds)
        {
            if (_cache.TryGetValue(GetCacheKey(id), out string? cached) && cached is not null)
                result[id] = cached;
            else
                missing.Add(id);
        }

        if (missing.Count == 0) return result;

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Batch-резолвинг: {Missing} из {Total} ID отсутствуют в кэше — резолвлю параллельно", missing.Count, normalizedIds.Count);

        var tasks = missing.Select(async id =>
        {
            await _parallelGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var name = await ResolveAndCacheAsync(id, GetCacheKey(id), cancellationToken).ConfigureAwait(false);
                result[id] = name;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось разрешить ID {UserId} в batch-режиме", id);
                result[id] = id;
            }
            finally
            {
                _parallelGate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return result;
    }

    private async Task<string> ResolveAndCacheAsync(string normalizedId, string cacheKey, CancellationToken cancellationToken)
    {
        try
        {
            string name;

            if (int.TryParse(normalizedId, out _))
                name = await ResolveByIdAsync(normalizedId, cancellationToken).ConfigureAwait(false);
            else
                name = await ResolveByLoginAsync(normalizedId, cancellationToken).ConfigureAwait(false);

            _cache.Set(cacheKey, name, TimeSpan.FromHours(CacheHours));
            return name;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось получить имя пользователя {UserId}", normalizedId);
            _cache.Set(cacheKey, normalizedId, TimeSpan.FromMinutes(NegativeCacheMinutes));
            return normalizedId;
        }
    }

    private async Task<string> ResolveByIdAsync(string id, CancellationToken cancellationToken)
    {
        var userElement = await _resolver.GetUserByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (userElement is null) return id;
        return ParseFullNameFromElement(userElement.Value) ?? id;
    }

    private async Task<string> ResolveByLoginAsync(string login, CancellationToken cancellationToken)
    {
        var users = await _resolver.SearchUsersByLoginAsync(login, cancellationToken).ConfigureAwait(false);
        if (users.Count == 0) return login;

        foreach (var user in users)
        {
            var name = user.TryGetProperty("name", out var n) ? n.GetString() : null;

            if (name is null || !name.Equals(login, StringComparison.OrdinalIgnoreCase)) continue;

            var fullName = ParseFullNameFromElement(user);
            if (!string.IsNullOrEmpty(fullName))
            {
                _cache.Set(GetCacheKey(login), fullName, TimeSpan.FromHours(CacheHours));
                return fullName;
            }
        }

        return login;
    }

    private static string? ParseFullNameFromElement(JsonElement element)
    {
        var realname = element.TryGetProperty("realname", out var r) ? r.GetString() : null;
        var firstname = element.TryGetProperty("firstname", out var f) ? f.GetString() : null;

        var fullName = $"{realname} {firstname}".Trim();
        return string.IsNullOrEmpty(fullName) ? null : fullName;
    }

    private static string NormalizeUserId(object? userId)
    {
        if (userId is null) return string.Empty;
        if (userId is System.Collections.IEnumerable and not string) return string.Empty;

        var str = userId.ToString()?.Trim();
        return string.IsNullOrEmpty(str) || str == "0" ? string.Empty : str;
    }

    private static string GetCacheKey(string normalizedId) => $"user_{normalizedId}";
}