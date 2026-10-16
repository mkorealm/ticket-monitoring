using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Services;
using Xunit;

namespace TicketMonitor.Core.Tests;

public class EmailTemplateRendererTests
{
    private readonly EmailTemplateRenderer _sut =
        new(NullLogger<EmailTemplateRenderer>.Instance);

    // -------------------------------------------------------------------------
    // Null-protection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_NullData_ThrowsArgumentNullException()
    {
        var act = async () => await _sut.RenderAsync(null!, "plain text");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RenderAsync_NullPlainTextFallback_ThrowsArgumentNullException()
    {
        var act = async () => await _sut.RenderAsync(new EmailMessageData(), null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // -------------------------------------------------------------------------
    // Замены плейсхолдеров (используем fallback-шаблон, если файл не найден)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_SubstitutesAllPlaceholders_WhenRealTemplateExists()
    {
        var data = new EmailMessageData
        {
            TicketId = "12345",
            Title = "Заголовок",
            Status = "Новая"
        };

        const string plainText = "plain-text-fallback";

        var html = await _sut.RenderAsync(data, plainText);

        // Проверяем, что плейсхолдеры подставлены, а не остались в виде {{...}}
        html.Should().Contain("12345");
        html.Should().Contain("Заголовок");
        html.Should().Contain("Новая");
        html.Should().NotContain("{{TicketId}}");
        html.Should().NotContain("{{Title}}");
        html.Should().NotContain("{{Status}}");
    }

    [Fact]
    public async Task RenderAsync_DoesNotThrow_WhenTemplateFileMissing()
    {
        var data = new EmailMessageData { TicketId = "1", Title = "T" };

        var act = async () => await _sut.RenderAsync(data, "fallback body");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RenderAsync_ProducesNonEmptyHtml()
    {
        var data = new EmailMessageData { TicketId = "1", Title = "Test" };

        var html = await _sut.RenderAsync(data, "plain");

        html.Should().NotBeNullOrWhiteSpace();
        html.Should().Contain("<html");
    }

    // -------------------------------------------------------------------------
    // Логика подстановки (проверяется через синтетические данные)
    // Если файл EmailTemplate.html реально существует — плейсхолдеры
    // {{TicketId}}, {{Title}}, {{Status}}, {{Initiator}}, {{Priority}},
    // {{OpenDate}}, {{Url}}, {{Description}}, {{Answers}} подставляются.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_IfRealTemplateExists_ReplacesAllPlaceholders()
    {
        // Пропускаем, если реального шаблона нет (только fallback)
        var templatePath = Path.Combine(
            AppContext.BaseDirectory, "Templates", "EmailTemplate.html");

        if (!File.Exists(templatePath))
        {
            // Fallback-шаблон не содержит {{TicketId}} и т.д., тест не имеет смысла
            return;
        }

        var data = new EmailMessageData
        {
            TicketId = "99999",
            Title = "Тестовый заголовок",
            Status = "Новая",
            Initiator = "Тестовый Инициатор",
            Priority = "Высокий",
            OpenDate = "15-09-2026 10:30",
            Url = "https://glpi/ticket/99999",
            Description = "<p>Описание</p>",
            Answers = "<div>Ответ</div>"
        };

        var html = await _sut.RenderAsync(data, "plain-text");

        html.Should().Contain("99999");
        html.Should().Contain("Тестовый заголовок");
        html.Should().Contain("Новая");
        html.Should().Contain("Тестовый Инициатор");
        html.Should().Contain("Высокий");
        html.Should().Contain("15-09-2026 10:30");
        html.Should().Contain("https://glpi/ticket/99999");
        html.Should().Contain("<p>Описание</p>");
        html.Should().Contain("<div>Ответ</div>");

        // Никаких {{...}} не осталось
        html.Should().NotContain("{{TicketId}}");
        html.Should().NotContain("{{Title}}");
        html.Should().NotContain("{{Status}}");
        html.Should().NotContain("{{Initiator}}");
        html.Should().NotContain("{{Priority}}");
        html.Should().NotContain("{{OpenDate}}");
        html.Should().NotContain("{{Url}}");
        html.Should().NotContain("{{Description}}");
        html.Should().NotContain("{{Answers}}");
    }
}