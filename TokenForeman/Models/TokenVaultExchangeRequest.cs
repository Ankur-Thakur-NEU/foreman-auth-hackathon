namespace TokenForeman.Models;

public sealed record TokenVaultExchangeRequest(
    string GrantType,
    string SubjectToken,
    string SubjectTokenType,
    string RequestedTokenType,
    string Connection,
    string ClientId,
    string ClientSecret)
{
    public IEnumerable<KeyValuePair<string, string>> ToFormFields()
    {
        yield return new KeyValuePair<string, string>("grant_type", GrantType);
        yield return new KeyValuePair<string, string>("subject_token", SubjectToken);
        yield return new KeyValuePair<string, string>("subject_token_type", SubjectTokenType);
        yield return new KeyValuePair<string, string>("requested_token_type", RequestedTokenType);
        yield return new KeyValuePair<string, string>("connection", Connection);
        yield return new KeyValuePair<string, string>("client_id", ClientId);
        yield return new KeyValuePair<string, string>("client_secret", ClientSecret);
    }
}
