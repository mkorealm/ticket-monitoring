using Microsoft.AspNetCore.Mvc;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Models.Dto;

namespace TicketMonitor.Api.Controllers;

[ApiController]
[Route("api/monitors")]
public class MonitorManagementController(IMonitorManagementService monitorManagementService) : ControllerBase
{
    /// <summary>Ключи в Settings, значения которых нельзя отдавать через API.</summary>
    private static readonly HashSet<string> SensitiveSettingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "WebhookUrl",
        "Token",
        "ApiToken",
        "Password",
        "Secret",
        "ApiKey"
    };

    private const string MaskedValue = "***";

    /// <summary>Возвращает список всех мониторов (секреты замаскированы).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MonitorConfig>), StatusCodes.Status200OK)]
    public IActionResult GetAllMonitors()
    {
        var monitors = monitorManagementService.GetAllMonitors()
            .Select(CloneWithMaskedSettings)
            .ToList();
        return Ok(monitors);
    }

    /// <summary>Возвращает монитор по имени (секреты замаскированы).</summary>
    [HttpGet("{monitorName}")]
    [ProducesResponseType(typeof(MonitorConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status404NotFound)]
    public IActionResult GetMonitor(string monitorName)
    {
        var monitor = monitorManagementService.GetMonitor(monitorName);
        if (monitor is null)
            return NotFound(ApiResponseDto.Fail($"Монитор '{monitorName}' не найден"));

        return Ok(CloneWithMaskedSettings(monitor));
    }

    /// <summary>Добавляет новый монитор и запускает его.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status400BadRequest)]
    public IActionResult AddMonitor([FromBody] MonitorConfig config)
    {
        var validationError = ValidateMonitor(config);
        if (validationError is not null)
            return BadRequest(ApiResponseDto.Fail(validationError));

        return monitorManagementService.AddMonitor(config)
            ? Ok(ApiResponseDto.Ok($"Монитор '{config.Name}' добавлен и запущен"))
            : BadRequest(ApiResponseDto.Fail($"Монитор '{config.Name}' уже существует"));
    }

    /// <summary>Обновляет монитор.</summary>
    [HttpPut("{monitorName}")]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status404NotFound)]
    public IActionResult UpdateMonitor(string monitorName, [FromBody] MonitorConfig config)
    {
        var validationError = ValidateMonitor(config);
        if (validationError is not null)
            return BadRequest(ApiResponseDto.Fail(validationError));

        return monitorManagementService.UpdateMonitor(monitorName, config)
            ? Ok(ApiResponseDto.Ok($"Монитор '{monitorName}' обновлён"))
            : NotFound(ApiResponseDto.Fail($"Монитор '{monitorName}' не найден"));
    }

    /// <summary>Удаляет монитор.</summary>
    [HttpDelete("{monitorName}")]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status404NotFound)]
    public IActionResult RemoveMonitor(string monitorName) =>
        monitorManagementService.RemoveMonitor(monitorName)
            ? Ok(ApiResponseDto.Ok($"Монитор '{monitorName}' удалён"))
            : NotFound(ApiResponseDto.Fail($"Монитор '{monitorName}' не найден"));

    /// <summary>Запускает ручную проверку для монитора.</summary>
    [HttpPost("{monitorName}/check")]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RunCheck(string monitorName)
    {
        var result = await monitorManagementService.RunCheckAsync(monitorName).ConfigureAwait(false);
        return result
            ? Ok(ApiResponseDto.Ok("Проверка выполнена"))
            : NotFound(ApiResponseDto.Fail($"Монитор '{monitorName}' не найден или проверка не удалась"));
    }

    /// <summary>Возвращает статус монитора.</summary>
    [HttpGet("{monitorName}/status")]
    [ProducesResponseType(typeof(MonitorStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status404NotFound)]
    public IActionResult GetStatus(string monitorName)
    {
        var status = monitorManagementService.GetStatus(monitorName);
        return status is null
            ? NotFound(ApiResponseDto.Fail($"Монитор '{monitorName}' не найден"))
            : Ok(status);
    }

    private static MonitorConfig CloneWithMaskedSettings(MonitorConfig source)
    {
        return new MonitorConfig
        {
            Name = source.Name,
            CategoryFilter = source.CategoryFilter,
            CheckIntervalMinutes = source.CheckIntervalMinutes,
            DatabaseFileName = source.DatabaseFileName,
            SupportGroupId = source.SupportGroupId,
            IsSystem = source.IsSystem,
            Notification = new NotificationChannelsConfig
            {
                Channels = [.. source.Notification.Channels.Select(c => new NotificationChannelConfig
            {
                Type = c.Type,
                Enabled = c.Enabled,
                Settings = c.Settings.ToDictionary(
                    kvp => kvp.Key,
                    kvp => SensitiveSettingKeys.Contains(kvp.Key) ? MaskedValue : kvp.Value)
            })]
            }
        };
    }

    private static string? ValidateMonitor(MonitorConfig? config)
    {
        if (config is null) return "Тело запроса обязательно";
        if (string.IsNullOrWhiteSpace(config.Name)) return "Name обязателен";
        if (string.IsNullOrWhiteSpace(config.CategoryFilter)) return "CategoryFilter обязателен";
        if (config.CheckIntervalMinutes <= 0) return "CheckIntervalMinutes должен быть больше 0";
        if (string.IsNullOrWhiteSpace(config.DatabaseFileName)) return "DatabaseFileName обязателен";
        if (config.IsSystem) return "Нельзя создать системный монитор через API";
        return null;
    }
}