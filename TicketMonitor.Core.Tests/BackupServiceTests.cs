using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Services;
using TicketMonitor.Core.Tests.TestHelpers;
using Xunit;

namespace TicketMonitor.Core.Tests;

/// <summary>
/// Интеграционные тесты <see cref="BackupService"/>.
/// Используют изолированную временную папку — не трогают продовые данные.
/// </summary>
public sealed class BackupServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _dataFolder;
    private readonly string _backupFolder;

    public BackupServiceTests()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            $"TicketMonitorTests_{Guid.NewGuid():N}");

        _dataFolder = Path.Combine(_testRoot, "Data");
        _backupFolder = Path.Combine(_testRoot, "Backups");

        Directory.CreateDirectory(_dataFolder);
        Directory.CreateDirectory(_backupFolder);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
                Directory.Delete(_testRoot, recursive: true);
        }
        catch
        {
            // Ничего страшного, если temp-папка не удалилась.
        }
    }

    // -------------------------------------------------------------------------
    // CreateBackupAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateBackupAsync_EmptyDataFolder_CreatesEmptyZip()
    {
        var sut = CreateService();

        var fileName = await sut.CreateBackupAsync();

        fileName.Should().StartWith("backup_");
        fileName.Should().EndWith(".zip");

        var filePath = Path.Combine(_backupFolder, fileName);
        File.Exists(filePath).Should().BeTrue("ZIP-файл должен быть создан");
    }

    [Fact]
    public async Task CreateBackupAsync_WithDbFiles_PacksAllOfThem()
    {
        File.WriteAllText(Path.Combine(_dataFolder, "tickets_a.db"), "data-a");
        File.WriteAllText(Path.Combine(_dataFolder, "tickets_b.db"), "data-b");
        File.WriteAllText(Path.Combine(_dataFolder, "tickets_c.db"), "data-c");
        File.WriteAllText(Path.Combine(_dataFolder, "readme.txt"), "not a db");

        var sut = CreateService();

        var fileName = await sut.CreateBackupAsync();

        var zipPath = Path.Combine(_backupFolder, fileName);
        var extractPath = Path.Combine(_testRoot, "extracted");
        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);

        Directory.GetFiles(extractPath).Should().HaveCount(3);
        File.Exists(Path.Combine(extractPath, "tickets_a.db")).Should().BeTrue();
        File.Exists(Path.Combine(extractPath, "tickets_b.db")).Should().BeTrue();
        File.Exists(Path.Combine(extractPath, "tickets_c.db")).Should().BeTrue();
        File.Exists(Path.Combine(extractPath, "readme.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task CreateBackupAsync_Twice_ProducesTwoDistinctFiles()
    {
        File.WriteAllText(Path.Combine(_dataFolder, "tickets.db"), "data");

        var sut = CreateService();

        var first = await sut.CreateBackupAsync();
        await Task.Delay(1100);
        var second = await sut.CreateBackupAsync();

        first.Should().NotBe(second);
        File.Exists(Path.Combine(_backupFolder, first)).Should().BeTrue();
        File.Exists(Path.Combine(_backupFolder, second)).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Retention policy
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateBackupAsync_RetentionCount_RemovesOldFiles()
    {
        File.WriteAllText(Path.Combine(_dataFolder, "tickets.db"), "data");

        var sut = CreateService(retentionCount: 2);

        for (var i = 0; i < 4; i++)
        {
            await sut.CreateBackupAsync();
            await Task.Delay(1100);
        }

        var available = sut.GetAvailableBackups();

        available.Should().HaveCount(2, "retention policy должна оставить только 2 последних файла");
    }

    [Fact]
    public async Task CreateBackupAsync_RetentionCountZero_RemovesAllPreviousFiles()
    {
        File.WriteAllText(Path.Combine(_dataFolder, "tickets.db"), "data");

        var sut = CreateService(retentionCount: 0);

        await sut.CreateBackupAsync();
        await Task.Delay(1100);
        await sut.CreateBackupAsync();

        sut.GetAvailableBackups().Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // GetAvailableBackups
    // -------------------------------------------------------------------------

    [Fact]
    public void GetAvailableBackups_EmptyFolder_ReturnsEmpty()
    {
        var sut = CreateService();

        var result = sut.GetAvailableBackups();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableBackups_ReturnsSortedDescending()
    {
        File.WriteAllText(Path.Combine(_dataFolder, "tickets.db"), "data");

        var sut = CreateService(retentionCount: 10);

        var first = await sut.CreateBackupAsync();
        await Task.Delay(1100);
        var second = await sut.CreateBackupAsync();

        var result = sut.GetAvailableBackups();

        result.Should().HaveCount(2);
        result[0].Should().Be(second);
        result[1].Should().Be(first);
    }

    // -------------------------------------------------------------------------
    // RestoreFromBackupAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RestoreFromBackupAsync_MissingFile_ReturnsFalse()
    {
        var sut = CreateService();

        var result = await sut.RestoreFromBackupAsync("nonexistent.zip");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreFromBackupAsync_EmptyFileName_Throws()
    {
        var sut = CreateService();

        var act = async () => await sut.RestoreFromBackupAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RestoreFromBackupAsync_RestoresFilesIntoDataFolder()
    {
        File.WriteAllText(Path.Combine(_dataFolder, "tickets.db"), "original");

        var sut = CreateService();
        var backupFileName = await sut.CreateBackupAsync();

        File.WriteAllText(Path.Combine(_dataFolder, "tickets.db"), "modified");

        var result = await sut.RestoreFromBackupAsync(backupFileName);

        result.Should().BeTrue();

        var content = File.ReadAllText(Path.Combine(_dataFolder, "tickets.db"));
        content.Should().Be("original", "восстановление должно вернуть исходное содержимое");
    }

    [Fact]
    public async Task RestoreFromBackupAsync_DeletesExtraFilesNotInBackup()
    {
        File.WriteAllText(Path.Combine(_dataFolder, "tickets.db"), "in backup");
        var sut = CreateService();
        var backupFileName = await sut.CreateBackupAsync();

        File.WriteAllText(Path.Combine(_dataFolder, "extra.db"), "not in backup");

        var result = await sut.RestoreFromBackupAsync(backupFileName);

        result.Should().BeTrue();
        File.Exists(Path.Combine(_dataFolder, "extra.db"))
            .Should().BeFalse("лишние файлы должны быть удалены перед восстановлением");
        File.Exists(Path.Combine(_dataFolder, "tickets.db")).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Concurrency (SemaphoreSlim)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateBackupAsync_ParallelCalls_AreSerialized()
    {
        File.WriteAllText(Path.Combine(_dataFolder, "tickets.db"), "data");

        var sut = CreateService(retentionCount: 100);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => sut.CreateBackupAsync())
            .ToList();

        var fileNames = await Task.WhenAll(tasks);

        fileNames.Should().OnlyHaveUniqueItems();
        foreach (var name in fileNames)
        {
            File.Exists(Path.Combine(_backupFolder, name))
                .Should().BeTrue($"файл {name} должен существовать");
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private BackupService CreateService(int retentionCount = 10)
    {
        var options = Options.Create(new BackupOptions
        {
            Enabled = true,
            IntervalHours = 24,
            RetentionCount = retentionCount,
            DataFolder = _dataFolder,
            BackupFolder = _backupFolder
        });

        var monitorManagementMock = new NoOpMonitorManagementService();

        return new BackupService(
            monitorManagementMock,
            options,
            new NoOpOperationRegistry(),
            NullLogger<BackupService>.Instance);
    }

    /// <summary>
    /// Заглушка <see cref="IMonitorManagementService"/> для тестов:
    /// все методы — no-op, возвращают пустые результаты.
    /// </summary>
    private sealed class NoOpMonitorManagementService : IMonitorManagementService
    {
        public void LoadMonitorsFromDatabase() { }
        public bool AddMonitor(MonitorConfig config) => false;
        public bool AddOrUpdateSystemMonitor(MonitorConfig config) => false;
        public bool UpdateMonitor(string monitorName, MonitorConfig newConfig) => false;
        public bool RemoveMonitor(string monitorName) => false;
        public IReadOnlyList<MonitorConfig> GetAllMonitors() => [];
        public MonitorConfig? GetMonitor(string monitorName) => null;
        public Task<bool> RunCheckAsync(string monitorName) => Task.FromResult(false);
        public Models.Dto.MonitorStatusDto? GetStatus(string monitorName) => null;
    }
}