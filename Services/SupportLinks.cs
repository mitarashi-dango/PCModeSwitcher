namespace PCModeSwitcher.Services;

internal static class SupportLinks
{
    public const string KoFi = "https://ko-fi.com/nioudachi";

    public static bool TryCreateSupportUri(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
            candidate.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            !candidate.IsDefaultPort)
        {
            return false;
        }

        var host = candidate.IdnHost;
        var isTrustedHost = host.Equals("ko-fi.com", StringComparison.OrdinalIgnoreCase) ||
                            host.Equals("www.ko-fi.com", StringComparison.OrdinalIgnoreCase);
        if (!isTrustedHost)
        {
            return false;
        }

        uri = candidate;
        return true;
    }
}
