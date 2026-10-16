using TicketMonitor.Core.Contracts;

namespace TicketMonitor.Core.Tests.TestHelpers;

/// <summary>
/// Заглушка <see cref="IOperationRegistry"/> для тестов: выполняет действие,
/// не пишет никаких записей. Используется везде, где реальный реестр не важен.
/// </summary>
public sealed class NoOpOperationRegistry : IOperationRegistry
{
    /// <inheritdoc />
    public Task<T> RunAsync<T>(
        string type,
        Func<CancellationToken, Task<T>> action,
        object? context = null,
        CancellationToken cancellationToken = default)
        => action(cancellationToken);

    /// <inheritdoc />
    public Task RunAsync(
        string type,
        Func<CancellationToken, Task> action,
        object? context = null,
        CancellationToken cancellationToken = default)
        => action(cancellationToken);
}