using System.Diagnostics;
using Microsoft.ApplicationInsights;

namespace BDCopilot.Api.Middleware;

/// <summary>Records API request latency to Application Insights (BdCopilot.ApiLatencyMs).</summary>
public sealed class RequestLatencyMiddleware
{
    private readonly RequestDelegate _next;

    public RequestLatencyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            try
            {
                var telemetry = context.RequestServices.GetService<TelemetryClient>();
                if (telemetry is not null)
                {
                    telemetry.TrackEvent(
                        "BdCopilot.ApiLatency",
                        new Dictionary<string, string>
                        {
                            ["Path"] = path,
                            ["Method"] = context.Request.Method,
                            ["StatusCode"] = context.Response.StatusCode.ToString()
                        },
                        new Dictionary<string, double>
                        {
                            ["DurationMs"] = sw.Elapsed.TotalMilliseconds
                        });
                }
            }
            catch
            {
                // Telemetry must never break the request pipeline.
            }
        }
    }
}
