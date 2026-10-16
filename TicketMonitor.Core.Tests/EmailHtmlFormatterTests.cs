using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Services;
using TicketMonitor.Core.Services.Formatters;
using Xunit;

namespace TicketMonitor.Core.Tests;

public class EmailHtmlFormatterTests
{
    private readonly EmailHtmlFormatter _sut;

    public EmailHtmlFormatterTests()
    {
        var renderer = new EmailTemplateRenderer(NullLogger<EmailTemplateRenderer>.Instance);
        _sut = new EmailHtmlFormatter(renderer, NullLogger<EmailHtmlFormatter>.Instance);
    }

    [Fact]
    public void Format_NullMessage_Throws()
    {
        var act = () => _sut.Format(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Format_ProducesHtml()
    {
        var message = MakeMessage();

        var html = _sut.Format(message);

        html.Should().NotBeNullOrWhiteSpace();
        html.Should().Contain("<html");
        html.Should().Contain("12345");
    }

    [Fact]
    public void Format_EncodesHtmlInTicketFields()
    {
        var message = MakeMessage() with
        {
            Ticket = MakeMessage().Ticket with { Title = "<script>alert('xss')</script>" }
        };

        var html = _sut.Format(message);

        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void Format_WithAnswers_RendersAnswerBlocks()
    {
        var message = MakeMessage() with
        {
            Answers = [new NotificationAnswer { Author = "Иванов И.И.", Text = "Проблема решена." }]
        };

        var html = _sut.Format(message);

        html.Should().Contain("Иванов И.И.");
        html.Should().Contain("Проблема решена.");
    }

    [Fact]
    public void Format_NoAnswers_UsesPlaceholder()
    {
        var message = MakeMessage() with { Answers = [] };

        var html = _sut.Format(message);

        html.Should().Contain("Нет новых ответов");
    }

    [Fact]
    public void Format_NoDescription_UsesPlaceholder()
    {
        var message = MakeMessage() with { Description = null };

        var html = _sut.Format(message);

        html.Should().Contain("Нет описания");
    }

    [Fact]
    public void Format_WithTwoActionLinks_RendersBothButtons()
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

        var html = _sut.Format(message);

        html.Should().Contain("➕ Создать задачу в Redmine");
        html.Should().Contain("http://ticket-monitor:8082/redmine/create?ticketId=12345");
        html.Should().Contain("✅ Добавить решение");
        html.Should().Contain("http://ticket-monitor:8082/glpi/solution?ticketId=12345");
    }

    [Fact]
    public void Format_WithoutActionLinks_NoButtonsRendered()
    {
        var message = MakeMessage() with { ActionLinks = [] };

        var html = _sut.Format(message);

        html.Should().NotContain("Создать задачу в Redmine");
        html.Should().NotContain("Добавить решение");
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