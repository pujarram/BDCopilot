using System.Text;
using System.Text.Json;
using BDCopilot.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BDCopilot.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly AdminUserSettings _settings;

    public AuthController(IOptions<AdminUserSettings> settings)
    {
        _settings = settings.Value;
    }

    /// <summary>Temporary admin login until full Entra user management is wired.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AdminLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<AdminLoginResponse> Login([FromBody] AdminLoginRequest request)
    {
        var users = _settings.Users.Count > 0
            ? _settings.Users
            : new List<AdminUserAccount>
            {
                new() { Username = "admin", Password = "admin123", DisplayName = "BD Admin", Role = "Admin" }
            };

        var match = users.FirstOrDefault(u =>
            string.Equals(u.Username, request.Username, StringComparison.OrdinalIgnoreCase) &&
            u.Password == request.Password);

        if (match is null)
        {
            return Unauthorized(new { title = "Invalid username or password." });
        }

        var expires = DateTimeOffset.UtcNow.AddHours(12);
        var payload = JsonSerializer.Serialize(new
        {
            sub = match.Username,
            name = match.DisplayName,
            role = match.Role,
            exp = expires.ToUnixTimeSeconds()
        });
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));

        return Ok(new AdminLoginResponse
        {
            Token = token,
            Username = match.Username,
            DisplayName = match.DisplayName,
            Role = match.Role,
            ExpiresAt = expires
        });
    }

    [HttpGet("me")]
    public ActionResult<object> Me([FromHeader(Name = "X-Bd-Admin-Token")] string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !TryParse(token, out var username, out var displayName, out var role, out var exp))
        {
            return Unauthorized();
        }

        if (exp < DateTimeOffset.UtcNow)
        {
            return Unauthorized(new { title = "Session expired." });
        }

        return Ok(new { username, displayName, role, expiresAt = exp });
    }

    internal static bool TryParse(
        string token,
        out string username,
        out string displayName,
        out string role,
        out DateTimeOffset expiresAt)
    {
        username = "";
        displayName = "";
        role = "";
        expiresAt = default;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            using var doc = JsonDocument.Parse(json);
            username = doc.RootElement.GetProperty("sub").GetString() ?? "";
            displayName = doc.RootElement.GetProperty("name").GetString() ?? username;
            role = doc.RootElement.GetProperty("role").GetString() ?? "Admin";
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(doc.RootElement.GetProperty("exp").GetInt64());
            return !string.IsNullOrWhiteSpace(username);
        }
        catch
        {
            return false;
        }
    }
}
