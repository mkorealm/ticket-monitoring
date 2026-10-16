using Xunit;

namespace TicketMonitor.Watchdog.Tests.TestHelpers;

/// <summary>
/// Collection для тестов, использующих один файл <c>watchdog.db</c>.
/// Отключает параллелизм, чтобы тесты не мешали друг другу.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WatchdogDbCollection
{
    /// <summary>Имя коллекции.</summary>
    public const string Name = "Watchdog DB";
}