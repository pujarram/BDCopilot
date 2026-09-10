using BDCopilot.Core.Models;

namespace BDCopilot.Infrastructure.Teams;

/// <summary>Resolves a clickable open URL for a grounded citation (SharePoint or API redirect).</summary>
public static class CitationOpenUrl
{
    public static string? Resolve(Citation citation, string apiBaseUrl, string userObjectId)
    {
        if (IsHttpUrl(citation.SharePointUrl))
        {
            return citation.SharePointUrl;
        }

        if (citation.DocumentId == Guid.Empty || string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return null;
        }

        var baseUrl = apiBaseUrl.TrimEnd('/');
        return $"{baseUrl}/api/documents/{citation.DocumentId}/open?userObjectId={Uri.EscapeDataString(userObjectId)}";
    }

    public static string FormatMarkdownLink(Citation citation, string apiBaseUrl, string userObjectId)
    {
        var label = FormatLabel(citation);
        var url = Resolve(citation, apiBaseUrl, userObjectId);
        return url is null ? $"• {label}" : $"• [{label}]({url})";
    }

    public static string FormatLabel(Citation citation)
    {
        var locator = string.IsNullOrWhiteSpace(citation.Locator) ? "" : $" · {citation.Locator}";
        return $"{citation.FileName}{locator}";
    }

    private static bool IsHttpUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
