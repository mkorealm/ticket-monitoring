using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TicketMonitor.Core.Models;
using TicketMonitor.Watchdog.Models;
using TicketMonitor.Watchdog.Services;
using Xunit;

namespace TicketMonitor.Watchdog.Tests;

/// <summary>Юнит-тесты <see cref="OperationsAggregator"/>.</summary>
public sealed class OperationsAggregatorTests
{
    [Fact]
    public void Aggregate_EmptyList_ReturnsZero()
    {
        var repo = new FakeWatchdogRepository();
        var sut = CreateSut(repo);

        var result = sut.Aggregate("Api", []);

        result.Should().Be(0);
        repo.Increments.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_NullServiceName_Throws()
    {
        var repo = new FakeWatchdogRepository();
        var sut = CreateSut(repo);

        var act = () => sut.Aggregate("", []);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Aggregate_SingleOperation_CountsOnce()
    {
        var repo = new FakeWatchdogRepository();
        var sut = CreateSut(repo);

        var ops = new[]
        {
            MakeOp("GLPI:FetchTicket", "Success", 100)
        };

        var result = sut.Aggregate("Api", ops);

        result.Should().Be(1);
        repo.Increments.Should().ContainSingle();
        var inc = repo.Increments[0];
        inc.ServiceName.Should().Be("Api");
        inc.Type.Should().Be("GLPI:FetchTicket");
        inc.Status.Should().Be("Success");
        inc.Count.Should().Be(1);
        inc.TotalMs.Should().Be(100);
    }

    [Fact]
    public void Aggregate_SameTypeAndStatus_GroupsTogether()
    {
        var repo = new FakeWatchdogRepository();
        var sut = CreateSut(repo);

        var ops = new[]
        {
            MakeOp("GLPI:FetchTicket", "Success", 100),
            MakeOp("GLPI:FetchTicket", "Success", 200),
            MakeOp("GLPI:FetchTicket", "Success", 300)
        };

        sut.Aggregate("Api", ops);

        repo.Increments.Should().ContainSingle();
        repo.Increments[0].Count.Should().Be(3);
        repo.Increments[0].TotalMs.Should().Be(600);
    }

    [Fact]
    public void Aggregate_DifferentStatuses_SeparateGroups()
    {
        var repo = new FakeWatchdogRepository();
        var sut = CreateSut(repo);

        var ops = new[]
        {
            MakeOp("GLPI:FetchTicket", "Success", 100),
            MakeOp("GLPI:FetchTicket", "Failed", 200),
            MakeOp("GLPI:FetchTicket", "Cancelled", 50)
        };

        sut.Aggregate("Api", ops);

        repo.Increments.Should().HaveCount(3);
        repo.Increments.Should().Contain(x => x.Status == "Success" && x.Count == 1 && x.TotalMs == 100);
        repo.Increments.Should().Contain(x => x.Status == "Failed" && x.Count == 1 && x.TotalMs == 200);
        repo.Increments.Should().Contain(x => x.Status == "Cancelled" && x.Count == 1 && x.TotalMs == 50);
    }

    [Fact]
    public void Aggregate_EmptyType_ReplacedWithUnknown()
    {
        var repo = new FakeWatchdogRepository();
        var sut = CreateSut(repo);

        var ops = new[]
        {
            MakeOp("", "Success", 100)
        };

        sut.Aggregate("Api", ops);

        repo.Increments.Should().ContainSingle();
        repo.Increments[0].Type.Should().Be("Unknown");
    }

    [Fact]
    public void Aggregate_NullDurationMs_TreatedAsZero()
    {
        var repo = new FakeWatchdogRepository();
        var sut = CreateSut(repo);

        var op = new OperationRecord
        {
            Type = "GLPI:FetchTicket",
            Status = "Success",
            DurationMs = null
        };

        sut.Aggregate("Api", [op]);

        repo.Increments[0].TotalMs.Should().Be(0);
    }

    [Fact]
    public void Aggregate_ReturnsOperationCount()
    {
        var repo = new FakeWatchdogRepository();
        var sut = CreateSut(repo);

        var ops = new[]
        {
            MakeOp("A", "Success", 10),
            MakeOp("B", "Success", 20),
            MakeOp("C", "Failed", 30)
        };

        var result = sut.Aggregate("Api", ops);

        result.Should().Be(3);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static OperationsAggregator CreateSut(FakeWatchdogRepository repo)
        => new(repo, NullLogger<OperationsAggregator>.Instance);

    private static OperationRecord MakeOp(string type, string status, long? durationMs)
        => new()
        {
            Type = type,
            Status = status,
            DurationMs = durationMs
        };

    /// <summary>In-memory фейк <see cref="WatchdogRepository"/>.</summary>
    private sealed class FakeWatchdogRepository : WatchdogRepository
    {
        public FakeWatchdogRepository() : base(NullLogger<WatchdogRepository>.Instance)
        {
        }

        public List<(string ServiceName, string Type, string Status, long Count, double TotalMs)> Increments { get; } = [];

        public override void IncrementDailyOperationStats(
            string serviceName,
            string type,
            string status,
            long count,
            double totalDurationMs)
        {
            Increments.Add((serviceName, type, status, count, totalDurationMs));
        }
    }
}