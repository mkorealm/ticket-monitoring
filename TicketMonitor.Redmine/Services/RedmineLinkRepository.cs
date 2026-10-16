using LiteDB;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Redmine.Models;

namespace TicketMonitor.Redmine.Services;

/// <summary>Репозиторий связей заявок GLPI и задач Redmine.</summary>
public sealed class RedmineLinkRepository : IDisposable
{
    private const string CollectionName = "redmine_links";

    private readonly IOperationRegistry _operationRegistry;
    private readonly ILogger<RedmineLinkRepository> _logger;
    private readonly string _dbPath;
    private readonly Lock _lock = new();
    private LiteDatabase? _db;
    private bool _disposed;

    /// <summary>Инициализирует репозиторий.</summary>
    public RedmineLinkRepository(
        IOperationRegistry operationRegistry,
        ILogger<RedmineLinkRepository> logger)
    {
        _operationRegistry = operationRegistry ?? throw new ArgumentNullException(nameof(operationRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var dataFolder = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataFolder);
        _dbPath = Path.Combine(dataFolder, "redmine_links.db");
    }

    /// <summary>Сохраняет связь (upsert по <c>TicketId</c>).</summary>
    public void Save(RedmineLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentException.ThrowIfNullOrWhiteSpace(link.TicketId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _operationRegistry.RunAsync(
            "Redmine:LinkSave",
            _ =>
            {
                lock (_lock)
                {
                    var db = GetDatabase();
                    var collection = db.GetCollection<RedmineLink>(CollectionName);
                    var existing = collection.FindById(link.TicketId);

                    if (existing is null)
                    {
                        collection.Insert(link);

                        if (_logger.IsEnabled(LogLevel.Information))
                            _logger.LogInformation("Сохранена связь: GLPI #{TicketId} → Redmine #{IssueId} ({IssueKey})",
                                link.TicketId, link.RedmineIssueId, link.RedmineIssueKey);
                    }
                    else
                    {
                        collection.Update(link);

                        if (_logger.IsEnabled(LogLevel.Information))
                            _logger.LogInformation("Обновлена связь: GLPI #{TicketId} → Redmine #{IssueId} ({IssueKey})",
                                link.TicketId, link.RedmineIssueId, link.RedmineIssueKey);
                    }
                }

                return Task.CompletedTask;
            },
            context: new { link.TicketId, link.RedmineIssueId })
            .GetAwaiter().GetResult();
    }

    /// <summary>Возвращает связь по ID заявки GLPI.</summary>
    public RedmineLink? GetByTicketId(string ticketId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _operationRegistry.RunAsync(
            "Redmine:LinkGet",
            _ =>
            {
                lock (_lock)
                {
                    var db = GetDatabase();
                    var collection = db.GetCollection<RedmineLink>(CollectionName);
                    return Task.FromResult(collection.FindById(ticketId));
                }
            },
            context: new { ticketId })
            .GetAwaiter().GetResult();
    }

    /// <summary>Возвращает связь по ID задачи Redmine.</summary>
    public RedmineLink? GetByRedmineIssueId(int issueId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _operationRegistry.RunAsync(
            "Redmine:LinkGetByIssue",
            _ =>
            {
                lock (_lock)
                {
                    var db = GetDatabase();
                    var collection = db.GetCollection<RedmineLink>(CollectionName);
                    return Task.FromResult(collection.FindOne(x => x.RedmineIssueId == issueId));
                }
            },
            context: new { issueId })
            .GetAwaiter().GetResult();
    }

    /// <summary>Удаляет связь по ID заявки GLPI.</summary>
    public bool Remove(string ticketId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _operationRegistry.RunAsync(
            "Redmine:LinkRemove",
            _ =>
            {
                lock (_lock)
                {
                    var db = GetDatabase();
                    var collection = db.GetCollection<RedmineLink>(CollectionName);
                    var deleted = collection.Delete(ticketId);

                    if (deleted && _logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation("Удалена связь для GLPI #{TicketId}", ticketId);

                    return Task.FromResult(deleted);
                }
            },
            context: new { ticketId })
            .GetAwaiter().GetResult();
    }

    /// <summary>Возвращает все связи.</summary>
    public IReadOnlyList<RedmineLink> GetAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _operationRegistry.RunAsync(
            "Redmine:LinkGetAll",
            _ =>
            {
                lock (_lock)
                {
                    var db = GetDatabase();
                    var collection = db.GetCollection<RedmineLink>(CollectionName);
                    return Task.FromResult<IReadOnlyList<RedmineLink>>([.. collection.FindAll()]);
                }
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

        var collection = _db.GetCollection<RedmineLink>(CollectionName);
        collection.EnsureIndex(x => x.TicketId);
        collection.EnsureIndex(x => x.RedmineIssueId);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Открыта база связей Redmine: {DbPath}", _dbPath);

        return _db;
    }
}