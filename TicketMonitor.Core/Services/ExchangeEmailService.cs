using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Services;

/// <summary>Сервис отправки email через SMTP.</summary>
public sealed class ExchangeEmailService(
    IOptions<EmailOptions> options,
    IEmailMessageParser parser,
    IEmailTemplateRenderer renderer,
    IOperationRegistry operationRegistry,
    ILogger<ExchangeEmailService> logger) : INotificationService
{
    private readonly EmailOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly IEmailMessageParser _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    private readonly IEmailTemplateRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly IOperationRegistry _operationRegistry = operationRegistry ?? throw new ArgumentNullException(nameof(operationRegistry));
    private readonly ILogger<ExchangeEmailService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<string?> SendNotificationAsync(string subject, string message, CancellationToken cancellationToken = default)
    {
        return await _operationRegistry.RunAsync(
            "Email:Send",
            async ct =>
            {
                try
                {
                    if (!ValidateOptionsOrLog())
                        return null;

                    var data = _parser.Parse(message);
                    var htmlBody = await _renderer.RenderAsync(data, plainTextFallback: message, ct).ConfigureAwait(false);

                    await SendMimeAsync(subject, message, htmlBody, ct).ConfigureAwait(false);

                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation("Email отправлен: {Subject}", subject);

                    return (string?)null;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _logger.LogInformation("Отправка email отменена");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при отправке email");
                    return null;
                }
            },
            context: new { subject },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Отправляет email с готовым HTML-телом.</summary>
    /// <returns><c>true</c>, если отправка прошла успешно.</returns>
    public async Task<bool> SendHtmlAsync(
        string subject,
        string htmlBody,
        string plainTextFallback,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(htmlBody);

        return await _operationRegistry.RunAsync(
            "Email:SendHtml",
            async ct =>
            {
                try
                {
                    if (!ValidateOptionsOrLog())
                        return false;

                    await SendMimeAsync(subject, plainTextFallback, htmlBody, ct).ConfigureAwait(false);

                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation("Email (HTML) отправлен: {Subject}", subject);

                    return true;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _logger.LogInformation("Отправка HTML email отменена");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при отправке HTML email");
                    return false;
                }
            },
            context: new { subject },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private bool ValidateOptionsOrLog()
    {
        if (_options.Recipients.Count == 0)
        {
            _logger.LogWarning("Не указаны получатели email (Email:To)");
            return false;
        }

        if (string.IsNullOrEmpty(_options.From))
        {
            _logger.LogWarning("Не указан отправитель email (Email:From)");
            return false;
        }

        if (string.IsNullOrEmpty(_options.SmtpServer))
        {
            _logger.LogWarning("Не указан SMTP-сервер (Email:SmtpServer)");
            return false;
        }

        return true;
    }

    private async Task SendMimeAsync(string subject, string plainText, string htmlBody, CancellationToken cancellationToken)
    {
        var email = new MimeMessage();
        email.From.Add(new MailboxAddress("Ticket Monitor", _options.From));

        foreach (var recipient in _options.Recipients)
            email.To.Add(new MailboxAddress("", recipient));

        email.Subject = subject;
        email.Body = new BodyBuilder { TextBody = plainText, HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();

        await client.ConnectAsync(_options.SmtpServer, _options.Port, SecureSocketOptions.None, cancellationToken).ConfigureAwait(false);
        await client.SendAsync(email, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
    }
}