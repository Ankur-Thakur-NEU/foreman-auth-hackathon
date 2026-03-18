using System.Text.Json.Serialization;

namespace TokenForeman.Models;

public sealed class TokenVaultDelegatedTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = string.Empty;

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}
