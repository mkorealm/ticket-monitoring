using System.Text;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Services;

/// <summary>Рендерер HTML-шаблона email-уведомления.</summary>
public sealed class EmailTemplateRenderer(ILogger<EmailTemplateRenderer> logger) : IEmailTemplateRenderer
{
    private const string TemplateFolder = "Templates";
    private const string TemplateFileName = "EmailTemplate.html";
    private const string FallbackTemplate = "<html><body><h1>Уведомление</h1><pre>{{Body}}</pre></body></html>";

    private readonly ILogger<EmailTemplateRenderer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<string> RenderAsync(EmailMessageData data, string plainTextFallback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(plainTextFallback);

        var template = await LoadTemplateAsync(cancellationToken).ConfigureAwait(false);

        return template
            .Replace("{{TicketId}}", data.TicketId)
            .Replace("{{Title}}", data.Title)
            .Replace("{{Status}}", data.Status)
            .Replace("{{Initiator}}", data.Initiator)
            .Replace("{{Priority}}", data.Priority)
            .Replace("{{OpenDate}}", data.OpenDate)
            .Replace("{{Url}}", data.Url)
            .Replace("{{Description}}", data.Description)
            .Replace("{{Answers}}", data.Answers)
            .Replace("{{Body}}", plainTextFallback)
            .Replace("{{ActionButton}}", data.ActionButton);
    }

    private async Task<string> LoadTemplateAsync(CancellationToken cancellationToken)
    {
        var templatePath = ResolveTemplatePath();

        if (templatePath is not null)
            return await File.ReadAllTextAsync(templatePath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        _logger.LogWarning("Файл шаблона '{FileName}' не найден. Используется встроенный fallback-шаблон", TemplateFileName);
        return FallbackTemplate;
    }

    private static string? ResolveTemplatePath()
    {
        var deployedPath = Path.Combine(AppContext.BaseDirectory, TemplateFolder, TemplateFileName);
        if (File.Exists(deployedPath)) return deployedPath;

        var sourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "TicketMonitor.Core", TemplateFolder, TemplateFileName);
        return File.Exists(sourcePath) ? sourcePath : null;
    }
}