namespace TicketMonitor.Core.Models;

/// <summary>Конфигурация параметров резервного копирования.</summary>
public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    public bool Enabled { get; set; } = true;
    public double IntervalHours { get; set; } = 24;
    public int RetentionCount { get; set; } = 10;
    public string? DataFolder { get; set; }
    public string? BackupFolder { get; set; }
}