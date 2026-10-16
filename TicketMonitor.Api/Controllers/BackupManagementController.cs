using Microsoft.AspNetCore.Mvc;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models.Dto;

namespace TicketMonitor.Api.Controllers;

[ApiController]
[Route("api/backups")]
public class BackupManagementController(IBackupService backupService) : ControllerBase
{
    /// <summary>Возвращает список всех доступных точек резервного копирования.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public IActionResult GetAvailableBackups() => Ok(backupService.GetAvailableBackups());

    /// <summary>Запускает ручное создание резервной копии баз данных.</summary>
    [HttpPost("create")]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateBackup(CancellationToken cancellationToken)
    {
        try
        {
            var fileName = await backupService.CreateBackupAsync(cancellationToken).ConfigureAwait(false);
            return Ok(ApiResponseDto.Ok($"Резервная копия {fileName} успешно создана"));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponseDto.Fail($"Ошибка создания бэкапа: {ex.Message}"));
        }
    }

    /// <summary>Восстанавливает базы данных системы из указанной резервной копии.</summary>
    [HttpPost("restore")]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RestoreFromBackup([FromQuery] string fileName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return BadRequest(ApiResponseDto.Fail("Имя файла бэкапа обязательно к заполнению"));

        var success = await backupService.RestoreFromBackupAsync(fileName, cancellationToken).ConfigureAwait(false);
        return success
            ? Ok(ApiResponseDto.Ok("Данные системы успешно восстановлены из резервной копии"))
            : BadRequest(ApiResponseDto.Fail($"Не удалось восстановить систему из файла {fileName}. Проверьте логи сервера."));
    }
}