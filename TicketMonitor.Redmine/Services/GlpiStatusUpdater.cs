using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Redmine.Models;

namespace TicketMonitor.Redmine.Services;

/// <summary>Сервис обновления статуса заявки в GLPI по событиям из Redmine.</summary>
public sealed class GlpiStatusUpdater(
    GlpiClient glpiClient,
    IOperationRegistry operationRegistry,
    ILogger<GlpiStatusUpdater> logger)
{
    private static readonly Dictionary<int, int> RedmineToGlpiStatusMap = new()
    {
        [3] = 5,
        [5] = 6,
        [6] = 4,
        [11] = 5,
        [12] = 6
    };

    private readonly GlpiClient _glpiClient = glpiClient ?? throw new ArgumentNullException(nameof(glpiClient));
    private readonly IOperationRegistry _operationRegistry = operationRegistry ?? throw new ArgumentNullException(nameof(operationRegistry));
    private readonly ILogger<GlpiStatusUpdater> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Обновляет статус заявки GLPI по маппингу Redmine → GLPI.</summary>
    public async Task<bool> UpdateStatusAsync(
        string glpiTicketId,
        int redmineStatusId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(glpiTicketId);

        if (!RedmineToGlpiStatusMap.TryGetValue(redmineStatusId, out var glpiStatusId))
        {
            _logger.LogWarning(
                "Нет маппинга Redmine статуса {RedmineStatusId} → GLPI. Пропускаю обновление для заявки #{TicketId}",
                redmineStatusId, glpiTicketId);
            return false;
        }

        return await SetTicketStatusAsync(glpiTicketId, glpiStatusId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Устанавливает GLPI статус напрямую (без маппинга).</summary>
    public async Task<bool> SetGlpiStatusAsync(
        string glpiTicketId,
        int glpiStatusId,
        CancellationToken cancellationToken = default)
    {
        return await SetTicketStatusAsync(glpiTicketId, glpiStatusId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Дополняет поле «Внешний ID» (externalid) заявки GLPI новым ID через запятую.</summary>
    public async Task<bool> AppendExternalIdAsync(
        string glpiTicketId,
        string newExternalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(glpiTicketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newExternalId);

        string? oldExternalId;

        try
        {
            var ticket = await _glpiClient.GetTicketAsync(glpiTicketId, cancellationToken).ConfigureAwait(false);
            oldExternalId = ticket.TryGetValue("externalid", out var ext) ? ext?.ToString() : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Не удалось прочитать текущий externalid заявки GLPI #{TicketId} — записываю только новый ID",
                glpiTicketId);
            oldExternalId = null;
        }

        var finalExternalId = string.IsNullOrWhiteSpace(oldExternalId)
            ? newExternalId
            : $"{oldExternalId.Trim()}, {newExternalId}";

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation(
                "Заявка GLPI #{TicketId}: externalid '{Old}' -> '{New}'",
                glpiTicketId, oldExternalId ?? "(пусто)", finalExternalId);

        return await SetExternalIdAsync(glpiTicketId, finalExternalId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Устанавливает поле «Внешний ID» (externalid) в заявке GLPI.</summary>
    public async Task<bool> SetExternalIdAsync(
        string glpiTicketId,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(glpiTicketId);

        if (string.IsNullOrWhiteSpace(externalId))
        {
            _logger.LogWarning("Пустой externalId для заявки #{TicketId} — пропускаю", glpiTicketId);
            return false;
        }

        return await _operationRegistry.RunAsync(
            "GLPI:SetExternalId",
            async ct =>
            {
                try
                {
                    var url = $"apirest.php/Ticket/{glpiTicketId}";
                    var payload = new
                    {
                        input = new
                        {
                            id = int.Parse(glpiTicketId),
                            externalid = externalId
                        }
                    };

                    var json = JsonSerializer.Serialize(payload);

                    using var request = _glpiClient.CreateRequest(HttpMethod.Put, url);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var response = await _glpiClient.SendWithSessionRetryAsync(request, ct).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        _logger.LogError(
                            "GLPI вернул {StatusCode} при установке externalid заявки #{TicketId}. Ответ: {Error}",
                            response.StatusCode, glpiTicketId, error);
                        return false;
                    }

                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation(
                            "Заявке GLPI #{TicketId} установлен externalid = {ExternalId}",
                            glpiTicketId, externalId);

                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при установке externalid заявки GLPI #{TicketId}", glpiTicketId);
                    return false;
                }
            },
            context: new { glpiTicketId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> SetTicketStatusAsync(
        string glpiTicketId,
        int glpiStatusId,
        CancellationToken cancellationToken)
    {
        return await _operationRegistry.RunAsync(
            "GLPI:SetStatus",
            async ct =>
            {
                try
                {
                    var url = $"apirest.php/Ticket/{glpiTicketId}";
                    var payload = new
                    {
                        input = new
                        {
                            id = int.Parse(glpiTicketId),
                            status = glpiStatusId
                        }
                    };

                    var json = JsonSerializer.Serialize(payload);

                    using var request = _glpiClient.CreateRequest(HttpMethod.Put, url);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var response = await _glpiClient.SendWithSessionRetryAsync(request, ct).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        _logger.LogError(
                            "GLPI вернул {StatusCode} при обновлении статуса заявки #{TicketId}. Ответ: {Error}",
                            response.StatusCode, glpiTicketId, error);
                        return false;
                    }

                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation(
                            "Статус заявки GLPI #{TicketId} обновлён на {StatusId}",
                            glpiTicketId, glpiStatusId);

                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при обновлении статуса заявки GLPI #{TicketId}", glpiTicketId);
                    return false;
                }
            },
            context: new { glpiTicketId, glpiStatusId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Добавляет followup (комментарий) к заявке GLPI.
    /// Возвращает <c>true</c>, если followup успешно создан.
    /// </summary>
    public async Task<bool> AddFollowupAsync(
        string glpiTicketId,
        string comment,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(glpiTicketId);

        if (string.IsNullOrWhiteSpace(comment))
        {
            _logger.LogWarning("Пустой текст followup для заявки #{TicketId} — пропускаю", glpiTicketId);
            return false;
        }

        return await _operationRegistry.RunAsync(
            "GLPI:AddFollowup",
            async ct =>
            {
                try
                {
                    var url = $"apirest.php/Ticket/{glpiTicketId}/ITILFollowup";
                    var payload = new
                    {
                        input = new
                        {
                            itemtype = "Ticket",
                            items_id = int.Parse(glpiTicketId),
                            content = comment
                        }
                    };

                    var json = JsonSerializer.Serialize(payload);

                    using var request = _glpiClient.CreateRequest(HttpMethod.Post, url);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var response = await _glpiClient.SendWithSessionRetryAsync(request, ct).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        _logger.LogWarning(
                            "GLPI вернул {StatusCode} при добавлении followup к заявке #{TicketId}. Ответ: {Error}",
                            response.StatusCode, glpiTicketId, error);
                        return false;
                    }

                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation("Followup добавлен к заявке GLPI #{TicketId}", glpiTicketId);

                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при добавлении followup к заявке GLPI #{TicketId}", glpiTicketId);
                    return false;
                }
            },
            context: new { glpiTicketId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Возвращает GLPI-статус по Redmine-статусу или <c>null</c>.</summary>
    public static int? MapRedmineToGlpi(int redmineStatusId)
        => RedmineToGlpiStatusMap.TryGetValue(redmineStatusId, out var glpiStatusId)
            ? glpiStatusId
            : null;
}