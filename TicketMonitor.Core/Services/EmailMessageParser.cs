using System.Text;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Services;

/// <summary>Парсер plain-text уведомлений в <see cref="EmailMessageData"/>.</summary>
public sealed class EmailMessageParser : IEmailMessageParser
{
    private const string DescriptionSectionHeader = "Описание:";
    private const string AnswersSectionHeader = "Новые ответы:";
    private const string EmptyDescriptionPlaceholder = "<span style=\"color: #6c757d; font-style: italic;\">Нет описания</span>";
    private const string EmptyAnswersPlaceholder = "<span style=\"color: #6c757d; font-style: italic;\">Нет новых ответов</span>";

    /// <inheritdoc />
    public EmailMessageData Parse(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        var builder = new MessageBuilder();
        var currentSection = Section.None;

        foreach (var rawLine in plainText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith(DescriptionSectionHeader, StringComparison.Ordinal))
            {
                currentSection = Section.Description;
                continue;
            }

            if (line.StartsWith(AnswersSectionHeader, StringComparison.Ordinal))
            {
                currentSection = Section.Answers;
                continue;
            }

            if (currentSection == Section.Answers && line.StartsWith('—'))
            {
                builder.AppendAnswer(line);
                continue;
            }

            if (currentSection != Section.Description && !line.StartsWith('—') && line.Contains(':'))
            {
                currentSection = Section.None;
                builder.AppendKeyValue(line);
                continue;
            }

            if (currentSection == Section.Description)
                builder.AppendDescription(line);
        }

        return builder.Build();
    }

    private sealed class MessageBuilder
    {
        private string _ticketId = string.Empty;
        private string _title = string.Empty;
        private string _status = string.Empty;
        private string _initiator = string.Empty;
        private string _priority = string.Empty;
        private string _openDate = string.Empty;
        private string _url = string.Empty;
        private readonly StringBuilder _description = new();
        private readonly StringBuilder _answers = new();
        private readonly List<string> _actionButtons = [];

        public void AppendKeyValue(string line)
        {
            var parts = line.Split(':', 2);
            var key = parts[0].Trim();
            var value = parts.Length > 1 ? parts[1].Trim() : string.Empty;

            switch (key)
            {
                case "ID": _ticketId = value; break;
                case "Заголовок": _title = value; break;
                case "Статус": _status = value; break;
                case "Инициатор": _initiator = value; break;
                case "Приоритет": _priority = value; break;
                case "Дата открытия": _openDate = value; break;
                case "Ссылка": _url = value; break;
                default:
                    // Строки вида "<label>: <url>" после блока «Ссылка:» — это action-кнопки.
                    // Признак: значение начинается с http:// или https://.
                    if (!string.IsNullOrEmpty(value)
                        && (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                    {
                        _actionButtons.Add($"<p><a href=\"{value}\" target=\"_blank\" rel=\"noopener\">{key}</a></p>");
                    }
                    break;
            }
        }

        public void AppendDescription(string line) => _description.Append(line).Append("<br/>");

        public void AppendAnswer(string line)
        {
            var content = line.TrimStart('—', ' ');
            var separatorIndex = content.IndexOf(':');

            if (separatorIndex < 0)
            {
                _answers.Append($@"<div style=""padding: 4px 0; color: #34495e;"">{line}</div>");
                return;
            }

            var author = content[..separatorIndex].Trim();
            var answerText = content[(separatorIndex + 1)..].Trim();

            _answers.Append($@"
                <div style=""padding: 8px; margin: 8px 0; background-color: #ffffff; border-left: 3px solid #3498db; border-radius: 4px;"">
                    <span style=""font-weight: 600; color: #2c3e50;"">{author}:</span>
                    <span style=""color: #34495e;"">{answerText}</span>
                </div>");
        }

        public EmailMessageData Build() => new()
        {
            TicketId = _ticketId,
            Title = _title,
            Status = _status,
            Initiator = _initiator,
            Priority = _priority,
            OpenDate = _openDate,
            Url = string.IsNullOrEmpty(_url) ? "#" : _url,
            Description = _description.Length == 0 ? EmptyDescriptionPlaceholder : _description.ToString(),
            Answers = _answers.Length == 0 ? EmptyAnswersPlaceholder : _answers.ToString(),
            ActionButton = _actionButtons.Count == 0 ? string.Empty : string.Join(Environment.NewLine, _actionButtons)
        };
    }

    private enum Section { None, Description, Answers }
}