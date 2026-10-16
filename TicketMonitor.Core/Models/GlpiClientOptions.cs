namespace TicketMonitor.Core.Models;

/// <summary>Настройки для GlpiClient.</summary>
public class GlpiClientOptions
{
    public const string SectionName = "GLPI";

    public string BaseUrl { get; set; } = string.Empty;
    public string AppToken { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 30;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
    public TimeSpan RetryDelay => TimeSpan.FromSeconds(RetryDelaySeconds);
}