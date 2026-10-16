using System.Text;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Services.Formatters;

/// <summary>Форматтер уведомлений для Rocket.Chat (Markdown).</summary>
public sealed class RocketChatMarkdownFormatter(
    ILogger<RocketChatMarkdownFormatter> logger) : INotificationFormatter
{
    private readonly ILogger<RocketChatMarkdownFormatter> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public string FormatName => "rocketchat-markdown";

    /// <inheritdoc />
    public string Format(NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sb = new StringBuilder(capacity: 512);

        sb.AppendLine($"**{EscapeMarkdown(message.Subject)}**");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(message.Ticket.Status))
            sb.AppendLine($"**Статус:** {EscapeMarkdown(message.Ticket.Status)}");

        if (!string.IsNullOrEmpty(message.Ticket.Initiator))
            sb.AppendLine($"**Инициатор:** {EscapeMarkdown(message.Ticket.Initiator)}");

        if (!string.IsNullOrEmpty(message.Ticket.Priority))
            sb.AppendLine($"**Приоритет:** {EscapeMarkdown(message.Ticket.Priority)}");

        if (message.Ticket.OpenDate != default)
            sb.AppendLine($"**Открыта:** {message.Ticket.OpenDate:dd-MM-yyyy HH:mm}");

        if (message.Deltas.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Изменения:**");
            foreach (var delta in message.Deltas)
                sb.AppendLine($"- {EscapeMarkdown(delta)}");
        }

        if (!string.IsNullOrWhiteSpace(message.Description))
        {
            sb.AppendLine();
            sb.AppendLine("**Описание:**");
            sb.AppendLine(EscapeMarkdown(message.Description));
        }

        if (message.Answers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Ответы:**");

            if (message.TotalAnswersCount > message.Answers.Count)
            {
                sb.AppendLine($"_Новых ответов: {message.TotalAnswersCount}, показаны последние {message.Answers.Count}:_");
                sb.AppendLine();
            }

            foreach (var answer in message.Answers)
                sb.AppendLine($"> **{EscapeMarkdown(answer.Author)}:** {EscapeMarkdown(answer.Text)}");
        }

        sb.AppendLine();

        if (!string.IsNullOrEmpty(message.Ticket.Url))
            sb.AppendLine($"🔗 [Открыть заявку]({message.Ticket.Url})");

        foreach (var link in message.ActionLinks)
            sb.AppendLine($"[{EscapeMarkdown(link.Label)}]({link.Url})");

        var result = sb.ToString().TrimEnd();

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Сформировано сообщение Rocket.Chat ({Length} симв.)", result.Length);

        return result;
    }

    /// <summary>Экранирует Markdown-спецсимволы, чтобы пользовательский текст не ломал форматирование.</summary>
    private static string EscapeMarkdown(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("\\", "\\\\")
            .Replace("`", "\\`")
            .Replace("*", "\\*")
            .Replace("_", "\\_")
            .Replace("[", "\\[")
            .Replace("]", "\\]")
            .Replace("(", "\\(")
            .Replace(")", "\\)")
            .Replace("#", "\\#");
    }
}