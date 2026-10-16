using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TicketMonitor.Core.Clients;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Core.Helpers;
using TicketMonitor.Core.Models;

namespace TicketMonitor.Core.Services;

/// <summary>Сервис получения заявок из GLPI API.</summary>
public class GlpiTicketService(
    GlpiClient glpiClient,
    IGlpiUserCacheService userCache,
    IOperationRegistry operationRegistry,
    ILogger<GlpiTicketService> logger) : ITicketService
{
    private readonly GlpiClient _glpiClient = glpiClient ?? throw new ArgumentNullException(nameof(glpiClient));
    private readonly IGlpiUserCacheService _userCache = userCache ?? throw new ArgumentNullException(nameof(userCache));
    private readonly IOperationRegistry _operationRegistry = operationRegistry ?? throw new ArgumentNullException(nameof(operationRegistry));
    private readonly ILogger<GlpiTicketService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<List<TicketInfo>> GetTicketsByCategoryAsync(string categoryFilter, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryFilter);

        return await _operationRegistry.RunAsync(
            "GLPI:FetchTicketsByCategory",
            async ct =>
            {
                var allTickets = new List<TicketInfo>();
                var seenIds = new HashSet<string>(StringComparer.Ordinal);
                var start = 0;
                const int batchSize = 100;

                while (true)
                {
                    var url = BuildSearchUrl(start, batchSize);
                    var payload = new
                    {
                        criteria = new[]
                        {
                            new { link = "AND", field = GlpiFields.Status, searchtype = "equals", value = "notold" },
                            new { link = "AND", field = GlpiFields.Category, searchtype = "contains", value = categoryFilter }
                        }
                    };

                    var jsonPayload = JsonSerializer.Serialize(payload);
                    using var request = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                    };
                    request.Headers.Add("Session-Token", _glpiClient.GetSessionToken());

                    using var response = await _glpiClient.SendAsync(request, ct).ConfigureAwait(false);
                    var ticketsData = await _glpiClient.ParseResponse(response, ct).ConfigureAwait(false);

                    if (ticketsData.Count == 0) break;

                    var ticketsBatch = ticketsData
                        .Where(d => !string.IsNullOrEmpty(NotificationHelper.GetString(d, GlpiFields.Id)))
                        .Where(d => seenIds.Add(NotificationHelper.GetString(d, GlpiFields.Id)!))
                        .ToList();

                    if (ticketsBatch.Count == 0)
                    {
                        if (ticketsData.Count < batchSize) break;
                        start += batchSize;
                        continue;
                    }

                    var userIds = new List<object?>(ticketsBatch.Count * 2);
                    foreach (var data in ticketsBatch)
                    {
                        userIds.Add(GetUserId(data, GlpiFields.Requester));
                        userIds.Add(GetUserId(data, GlpiFields.Assignee));
                    }

                    var userNames = await _userCache.GetUserNamesBatchAsync(userIds, ct).ConfigureAwait(false);

                    foreach (var data in ticketsBatch)
                    {
                        var ticket = ParseTicket(data, userNames);
                        if (ticket is not null) allTickets.Add(ticket);
                    }

                    if (ticketsData.Count < batchSize) break;
                    start += batchSize;
                }

                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("Получено {Count} заявок для категории '{Category}'", allTickets.Count, categoryFilter);

                return allTickets;
            },
            context: new { categoryFilter },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string BuildSearchUrl(int start, int batchSize)
    {
        return $"apirest.php/search/Ticket?range={start}-{start + batchSize - 1}" +
               $"&order=ASC&sortBy={GlpiFields.Id}&expand_dropdowns=true" +
               $"&forcedisplay[0]={GlpiFields.Id}" +
               $"&forcedisplay[1]={GlpiFields.Title}" +
               $"&forcedisplay[2]={GlpiFields.Status}" +
               $"&forcedisplay[3]={GlpiFields.OpenDate}" +
               $"&forcedisplay[4]={GlpiFields.Requester}" +
               $"&forcedisplay[5]={GlpiFields.Assignee}" +
               $"&forcedisplay[6]={GlpiFields.Category}" +
               $"&forcedisplay[7]={GlpiFields.Priority}" +
               $"&forcedisplay[8]={GlpiFields.LastModified}";
    }

    private static TicketInfo? ParseTicket(Dictionary<string, object?> data, IReadOnlyDictionary<string, string> userNames)
    {
        var id = NotificationHelper.GetString(data, GlpiFields.Id);
        if (string.IsNullOrEmpty(id)) return null;

        var statusCode = NotificationHelper.GetInt(data, GlpiFields.Status) ?? 0;
        var statusText = NotificationHelper.StatusMap.TryGetValue(statusCode, out var mappedStatus) ? mappedStatus : statusCode.ToString();

        var priorityCode = NotificationHelper.GetInt(data, GlpiFields.Priority) ?? 0;
        var priorityText = NotificationHelper.PriorityMap.TryGetValue(priorityCode, out var mappedPriority) ? mappedPriority : priorityCode.ToString();

        var initiatorId = GetUserId(data, GlpiFields.Requester);
        var initiatorName = ResolveName(userNames, initiatorId);

        var assigneeId = GetUserId(data, GlpiFields.Assignee);
        var assigneeName = ResolveName(userNames, assigneeId);

        return new TicketInfo
        {
            Id = id,
            Title = NotificationHelper.GetString(data, GlpiFields.Title) ?? string.Empty,
            Status = statusText,
            OpenDate = ParseDate(NotificationHelper.GetString(data, GlpiFields.OpenDate) ?? string.Empty),
            Initiator = initiatorName,
            Assignee = assigneeName,
            Category = NotificationHelper.GetString(data, GlpiFields.Category) ?? string.Empty,
            Priority = priorityText,
            LastModified = ParseDate(NotificationHelper.GetString(data, GlpiFields.LastModified) ?? string.Empty),
            Url = $"https://glpi.example.ru/front/ticket.form.php?id={id}"
        };
    }

    private static string ResolveName(IReadOnlyDictionary<string, string> userNames, string userId)
    {
        if (string.IsNullOrEmpty(userId)) return string.Empty;
        return userNames.TryGetValue(userId, out var name) ? name : userId;
    }

    private static string GetUserId(Dictionary<string, object?> data, int key)
    {
        if (!data.TryGetValue(key.ToString(), out var value) || value is null)
            return string.Empty;

        if (value is string str) return str;

        if (value is System.Collections.IEnumerable list and not string)
        {
            foreach (var item in list)
            {
                if (item is not null) return item.ToString() ?? string.Empty;
            }
            return string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }

    private static DateTime ParseDate(string dateString)
    {
        if (DateTime.TryParseExact(dateString, "yyyy-MM-dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
            return date;

        return DateTime.TryParse(dateString, out date) ? date : DateTime.MinValue;
    }
}