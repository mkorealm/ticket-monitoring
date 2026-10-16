using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Helpers;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Services;

/// <summary>Результат сбора ответов по заявке.</summary>
public sealed record AnswersCollectionResult
{
    /// <summary>Показанные ответы (после обрезки до лимита).</summary>
    public required IReadOnlyList<NotificationAnswer> Answers { get; init; }

    /// <summary>Общее количество подходящих ответов (до обрезки).</summary>
    public required int TotalCount { get; init; }
}

/// <summary>Сборщик деталей заявки для уведомлений.</summary>
public sealed class TicketDetailsCollector(
    GlpiClient glpiClient,
    UserGroupCacheService userGroupCache,
    ILogger<TicketDetailsCollector> logger)
{
    private const int DefaultMaxAnswers = 5;

    private readonly GlpiClient _glpiClient = glpiClient ?? throw new ArgumentNullException(nameof(glpiClient));
    private readonly UserGroupCacheService _userGroupCache = userGroupCache ?? throw new ArgumentNullException(nameof(userGroupCache));
    private readonly ILogger<TicketDetailsCollector> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Возвращает описание заявки, очищенное от HTML.</summary>
    public async Task<string?> GetDescriptionAsync(string ticketId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);

        try
        {
            var ticket = await _glpiClient.GetTicketAsync(ticketId, cancellationToken).ConfigureAwait(false);

            if (!ticket.TryGetValue("content", out var contentObj))
                return null;

            var content = contentObj?.ToString();
            if (string.IsNullOrEmpty(content))
                return null;

            return NotificationHelper.StripHtml(content);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось получить описание заявки #{TicketId}", ticketId);
            return null;
        }
    }

    /// <summary>
    /// Возвращает ответы по заявке, исключая ответы группы поддержки.
    /// Показывает только <paramref name="maxAnswers"/> последних ответов,
    /// но возвращает и общее количество (для счётчика).
    /// </summary>
    /// <param name="ticketId">ID заявки.</param>
    /// <param name="supportGroupId">ID группы поддержки (её ответы игнорируются).</param>
    /// <param name="since">Показывать только ответы после этой даты. Если <c>null</c> — все.</param>
    /// <param name="maxAnswers">Максимум ответов в уведомлении.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task<AnswersCollectionResult> GetAnswersAsync(
        string ticketId,
        int supportGroupId,
        DateTime? since = null,
        int maxAnswers = DefaultMaxAnswers,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAnswers);

        try
        {
            var followups = await _glpiClient.GetTicketFollowupsAsync(ticketId, cancellationToken).ConfigureAwait(false);
            if (followups.Count == 0)
            {
                return new AnswersCollectionResult
                {
                    Answers = [],
                    TotalCount = 0
                };
            }

            var supportUsers = await _userGroupCache.GetGroupUsersAsync(supportGroupId, cancellationToken).ConfigureAwait(false);

            // Собираем все подходящие ответы (с датой для сортировки).
            var candidates = new List<(DateTime Date, NotificationAnswer Answer)>();

            foreach (var followup in followups)
            {
                var userId = NotificationHelper.GetInt(followup, "users_id") ?? 0;
                if (supportUsers.Contains(userId))
                    continue;

                var content = followup.TryGetValue("content", out var c) ? c?.ToString() ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(content))
                    continue;

                var date = ParseFollowupDate(followup);

                // Фильтр по дате: если since задан — пропускаем старые ответы.
                if (since.HasValue && date < since.Value)
                    continue;

                var userName = await NotificationHelper.GetUserNameById(userId, _glpiClient, _logger, cancellationToken).ConfigureAwait(false);

                candidates.Add((date, new NotificationAnswer
                {
                    Author = userName,
                    Text = NotificationHelper.StripHtml(content)
                }));
            }

            var totalCount = candidates.Count;

            // Берём последние maxAnswers, восстанавливаем хронологический порядок.
            var recent = candidates
                .OrderByDescending(c => c.Date)
                .Take(maxAnswers)
                .OrderBy(c => c.Date)
                .Select(c => c.Answer)
                .ToList();

            return new AnswersCollectionResult
            {
                Answers = recent,
                TotalCount = totalCount
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось получить ответы по заявке #{TicketId}", ticketId);
            return new AnswersCollectionResult
            {
                Answers = [],
                TotalCount = 0
            };
        }
    }

    /// <summary>Извлекает дату followup из GLPI.</summary>
    private static DateTime ParseFollowupDate(Dictionary<string, object?> followup)
    {
        var raw = NotificationHelper.GetString(followup, "date")
               ?? NotificationHelper.GetString(followup, "date_creation");

        if (string.IsNullOrWhiteSpace(raw))
            return DateTime.MinValue;

        if (DateTime.TryParseExact(raw, "yyyy-MM-dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var exact))
            return exact;

        return DateTime.TryParse(raw, out var parsed) ? parsed : DateTime.MinValue;
    }

    /// <summary>Извлекает строки-дельты из текстового сообщения детектора.</summary>
    public static IReadOnlyList<string> ExtractDeltas(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return [];

        var lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var deltas = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Contains('→') && trimmed.Contains(':'))
                deltas.Add(trimmed);
        }

        return deltas;
    }
}