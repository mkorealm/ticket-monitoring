using Microsoft.AspNetCore.Mvc;
using TicketMonitor.Api.Models;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models.Dto;
using TicketMonitor.Core.Services;

namespace TicketMonitor.Api.Controllers;

[ApiController]
[Route("api/monitors")]
public class MonitorsController(
    ChangesStoreService changesStore,
    IMonitorManagementService monitorManagementService,
    GlpiClient glpiClient,
    IGlpiUserCacheService userCacheService,
    NotificationDispatcherService dispatcherService,
    ITicketRepository ticketRepository) : ControllerBase
{
    /// <summary>Возвращает изменения по конкретному монитору (без очистки).</summary>
    [HttpGet("{monitorName}/changes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetChanges(string monitorName) => Ok(changesStore.GetChanges(monitorName));

    /// <summary>Возвращает изменения и очищает хранилище для монитора.</summary>
    [HttpGet("{monitorName}/changes/consume")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult ConsumeChanges(string monitorName) => Ok(changesStore.ConsumeChanges(monitorName));

    /// <summary>Отправляет уведомление по заявке указанного монитора.</summary>
    [HttpPost("{monitorName}/notify/{ticketId}")]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendNotification(
        [FromRoute] string monitorName,
        [FromRoute] string ticketId,
        [FromBody] NotificationRequest? request = null)
    {
        var monitor = monitorManagementService.GetMonitor(monitorName);
        if (monitor is null)
            return NotFound(ApiResponseDto.Fail($"Монитор '{monitorName}' не найден"));

        var tickets = ticketRepository.LoadTickets(monitor.DatabaseFileName);
        var ticket = tickets.FirstOrDefault(t => t.Id == ticketId);
        if (ticket is null)
            return NotFound(ApiResponseDto.Fail($"Заявка #{ticketId} не найдена в мониторе '{monitorName}'"));

        var result = await dispatcherService.SendManualNotificationAsync(
            ticketId, request?.Targets, monitor.SupportGroupId, monitor).ConfigureAwait(false);

        return result
            ? Ok(ApiResponseDto.Ok($"Уведомление по заявке #{ticketId} отправлено"))
            : BadRequest(ApiResponseDto.Fail("Не удалось отправить уведомление"));
    }

    /// <summary>Возвращает все заявки монитора с базовым набором полей.</summary>
    [HttpGet("{monitorName}/tickets")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTickets(string monitorName)
    {
        var monitor = monitorManagementService.GetMonitor(monitorName);
        if (monitor is null)
            return NotFound(ApiResponseDto.Fail($"Монитор '{monitorName}' не найден"));

        try
        {
            var tickets = await glpiClient.GetAllTicketsCompactAsync(monitor.CategoryFilter).ConfigureAwait(false);
            await EnrichTicketNamesAsync(tickets).ConfigureAwait(false);
            return Ok(tickets);
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponseDto.Fail($"Не удалось получить заявки: {ex.Message}"));
        }
    }

    /// <summary>Возвращает все заявки монитора со всеми полями.</summary>
    [HttpGet("{monitorName}/tickets/full")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTicketsFull(string monitorName)
    {
        var monitor = monitorManagementService.GetMonitor(monitorName);
        if (monitor is null)
            return NotFound(ApiResponseDto.Fail($"Монитор '{monitorName}' не найден"));

        try
        {
            var tickets = await glpiClient.GetAllTicketsFullAsync(monitor.CategoryFilter).ConfigureAwait(false);
            await EnrichTicketNamesAsync(tickets).ConfigureAwait(false);
            return Ok(tickets);
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponseDto.Fail($"Не удалось получить заявки: {ex.Message}"));
        }
    }

    /// <summary>Возвращает конкретную заявку монитора.</summary>
    [HttpGet("{monitorName}/tickets/{ticketId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTicket(string monitorName, string ticketId)
    {
        var monitor = monitorManagementService.GetMonitor(monitorName);
        if (monitor is null)
            return NotFound(ApiResponseDto.Fail($"Монитор '{monitorName}' не найден"));

        try
        {
            var ticket = await glpiClient.GetTicketAsync(ticketId).ConfigureAwait(false);

            if (ticket.TryGetValue("users_id_recipient", out var recipient) && recipient is not null)
            {
                var name = await userCacheService.GetUserNameAsync(recipient).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(name)) ticket["users_id_recipient"] = name;
            }

            if (ticket.TryGetValue("users_id_lastupdater", out var updater) && updater is not null)
            {
                var name = await userCacheService.GetUserNameAsync(updater).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(name)) ticket["users_id_lastupdater"] = name;
            }

            return Ok(ticket);
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponseDto.Fail($"Не удалось получить заявку {ticketId}: {ex.Message}"));
        }
    }

    private async Task EnrichTicketNamesAsync(List<Dictionary<string, object?>> tickets)
    {
        foreach (var ticket in tickets)
        {
            if (ticket.TryGetValue("4", out var initiatorId) && initiatorId is not null)
            {
                var name = await userCacheService.GetUserNameAsync(initiatorId).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(name)) ticket["4"] = name;
            }

            if (ticket.TryGetValue("5", out var assigneeId) && assigneeId is not null)
            {
                var name = await userCacheService.GetUserNameAsync(assigneeId).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(name)) ticket["5"] = name;
            }
        }
    }
}