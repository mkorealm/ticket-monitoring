namespace TicketMonitor.Core.Models;

/// <summary>Настройки реестра операций.</summary>
public sealed class OperationOptions
{
    public const string SectionName = "Operations";

    /// <summary>Имя сервиса-владельца (например, <c>"TicketMonitor.Api"</c>).</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Имя файла LiteDB-базы с операциями (в папке <c>Data/</c>).</summary>
    public string DatabaseFileName { get; set; } = "operations.db";

    /// <summary>Сколько дней хранить записи операций. Старше — удаляются фоновым сервисом.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Максимальная длина <c>ErrorMessage</c> (символов).</summary>
    public int MaxErrorMessageLength { get; set; } = 2000;

    /// <summary>Максимальная длина <c>StackTrace</c> (символов).</summary>
    public int MaxStackTraceLength { get; set; } = 4000;

    /// <summary>Максимальная длина <c>ContextJson</c> (символов).</summary>
    public int MaxContextJsonLength { get; set; } = 1000;
}