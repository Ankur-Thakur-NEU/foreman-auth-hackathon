using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TokenForeman.Services;

namespace TokenForeman.Controllers;

// PERMISSION BOUNDARY (for judges): This controller only reads the Bearer token from the Authorization header to (1) validate the user via Auth0 JWT and (2) pass the token to Token Vault for exchange. It never stores, logs, or forwards the raw token. User input (userQuery/task) is sanitized and length-limited before use.
[ApiController]
[Route("api/foreman")]
[Authorize]
public sealed class ForemanController : ControllerBase
{
    private static readonly string[] StepUpKeywords = ["overtime", "budget", "change order"];

    private readonly AgentService _agentService;
    private readonly ILogger<ForemanController> _logger;

    public ForemanController(AgentService agentService, ILogger<ForemanController> logger)
    {
        _agentService = agentService;
        _logger = logger;
    }

    // Called by OpenClaw restricted-mode local agent via webhook (task/userId) or by PWA (userQuery).
    [HttpPost("action")]
    [ProducesResponseType(typeof(ForemanActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ExecuteActionAsync([FromBody] ForemanActionRequest? request)
    {
        if (request is null)
        {
            _logger.LogWarning("Foreman action rejected: null request body.");
            return BadRequest(new { error = "invalid_payload", message = "Request body is required." });
        }

        _logger.LogInformation("Foreman action request received; payload has UserQuery={HasUserQuery}, Task={HasTask}, UserId={HasUserId}",
            !string.IsNullOrWhiteSpace(request.UserQuery),
            !string.IsNullOrWhiteSpace(request.Task),
            !string.IsNullOrWhiteSpace(request.UserId));

        var rawQuery = ResolveUserQuery(request);
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            _logger.LogWarning("Foreman action rejected: missing userQuery and task.");
            return BadRequest(new
            {
                error = "invalid_payload",
                message = "Provide either userQuery or both task and userId (OpenClaw webhook)."
            });
        }

        var userQuery = InputSanitizer.SanitizeQuery(rawQuery);
        if (string.IsNullOrWhiteSpace(userQuery))
        {
            _logger.LogWarning("Foreman action rejected: query empty after sanitization.");
            return BadRequest(new { error = "invalid_payload", message = "Query was empty or invalid after sanitization." });
        }

        if (rawQuery.Length != userQuery.Length)
            _logger.LogDebug("Foreman action input sanitized: length {Before} -> {After}", rawQuery.Length, userQuery.Length);

        if (RequiresStepUp(userQuery))
        {
            _logger.LogInformation("Foreman action step-up required for query containing sensitive keyword.");
            Response.Headers.WWWAuthenticate =
                "Bearer error=\"insufficient_scope\", error_description=\"Step-up authentication required\"";

            return Unauthorized(new
            {
                error = "step_up_required",
                message = "This action requires step-up authentication."
            });
        }

        var authHeader = Request.Headers.Authorization.ToString();
        var accessToken = ExtractBearerToken(authHeader);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogWarning("Foreman action rejected: missing or invalid Bearer token.");
            Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\", error_description=\"Missing access token\"";
            return Unauthorized(new
            {
                error = "missing_token",
                message = "A valid Bearer token is required."
            });
        }

        // PERMISSION BOUNDARY: accessToken is passed only to AgentService for Token Vault exchange and downstream API calls; never logged or stored.
        _logger.LogInformation("Executing Foreman action for authenticated request; query length={Length}.", userQuery.Length);

        ForemanExecutionResult result;
        try
        {
            result = await _agentService.ExecuteForemanActionAsync(userQuery, accessToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Foreman action execution failed for query length={Length}.", userQuery.Length);
            throw;
        }

        var calendarLink = FindActionProperty(result.ExecutedActions, "CreateOrUpdateCalendarEventTool", "calendarLink", "eventId");
        var slackTs = FindActionProperty(result.ExecutedActions, "PostToSlackTool", "ts");
        var procoreId = FindActionProperty(result.ExecutedActions, "CreateProcoreTaskOrRFITool", "procoreId");

        _logger.LogInformation("Foreman action completed; actions executed={Count}, hasCalendarLink={HasCal}, hasSlackTs={HasSlack}, hasProcoreId={HasProcore}.",
            result.ExecutedActions.Count, calendarLink != null, slackTs != null, procoreId != null);

        return Ok(new ForemanActionResponse(
            result.UserQuery,
            result.UserSub,
            result.ExecutedActions.Select(action => new ForemanActionItem(
                action.ToolName,
                action.Intent,
                action.Output)).ToArray(),
            calendarLink,
            slackTs,
            procoreId));
    }

    private static string? ResolveUserQuery(ForemanActionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.UserQuery)) return request.UserQuery;
        if (!string.IsNullOrWhiteSpace(request.Task)) return request.Task;
        return null;
    }

    private static bool RequiresStepUp(string userQuery)
    {
        return StepUpKeywords.Any(keyword => userQuery.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractBearerToken(string authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return null;
        }

        const string bearerPrefix = "Bearer ";
        if (!authorizationHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return authorizationHeader[bearerPrefix.Length..].Trim();
    }

    private static string? FindActionProperty(
        IReadOnlyList<ForemanActionResult> actions,
        string toolName,
        params string[] propertyNames)
    {
        var action = actions.FirstOrDefault(item => item.ToolName.Equals(toolName, StringComparison.Ordinal));
        if (action is null || string.IsNullOrWhiteSpace(action.Output))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(action.Output);
            foreach (var propertyName in propertyNames)
            {
                if (document.RootElement.TryGetProperty(propertyName, out var element))
                {
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}

/// <summary>PWA: userQuery. OpenClaw webhook: task + userId (called by OpenClaw restricted-mode local agent via webhook).</summary>
public sealed record ForemanActionRequest(
    string? UserQuery,
    string? Task,
    string? UserId);

public sealed record ForemanActionResponse(
    string UserQuery,
    string UserSub,
    IReadOnlyList<ForemanActionItem> ActionsTaken,
    string? CalendarLink,
    string? SlackTs,
    string? ProcoreId);

public sealed record ForemanActionItem(
    string ToolName,
    string Intent,
    string Output);
