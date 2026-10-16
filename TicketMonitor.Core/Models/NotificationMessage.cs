namespace TicketMonitor.Core.Models;

/// <summary>Типизированное представление уведомления.</summary>
public sealed record NotificationMessage
{
    public required string Subject { get; init; }
    public required NotificationKind Kind { get; init; }
    public required TicketInfo Ticket { get; init; }
    public string? Description { get; init; }

    /// <summary>Показанные ответы (уже обрезаны до лимита).</summary>
    public IReadOnlyList<NotificationAnswer> Answers { get; init; } = [];

    /// <summary>Общее количество новых ответов (до обрезки).</summary>
    public int TotalAnswersCount { get; init; }

    public IReadOnlyList<string> Deltas { get; init; } = [];

    /// <summary>Ссылки-действия в уведомлении (может быть 0, 1 или несколько).</summary>
    public IReadOnlyList<TicketActionLink> ActionLinks { get; init; } = [];
}

/// <summary>Ссылка-действие в уведомлении.</summary>
public sealed record TicketActionLink
{
    public required string Label { get; init; }
    public required string Url { get; init; }
}

/// <summary>Тип уведомления.</summary>
public enum NotificationKind
{
    New,
    Updated,
    Manual
}

/// <summary>Один ответ по заявке.</summary>
public sealed record NotificationAnswer
{
    public required string Author { get; init; }
    public required string Text { get; init; }
}