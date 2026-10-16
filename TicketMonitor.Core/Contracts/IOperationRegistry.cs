namespace TicketMonitor.Core.Contracts;

/// <summary>
/// Реестр операций: оборачивает внешние вызовы (HTTP, БД, отправка) и пишет
/// структурированную запись о результате в собственную БД сервиса.
/// Используется Watchdog'ом для агрегации статистики без парсинга логов.
/// </summary>
public interface IOperationRegistry
{
    /// <summary>
    /// Выполняет операцию, регистрирует результат и возвращает значение.
    /// </summary>
    /// <typeparam name="T">Тип возвращаемого значения.</typeparam>
    /// <param name="type">Тип операции в формате <c>"Namespace:Action"</c> (например, <c>"GLPI:FetchTicket"</c>).</param>
    /// <param name="action">Действие для выполнения.</param>
    /// <param name="context">Опциональный контекст (например, <c>new { ticketId = "12345" }</c>). Сериализуется в JSON.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<T> RunAsync<T>(
        string type,
        Func<CancellationToken, Task<T>> action,
        object? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Выполняет операцию без возвращаемого значения, регистрирует результат.
    /// </summary>
    /// <param name="type">Тип операции в формате <c>"Namespace:Action"</c>.</param>
    /// <param name="action">Действие для выполнения.</param>
    /// <param name="context">Опциональный контекст.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task RunAsync(
        string type,
        Func<CancellationToken, Task> action,
        object? context = null,
        CancellationToken cancellationToken = default);
}