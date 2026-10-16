using TicketMonitor.Core.Contracts;

namespace TicketMonitor.Redmine.Tests.TestHelpers;

/// <summary>
/// Заглушка <see cref="IOperationRegistry"/>: выполняет действие, ничего не пишет.
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