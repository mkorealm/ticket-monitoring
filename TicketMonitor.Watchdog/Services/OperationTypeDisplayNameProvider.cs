using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TicketMonitor.Watchdog.Services;

/// <summary>
/// Провайдер человекочитаемых имён типов операций.
/// Читает <c>operation-types.json</c> из папки приложения один раз при старте.
/// </summary>
public sealed class OperationTypeDisplayNameProvider
{
    private const string FileName = "operation-types.json";

    private readonly ILogger<OperationTypeDisplayNameProvider> _logger;
    private readonly IReadOnlyDictionary<string, string> _mapping;

    /// <summary>Инициализирует провайдер и загружает маппинг.</summary>
    public OperationTypeDisplayNameProvider(ILogger<OperationTypeDisplayNameProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mapping = LoadMapping();
    }

    /// <summary>
    /// Возвращает человекочитаемое имя типа операции.
    /// Если тип не найден в маппинге — возвращает сам тип.
    /// </summary>
    public string GetDisplayName(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return string.Empty;

        return _mapping.TryGetValue(type, out var name) ? name : type;
    }

    private IReadOnlyDictionary<string, string> LoadMapping()
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName);

        if (!File.Exists(path))
        {
            _logger.LogWarning("Файл {FileName} не найден — имена операций будут отображаться как есть", FileName);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var json = File.ReadAllText(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict is null)
            {
                _logger.LogWarning("Файл {FileName} пуст — имена операций будут отображаться как есть", FileName);
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Загружено {Count} имён типов операций из {FileName}", dict.Count, FileName);

            return new Dictionary<string, string>(dict, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка чтения {FileName} — имена операций будут отображаться как есть", FileName);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}