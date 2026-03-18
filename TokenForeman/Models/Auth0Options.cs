namespace TokenForeman.Models;

public sealed class Auth0Options
{
    public const string SectionName = "Auth0";

    public string Domain { get; init; } = string.Empty;

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;
}
