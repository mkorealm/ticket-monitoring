using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Services;
using Xunit;

namespace TicketMonitor.Core.Tests;

/// <summary>Юнит-тесты <see cref="MessageBuilderService"/>.</summary>
public sealed class MessageBuilderServiceTests
{
    private readonly MessageBuilderService _sut =
        new(NullLogger<MessageBuilderService>.Instance);

    // -------------------------------------------------------------------------
    // BuildForNewTicket
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildForNewTicket_NullTicket_Throws()
    {
        var act = () => _sut.BuildForNewTicket(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildForNewTicket_ContainsAllRequiredFields()
    {
        var ticket = new TicketInfo
        {
            Id = "123",
            Title = "Не работает принтер",
            Status = "Новая",
            Initiator = "Иванов И.И.",
            Priority = "Высокий",
            OpenDate = new DateTime(2026, 9, 14, 10, 30, 0),
            Url = "https://glpi/ticket/123"
        };

        var message = _sut.BuildForNewTicket(ticket);

        message.Should().Contain("ID: 123");
        message.Should().Contain("Заголовок: Не работает принтер");
        message.Should().Contain("Статус: Новая");
        message.Should().Contain("Инициатор: Иванов И.И.");
        message.Should().Contain("Приоритет: Высокий");
        message.Should().Contain("Дата открытия: 14-09-2026 10:30");
        message.Should().Contain("Ссылка: https://glpi/ticket/123");
    }

    [Fact]
    public void BuildForNewTicket_EmptyInitiator_OmitsLine()
    {
        var ticket = new TicketInfo
        {
            Id = "1",
            Title = "T",
            Status = "Новая",
            Initiator = "",
            Priority = "Средний",
            OpenDate = DateTime.Now,
            Url = "https://x"
        };

        var message = _sut.BuildForNewTicket(ticket);

        message.Should().NotContain("Инициатор:");
    }

    // -------------------------------------------------------------------------
    // BuildForUpdatedTicket
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildForUpdatedTicket_NullTicket_Throws()
    {
        var act = () => _sut.BuildForUpdatedTicket(null!, []);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildForUpdatedTicket_NullDeltas_Throws()
    {
        var ticket = new TicketInfo { Id = "1", Title = "T", Status = "Новая", Priority = "Средний", Url = "u" };

        var act = () => _sut.BuildForUpdatedTicket(ticket, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildForUpdatedTicket_ContainsDeltas()
    {
        var ticket = new TicketInfo
        {
            Id = "123",
            Title = "T",
            Status = "Решена",
            Initiator = "X",
            Priority = "Высокий",
            Url = "https://glpi/ticket/123"
        };
        var deltas = new[] { "Статус: Новая → Решена", "Приоритет: Низкий → Высокий" };

        var message = _sut.BuildForUpdatedTicket(ticket, deltas);

        message.Should().Contain("Статус: Новая → Решена");
        message.Should().Contain("Приоритет: Низкий → Высокий");
        message.Should().Contain("Ссылка: https://glpi/ticket/123");
    }

    [Fact]
    public void BuildForUpdatedTicket_NoDeltas_ReturnsBaseOnly()
    {
        var ticket = new TicketInfo
        {
            Id = "1",
            Title = "T",
            Status = "Новая",
            Priority = "Средний",
            Url = "u"
        };

        var message = _sut.BuildForUpdatedTicket(ticket, []);

        message.Should().Contain("ID: 1");
        message.Should().Contain("Ссылка: u");
    }
}