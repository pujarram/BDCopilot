using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace BDCopilot.Api.Controllers;

/// <summary>Knowledge Search — ACL-trimmed retrieval, grounded answer, and source list.</summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SearchController : ControllerBase
{
    private readonly IVectorSearchService _search;
    private readonly IAiChatService _chat;
    private readonly ILogger<SearchController> _logger;

    public SearchController(IVectorSearchService search, IAiChatService chat, ILogger<SearchController> logger)
    {
        _search = search;
        _chat = chat;
        _logger = logger;
    }

    /// <param name="q">Free-text query.</param>
    /// <param name="userObjectId">Entra ID object id of the caller.</param>
    /// <param name="topK">Maximum results to return.</param>
    /// <param name="corpusSource">Local | Online | All</param>
    [HttpGet]
    [ProducesResponseType(typeof(List<SearchResultItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SearchResultItem>>> Search(
        [FromQuery] string q,
        [FromQuery] string userObjectId,
        [FromQuery] int topK = 8,
        [FromQuery] string? corpusSource = null,
        CancellationToken ct = default)
    {
        var oid = UserIdentity.ResolveObjectId(User, userObjectId);
        var results = await _search.SearchAsync(q, oid, topK, corpusSource, ct);
        return Ok(results);
    }

    /// <summary>
    /// Find sources and draft a short grounded answer with citations (Knowledge Search UX).
    /// </summary>
    [HttpPost("ask")]
    [ProducesResponseType(typeof(KnowledgeSearchResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<KnowledgeSearchResponse>> Ask(
        [FromBody] KnowledgeSearchRequest request,
        CancellationToken ct)
    {
        try
        {
            var oid = UserIdentity.ResolveObjectId(User, request.UserObjectId);
            if (!string.IsNullOrWhiteSpace(oid))
            {
                request.UserObjectId = oid;
            }

            return Ok(await _chat.SearchAndAnswerAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Knowledge search answer failed.");
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Knowledge search failed",
                Detail = ex.Message
            });
        }
    }
}
