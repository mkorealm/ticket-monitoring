using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using Serilog;
using Serilog.Events;
using System.Text;
using TicketMonitor.Watchdog.Configuration;
using TicketMonitor.Watchdog.Services;
using TicketMonitor.Watchdog.Workers;

var builder = WebApplication.CreateBuilder(args);

if (OperatingSystem.IsWindows())
{
    using var serviceKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TicketMonitor-Watchdog");
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
        path: Path.Combine(logFolder, "watchdog-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 100 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        encoding: Encoding.UTF8,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: Path.Combine(logFolder, "watchdog-errors-.log"),
        rollingInterval: RollingInterval.Day,
        restrictedToMinimumLevel: LogEventLevel.Error,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 50 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        encoding: Encoding.UTF8,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Services.AddSerilog();

builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Ticket Monitor Watchdog API",
        Description = "Сервис мониторинга работоспособности TicketMonitor",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact { Name = "Королев Максим", Email = "mkorolev@example.ru" },
        Version = "v1"
    });
});

builder.Services.AddOptions<WatchdogOptions>()
    .Bind(builder.Configuration.GetSection(WatchdogOptions.SectionName))
    .Validate(o => o.HealthCheckIntervalSeconds > 0, "Watchdog:HealthCheckIntervalSeconds должен быть больше 0")
    .Validate(o => o.OperationsCheckIntervalSeconds > 0, "Watchdog:OperationsCheckIntervalSeconds должен быть больше 0")
    .Validate(o => o.FailureThreshold > 0, "Watchdog:FailureThreshold должен быть больше 0")
    .Validate(o => o.HttpRawRetentionDays > 0, "Watchdog:HttpRawRetentionDays должен быть больше 0")
    .Validate(o => o.HttpAggregateRetentionDays > 0, "Watchdog:HttpAggregateRetentionDays должен быть больше 0")
    .Validate(o => o.OperationsAggregateRetentionDays > 0, "Watchdog:OperationsAggregateRetentionDays должен быть больше 0")
    .Validate(o => o.Services.Count > 0, "Watchdog:Services должен содержать хотя бы один сервис")
    .ValidateOnStart();

builder.Services.AddHttpClient();

builder.Services.AddSingleton<OperationTypeDisplayNameProvider>();
builder.Services.AddSingleton<WatchdogRepository>();
builder.Services.AddSingleton<OperationsDbReader>();
builder.Services.AddSingleton<HttpHealthChecker>();
builder.Services.AddSingleton<DowntimeTracker>();
builder.Services.AddSingleton<OperationsAggregator>();

builder.Services.AddHostedService<HttpMonitorWorker>();
builder.Services.AddHostedService<OperationsMonitorWorker>();
builder.Services.AddHostedService<RetentionWorker>();

builder.Services.AddWindowsService(options => options.ServiceName = "Ticket Monitor Watchdog Service");

var app = builder.Build();

app.UseStaticFiles();
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapRazorPages();

app.Run();