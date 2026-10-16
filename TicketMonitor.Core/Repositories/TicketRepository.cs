using LiteDB;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Repositories;

/// <summary>Репозиторий заявок на основе LiteDB.</summary>
public sealed class TicketRepository(
    IOperationRegistry operationRegistry,
    ILogger<TicketRepository> logger) : ITicketRepository
{
    private const string CollectionName = "tickets";

    private readonly IOperationRegistry _operationRegistry = operationRegistry ?? throw new ArgumentNullException(nameof(operationRegistry));
    private readonly ILogger<TicketRepository> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly string _dataFolder = Path.Combine(AppContext.BaseDirectory, "Data");

    /// <inheritdoc />
    public void SaveTickets(List<TicketInfo> tickets, string dbFileName)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        ArgumentException.ThrowIfNullOrWhiteSpace(dbFileName);

        _operationRegistry.RunAsync(
            "DB:SaveTickets",
            _ =>
            {
                Directory.CreateDirectory(_dataFolder);

                using var db = new LiteDatabase(GetConnectionString(dbFileName));
                var collection = db.GetCollection<TicketInfo>(CollectionName);
                collection.EnsureIndex(t => t.Id);

                db.BeginTrans();
                try
                {
                    collection.DeleteAll();
                    collection.InsertBulk(tickets);
                    db.Commit();

                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation("Сохранено {Count} заявок в базу {DbFileName}", tickets.Count, dbFileName);
                }
                catch (Exception ex)
                {
                    db.Rollback();
                    _logger.LogError(ex, "Ошибка при сохранении заявок в базу {DbFileName}", dbFileName);
                    throw;
                }

                return Task.CompletedTask;
            },
            context: new { count = tickets.Count, dbFileName })
            .GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public IReadOnlyList<TicketInfo> LoadTickets(string dbFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbFileName);

        return _operationRegistry.RunAsync(
            "DB:LoadTickets",
            _ =>
            {
                Directory.CreateDirectory(_dataFolder);

                using var db = new LiteDatabase(GetConnectionString(dbFileName));
                var collection = db.GetCollection<TicketInfo>(CollectionName);
                var result = collection.FindAll().ToList();

                return Task.FromResult<IReadOnlyList<TicketInfo>>(result);
            },
            context: new { dbFileName })
            .GetAwaiter().GetResult();
    }

    private string GetConnectionString(string dbFileName)
    {
        var dbPath = Path.Combine(_dataFolder, dbFileName);
        return $"Filename={dbPath};Connection=shared";
    }
}