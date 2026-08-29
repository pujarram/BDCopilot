using System.Net;
using Hangfire.Dashboard;

namespace BDCopilot.Api.Filters;

/// <summary>
/// Restricts the Hangfire dashboard to localhost or authenticated BdCopilot.Admin users.
/// </summary>
public class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var remoteIp = httpContext.Connection.RemoteIpAddress;

        if (remoteIp is not null && IPAddress.IsLoopback(remoteIp))
        {
            return true;
        }

        if (remoteIp?.ToString() is "127.0.0.1" or "::1")
        {
            return true;
        }

        return httpContext.User.Identity?.IsAuthenticated == true &&
               httpContext.User.IsInRole("BdCopilot.Admin");
    }
}
