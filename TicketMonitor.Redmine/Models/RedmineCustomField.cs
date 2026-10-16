namespace TicketMonitor.Redmine.Models;

/// <summary>Custom field проекта Redmine (для динамической формы).</summary>
public sealed record RedmineCustomField
{
    /// <summary>ID поля.</summary>
    public int Id { get; init; }

    /// <summary>Название поля.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Формат: <c>string</c>, <c>int</c>, <c>bool</c>, <c>list</c>, <c>date</c>, <c>link</c>.</summary>
    public string FieldFormat { get; init; } = string.Empty;

    /// <summary>Обязательное ли поле.</summary>
    public bool IsRequired { get; init; }

    /// <summary>Допустимые значения (для <c>field_format = list</c>).</summary>
    public IReadOnlyList<string> PossibleValues { get; init; } = [];
}