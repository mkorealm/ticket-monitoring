using Microsoft.AspNetCore.Mvc;
using TicketMonitor.Redmine.Clients;
using TicketMonitor.Redmine.Models;
using TicketMonitor.Redmine.Services;

namespace TicketMonitor.Redmine.Controllers;

/// <summary>
/// API-эндпоинты для загрузки справочников Redmine и проверки связей заявок GLPI.
/// </summary>
[ApiController]
[Route("api/redmine")]
public sealed class RedmineApiController(
    IRedmineClient redmineClient,
    RedmineLinkRepository linkRepository,
    ILogger<RedmineApiController> logger) : ControllerBase
{
    private readonly IRedmineClient _redmineClient = redmineClient ?? throw new ArgumentNullException(nameof(redmineClient));
    private readonly RedmineLinkRepository _linkRepository = linkRepository ?? throw new ArgumentNullException(nameof(linkRepository));
    private readonly ILogger<RedmineApiController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // -------------------------------------------------------------------------
    // Справочники
    // -------------------------------------------------------------------------

    /// <summary>Список проектов Redmine.</summary>
    [HttpGet("projects")]
    [ProducesResponseType(typeof(IReadOnlyList<RedmineProject>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProjects(CancellationToken cancellationToken)
    {
        var projects = await _redmineClient
            .GetProjectsAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(projects);
    }

    /// <summary>Трекеры указанного проекта.</summary>
    [HttpGet("projects/{projectId:int}/trackers")]
    [ProducesResponseType(typeof(IReadOnlyList<RedmineTracker>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTrackers(int projectId, CancellationToken cancellationToken)
    {
        var trackers = await _redmineClient
            .GetTrackersAsync(projectId, cancellationToken)
            .ConfigureAwait(false);

        return Ok(trackers);
    }

    /// <summary>Custom fields указанного проекта.</summary>
    [HttpGet("projects/{projectId:int}/custom-fields")]
    [ProducesResponseType(typeof(IReadOnlyList<RedmineCustomField>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCustomFields(int projectId, CancellationToken cancellationToken)
    {
        var customFields = await _redmineClient
            .GetCustomFieldsAsync(projectId, cancellationToken)
            .ConfigureAwait(false);

        return Ok(customFields);
    }

    /// <summary>Участники указанного проекта.</summary>
    [HttpGet("projects/{projectId:int}/memberships")]
    [ProducesResponseType(typeof(IReadOnlyList<RedmineMembership>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberships(int projectId, CancellationToken cancellationToken)
    {
        var memberships = await _redmineClient
            .GetProjectMembershipsAsync(projectId, cancellationToken)
            .ConfigureAwait(false);

        return Ok(memberships);
    }

    // -------------------------------------------------------------------------
    // Проверка связей (cache-aside: redmine_links.db — кэш, Redmine — источник истины)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Возвращает признак, что по заявке GLPI уже создана задача в Redmine.
    /// </summary>
    /// <remarks>
    /// Схема <b>cache-aside</b>:
    /// <list type="number">
    ///   <item>Сначала ищем связь в локальном кэше <c>redmine_links.db</c>.</item>
    ///   <item>Если в кэше нет — идём в Redmine (источник истины) и ищем задачу по
    ///     custom field «Номер в HD» = <paramref name="ticketId"/>.</item>
    ///   <item>Если нашли в Redmine — сохраняем в кэш и возвращаем <c>linked: true</c>.</item>
    ///   <item>Если не нашли — возвращаем <c>linked: false</c> (negative cache держит
    ///     <c>TicketMonitor.Api</c> через <c>RedmineLinkChecker</c>).</item>
    /// </list>
    /// </remarks>
    /// <param name="ticketId">ID заявки GLPI.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><c>{ "linked": true/false }</c>.</returns>
    [HttpGet("links/{ticketId}/exists")]
    [ProducesResponseType(typeof(RedmineLinkStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LinkExists(string ticketId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticketId))
        {
            _logger.LogWarning("LinkExists: пустой ticketId");
            return BadRequest(new { error = "ticketId обязателен" });
        }

        // 1. Кэш: redmine_links.db
        var cached = _linkRepository.GetByTicketId(ticketId);
        if (cached is not null)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("LinkExists (кэш): GLPI #{TicketId} → Redmine #{IssueId}", ticketId, cached.RedmineIssueId);

            return Ok(new RedmineLinkStatusResponse { Linked = true });
        }

        // 2. Источник истины: Redmine
        RedmineIssue? issue;
        try
        {
            issue = await _redmineClient
                .FindIssueByGlpiTicketIdAsync(ticketId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "LinkExists: не удалось проверить связь в Redmine для GLPI #{TicketId} — считаю, что связи нет",
                ticketId);

            return Ok(new RedmineLinkStatusResponse { Linked = false });
        }

        if (issue is null)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("LinkExists (Redmine): GLPI #{TicketId} — задачи нет", ticketId);

            return Ok(new RedmineLinkStatusResponse { Linked = false });
        }

        // 3. Нашли — кэшируем в redmine_links.db
        try
        {
            _linkRepository.Save(new RedmineLink
            {
                TicketId = ticketId,
                RedmineIssueId = issue.Id,
                RedmineIssueKey = issue.Key ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "cache-aside"
            });

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation(
                    "LinkExists: связь GLPI #{TicketId} → Redmine #{IssueId} найдена в Redmine и закэширована",
                    ticketId, issue.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "LinkExists: не удалось закэшировать связь GLPI #{TicketId} → Redmine #{IssueId}",
                ticketId, issue.Id);
        }

        return Ok(new RedmineLinkStatusResponse { Linked = true });
    }
}

/// <summary>Ответ с признаком наличия связи заявки GLPI с задачей Redmine.</summary>
public sealed record RedmineLinkStatusResponse
{
    /// <summary><c>true</c>, если связь существует.</summary>
    public bool Linked { get; init; }
}