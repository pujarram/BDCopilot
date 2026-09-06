using BDCopilot.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BDCopilot.Api.Filters;

/// <summary>
/// Protects admin APIs with X-Bd-Admin-Token or Entra role BdCopilot.Admin.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdminAuthorizeAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated == true
            && context.HttpContext.User.IsInRole("BdCopilot.Admin"))
        {
            return;
        }

        var token = context.HttpContext.Request.Headers["X-Bd-Admin-Token"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(token)
            && AuthController.TryParse(token, out _, out _, out _, out var exp)
            && exp >= DateTimeOffset.UtcNow)
        {
            return;
        }

        context.Result = new UnauthorizedObjectResult(new { title = "Admin authentication required." });
    }
}
