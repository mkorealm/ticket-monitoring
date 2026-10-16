using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Services.Formatters;

/// <summary>
/// Форматтер уведомлений для email (HTML).
/// Использует <see cref="IEmailTemplateRenderer"/> для подстановки в шаблон.
/// </summary>
public sealed class EmailHtmlFormatter(
    IEmailTemplateRenderer renderer,
    ILogger<EmailHtmlFormatter> logger) : INotificationFormatter
{
    private readonly IEmailTemplateRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly ILogger<EmailHtmlFormatter> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public string FormatName => "email-html";

    /// <inheritdoc />
    public string Format(NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var data = new EmailMessageData
        {
            TicketId = WebUtility.HtmlEncode(message.Ticket.Id),
            Title = WebUtility.HtmlEncode(message.Ticket.Title),
            Status = WebUtility.HtmlEncode(message.Ticket.Status),
            Initiator = WebUtility.HtmlEncode(message.Ticket.Initiator),
            Priority = WebUtility.HtmlEncode(message.Ticket.Priority),
            OpenDate = message.Ticket.OpenDate == default
                ? string.Empty
                : message.Ticket.OpenDate.ToString("dd-MM-yyyy HH:mm"),
            Url = WebUtility.HtmlEncode(message.Ticket.Url),
            Description = FormatDescription(message.Description),
            Answers = FormatAnswers(message.Answers, message.TotalAnswersCount),
            ActionButton = FormatActionButtons(message.ActionLinks),
        };

        var plainText = BuildPlainText(message);

        var html = _renderer.RenderAsync(data, plainText)
            .GetAwaiter()
            .GetResult();

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Сформирован HTML-email ({Length} симв.)", html.Length);

        return html;
    }

    /// <summary>
    /// Формирует HTML-разметку для всех кнопок-действий.
    /// Для Outlook (Word-движок) используется <b>VML</b>, для остальных — обычный <c>&lt;a&gt;</c>.
    /// </summary>
    private static string FormatActionButtons(IReadOnlyList<TicketActionLink> actionLinks)
    {
        if (actionLinks.Count == 0)
            return string.Empty;

        var sb = new StringBuilder(capacity: 1024);
        foreach (var link in actionLinks)
            sb.Append(FormatActionButton(link));

        return sb.ToString();
    }

    private static string FormatActionButton(TicketActionLink actionLink)
    {
        var url = WebUtility.HtmlEncode(actionLink.Url);
        var label = WebUtility.HtmlEncode(actionLink.Label);

        const string ButtonColor = "#2980b9";
        const string ButtonBorderColor = "#2471a3";

        return $"""
        <table role="presentation" cellspacing="0" cellpadding="0" border="0" style="margin: 24px 0 8px 0;">
            <tr>
                <td align="left" style="padding: 0;">
                    <!--[if mso]>
                    <v:roundrect xmlns:v="urn:schemas-microsoft-com:vml"
                                 xmlns:w="urn:schemas-microsoft-com:office:word"
                                 href="{url}"
                                 style="height:48px;v-text-anchor:middle;width:320px;"
                                 arcsize="12%"
                                 strokecolor="{ButtonBorderColor}"
                                 fillcolor="{ButtonColor}">
                        <w:anchorlock/>
                        <center style="color:#ffffff;font-family:Arial,sans-serif;font-size:15px;font-weight:bold;">
                            {label}
                        </center>
                    </v:roundrect>
                    <![endif]-->
                    <!--[if !mso]><!-->
                    <a href="{url}"
                       target="_blank"
                       rel="noopener"
                       style="display: inline-block;
                              padding: 14px 28px;
                              font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Arial, sans-serif;
                              font-size: 15px;
                              font-weight: 700;
                              line-height: 1.2;
                              color: #ffffff;
                              background-color: {ButtonColor};
                              text-decoration: none;
                              border-radius: 6px;
                              border: 1px solid {ButtonBorderColor};
                              mso-padding-alt: 14px 28px;
                              text-align: center;
                              min-width: 260px;">
                        {label}
                    </a>
                    <!--<![endif]-->
                </td>
            </tr>
        </table>
        """;
    }

    private static string FormatDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "<span style=\"color: #6c757d; font-style: italic;\">Нет описания</span>";

        var encoded = WebUtility.HtmlEncode(description);
        return encoded.Replace("\r\n", "<br/>").Replace("\n", "<br/>");
    }

    private static string FormatAnswers(IReadOnlyList<NotificationAnswer> answers, int totalCount)
    {
        if (answers.Count == 0)
            return "<span style=\"color: #6c757d; font-style: italic;\">Нет новых ответов</span>";

        var sb = new StringBuilder(capacity: 512);

        if (totalCount > answers.Count)
        {
            sb.Append("<div style=\"color: #6c757d; font-style: italic; margin-bottom: 8px;\">");
            sb.Append($"Новых ответов: {totalCount}, показаны последние {answers.Count}:");
            sb.Append("</div>");
        }

        foreach (var answer in answers)
        {
            sb.Append("<div style=\"padding: 8px; margin: 8px 0; background-color: #ffffff; border-left: 3px solid #3498db; border-radius: 4px;\">");
            sb.Append($"<span style=\"font-weight: 600; color: #2c3e50;\">{WebUtility.HtmlEncode(answer.Author)}:</span> ");
            sb.Append($"<span style=\"color: #34495e;\">{WebUtility.HtmlEncode(answer.Text)}</span>");
            sb.Append("</div>");
        }

        return sb.ToString();
    }

    private static string BuildPlainText(NotificationMessage message)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ID: {message.Ticket.Id}");
        sb.AppendLine($"Заголовок: {message.Ticket.Title}");
        sb.AppendLine($"Статус: {message.Ticket.Status}");

        if (!string.IsNullOrEmpty(message.Ticket.Initiator))
            sb.AppendLine($"Инициатор: {message.Ticket.Initiator}");

        sb.AppendLine($"Приоритет: {message.Ticket.Priority}");

        if (message.Ticket.OpenDate != default)
            sb.AppendLine($"Дата открытия: {message.Ticket.OpenDate:dd-MM-yyyy HH:mm}");

        sb.AppendLine($"Ссылка: {message.Ticket.Url}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(message.Description))
        {
            sb.AppendLine("Описание:");
            sb.AppendLine(message.Description);
            sb.AppendLine();
        }

        if (message.Answers.Count > 0)
        {
            sb.AppendLine("Новые ответы:");

            if (message.TotalAnswersCount > message.Answers.Count)
                sb.AppendLine($"(всего {message.TotalAnswersCount}, показаны последние {message.Answers.Count})");

            foreach (var answer in message.Answers)
                sb.AppendLine($"— {answer.Author}: {answer.Text}");
        }

        if (message.ActionLinks.Count > 0)
        {
            sb.AppendLine();
            foreach (var link in message.ActionLinks)
                sb.AppendLine($"{link.Label}: {link.Url}");
        }

        return sb.ToString().TrimEnd();
    }
}