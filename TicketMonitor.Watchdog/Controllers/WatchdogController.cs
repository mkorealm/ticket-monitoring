using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TicketMonitor.Watchdog.Configuration;
using TicketMonitor.Watchdog.Models;
using TicketMonitor.Watchdog.Services;

namespace TicketMonitor.Watchdog.Controllers;

/// <summary>JSON-эндпоинты Watchdog.</summary>
[ApiController]
[Route("api/watchdog")]
public sealed class WatchdogController(
    WatchdogRepository repository,
    OperationTypeDisplayNameProvider displayNameProvider,
    IOptions<WatchdogOptions> options) : ControllerBase
{
    private readonly WatchdogRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly OperationTypeDisplayNameProvider _displayNameProvider = displayNameProvider ?? throw new ArgumentNullException(nameof(displayNameProvider));
    private readonly WatchdogOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    // -------------------------------------------------------------------------
    // Health самого watchdog
    // -------------------------------------------------------------------------

    /// <summary>Простой health-эндпоинт Watchdog.</summary>
    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Health() => Ok(new
    {
        status = "running",
        utcNow = DateTime.UtcNow,
        services = _options.Services.Count
    });

    // -------------------------------------------------------------------------
    // Список сервисов
    // -------------------------------------------------------------------------

    /// <summary>Возвращает список отслеживаемых сервисов.</summary>
    [HttpGet("services")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetServices() => Ok(_options.Services.Select(s => new
    {
        s.Name,
        s.DisplayName,
        s.HealthUrl,
        s.BasePath,
        s.OperationsDbFile
    }));

    // -------------------------------------------------------------------------
    // Список типов операций
    // -------------------------------------------------------------------------

    /// <summary>Возвращает список известных типов операций с человекочитаемыми именами.</summary>
    [HttpGet("operation-types")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetOperationTypes()
    {
        // Возвращаем уникальные типы, которые есть в БД за последние 90 дней.
        var from = DateTime.UtcNow.Date.AddDays(-_options.OperationsAggregateRetentionDays);
        var to = DateTime.UtcNow.Date;
        var stats = _repository.GetDailyOperationStats(from, to);

        var types = stats
            .Select(x => x.Type)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(t => new { Type = t, DisplayName = _displayNameProvider.GetDisplayName(t) })
            .ToList();

        return Ok(types);
    }

    // -------------------------------------------------------------------------
    // Статистика за день
    // -------------------------------------------------------------------------

    /// <summary>Статистика за сегодня.</summary>
    [HttpGet("stats/today")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetStatsToday() => Ok(BuildStats(DateTime.UtcNow.Date, DateTime.UtcNow.Date));

    /// <summary>Статистика за вчера.</summary>
    [HttpGet("stats/yesterday")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetStatsYesterday()
    {
        var yesterday = DateTime.UtcNow.Date.AddDays(-1);
        return Ok(BuildStats(yesterday, yesterday));
    }

    /// <summary>Статистика за 7 дней.</summary>
    [HttpGet("stats/week")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetStatsWeek()
    {
        var to = DateTime.UtcNow.Date;
        var from = to.AddDays(-6);
        return Ok(BuildStats(from, to));
    }

    /// <summary>Статистика за 30 дней.</summary>
    [HttpGet("stats/month")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetStatsMonth()
    {
        var to = DateTime.UtcNow.Date;
        var from = to.AddDays(-29);
        return Ok(BuildStats(from, to));
    }

    /// <summary>Статистика за произвольный период.</summary>
    [HttpGet("stats/range")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult GetStatsRange([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        if (from > to)
            return BadRequest(new { error = "from не может быть позже to" });

        return Ok(BuildStats(from.Date, to.Date));
    }

    // -------------------------------------------------------------------------
    // Простое (для отладки)
    // -------------------------------------------------------------------------

    /// <summary>Сырые события падения за период.</summary>
    [HttpGet("downtimes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetDowntimes([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var fromUtc = (from ?? DateTime.UtcNow.Date.AddDays(-7)).Date;
        var toUtc = (to ?? DateTime.UtcNow.Date).Date.AddDays(1);

        var events = _repository.GetDowntimes(fromUtc, toUtc).Select(x => new
        {
            x.Id,
            x.ServiceName,
            x.StartTime,
            x.EndTime,
            x.DurationMinutes,
            x.Reason
        });

        return Ok(events);
    }

    // -------------------------------------------------------------------------
    // Private
    // -------------------------------------------------------------------------

    private object BuildStats(DateTime fromDate, DateTime toDate)
    {
        var http = _repository.GetDailyHttpStats(fromDate, toDate);
        var operations = _repository.GetDailyOperationStats(fromDate, toDate);

        var services = _options.Services
            .Select(config =>
            {
                var serviceHttp = http.Where(x => x.ServiceName == config.Name).ToList();
                var serviceOps = operations.Where(x => x.ServiceName == config.Name).ToList();

                var totalChecks = serviceHttp.Sum(x => x.TotalChecks);
                var failedChecks = serviceHttp.Sum(x => x.FailedChecks);
                var totalResponseMs = serviceHttp.Sum(x => x.TotalResponseTimeMs);
                var totalDowntimeMinutes = serviceHttp.Sum(x => x.TotalDowntimeMinutes);

                var uptimePercent = totalChecks > 0
                    ? Math.Round((double)(totalChecks - failedChecks) / totalChecks * 100, 2)
                    : 100.0;

                var avgResponseMs = totalChecks > failedChecks
                    ? Math.Round(totalResponseMs / (totalChecks - failedChecks), 1)
                    : 0.0;

                // Агрегаты операций: группируем по Type, считаем Success / Failed / Cancelled.
                var byType = serviceOps
                    .GroupBy(x => x.Type)
                    .Select(g => new
                    {
                        Type = g.Key,
                        DisplayName = _displayNameProvider.GetDisplayName(g.Key),
                        SuccessCount = g.Where(x => x.Status == "Success").Sum(x => x.Count),
                        FailedCount = g.Where(x => x.Status == "Failed").Sum(x => x.Count),
                        CancelledCount = g.Where(x => x.Status == "Cancelled").Sum(x => x.Count),
                        TotalDurationMs = g.Sum(x => x.TotalDurationMs),
                        TotalCount = g.Sum(x => x.Count)
                    })
                    .OrderByDescending(x => x.TotalCount)
                    .ToList();

                return new
                {
                    Name = config.Name,
                    DisplayName = config.DisplayName,
                    Http = new
                    {
                        TotalChecks = totalChecks,
                        FailedChecks = failedChecks,
                        UptimePercent = uptimePercent,
                        AvgResponseMs = avgResponseMs,
                        TotalDowntimeMinutes = Math.Round(totalDowntimeMinutes, 1)
                    },
                    Operations = new
                    {
                        TotalCount = serviceOps.Sum(x => x.Count),
                        FailedCount = serviceOps.Where(x => x.Status == "Failed").Sum(x => x.Count),
                        CancelledCount = serviceOps.Where(x => x.Status == "Cancelled").Sum(x => x.Count),
                        ByType = byType
                    }
                };
            })
            .ToList();

        return new
        {
            From = fromDate,
            To = toDate,
            GeneratedAt = DateTime.UtcNow,
            Services = services
        };
    }
}