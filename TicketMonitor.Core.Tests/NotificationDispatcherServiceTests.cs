using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Services;
using TicketMonitor.Core.Services.Formatters;
using TicketMonitor.Core.Tests.TestHelpers;
using Xunit;

namespace TicketMonitor.Core.Tests;

/// <summary>Юнит-тесты <see cref="NotificationDispatcherService"/>.</summary>
public sealed class NotificationDispatcherServiceTests : IDisposable
{
    private readonly List<string> _createdDbFiles = [];
    private readonly string _dataFolder = Path.Combine(AppContext.BaseDirectory, "Data");

    public void Dispose()
    {
        foreach (var file in _createdDbFiles)
        {
            var path = Path.Combine(_dataFolder, file);
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task DispatchAsync_EmptyServices_ReturnsZero()
    {
        var (sut, _, _) = CreateSut();
        var db = CreateUniqueDb();

        var result = await sut.DispatchAsync(MakeNotification(), [], db, "Test");

        result.Should().Be(0);
    }

    [Fact]
    public async Task DispatchAsync_RocketChatOnly_SendsOnce()
    {
        var (sut, rocketClient, _) = CreateSut();
        var rocket = CreateRocketChatService(rocketClient);
        var db = CreateUniqueDb();

        var result = await sut.DispatchAsync(MakeNotification(), [rocket], db, "Test");

        result.Should().Be(1);
        rocketClient.SendCalls.Should().HaveCount(1);
    }

    [Fact]
    public async Task DispatchAsync_RocketChatReturnsNull_ReturnsZero()
    {
        var (sut, rocketClient, _) = CreateSut();
        rocketClient.ReturnNull = true;
        var rocket = CreateRocketChatService(rocketClient);
        var db = CreateUniqueDb();

        var result = await sut.DispatchAsync(MakeNotification(), [rocket], db, "Test");

        result.Should().Be(0);
    }

    [Fact]
    public async Task DispatchAsync_UsesThreadedSendForRocketChat()
    {
        var (sut, rocketClient, _) = CreateSut();
        var rocket = CreateRocketChatService(rocketClient);
        var db = CreateUniqueDb();

        await sut.DispatchAsync(MakeNotification(), [rocket], db, "Test");

        rocketClient.SendCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task DispatchAsync_SecondSendToSameTicket_UsesThread()
    {
        var (sut, rocketClient, _) = CreateSut();
        var rocket = CreateRocketChatService(rocketClient);
        var db = CreateUniqueDb();

        await sut.DispatchAsync(MakeNotification(), [rocket], db, "Test");
        await sut.DispatchAsync(MakeNotification(), [rocket], db, "Test");

        rocketClient.SendCalls.Should().HaveCount(2);
        rocketClient.SendCalls[0].ThreadId.Should().BeNull("первое сообщение создаёт тред");
        rocketClient.SendCalls[1].ThreadId.Should().Be("msg-id-1", "второе сообщение идёт в тред");
    }

    [Fact]
    public async Task DispatchAsync_ClosedTicketWithSavedMessageId_EditsRootMessage()
    {
        var (sut, rocketClient, _) = CreateSut();
        var rocket = CreateRocketChatService(rocketClient);
        var db = CreateUniqueDb();

        await sut.DispatchAsync(MakeNotification(), [rocket], db, "Test");

        var closedNotification = MakeNotification() with
        {
            Ticket = MakeNotification().Ticket with { Status = "Закрыта" }
        };
        await sut.DispatchAsync(closedNotification, [rocket], db, "Test");

        rocketClient.UpdateCalls.Should().ContainSingle();
        var updateCall = rocketClient.UpdateCalls[0];
        updateCall.MessageId.Should().Be("msg-id-1");
        updateCall.NewText.Should().Be("Завершённая заявка #12345");
    }

    [Fact]
    public async Task DispatchAsync_OpenTicket_DoesNotEditRootMessage()
    {
        var (sut, rocketClient, _) = CreateSut();
        var rocket = CreateRocketChatService(rocketClient);
        var db = CreateUniqueDb();

        await sut.DispatchAsync(MakeNotification(), [rocket], db, "Test");

        var openNotification = MakeNotification() with
        {
            Ticket = MakeNotification().Ticket with { Status = "Новая" }
        };
        await sut.DispatchAsync(openNotification, [rocket], db, "Test");

        rocketClient.UpdateCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_ClosedTicketWithoutSavedMessageId_SkipsEdit()
    {
        var (sut, rocketClient, _) = CreateSut();
        var rocket = CreateRocketChatService(rocketClient);
        var db = CreateUniqueDb();

        var closedNotification = MakeNotification() with
        {
            Ticket = MakeNotification().Ticket with { Status = "Закрыта" }
        };
        await sut.DispatchAsync(closedNotification, [rocket], db, "Test");

        rocketClient.UpdateCalls.Should().BeEmpty();
        rocketClient.SendCalls.Should().ContainSingle("должно быть отправлено новое корневое сообщение");
        rocketClient.SendCalls[0].ThreadId.Should().BeNull();
    }

    [Fact]
    public async Task DispatchAsync_FirstMessageWithActionLinks_IncludesLinksInBody()
    {
        var (sut, rocketClient, _) = CreateSut();
        var rocket = CreateRocketChatService(rocketClient);
        var db = CreateUniqueDb();

        var notification = MakeNotification() with
        {
            ActionLinks =
            [
                new TicketActionLink
                {
                    Label = "➕ Создать задачу в Redmine",
                    Url = "http://ticket-monitor:8082/redmine/create?ticketId=12345"
                },
                new TicketActionLink
                {
                    Label = "✅ Добавить решение",
                    Url = "http://ticket-monitor:8082/glpi/solution?ticketId=12345"
                }
            ]
        };

        await sut.DispatchAsync(notification, [rocket], db, "Test");

        rocketClient.SendCalls.Should().ContainSingle();
        var body = rocketClient.SendCalls[0].Text;
        body.Should().Contain("Создать задачу в Redmine");
        body.Should().Contain("Добавить решение");
        body.Should().Contain("redmine/create?ticketId=12345");
        body.Should().Contain("glpi/solution?ticketId=12345");
    }

    [Fact]
    public async Task DispatchAsync_ThreadedMessage_DropsActionLinks()
    {
        var (sut, rocketClient, _) = CreateSut();
        var rocket = CreateRocketChatService(rocketClient);
        var db = CreateUniqueDb();

        var notification = MakeNotification() with
        {
            ActionLinks =
            [
                new TicketActionLink
                {
                    Label = "➕ Создать задачу в Redmine",
                    Url = "http://ticket-monitor:8082/redmine/create?ticketId=12345"
                }
            ]
        };

        await sut.DispatchAsync(notification, [rocket], db, "Test");
        await sut.DispatchAsync(notification, [rocket], db, "Test");

        rocketClient.SendCalls.Should().HaveCount(2);
        rocketClient.SendCalls[0].Text.Should().Contain("Создать задачу в Redmine");
        rocketClient.SendCalls[1].Text.Should().NotContain("Создать задачу в Redmine",
            "в тред-сообщении кнопки не должны дублироваться");
    }

    private string CreateUniqueDb()
    {
        var name = $"test_{Guid.NewGuid():N}.db";
        _createdDbFiles.Add(name);
        return name;
    }

    private static (NotificationDispatcherService Sut, FakeRocketChatClient RocketClient, ExchangeEmailService EmailService) CreateSut()
    {
        var emailOptions = Options.Create(new EmailOptions
        {
            SmtpServer = "mail.example.ru",
            Port = 25,
            From = "monitor@example.ru",
            To = "admin@example.ru"
        });

        var rocketClient = new FakeRocketChatClient();

        var parser = new EmailMessageParser();
        var renderer = new EmailTemplateRenderer(NullLogger<EmailTemplateRenderer>.Instance);
        var emailFormatter = new EmailHtmlFormatter(renderer, NullLogger<EmailHtmlFormatter>.Instance);
        var rcFormatter = new RocketChatMarkdownFormatter(NullLogger<RocketChatMarkdownFormatter>.Instance);

        var emailService = new ExchangeEmailService(
            emailOptions, parser, renderer, new NoOpOperationRegistry(),
            NullLogger<ExchangeEmailService>.Instance);

        var resolver = new FakeChannelResolver();
        var messageIdRepository = new FakeMessageIdRepository();

        var glpiOptions = Options.Create(new GlpiClientOptions
        {
            BaseUrl = "https://glpi.example.ru",
            AppToken = "test"
        });
        var httpFactory = new NoOpHttpClientFactory();
        var glpiClient = new GlpiClient(httpFactory, glpiOptions, new NoOpOperationRegistry(), NullLogger<GlpiClient>.Instance);

        var userGroupCache = new UserGroupCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            glpiClient,
            new NoOpOperationRegistry(),
            NullLogger<UserGroupCacheService>.Instance);

        var detailsCollector = new TicketDetailsCollector(
            glpiClient, userGroupCache,
            NullLogger<TicketDetailsCollector>.Instance);

        var sut = new NotificationDispatcherService(
            resolver, detailsCollector, glpiClient, new FakeGlpiUserCache(), messageIdRepository,
            emailFormatter, rcFormatter,
            NullLogger<NotificationDispatcherService>.Instance);

        return (sut, rocketClient, emailService);
    }

    private static RocketChatNotificationService CreateRocketChatService(IRocketChatClient client)
    {
        var options = Options.Create(new RocketChatOptions
        {
            ServerUrl = "https://chat.example.ru",
            DefaultRoomId = "room1",
            TimeoutSeconds = 30
        });

        return new RocketChatNotificationService(client, options, NullLogger<RocketChatNotificationService>.Instance);
    }

    private static NotificationMessage MakeNotification() => new()
    {
        Subject = "Тест",
        Kind = NotificationKind.New,
        Ticket = new TicketInfo
        {
            Id = "12345",
            Title = "Тестовая заявка",
            Status = "Новая",
            Initiator = "Иванов И.И.",
            Priority = "Средний",
            OpenDate = new DateTime(2026, 9, 15, 10, 0, 0),
            Url = "https://glpi/ticket/12345"
        },
        Description = "Описание",
        Answers = []
    };

    private sealed record SendCall(string RoomId, string Text, string? ThreadId);

    private sealed record UpdateCall(string RoomId, string MessageId, string NewText);

    private sealed class FakeRocketChatClient : IRocketChatClient
    {
        public List<SendCall> SendCalls { get; } = [];
        public List<UpdateCall> UpdateCalls { get; } = [];
        public bool ReturnNull { get; set; }

        public Task<string?> SendMessageAsync(
            string roomId, string text, string? threadMessageId,
            CancellationToken cancellationToken = default)
        {
            SendCalls.Add(new SendCall(roomId, text, threadMessageId));
            return Task.FromResult(ReturnNull ? null : "msg-id-1");
        }

        public Task<bool> UpdateMessageAsync(
            string roomId, string messageId, string newText,
            CancellationToken cancellationToken = default)
        {
            UpdateCalls.Add(new UpdateCall(roomId, messageId, newText));
            return Task.FromResult(true);
        }
    }

    private sealed class FakeChannelResolver : INotificationChannelResolver
    {
        public IReadOnlyList<INotificationService> ResolveForMonitor(MonitorConfig monitorConfig) => [];
        public IReadOnlyList<INotificationService> ResolveByTargets(IReadOnlyList<string>? targets) => [];
        public bool ContainsRocketChat(IReadOnlyList<INotificationService> services) => false;
        public RocketChatNotificationService GetRocketChatService()
            => throw new InvalidOperationException("Not used");
    }

    private sealed class FakeGlpiUserCache : IGlpiUserCacheService
    {
        public Task<string> GetUserNameAsync(object? userId, CancellationToken cancellationToken = default)
            => Task.FromResult(userId?.ToString() ?? string.Empty);

        public Task<IReadOnlyDictionary<string, string>> GetUserNamesBatchAsync(
            IEnumerable<object?> userIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }

    private sealed class FakeMessageIdRepository : IMessageIdRepository
    {
        private readonly Dictionary<string, string> _store = new(StringComparer.Ordinal);

        public string? GetMessageId(string ticketId, string dbFileName)
            => _store.TryGetValue(ticketId, out var id) ? id : null;

        public void SaveMessageId(string ticketId, string messageId, string dbFileName)
            => _store[ticketId] = messageId;
    }

    private sealed class NoOpHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}