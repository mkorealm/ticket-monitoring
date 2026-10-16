using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using Serilog;
using Serilog.Events;
using System.Text;
using TicketMonitor.Api.Configuration;
using TicketMonitor.Api.Errors;
using TicketMonitor.Api.HealthChecks;
using TicketMonitor.Core.Background;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Repositories;
using TicketMonitor.Core.Services;
using TicketMonitor.Core.Services.Formatters;

var builder = WebApplication.CreateBuilder(args);

if (OperatingSystem.IsWindows())
{
    using var serviceKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TicketMonitor");
    if (serviceKey?.GetValue("Environment") is string[] envVars)
    {
        foreach (var envVar in envVars)
        {
            var parts = envVar.Split('=', 2);
            if (parts.Length == 2) Environment.SetEnvironmentVariable(parts[0], parts[1]);
        }
    }
}

builder.Configuration.AddEnvironmentVariables();

var appFolder = AppContext.BaseDirectory;
var dataFolder = Path.Combine(appFolder, "Data");
var logFolder = Path.Combine(appFolder, "Logs");
Directory.CreateDirectory(dataFolder);
Directory.CreateDirectory(logFolder);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(logFolder, "ticket-monitor-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 100 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        encoding: Encoding.UTF8,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: Path.Combine(logFolder, "errors-.log"),
        rollingInterval: RollingInterval.Day,
        restrictedToMinimumLevel: LogEventLevel.Error,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 50 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        encoding: Encoding.UTF8,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.Map("MonitorName", (monitorName, writeTo) => writeTo.File(
        path: Path.Combine(logFolder, $"monitor-{monitorName}-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 50 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        encoding: Encoding.UTF8,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"))
    .CreateLogger();

builder.Services.AddSerilog();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Ticket Monitor API",
        Description = "API для мониторинга заявок GLPI",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact { Name = "Королев Максим", Email = "mkorolev@example.ru" },
        Version = "v7"
    });

    var coreXml = Path.Combine(AppContext.BaseDirectory, "TicketMonitor.Core.xml");
    if (File.Exists(coreXml)) c.IncludeXmlComments(coreXml);

    var apiXml = Path.Combine(AppContext.BaseDirectory, "TicketMonitor.Api.xml");
    if (File.Exists(apiXml)) c.IncludeXmlComments(apiXml);
});

builder.Services.AddOptions<GlpiClientOptions>()
    .Bind(builder.Configuration.GetSection(GlpiClientOptions.SectionName))
    .Validate(o => !string.IsNullOrEmpty(o.BaseUrl), "BaseUrl обязателен")
    .Validate(o => o.TimeoutSeconds > 0, "TimeoutSeconds должен быть больше 0")
    .Validate(o => o.MaxRetries > 0, "MaxRetries должен быть больше 0")
    .ValidateOnStart();

builder.Services.AddOptions<BackupOptions>()
    .Bind(builder.Configuration.GetSection(BackupOptions.SectionName))
    .Validate(o => o.IntervalHours > 0, "Интервал бэкапа должен быть больше 0 часов")
    .Validate(o => o.RetentionCount >= 0, "Количество хранимых бэкапов не может быть отрицательным")
    .ValidateOnStart();

builder.Services.AddOptions<RocketChatOptions>()
    .Bind(builder.Configuration.GetSection(RocketChatOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName))
    .Validate(o => o.Port > 0, "Email:Port должен быть больше 0")
    .ValidateOnStart();

builder.Services.AddMemoryCache();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddTransient<GlpiRetryHandler>();

builder.Services.AddHttpClient(GlpiClient.HttpClientName, (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<GlpiClientOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = opts.Timeout;
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    if (!string.IsNullOrEmpty(opts.AppToken)) client.DefaultRequestHeaders.Add("App-Token", opts.AppToken);
})
.AddHttpMessageHandler<GlpiRetryHandler>();

builder.Services.AddSingleton<GlpiClient>();
builder.Services.AddApiRateLimiting();

// ----- Репозитории -----
builder.Services.AddSingleton<ITicketRepository, TicketRepository>();
builder.Services.AddSingleton<IMessageIdRepository, MessageIdRepository>();

// ----- Сервисы Core -----
builder.Services.AddSingleton<IMessageBuilderService, MessageBuilderService>();
builder.Services.AddSingleton<ITicketService, GlpiTicketService>();
builder.Services.AddSingleton<ITicketChangeDetector, TicketChangeDetector>();
builder.Services.AddSingleton<IMonitorManagementService, MonitorManagementService>();
builder.Services.AddSingleton<INotificationChannelResolver, NotificationChannelResolver>();
builder.Services.AddSingleton<IGlpiUserResolver, GlpiUserResolver>();
builder.Services.AddSingleton<IGlpiUserCacheService, GlpiUserCacheService>();
builder.Services.AddSingleton<UserGroupCacheService>();
builder.Services.AddSingleton<NotificationDispatcherService>();
builder.Services.AddSingleton<EmailHtmlFormatter>();
builder.Services.AddSingleton<RocketChatMarkdownFormatter>();
builder.Services.AddSingleton<TicketDetailsCollector>();

// ----- RedmineLinkChecker (проверка связи с Redmine-сервисом) -----
builder.Services.AddOptions<RedmineLinkCheckerOptions>()
    .Bind(builder.Configuration.GetSection(RedmineLinkCheckerOptions.SectionName));

builder.Services.AddHttpClient(RedmineLinkChecker.HttpClientName, (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<RedmineLinkCheckerOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
        client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Services.AddSingleton<IRedmineLinkChecker, RedmineLinkChecker>();

builder.Services.AddOptions<OperationOptions>()
    .Bind(builder.Configuration.GetSection(OperationOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ServiceName), "Operations:ServiceName обязателен")
    .Validate(o => o.RetentionDays > 0, "Operations:RetentionDays должен быть больше 0")
    .ValidateOnStart();

builder.Services.AddSingleton<IOperationRegistry, LiteDbOperationRegistry>();
builder.Services.AddHostedService<OperationRetentionService>();

builder.Services.AddHttpClient(RocketChatClient.HttpClientName, (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<RocketChatOptions>>().Value;
    if (!string.IsNullOrEmpty(opts.ServerUrl)) client.BaseAddress = new Uri(opts.ServerUrl.TrimEnd('/') + "/");
    client.Timeout = opts.Timeout;
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    if (!string.IsNullOrEmpty(opts.Token) && !string.IsNullOrEmpty(opts.UserId))
    {
        client.DefaultRequestHeaders.Add("X-Auth-Token", opts.Token);
        client.DefaultRequestHeaders.Add("X-User-Id", opts.UserId);
    }
});

builder.Services.AddSingleton<IRocketChatClient, RocketChatClient>();
builder.Services.AddSingleton<IEmailMessageParser, EmailMessageParser>();
builder.Services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();
builder.Services.AddSingleton<RocketChatNotificationService>();
builder.Services.AddSingleton<ExchangeEmailService>();
builder.Services.AddSingleton<ChangesStoreService>();
builder.Services.AddSingleton<HealthService>();

// ----- Health-проверки внешних сервисов -----
builder.Services.AddSingleton<IExternalServiceProbe, GlpiHealthProbe>();
builder.Services.AddSingleton<IExternalServiceProbe, RocketChatHealthProbe>();
builder.Services.AddSingleton<IExternalServiceProbe, RedmineHealthProbe>();

builder.Services.AddSingleton<IBackupService, BackupService>();
builder.Services.AddHostedService<BackupBackgroundService>();
builder.Services.AddHostedService<TicketMonitorBackgroundService>();

builder.Services.AddHealthChecks()
    .AddCheck<ExternalServicesHealthCheck>("external-services");

builder.Services.AddWindowsService(options => options.ServiceName = "TicketMonitor");

var app = builder.Build();

app.UseExceptionHandler();
app.UseRateLimiter();
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers().RequireRateLimiting(RateLimitingConfiguration.DefaultPolicyName);

// ----- /healthz с детальным JSON-ответом -----
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var response = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration,
            checkedAt = DateTime.UtcNow,
            entries = report.Entries.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    status = kvp.Value.Status.ToString(),
                    duration = kvp.Value.Duration,
                    description = kvp.Value.Description,
                    error = kvp.Value.Exception?.Message,
                    data = kvp.Value.Data
                })
        };

        await context.Response.WriteAsJsonAsync(response);
    }
});

app.Run();