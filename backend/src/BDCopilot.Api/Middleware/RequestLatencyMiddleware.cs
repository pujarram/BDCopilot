using System.Diagnostics;
using Microsoft.ApplicationInsights;

namespace BDCopilot.Api.Middleware;

/// <summary>Records API request latency to Application Insights (BdCopilot.ApiLatencyMs).</summary>
public sealed class RequestLatencyMiddleware
{
    private readonly RequestDelegate _next;

    public RequestLatencyMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context?.Request is null)
        {
            await _next(context!);
            return;
        }

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
            TrackLatency(context, path, sw.Elapsed.TotalMilliseconds);
        }
    }

    private static void TrackLatency(HttpContext context, string path, double durationMs)
    {
        try
        {
            var telemetry = context.RequestServices?.GetService<TelemetryClient>();
            if (telemetry is null)
            {
                return;
            }

            telemetry.TrackEvent(
                "BdCopilot.ApiLatency",
                new Dictionary<string, string>
                {
                    ["Path"] = path,
                    ["Method"] = context.Request.Method ?? "GET",
                    ["StatusCode"] = context.Response?.StatusCode.ToString() ?? "0"
                },
                new Dictionary<string, double>
                {
                    ["DurationMs"] = durationMs
                });
        }
        catch
        {
            // Telemetry must never break the request pipeline.
        }
    }
}
