using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Clients;

namespace TicketMonitor.Core.Helpers;

/// <summary>Вспомогательные методы для работы с уведомлениями.</summary>
public static partial class NotificationHelper
{
    [GeneratedRegex("<.*?>")]
    private static partial Regex HtmlTagRegex();

    public static readonly Dictionary<int, string> StatusMap = new()
    {
        { 1, "Новая" },
        { 2, "В работе (назначена)" },
        { 3, "В работе (запланирована)" },
        { 4, "Ожидание" },
        { 5, "Решена" },
        { 6, "Закрыта" },
        { 10, "Согласование" }
    };

    public static readonly Dictionary<int, string> PriorityMap = new()
    {
        { 1, "Очень низкий" },
        { 2, "Низкий" },
        { 3, "Средний" },
        { 4, "Высокий" },
        { 5, "Очень высокий" }
    };

    /// <summary>Удаляет HTML-теги и декодирует HTML-сущности.</summary>
    public static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var text = HtmlTagRegex().Replace(html, string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
    }

    public static int? GetInt(Dictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var value) && value != null)
        {
            if (value is int intValue) return intValue;
            if (int.TryParse(value.ToString(), out int parsed)) return parsed;
        }
        return null;
    }

    public static int? GetInt(Dictionary<string, object?> data, int key) => GetInt(data, key.ToString());

    public static string? GetString(Dictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var value) && value != null)
            return value.ToString();
        return null;
    }

    public static string? GetString(Dictionary<string, object?> data, int key) => GetString(data, key.ToString());

    /// <summary>Получает имя пользователя GLPI по его ID.</summary>
    public static async Task<string> GetUserNameById(int userId, GlpiClient glpiClient, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (userId == 0) return "Неизвестный пользователь";

        try
        {
            var url = $"apirest.php/User/{userId}";
            using var request = glpiClient.CreateRequest(HttpMethod.Get, url);
            using var response = await glpiClient.SendWithSessionRetryAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                var realname = doc.RootElement.TryGetProperty("realname", out var r) ? r.GetString() ?? string.Empty : string.Empty;
                var firstname = doc.RootElement.TryGetProperty("firstname", out var f) ? f.GetString() ?? string.Empty : string.Empty;

                var fullName = $"{realname} {firstname}".Trim();
                return string.IsNullOrEmpty(fullName) ? $"Пользователь {userId}" : fullName;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось получить имя пользователя {UserId}", userId);
        }

        return $"Пользователь {userId}";
    }
}