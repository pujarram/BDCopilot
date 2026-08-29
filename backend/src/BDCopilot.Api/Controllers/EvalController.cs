using System.Text.Json;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace BDCopilot.Api.Controllers;

/// <summary>Phase 1/4 golden Q&amp;A eval harness — smoke-tests retrieval against known questions.</summary>
[ApiController]
[Route("api/eval")]
[Produces("application/json")]
public class EvalController : ControllerBase
{
    private readonly IVectorSearchService _search;
    private readonly IWebHostEnvironment _env;

    public EvalController(IVectorSearchService search, IWebHostEnvironment env)
    {
        _search = search;
        _env = env;
    }

    [HttpPost("golden")]
    public async Task<IActionResult> RunGolden([FromQuery] string userObjectId = "eval-runner", CancellationToken ct = default)
    {
        var path = Path.Combine(_env.ContentRootPath, "Eval", "golden-qa.json");
        if (!System.IO.File.Exists(path))
        {
            // Fall back to repo-relative path when running from bin/
            path = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "..", "..", "..", "Eval", "golden-qa.json"));
        }

        if (!System.IO.File.Exists(path))
        {
            return NotFound(new { error = "golden-qa.json not found", tried = path });
        }

        var json = await System.IO.File.ReadAllTextAsync(path, ct);
        var cases = JsonSerializer.Deserialize<List<GoldenCase>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new();

        var results = new List<object>();
        var passed = 0;

        foreach (var g in cases)
        {
            var hits = await _search.SearchAsync(g.Question, userObjectId, topK: 5, ct: ct);
            var fileNames = hits.Select(h => h.Source.FileName).ToList();
            var hit = g.ExpectedFileNameFragments.Any(frag =>
                fileNames.Any(f => f.Contains(frag, StringComparison.OrdinalIgnoreCase)));

            if (hit) passed++;
            results.Add(new
            {
                g.Id,
                g.Question,
                passed = hit,
                expected = g.ExpectedFileNameFragments,
                actual = fileNames
            });
        }

        return Ok(new
        {
            total = cases.Count,
            passed,
            failed = cases.Count - passed,
            passRate = cases.Count == 0 ? 0 : (double)passed / cases.Count,
            results
        });
    }

    private sealed class GoldenCase
    {
        public string Id { get; set; } = "";
        public string Question { get; set; } = "";
        public List<string> ExpectedFileNameFragments { get; set; } = new();
    }
}
