using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Contracts;

/// <summary>Детектор изменений между двумя снимками заявок.</summary>
public interface ITicketChangeDetector
{
    /// <summary>Сравнивает два снимка заявок и возвращает список изменений.</summary>
    IReadOnlyList<TicketChange> Detect(IReadOnlyList<TicketInfo> previous, IReadOnlyList<TicketInfo> current);
}