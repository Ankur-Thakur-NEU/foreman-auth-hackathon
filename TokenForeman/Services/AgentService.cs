using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.SemanticKernel;
using JetBrains.Annotations;

namespace TokenForeman.Services;

public sealed class AgentService
{
    private const string PluginName = "ForemanTools";
    private const string GoogleCalendarConnectionName = "google-oauth2";
    private const string ProcoreConnectionName = "procore";
    private const string ForemanHttpClientName = "ForemanIntegrations";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TokenVaultService _tokenVaultService;

    public AgentService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AgentService> logger,
        ILoggerFactory loggerFactory,
        TokenVaultService tokenVaultService)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _tokenVaultService = tokenVaultService;
    }

    public async Task<ForemanExecutionResult> ExecuteForemanActionAsync(string userQuery, string auth0AccessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userQuery);
        ArgumentException.ThrowIfNullOrWhiteSpace(auth0AccessToken);

        var userSub = ExtractSubject(auth0AccessToken);
        var plannedActions = BuildPlan(userQuery, userSub, auth0AccessToken);

        if (plannedActions.Count == 0)
        {
            _logger.LogInformation("No Foreman tools matched the request for user {UserSub}.", Mask(userSub));
            return new ForemanExecutionResult(userQuery, userSub, Array.Empty<ForemanActionResult>());
        }

        var kernel = BuildKernel();
        var executedActions = new List<ForemanActionResult>(plannedActions.Count);

        foreach (var plannedAction in plannedActions)
        {
            _logger.LogInformation(
                "Executing Foreman tool {ToolName} for user {UserSub}.",
                plannedAction.ToolName,
                Mask(userSub));

            var kernelArguments = new KernelArguments();
            foreach (var item in plannedAction.Arguments)
            {
                if (item.Value is not null)
                {
                    kernelArguments[item.Key] = item.Value;
                }
            }

            var result = await kernel.InvokeAsync(PluginName, plannedAction.ToolName, kernelArguments);
            var output = result.GetValue<string>() ?? result.ToString();

            executedActions.Add(new ForemanActionResult(
                plannedAction.ToolName,
                plannedAction.Intent,
                output));
        }

        return new ForemanExecutionResult(userQuery, userSub, executedActions);
    }

    private Kernel BuildKernel()
    {
        var builder = Kernel.CreateBuilder();
        builder.Plugins.AddFromObject(
            new ForemanKernelTools(
                _httpClientFactory,
                _configuration,
                _loggerFactory.CreateLogger("TokenForeman.ForemanKernelTools"),
                _tokenVaultService),
            PluginName);

        return builder.Build();
    }

    private static string ExtractSubject(string auth0AccessToken)
    {
        var handler = new JwtSecurityTokenHandler();

        if (!handler.CanReadToken(auth0AccessToken))
        {
            throw new InvalidOperationException("The Auth0 access token could not be read as a JWT.");
        }

        var token = handler.ReadJwtToken(auth0AccessToken);
        return token.Claims.FirstOrDefault(claim => claim.Type == "sub")?.Value
               ?? throw new InvalidOperationException("The Auth0 access token does not contain a sub claim.");
    }

    private List<PlannedAction> BuildPlan(string userQuery, string userSub, string auth0AccessToken)
    {
        var normalizedQuery = userQuery.Trim();
        var candidates = new List<(int Order, PlannedAction Action)>();

        if (TryFindKeywordIndex(normalizedQuery, CalendarKeywords, out var calendarIndex))
        {
            candidates.Add((calendarIndex, CreateCalendarAction(normalizedQuery, userSub, auth0AccessToken)));
        }

        if (TryFindKeywordIndex(normalizedQuery, SlackKeywords, out var slackIndex))
        {
            candidates.Add((slackIndex, CreateSlackAction(normalizedQuery)));
        }

        if (TryFindKeywordIndex(normalizedQuery, ProcoreKeywords, out var procoreIndex))
        {
            candidates.Add((procoreIndex, CreateProcoreAction(normalizedQuery, userSub, auth0AccessToken)));
        }

        return candidates
            .OrderBy(candidate => candidate.Order)
            .Select(candidate => candidate.Action)
            .ToList();
    }

    private PlannedAction CreateCalendarAction(string userQuery, string userSub, string auth0AccessToken)
    {
        var summary = Truncate(userQuery, 120);
        var startTime = DateTimeOffset.UtcNow.AddHours(1);
        var endTime = startTime.AddHours(1);

        return new PlannedAction(
            "CreateOrUpdateCalendarEventTool",
            "Create or update a Google Calendar event",
            new Dictionary<string, object?>
            {
                ["userSub"] = userSub,
                ["auth0AccessToken"] = auth0AccessToken,
                ["summary"] = summary,
                ["description"] = userQuery,
                ["startTime"] = startTime,
                ["endTime"] = endTime,
                ["calendarId"] = _configuration["GoogleCalendar:CalendarId"] ?? "primary",
                ["timeZone"] = _configuration["GoogleCalendar:TimeZone"] ?? "UTC"
            });
    }

    private PlannedAction CreateSlackAction(string userQuery)
    {
        return new PlannedAction(
            "PostToSlackTool",
            "Post a message to Slack",
            new Dictionary<string, object?>
            {
                ["channel"] = _configuration["Slack:DefaultChannel"] ?? "#general",
                ["message"] = userQuery
            });
    }

    private PlannedAction CreateProcoreAction(string userQuery, string userSub, string auth0AccessToken)
    {
        var itemType = userQuery.Contains("rfi", StringComparison.OrdinalIgnoreCase) ? "rfi" : "task";

        return new PlannedAction(
            "CreateProcoreTaskOrRFITool",
            itemType == "rfi" ? "Create a Procore RFI" : "Create a Procore task",
            new Dictionary<string, object?>
            {
                ["userSub"] = userSub,
                ["auth0AccessToken"] = auth0AccessToken,
                ["companyId"] = _configuration["Procore:CompanyId"],
                ["projectId"] = _configuration["Procore:ProjectId"],
                ["title"] = Truncate(userQuery, 120),
                ["description"] = userQuery,
                ["itemType"] = itemType
            });
    }

    private static bool TryFindKeywordIndex(string text, string[] keywords, out int index)
    {
        index = int.MaxValue;
        var found = false;

        foreach (var keyword in keywords)
        {
            var candidateIndex = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (candidateIndex >= 0 && candidateIndex < index)
            {
                index = candidateIndex;
                found = true;
            }
        }

        return found;
    }

    private static string Truncate(string value, int length)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= length)
        {
            return value;
        }

        return value[..length];
    }

    private static string Mask(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        if (value.Length <= 8)
        {
            return "***";
        }

        return $"{value[..4]}***{value[^4..]}";
    }

    private static readonly string[] CalendarKeywords = ["calendar", "meeting", "event", "schedule", "reschedule"];
    private static readonly string[] SlackKeywords = ["slack", "message", "channel", "notify", "post"];
    private static readonly string[] ProcoreKeywords = ["procore", "rfi", "task", "submittal", "issue"];

    [SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Invoked via Semantic Kernel reflection.")]
    private sealed class ForemanKernelTools
    {
        private const string GoogleCalendarBaseUrl = "https://www.googleapis.com/calendar/v3";
        private const string SlackPostMessageUrl = "https://slack.com/api/chat.postMessage";
        private const string ProcoreBaseUrl = "https://api.procore.com/rest/v1.0";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger _logger;
        private readonly TokenVaultService _tokenVaultService;

        public ForemanKernelTools(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger logger,
            TokenVaultService tokenVaultService)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _tokenVaultService = tokenVaultService;
        }

        [UsedImplicitly]
        [SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Invoked via Semantic Kernel reflection.")]
        [KernelFunction(nameof(CreateOrUpdateCalendarEventTool))]
        [Description("Creates or updates a Google Calendar v3 event using a delegated token from Auth0 Token Vault.")]
        public async Task<string> CreateOrUpdateCalendarEventTool(
            [Description("The Auth0 subject identifier for the current user.")]
            string userSub,
            [Description("Calendar event summary or title.")]
            string summary,
            [Description("The event start time.")] DateTimeOffset startTime,
            [Description("The event end time.")] DateTimeOffset endTime,
            [Description("Optional calendar id, defaults to primary.")]
            string? calendarId = null,
            [Description("Optional existing event id to update.")]
            string? eventId = null,
            [Description("Optional description for the calendar event.")]
            string? description = null,
            [Description("Optional IANA time zone name.")]
            string? timeZone = null,
            [Description("Auth0 access token for Token Vault exchange (server-only, not logged). Required; do not pass sub claim.")]
            string? auth0AccessToken = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(summary);
            if (string.IsNullOrWhiteSpace(auth0AccessToken))
                throw new InvalidOperationException("Auth0 access token is required for Token Vault exchange. The subject_token must be the Bearer token, not the user sub claim.");
            var delegatedToken = await _tokenVaultService.GetDelegatedTokenAsync(GoogleCalendarConnectionName, auth0AccessToken);
            var client = _httpClientFactory.CreateClient(ForemanHttpClientName);
            var resolvedCalendarId = string.IsNullOrWhiteSpace(calendarId) ? "primary" : calendarId.Trim();
            var resolvedTimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone.Trim();
            var requestUri = string.IsNullOrWhiteSpace(eventId)
                ? $"{GoogleCalendarBaseUrl}/calendars/{Uri.EscapeDataString(resolvedCalendarId)}/events"
                : $"{GoogleCalendarBaseUrl}/calendars/{Uri.EscapeDataString(resolvedCalendarId)}/events/{Uri.EscapeDataString(eventId.Trim())}";

            var payload = new
            {
                summary,
                description,
                start = new
                {
                    dateTime = startTime,
                    timeZone = resolvedTimeZone
                },
                end = new
                {
                    dateTime = endTime,
                    timeZone = resolvedTimeZone
                }
            };

            using var request = new HttpRequestMessage(
                string.IsNullOrWhiteSpace(eventId) ? HttpMethod.Post : HttpMethod.Patch,
                requestUri);
            request.Content = JsonContent.Create(payload, options: JsonOptions);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", delegatedToken.AccessToken);

            _logger.LogInformation(
                "Calling Google Calendar for {CalendarId} with tool {ToolName}.",
                resolvedCalendarId,
                nameof(CreateOrUpdateCalendarEventTool));

            using var response = await client.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Google Calendar request failed with status code {(int)response.StatusCode}: {TruncateResponse(responseBody, 500)}",
                    inner: null,
                    statusCode: response.StatusCode);
            }

            var parsed = TryParseJson(responseBody, out var parsedJson);
            var remoteId = parsed && parsedJson.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var calendarLink = parsed && parsedJson.TryGetProperty("htmlLink", out var htmlLinkElement)
                ? htmlLinkElement.GetString()
                : null;

            return JsonSerializer.Serialize(new
            {
                tool = nameof(CreateOrUpdateCalendarEventTool),
                action = string.IsNullOrWhiteSpace(eventId) ? "created" : "updated",
                calendarId = resolvedCalendarId,
                eventId = remoteId ?? eventId,
                calendarLink,
                status = response.StatusCode.ToString(),
                response = parsed ? (object)parsedJson : responseBody
            }, JsonOptions);
        }

        [UsedImplicitly]
        [SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Invoked via Semantic Kernel reflection.")]
        [KernelFunction(nameof(PostToSlackTool))]
        [Description("Posts a message to Slack using the Slack Web API.")]
        public async Task<string> PostToSlackTool(
            [Description("The Slack channel name or ID.")]
            string channel,
            [Description("The message text to post.")]
            string message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(channel);
            ArgumentException.ThrowIfNullOrWhiteSpace(message);

            var slackBotToken = _configuration["Slack:BotToken"];
            if (string.IsNullOrWhiteSpace(slackBotToken))
            {
                throw new InvalidOperationException("Slack:BotToken is required to post to Slack.");
            }

            var client = _httpClientFactory.CreateClient(ForemanHttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, SlackPostMessageUrl);
            request.Content = JsonContent.Create(new
            {
                channel,
                text = message
            }, options: JsonOptions);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", slackBotToken);

            _logger.LogInformation("Posting a message to Slack channel {Channel}.", channel);

            using var response = await client.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Slack request failed with status code {(int)response.StatusCode}: {TruncateResponse(responseBody, 500)}",
                    inner: null,
                    statusCode: response.StatusCode);
            }

            var parsed = TryParseJson(responseBody, out var parsedJson);
            if (parsed && parsedJson.TryGetProperty("ok", out var okElement) &&
                okElement.ValueKind == JsonValueKind.False)
            {
                var error = parsedJson.TryGetProperty("error", out var errorElement)
                    ? errorElement.GetString()
                    : "unknown_error";
                throw new InvalidOperationException($"Slack returned an error response: {error}");
            }

            var ts = parsed && parsedJson.TryGetProperty("ts", out var tsElement) ? tsElement.GetString() : null;

            return JsonSerializer.Serialize(new
            {
                tool = nameof(PostToSlackTool),
                channel,
                status = "posted",
                ts,
                response = parsed ? (object)parsedJson : responseBody
            }, JsonOptions);
        }

        [UsedImplicitly]
        [SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Invoked via Semantic Kernel reflection.")]
        [KernelFunction("CreateProcoreTaskOrRFITool")]
        [Description("Creates a Procore task or RFI using a delegated token from Auth0 Token Vault.")]
        public async Task<string> CreateProcoreTaskOrRfiTool(
            [Description("The Auth0 subject identifier for the current user.")]
            string userSub,
            [Description("The Procore project identifier.")]
            string projectId,
            [Description("The title of the task or RFI.")]
            string title,
            [Description("Either task or rfi.")] string itemType = "task",
            [Description("Optional Procore company identifier.")]
            string? companyId = null,
            [Description("Optional description for the Procore item.")]
            string? description = null,
            [Description("Auth0 access token for Token Vault exchange (server-only, not logged). Required; do not pass sub claim.")]
            string? auth0AccessToken = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
            ArgumentException.ThrowIfNullOrWhiteSpace(title);
            if (string.IsNullOrWhiteSpace(auth0AccessToken))
                throw new InvalidOperationException("Auth0 access token is required for Token Vault exchange. The subject_token must be the Bearer token, not the user sub claim.");
            var delegatedToken = await _tokenVaultService.GetDelegatedTokenAsync(ProcoreConnectionName, auth0AccessToken);
            var client = _httpClientFactory.CreateClient(ForemanHttpClientName);
            var resolvedType = itemType.Equals("rfi", StringComparison.OrdinalIgnoreCase) ? "rfi" : "task";
            var requestUri = BuildProcoreUri(projectId, companyId, resolvedType);
            var payload = new
            {
                title,
                description
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Content = JsonContent.Create(payload, options: JsonOptions);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", delegatedToken.AccessToken);

            _logger.LogInformation(
                "Calling Procore API for project {ProjectId} using tool {ToolName}.",
                projectId,
                nameof(CreateProcoreTaskOrRfiTool));

            using var response = await client.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Procore request failed with status code {(int)response.StatusCode}: {TruncateResponse(responseBody, 500)}",
                    inner: null,
                    statusCode: response.StatusCode);
            }

            var parsed = TryParseJson(responseBody, out var parsedJson);
            var remoteId = parsed && parsedJson.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;

            return JsonSerializer.Serialize(new
            {
                tool = nameof(CreateProcoreTaskOrRfiTool),
                itemType = resolvedType,
                projectId,
                companyId,
                procoreId = remoteId,
                status = response.StatusCode.ToString(),
                response = parsed ? (object)parsedJson : responseBody
            }, JsonOptions);
        }

        private static Uri BuildProcoreUri(string projectId, string? companyId, string itemType)
        {
            var path = itemType.Equals("rfi", StringComparison.OrdinalIgnoreCase)
                ? $"/projects/{Uri.EscapeDataString(projectId.Trim())}/rfis"
                : $"/projects/{Uri.EscapeDataString(projectId.Trim())}/tasks";

            if (!string.IsNullOrWhiteSpace(companyId))
            {
                path += $"?company_id={Uri.EscapeDataString(companyId.Trim())}";
            }

            return new Uri($"{ProcoreBaseUrl}{path}");
        }

        private static bool TryParseJson(string responseBody, out JsonElement root)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                root = default;
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(responseBody);
                root = document.RootElement.Clone();
                return true;
            }
            catch (JsonException)
            {
                root = default;
                return false;
            }
        }

        private static string TruncateResponse(string value, int length)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= length)
            {
                return value;
            }

            return value[..length];
        }
    }

    private sealed record PlannedAction(
        string ToolName,
        string Intent,
        IReadOnlyDictionary<string, object?> Arguments);
}

public sealed record ForemanExecutionResult(
    string UserQuery,
    string UserSub,
    IReadOnlyList<ForemanActionResult> ExecutedActions);

public sealed record ForemanActionResult(
    string ToolName,
    string Intent,
    string Output);
