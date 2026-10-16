namespace TicketMonitor.Redmine.Models;

/// <summary>Модель submit-формы добавления решения в заявку GLPI.</summary>
public sealed class GlpiSolutionSubmitModel
{
    /// <summary>ID заявки GLPI, по которой добавляется решение.</summary>
    public string TicketId { get; set; } = string.Empty;

    /// <summary>Текст решения.</summary>
    public string SolutionText { get; set; } = string.Empty;
}