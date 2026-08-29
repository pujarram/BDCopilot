using BDCopilot.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BDCopilot.Api.Controllers;

public class AiProviderStatus
{
    public required string Provider { get; set; }
    public required string ChatModel { get; set; }
    public required string EmbeddingModel { get; set; }
}

/// <summary>
/// Read-only view of which AI provider is active. Unlike the HTML prototype's Settings screen,
/// this does NOT let a caller flip Ai:Provider at runtime — that value is deliberately owned by
/// appsettings.{Environment}.json (or an App Service environment variable in production) so a
/// misbehaving client request can never silently redirect production traffic to a developer's
/// local Ollama instance. Switching providers means redeploying with a different environment,
/// exactly as described in the architecture doc.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SettingsController : ControllerBase
{
    private readonly AiSettings _settings;

    public SettingsController(IOptions<AiSettings> settings)
    {
        _settings = settings.Value;
    }

    [HttpGet("ai-provider")]
    [ProducesResponseType(typeof(AiProviderStatus), StatusCodes.Status200OK)]
    public ActionResult<AiProviderStatus> GetAiProvider()
    {
        var status = new AiProviderStatus
        {
            Provider = _settings.Provider,
            ChatModel = _settings.Provider == "AzureOpenAI" ? _settings.AzureOpenAI.ChatDeployment : _settings.Ollama.ChatModel,
            EmbeddingModel = _settings.Provider == "AzureOpenAI" ? _settings.AzureOpenAI.EmbeddingDeployment : _settings.Ollama.EmbeddingModel
        };
        return Ok(status);
    }
}
