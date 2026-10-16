using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Redmine.Clients;
using TicketMonitor.Redmine.Models;
using TicketMonitor.Redmine.Tests.TestHelpers;
using Xunit;

namespace TicketMonitor.Redmine.Tests;

/// <summary>Юнит-тесты <see cref="RedmineClient"/>.</summary>
public sealed class RedmineClientTests
{
    // -------------------------------------------------------------------------
    // CreateIssueAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateIssueAsync_NullRequest_Throws()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, "{}");

        var act = async () => await sut.CreateIssueAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CreateIssueAsync_EmptyGlpiTicketId_Throws()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, "{}");

        var request = new RedmineCreateRequest
        {
            GlpiTicketId = "",
            ProjectId = 18,
            TrackerId = 3,
            Subject = "Test"
        };

        var act = async () => await sut.CreateIssueAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateIssueAsync_EmptySubject_Throws()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, "{}");

        var request = new RedmineCreateRequest
        {
            GlpiTicketId = "12345",
            ProjectId = 18,
            TrackerId = 3,
            Subject = ""
        };

        var act = async () => await sut.CreateIssueAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateIssueAsync_Success_ReturnsIssueWithId()
    {
        const string json = """{"issue":{"id":999,"subject":"Test","project":{"id":18},"status":{"id":1,"name":"New"}}}""";
        var (sut, handler) = CreateSut(HttpStatusCode.OK, json);

        var request = new RedmineCreateRequest
        {
            GlpiTicketId = "12345",
            ProjectId = 18,
            TrackerId = 3,
            Subject = "Test"
        };

        var result = await sut.CreateIssueAsync(request);

        result.Id.Should().Be(999);
        result.Subject.Should().Be("Test");
        result.ProjectId.Should().Be(18);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].Body.Should().Contain("\"project_id\":18");
        handler.Requests[0].Body.Should().Contain("\"subject\":\"Test\"");
    }

    [Fact]
    public async Task CreateIssueAsync_HttpError_Throws()
    {
        var (sut, _) = CreateSut(HttpStatusCode.BadRequest, """{"errors":["Subject can't be blank"]}""");

        var request = new RedmineCreateRequest
        {
            GlpiTicketId = "12345",
            ProjectId = 18,
            TrackerId = 3,
            Subject = "Test"
        };

        var act = async () => await sut.CreateIssueAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Redmine*");
    }

    [Fact]
    public async Task CreateIssueAsync_SuccessButNoIdInResponse_Throws()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, """{"issue":{}}""");

        var request = new RedmineCreateRequest
        {
            GlpiTicketId = "12345",
            ProjectId = 18,
            TrackerId = 3,
            Subject = "Test"
        };

        var act = async () => await sut.CreateIssueAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // FindIssueByGlpiTicketIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FindIssueByGlpiTicketIdAsync_EmptyId_Throws()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, "{}");

        var act = async () => await sut.FindIssueByGlpiTicketIdAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task FindIssueByGlpiTicketIdAsync_IssueFound_ReturnsFirst()
    {
        const string json = """{"issues":[{"id":999,"subject":"Test","project":{"id":18},"status":{"id":3,"name":"Closed"},"custom_fields":[{"id":5,"value":"12345"}]}]}""";
        var (sut, handler) = CreateSut(HttpStatusCode.OK, json);

        var result = await sut.FindIssueByGlpiTicketIdAsync("12345");

        result.Should().NotBeNull();
        result!.Id.Should().Be(999);
        result.GlpiTicketId.Should().Be("12345");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.ToString().Should().Contain("cf_5=12345");
    }

    [Fact]
    public async Task FindIssueByGlpiTicketIdAsync_NoIssues_ReturnsNull()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, """{"issues":[]}""");

        var result = await sut.FindIssueByGlpiTicketIdAsync("12345");

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindIssueByGlpiTicketIdAsync_HttpError_ReturnsNull()
    {
        var (sut, _) = CreateSut(HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        var result = await sut.FindIssueByGlpiTicketIdAsync("12345");

        result.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // GetIssueAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetIssueAsync_Success_ReturnsIssue()
    {
        const string json = """{"issue":{"id":999,"subject":"Test","project":{"id":18},"status":{"id":5,"name":"Closed","is_closed":true}}}""";
        var (sut, _) = CreateSut(HttpStatusCode.OK, json);

        var result = await sut.GetIssueAsync(999);

        result.Should().NotBeNull();
        result!.Id.Should().Be(999);
        result.StatusId.Should().Be(5);
        result.IsClosed.Should().BeTrue();
    }

    [Fact]
    public async Task GetIssueAsync_NotFound_ReturnsNull()
    {
        var (sut, _) = CreateSut(HttpStatusCode.NotFound, "{}");

        var result = await sut.GetIssueAsync(999);

        result.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // GetClosedIssuesAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetClosedIssuesAsync_EmptyResponse_ReturnsEmpty()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, """{"issues":[],"total_count":0}""");

        var result = await sut.GetClosedIssuesAsync(DateTime.UtcNow.AddHours(-1));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetClosedIssuesAsync_ReturnsIssues()
    {
        var updated = DateTime.UtcNow.AddMinutes(-30).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var json = $$"""{"issues":[{"id":999,"subject":"Test","project":{"id":18},"status":{"id":5,"name":"Closed","is_closed":true},"updated_on":"{{updated}}","custom_fields":[{"id":5,"value":"12345"}]}]}""";
        var (sut, _) = CreateSut(HttpStatusCode.OK, json);

        var result = await sut.GetClosedIssuesAsync(DateTime.UtcNow.AddHours(-1));

        result.Should().ContainSingle();
        result[0].Id.Should().Be(999);
        result[0].GlpiTicketId.Should().Be("12345");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static (RedmineClient Sut, FakeHttpMessageHandler Handler) CreateSut(
        HttpStatusCode statusCode,
        string content)
    {
        var handler = new FakeHttpMessageHandler(statusCode, content);
        var factory = new FakeHttpClientFactory(handler, "https://redmine.example.ru/");
        var options = Options.Create(new RedmineOptions
        {
            BaseUrl = "https://redmine.example.ru",
            ApiToken = "test",
            CustomFieldHdNumber = 5
        });

        var sut = new RedmineClient(factory, options, new NoOpOperationRegistry(), NullLogger<RedmineClient>.Instance);
        return (sut, handler);
    }
}