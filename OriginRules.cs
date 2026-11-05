namespace Skuload.PrintBridge;

/// <summary>Validation for the extra origins a user can add in settings.json.</summary>
internal static class OriginRules
{
    /// <summary>
    /// Accepts only a bare https origin (<c>https://host</c> or <c>https://host:port</c>)
    /// and returns it normalised. Anything else (http, a path, a query, a user name,
    /// a wildcard) is refused.
    /// </summary>
    public static bool TryNormalize(string? value, out string origin)
    {
        origin = string.Empty;
        var text = value?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(text) || text.Contains('*')
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var bare = uri.Scheme == Uri.UriSchemeHttps
                   && !string.IsNullOrEmpty(uri.Host)
                   && string.IsNullOrEmpty(uri.UserInfo)
                   && uri.AbsolutePath == "/"
                   && string.IsNullOrEmpty(uri.Query)
                   && string.IsNullOrEmpty(uri.Fragment);
        if (!bare)
        {
            return false;
        }

        origin = uri.IsDefaultPort
            ? $"https://{uri.Host}"
            : $"https://{uri.Host}:{uri.Port}";
        return true;
    }
}
