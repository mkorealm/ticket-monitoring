using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TicketMonitor.Api.Configuration;
using TicketMonitor.Core.Models.Dto;
using TicketMonitor.Core.Services;

namespace TicketMonitor.Api.Controllers;

[ApiController]
[Route("api")]
public class HealthController(HealthService healthService) : ControllerBase
{
    /// <summary>Возвращает состояние сервиса.</summary>
    [HttpGet("health")]
    [EnableRateLimiting(RateLimitingConfiguration.HealthPolicyName)]
    [ProducesResponseType(typeof(HealthStatusDto), StatusCodes.Status200OK)]
    public IActionResult GetHealth() => Ok(healthService.GetStatus());
}