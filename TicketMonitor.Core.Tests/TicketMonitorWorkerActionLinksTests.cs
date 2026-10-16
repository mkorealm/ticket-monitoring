using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TicketMonitor.Core.Background;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Services;
using Xunit;

namespace TicketMonitor.Core.Tests;

/// <summary>
/// Smoke-тесты <see cref="TicketMonitorWorker"/>.
/// Полная логика выбора кнопок (<c>BuildActionLinksAsync</c>) — <c>private</c>,
/// тестируется через тесты форматеров и интеграционные прогоны.
/// </summary>
public sealed class TicketMonitorWorkerActionLinksTests
{
    [Fact]
    public void GetStatus_ReturnsConfiguredMonitorMetadata()
    {
        var worker = CreateWorker();

        var status = worker.GetStatus();

        status.Name.Should().Be("TestMonitor");
        status.Category.Should().Be("TestCategory");
        status.IntervalMinutes.Should().Be(5);
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var worker = CreateWorker();

        var act = () =>
        {
            worker.Dispose();
            worker.Dispose();
        };

        act.Should().NotThrow();
    }

    private static TicketMonitorWorker CreateWorker()
    {
        var config = new MonitorConfig
        {
            Name = "TestMonitor",
            CategoryFilter = "TestCategory",
            CheckIntervalMinutes = 5,
            DatabaseFileName = "test.db",
            SupportGroupId = 3
        };

        return new TicketMonitorWorker(
            config,
            new FakeTicketService(),
            new FakeTicketRepository(),
            new FakeChangeDetector(),
            new FakeChannelResolver(),
            dispatcherService: null!,
            new ChangesStoreService(),
            new HealthService(),
            detailsCollector: null!,
            new FakeRedmineLinkChecker(),
            NullLogger<TicketMonitorBackgroundService>.Instance);
    }

    private sealed class FakeTicketService : ITicketService
    {
        public Task<List<TicketInfo>> GetTicketsByCategoryAsync(string categoryFilter, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<TicketInfo>());
    }

    private sealed class FakeTicketRepository : ITicketRepository
    {
        public void SaveTickets(List<TicketInfo> tickets, string dbFileName) { }
        public IReadOnlyList<TicketInfo> LoadTickets(string dbFileName) => [];
    }

    private sealed class FakeChangeDetector : ITicketChangeDetector
    {
        public IReadOnlyList<TicketChange> Detect(IReadOnlyList<TicketInfo> previous, IReadOnlyList<TicketInfo> current) => [];
    }

    private sealed class FakeChannelResolver : INotificationChannelResolver
    {
        public IReadOnlyList<INotificationService> ResolveForMonitor(MonitorConfig monitorConfig) => [];
        public IReadOnlyList<INotificationService> ResolveByTargets(IReadOnlyList<string>? targets) => [];
        public bool ContainsRocketChat(IReadOnlyList<INotificationService> services) => false;
        public RocketChatNotificationService GetRocketChatService() => throw new InvalidOperationException();
    }

    private sealed class FakeRedmineLinkChecker : IRedmineLinkChecker
    {
        public Task<bool> IsLinkedAsync(string glpiTicketId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}