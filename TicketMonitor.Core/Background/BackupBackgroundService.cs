using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Background;

/// <summary>Фоновый сервис автоматического периодического создания резервных копий.</summary>
public sealed class BackupBackgroundService(
    IBackupService backupService,
    IOptions<BackupOptions> options,
    ILogger<BackupBackgroundService> logger) : BackgroundService
{
    private readonly IBackupService _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
    private readonly IOptions<BackupOptions> _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<BackupBackgroundService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backupOptions = _options.Value;

        if (!backupOptions.Enabled)
        {
            _logger.LogInformation("Автоматическое резервное копирование отключено в конфигурации");
            return;
        }

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Запущен фоновый сервис автоматического резервного копирования с интервалом {Interval} ч.", backupOptions.IntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(backupOptions.IntervalHours), stoppingToken).ConfigureAwait(false);
                await _backupService.CreateBackupAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Произошла ошибка в цикле автоматического резервного копирования");
            }
        }
    }
}