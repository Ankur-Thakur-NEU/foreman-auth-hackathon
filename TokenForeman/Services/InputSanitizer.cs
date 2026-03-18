namespace TokenForeman.Services;

/// <summary>
/// Sanitizes user-supplied input for API use. Reduces risk of injection and oversized payloads.
/// Permission boundary: sanitized strings are safe for logging (no tokens) and for passing to the agent/tools.
/// </summary>
public static class InputSanitizer
{
    /// <summary>Max allowed length for a single user query or task string (characters).</summary>
    public const int MaxQueryLength = 4096;

    /// <summary>Sanitize a user query or task: trim, limit length, remove control characters.</summary>
    public static string SanitizeQuery(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if (trimmed.Length > MaxQueryLength)
            trimmed = trimmed[..MaxQueryLength];

        return RemoveControlCharacters(trimmed);
    }

    /// <summary>Sanitize an optional userId (e.g. OpenClaw): allow alphanumeric, pipe, dash; limit length.</summary>
    public static string? SanitizeUserId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length > 128)
            trimmed = trimmed[..128];

        return RemoveControlCharacters(trimmed);
    }

    private static string RemoveControlCharacters(string value)
    {
        var span = value.AsSpan();
        var buffer = new char[span.Length];
        var write = 0;
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (c >= ' ' && c != '\u007f' && (c < '\u00a0' || c > '\u009f'))
                buffer[write++] = c;
        }
        return write == span.Length ? value : new string(buffer, 0, write);
    }
}
