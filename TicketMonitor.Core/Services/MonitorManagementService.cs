using LiteDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Background;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Models;
using TicketMonitor.Core.Models.Dto;
using System.Collections.Concurrent;

namespace TicketMonitor.Core.Services;

/// <summary>
/// Сервис динамического управления мониторами.
/// Хранит пул <see cref="MonitorRuntime"/> — по одному на каждый активный монитор.
/// Потокобезопасность: ConcurrentDictionary для чтения,
/// <see cref="SemaphoreSlim"/> — для эксклюзивных операций (start/stop/update).
/// </summary>
public sealed class MonitorManagementService : IMonitorManagementService
{
    private const string CollectionName = "monitors";
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(20);

    private readonly ConcurrentDictionary<string, MonitorRuntime> _runtimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MonitorManagementService> _logger;
    private readonly string _dbPath;

    /// <summary>Инициализирует сервис управления мониторами.</summary>
    public MonitorManagementService(IServiceProvider serviceProvider, IHostApplicationLifetime lifetime, ILogger<MonitorManagementService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var dataFolder = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataFolder);
        _dbPath = Path.Combine(dataFolder, "monitors.db");

        lifetime.ApplicationStopping.Register(OnApplicationStopping);
    }

    /// <inheritdoc />
    public void LoadMonitorsFromDatabase()
    {
        var monitors = ReadMonitorsFromDatabase();

        foreach (var monitor in monitors)
        {
            if (monitor.IsSystem) continue;
            NormalizeDatabaseFileName(monitor);

            var runtime = CreateRuntime(monitor);
            if (!_runtimes.TryAdd(monitor.Name, runtime))
            {
                _logger.LogWarning("Монитор '{MonitorName}' из БД пропущен: уже активен", monitor.Name);
                continue;
            }

            _ = StartRuntimeSafeAsync(runtime);

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Монитор '{MonitorName}' загружен из БД", monitor.Name);
        }
    }

    /// <inheritdoc />
    public bool AddMonitor(MonitorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.IsSystem)
        {
            _logger.LogWarning("Нельзя добавить системный монитор через AddMonitor");
            return false;
        }

        NormalizeDatabaseFileName(config);
        var runtime = CreateRuntime(config);

        if (!_runtimes.TryAdd(config.Name, runtime))
        {
            _logger.LogWarning("Монитор '{MonitorName}' уже существует", config.Name);
            return false;
        }

        UpsertMonitorToDatabase(config);
        _ = StartRuntimeSafeAsync(runtime);

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Монитор '{MonitorName}' добавлен, запущен и сохранён в БД", config.Name);

        return true;
    }

    /// <inheritdoc />
    public bool AddOrUpdateSystemMonitor(MonitorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!config.IsSystem)
        {
            _logger.LogWarning("Метод AddOrUpdateSystemMonitor можно использовать только для системных мониторов");
            return false;
        }

        NormalizeDatabaseFileName(config);

        _lifecycleGate.Wait();
        try
        {
            if (_runtimes.TryRemove(config.Name, out var existing))
            {
                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("Монитор '{MonitorName}' уже активен — перезапускается как системный", config.Name);

                StopRuntimeSafeAsync(existing).GetAwaiter().GetResult();
            }

            var runtime = CreateRuntime(config);
            _runtimes[config.Name] = runtime;
            _ = StartRuntimeSafeAsync(runtime);

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Системный монитор '{MonitorName}' запущен", config.Name);

            return true;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public bool UpdateMonitor(string monitorName, MonitorConfig newConfig)
    {
        ArgumentException.ThrowIfNullOrEmpty(monitorName);
        ArgumentNullException.ThrowIfNull(newConfig);

        if (!_runtimes.TryGetValue(monitorName, out var existing))
        {
            _logger.LogWarning("Монитор '{MonitorName}' не найден", monitorName);
            return false;
        }

        if (existing.Config.IsSystem)
        {
            _logger.LogWarning("Монитор '{MonitorName}' является системным и не может быть изменён", monitorName);
            return false;
        }

        NormalizeDatabaseFileName(newConfig);
        newConfig.Name = monitorName;

        _lifecycleGate.Wait();
        try
        {
            if (!_runtimes.TryGetValue(monitorName, out var current) || !ReferenceEquals(current, existing))
            {
                _logger.LogWarning("Монитор '{MonitorName}' был изменён параллельно", monitorName);
                return false;
            }

            StopRuntimeSafeAsync(existing).GetAwaiter().GetResult();

            var runtime = CreateRuntime(newConfig);
            _runtimes[monitorName] = runtime;
            _ = StartRuntimeSafeAsync(runtime);
        }
        finally
        {
            _lifecycleGate.Release();
        }

        UpsertMonitorToDatabase(newConfig);

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Монитор '{MonitorName}' обновлён", monitorName);

        return true;
    }

    /// <inheritdoc />
    public bool RemoveMonitor(string monitorName)
    {
        ArgumentException.ThrowIfNullOrEmpty(monitorName);

        if (!_runtimes.TryGetValue(monitorName, out var runtime))
        {
            _logger.LogWarning("Монитор '{MonitorName}' не найден", monitorName);
            return false;
        }

        if (runtime.Config.IsSystem)
        {
            _logger.LogWarning("Монитор '{MonitorName}' является системным и не может быть удалён", monitorName);
            return false;
        }

        _lifecycleGate.Wait();
        try
        {
            if (!_runtimes.TryRemove(monitorName, out var removed))
            {
                _logger.LogWarning("Монитор '{MonitorName}' уже удалён", monitorName);
                return false;
            }

            StopRuntimeSafeAsync(removed).GetAwaiter().GetResult();
        }
        finally
        {
            _lifecycleGate.Release();
        }

        DeleteMonitorFromDatabase(monitorName);

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Монитор '{MonitorName}' удалён", monitorName);

        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<MonitorConfig> GetAllMonitors() => [.. _runtimes.Values.Select(r => r.Config)];

    /// <inheritdoc />
    public MonitorConfig? GetMonitor(string monitorName)
    {
        if (string.IsNullOrEmpty(monitorName)) return null;
        return _runtimes.TryGetValue(monitorName, out var runtime) ? runtime.Config : null;
    }

    /// <inheritdoc />
    public async Task<bool> RunCheckAsync(string monitorName)
    {
        if (string.IsNullOrEmpty(monitorName)) return false;

        if (!_runtimes.TryGetValue(monitorName, out var runtime))
        {
            _logger.LogWarning("Монитор '{MonitorName}' не найден", monitorName);
            return false;
        }

        var worker = runtime.Worker;
        if (worker is null)
        {
            _logger.LogWarning("Монитор '{MonitorName}' ещё не запущен — проверка невозможна", monitorName);
            return false;
        }

        return await worker.CheckAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public MonitorStatusDto? GetStatus(string monitorName)
    {
        if (string.IsNullOrEmpty(monitorName)) return null;
        return _runtimes.TryGetValue(monitorName, out var runtime) ? runtime.Worker?.GetStatus() : null;
    }

    private MonitorRuntime CreateRuntime(MonitorConfig config)
    {
        var logger = _serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger($"TicketMonitor.Core.Background.MonitorRuntime[{config.Name}]");

        return new MonitorRuntime(config, _serviceProvider, logger);
    }

    private async Task StartRuntimeSafeAsync(MonitorRuntime runtime)
    {
        try
        {
            await runtime.StartAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось запустить воркер монитора '{MonitorName}'", runtime.Config.Name);
        }
    }

    private async Task StopRuntimeSafeAsync(MonitorRuntime runtime)
    {
        try
        {
            await runtime.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при остановке воркера монитора '{MonitorName}'", runtime.Config.Name);
        }
        finally
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void NormalizeDatabaseFileName(MonitorConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.DatabaseFileName))
            throw new InvalidOperationException($"DatabaseFileName не задан для монитора '{config.Name}'");

        if (!config.DatabaseFileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            config.DatabaseFileName += ".db";
    }

    private List<MonitorConfig> ReadMonitorsFromDatabase()
    {
        try
        {
            using var db = new LiteDatabase(_dbPath);
            var collection = db.GetCollection<MonitorConfig>(CollectionName);
            return [.. collection.FindAll()];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось прочитать мониторы из БД {DbPath}", _dbPath);
            return [];
        }
    }

    private void UpsertMonitorToDatabase(MonitorConfig config)
    {
        try
        {
            using var db = new LiteDatabase(_dbPath);
            var collection = db.GetCollection<MonitorConfig>(CollectionName);
            collection.Upsert(config);

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Монитор '{MonitorName}' сохранён в БД", config.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сохранить монитор '{MonitorName}' в БД", config.Name);
        }
    }

    private void DeleteMonitorFromDatabase(string monitorName)
    {
        try
        {
            using var db = new LiteDatabase(_dbPath);
            var collection = db.GetCollection<MonitorConfig>(CollectionName);
            collection.Delete(monitorName);

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Монитор '{MonitorName}' удалён из БД", monitorName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось удалить монитор '{MonitorName}' из БД", monitorName);
        }
    }

    private void OnApplicationStopping()
    {
        var runtimes = _runtimes.Values.ToArray();
        if (runtimes.Length == 0) return;

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Остановка {Count} воркеров мониторов...", runtimes.Length);

        try
        {
            var stopAll = Task.WhenAll(runtimes.Select(StopRuntimeSafeAsync));

            if (!stopAll.Wait(ShutdownTimeout))
                _logger.LogWarning("Не все воркеры остановились за {Timeout} секунд. Принудительное завершение", ShutdownTimeout.TotalSeconds);
            else if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Все воркеры остановлены");
        }
        catch (AggregateException ex)
        {
            _logger.LogError(ex.Flatten(), "Ошибки при остановке воркеров");
        }
        finally
        {
            _runtimes.Clear();
        }
    }
}