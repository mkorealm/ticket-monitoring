using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Helpers;
using TicketMonitor.Core.Models;
using TicketMonitor.Redmine.Clients;
using TicketMonitor.Redmine.Models;
using TicketMonitor.Redmine.Services;

namespace TicketMonitor.Redmine.Controllers;

/// <summary>Контроллер формы создания задачи в Redmine.</summary>
public sealed class RedmineController(
    IRedmineClient redmineClient,
    RedmineLinkRepository linkRepository,
    GlpiStatusUpdater glpiStatusUpdater,
    GlpiClient glpiClient,
    IOptions<RedmineOptions> options,
    ILogger<RedmineController> logger) : Controller
{
    private readonly IRedmineClient _redmineClient = redmineClient ?? throw new ArgumentNullException(nameof(redmineClient));
    private readonly RedmineLinkRepository _linkRepository = linkRepository ?? throw new ArgumentNullException(nameof(linkRepository));
    private readonly GlpiStatusUpdater _glpiStatusUpdater = glpiStatusUpdater ?? throw new ArgumentNullException(nameof(glpiStatusUpdater));
    private readonly GlpiClient _glpiClient = glpiClient ?? throw new ArgumentNullException(nameof(glpiClient));
    private readonly RedmineOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<RedmineController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // -------------------------------------------------------------------------
    // GET /redmine/create
    // -------------------------------------------------------------------------

    /// <summary>Рендерит форму создания задачи.</summary>
    [HttpGet("/redmine/create")]
    public async Task<IActionResult> Create(
        [FromQuery] string ticketId,
        [FromQuery] int? projectId,
        [FromQuery] bool forceCreate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticketId))
        {
            _logger.LogWarning("Запрос формы без ticketId");
            return BadRequest("Параметр ticketId обязателен");
        }

        var ticket = await FindTicketAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null)
        {
            _logger.LogWarning("Заявка #{TicketId} не найдена в GLPI", ticketId);
            return NotFound($"Заявка #{ticketId} не найдена");
        }

        var existingLink = await FindExistingLinkAsync(ticketId, cancellationToken).ConfigureAwait(false);

        if (existingLink is not null && !forceCreate)
        {
            var externalId = await TryReadExternalIdAsync(ticketId, cancellationToken).ConfigureAwait(false);

            var alreadyVm = new RedmineFormViewModel
            {
                Ticket = ticket,
                IsAlreadyLinked = true,
                ExistingRedmineIssueId = existingLink.RedmineIssueId,
                ExistingRedmineIssueUrl = _options.GetIssueUrl(existingLink.RedmineIssueId),
                ExistingGlpiExternalId = externalId
            };
            return View("AlreadyLinked", alreadyVm);
        }

        var selectedProjectId = projectId ?? _options.DefaultProjectId;
        var viewModel = await BuildViewModelAsync(ticket, selectedProjectId, forceCreate, cancellationToken).ConfigureAwait(false);
        return View("Create", viewModel);
    }

    // -------------------------------------------------------------------------
    // POST /redmine/create
    // -------------------------------------------------------------------------

    /// <summary>
    /// Обрабатывает submit формы — создаёт задачу в Redmine.
    /// При <c>ForceCreate=true</c> игнорирует существующую связь и создаёт вторую задачу.
    /// </summary>
    [HttpPost("/redmine/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromForm] RedmineFormSubmitModel form,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("Форма содержит ошибки валидации: {Errors}",
                string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
            return BadRequest("Некорректные данные формы");
        }

        var ticket = await FindTicketAsync(form.GlpiTicketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null)
            return NotFound($"Заявка #{form.GlpiTicketId} не найдена");

        var existingLink = await FindExistingLinkAsync(form.GlpiTicketId, cancellationToken).ConfigureAwait(false);

        if (existingLink is not null && !form.ForceCreate)
        {
            _logger.LogWarning("Заявка #{TicketId} уже связана с задачей Redmine #{IssueId} (без ForceCreate)",
                form.GlpiTicketId, existingLink.RedmineIssueId);
            return Redirect(_options.GetIssueUrl(existingLink.RedmineIssueId));
        }

        var isForcedRecreation = existingLink is not null && form.ForceCreate;
        if (isForcedRecreation && _logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation(
                "Заявка #{TicketId}: принудительное повторное создание задачи (старая Redmine #{OldIssueId})",
                form.GlpiTicketId, existingLink!.RedmineIssueId);

        var request = new RedmineCreateRequest
        {
            GlpiTicketId = form.GlpiTicketId,
            ProjectId = form.ProjectId,
            TrackerId = form.TrackerId,
            Subject = form.Subject,
            Description = form.Description ?? string.Empty,
            AssignedToId = form.AssignedToId,
            CustomFields = ParseCustomFields(form.CustomFields)
        };

        try
        {
            var issue = await _redmineClient.CreateIssueAsync(request, cancellationToken).ConfigureAwait(false);

            SaveLinkSafe(form.GlpiTicketId, issue.Id, issue.Key);

            await _glpiStatusUpdater
                .SetGlpiStatusAsync(form.GlpiTicketId, 3, cancellationToken)
                .ConfigureAwait(false);

            var newIssueId = issue.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (isForcedRecreation)
            {
                await _glpiStatusUpdater
                    .AppendExternalIdAsync(form.GlpiTicketId, newIssueId, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _glpiStatusUpdater
                    .SetExternalIdAsync(form.GlpiTicketId, newIssueId, cancellationToken)
                    .ConfigureAwait(false);
            }

            return Redirect(_options.GetIssueUrl(issue.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось создать задачу Redmine по заявке GLPI #{TicketId}", form.GlpiTicketId);
            ModelState.AddModelError(string.Empty, $"Не удалось создать задачу: {ex.Message}");
            return View("Create", await BuildViewModelAsync(ticket, form.ProjectId, form.ForceCreate, cancellationToken).ConfigureAwait(false));
        }
    }

    // -------------------------------------------------------------------------
    // Private
    // -------------------------------------------------------------------------

    private async Task<RedmineFormViewModel> BuildViewModelAsync(
    TicketInfo ticket,
    int projectId,
    bool forceCreate,
    CancellationToken cancellationToken)
    {
        var allProjects = await _redmineClient.GetProjectsAsync(cancellationToken).ConfigureAwait(false);

        var allowedIds = _options.AllowedProjectIds.ToHashSet();
        var projects = allProjects
            .Where(p => allowedIds.Contains(p.Id))
            .ToList();

        if (!allowedIds.Contains(projectId))
            projectId = _options.DefaultProjectId;

        var trackers = await _redmineClient.GetTrackersAsync(projectId, cancellationToken).ConfigureAwait(false);
        var memberships = await _redmineClient.GetProjectMembershipsAsync(projectId, cancellationToken).ConfigureAwait(false);

        // Выбираем трекер по умолчанию: сначала DefaultTrackerId, иначе — первый доступный.
        // Используем индексатор вместо LINQ FirstOrDefault(), т.к. IReadOnlyList индексируется напрямую.
        var selectedTrackerId = 0;
        for (var i = 0; i < trackers.Count; i++)
        {
            if (trackers[i].Id == _options.DefaultTrackerId)
            {
                selectedTrackerId = trackers[i].Id;
                break;
            }
        }

        if (selectedTrackerId == 0 && trackers.Count > 0)
            selectedTrackerId = trackers[0].Id;

        return new RedmineFormViewModel
        {
            Ticket = ticket,
            Projects = projects,
            Trackers = trackers,
            Memberships = memberships,
            SelectedProjectId = projectId,
            SelectedTrackerId = selectedTrackerId,
            ForceCreate = forceCreate
        };
    }

    /// <summary>
    /// Cache-aside: сначала ищем связь в локальном кэше (redmine_links.db),
    /// потом в Redmine (источник истины). Если нашли в Redmine — кэшируем.
    /// </summary>
    private async Task<RedmineLink?> FindExistingLinkAsync(string ticketId, CancellationToken cancellationToken)
    {
        try
        {
            var cached = _linkRepository.GetByTicketId(ticketId);
            if (cached is not null)
                return cached;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось прочитать кэш связей для GLPI #{TicketId} — иду в Redmine", ticketId);
        }

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
            _logger.LogWarning(ex, "Не удалось найти задачу в Redmine для GLPI #{TicketId}", ticketId);
            return null;
        }

        if (issue is null)
            return null;

        var link = new RedmineLink
        {
            TicketId = ticketId,
            RedmineIssueId = issue.Id,
            RedmineIssueKey = issue.Key ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "cache-aside"
        };

        SaveLinkSafe(ticketId, issue.Id, issue.Key);

        return link;
    }

    private void SaveLinkSafe(string ticketId, int issueId, string? issueKey)
    {
        try
        {
            _linkRepository.Save(new RedmineLink
            {
                TicketId = ticketId,
                RedmineIssueId = issueId,
                RedmineIssueKey = issueKey ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "web-form"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Не удалось сохранить связь GLPI #{TicketId} → Redmine #{IssueId} в кэш",
                ticketId, issueId);
        }
    }

    private async Task<string?> TryReadExternalIdAsync(string glpiTicketId, CancellationToken cancellationToken)
    {
        try
        {
            var ticket = await _glpiClient.GetTicketAsync(glpiTicketId, cancellationToken).ConfigureAwait(false);
            return ticket.TryGetValue("externalid", out var ext) ? ext?.ToString() : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось прочитать externalid заявки #{TicketId}", glpiTicketId);
            return null;
        }
    }

    private async Task<TicketInfo?> FindTicketAsync(string ticketId, CancellationToken cancellationToken)
    {
        try
        {
            var ticket = await _glpiClient.GetTicketAsync(ticketId, cancellationToken).ConfigureAwait(false);
            if (ticket is null || ticket.Count == 0)
                return null;

            return MapToTicketInfo(ticketId, ticket);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось получить заявку #{TicketId} из GLPI", ticketId);
            return null;
        }
    }

    private static TicketInfo MapToTicketInfo(string ticketId, Dictionary<string, object?> raw)
    {
        var statusCode = NotificationHelper.GetInt(raw, "status") ?? 0;
        var statusText = NotificationHelper.StatusMap.TryGetValue(statusCode, out var s) ? s : statusCode.ToString();

        var priorityCode = NotificationHelper.GetInt(raw, "priority") ?? 0;
        var priorityText = NotificationHelper.PriorityMap.TryGetValue(priorityCode, out var p) ? p : priorityCode.ToString();

        var title = NotificationHelper.GetString(raw, "name") ?? string.Empty;

        return new TicketInfo
        {
            Id = ticketId,
            Title = title,
            Status = statusText,
            Priority = priorityText,
            OpenDate = ParseDate(NotificationHelper.GetString(raw, "date")),
            Initiator = NotificationHelper.GetString(raw, "users_id_recipient") ?? string.Empty,
            Assignee = NotificationHelper.GetString(raw, "users_id_assign") ?? string.Empty,
            Category = NotificationHelper.GetString(raw, "itilcategories_id") ?? string.Empty,
            LastModified = ParseDate(NotificationHelper.GetString(raw, "date_mod")),
            Url = $"https://glpi.example.ru/front/ticket.form.php?id={ticketId}"
        };
    }

    private static DateTime ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return default;
        return DateTime.TryParse(s, out var d) ? d : default;
    }

    private static Dictionary<int, string> ParseCustomFields(Dictionary<string, string>? formFields)
    {
        var result = new Dictionary<int, string>();
        if (formFields is null)
            return result;

        foreach (var (key, value) in formFields)
        {
            if (int.TryParse(key, out var id) && !string.IsNullOrEmpty(value))
                result[id] = value;
        }

        return result;
    }
}