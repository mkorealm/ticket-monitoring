using Microsoft.Extensions.Options;
using Microsoft.Win32;
using Serilog;
using Serilog.Events;
using System.Text;
using TicketMonitor.Core.Background;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Repositories;
using TicketMonitor.Core.Services;
using TicketMonitor.Redmine.Background;
using TicketMonitor.Redmine.Clients;
using TicketMonitor.Redmine.Models;
using TicketMonitor.Redmine.Services;

var builder = WebApplication.CreateBuilder(args);

if (OperatingSystem.IsWindows())
{
    using var serviceKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TicketMonitor-Redmine");
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
        path: Path.Combine(logFolder, "redmine-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 100 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        encoding: Encoding.UTF8,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: Path.Combine(logFolder, "redmine-errors-.log"),
        rollingInterval: RollingInterval.Day,
        restrictedToMinimumLevel: LogEventLevel.Error,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 50 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        encoding: Encoding.UTF8,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Services.AddSerilog();

builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Ticket Monitor Redmine API",
        Description = "Интеграция с Redmine: создание задач по заявкам GLPI, синхронизация статусов",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact { Name = "Королев Максим", Email = "mkorolev@example.ru" },
        Version = "v2"
    });
});

builder.Services.AddOptions<GlpiClientOptions>()
    .Bind(builder.Configuration.GetSection(GlpiClientOptions.SectionName))
    .Validate(o => !string.IsNullOrEmpty(o.BaseUrl), "GLPI:BaseUrl обязателен")
    .Validate(o => o.TimeoutSeconds > 0, "GLPI:TimeoutSeconds должен быть больше 0")
    .ValidateOnStart();

builder.Services.AddOptions<RedmineOptions>()
    .Bind(builder.Configuration.GetSection(RedmineOptions.SectionName))
    .Validate(o => !string.IsNullOrEmpty(o.BaseUrl), "Redmine:BaseUrl обязателен")
    .Validate(o => !string.IsNullOrEmpty(o.ApiToken), "Redmine:ApiToken обязателен (env REDMINE__ApiToken)")
    .Validate(o => o.DefaultProjectId > 0, "Redmine:DefaultProjectId должен быть больше 0")
    .Validate(o => o.DefaultTrackerId > 0, "Redmine:DefaultTrackerId должен быть больше 0")
    .ValidateOnStart();

builder.Services.AddOptions<OperationOptions>()
    .Bind(builder.Configuration.GetSection(OperationOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ServiceName), "Operations:ServiceName обязателен")
    .Validate(o => o.RetentionDays > 0, "Operations:RetentionDays должен быть больше 0")
    .ValidateOnStart();

builder.Services.AddSingleton<IOperationRegistry, LiteDbOperationRegistry>();
builder.Services.AddHostedService<OperationRetentionService>();

builder.Services.AddSingleton<GlpiClient>();

builder.Services.AddHttpClient(GlpiClient.HttpClientName, (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<GlpiClientOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = opts.Timeout;
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    if (!string.IsNullOrEmpty(opts.AppToken)) client.DefaultRequestHeaders.Add("App-Token", opts.AppToken);
});

builder.Services.AddHttpClient(RedmineClient.HttpClientName, (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<RedmineOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("X-Redmine-API-Key", opts.ApiToken);
});

builder.Services.AddSingleton<IRedmineClient, RedmineClient>();
builder.Services.AddSingleton<RedmineLinkRepository>();
builder.Services.AddSingleton<RedminePollStateRepository>();
builder.Services.AddSingleton<GlpiStatusUpdater>();

builder.Services.AddHostedService<GlpiSessionBackgroundService>();
//builder.Services.AddHostedService<RedminePollingWorker>();

builder.Services.AddHealthChecks();

builder.Services.AddWindowsService(options => options.ServiceName = "Ticket Monitor Redmine Service");

var app = builder.Build();

app.UseStaticFiles();

app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health");
app.MapDefaultControllerRoute();

app.Run();