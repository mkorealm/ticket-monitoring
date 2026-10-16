using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Services.Formatters;
using Xunit;

namespace TicketMonitor.Core.Tests;

public class RocketChatMarkdownFormatterTests
{
    private readonly RocketChatMarkdownFormatter _sut =
        new(NullLogger<RocketChatMarkdownFormatter>.Instance);

    [Fact]
    public void Format_NullMessage_Throws()
    {
        var act = () => _sut.Format(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Format_ContainsSubject()
    {
        var message = MakeMessage();

        var md = _sut.Format(message);

        md.Should().Contain("**Тест**");
    }

    [Fact]
    public void Format_ContainsTicketFields()
    {
        var message = MakeMessage();

        var md = _sut.Format(message);

        md.Should().Contain("**Тест**");
        md.Should().Contain("**Статус:** Новая");
        md.Should().Contain("**Инициатор:** Иванов И.И.");
        md.Should().Contain("**Приоритет:** Высокий");
        md.Should().Contain("**Открыта:** 15-09-2026 10:30");
    }

    [Fact]
    public void Format_ContainsDeltas()
    {
        var message = MakeMessage() with { Deltas = ["Статус: Новая → Решена"] };

        var md = _sut.Format(message);

        md.Should().Contain("**Изменения:**");
        md.Should().Contain("- Статус: Новая → Решена");
    }

    [Fact]
    public void Format_ContainsDescription()
    {
        var message = MakeMessage();

        var md = _sut.Format(message);

        md.Should().Contain("**Описание:**");
        md.Should().Contain("Принтер не печатает.");
    }

    [Fact]
    public void Format_ContainsAnswers()
    {
        var message = MakeMessage() with
        {
            Answers = [new NotificationAnswer { Author = "Петров П.П.", Text = "Взял в работу." }]
        };

        var md = _sut.Format(message);

        md.Should().Contain("**Ответы:**");
        md.Should().Contain("> **Петров П.П.:** Взял в работу.");
    }

    [Fact]
    public void Format_ContainsUrl()
    {
        var message = MakeMessage();

        var md = _sut.Format(message);

        md.Should().Contain("[Открыть заявку](https://glpi/ticket/12345)");
    }

    [Fact]
    public void Format_EscapesMarkdownSpecialChars()
    {
        var message = MakeMessage() with
        {
            Description = "Test_underscore *bold* `code`"
        };

        var md = _sut.Format(message);

        md.Should().Contain("Test\\_underscore");
        md.Should().Contain("\\*bold\\*");
        md.Should().Contain("\\`code\\`");
    }

    [Fact]
    public void Format_EmptyDeltas_NoChangesSection()
    {
        var message = MakeMessage() with { Deltas = [] };

        var md = _sut.Format(message);

        md.Should().NotContain("**Изменения:**");
    }

    [Fact]
    public void Format_EmptyAnswers_NoAnswersSection()
    {
        var message = MakeMessage() with { Answers = [] };

        var md = _sut.Format(message);

        md.Should().NotContain("**Ответы:**");
    }

    [Fact]
    public void Format_WithSingleActionLink_RendersMarkdownLink()
    {
        var message = MakeMessage() with
        {
            ActionLinks =
            [
                new TicketActionLink
                {
                    Label = "➕ Создать задачу в Redmine",
                    Url = "http://ticket-monitor:8082/redmine/create?ticketId=12345"
                }
            ]
        };

        var md = _sut.Format(message);

        md.Should().Contain("[➕ Создать задачу в Redmine](http://ticket-monitor:8082/redmine/create?ticketId=12345)");
    }

    [Fact]
    public void Format_WithTwoActionLinks_RendersBothLinks()
    {
        var message = MakeMessage() with
        {
            ActionLinks =
            [
                new TicketActionLink
                {
                    Label = "➕ Создать задачу в Redmine",
                    Url = "http://ticket-monitor:8082/redmine/create?ticketId=12345"
                },
                new TicketActionLink
                {
                    Label = "✅ Добавить решение",
                    Url = "http://ticket-monitor:8082/glpi/solution?ticketId=12345"
                }
            ]
        };

        var md = _sut.Format(message);

        md.Should().Contain("[➕ Создать задачу в Redmine](http://ticket-monitor:8082/redmine/create?ticketId=12345)");
        md.Should().Contain("[✅ Добавить решение](http://ticket-monitor:8082/glpi/solution?ticketId=12345)");
    }

    [Fact]
    public void Format_WithoutActionLinks_NoActionLines()
    {
        var message = MakeMessage() with { ActionLinks = [] };

        var md = _sut.Format(message);

        md.Should().NotContain("Создать задачу в Redmine");
        md.Should().NotContain("Добавить решение");
    }

    [Fact]
    public void Format_NoDuplicateIdInBody()
    {
        var message = MakeMessage();

        var md = _sut.Format(message);

        md.Should().NotContain("**ID:**");
    }

    private static NotificationMessage MakeMessage() => new()
    {
        Subject = "Тест",
        Kind = NotificationKind.New,
        Ticket = new TicketInfo
        {
            Id = "12345",
            Title = "Не работает принтер",
            Status = "Новая",
            Initiator = "Иванов И.И.",
            Priority = "Высокий",
            OpenDate = new DateTime(2026, 9, 15, 10, 30, 0),
            Url = "https://glpi/ticket/12345"
        },
        Description = "Принтер не печатает.",
        Answers = []
    };
}