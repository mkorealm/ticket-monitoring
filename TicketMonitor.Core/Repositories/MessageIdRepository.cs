using LiteDB;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Repositories;

/// <summary>Репозиторий MessageId (ID сообщений во внешних системах) на основе LiteDB.</summary>
public sealed class MessageIdRepository(
    IOperationRegistry operationRegistry,
    ILogger<MessageIdRepository> logger) : IMessageIdRepository
{
    private const string CollectionName = "messageIds";

    private readonly IOperationRegistry _operationRegistry = operationRegistry ?? throw new ArgumentNullException(nameof(operationRegistry));
    private readonly ILogger<MessageIdRepository> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly string _dataFolder = Path.Combine(AppContext.BaseDirectory, "Data");

    /// <inheritdoc />
    public string? GetMessageId(string ticketId, string dbFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dbFileName);

        return _operationRegistry.RunAsync(
            "DB:GetMessageId",
            _ =>
            {
                Directory.CreateDirectory(_dataFolder);

                using var db = new LiteDatabase(GetConnectionString(dbFileName));
                var collection = db.GetCollection<MessageIdRecord>(CollectionName);
                collection.EnsureIndex(m => m.TicketId);
                return Task.FromResult(collection.FindById(ticketId)?.MessageId);
            },
            context: new { ticketId, dbFileName })
            .GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void SaveMessageId(string ticketId, string messageId, string dbFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dbFileName);

        _operationRegistry.RunAsync(
            "DB:SaveMessageId",
            _ =>
            {
                Directory.CreateDirectory(_dataFolder);

                using var db = new LiteDatabase(GetConnectionString(dbFileName));
                var collection = db.GetCollection<MessageIdRecord>(CollectionName);
                collection.EnsureIndex(m => m.TicketId);

                db.BeginTrans();
                try
                {
                    var record = collection.FindById(ticketId);
                    if (record is null)
                    {
                        record = new MessageIdRecord { TicketId = ticketId, MessageId = messageId };
                        collection.Insert(record);
                    }
                    else
                    {
                        record.MessageId = messageId;
                        collection.Update(record);
                    }
                    db.Commit();
                }
                catch (Exception ex)
                {
                    db.Rollback();
                    _logger.LogError(ex, "Ошибка при сохранении MessageId для заявки {TicketId}", ticketId);
                    throw;
                }

                return Task.CompletedTask;
            },
            context: new { ticketId, dbFileName })
            .GetAwaiter().GetResult();
    }

    private string GetConnectionString(string dbFileName)
    {
        var dbPath = Path.Combine(_dataFolder, dbFileName);
        return $"Filename={dbPath};Connection=shared";
    }
}