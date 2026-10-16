using TicketMonitor.Core.Clients;

namespace TicketMonitor.Core.Clients;

/// <summary>Настройки клиента проверки связей с Redmine.</summary>
public sealed class RedmineLinkCheckerOptions
{
    public const string SectionName = "RedmineLinkChecker";

    /// <summary>Базовый URL Redmine-сервиса (например, <c>http://localhost:8082</c>).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Таймаут HTTP-запроса (сек).</summary>
    public int TimeoutSeconds { get; set; } = 5;
}