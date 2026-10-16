using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Contracts;

/// <summary>Интерфейс сервиса получения заявок.</summary>
public interface ITicketService
{
    Task<List<TicketInfo>> GetTicketsByCategoryAsync(string categoryFilter, CancellationToken cancellationToken = default);
}