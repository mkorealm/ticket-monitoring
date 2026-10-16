using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Services;

/// <summary>Сервис управления резервными копиями баз данных TicketMonitor.</summary>
public sealed class BackupService : IBackupService
{
    private const string BackupFilePattern = "backup_*.zip";
    private const string DataFilePattern = "*.db";

    private readonly IMonitorManagementService _monitorManagementService;
    private readonly BackupOptions _options;
    private readonly IOperationRegistry _operationRegistry;
    private readonly ILogger<BackupService> _logger;
    private readonly string _dataFolder;
    private readonly string _backupFolder;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Инициализирует сервис резервного копирования.</summary>
    public BackupService(
        IMonitorManagementService monitorManagementService,
        IOptions<BackupOptions> options,
        IOperationRegistry operationRegistry,
        ILogger<BackupService> logger)
    {
        _monitorManagementService = monitorManagementService ?? throw new ArgumentNullException(nameof(monitorManagementService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _operationRegistry = operationRegistry ?? throw new ArgumentNullException(nameof(operationRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _dataFolder = string.IsNullOrWhiteSpace(_options.DataFolder)
            ? Path.Combine(AppContext.BaseDirectory, "Data")
            : _options.DataFolder;

        _backupFolder = string.IsNullOrWhiteSpace(_options.BackupFolder)
            ? Path.Combine(AppContext.BaseDirectory, "Backups")
            : _options.BackupFolder;

        Directory.CreateDirectory(_backupFolder);
    }

    /// <inheritdoc />
    public async Task<string> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _operationRegistry.RunAsync(
                "Backup:Create",
                ct => Task.Run(() => CreateBackupCore(ct), ct),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> RestoreFromBackupAsync(string backupFileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFileName);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _operationRegistry.RunAsync(
                "Backup:Restore",
                ct => Task.Run(() => RestoreCore(backupFileName, ct), ct),
                context: new { backupFileName },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAvailableBackups()
    {
        if (!_gate.Wait(0))
        {
            _logger.LogDebug("GetAvailableBackups вызван во время активной операции — возвращаю текущий снимок");
        }
        else
        {
            _gate.Release();
        }

        if (!Directory.Exists(_backupFolder))
            return [];

        try
        {
            return [.. Directory.GetFiles(_backupFolder, BackupFilePattern)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .OrderByDescending(name => name, StringComparer.Ordinal)
                .Cast<string>()];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    private string CreateBackupCore(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Запущена процедура создания резервной копии");

        if (!Directory.Exists(_dataFolder))
            throw new DirectoryNotFoundException($"Директория с данными не найдена: {_dataFolder}");

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var tempZipPath = Path.Combine(_backupFolder, $"backup_{timestamp}_{uniqueSuffix}.tmp.zip");
        var finalZipPath = Path.Combine(_backupFolder, $"backup_{timestamp}_{uniqueSuffix}.zip");

        try
        {
            using (var zipArchive = ZipFile.Open(tempZipPath, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.GetFiles(_dataFolder, DataFilePattern))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fileName = Path.GetFileName(file);
                    zipArchive.CreateEntryFromFile(file, fileName, CompressionLevel.Optimal);
                }
            }

            if (File.Exists(finalZipPath))
                File.Delete(finalZipPath);

            File.Move(tempZipPath, finalZipPath);

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Резервная копия успешно создана: {Path}", finalZipPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании резервной копии баз данных");
            TryDeleteFile(tempZipPath);
            throw;
        }

        ApplyRetentionPolicy();
        return Path.GetFileName(finalZipPath);
    }

    private bool RestoreCore(string backupFileName, CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Запущена процедура восстановления из резервной копии: {FileName}", backupFileName);

        var backupFilePath = Path.Combine(_backupFolder, backupFileName);
        if (!File.Exists(backupFilePath))
        {
            _logger.LogWarning("Файл резервной копии не найден: {Path}", backupFilePath);
            return false;
        }

        try
        {
            StopNonSystemMonitors();
            PrepareDataFolderForRestore(cancellationToken);

            _logger.LogInformation("Распаковка файлов резервной копии...");
            ZipFile.ExtractToDirectory(backupFilePath, _dataFolder, overwriteFiles: true);

            _logger.LogInformation("Перезагрузка конфигураций мониторов из восстановленной БД...");
            _monitorManagementService.LoadMonitorsFromDatabase();

            _logger.LogInformation("Система успешно восстановлена из бэкапа");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Критическая ошибка в процессе восстановления данных из бэкапа. Система может находиться в нестабильном состоянии");
            return false;
        }
    }

    private void StopNonSystemMonitors()
    {
        _logger.LogInformation("Остановка активных несистемных мониторов перед восстановлением...");
        foreach (var monitor in _monitorManagementService.GetAllMonitors())
        {
            if (monitor.IsSystem) continue;
            _monitorManagementService.RemoveMonitor(monitor.Name);
        }
    }

    private void PrepareDataFolderForRestore(CancellationToken cancellationToken)
    {
        if (Directory.Exists(_dataFolder))
        {
            foreach (var file in Directory.GetFiles(_dataFolder, DataFilePattern))
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(file);
            }
        }
        else
        {
            Directory.CreateDirectory(_dataFolder);
        }
    }

    private void ApplyRetentionPolicy()
    {
        try
        {
            var files = Directory.GetFiles(_backupFolder, BackupFilePattern)
                .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                .ToList();

            if (files.Count <= _options.RetentionCount)
                return;

            var toDelete = files.Count - _options.RetentionCount;
            for (var i = 0; i < toDelete; i++)
            {
                File.Delete(files[i]);
                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("Удалён старый файл резервной копии: {Path}", files[i]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось применить правила хранения бэкапов");
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось удалить временный файл {Path}", path);
        }
    }
}