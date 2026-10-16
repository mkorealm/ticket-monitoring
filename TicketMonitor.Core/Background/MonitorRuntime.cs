using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Background;

/// <summary>Runtime-состояние одного монитора.</summary>
internal sealed class MonitorRuntime : IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private TicketMonitorWorker? _worker;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private bool _started;
    private bool _disposed;

    public MonitorConfig Config { get; }

    /// <summary>Инициализирует новый runtime монитора.</summary>
    public MonitorRuntime(MonitorConfig config, IServiceProvider serviceProvider, ILogger logger)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public TicketMonitorWorker? Worker => _worker;

    /// <summary>Запускает воркер. Идемпотентно.</summary>
    public async Task StartAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;

            _cts = new CancellationTokenSource();
            _worker = ActivatorUtilities.CreateInstance<TicketMonitorWorker>(_serviceProvider, Config);

            var token = _cts.Token;
            _runTask = Task.Run(async () =>
            {
                try
                {
                    await _worker.RunAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Воркер монитора '{MonitorName}' упал", Config.Name);
                }
            }, token);

            _started = true;

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Воркер монитора '{MonitorName}' запущен", Config.Name);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>Останавливает воркер. Идемпотентно.</summary>
    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_started) return;

            try
            {
                if (_cts is not null) await _cts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException) { }

            if (_runTask is not null)
            {
                try
                {
                    await _runTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при ожидании воркера '{MonitorName}'", Config.Name);
                }
            }

            _worker?.Dispose();
            _worker = null;
            _cts?.Dispose();
            _cts = null;
            _runTask = null;
            _started = false;

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Воркер монитора '{MonitorName}' остановлен", Config.Name);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _disposed = true;
            _lifecycleGate.Dispose();
        }
    }
}