namespace TicketMonitor.Core.Contracts;

/// <summary>Проверка доступности внешнего сервиса (GLPI, RocketChat, Redmine).</summary>
public interface IExternalServiceProbe
{
    /// <summary>Имя проверяемого сервиса.</summary>
    string ServiceName { get; }

    /// <summary>Проверяет доступность сервиса.</summary>
    Task<ProbeResult> ProbeAsync(CancellationToken cancellationToken = default);
}

/// <summary>Результат проверки.</summary>
public sealed record ProbeResult
{
    /// <summary>Сервис доступен.</summary>
    public bool Healthy { get; init; }

    /// <summary>Время ответа (мс).</summary>
    public double ResponseTimeMs { get; init; }

    /// <summary>Текст ошибки, если сервис недоступен.</summary>
    public string? ErrorMessage { get; init; }
}