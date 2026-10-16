using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json;
using TicketMonitor.Core.Contracts;
using TicketMonitor.Redmine.Models;

namespace TicketMonitor.Redmine.Clients;

/// <summary>HTTP-клиент Redmine REST API через именованный <see cref="HttpClient"/>.</summary>
public sealed class RedmineClient(
    IHttpClientFactory httpClientFactory,
    IOptions<RedmineOptions> options,
    IOperationRegistry operationRegistry,
    ILogger<RedmineClient> logger) : IRedmineClient
{
    /// <summary>Имя именованного HttpClient, настроенного в DI.</summary>
    public const string HttpClientName = "redmine";

    private const int DefaultPageSize = 100;

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly RedmineOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly IOperationRegistry _operationRegistry = operationRegistry ?? throw new ArgumentNullException(nameof(operationRegistry));
    private readonly ILogger<RedmineClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<RedmineIssue> CreateIssueAsync(RedmineCreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.GlpiTicketId))
            throw new ArgumentException("GlpiTicketId обязателен", nameof(request));

        if (string.IsNullOrWhiteSpace(request.Subject))
            throw new ArgumentException("Subject обязателен", nameof(request));

        return await _operationRegistry.RunAsync(
            "Redmine:CreateIssue",
            async ct =>
            {
                var payload = BuildCreatePayload(request);
                var json = JsonSerializer.Serialize(payload);

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "issues.json") { Content = content };
                using var response = await SendAsync(httpRequest, ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    _logger.LogError("Redmine вернул {StatusCode} при создании задачи. Ответ: {Body}", response.StatusCode, body);
                    throw new InvalidOperationException($"Ошибка Redmine API при создании задачи: {response.StatusCode} — {body}");
                }

                var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var issue = ParseIssue(responseJson, fromSingleIssue: true)
                    ?? throw new InvalidOperationException("Redmine вернул успешный статус, но не удалось разобрать созданную задачу.");

                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("Создана задача Redmine #{IssueId} ({IssueKey}) по заявке GLPI #{TicketId}",
                        issue.Id, issue.Key ?? "?", request.GlpiTicketId);

                return issue;
            },
            context: new { request.GlpiTicketId, request.ProjectId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RedmineIssue?> GetIssueAsync(int issueId, CancellationToken cancellationToken = default)
    {
        return await _operationRegistry.RunAsync(
            "Redmine:GetIssue",
            async ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"issues/{issueId}.json");
                using var response = await SendAsync(request, ct).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return (RedmineIssue?)null;

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    _logger.LogError("Redmine вернул {StatusCode} при чтении задачи #{IssueId}. Ответ: {Body}",
                        response.StatusCode, issueId, body);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return ParseIssue(json, fromSingleIssue: true);
            },
            context: new { issueId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RedmineProject>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<RedmineProject>();
        var offset = 0;

        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"projects.json?limit={DefaultPageSize}&offset={offset}");

            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogError("Redmine вернул {StatusCode} при чтении проектов. Ответ: {Body}", response.StatusCode, body);
                break;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var page = ParseProjects(json);
            result.AddRange(page);

            if (page.Count < DefaultPageSize)
                break;

            offset += DefaultPageSize;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RedmineTracker>> GetTrackersAsync(int projectId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"projects/{projectId}.json?include=trackers");

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("Redmine вернул {StatusCode} при чтении трекеров проекта {ProjectId}. Ответ: {Body}",
                response.StatusCode, projectId, body);
            return [];
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseTrackers(json);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RedmineCustomField>> GetCustomFieldsAsync(int projectId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"projects/{projectId}.json?include=issue_custom_fields");

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("Redmine вернул {StatusCode} при чтении custom fields проекта {ProjectId}. Ответ: {Body}",
                response.StatusCode, projectId, body);
            return [];
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseCustomFields(json);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RedmineMembership>> GetProjectMembershipsAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var result = new List<RedmineMembership>();
        var offset = 0;

        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"projects/{projectId}/memberships.json?limit={DefaultPageSize}&offset={offset}");

            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogError("Redmine вернул {StatusCode} при чтении участников проекта {ProjectId}. Ответ: {Body}",
                    response.StatusCode, projectId, body);
                break;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var page = ParseMemberships(json);
            result.AddRange(page);

            if (page.Count < DefaultPageSize)
                break;

            offset += DefaultPageSize;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Проект {ProjectId}: получено {Count} участников", projectId, result.Count);

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RedmineIssue>> GetClosedIssuesAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        return await _operationRegistry.RunAsync(
            "Redmine:GetClosedIssues",
            async ct =>
            {
                var statusIds = string.Join(",", _options.PollingClosedStatusIds);
                var updatedOnDate = sinceUtc.AddDays(-1).ToString("yyyy-MM-dd");
                var result = new List<RedmineIssue>();
                var offset = 0;

                while (true)
                {
                    var url = $"issues.json?status_id={statusIds}" +
                              $"&updated_on=>={Uri.EscapeDataString(updatedOnDate)}" +
                              $"&limit={DefaultPageSize}&offset={offset}&sort=updated_on:desc";

                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    using var response = await SendAsync(request, ct).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        _logger.LogError("Redmine вернул {StatusCode} при polling. Ответ: {Body}", response.StatusCode, body);
                        break;
                    }

                    var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var page = ParseIssuesList(json);

                    foreach (var issue in page)
                    {
                        if (issue.UpdatedOn >= sinceUtc)
                            result.Add(issue);
                    }

                    if (page.Count < DefaultPageSize)
                        break;

                    var oldestInBatch = DateTime.MaxValue;
                    foreach (var issue in page)
                    {
                        if (issue.UpdatedOn < oldestInBatch)
                            oldestInBatch = issue.UpdatedOn;
                    }

                    if (oldestInBatch < sinceUtc)
                        break;

                    offset += DefaultPageSize;
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Polling: получено {Count} закрытых задач с {Since} (после фильтрации)", result.Count, sinceUtc);

                return (IReadOnlyList<RedmineIssue>)result;
            },
            context: new { sinceUtc },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RedmineIssue?> FindIssueByGlpiTicketIdAsync(
        string glpiTicketId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(glpiTicketId);

        return await _operationRegistry.RunAsync(
            "Redmine:FindIssue",
            async ct =>
            {
                var cfId = _options.CustomFieldHdNumber;
                var url = $"issues.json?cf_{cfId}={Uri.EscapeDataString(glpiTicketId)}" +
                          $"&status_id=*&limit=1&sort=id:desc";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await SendAsync(request, ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    _logger.LogWarning(
                        "Redmine вернул {StatusCode} при поиске задачи по GLPI #{TicketId}. Ответ: {Body}",
                        response.StatusCode, glpiTicketId, body);
                    return (RedmineIssue?)null;
                }

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var issues = ParseIssuesList(json);

                var found = issues.Count > 0 ? issues[0] : null;

                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug(
                        "FindIssueByGlpiTicketId GLPI #{TicketId}: найдено {Count} задач (первая: #{IssueId})",
                        glpiTicketId, issues.Count, found?.Id);

                return found;
            },
            context: new { glpiTicketId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private object BuildCreatePayload(RedmineCreateRequest request)
    {
        var issue = new Dictionary<string, object>
        {
            ["project_id"] = request.ProjectId,
            ["tracker_id"] = request.TrackerId,
            ["status_id"] = _options.DefaultStatusId,
            ["priority_id"] = _options.DefaultPriorityId,
            ["subject"] = request.Subject,
            ["description"] = request.Description ?? string.Empty
        };

        if (_options.AllowedProjectIds.Contains(request.ProjectId))
        {
            issue["custom_field_values"] = new Dictionary<string, string>
            {
                [_options.CustomFieldHdNumber.ToString()] = request.GlpiTicketId,
                [_options.CustomFieldTechDebt.ToString()] = request.CustomFields.TryGetValue(_options.CustomFieldTechDebt, out var techDebt)
                    ? techDebt
                    : "0",
                [_options.CustomFieldDevEnvironment.ToString()] = request.CustomFields.TryGetValue(_options.CustomFieldDevEnvironment, out var devEnv)
                    ? devEnv
                    : _options.DefaultDevelopmentEnvironment
            };
        }

        if (request.AssignedToId.HasValue && request.AssignedToId.Value > 0)
            issue["assigned_to_id"] = request.AssignedToId.Value;

        return new { issue };
    }

    private RedmineIssue? ParseIssue(string json, bool fromSingleIssue)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var issueElement = fromSingleIssue && root.TryGetProperty("issue", out var wrapped)
                ? wrapped
                : root;

            return ParseIssueElement(issueElement);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Не удалось разобрать JSON задачи Redmine");
            return null;
        }
    }

    private List<RedmineIssue> ParseIssuesList(string json)
    {
        var result = new List<RedmineIssue>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("issues", out var issuesArray)
                || issuesArray.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var element in issuesArray.EnumerateArray())
            {
                var issue = ParseIssueElement(element);
                if (issue is not null)
                    result.Add(issue);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Не удалось разобрать JSON списка задач Redmine");
        }

        return result;
    }

    private RedmineIssue? ParseIssueElement(JsonElement element)
    {
        if (!element.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var id))
            return null;

        var subject = element.TryGetProperty("subject", out var s) ? s.GetString() ?? string.Empty : string.Empty;

        var projectId = 0;
        if (element.TryGetProperty("project", out var projectEl)
            && projectEl.TryGetProperty("id", out var projIdEl)
            && projIdEl.TryGetInt32(out var pid))
        {
            projectId = pid;
        }

        var statusId = 0;
        var statusName = string.Empty;
        var isClosed = false;
        if (element.TryGetProperty("status", out var statusEl))
        {
            if (statusEl.TryGetProperty("id", out var sidEl) && sidEl.TryGetInt32(out var sid))
                statusId = sid;

            if (statusEl.TryGetProperty("name", out var snameEl))
                statusName = snameEl.GetString() ?? string.Empty;

            if (statusEl.TryGetProperty("is_closed", out var isClosedEl) && isClosedEl.ValueKind == JsonValueKind.True)
                isClosed = true;
        }

        var updatedOn = DateTime.MinValue;
        if (element.TryGetProperty("updated_on", out var updEl) && updEl.ValueKind == JsonValueKind.String)
        {
            var updStr = updEl.GetString();
            if (!string.IsNullOrEmpty(updStr) && DateTime.TryParse(updStr, out var parsed))
                updatedOn = parsed.ToUniversalTime();
        }

        var customFields = ParseCustomFieldValues(element);
        var glpiTicketId = customFields.TryGetValue(_options.CustomFieldHdNumber, out var hdNumber)
            ? hdNumber
            : null;

        return new RedmineIssue
        {
            Id = id,
            Subject = subject,
            ProjectId = projectId,
            StatusId = statusId,
            StatusName = statusName,
            IsClosed = isClosed,
            UpdatedOn = updatedOn,
            CustomFieldValues = customFields,
            GlpiTicketId = glpiTicketId
        };
    }

    private static IReadOnlyDictionary<int, string> ParseCustomFieldValues(JsonElement issueElement)
    {
        var dict = new Dictionary<int, string>();

        if (!issueElement.TryGetProperty("custom_fields", out var cfArray)
            || cfArray.ValueKind != JsonValueKind.Array)
            return dict;

        foreach (var cf in cfArray.EnumerateArray())
        {
            if (!cf.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var cfId))
                continue;

            if (!cf.TryGetProperty("value", out var valueEl))
                continue;

            var value = valueEl.ValueKind switch
            {
                JsonValueKind.String => valueEl.GetString() ?? string.Empty,
                JsonValueKind.Number => valueEl.GetRawText(),
                JsonValueKind.True => "1",
                JsonValueKind.False => "0",
                JsonValueKind.Array => string.Join(",", valueEl.EnumerateArray().Select(x => x.ToString())),
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(value))
                dict[cfId] = value;
        }

        return dict;
    }

    private List<RedmineMembership> ParseMemberships(string json)
    {
        var result = new List<RedmineMembership>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("memberships", out var array)
                || array.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var el in array.EnumerateArray())
            {
                if (!el.TryGetProperty("user", out var userEl))
                    continue;

                if (!userEl.TryGetProperty("id", out var uidEl) || !uidEl.TryGetInt32(out var userId))
                    continue;

                var userName = userEl.TryGetProperty("name", out var nameEl)
                    ? nameEl.GetString() ?? string.Empty
                    : string.Empty;

                var roles = new List<string>();
                if (el.TryGetProperty("roles", out var rolesEl) && rolesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var roleEl in rolesEl.EnumerateArray())
                    {
                        if (roleEl.TryGetProperty("name", out var roleNameEl))
                        {
                            var roleName = roleNameEl.GetString();
                            if (!string.IsNullOrEmpty(roleName))
                                roles.Add(roleName);
                        }
                    }
                }

                result.Add(new RedmineMembership
                {
                    UserId = userId,
                    UserName = userName,
                    Roles = roles
                });
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Не удалось разобрать JSON участников проекта");
        }

        return result;
    }

    private List<RedmineProject> ParseProjects(string json)
    {
        var result = new List<RedmineProject>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("projects", out var array)
                || array.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var el in array.EnumerateArray())
            {
                if (!el.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id))
                    continue;

                var name = el.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                var identifier = el.TryGetProperty("identifier", out var idfEl) ? idfEl.GetString() ?? string.Empty : string.Empty;

                int? parentId = null;
                if (el.TryGetProperty("parent", out var parentEl)
                    && parentEl.TryGetProperty("id", out var pidEl)
                    && pidEl.TryGetInt32(out var pid))
                {
                    parentId = pid;
                }

                result.Add(new RedmineProject
                {
                    Id = id,
                    Name = name,
                    Identifier = identifier,
                    ParentId = parentId
                });
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Не удалось разобрать JSON проектов Redmine");
        }

        return result;
    }

    private List<RedmineTracker> ParseTrackers(string json)
    {
        var result = new List<RedmineTracker>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("project", out var projectEl))
                return result;

            if (!projectEl.TryGetProperty("trackers", out var trackersEl)
                || trackersEl.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var el in trackersEl.EnumerateArray())
            {
                if (!el.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id))
                    continue;

                var name = el.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;

                result.Add(new RedmineTracker { Id = id, Name = name });
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Не удалось разобрать JSON трекеров проекта");
        }

        return result;
    }

    private List<RedmineCustomField> ParseCustomFields(string json)
    {
        var result = new List<RedmineCustomField>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("project", out var projectEl))
                return result;

            if (!projectEl.TryGetProperty("issue_custom_fields", out var cfEl)
                || cfEl.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var el in cfEl.EnumerateArray())
            {
                if (!el.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id))
                    continue;

                var name = el.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                var fieldFormat = el.TryGetProperty("field_format", out var ffEl) ? ffEl.GetString() ?? string.Empty : string.Empty;

                var isRequired = el.TryGetProperty("is_required", out var reqEl)
                    && reqEl.ValueKind == JsonValueKind.True;

                var possibleValues = new List<string>();
                if (el.TryGetProperty("possible_values", out var pvEl) && pvEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pv in pvEl.EnumerateArray())
                    {
                        if (pv.TryGetProperty("value", out var valueEl))
                        {
                            var value = valueEl.GetString();
                            if (!string.IsNullOrEmpty(value))
                                possibleValues.Add(value);
                        }
                    }
                }

                result.Add(new RedmineCustomField
                {
                    Id = id,
                    Name = name,
                    FieldFormat = fieldFormat,
                    IsRequired = isRequired,
                    PossibleValues = possibleValues
                });
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Не удалось разобрать JSON custom fields проекта");
        }

        return result;
    }
}