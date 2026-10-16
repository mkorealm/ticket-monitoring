using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Services;
using Xunit;

namespace TicketMonitor.Core.Tests;

/// <summary>
/// Юнит-тесты <see cref="GlpiUserCacheService"/>.
/// Используют фейковый <see cref="IGlpiUserResolver"/> — без реального GLPI.
/// </summary>
public sealed class GlpiUserCacheServiceTests
{
    // -------------------------------------------------------------------------
    // Одиночный GetUserNameAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetUserNameAsync_NullInput_ReturnsEmpty()
    {
        var resolver = new FakeResolver();
        var sut = CreateService(resolver);

        var result = await sut.GetUserNameAsync(null);

        result.Should().BeEmpty();
        resolver.Requests.Should().BeEmpty("не должно быть HTTP-запросов для null");
    }

    [Fact]
    public async Task GetUserNameAsync_ZeroInput_ReturnsEmpty()
    {
        var resolver = new FakeResolver();
        var sut = CreateService(resolver);

        var result = await sut.GetUserNameAsync(0);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserNameAsync_EmptyString_ReturnsEmpty()
    {
        var sut = CreateService(new FakeResolver());

        var result = await sut.GetUserNameAsync("");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserNameAsync_IdNotFound_ReturnsIdAsFallback()
    {
        var resolver = new FakeResolver();
        // Резолвер вернёт null → fallback на ID
        var sut = CreateService(resolver);

        var result = await sut.GetUserNameAsync("12345");

        result.Should().Be("12345");
    }

    [Fact]
    public async Task GetUserNameAsync_ValidId_ReturnsFullName()
    {
        var resolver = new FakeResolver();
        resolver.AddUser("12345", realname: "Иванов", firstname: "Иван");
        var sut = CreateService(resolver);

        var result = await sut.GetUserNameAsync("12345");

        result.Should().Be("Иванов Иван");
    }

    [Fact]
    public async Task GetUserNameAsync_OnlyRealname_ReturnsRealname()
    {
        var resolver = new FakeResolver();
        resolver.AddUser("1", realname: "Иванов", firstname: "");
        var sut = CreateService(resolver);

        var result = await sut.GetUserNameAsync("1");

        result.Should().Be("Иванов");
    }

    [Fact]
    public async Task GetUserNameAsync_CacheHit_DoesNotCallResolver()
    {
        var resolver = new FakeResolver();
        resolver.AddUser("1", realname: "Иванов", firstname: "И");
        var sut = CreateService(resolver);

        await sut.GetUserNameAsync("1");
        var requestsAfterFirst = resolver.Requests.Count;

        await sut.GetUserNameAsync("1"); // второй вызов — из кэша

        resolver.Requests.Count.Should().Be(requestsAfterFirst,
            "второй вызов должен идти из кэша, без HTTP");
    }

    [Fact]
    public async Task GetUserNameAsync_ArrayInput_UnwrapsFirstElement()
    {
        var resolver = new FakeResolver();
        resolver.AddUser("42", realname: "Тест", firstname: "");
        var sut = CreateService(resolver);

        var result = await sut.GetUserNameAsync(new List<object> { "42" });

        result.Should().Be("Тест");
    }

    [Fact]
    public async Task GetUserNameAsync_LoginSearch_FindsUser()
    {
        var resolver = new FakeResolver();
        resolver.AddUserByLogin("ivanov", realname: "Иванов", firstname: "И.");
        var sut = CreateService(resolver);

        var result = await sut.GetUserNameAsync("ivanov");

        result.Should().Be("Иванов И.");
    }

    [Fact]
    public async Task GetUserNameAsync_LoginNotFound_ReturnsLogin()
    {
        var resolver = new FakeResolver();
        var sut = CreateService(resolver);

        var result = await sut.GetUserNameAsync("unknown_login");

        result.Should().Be("unknown_login");
    }

    // -------------------------------------------------------------------------
    // Thundering herd protection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetUserNameAsync_ParallelSameId_OnlyOneHttpRequest()
    {
        var resolver = new FakeResolver
        {
            DelayPerRequest = TimeSpan.FromMilliseconds(100)
        };
        resolver.AddUser("1", realname: "Иванов", firstname: "");
        var sut = CreateService(resolver);

        // Запускаем 10 параллельных вызовов одного ID
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => sut.GetUserNameAsync("1"))
            .ToList();

        var results = await Task.WhenAll(tasks);

        results.Should().AllBe("Иванов");
        resolver.Requests.Should().ContainSingle(
            "thundering herd protection должен схлопнуть 10 вызовов в 1 HTTP-запрос");
    }

    // -------------------------------------------------------------------------
    // GetUserNamesBatchAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetUserNamesBatchAsync_NullInput_Throws()
    {
        var sut = CreateService(new FakeResolver());

        var act = async () => await sut.GetUserNamesBatchAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetUserNamesBatchAsync_EmptyInput_ReturnsEmpty()
    {
        var sut = CreateService(new FakeResolver());

        var result = await sut.GetUserNamesBatchAsync([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserNamesBatchAsync_SingleId_Resolves()
    {
        var resolver = new FakeResolver();
        resolver.AddUser("1", realname: "Иванов", firstname: "");
        var sut = CreateService(resolver);

        var result = await sut.GetUserNamesBatchAsync(["1"]);

        result.Should().ContainKey("1").WhoseValue.Should().Be("Иванов");
    }

    [Fact]
    public async Task GetUserNamesBatchAsync_MultipleIds_ResolvesAll()
    {
        var resolver = new FakeResolver();
        resolver.AddUser("1", realname: "Первый", firstname: "");
        resolver.AddUser("2", realname: "Второй", firstname: "");
        resolver.AddUser("3", realname: "Третий", firstname: "");
        var sut = CreateService(resolver);

        var result = await sut.GetUserNamesBatchAsync(["1", "2", "3"]);

        result.Should().HaveCount(3);
        result["1"].Should().Be("Первый");
        result["2"].Should().Be("Второй");
        result["3"].Should().Be("Третий");
    }

    [Fact]
    public async Task GetUserNamesBatchAsync_Duplicates_Deduplicated()
    {
        var resolver = new FakeResolver();
        resolver.AddUser("1", realname: "Иванов", firstname: "");
        var sut = CreateService(resolver);

        var result = await sut.GetUserNamesBatchAsync(["1", "1", "1", "1"]);

        result.Should().HaveCount(1);
        resolver.Requests.Should().ContainSingle("дубликаты должны дедуплицироваться");
    }

    [Fact]
    public async Task GetUserNamesBatchAsync_SomeInCache_OnlyMissingResolved()
    {
        var resolver = new FakeResolver();
        resolver.AddUser("1", realname: "Первый", firstname: "");
        resolver.AddUser("2", realname: "Второй", firstname: "");
        resolver.AddUser("3", realname: "Третий", firstname: "");
        var sut = CreateService(resolver);

        // Первый вызов — заполняет кэш для "1"
        await sut.GetUserNameAsync("1");
        var requestsAfterFirst = resolver.Requests.Count;

        // Batch с тремя ID — должны резолвиться только "2" и "3"
        var result = await sut.GetUserNamesBatchAsync(["1", "2", "3"]);

        result.Should().HaveCount(3);
        resolver.Requests.Count.Should().Be(requestsAfterFirst + 2,
            "ID '1' должен взяться из кэша, '2' и '3' — зарезолвиться");
    }

    [Fact]
    public async Task GetUserNamesBatchAsync_EmptyAndNullIds_Ignored()
    {
        var resolver = new FakeResolver();
        resolver.AddUser("1", realname: "Иванов", firstname: "");
        var sut = CreateService(resolver);

        var result = await sut.GetUserNamesBatchAsync(["1", null, "", "0", "  "]);

        result.Should().HaveCount(1);
        result.Should().ContainKey("1");
    }

    [Fact]
    public async Task GetUserNamesBatchAsync_ParallelLimit_RespectsMaxConcurrency()
    {
        // 20 ID, MaxParallelRequests = 8
        // Резолвер должен увидеть максимум 8 параллельных запросов
        var resolver = new FakeResolver
        {
            DelayPerRequest = TimeSpan.FromMilliseconds(50)
        };

        for (var i = 1; i <= 20; i++)
            resolver.AddUser(i.ToString(), realname: $"User{i}", firstname: "");

        var sut = CreateService(resolver);

        var result = await sut.GetUserNamesBatchAsync(
            Enumerable.Range(1, 20).Select(i => (object?)i.ToString()));

        result.Should().HaveCount(20);
        resolver.MaxObservedConcurrency.Should().BeLessOrEqualTo(8,
            "SemaphoreSlim должен ограничивать параллелизм до 8");
        resolver.MaxObservedConcurrency.Should().BeGreaterThan(1,
            "запросы должны идти параллельно, а не последовательно");
    }

    [Fact]
    public async Task GetUserNamesBatchAsync_NotFound_ReturnsOriginalIdAsValue()
    {
        var resolver = new FakeResolver();
        // ID "999" нет в резолвере
        var sut = CreateService(resolver);

        var result = await sut.GetUserNamesBatchAsync(["999"]);

        result["999"].Should().Be("999", "если пользователь не найден — fallback на ID");
    }

    // -------------------------------------------------------------------------
    // Отрицательное кэширование
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetUserNameAsync_NotFound_CachedNegatively()
    {
        var resolver = new FakeResolver();
        var sut = CreateService(resolver);

        // Первый вызов — идёт в API, ничего не находит
        await sut.GetUserNameAsync("999");
        var requestsAfterFirst = resolver.Requests.Count;

        // Второй вызов — из отрицательного кэша
        await sut.GetUserNameAsync("999");

        resolver.Requests.Count.Should().Be(requestsAfterFirst,
            "несуществующий ID должен кэшироваться и не долбить API повторно");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static GlpiUserCacheService CreateService(IGlpiUserResolver resolver)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new GlpiUserCacheService(
            cache,
            resolver,
            NullLogger<GlpiUserCacheService>.Instance);
    }

    // -------------------------------------------------------------------------
    // Fake IGlpiUserResolver
    // -------------------------------------------------------------------------

    private sealed class FakeResolver : IGlpiUserResolver
    {
        private readonly Dictionary<string, string> _byId = [];
        private readonly Dictionary<string, string> _byLogin = [];

        public TimeSpan DelayPerRequest { get; init; } = TimeSpan.Zero;
        public List<string> Requests { get; } = [];
        public int CurrentConcurrency;
        public int MaxObservedConcurrency;

        public void AddUser(string id, string realname, string firstname)
        {
            var json = BuildUserJson(realname, firstname, login: null);
            _byId[id] = json;
        }

        public void AddUserByLogin(string login, string realname, string firstname)
        {
            var json = BuildUserJson(realname, firstname, login);
            _byLogin[login] = json;
        }

        public async Task<JsonElement?> GetUserByIdAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            await TrackConcurrency(async () =>
            {
                Requests.Add($"byId:{userId}");

                if (DelayPerRequest > TimeSpan.Zero)
                    await Task.Delay(DelayPerRequest, cancellationToken);
            }).ConfigureAwait(false);

            if (!_byId.TryGetValue(userId, out var json))
                return null;

            return ParseSingle(json);
        }

        public async Task<IReadOnlyList<JsonElement>> SearchUsersByLoginAsync(
            string login,
            CancellationToken cancellationToken = default)
        {
            await TrackConcurrency(async () =>
            {
                Requests.Add($"byLogin:{login}");

                if (DelayPerRequest > TimeSpan.Zero)
                    await Task.Delay(DelayPerRequest, cancellationToken);
            }).ConfigureAwait(false);

            if (!_byLogin.TryGetValue(login, out var json))
                return [];

            return [ParseSingle(json)];
        }

        private async Task TrackConcurrency(Func<Task> action)
        {
            var current = Interlocked.Increment(ref CurrentConcurrency);

            // Обновляем максимум атомарно через CAS-цикл
            int observed;
            do
            {
                observed = MaxObservedConcurrency;
                if (current <= observed)
                    break;
            } while (Interlocked.CompareExchange(
                ref MaxObservedConcurrency, current, observed) != observed);

            try
            {
                await action().ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref CurrentConcurrency);
            }
        }

        private static JsonElement ParseSingle(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        private static string BuildUserJson(string realname, string firstname, string? login)
        {
            var loginPart = login is null ? "" : $",\"name\":\"{login}\"";
            return $"{{\"realname\":\"{realname}\",\"firstname\":\"{firstname}\"{loginPart}}}";
        }
    }
}