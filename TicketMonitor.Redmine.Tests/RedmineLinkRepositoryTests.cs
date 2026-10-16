using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TicketMonitor.Redmine.Models;
using TicketMonitor.Redmine.Services;
using TicketMonitor.Redmine.Tests.TestHelpers;
using Xunit;

namespace TicketMonitor.Redmine.Tests;

/// <summary>
/// Интеграционные тесты <see cref="RedmineLinkRepository"/>.
/// Используют реальную LiteDB в изолированной временной папке.
/// </summary>
public sealed class RedmineLinkRepositoryTests : IDisposable
{
    private readonly string _dataFolder;
    private readonly RedmineLinkRepository _sut;

    public RedmineLinkRepositoryTests()
    {
        // Переопределяем AppContext.BaseDirectory не получится,
        // поэтому используем реальную папку bin/.../Data, но чистим за собой.
        _dataFolder = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(_dataFolder);

        // Удаляем возможные остатки от предыдущих тестов.
        var path = Path.Combine(_dataFolder, "redmine_links.db");
        if (File.Exists(path)) File.Delete(path);

        _sut = new RedmineLinkRepository(new NoOpOperationRegistry(), NullLogger<RedmineLinkRepository>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        var path = Path.Combine(_dataFolder, "redmine_links.db");
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    [Fact]
    public void Save_NewLink_CanBeReadBack()
    {
        var link = new RedmineLink
        {
            TicketId = "12345",
            RedmineIssueId = 999,
            RedmineIssueKey = "DVITS-999",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        _sut.Save(link);
        var result = _sut.GetByTicketId("12345");

        result.Should().NotBeNull();
        result!.TicketId.Should().Be("12345");
        result.RedmineIssueId.Should().Be(999);
        result.RedmineIssueKey.Should().Be("DVITS-999");
    }

    [Fact]
    public void Save_ExistingLink_UpdatesInsteadOfInsert()
    {
        _sut.Save(new RedmineLink
        {
            TicketId = "12345",
            RedmineIssueId = 999,
            RedmineIssueKey = "DVITS-999",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        _sut.Save(new RedmineLink
        {
            TicketId = "12345",
            RedmineIssueId = 1000,
            RedmineIssueKey = "DVITS-1000",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        var result = _sut.GetByTicketId("12345");
        result!.RedmineIssueId.Should().Be(1000);
        result.RedmineIssueKey.Should().Be("DVITS-1000");
    }

    [Fact]
    public void GetByTicketId_NotFound_ReturnsNull()
    {
        var result = _sut.GetByTicketId("99999");

        result.Should().BeNull();
    }

    [Fact]
    public void GetByTicketId_EmptyId_Throws()
    {
        var act = () => _sut.GetByTicketId("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetByRedmineIssueId_ReturnsLink()
    {
        _sut.Save(new RedmineLink
        {
            TicketId = "12345",
            RedmineIssueId = 999,
            RedmineIssueKey = "DVITS-999",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        var result = _sut.GetByRedmineIssueId(999);

        result.Should().NotBeNull();
        result!.TicketId.Should().Be("12345");
    }

    [Fact]
    public void GetByRedmineIssueId_NotFound_ReturnsNull()
    {
        var result = _sut.GetByRedmineIssueId(99999);

        result.Should().BeNull();
    }

    [Fact]
    public void Remove_Existing_ReturnsTrue()
    {
        _sut.Save(new RedmineLink
        {
            TicketId = "12345",
            RedmineIssueId = 999,
            RedmineIssueKey = "DVITS-999",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        var removed = _sut.Remove("12345");

        removed.Should().BeTrue();
        _sut.GetByTicketId("12345").Should().BeNull();
    }

    [Fact]
    public void Remove_NonExistent_ReturnsFalse()
    {
        var result = _sut.Remove("99999");

        result.Should().BeFalse();
    }

    [Fact]
    public void GetAll_ReturnsAllLinks()
    {
        _sut.Save(new RedmineLink { TicketId = "1", RedmineIssueId = 10, RedmineIssueKey = "T-1", CreatedAt = DateTime.UtcNow });
        _sut.Save(new RedmineLink { TicketId = "2", RedmineIssueId = 20, RedmineIssueKey = "T-2", CreatedAt = DateTime.UtcNow });

        var all = _sut.GetAll();

        all.Should().HaveCount(2);
        all.Select(x => x.TicketId).Should().BeEquivalentTo(["1", "2"]);
    }

    [Fact]
    public void Save_NullLink_Throws()
    {
        var act = () => _sut.Save(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}