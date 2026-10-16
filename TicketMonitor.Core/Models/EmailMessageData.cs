namespace TicketMonitor.Core.Models;

/// <summary>Данные для подстановки в HTML-шаблон email.</summary>
public sealed record EmailMessageData
{
    public string TicketId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Initiator { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public string OpenDate { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Answers { get; init; } = string.Empty;

    /// <summary>
    /// HTML-разметка кнопки-действия (например, «Создать задачу в Redmine»).
    /// Если пусто — блок не выводится.
    /// </summary>
    public string ActionButton { get; init; } = string.Empty;
}