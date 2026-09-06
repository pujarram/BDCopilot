using System.Security.Claims;

namespace BDCopilot.Api;

/// <summary>Resolves the caller's Entra object id from JWT claims or request fallback.</summary>
public static class UserIdentity
{
    public static string ResolveObjectId(ClaimsPrincipal? user, string? fallback = null)
    {
        if (user?.Identity?.IsAuthenticated == true)
        {
            var oid = user.FindFirstValue("oid")
                      ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
                      ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(oid))
            {
                return oid;
            }
        }

        return string.IsNullOrWhiteSpace(fallback) ? "" : fallback.Trim();
    }

    public static bool IsAdmin(ClaimsPrincipal? user) =>
        user?.IsInRole("BdCopilot.Admin") == true;
}
