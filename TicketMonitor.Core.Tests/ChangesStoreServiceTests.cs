using FluentAssertions;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Services;
using Xunit;

namespace TicketMonitor.Core.Tests;

/// <summary>
/// Юнит-тесты <see cref="ChangesStoreService"/>.
/// </summary>
public sealed class ChangesStoreServiceTests
{
    private readonly ChangesStoreService _sut = new();

    // -------------------------------------------------------------------------
    // AddChanges / GetChanges
    // -------------------------------------------------------------------------

    [Fact]
    public void GetChanges_EmptyStore_ReturnsEmpty()
    {
        var result = _sut.GetChanges("Monitor1");

        result.Should().BeEmpty();
    }

    [Fact]
    public void AddChanges_SingleChange_StoredCorrectly()
    {
        var ticket = MakeTicket("1");
        var change = MakeChange(ChangeType.New, ticket);

        _sut.AddChanges([change], "Monitor1");

        var result = _sut.GetChanges("Monitor1");
        result.Should().ContainSingle();

        var record = result[0];
        record.Type.Should().Be("New");
        record.TicketId.Should().Be("1");
        record.Title.Should().Be(ticket.Title);
        record.Status.Should().Be(ticket.Status);
        record.Initiator.Should().Be(ticket.Initiator);
        record.Priority.Should().Be(ticket.Priority);
        record.Url.Should().Be(ticket.Url);
    }

    [Fact]
    public void AddChanges_MultipleChanges_AllStored()
    {
        var changes = new List<TicketChange>
        {
            MakeChange(ChangeType.New, MakeTicket("1")),
            MakeChange(ChangeType.Updated, MakeTicket("2")),
            MakeChange(ChangeType.New, MakeTicket("3"))
        };

        _sut.AddChanges(changes, "Monitor1");

        _sut.GetChanges("Monitor1").Should().HaveCount(3);
    }

    [Fact]
    public void AddChanges_MultipleCalls_Accumulated()
    {
        _sut.AddChanges([MakeChange(ChangeType.New, MakeTicket("1"))], "Monitor1");
        _sut.AddChanges([MakeChange(ChangeType.New, MakeTicket("2"))], "Monitor1");

        _sut.GetChanges("Monitor1").Should().HaveCount(2);
    }

    [Fact]
    public void AddChanges_DifferentMonitors_KeptSeparately()
    {
        _sut.AddChanges([MakeChange(ChangeType.New, MakeTicket("1"))], "Monitor1");
        _sut.AddChanges([MakeChange(ChangeType.New, MakeTicket("2"))], "Monitor2");

        _sut.GetChanges("Monitor1").Should().ContainSingle()
            .Which.TicketId.Should().Be("1");
        _sut.GetChanges("Monitor2").Should().ContainSingle()
            .Which.TicketId.Should().Be("2");
    }

    [Fact]
    public void AddChanges_EachRecordHasUniqueId()
    {
        _sut.AddChanges([
            MakeChange(ChangeType.New, MakeTicket("1")),
            MakeChange(ChangeType.New, MakeTicket("1")) // тот же тикет
        ], "Monitor1");

        var records = _sut.GetChanges("Monitor1");
        records.Should().HaveCount(2);
        records[0].Id.Should().NotBe(records[1].Id);
    }

    [Fact]
    public void AddChanges_RecordTimestampIsRecent()
    {
        var before = DateTime.Now.AddSeconds(-1);

        _sut.AddChanges([MakeChange(ChangeType.New, MakeTicket("1"))], "Monitor1");

        var record = _sut.GetChanges("Monitor1")[0];
        record.DetectedAt.Should().BeAfter(before);
    }

    // -------------------------------------------------------------------------
    // GetChanges — не потребляет
    // -------------------------------------------------------------------------

    [Fact]
    public void GetChanges_CalledTwice_DoesNotClearStore()
    {
        _sut.AddChanges([MakeChange(ChangeType.New, MakeTicket("1"))], "Monitor1");

        var first = _sut.GetChanges("Monitor1");
        var second = _sut.GetChanges("Monitor1");

        first.Should().ContainSingle();
        second.Should().ContainSingle();
    }

    [Fact]
    public void GetChanges_ReturnsCopy_NotInternalReference()
    {
        _sut.AddChanges([MakeChange(ChangeType.New, MakeTicket("1"))], "Monitor1");

        var first = _sut.GetChanges("Monitor1");
        first.Clear(); // модифицируем полученный список

        var second = _sut.GetChanges("Monitor1");
        second.Should().ContainSingle("внутреннее хранилище не должно зависеть от возвращённого списка");
    }

    // -------------------------------------------------------------------------
    // ConsumeChanges
    // -------------------------------------------------------------------------

    [Fact]
    public void ConsumeChanges_ReturnsAndClears()
    {
        _sut.AddChanges([MakeChange(ChangeType.New, MakeTicket("1"))], "Monitor1");

        var consumed = _sut.ConsumeChanges("Monitor1");
        consumed.Should().ContainSingle();

        var after = _sut.GetChanges("Monitor1");
        after.Should().BeEmpty("после ConsumeChanges хранилище должно быть пустым");
    }

    [Fact]
    public void ConsumeChanges_EmptyStore_ReturnsEmpty()
    {
        var result = _sut.ConsumeChanges("Monitor1");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ConsumeChanges_OnlyAffectsSpecifiedMonitor()
    {
        _sut.AddChanges([MakeChange(ChangeType.New, MakeTicket("1"))], "Monitor1");
        _sut.AddChanges([MakeChange(ChangeType.New, MakeTicket("2"))], "Monitor2");

        _sut.ConsumeChanges("Monitor1");

        _sut.GetChanges("Monitor1").Should().BeEmpty();
        _sut.GetChanges("Monitor2").Should().ContainSingle();
    }

    // -------------------------------------------------------------------------
    // ClearChanges
    // -------------------------------------------------------------------------

    [Fact]
    public void ClearChanges_RemovesAllForMonitor()
    {
        _sut.AddChanges([MakeChange(ChangeType.New, MakeTicket("1"))], "Monitor1");
        _sut.AddChanges([MakeChange(ChangeType.New, MakeTicket("2"))], "Monitor2");

        _sut.ClearChanges("Monitor1");

        _sut.GetChanges("Monitor1").Should().BeEmpty();
        _sut.GetChanges("Monitor2").Should().ContainSingle();
    }

    [Fact]
    public void ClearChanges_NonExistentMonitor_DoesNotThrow()
    {
        var act = () => _sut.ClearChanges("NonExistent");

        act.Should().NotThrow();
    }

    // -------------------------------------------------------------------------
    // Thread safety
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AddChanges_ParallelCalls_NoDataLoss()
    {
        const int parallelTasks = 10;
        const int changesPerTask = 100;

        var tasks = Enumerable.Range(0, parallelTasks).Select(taskIndex => Task.Run(() =>
        {
            for (var i = 0; i < changesPerTask; i++)
            {
                _sut.AddChanges(
                    [MakeChange(ChangeType.New, MakeTicket($"{taskIndex}-{i}"))],
                    "Monitor1");
            }
        })).ToList();

        await Task.WhenAll(tasks);

        _sut.GetChanges("Monitor1").Should().HaveCount(parallelTasks * changesPerTask);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static TicketInfo MakeTicket(string id) => new()
    {
        Id = id,
        Title = $"Ticket {id}",
        Status = "Новая",
        Initiator = "Иванов",
        Assignee = "Петров",
        Priority = "Средний",
        OpenDate = new DateTime(2026, 1, 1, 10, 0, 0),
        LastModified = new DateTime(2026, 1, 1, 11, 0, 0),
        Url = $"https://glpi/ticket/{id}"
    };

    private static TicketChange MakeChange(ChangeType type, TicketInfo ticket) => new()
    {
        Type = type,
        Ticket = ticket,
        Message = $"Change for ticket {ticket.Id}"
    };
}