using LiteDB;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Background;

/// <summary>
/// Фоновый сервис чистки старых записей операций.
/// Раз в сутки удаляет записи старше <see cref="OperationOptions.RetentionDays"/>.
/// </summary>
public sealed class OperationRetentionService(
    IOptions<OperationOptions> options,
    ILogger<OperationRetentionService> logger) : BackgroundService
{
    private const string CollectionName = "operations";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);

    private readonly OperationOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<OperationRetentionService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Cleanup();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка чистки старых операций");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Cleanup()
    {
        var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);
        var dbPath = Path.Combine(AppContext.BaseDirectory, "Data", _options.DatabaseFileName);

        if (!File.Exists(dbPath))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("БД операций не найдена: {DbPath}", dbPath);
            return;
        }

        using var db = new LiteDatabase($"Filename={dbPath};Connection=shared");
        var collection = db.GetCollection<OperationRecord>(CollectionName);
        var deleted = collection.DeleteMany(x => x.StartedAt < cutoff);

        if (deleted > 0 && _logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Удалено {Count} операций старше {RetentionDays} дней", deleted, _options.RetentionDays);
    }
}