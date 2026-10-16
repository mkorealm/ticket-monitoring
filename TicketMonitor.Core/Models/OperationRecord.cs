using LiteDB;

namespace TicketMonitor.Core.Models;

/// <summary>Запись об одной выполненной операции (успех или ошибка).</summary>
public sealed class OperationRecord
{
    /// <summary>Уникальный ID записи.</summary>
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Тип операции в формате <c>"Namespace:Action"</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Имя сервиса-владельца (например, <c>"TicketMonitor.Api"</c>).</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Статус: <c>"Success"</c>, <c>"Failed"</c>, <c>"InProgress"</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Время начала операции.</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>Время завершения операции (может быть <c>null</c>, если операция не завершилась).</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Длительность операции в миллисекундах.</summary>
    public long? DurationMs { get; set; }

    /// <summary>Сообщение об ошибке (если операция упала). Обрезано до 2000 символов.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Стектрейс исключения (если было). Обрезан до 4000 символов.</summary>
    public string? StackTrace { get; set; }

    /// <summary>JSON-сериализованный контекст операции. Обрезан до 1000 символов.</summary>
    public string? ContextJson { get; set; }
}