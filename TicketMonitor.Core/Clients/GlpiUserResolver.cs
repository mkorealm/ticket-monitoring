using System.Text.Json;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Contracts;

namespace TicketMonitor.Core.Clients;

/// <summary>Реализация <see cref="IGlpiUserResolver"/> поверх <see cref="GlpiClient"/>.</summary>
public sealed class GlpiUserResolver(
    GlpiClient glpiClient,
    IOperationRegistry operationRegistry,
    ILogger<GlpiUserResolver> logger) : IGlpiUserResolver
{
    private readonly GlpiClient _glpiClient = glpiClient ?? throw new ArgumentNullException(nameof(glpiClient));
    private readonly IOperationRegistry _operationRegistry = operationRegistry ?? throw new ArgumentNullException(nameof(operationRegistry));
    private readonly ILogger<GlpiUserResolver> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<JsonElement?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return await _operationRegistry.RunAsync(
            "UserCache:ResolveById",
            async ct =>
            {
                try
                {
                    var url = $"apirest.php/User/{userId}";
                    using var request = _glpiClient.CreateRequest(HttpMethod.Get, url);
                    using var response = await _glpiClient.SendWithSessionRetryAsync(request, ct).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        if (_logger.IsEnabled(LogLevel.Debug))
                            _logger.LogDebug("GLPI вернул {StatusCode} для пользователя {UserId}", response.StatusCode, userId);
                        return (JsonElement?)null;
                    }

                    var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    return doc.RootElement.Clone();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Ошибка парсинга JSON пользователя {UserId}", userId);
                    return (JsonElement?)null;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось получить пользователя {UserId}", userId);
                    return (JsonElement?)null;
                }
            },
            context: new { userId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JsonElement>> SearchUsersByLoginAsync(string login, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);

        return await _operationRegistry.RunAsync(
            "UserCache:SearchByLogin",
            async ct =>
            {
                try
                {
                    var url = $"apirest.php/User?searchText={Uri.EscapeDataString(login)}&range=0-100";
                    using var request = _glpiClient.CreateRequest(HttpMethod.Get, url);
                    using var response = await _glpiClient.SendWithSessionRetryAsync(request, ct).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        if (_logger.IsEnabled(LogLevel.Debug))
                            _logger.LogDebug("GLPI вернул {StatusCode} при поиске '{Login}'", response.StatusCode, login);
                        return (IReadOnlyList<JsonElement>)[];
                    }

                    var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.ValueKind != JsonValueKind.Array)
                        return (IReadOnlyList<JsonElement>)[];

                    var result = new List<JsonElement>();
                    foreach (var element in doc.RootElement.EnumerateArray())
                        result.Add(element.Clone());

                    return result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Ошибка парсинга JSON при поиске '{Login}'", login);
                    return (IReadOnlyList<JsonElement>)[];
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось выполнить поиск пользователей по '{Login}'", login);
                    return (IReadOnlyList<JsonElement>)[];
                }
            },
            context: new { login },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}