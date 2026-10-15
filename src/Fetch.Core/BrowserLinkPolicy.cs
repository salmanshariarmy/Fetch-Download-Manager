namespace Fetch.Core;

/// <summary>
/// Browser-supplied referrer URLs are never copied into headers verbatim.
/// Only a same-origin scheme/host/port is kept, without path, query or fragment.
/// No cookies, Authorization, or browser credentials are imported.
/// </summary>
public static class BrowserLinkPolicy
{
    public static string? SafeReferrerOrigin(string downloadUrl, string? sourcePage)
    {
        if (string.IsNullOrWhiteSpace(sourcePage) || sourcePage.Length > 2048 ||
            !Uri.TryCreate(downloadUrl, UriKind.Absolute, out var target) ||
            !Uri.TryCreate(sourcePage, UriKind.Absolute, out var page)) return null;
        if (target.Scheme is not ("http" or "https") ||
            page.Scheme is not ("http" or "https") ||
            target.UserInfo.Length > 0 || page.UserInfo.Length > 0 ||
            !string.Equals(target.Scheme, page.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(target.IdnHost, page.IdnHost, StringComparison.OrdinalIgnoreCase) ||
            target.Port != page.Port) return null;
        return page.GetLeftPart(UriPartial.Authority) + "/";
    }
}
