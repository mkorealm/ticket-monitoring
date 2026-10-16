using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Services;
using TicketMonitor.Core.Tests.TestHelpers;
using Xunit;

namespace TicketMonitor.Core.Tests;

/// <summary>
/// Юнит-тесты <see cref="NotificationChannelResolver"/>.
/// </summary>
public sealed class NotificationChannelResolverTests
{
    private readonly RocketChatNotificationService _rocketChat;
    private readonly ExchangeEmailService _exchange;
    private readonly NotificationChannelResolver _sut;

    public NotificationChannelResolverTests()
    {
        var rocketOptions = Options.Create(new RocketChatOptions
        {
            ServerUrl = "https://chat.example.ru",
            TimeoutSeconds = 30
        });

        var emailOptions = Options.Create(new EmailOptions
        {
            SmtpServer = "mail.example.ru",
            Port = 25,
            From = "monitor@example.ru",
            To = "admin@example.ru"
        });

        var rocketClient = new NoOpRocketChatClient();
        _rocketChat = new RocketChatNotificationService(
            rocketClient, rocketOptions, NullLogger<RocketChatNotificationService>.Instance);

        var parser = new EmailMessageParser();
        var renderer = new EmailTemplateRenderer(NullLogger<EmailTemplateRenderer>.Instance);
        _exchange = new ExchangeEmailService(
            emailOptions, parser, renderer, new NoOpOperationRegistry(),
            NullLogger<ExchangeEmailService>.Instance);

        _sut = new NotificationChannelResolver(
            _rocketChat, _exchange, NullLogger<NotificationChannelResolver>.Instance);
    }

    // -------------------------------------------------------------------------
    // ResolveForMonitor
    // -------------------------------------------------------------------------

    [Fact]
    public void ResolveForMonitor_NullConfig_Throws()
    {
        var act = () => _sut.ResolveForMonitor(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ResolveForMonitor_NoChannels_ReturnsEmpty()
    {
        var config = CreateMonitorConfig(channels: []);

        var result = _sut.ResolveForMonitor(config);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ResolveForMonitor_RocketChatEnabled_ReturnsRocketChat()
    {
        var config = CreateMonitorConfig(new NotificationChannelConfig
        {
            Type = "RocketChat",
            Enabled = true
        });

        var result = _sut.ResolveForMonitor(config);

        result.Should().ContainSingle().Which.Should().BeSameAs(_rocketChat);
    }

    [Fact]
    public void ResolveForMonitor_ExchangeEnabled_ReturnsExchange()
    {
        var config = CreateMonitorConfig(new NotificationChannelConfig
        {
            Type = "Exchange",
            Enabled = true
        });

        var result = _sut.ResolveForMonitor(config);

        result.Should().ContainSingle().Which.Should().BeSameAs(_exchange);
    }

    [Fact]
    public void ResolveForMonitor_BothEnabled_ReturnsBoth()
    {
        var config = CreateMonitorConfig(
            new NotificationChannelConfig { Type = "RocketChat", Enabled = true },
            new NotificationChannelConfig { Type = "Exchange", Enabled = true });

        var result = _sut.ResolveForMonitor(config);

        result.Should().HaveCount(2);
        result.Should().Contain(_rocketChat);
        result.Should().Contain(_exchange);
    }

    [Fact]
    public void ResolveForMonitor_DisabledChannel_Skipped()
    {
        var config = CreateMonitorConfig(
            new NotificationChannelConfig { Type = "RocketChat", Enabled = false },
            new NotificationChannelConfig { Type = "Exchange", Enabled = true });

        var result = _sut.ResolveForMonitor(config);

        result.Should().ContainSingle().Which.Should().BeSameAs(_exchange);
    }

    [Fact]
    public void ResolveForMonitor_UnknownChannelType_Skipped()
    {
        var config = CreateMonitorConfig(new NotificationChannelConfig
        {
            Type = "Slack",
            Enabled = true
        });

        var result = _sut.ResolveForMonitor(config);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ResolveForMonitor_DuplicateChannelTypes_Deduplicated()
    {
        var config = CreateMonitorConfig(
            new NotificationChannelConfig { Type = "RocketChat", Enabled = true },
            new NotificationChannelConfig { Type = "RocketChat", Enabled = true },
            new NotificationChannelConfig { Type = "rocketchat", Enabled = true });

        var result = _sut.ResolveForMonitor(config);

        result.Should().ContainSingle("дубликаты не должны попадать в результат");
    }

    [Fact]
    public void ResolveForMonitor_CaseInsensitiveChannelType()
    {
        var config = CreateMonitorConfig(new NotificationChannelConfig
        {
            Type = "rocketchat",
            Enabled = true
        });

        var result = _sut.ResolveForMonitor(config);

        result.Should().ContainSingle().Which.Should().BeSameAs(_rocketChat);
    }

    // -------------------------------------------------------------------------
    // ResolveByTargets
    // -------------------------------------------------------------------------

    [Fact]
    public void ResolveByTargets_NullTargets_ReturnsAll()
    {
        var result = _sut.ResolveByTargets(null);

        result.Should().HaveCount(2);
        result.Should().Contain(_rocketChat);
        result.Should().Contain(_exchange);
    }

    [Fact]
    public void ResolveByTargets_EmptyTargets_ReturnsAll()
    {
        var result = _sut.ResolveByTargets([]);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void ResolveByTargets_RocketChatOnly_ReturnsRocketChat()
    {
        var result = _sut.ResolveByTargets(["rocketchat"]);

        result.Should().ContainSingle().Which.Should().BeSameAs(_rocketChat);
    }

    [Fact]
    public void ResolveByTargets_ExchangeOnly_ReturnsExchange()
    {
        var result = _sut.ResolveByTargets(["exchange"]);

        result.Should().ContainSingle().Which.Should().BeSameAs(_exchange);
    }

    [Fact]
    public void ResolveByTargets_BothTargets_ReturnsBoth()
    {
        var result = _sut.ResolveByTargets(["rocketchat", "exchange"]);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void ResolveByTargets_UnknownTarget_Ignored()
    {
        var result = _sut.ResolveByTargets(["unknown", "rocketchat"]);

        result.Should().ContainSingle().Which.Should().BeSameAs(_rocketChat);
    }

    [Fact]
    public void ResolveByTargets_CaseInsensitive()
    {
        var result = _sut.ResolveByTargets(["RocketChat", "EXCHANGE"]);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void ResolveByTargets_WhitespaceTarget_Ignored()
    {
        var result = _sut.ResolveByTargets(["", "   ", "rocketchat"]);

        result.Should().ContainSingle().Which.Should().BeSameAs(_rocketChat);
    }

    // -------------------------------------------------------------------------
    // ContainsRocketChat
    // -------------------------------------------------------------------------

    [Fact]
    public void ContainsRocketChat_TrueIfPresent()
    {
        var result = _sut.ContainsRocketChat([_rocketChat, _exchange]);

        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsRocketChat_FalseIfAbsent()
    {
        var result = _sut.ContainsRocketChat([_exchange]);

        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsRocketChat_NullThrows()
    {
        var act = () => _sut.ContainsRocketChat(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // -------------------------------------------------------------------------
    // GetRocketChatService
    // -------------------------------------------------------------------------

    [Fact]
    public void GetRocketChatService_ReturnsInstance()
    {
        var result = _sut.GetRocketChatService();

        result.Should().BeSameAs(_rocketChat);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static MonitorConfig CreateMonitorConfig(
        params NotificationChannelConfig[] channels)
    {
        return new MonitorConfig
        {
            Name = "Test",
            CategoryFilter = "Test",
            DatabaseFileName = "test.db",
            Notification = new NotificationChannelsConfig
            {
                Channels = [.. channels]
            }
        };
    }

    /// <summary>
    /// Заглушка клиента Rocket.Chat — ничего не делает.
    /// </summary>
    private sealed class NoOpRocketChatClient : Contracts.IRocketChatClient
    {
        public Task<string?> SendMessageAsync(
            string roomId,
            string text,
            string? threadMessageId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<bool> UpdateMessageAsync(
            string roomId,
            string messageId,
            string newText,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}