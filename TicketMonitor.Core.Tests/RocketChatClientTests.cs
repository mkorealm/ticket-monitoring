using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Tests.TestHelpers;
using Xunit;

namespace TicketMonitor.Core.Tests;

/// <summary>Юнит-тесты <see cref="RocketChatClient"/>.</summary>
public sealed class RocketChatClientTests
{
    [Fact]
    public async Task SendMessageAsync_NullRoomId_Throws()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, "{}");

        var act = async () => await sut.SendMessageAsync(null!, "text", null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendMessageAsync_NullText_Throws()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, "{}");

        var act = async () => await sut.SendMessageAsync("room", null!, null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendMessageAsync_ServerUrlNotConfigured_ReturnsNull()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, "{}", serverUrl: null);

        var result = await sut.SendMessageAsync("room", "text", null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SendMessageAsync_Success_ReturnsMessageId()
    {
        const string json = """{"success":true,"message":{"_id":"abc123"}}""";
        var (sut, handler) = CreateSut(HttpStatusCode.OK, json);

        var result = await sut.SendMessageAsync("room", "text", null);

        result.Should().Be("abc123");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task SendMessageAsync_WithThread_IncludesTmid()
    {
        const string json = """{"success":true,"message":{"_id":"msg2"}}""";
        var (sut, handler) = CreateSut(HttpStatusCode.OK, json);

        await sut.SendMessageAsync("room", "text", "parent-msg-id");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Body.Should().Contain("\"tmid\":\"parent-msg-id\"");
    }

    [Fact]
    public async Task SendMessageAsync_HttpError_ReturnsNull()
    {
        var (sut, _) = CreateSut(HttpStatusCode.BadRequest, "{\"error\":\"bad\"}");

        var result = await sut.SendMessageAsync("room", "text", null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SendMessageAsync_EmptyBody_ReturnsNull()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, "");

        var result = await sut.SendMessageAsync("room", "text", null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SendMessageAsync_NoMessageIdInResponse_ReturnsNull()
    {
        const string json = """{"success":true}""";
        var (sut, _) = CreateSut(HttpStatusCode.OK, json);

        var result = await sut.SendMessageAsync("room", "text", null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SendMessageAsync_InvalidJson_ReturnsNull()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, "not-a-json");

        var result = await sut.SendMessageAsync("room", "text", null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateMessageAsync_Success_ReturnsTrue()
    {
        const string json = """{"success":true,"message":{"_id":"msg-1","msg":"Завершённая заявка #12345"}}""";
        var (sut, handler) = CreateSut(HttpStatusCode.OK, json);

        var result = await sut.UpdateMessageAsync("room", "msg-1", "Завершённая заявка #12345");

        result.Should().BeTrue();
        handler.Requests.Should().ContainSingle();

        using var doc = System.Text.Json.JsonDocument.Parse(handler.Requests[0].Body);
        doc.RootElement.GetProperty("roomId").GetString().Should().Be("room");
        doc.RootElement.GetProperty("msgId").GetString().Should().Be("msg-1");
        doc.RootElement.GetProperty("text").GetString().Should().Be("Завершённая заявка #12345");
    }

    [Fact]
    public async Task UpdateMessageAsync_HttpError_ReturnsFalse()
    {
        var (sut, _) = CreateSut(HttpStatusCode.BadRequest, "{\"success\":false,\"error\":\"bad\"}");

        var result = await sut.UpdateMessageAsync("room", "msg-1", "text");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateMessageAsync_SuccessFalse_ReturnsFalse()
    {
        const string json = """{"success":false,"error":"unauthorized"}""";
        var (sut, _) = CreateSut(HttpStatusCode.OK, json);

        var result = await sut.UpdateMessageAsync("room", "msg-1", "text");

        result.Should().BeFalse();
    }

    private static (RocketChatClient Sut, FakeHttpMessageHandler Handler) CreateSut(
        HttpStatusCode statusCode,
        string content,
        string? serverUrl = "https://chat.example.ru")
    {
        var handler = new FakeHttpMessageHandler(statusCode, content);
        var factory = new FakeHttpClientFactory(handler);
        var options = Options.Create(new RocketChatOptions
        {
            ServerUrl = serverUrl ?? string.Empty,
            TimeoutSeconds = 30
        });

        var sut = new RocketChatClient(factory, options, new NoOpOperationRegistry(), NullLogger<RocketChatClient>.Instance);
        return (sut, handler);
    }
}