using TicketMonitor.Core.Models;

namespace TicketMonitor.Redmine.Models;

/// <summary>ViewModel формы добавления решения в заявку GLPI.</summary>
public sealed class GlpiSolutionFormViewModel
{
    /// <summary>Заявка GLPI, по которой добавляется решение.</summary>
    public required TicketInfo Ticket { get; init; }

    /// <summary>
    /// Код статуса заявки GLPI (1 — Новая, 2 — В работе (назначена), 3 — В работе (запланирована),
    /// 4 — Ожидание, 5 — Решена, 6 — Закрыта, 10 — Согласование).
    /// </summary>
    public int StatusCode { get; init; }

    /// <summary>Введённый текст решения (для повторного рендера формы при ошибке).</summary>
    public string? SolutionText { get; init; }

    /// <summary>Сообщение об ошибке, если submit не удался.</summary>
    public string? Error { get; init; }

    /// <summary>URL заявки в GLPI (для кнопки «Отмена»).</summary>
    public string GlpiTicketUrl => Ticket.Url;

    /// <summary>
    /// Признак, что заявка находится в статусе «Согласование» (10).
    /// В этом случае GLPI при добавлении решения сам переводит заявку в «Решена»,
    /// поэтому показываем предупреждение.
    /// </summary>
    public bool ShowPendingWarning => StatusCode == 10;
}