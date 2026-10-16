using Microsoft.AspNetCore.Mvc;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Helpers;
using TicketMonitor.Core.Models;
using TicketMonitor.Redmine.Models;
using TicketMonitor.Redmine.Services;

namespace TicketMonitor.Redmine.Controllers;

/// <summary>Контроллер формы добавления решения в заявку GLPI.</summary>
public sealed class GlpiSolutionController(
    GlpiClient glpiClient,
    GlpiStatusUpdater glpiStatusUpdater,
    ILogger<GlpiSolutionController> logger) : Controller
{
    /// <summary>GLPI-код статуса «Решена».</summary>
    private const int GlpiStatusSolved = 5;

    /// <summary>GLPI-код статуса «Закрыта».</summary>
    private const int GlpiStatusClosed = 6;

    /// <summary>Максимальная длина текста решения.</summary>
    private const int MaxSolutionTextLength = 10000;

    private readonly GlpiClient _glpiClient = glpiClient ?? throw new ArgumentNullException(nameof(glpiClient));
    private readonly GlpiStatusUpdater _glpiStatusUpdater = glpiStatusUpdater ?? throw new ArgumentNullException(nameof(glpiStatusUpdater));
    private readonly ILogger<GlpiSolutionController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // -------------------------------------------------------------------------
    // GET /glpi/solution
    // -------------------------------------------------------------------------

    /// <summary>Рендерит форму добавления решения.</summary>
    [HttpGet("/glpi/solution")]
    public async Task<IActionResult> Solution(
        [FromQuery] string ticketId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticketId))
        {
            _logger.LogWarning("Запрос формы решения без ticketId");
            return BadRequest("Параметр ticketId обязателен");
        }

        var rawTicket = await LoadRawTicketAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (rawTicket is null)
        {
            _logger.LogWarning("Заявка #{TicketId} не найдена в GLPI", ticketId);
            return NotFound($"Заявка #{ticketId} не найдена");
        }

        var statusCode = NotificationHelper.GetInt(rawTicket, "status") ?? 0;

        if (statusCode is GlpiStatusSolved or GlpiStatusClosed)
        {
            _logger.LogWarning(
                "Заявка #{TicketId} уже в статусе {StatusId} — форма решения не показывается",
                ticketId, statusCode);
            return Conflict($"Заявка #{ticketId} уже решена или закрыта");
        }

        var viewModel = BuildViewModel(ticketId, rawTicket, statusCode, solutionText: null, error: null);
        return View("Solution", viewModel);
    }

    // -------------------------------------------------------------------------
    // POST /glpi/solution
    // -------------------------------------------------------------------------

    /// <summary>Обрабатывает submit формы — добавляет followup и меняет статус заявки на «Решена».</summary>
    [HttpPost("/glpi/solution")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Solution(
        [FromForm] GlpiSolutionSubmitModel form,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form.TicketId))
        {
            _logger.LogWarning("Submit формы решения с пустым TicketId");
            return BadRequest("Параметр TicketId обязателен");
        }

        if (string.IsNullOrWhiteSpace(form.SolutionText))
        {
            _logger.LogWarning("Submit формы решения с пустым текстом для заявки #{TicketId}", form.TicketId);
            return BadRequest("Текст решения обязателен");
        }

        if (form.SolutionText.Length > MaxSolutionTextLength)
        {
            _logger.LogWarning(
                "Submit формы решения для заявки #{TicketId}: текст слишком длинный ({Length} симв.)",
                form.TicketId, form.SolutionText.Length);
            return BadRequest($"Текст решения не должен превышать {MaxSolutionTextLength} символов");
        }

        var rawTicket = await LoadRawTicketAsync(form.TicketId, cancellationToken).ConfigureAwait(false);
        if (rawTicket is null)
        {
            _logger.LogWarning("Submit формы решения: заявка #{TicketId} не найдена в GLPI", form.TicketId);
            return NotFound($"Заявка #{form.TicketId} не найдена");
        }

        var statusCode = NotificationHelper.GetInt(rawTicket, "status") ?? 0;

        if (statusCode is GlpiStatusSolved or GlpiStatusClosed)
        {
            _logger.LogWarning(
                "Submit формы решения: заявка #{TicketId} уже в статусе {StatusId}",
                form.TicketId, statusCode);
            return Conflict($"Заявка #{form.TicketId} уже решена или закрыта");
        }

        var followupAdded = await _glpiStatusUpdater
            .AddFollowupAsync(form.TicketId, form.SolutionText, cancellationToken)
            .ConfigureAwait(false);

        if (!followupAdded)
        {
            _logger.LogError("Не удалось добавить followup к заявке #{TicketId}", form.TicketId);

            var errorViewModel = BuildViewModel(
                form.TicketId, rawTicket, statusCode, form.SolutionText,
                "Не удалось добавить решение в GLPI. Попробуйте позже или добавьте решение вручную.");
            return View("Solution", errorViewModel);
        }

        var statusChanged = await _glpiStatusUpdater
            .SetGlpiStatusAsync(form.TicketId, GlpiStatusSolved, cancellationToken)
            .ConfigureAwait(false);

        if (!statusChanged)
        {
            _logger.LogWarning(
                "Заявка #{TicketId}: followup добавлен, но статус изменить не удалось. " +
                "Сотрудник ТП должен сменить статус вручную",
                form.TicketId);
        }

        var ticketUrl = BuildTicketUrl(form.TicketId);
        return Redirect(ticketUrl);
    }

    // -------------------------------------------------------------------------
    // Private
    // -------------------------------------------------------------------------

    private async Task<Dictionary<string, object?>?> LoadRawTicketAsync(
        string ticketId,
        CancellationToken cancellationToken)
    {
        try
        {
            var ticket = await _glpiClient.GetTicketAsync(ticketId, cancellationToken).ConfigureAwait(false);
            return ticket.Count == 0 ? null : ticket;
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

    private static GlpiSolutionFormViewModel BuildViewModel(
        string ticketId,
        Dictionary<string, object?> rawTicket,
        int statusCode,
        string? solutionText,
        string? error)
    {
        return new GlpiSolutionFormViewModel
        {
            Ticket = MapToTicketInfo(ticketId, rawTicket),
            StatusCode = statusCode,
            SolutionText = solutionText,
            Error = error
        };
    }

    private static TicketInfo MapToTicketInfo(string ticketId, Dictionary<string, object?> raw)
    {
        var statusCode = NotificationHelper.GetInt(raw, "status") ?? 0;
        var statusText = NotificationHelper.StatusMap.TryGetValue(statusCode, out var s)
            ? s
            : statusCode.ToString();

        var priorityCode = NotificationHelper.GetInt(raw, "priority") ?? 0;
        var priorityText = NotificationHelper.PriorityMap.TryGetValue(priorityCode, out var p)
            ? p
            : priorityCode.ToString();

        return new TicketInfo
        {
            Id = ticketId,
            Title = NotificationHelper.GetString(raw, "name") ?? string.Empty,
            Status = statusText,
            Priority = priorityText,
            OpenDate = ParseDate(NotificationHelper.GetString(raw, "date")),
            Initiator = NotificationHelper.GetString(raw, "users_id_recipient") ?? string.Empty,
            Assignee = NotificationHelper.GetString(raw, "users_id_assign") ?? string.Empty,
            Category = NotificationHelper.GetString(raw, "itilcategories_id") ?? string.Empty,
            LastModified = ParseDate(NotificationHelper.GetString(raw, "date_mod")),
            Url = BuildTicketUrl(ticketId)
        };
    }

    private static string BuildTicketUrl(string ticketId)
        => $"https://glpi.example.ru/front/ticket.form.php?id={ticketId}";

    private static DateTime ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return default;
        return DateTime.TryParse(s, out var d) ? d : default;
    }
}