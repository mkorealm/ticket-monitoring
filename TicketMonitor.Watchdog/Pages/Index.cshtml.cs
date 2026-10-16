using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TicketMonitor.Watchdog.Pages;

/// <summary>
/// Модель страницы статистики Watchdog.
/// Данные подгружаются на клиенте через fetch к <c>/api/watchdog/*</c>.
/// </summary>
public sealed class IndexModel : PageModel
{
    /// <summary>Пустой обработчик GET — вся логика на клиенте.</summary>
    public void OnGet()
    {
    }
}