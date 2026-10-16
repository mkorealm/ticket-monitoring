namespace TicketMonitor.Redmine.Models;

/// <summary>Проект Redmine (для dropdown).</summary>
public sealed record RedmineProject
{
    /// <summary>ID проекта.</summary>
    public int Id { get; init; }

    /// <summary>Название проекта.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Идентификатор проекта (латиница, например <c>dv-its-asuo</c>).</summary>
    public string Identifier { get; init; } = string.Empty;

    /// <summary>ID родительского проекта (если есть).</summary>
    public int? ParentId { get; init; }
}