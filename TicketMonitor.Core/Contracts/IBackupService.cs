namespace TicketMonitor.Core.Contracts;

/// <summary>Интерфейс сервиса резервного копирования и восстановления данных.</summary>
public interface IBackupService
{
    /// <summary>Создает резервную копию файлов базы данных.</summary>
    Task<string> CreateBackupAsync(CancellationToken cancellationToken = default);

    /// <summary>Восстанавливает базы данных из указанного файла бэкапа.</summary>
    Task<bool> RestoreFromBackupAsync(string backupFileName, CancellationToken cancellationToken = default);

    /// <summary>Возвращает список имен доступных файлов резервных копий.</summary>
    IReadOnlyList<string> GetAvailableBackups();
}