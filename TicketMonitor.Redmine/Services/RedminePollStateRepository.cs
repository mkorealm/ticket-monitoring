using LiteDB;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Redmine.Models;

namespace TicketMonitor.Redmine.Services;

/// <summary>Репозиторий состояния polling (LiteDB).</summary>
public sealed class RedminePollStateRepository : IDisposable
{
    private const string CollectionName = "poll_state";
    private const string SingletonId = "singleton";

    private readonly IOperationRegistry _operationRegistry;
    private readonly ILogger<RedminePollStateRepository> _logger;
    private readonly string _dbPath;
    private readonly Lock _lock = new();
    private LiteDatabase? _db;
    private bool _disposed;

    /// <summary>Инициализирует репозиторий.</summary>
    public RedminePollStateRepository(
        IOperationRegistry operationRegistry,
        ILogger<RedminePollStateRepository> logger)
    {
        _operationRegistry = operationRegistry ?? throw new ArgumentNullException(nameof(operationRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var dataFolder = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataFolder);
        _dbPath = Path.Combine(dataFolder, "redmine_state.db");
    }

    /// <summary>Возвращает состояние или <c>null</c>, если polling ещё не запускался.</summary>
    public RedminePollState? Get()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _operationRegistry.RunAsync(
            "Redmine:PollStateGet",
            _ =>
            {
                lock (_lock)
                {
                    var db = GetDatabase();
                    var collection = db.GetCollection<RedminePollState>(CollectionName);
                    return Task.FromResult(collection.FindById(SingletonId));
                }
            })
            .GetAwaiter().GetResult();
    }

    /// <summary>Сохраняет состояние (upsert).</summary>
    public void Save(RedminePollState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _operationRegistry.RunAsync(
            "Redmine:PollStateSave",
            _ =>
            {
                lock (_lock)
                {
                    var db = GetDatabase();
                    var collection = db.GetCollection<RedminePollState>(CollectionName);

                    var existing = collection.FindById(SingletonId);
                    if (existing is null)
                        collection.Insert(state with { Id = SingletonId });
                    else
                        collection.Update(state with { Id = SingletonId });

                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug("Состояние polling сохранено: LastPoll={LastPoll}, Count={Count}",
                            state.LastPollTimestamp, state.LastProcessedCount);
                }

                return Task.CompletedTask;
            })
            .GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            _db?.Dispose();
            _db = null;
        }

        _disposed = true;
    }

    private LiteDatabase GetDatabase()
    {
        if (_db is not null)
            return _db;

        var connectionString = $"Filename={_dbPath};Connection=shared";
        _db = new LiteDatabase(connectionString);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Открыта база состояния polling: {DbPath}", _dbPath);

        return _db;
    }
}