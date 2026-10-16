namespace TicketMonitor.Core.Models;

/// <summary>Настройки отправки email через SMTP.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string SmtpServer { get; set; } = string.Empty;
    public int Port { get; set; } = 25;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;

    /// <summary>Разобранный список получателей (разделители: <c>;</c>, <c>,</c>).</summary>
    public IReadOnlyList<string> Recipients =>
        [.. To.Split([';', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(r => r.Trim())
            .Where(r => !string.IsNullOrEmpty(r))];
}