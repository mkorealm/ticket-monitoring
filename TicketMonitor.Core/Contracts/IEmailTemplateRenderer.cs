using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Contracts;

/// <summary>Рендерер HTML-шаблона email-уведомления.</summary>
public interface IEmailTemplateRenderer
{
    /// <summary>Рендерит HTML-письмо по шаблону, подставляя поля <paramref name="data"/>.</summary>
    Task<string> RenderAsync(EmailMessageData data, string plainTextFallback, CancellationToken cancellationToken = default);
}