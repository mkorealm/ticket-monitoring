using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Services;
using Xunit;

namespace TicketMonitor.Core.Tests;

public class TicketChangeDetectorTests
{
    private readonly TicketChangeDetector _sut = new(
    new MessageBuilderService(NullLogger<MessageBuilderService>.Instance),
    NullLogger<TicketChangeDetector>.Instance);

    // ---------------------------------------------------------------------
    // Базовые случаи
    // ---------------------------------------------------------------------

    [Fact]
    public void Detect_EmptyPrevious_EmptyCurrent_ReturnsEmpty()
    {
        var result = _sut.Detect([], []);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Detect_SameSnapshot_ReturnsEmpty()
    {
        var ticket = MakeTicket("1", status: "В работе (назначена)", priority: "Средний");

        var result = _sut.Detect([ticket], [ticket]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Detect_IdenticalButDifferentInstances_ReturnsEmpty()
    {
        var previous = new[] { MakeTicket("1", status: "Новая") };
        var current = new[] { MakeTicket("1", status: "Новая") };

        var result = _sut.Detect(previous, current);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Detect_EmptyCurrent_ReturnsEmpty()
    {
        var previous = new[] { MakeTicket("1") };

        var result = _sut.Detect(previous, []);

        result.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------
    // Новые заявки
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("Новая")]
    [InlineData("Новая заявка")]
    [InlineData("В работе (назначена)")]
    [InlineData("назначена")]
    public void Detect_NewTicketWithNewStatus_ClassifiedAsNew(string status)
    {
        var ticket = MakeTicket("1", status: status);

        var result = _sut.Detect([], [ticket]);

        result.Should().ContainSingle()
            .Which.Type.Should().Be(ChangeType.New);
    }

    [Theory]
    [InlineData("Решена")]
    [InlineData("Закрыта")]
    [InlineData("Ожидание")]
    public void Detect_NewTicketWithOldStatus_ClassifiedAsUpdated(string status)
    {
        var ticket = MakeTicket("1", status: status);

        var result = _sut.Detect([], [ticket]);

        result.Should().ContainSingle()
            .Which.Type.Should().Be(ChangeType.Updated);
    }

    // ---------------------------------------------------------------------
    // Изменённые заявки
    // ---------------------------------------------------------------------

    [Fact]
    public void Detect_StatusChanged_ReturnsUpdatedWithDelta()
    {
        var previous = new[] { MakeTicket("1", status: "Новая") };
        var current = new[] { MakeTicket("1", status: "В работе (назначена)") };

        var result = _sut.Detect(previous, current);

        result.Should().ContainSingle();
        result[0].Type.Should().Be(ChangeType.Updated);
        result[0].Message.Should().Contain("Статус: Новая → В работе (назначена)");
    }

    [Fact]
    public void Detect_AssigneeChanged_ReturnsUpdatedWithDelta()
    {
        var previous = new[] { MakeTicket("1", assignee: "Иванов") };
        var current = new[] { MakeTicket("1", assignee: "Петров") };

        var result = _sut.Detect(previous, current);

        result.Should().ContainSingle();
        result[0].Message.Should().Contain("Исполнитель: Иванов → Петров");
    }

    [Fact]
    public void Detect_PriorityChanged_ReturnsUpdatedWithDelta()
    {
        var previous = new[] { MakeTicket("1", priority: "Средний") };
        var current = new[] { MakeTicket("1", priority: "Высокий") };

        var result = _sut.Detect(previous, current);

        result.Should().ContainSingle();
        result[0].Message.Should().Contain("Приоритет: Средний → Высокий");
    }

    [Fact]
    public void Detect_MultipleFieldsChanged_ContainsAllDeltas()
    {
        var previous = new[] { MakeTicket("1", status: "Новая", assignee: "Иванов", priority: "Низкий") };
        var current = new[] { MakeTicket("1", status: "Решена", assignee: "Петров", priority: "Высокий") };

        var result = _sut.Detect(previous, current);

        result.Should().ContainSingle();
        var msg = result[0].Message;
        msg.Should().Contain("Статус: Новая → Решена");
        msg.Should().Contain("Исполнитель: Иванов → Петров");
        msg.Should().Contain("Приоритет: Низкий → Высокий");
    }

    // ---------------------------------------------------------------------
    // Нормализация пробелов
    // ---------------------------------------------------------------------

    [Fact]
    public void Detect_WhitespaceOnlyDifference_TreatedAsEqual()
    {
        var previous = new[] { MakeTicket("1", status: "Новая") };
        var current = new[] { MakeTicket("1", status: "  Новая  ") };

        var result = _sut.Detect(previous, current);

        result.Should().BeEmpty("различия только в пробелах не считаются изменениями");
    }

    [Fact]
    public void Detect_NullAndEmptyAssignee_TreatedAsEqual()
    {
        var previous = new[] { MakeTicket("1", assignee: "") };
        var current = new[] { MakeTicket("1", assignee: "   ") };

        var result = _sut.Detect(previous, current);

        result.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------
    // Смешанные сценарии
    // ---------------------------------------------------------------------

    [Fact]
    public void Detect_MixedNewAndUpdated_ReturnsBoth()
    {
        var previous = new[] { MakeTicket("1", status: "Новая") };
        var current = new[]
        {
            MakeTicket("1", status: "Решена"),         // updated
            MakeTicket("2", status: "Новая")           // new
        };

        var result = _sut.Detect(previous, current);

        result.Should().HaveCount(2);
        result.Should().ContainSingle(c => c.Type == ChangeType.Updated && c.Ticket!.Id == "1");
        result.Should().ContainSingle(c => c.Type == ChangeType.New && c.Ticket!.Id == "2");
    }

    [Fact]
    public void Detect_OnlyUnchangedTickets_ReturnsEmpty()
    {
        var previous = new[] { MakeTicket("1"), MakeTicket("2") };
        var current = new[] { MakeTicket("1"), MakeTicket("2") };

        var result = _sut.Detect(previous, current);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Detect_TicketWithoutId_Skipped()
    {
        var ticket = MakeTicket(id: "", status: "Новая");

        var result = _sut.Detect([], [ticket]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Detect_DuplicateIdsInPrevious_DoesNotThrow()
    {
        var previous = new[]
        {
            MakeTicket("1", status: "Новая"),
            MakeTicket("1", status: "Решена")   // дубль
        };
        var current = new[] { MakeTicket("1", status: "Решена") };

        var act = () => _sut.Detect(previous, current);

        act.Should().NotThrow();
    }

    // ---------------------------------------------------------------------
    // Побочные эффекты / контракт
    // ---------------------------------------------------------------------

    [Fact]
    public void Detect_DoesNotMutateInputs()
    {
        var previous = new[] { MakeTicket("1", status: "Новая") };
        var current = new[] { MakeTicket("1", status: "Решена") };
        var previousSnapshot = previous[0].Status;
        var currentSnapshot = current[0].Status;

        _sut.Detect(previous, current);

        previous[0].Status.Should().Be(previousSnapshot);
        current[0].Status.Should().Be(currentSnapshot);
    }

    [Fact]
    public void Detect_ReturnsTicketReferenceFromCurrent()
    {
        var previous = new[] { MakeTicket("1", status: "Новая") };
        var currentTicket = MakeTicket("1", status: "Решена");
        var current = new[] { currentTicket };

        var result = _sut.Detect(previous, current);

        result[0].Ticket.Should().BeSameAs(currentTicket);
    }

    // ---------------------------------------------------------------------
    // Helper
    // ---------------------------------------------------------------------

    private static TicketInfo MakeTicket(
        string id = "1",
        string title = "Test",
        string status = "Новая",
        string assignee = "",
        string priority = "Средний",
        DateTime? lastModified = null)
    {
        return new TicketInfo
        {
            Id = id,
            Title = title,
            Status = status,
            Assignee = assignee,
            Priority = priority,
            LastModified = lastModified ?? new DateTime(2026, 1, 1)
        };
    }
}