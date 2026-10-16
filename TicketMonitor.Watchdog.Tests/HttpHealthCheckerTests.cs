using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TicketMonitor.Watchdog.Services;
using TicketMonitor.Watchdog.Tests.TestHelpers;
using Xunit;

namespace TicketMonitor.Watchdog.Tests;

/// <summary>Юнит-тесты <see cref="HttpHealthChecker"/>.</summary>
public sealed class HttpHealthCheckerTests
{
    [Fact]
    public async Task CheckAsync_EmptyUrl_Throws()
    {
        var sut = CreateSut(HttpStatusCode.OK, "");

        var act = async () => await sut.CheckAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CheckAsync_200_ReturnsHealthy()
    {
        var sut = CreateSut(HttpStatusCode.OK, "{}");

        var result = await sut.CheckAsync("http://test.local/health");

        result.Healthy.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.ResponseTimeMs.Should().NotBeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_500_ReturnsUnhealthy()
    {
        var sut = CreateSut(HttpStatusCode.InternalServerError, "{}");

        var result = await sut.CheckAsync("http://test.local/health");

        result.Healthy.Should().BeFalse();
        result.StatusCode.Should().Be(500);
        result.ErrorMessage.Should().Contain("500");
    }

    [Fact]
    public async Task CheckAsync_404_ReturnsUnhealthy()
    {
        var sut = CreateSut(HttpStatusCode.NotFound, "");

        var result = await sut.CheckAsync("http://test.local/health");

        result.Healthy.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task CheckAsync_NetworkError_ReturnsUnhealthyWithMessage()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var factory = new FakeHttpClientFactory(handler);
        var sut = new HttpHealthChecker(factory, NullLogger<HttpHealthChecker>.Instance);

        var result = await sut.CheckAsync("http://test.local/health");

        result.Healthy.Should().BeFalse();
        result.ErrorMessage.Should().Contain("connection refused");
    }

    [Fact]
    public async Task CheckAsync_SendsGetRequest()
    {
        var (sut, handler) = CreateSutWithHandler(HttpStatusCode.OK, "{}");

        await sut.CheckAsync("http://test.local/health");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.ToString().Should().Be("http://test.local/health");
    }

    private static HttpHealthChecker CreateSut(HttpStatusCode statusCode, string content)
    {
        var handler = new FakeHttpMessageHandler(statusCode, content);
        var factory = new FakeHttpClientFactory(handler);
        return new HttpHealthChecker(factory, NullLogger<HttpHealthChecker>.Instance);
    }

    private static (HttpHealthChecker Sut, FakeHttpMessageHandler Handler) CreateSutWithHandler(
        HttpStatusCode statusCode,
        string content)
    {
        var handler = new FakeHttpMessageHandler(statusCode, content);
        var factory = new FakeHttpClientFactory(handler);
        return (new HttpHealthChecker(factory, NullLogger<HttpHealthChecker>.Instance), handler);
    }
}