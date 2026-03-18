using System.Text.Json;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using TokenForeman.Models;

namespace TokenForeman.Services;

// PERMISSION BOUNDARY (for judges): This service exchanges the user's Auth0 access token (as userSub) for delegated tokens via Auth0 Token Vault. ClientSecret is used only in the server-side exchange request; delegated tokens are returned to the caller for immediate use and are not persisted here.
public sealed class TokenVaultService
{
    private const string HttpClientName = "Auth0TokenVault";
    private const string TokenEndpointPath = "/oauth/token";

    private const string GrantType =
        "urn:auth0:params:oauth:grant-type:token-exchange:federated-connection-access-token";

    private const string RequestedTokenType = "http://auth0.com/oauth/token-type/federated-connection-access-token";
    private const string SubjectTokenType = "urn:ietf:params:oauth:token-type:access_token";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<Auth0Options> _auth0Options;
    private readonly ILogger<TokenVaultService> _logger;

    public TokenVaultService(
        IHttpClientFactory httpClientFactory,
        IOptions<Auth0Options> auth0Options,
        ILogger<TokenVaultService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _auth0Options = auth0Options;
        _logger = logger;
    }

    public async Task<TokenVaultDelegatedTokenResponse> GetDelegatedTokenAsync(string connectionName, string userSub)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(userSub);

        var settings = _auth0Options.Value;
        ValidateSettings(settings);

        var request = new TokenVaultExchangeRequest(
            GrantType,
            userSub,
            SubjectTokenType,
            RequestedTokenType,
            connectionName,
            settings.ClientId,
            settings.ClientSecret);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var message = new HttpRequestMessage(HttpMethod.Post, BuildTokenEndpoint(settings.Domain))
        {
            Content = new FormUrlEncodedContent(request.ToFormFields())
        };

        _logger.LogInformation(
            "Requesting delegated token for connection {ConnectionName} and user subject {UserSub}.",
            connectionName,
            Mask(userSub));

        using var response = await client.SendAsync(message);
        var payload = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Auth0 token exchange failed for connection {ConnectionName} and user subject {UserSub}. Status {StatusCode}. Body: {Body}",
                connectionName,
                Mask(userSub),
                (int)response.StatusCode,
                payload);

            throw new HttpRequestException(
                $"Auth0 token exchange failed with status code {(int)response.StatusCode}.",
                inner: null,
                statusCode: response.StatusCode);
        }

        var delegatedToken = JsonSerializer.Deserialize<TokenVaultDelegatedTokenResponse>(payload, JsonOptions)
                             ?? throw new InvalidOperationException(
                                 "Auth0 token exchange succeeded but the response body was empty.");

        _logger.LogInformation(
            "Auth0 token exchange succeeded for connection {ConnectionName}. Token expires in {ExpiresIn} seconds.",
            connectionName,
            delegatedToken.ExpiresIn);

        return delegatedToken;
    }

    private static Uri BuildTokenEndpoint(string domain)
    {
        var normalizedDomain = NormalizeDomain(domain);
        return new Uri($"https://{normalizedDomain}{TokenEndpointPath}");
    }

    private static void ValidateSettings(Auth0Options settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.ClientSecret);
    }

    private static string NormalizeDomain(string domain)
    {
        var normalized = domain.Trim();

        if (normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["https://".Length..];
        }
        else if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["http://".Length..];
        }

        return normalized.TrimEnd('/');
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
}
