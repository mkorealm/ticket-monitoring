using FluentAssertions;
using LiteDB;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Services;
using Xunit;

namespace TicketMonitor.Core.Tests;

/// <summary>Юнит-тесты <see cref="LiteDbOperationRegistry"/>.</summary>
public sealed class LiteDbOperationRegistryTests : IDisposable
{
    private readonly string _dataFolder = Path.Combine(AppContext.BaseDirectory, "Data");
    private readonly string _dbFileName;
    private readonly LiteDbOperationRegistry _sut;

    public LiteDbOperationRegistryTests()
    {
        _dbFileName = $"test_ops_{Guid.NewGuid():N}.db";
        var options = Options.Create(new OperationOptions
        {
            ServiceName = "TestService",
            DatabaseFileName = _dbFileName,
            RetentionDays = 30
        });
        _sut = new LiteDbOperationRegistry(options, NullLogger<LiteDbOperationRegistry>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        var path = Path.Combine(_dataFolder, _dbFileName);
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    [Fact]
    public async Task RunAsync_Generic_Success_ReturnsValue()
    {
        var result = await _sut.RunAsync("Test:Action", _ => Task.FromResult(42));
        result.Should().Be(42);

        await _sut.FlushAsync();

        using var db = new LiteDatabase($"Filename={Path.Combine(_dataFolder, _dbFileName)};Connection=shared");
        var collection = db.GetCollection<OperationRecord>("operations");
        var records = collection.FindAll().ToList();

        records.Should().ContainSingle();
        records[0].Type.Should().Be("Test:Action");
        records[0].ServiceName.Should().Be("TestService");
        records[0].Status.Should().Be("Success");
        records[0].ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_NonGeneric_Success_WritesRecord()
    {
        await _sut.RunAsync("Test:Void", _ => Task.CompletedTask);
        await _sut.FlushAsync();

        using var db = new LiteDatabase($"Filename={Path.Combine(_dataFolder, _dbFileName)};Connection=shared");
        var collection = db.GetCollection<OperationRecord>("operations");
        var records = collection.FindAll().ToList();

        records.Should().ContainSingle();
        records[0].Status.Should().Be("Success");
    }

    [Fact]
    public async Task RunAsync_Failure_WritesFailedRecordAndRethrows()
    {
        var act = async () => await _sut.RunAsync<int>(
            "Test:Fail",
            _ => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>();

        await _sut.FlushAsync();

        using var db = new LiteDatabase($"Filename={Path.Combine(_dataFolder, _dbFileName)};Connection=shared");
        var collection = db.GetCollection<OperationRecord>("operations");
        var records = collection.FindAll().ToList();

        records.Should().ContainSingle();
        records[0].Status.Should().Be("Failed");
        records[0].ErrorMessage.Should().Be("boom");
        records[0].StackTrace.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public async Task RunAsync_Cancellation_WritesCancelledStatus()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _sut.RunAsync<int>(
            "Test:Cancel",
            _ => throw new OperationCanceledException(),
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        await _sut.FlushAsync();

        using var db = new LiteDatabase($"Filename={Path.Combine(_dataFolder, _dbFileName)};Connection=shared");
        var collection = db.GetCollection<OperationRecord>("operations");
        var records = collection.FindAll().ToList();

        records.Should().ContainSingle();
        records[0].Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task RunAsync_WithContext_WritesContextJson()
    {
        await _sut.RunAsync("Test:Context", _ => Task.CompletedTask, context: new { ticketId = "12345" });
        await _sut.FlushAsync();

        using var db = new LiteDatabase($"Filename={Path.Combine(_dataFolder, _dbFileName)};Connection=shared");
        var collection = db.GetCollection<OperationRecord>("operations");
        var record = collection.FindAll().First();

        record.ContextJson.Should().Contain("ticketId");
        record.ContextJson.Should().Contain("12345");
    }

    [Fact]
    public async Task RunAsync_LongError_TruncatedTo2000Chars()
    {
        var longMessage = new string('x', 5000);

        var act = async () => await _sut.RunAsync<int>(
            "Test:LongError",
            _ => throw new InvalidOperationException(longMessage));

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _sut.FlushAsync();

        using var db = new LiteDatabase($"Filename={Path.Combine(_dataFolder, _dbFileName)};Connection=shared");
        var collection = db.GetCollection<OperationRecord>("operations");
        var record = collection.FindAll().First();

        record.ErrorMessage!.Length.Should().Be(2000);
    }

    [Fact]
    public async Task RunAsync_MeasuresDuration()
    {
        await _sut.RunAsync("Test:Duration", async _ =>
        {
            await Task.Delay(50);
        });

        await _sut.FlushAsync();

        using var db = new LiteDatabase($"Filename={Path.Combine(_dataFolder, _dbFileName)};Connection=shared");
        var collection = db.GetCollection<OperationRecord>("operations");
        var record = collection.FindAll().First();

        record.DurationMs.Should().BeGreaterOrEqualTo(50);
    }
}