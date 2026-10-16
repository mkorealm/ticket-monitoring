using System.Diagnostics;
using System.Text.Json;
using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Services;

/// <summary>
/// Реализация <see cref="IOperationRegistry"/> поверх LiteDB.
/// Пишет запись об операции в отдельную БД (<c>Data/operations.db</c> по умолчанию).
/// </summary>
public sealed class LiteDbOperationRegistry : IOperationRegistry, IDisposable
{
    private const string CollectionName = "operations";
    private const int WriteBatchSize = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

    private readonly OperationOptions _options;
    private readonly ILogger<LiteDbOperationRegistry> _logger;
    private readonly string _dbPath;

    private readonly Lock _bufferLock = new();
    private readonly List<OperationRecord> _buffer = [];
    private readonly Timer _flushTimer;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private bool _disposed;

    /// <summary>Инициализирует реестр.</summary>
    public LiteDbOperationRegistry(
        IOptions<OperationOptions> options,
        ILogger<LiteDbOperationRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(_options.ServiceName))
            throw new InvalidOperationException("Operations:ServiceName не задан");

        var dataFolder = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataFolder);
        _dbPath = Path.Combine(dataFolder, _options.DatabaseFileName);

        _flushTimer = new Timer(
            callback: _ => FlushAsync().GetAwaiter().GetResult(),
            state: null,
            dueTime: FlushInterval,
            period: FlushInterval);
    }

    /// <inheritdoc />
    public async Task<T> RunAsync<T>(
        string type,
        Func<CancellationToken, Task<T>> action,
        object? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(action);

        var startedAt = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await action(cancellationToken).ConfigureAwait(false);

            sw.Stop();
            EnqueueRecord(type, startedAt, sw.ElapsedMilliseconds, "Success", null, null, context);

            return result;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            EnqueueRecord(type, startedAt, sw.ElapsedMilliseconds, "Cancelled", "Operation cancelled", null, context);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            EnqueueRecord(type, startedAt, sw.ElapsedMilliseconds, "Failed", ex.Message, ex.ToString(), context);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RunAsync(
        string type,
        Func<CancellationToken, Task> action,
        object? context = null,
        CancellationToken cancellationToken = default)
    {
        await RunAsync<object?>(
            type,
            async ct =>
            {
                await action(ct).ConfigureAwait(false);
                return null;
            },
            context,
            cancellationToken).ConfigureAwait(false);
    }

    private void EnqueueRecord(
        string type,
        DateTime startedAt,
        long durationMs,
        string status,
        string? errorMessage,
        string? stackTrace,
        object? context)
    {
        var record = new OperationRecord
        {
            Type = type,
            ServiceName = _options.ServiceName,
            Status = status,
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
            DurationMs = durationMs,
            ErrorMessage = Truncate(errorMessage, _options.MaxErrorMessageLength),
            StackTrace = Truncate(stackTrace, _options.MaxStackTraceLength),
            ContextJson = Truncate(SerializeContext(context), _options.MaxContextJsonLength)
        };

        lock (_bufferLock)
        {
            _buffer.Add(record);
        }
    }

    /// <summary>Сбрасывает буфер записей в LiteDB.</summary>
    public async Task FlushAsync()
    {
        List<OperationRecord> toWrite;
        lock (_bufferLock)
        {
            if (_buffer.Count == 0) return;
            toWrite = [.. _buffer];
            _buffer.Clear();
        }

        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            using var db = new LiteDatabase($"Filename={_dbPath};Connection=shared");
            var collection = db.GetCollection<OperationRecord>(CollectionName);
            collection.EnsureIndex(x => x.StartedAt);
            collection.EnsureIndex(x => x.Type);
            collection.EnsureIndex(x => x.Status);

            collection.InsertBulk(toWrite);

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Записано {Count} операций в {DbPath}", toWrite.Count, _dbPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось записать {Count} операций в {DbPath}", toWrite.Count, _dbPath);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _flushTimer.Dispose();

        try
        {
            FlushAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка финального сброса операций");
        }

        _writeGate.Dispose();
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string? SerializeContext(object? context)
    {
        if (context is null) return null;

        try
        {
            return System.Text.Json.JsonSerializer.Serialize(context);
        }
        catch
        {
            return null;
        }
    }
}