using FluentAssertions;
using TicketMonitor.Core.Services;
using Xunit;

namespace TicketMonitor.Core.Tests;

public class EmailMessageParserTests
{
    private readonly EmailMessageParser _sut = new();

    // -------------------------------------------------------------------------
    // Пустой ввод / защита от null
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_NullInput_ThrowsArgumentNullException()
    {
        var act = () => _sut.Parse(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Parse_EmptyString_ReturnsAllFallbacks()
    {
        var result = _sut.Parse(string.Empty);

        result.TicketId.Should().BeEmpty();
        result.Title.Should().BeEmpty();
        result.Status.Should().BeEmpty();
        result.Url.Should().Be("#");
        result.Description.Should().Contain("Нет описания");
        result.Answers.Should().Contain("Нет новых ответов");
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsAllFallbacks()
    {
        var result = _sut.Parse("   \n   \n\t\n   ");

        result.Url.Should().Be("#");
        result.Description.Should().Contain("Нет описания");
        result.Answers.Should().Contain("Нет новых ответов");
    }

    // -------------------------------------------------------------------------
    // Только базовые поля
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_AllBaseFields_ExtractsThem()
    {
        const string text =
            "ID: 12345\n" +
            "Заголовок: Не работает принтер\n" +
            "Статус: Новая\n" +
            "Инициатор: Иванов И.И.\n" +
            "Приоритет: Высокий\n" +
            "Дата открытия: 15-09-2026 10:30\n" +
            "Ссылка: https://glpi/ticket/12345";

        var result = _sut.Parse(text);

        result.TicketId.Should().Be("12345");
        result.Title.Should().Be("Не работает принтер");
        result.Status.Should().Be("Новая");
        result.Initiator.Should().Be("Иванов И.И.");
        result.Priority.Should().Be("Высокий");
        result.OpenDate.Should().Be("15-09-2026 10:30");
        result.Url.Should().Be("https://glpi/ticket/12345");
    }

    [Fact]
    public void Parse_NoUrl_UsesHashFallback()
    {
        const string text = "ID: 1\nСтатус: Новая";

        var result = _sut.Parse(text);

        result.Url.Should().Be("#");
    }

    [Fact]
    public void Parse_ValueWithColonInside_KeepsWholeValue()
    {
        // Ссылка содержит двоеточие — Split(':', 2) должен сохранить всё после первого ':'
        const string text = "Ссылка: https://glpi/ticket/12345?param=1";

        var result = _sut.Parse(text);

        result.Url.Should().Be("https://glpi/ticket/12345?param=1");
    }

    [Fact]
    public void Parse_SpacesAroundKeyAndValue_Trimmed()
    {
        const string text = "   ID:    12345   \n   Статус:    Решена   ";

        var result = _sut.Parse(text);

        result.TicketId.Should().Be("12345");
        result.Status.Should().Be("Решена");
    }

    // -------------------------------------------------------------------------
    // Секция "Описание"
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_DescriptionSection_SingleLine()
    {
        const string text =
            "ID: 1\n" +
            "Описание:\n" +
            "Принтер не печатает уже второй день.";

        var result = _sut.Parse(text);

        result.Description.Should().Contain("Принтер не печатает уже второй день.");
        result.Description.Should().Contain("<br/>");
        result.Description.Should().NotContain("Нет описания");
    }

    [Fact]
    public void Parse_DescriptionSection_MultiLine_JoinedWithBr()
    {
        const string text =
            "Описание:\n" +
            "Первая строка.\n" +
            "Вторая строка.\n" +
            "Третья строка.";

        var result = _sut.Parse(text);

        result.Description.Should().Contain("Первая строка.");
        result.Description.Should().Contain("Вторая строка.");
        result.Description.Should().Contain("Третья строка.");
        result.Description.Split("<br/>").Length.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void Parse_DescriptionSection_Empty_UsesFallback()
    {
        const string text = "Описание:\n";

        var result = _sut.Parse(text);

        result.Description.Should().Contain("Нет описания");
    }

    // -------------------------------------------------------------------------
    // Секция "Новые ответы"
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_AnswersSection_SingleAnswer()
    {
        const string text =
            "Новые ответы:\n" +
            "— Иванов И.И.: Проблема решена.";

        var result = _sut.Parse(text);

        result.Answers.Should().Contain("Иванов И.И.");
        result.Answers.Should().Contain("Проблема решена.");
        result.Answers.Should().NotContain("Нет новых ответов");
    }

    [Fact]
    public void Parse_AnswersSection_MultipleAnswers()
    {
        const string text =
            "Новые ответы:\n" +
            "— Иванов И.И.: Проблема решена.\n" +
            "— Петров П.П.: Проверил, всё работает.";

        var result = _sut.Parse(text);

        result.Answers.Should().Contain("Иванов И.И.");
        result.Answers.Should().Contain("Проблема решена.");
        result.Answers.Should().Contain("Петров П.П.");
        result.Answers.Should().Contain("Проверил, всё работает.");
    }

    [Fact]
    public void Parse_AnswersSection_AnswerWithoutColon_AddedAsIs()
    {
        const string text =
            "Новые ответы:\n" +
            "— Просто текст без двоеточия";

        var result = _sut.Parse(text);

        result.Answers.Should().Contain("Просто текст без двоеточия");
    }

    [Fact]
    public void Parse_AnswersSection_AnswerWithColonInsideText_KeepsWholeText()
    {
        const string text =
            "Новые ответы:\n" +
            "— Иванов: Смотрите лог: ошибка в строке 42.";

        var result = _sut.Parse(text);

        result.Answers.Should().Contain("Иванов");
        result.Answers.Should().Contain("Смотрите лог: ошибка в строке 42.");
    }

    // -------------------------------------------------------------------------
    // Смешанные сценарии
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_FullMessage_AllSectionsExtracted()
    {
        const string text =
            "ID: 12345\n" +
            "Заголовок: Не работает принтер\n" +
            "Статус: Новая\n" +
            "Инициатор: Иванов И.И.\n" +
            "Приоритет: Высокий\n" +
            "Дата открытия: 15-09-2026 10:30\n" +
            "Ссылка: https://glpi/ticket/12345\n" +
            "\n" +
            "Описание:\n" +
            "Принтер не печатает.\n" +
            "Перезагрузка не помогла.\n" +
            "\n" +
            "Новые ответы:\n" +
            "— Иванов И.И.: Проверил кабель, всё в порядке.";

        var result = _sut.Parse(text);

        result.TicketId.Should().Be("12345");
        result.Title.Should().Be("Не работает принтер");
        result.Status.Should().Be("Новая");
        result.Initiator.Should().Be("Иванов И.И.");
        result.Priority.Should().Be("Высокий");
        result.OpenDate.Should().Be("15-09-2026 10:30");
        result.Url.Should().Be("https://glpi/ticket/12345");
        result.Description.Should().Contain("Принтер не печатает.");
        result.Description.Should().Contain("Перезагрузка не помогла.");
        result.Answers.Should().Contain("Иванов И.И.");
        result.Answers.Should().Contain("Проверил кабель, всё в порядке.");
    }

    [Fact]
    public void Parse_DashInDescriptionSection_TreatedAsText()
    {
        // "—" в описании (не в секции ответов) должен попасть в description
        const string text =
            "Описание:\n" +
            "— Это просто тире в тексте описания.";

        var result = _sut.Parse(text);

        // В описании не будет HTML-форматирования ответов
        result.Answers.Should().Contain("Нет новых ответов");
        // Тире в описании обрабатывается как обычная строка
        result.Description.Should().Contain("— Это просто тире в тексте описания.");
    }

    [Fact]
    public void Parse_UnknownKey_Ignored()
    {
        const string text =
            "ID: 1\n" +
            "Неизвестное поле: значение\n" +
            "Заголовок: Тест";

        var result = _sut.Parse(text);

        result.TicketId.Should().Be("1");
        result.Title.Should().Be("Тест");
        // "Неизвестное поле" никуда не попадает — просто игнорируется
    }

    [Fact]
    public void Parse_EmptyLinesSkipped()
    {
        const string text =
            "ID: 1\n" +
            "\n" +
            "\n" +
            "Заголовок: Тест\n" +
            "\n";

        var result = _sut.Parse(text);

        result.TicketId.Should().Be("1");
        result.Title.Should().Be("Тест");
    }

    // -------------------------------------------------------------------------
    // Секция без заголовка
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_AnswerSectionEndsAtNextKey()
    {
        // После секции ответов идёт строка "ID: ..." — она не должна попасть в ответы
        const string text =
            "Новые ответы:\n" +
            "— Иванов: Тест.\n" +
            "\n" +
            "Ссылка: https://glpi/ticket/1";

        var result = _sut.Parse(text);

        result.Answers.Should().Contain("Иванов");
        result.Answers.Should().NotContain("https://glpi/ticket/1");
        result.Url.Should().Be("https://glpi/ticket/1");
    }

    // -------------------------------------------------------------------------
    // Содержимое ответа
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_AnswerContainsAuthorAndContent()
    {
        const string text =
            "Новые ответы:\n" +
            "— Петров П.П.: Комментарий к заявке.";

        var result = _sut.Parse(text);

        // Автор и текст должны быть разделены в HTML
        result.Answers.Should().Contain("<span");
        result.Answers.Should().Contain("Петров П.П.:");
        result.Answers.Should().Contain("Комментарий к заявке.");
    }

    // -------------------------------------------------------------------------
    // Whitespace / trimming
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_TrailingWhitespaceInValue_Trimmed()
    {
        const string text = "Заголовок:    Тестовое название   ";

        var result = _sut.Parse(text);

        result.Title.Should().Be("Тестовое название");
    }

    [Fact]
    public void Parse_MultipleColonsInValue_KeepsAllButFirstColon()
    {
        const string text = "Заголовок: Тест: с двоеточием: внутри";

        var result = _sut.Parse(text);

        result.Title.Should().Be("Тест: с двоеточием: внутри");
    }
}