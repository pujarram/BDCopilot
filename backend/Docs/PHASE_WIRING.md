# Phase 1–4 DI Wiring (Program.cs)

Do **not** apply until Entra ID auth and Application Insights are ready in the host project.
Add the following to `BDCopilot.Api/Program.cs` in the indicated sections.

## Configuration bindings

```csharp
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Graph;
using BDCopilot.Infrastructure.Services;
using BDCopilot.Api.Filters;

// After existing Configure<GraphSyncSettings>:
builder.Services.Configure<AzureAdSettings>(builder.Configuration.GetSection(AzureAdSettings.SectionName));
builder.Services.Configure<AzureAiSearchSettings>(builder.Configuration.GetSection(AzureAiSearchSettings.SectionName));
// Or rely on VectorSearchServiceRegistration which also binds AzureAiSearchSettings.
```

### appsettings.Development.json snippet

```json
{
  "AzureAd": {
    "EnforceAcl": false
  },
  "Graph": {
    "AllowDevSeedWithoutGraph": true,
    "AllowDevBypass": true
  },
  "AzureSearch": {
    "Endpoint": "",
    "IndexName": "bdcopilot-chunks",
    "ApiKey": ""
  }
}
```

## Graph client

```csharp
builder.Services.AddSingleton<GraphClientFactory>();
```

## Parsing + sync pipeline

```csharp
builder.Services.AddScoped<IDocumentParserRouter, BDCopilot.Infrastructure.Parsing.DocumentParserRouter>();
// DocumentSyncService already registered — no change needed unless replacing stub.
```

## Access control + audit

```csharp
builder.Services.AddScoped<IAccessAuditService, AccessAuditService>();
// AccessControlService already registered — constructor now requires IAccessAuditService + GraphClientFactory.
```

## Generation workflow + export

```csharp
builder.Services.AddScoped<IDocumentExportService, DocumentExportService>();
builder.Services.AddScoped<IGenerationWorkflowService, GenerationWorkflowService>();
builder.Services.AddScoped<ITokenUsageTracker, TokenUsageTracker>();
```

## Vector search factory (replaces direct VectorSearchService registration)

```csharp
// REMOVE:
// builder.Services.AddScoped<IVectorSearchService, VectorSearchService>();

// ADD:
builder.Services.AddBdCopilotVectorSearch(builder.Configuration);
```

## Application Insights (optional — TokenUsageTracker emits custom events when registered)

```csharp
builder.Services.AddApplicationInsightsTelemetry();
```

## Hangfire dashboard auth

```csharp
// REPLACE:
// app.MapHangfireDashboard("/hangfire");

// WITH:
app.MapHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireDashboardAuthFilter()]
});
```

## Entra ID (required before EnforceAcl=true in production)

When wiring Microsoft.Identity.Web:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("BdCopilotAdmin", policy =>
        policy.RequireRole("BdCopilot.Admin"));
});
```

Ensure `app.UseAuthentication()` runs before `app.UseAuthorization()`.

## Database

Re-run or migrate schema after pulling Phase 1–4 changes:

```bash
psql "$ConnectionStrings__Postgres" -f database/init.sql
```

New tables: `sync_site_state`, `generation_feedback`, `token_usage`, `access_audit`.
New column: `documents.graph_drive_id`.
