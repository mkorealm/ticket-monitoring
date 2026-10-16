using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Contracts;

/// <summary>Интерфейс репозитория заявок.</summary>
public interface ITicketRepository
{
    void SaveTickets(List<TicketInfo> tickets, string dbFileName);
    IReadOnlyList<TicketInfo> LoadTickets(string dbFileName);
}