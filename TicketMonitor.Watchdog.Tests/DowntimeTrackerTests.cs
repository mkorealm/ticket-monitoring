using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketMonitor.Watchdog.Configuration;
using TicketMonitor.Watchdog.Models;
using TicketMonitor.Watchdog.Services;
using Xunit;

namespace TicketMonitor.Watchdog.Tests;

/// <summary>
/// Юнит-тесты <see cref="DowntimeTracker"/>.
/// Используют in-memory <see cref="FakeWatchdogRepository"/>.
/// </summary>
public sealed class DowntimeTrackerTests
{
    // -------------------------------------------------------------------------
    // Healthy path
    // -------------------------------------------------------------------------

    [Fact]
    public void ProcessResult_HealthyNoOpenDowntime_ReturnsFalse()
    {
        var repo = new FakeWatchdogRepository();
        var sut = CreateSut(repo);

        var changed = sut.ProcessResult("Api", healthy: true, reason: null);

        changed.Should().BeFalse();
        repo.SaveDowntimeCalls.Should().BeEmpty();
    }

    [Fact]
    public void ProcessResult_FailuresBelowThreshold_NoDowntimeOpened()
    {
        var repo = new FakeWatchdogRepository();
        var sut = CreateSut(repo, failureThreshold: 3);

        sut.ProcessResult("Api", false, "err 1").Should().BeFalse();
        sut.ProcessResult("Api", false, "err 2").Should().BeFalse();
        sut.ProcessResult("Api", false, "err 3").Should().BeTrue(); // порог достигнут

        repo.SaveDowntimeCalls.Should().ContainSingle();
        repo.SaveDowntimeCalls[0].EndTime.Should().BeNull();
        repo.SaveDowntimeCalls[0].ServiceName.Should().Be("Api");
    }

    [Fact]
    public void ProcessResult_ThirdFailure_OpensDowntime()
    {
        var repo = new FakeWatchdogRepository();
        var sut = CreateSut(repo, failureThreshold: 3);

        sut.ProcessResult("Api", false, "err 1");
        sut.ProcessResult("Api", false, "err 2");
        var changed = sut.ProcessResult("Api", false, "err 3");

        changed.Should().BeTrue();
        repo.SaveDowntimeCalls.Should().ContainSingle();
        repo.SaveDowntimeCalls[0].Reason.Should().Be("err 3");
    }

    [Fact]
    public void ProcessResult_FourthFailure_DowntimeNotDuplicated()
    {
        var repo = new FakeWatchdogRepository();
        var sut = CreateSut(repo, failureThreshold: 3);

        sut.ProcessResult("Api", false, "err 1");
        sut.ProcessResult("Api", false, "err 2");
        sut.ProcessResult("Api", false, "err 3"); // opens downtime
        var changed = sut.ProcessResult("Api", false, "err 4");

        changed.Should().BeFalse();
        repo.SaveDowntimeCalls.Should().ContainSingle();
    }

    [Fact]
    public void ProcessResult_HealthyAfterDowntime_ClosesDowntime()
    {
        var repo = new FakeWatchdogRepository();
        var sut = CreateSut(repo, failureThreshold: 3);

        sut.ProcessResult("Api", false, "err 1");
        sut.ProcessResult("Api", false, "err 2");
        sut.ProcessResult("Api", false, "err 3"); // opens

        // Симулируем, что репозиторий вернёт открытый downtime
        repo.OpenDowntime = repo.SaveDowntimeCalls[0];
        repo.SaveDowntimeCalls.Clear();

        var changed = sut.ProcessResult("Api", healthy: true, reason: null);

        changed.Should().BeTrue();
        repo.SaveDowntimeCalls.Should().ContainSingle();
        repo.SaveDowntimeCalls[0].EndTime.Should().NotBeNull();
        repo.SaveDowntimeCalls[0].DurationMinutes.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void ProcessResult_HealthyResetsCounter()
    {
        var repo = new FakeWatchdogRepository();
        var sut = CreateSut(repo, failureThreshold: 3);

        sut.ProcessResult("Api", false, "e1");
        sut.ProcessResult("Api", false, "e2");
        sut.ProcessResult("Api", healthy: true, reason: null); // reset
        sut.ProcessResult("Api", false, "e3");
        var changed = sut.ProcessResult("Api", false, "e4");

        // Только 2 неудачи подряд после reset — порог не достигнут.
        changed.Should().BeFalse();
        repo.SaveDowntimeCalls.Should().BeEmpty();
    }

    [Fact]
    public void ProcessResult_MultipleServices_TrackedIndependently()
    {
        var repo = new FakeWatchdogRepository();
        var sut = CreateSut(repo, failureThreshold: 3);

        sut.ProcessResult("Api", false, "e1");
        sut.ProcessResult("Api", false, "e2");
        sut.ProcessResult("Redmine", false, "e1");
        sut.ProcessResult("Redmine", false, "e2");
        sut.ProcessResult("Api", false, "e3"); // Api opens
        var redmineChanged = sut.ProcessResult("Redmine", false, "e3"); // Redmine opens

        repo.SaveDowntimeCalls.Should().HaveCount(2);
        redmineChanged.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static DowntimeTracker CreateSut(FakeWatchdogRepository repo, int failureThreshold = 3)
    {
        var options = Options.Create(new WatchdogOptions
        {
            FailureThreshold = failureThreshold
        });

        return new DowntimeTracker(repo, options, NullLogger<DowntimeTracker>.Instance);
    }

    /// <summary>In-memory фейк <see cref="WatchdogRepository"/>.</summary>
    private sealed class FakeWatchdogRepository : WatchdogRepository
    {
        public FakeWatchdogRepository() : base(NullLogger<WatchdogRepository>.Instance)
        {
        }

        public List<DowntimeEvent> SaveDowntimeCalls { get; } = [];
        public DowntimeEvent? OpenDowntime { get; set; }
        public List<double> AddDowntimeMinutesCalls { get; } = [];

        public override DowntimeEvent? GetOpenDowntime(string serviceName)
        {
            return OpenDowntime is not null && OpenDowntime.ServiceName == serviceName
                ? OpenDowntime
                : null;
        }

        public override void SaveDowntime(DowntimeEvent evt)
        {
            SaveDowntimeCalls.Add(evt);
            OpenDowntime = evt;
        }

        public override void AddDailyDowntimeMinutes(string serviceName, double minutes)
        {
            AddDowntimeMinutesCalls.Add(minutes);
        }
    }
}