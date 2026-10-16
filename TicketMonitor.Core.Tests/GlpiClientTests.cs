using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Tests.TestHelpers;
using Xunit;

namespace TicketMonitor.Core.Tests;

/// <summary>Юнит-тесты <see cref="GlpiClient"/>.</summary>
public sealed class GlpiClientTests
{
    [Fact]
    public async Task InitSession_NullToken_Throws()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, "{}");

        var act = async () => await sut.InitSession(null!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task InitSession_Success_SetsSessionToken()
    {
        const string json = """{"session_token":"abc-123"}""";
        var (sut, _) = CreateSut(HttpStatusCode.OK, json);

        var result = await sut.InitSession("user-token");

        result.Should().BeTrue();
        sut.GetSessionToken().Should().Be("abc-123");
    }

    [Fact]
    public async Task InitSession_NoTokenInResponse_ReturnsFalse()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, "{}");

        var result = await sut.InitSession("user-token");

        result.Should().BeFalse();
        sut.GetSessionToken().Should().BeEmpty();
    }

    [Fact]
    public async Task InitSession_EmptyToken_ReturnsFalse()
    {
        const string json = """{"session_token":""}""";
        var (sut, _) = CreateSut(HttpStatusCode.OK, json);

        var result = await sut.InitSession("user-token");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task InitSession_HttpError_ReturnsFalse()
    {
        var (sut, _) = CreateSut(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\"}");

        var result = await sut.InitSession("user-token");

        result.Should().BeFalse();
    }

    [Fact]
    public void GetSessionToken_NotInitialized_ReturnsEmpty()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, "{}");

        sut.GetSessionToken().Should().BeEmpty();
    }

    [Fact]
    public void CreateRequest_NoSession_Throws()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, "{}");

        var act = () => sut.CreateRequest(HttpMethod.Get, "apirest.php/Ticket/1");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Сессия не инициализирована*");
    }

    [Fact]
    public async Task ParseResponse_DataArray_ReturnsList()
    {
        const string json = """{"data":[{"id":1,"name":"Ticket 1"},{"id":2,"name":"Ticket 2"}]}""";
        var (sut, _) = CreateSut(HttpStatusCode.OK, json);

        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };

        var result = await sut.ParseResponse(response);

        result.Should().HaveCount(2);
        result[0]["id"].Should().Be(1);
        result[0]["name"].Should().Be("Ticket 1");
    }

    [Fact]
    public async Task ParseResponse_HttpError_Throws()
    {
        var (sut, _) = CreateSut(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}");

        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"error\":\"boom\"}")
        };

        var act = async () => await sut.ParseResponse(response);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SendWithSessionRetryAsync_Unauthorized_Reinitializes()
    {
        var callCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            callCount++;
            return callCount switch
            {
                1 => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"session_token":"old-token"}"""),
                2 => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}"),
                3 => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"session_token":"new-token"}"""),
                _ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"data":[]}""")
            };
        });

        var factory = new FakeHttpClientFactory(handler);
        var options = Options.Create(new GlpiClientOptions { BaseUrl = "https://glpi.example.ru" });
        var sut = new GlpiClient(factory, options, new NoOpOperationRegistry(), NullLogger<GlpiClient>.Instance);

        var initOk = await sut.InitSession("user-token");
        initOk.Should().BeTrue();
        sut.GetSessionToken().Should().Be("old-token");

        using var request = sut.CreateRequest(HttpMethod.Get, "apirest.php/Ticket/1");
        using var response = await sut.SendWithSessionRetryAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        callCount.Should().Be(4, "initSession + ticket(401) + initSession(retry) + ticket(retry)");
    }

    private static (GlpiClient Sut, FakeHttpMessageHandler Handler) CreateSut(
        HttpStatusCode statusCode,
        string content)
    {
        var handler = new FakeHttpMessageHandler(statusCode, content);
        var factory = new FakeHttpClientFactory(handler);
        var options = Options.Create(new GlpiClientOptions
        {
            BaseUrl = "https://glpi.example.ru",
            AppToken = "test"
        });

        var sut = new GlpiClient(factory, options, new NoOpOperationRegistry(), NullLogger<GlpiClient>.Instance);
        return (sut, handler);
    }
}