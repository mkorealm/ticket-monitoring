using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TicketMonitor.Watchdog.Models;
using TicketMonitor.Watchdog.Services;
using TicketMonitor.Watchdog.Tests.TestHelpers;
using Xunit;

namespace TicketMonitor.Watchdog.Tests;

/// <summary>
/// Интеграционные тесты <see cref="WatchdogRepository"/>.
/// Используют реальную LiteDB в изолированной папке.
/// </summary>
[Collection(WatchdogDbCollection.Name)]
public sealed class WatchdogRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public WatchdogRepositoryTests()
    {
        var dataFolder = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataFolder);
        _dbPath = Path.Combine(dataFolder, "watchdog.db");
        TryDelete();
    }

    public void Dispose() => TryDelete();

    private void TryDelete()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public void IncrementDailyHttpStats_CreatesNewRecord()
    {
        using var sut = CreateSut();

        sut.IncrementDailyHttpStats("Api", totalChecks: 10, failedChecks: 2, totalResponseTimeMs: 500);

        var stats = sut.GetDailyHttpStatsForDay(DateTime.UtcNow.Date);
        stats.Should().ContainSingle();
        stats[0].ServiceName.Should().Be("Api");
        stats[0].TotalChecks.Should().Be(10);
        stats[0].FailedChecks.Should().Be(2);
        stats[0].TotalResponseTimeMs.Should().Be(500);
    }

    [Fact]
    public void IncrementDailyHttpStats_AccumulatesOnSecondCall()
    {
        using var sut = CreateSut();

        sut.IncrementDailyHttpStats("Api", 10, 2, 500);
        sut.IncrementDailyHttpStats("Api", 5, 1, 200);

        var stats = sut.GetDailyHttpStatsForDay(DateTime.UtcNow.Date);
        stats[0].TotalChecks.Should().Be(15);
        stats[0].FailedChecks.Should().Be(3);
        stats[0].TotalResponseTimeMs.Should().Be(700);
    }

    [Fact]
    public void IncrementDailyOperationStats_CreatesNewRecord()
    {
        using var sut = CreateSut();

        sut.IncrementDailyOperationStats("Api", "GLPI:FetchTicket", "Success", 10, 1000);

        var stats = sut.GetDailyOperationStatsForDay(DateTime.UtcNow.Date);
        stats.Should().ContainSingle();
        stats[0].Type.Should().Be("GLPI:FetchTicket");
        stats[0].Status.Should().Be("Success");
        stats[0].Count.Should().Be(10);
        stats[0].TotalDurationMs.Should().Be(1000);
    }

    [Fact]
    public void IncrementDailyOperationStats_DifferentStatuses_SeparateRecords()
    {
        using var sut = CreateSut();

        sut.IncrementDailyOperationStats("Api", "X", "Success", 10, 100);
        sut.IncrementDailyOperationStats("Api", "X", "Failed", 2, 200);

        var stats = sut.GetDailyOperationStatsForDay(DateTime.UtcNow.Date);
        stats.Should().HaveCount(2);
    }

    [Fact]
    public void SaveDowntime_NewEvent_CanBeRead()
    {
        using var sut = CreateSut();

        sut.SaveDowntime(new DowntimeEvent
        {
            ServiceName = "Api",
            StartTime = DateTime.UtcNow,
            Reason = "connection refused"
        });

        var open = sut.GetOpenDowntime("Api");
        open.Should().NotBeNull();
        open!.Reason.Should().Be("connection refused");
    }

    [Fact]
    public void GetOpenDowntime_NoOpenEvent_ReturnsNull()
    {
        using var sut = CreateSut();

        var result = sut.GetOpenDowntime("Api");

        result.Should().BeNull();
    }

    [Fact]
    public void SaveDowntime_ClosedEvent_NotReturnedAsOpen()
    {
        using var sut = CreateSut();

        var evt = new DowntimeEvent
        {
            ServiceName = "Api",
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = DateTime.UtcNow,
            DurationMinutes = 5
        };

        sut.SaveDowntime(evt);

        sut.GetOpenDowntime("Api").Should().BeNull();
    }

    [Fact]
    public void SaveReadState_CanBeRead()
    {
        using var sut = CreateSut();

        var now = DateTime.UtcNow;
        sut.SaveReadState(new ServiceReadState
        {
            ServiceName = "Api",
            LastProcessedStartedAt = now
        });

        var state = sut.GetReadState("Api");
        state.Should().NotBeNull();

        // LiteDB не сохраняет DateTimeKind: при чтении возвращается Local/Unspecified.
        // Сравниваем с широким допуском, чтобы тест проходил на любой машине и таймзоне.
        state!.LastProcessedStartedAt.Should().BeCloseTo(now, TimeSpan.FromHours(15),
            "LiteDB теряет Kind — точное сравнение UTC-времени невозможно");
    }

    [Fact]
    public void ApplyRetention_RemovesOldHttpChecks()
    {
        using var sut = CreateSut();

        var old = new HttpCheckRecord
        {
            ServiceName = "Api",
            Timestamp = DateTime.UtcNow.AddDays(-60),
            ErrorMessage = "old error"
        };
        var recent = new HttpCheckRecord
        {
            ServiceName = "Api",
            Timestamp = DateTime.UtcNow.AddHours(-1),
            ErrorMessage = "recent error"
        };

        sut.SaveHttpCheck(old);
        sut.SaveHttpCheck(recent);

        var deleted = sut.ApplyRetention(30, 90, 90);

        deleted.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ApplyRetention_NoOldRecords_ReturnsZero()
    {
        using var sut = CreateSut();

        sut.IncrementDailyHttpStats("Api", 10, 0, 100);

        var deleted = sut.ApplyRetention(30, 90, 90);

        deleted.Should().Be(0);
    }

    private WatchdogRepository CreateSut()
        => new(NullLogger<WatchdogRepository>.Instance);
}