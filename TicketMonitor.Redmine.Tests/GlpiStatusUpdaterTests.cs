using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Models;
using TicketMonitor.Redmine.Services;
using TicketMonitor.Redmine.Tests.TestHelpers;
using Xunit;

namespace TicketMonitor.Redmine.Tests;

/// <summary>
/// Юнит-тесты <see cref="GlpiStatusUpdater"/>.
/// Используют фейковый <see cref="GlpiClient"/> — переопределяют
/// <see cref="GlpiClient.CreateRequest"/> и <see cref="GlpiClient.SendWithSessionRetryAsync"/>.
/// </summary>
public sealed class GlpiStatusUpdaterTests
{
    // -------------------------------------------------------------------------
    // AddFollowupAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AddFollowupAsync_EmptyTicketId_Throws()
    {
        var sut = CreateSut(FakeGlpiClient.OkJson("{}"));

        var act = async () => await sut.AddFollowupAsync("", "some comment");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AddFollowupAsync_EmptyComment_ReturnsFalse()
    {
        var sut = CreateSut(FakeGlpiClient.OkJson("{}"));

        var result = await sut.AddFollowupAsync("12345", "");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task AddFollowupAsync_Success_ReturnsTrueAndSendsPost()
    {
        var fake = FakeGlpiClient.OkJson("""{"id":42}""");
        var sut = CreateSut(fake);

        var result = await sut.AddFollowupAsync("12345", "Текст решения");

        result.Should().BeTrue();

        fake.Calls.Should().ContainSingle();
        var call = fake.Calls[0];
        call.Method.Should().Be(HttpMethod.Post);
        call.Url.Should().Contain("Ticket/12345/ITILFollowup");

        // JsonSerializer экранирует кириллицу как \uXXXX, поэтому проверяем структурно.
        using var doc = JsonDocument.Parse(call.Body);
        var input = doc.RootElement.GetProperty("input");
        input.GetProperty("itemtype").GetString().Should().Be("Ticket");
        input.GetProperty("items_id").GetInt32().Should().Be(12345);
        input.GetProperty("content").GetString().Should().Be("Текст решения");
    }

    [Fact]
    public async Task AddFollowupAsync_HttpError_ReturnsFalse()
    {
        var fake = FakeGlpiClient.Error(403, """{"error":"forbidden"}""");
        var sut = CreateSut(fake);

        var result = await sut.AddFollowupAsync("12345", "comment");

        result.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // SetGlpiStatusAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetGlpiStatusAsync_Success_ReturnsTrueAndSendsPut()
    {
        var fake = FakeGlpiClient.OkJson("""{"12345":true}""");
        var sut = CreateSut(fake);

        var result = await sut.SetGlpiStatusAsync("12345", 5);

        result.Should().BeTrue();

        fake.Calls.Should().ContainSingle();
        var call = fake.Calls[0];
        call.Method.Should().Be(HttpMethod.Put);
        call.Url.Should().EndWith("Ticket/12345");

        // JsonSerializer экранирует кириллицу как \uXXXX, поэтому проверяем структурно.
        using var doc = JsonDocument.Parse(call.Body);
        var input = doc.RootElement.GetProperty("input");
        input.GetProperty("id").GetInt32().Should().Be(12345);
        input.GetProperty("status").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task SetGlpiStatusAsync_HttpError_ReturnsFalse()
    {
        var fake = FakeGlpiClient.Error(403, """{"error":"forbidden"}""");
        var sut = CreateSut(fake);

        var result = await sut.SetGlpiStatusAsync("12345", 5);

        result.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // SetExternalIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetExternalIdAsync_EmptyExternalId_ReturnsFalse()
    {
        var sut = CreateSut(FakeGlpiClient.OkJson("{}"));

        var result = await sut.SetExternalIdAsync("12345", "");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetExternalIdAsync_Success_ReturnsTrueAndSendsPut()
    {
        var fake = FakeGlpiClient.OkJson("""{"12345":true}""");
        var sut = CreateSut(fake);

        var result = await sut.SetExternalIdAsync("12345", "DVITS-999");

        result.Should().BeTrue();

        fake.Calls.Should().ContainSingle();

        using var doc = JsonDocument.Parse(fake.Calls[0].Body);
        var input = doc.RootElement.GetProperty("input");
        input.GetProperty("externalid").GetString().Should().Be("DVITS-999");
    }

    // -------------------------------------------------------------------------
    // UpdateStatusAsync (маппинг Redmine → GLPI)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(3, 5)]
    [InlineData(11, 5)]
    public async Task UpdateStatusAsync_RedmineToGlpi_MapsTo5(int redmineStatus, int expectedGlpiStatus)
    {
        var fake = FakeGlpiClient.OkJson("""{"12345":true}""");
        var sut = CreateSut(fake);

        var result = await sut.UpdateStatusAsync("12345", redmineStatus);

        result.Should().BeTrue();

        using var doc = JsonDocument.Parse(fake.Calls[0].Body);
        doc.RootElement.GetProperty("input").GetProperty("status").GetInt32().Should().Be(expectedGlpiStatus);
    }

    [Theory]
    [InlineData(5, 6)]
    [InlineData(12, 6)]
    public async Task UpdateStatusAsync_RedmineToGlpi_MapsTo6(int redmineStatus, int expectedGlpiStatus)
    {
        var fake = FakeGlpiClient.OkJson("""{"12345":true}""");
        var sut = CreateSut(fake);

        var result = await sut.UpdateStatusAsync("12345", redmineStatus);

        result.Should().BeTrue();

        using var doc = JsonDocument.Parse(fake.Calls[0].Body);
        doc.RootElement.GetProperty("input").GetProperty("status").GetInt32().Should().Be(expectedGlpiStatus);
    }

    [Fact]
    public async Task UpdateStatusAsync_Redmine6_MapsToGlpi4()
    {
        var fake = FakeGlpiClient.OkJson("""{"12345":true}""");
        var sut = CreateSut(fake);

        var result = await sut.UpdateStatusAsync("12345", 6);

        result.Should().BeTrue();

        using var doc = JsonDocument.Parse(fake.Calls[0].Body);
        doc.RootElement.GetProperty("input").GetProperty("status").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task UpdateStatusAsync_UnknownRedmineStatus_ReturnsFalseAndSendsNothing()
    {
        var fake = FakeGlpiClient.OkJson("""{"12345":true}""");
        var sut = CreateSut(fake);

        var result = await sut.UpdateStatusAsync("12345", 999);

        result.Should().BeFalse();
        fake.Calls.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // MapRedmineToGlpi (статический)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(3, 5)]
    [InlineData(5, 6)]
    [InlineData(6, 4)]
    [InlineData(11, 5)]
    [InlineData(12, 6)]
    [InlineData(999, null)]
    public void MapRedmineToGlpi_ReturnsExpected(int redmineStatusId, int? expected)
    {
        var result = GlpiStatusUpdater.MapRedmineToGlpi(redmineStatusId);

        result.Should().Be(expected);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static GlpiStatusUpdater CreateSut(FakeGlpiClient client)
        => new(client, new NoOpOperationRegistry(), NullLogger<GlpiStatusUpdater>.Instance);

    /// <summary>
    /// Фейковый <see cref="GlpiClient"/>, который не ходит по HTTP,
    /// а записывает вызовы и возвращает заранее заданные ответы.
    /// </summary>
    private sealed class FakeGlpiClient : GlpiClient
    {
        public List<FakeCall> Calls { get; } = [];

        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        private FakeGlpiClient(HttpStatusCode statusCode, string responseBody)
            : base(
                new ThrowingHttpClientFactory(),
                Options.Create(new GlpiClientOptions { BaseUrl = "https://glpi.example.ru" }),
                new NoOpOperationRegistry(),
                NullLogger<GlpiClient>.Instance)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        public static FakeGlpiClient OkJson(string body) => new(HttpStatusCode.OK, body);
        public static FakeGlpiClient Error(int statusCode, string body) => new((HttpStatusCode)statusCode, body);

        public override HttpRequestMessage CreateRequest(HttpMethod method, string url)
        {
            // Не вызываем base — сессия не нужна, мы не пойдём по HTTP.
            return new HttpRequestMessage(method, url);
        }

        public override Task<HttpResponseMessage> SendWithSessionRetryAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken = default)
        {
            var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty;

            Calls.Add(new FakeCall(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                body));

            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }

        public sealed record FakeCall(HttpMethod Method, string Url, string Body);
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException("FakeGlpiClient не должен ходить по HTTP");
    }
}