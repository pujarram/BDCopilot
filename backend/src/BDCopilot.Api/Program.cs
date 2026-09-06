using BDCopilot.Api.Filters;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using BDCopilot.Infrastructure.Graph;
using BDCopilot.Infrastructure.Parsing;
using BDCopilot.Infrastructure.Services;
using BDCopilot.Infrastructure.Teams;
using BDCopilot.SyncJobs;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Key Vault (production): set KEYVAULT_URI or KeyVault:Uri — uses DefaultAzureCredential (Managed Identity on App Service).
var keyVaultUri = builder.Configuration["KeyVault:Uri"]
    ?? Environment.GetEnvironmentVariable("KEYVAULT_URI");
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUri),
        new Azure.Identity.DefaultAzureCredential());
}

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
builder.Services.Configure<AiSettings>(builder.Configuration.GetSection(AiSettings.SectionName));
builder.Services.Configure<BlobStorageSettings>(builder.Configuration.GetSection(BlobStorageSettings.SectionName));
builder.Services.Configure<GraphSyncSettings>(builder.Configuration.GetSection(GraphSyncSettings.SectionName));
builder.Services.Configure<AzureAdSettings>(builder.Configuration.GetSection(AzureAdSettings.SectionName));
builder.Services.Configure<AzureAiSearchSettings>(builder.Configuration.GetSection(AzureAiSearchSettings.SectionName));
builder.Services.Configure<AdminUserSettings>(builder.Configuration.GetSection(AdminUserSettings.SectionName));
builder.Services.Configure<TeamsBotSettings>(builder.Configuration.GetSection(TeamsBotSettings.SectionName));
builder.Services.Configure<LocalDocsSettings>(builder.Configuration.GetSection(LocalDocsSettings.SectionName));
builder.Services.Configure<PlannerSyncSettings>(builder.Configuration.GetSection(PlannerSyncSettings.SectionName));

var aiSettings = builder.Configuration.GetSection(AiSettings.SectionName).Get<AiSettings>() ?? new AiSettings();
var azureAd = builder.Configuration.GetSection(AzureAdSettings.SectionName).Get<AzureAdSettings>() ?? new AzureAdSettings();

// ---------------------------------------------------------------------------
// Data
// ---------------------------------------------------------------------------
var pgConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

// Npgsql must map Pgvector.Vector on the data source *and* in EF options, or INSERT/UPDATE
// of embeddings fails with InvalidCastException at SaveChangesAsync.
var npgsqlDataSourceBuilder = new NpgsqlDataSourceBuilder(pgConnectionString);
npgsqlDataSourceBuilder.UseVector();
var npgsqlDataSource = npgsqlDataSourceBuilder.Build();
builder.Services.AddSingleton(npgsqlDataSource);

builder.Services.AddDbContext<BdCopilotDbContext>(options =>
    options.UseNpgsql(npgsqlDataSource, npgsql => npgsql.UseVector()));

builder.Services.AddScoped(sp =>
{
    var opts = sp.GetRequiredService<DbContextOptions<BdCopilotDbContext>>();
    return new BdCopilotDbContext(opts, aiSettings.EmbeddingDimensions);
});

// ---------------------------------------------------------------------------
// Auth (Entra ID) — enabled when AzureAd:TenantId is set; otherwise Development bypass
// ---------------------------------------------------------------------------
var entraConfigured = !string.IsNullOrWhiteSpace(azureAd.TenantId)
    && !string.IsNullOrWhiteSpace(azureAd.ClientId);

if (entraConfigured)
{
    var authority = $"{azureAd.Instance.TrimEnd('/')}/{azureAd.TenantId}/v2.0";
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = !string.IsNullOrWhiteSpace(azureAd.Audience) || !string.IsNullOrWhiteSpace(azureAd.ClientId),
                ValidAudience = string.IsNullOrWhiteSpace(azureAd.Audience) ? azureAd.ClientId : azureAd.Audience,
                RoleClaimType = "roles",
                NameClaimType = "preferred_username"
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("BdCopilotAdmin", policy => policy.RequireRole("BdCopilot.Admin"));
        options.FallbackPolicy = azureAd.RequireAuthOnApi
            ? new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build()
            : null;
    });
}
else
{
    builder.Services.AddAuthorization();
}

if (!string.IsNullOrWhiteSpace(builder.Configuration["ApplicationInsights:ConnectionString"]))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

// ---------------------------------------------------------------------------
// AI + retrieval + Phase 1–4 services
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<ISemanticKernelFactory, SemanticKernelFactory>();
builder.Services.AddSingleton<GraphClientFactory>();
builder.Services.AddScoped<IDocumentParserRouter, DocumentParserRouter>();
builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<IAccessAuditService, AccessAuditService>();
builder.Services.AddScoped<IAccessControlService, AccessControlService>();
builder.Services.AddBdCopilotVectorSearch(builder.Configuration);
builder.Services.AddScoped<IAiChatService, AiChatService>();
builder.Services.AddScoped<IDocumentGeneratorService, DocumentGeneratorService>();
builder.Services.AddScoped<IDocumentSyncService, DocumentSyncService>();
builder.Services.AddScoped<ILocalDocumentSyncService, LocalDocumentSyncService>();
builder.Services.AddScoped<IPlannerSyncService, PlannerSyncService>();
builder.Services.AddScoped<IPlannerSnapshotService, PlannerSnapshotService>();
builder.Services.AddScoped<IProjectManagerService, ProjectManagerService>();
builder.Services.AddScoped<IUnifiedIntelligenceService, UnifiedIntelligenceService>();
builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<IDocumentExportService, DocumentExportService>();
builder.Services.AddScoped<IGenerationWorkflowService, GenerationWorkflowService>();
builder.Services.AddScoped<IRfpHistoryService, RfpHistoryService>();
builder.Services.AddScoped<IDocumentHistoryService, DocumentHistoryService>();
builder.Services.AddScoped<ITokenUsageTracker, TokenUsageTracker>();
builder.Services.AddSingleton<TeamsBotAuthValidator>();
builder.Services.AddHttpClient<ITeamsBotConnector, TeamsBotConnector>();
builder.Services.AddScoped<ITeamsChannelService, TeamsChannelService>();

// ---------------------------------------------------------------------------
// Background sync (Hangfire)
// ---------------------------------------------------------------------------
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(pgConnectionString)));
builder.Services.AddHangfireServer();

// ---------------------------------------------------------------------------
// Web API + Swagger
// ---------------------------------------------------------------------------
builder.Services.AddControllers().AddJsonOptions(opts =>
{
    opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "BD Copilot API",
        Version = "v1",
        Description =
            "AI-powered business-development copilot for Microsoft Teams — chat, RFP/business " +
            "case/proposal generators, knowledge search and the document library, all backed by " +
            "ACL-filtered retrieval over your indexed SharePoint/Teams files."
    });

    if (entraConfigured)
    {
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "Entra ID bearer token",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                },
                Array.Empty<string>()
            }
        });
    }

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath)) c.IncludeXmlComments(xmlPath);
});

const string AngularDevCorsPolicy = "AngularDev";
builder.Services.AddCors(options =>
{
    options.AddPolicy(AngularDevCorsPolicy, policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200",
                "https://localhost:4200",
                "https://teams.microsoft.com",
                "https://*.teams.microsoft.com")
            .SetIsOriginAllowedToAllowWildcardSubdomains()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "BD Copilot API v1");
        c.DocumentTitle = "BD Copilot API";
    });
}

// CORS before HTTPS redirect so cross-origin calls from Angular (localhost:4200) are not
// broken by a 307 to HTTPS that lacks Access-Control-Allow-Origin.
app.UseCors(AngularDevCorsPolicy);

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseMiddleware<BDCopilot.Api.Middleware.RequestLatencyMiddleware>();

if (entraConfigured)
{
    app.UseAuthentication();
}
app.UseAuthorization();
app.MapControllers();

app.MapHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new IDashboardAuthorizationFilter[] { new HangfireDashboardAuthFilter() }
});

using (var scope = app.Services.CreateScope())
{
    var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
    await using (var conn = await dataSource.OpenConnectionAsync())
    await using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS generated_document_history (
                id                          uuid PRIMARY KEY,
                generation_id               uuid NOT NULL,
                document_type               varchar(64)  NOT NULL,
                title                       varchar(512) NOT NULL,
                status                      varchar(32)  NOT NULL DEFAULT 'Draft',
                metadata_json               jsonb        NOT NULL DEFAULT '{}'::jsonb,
                sections_json               jsonb        NOT NULL DEFAULT '[]'::jsonb,
                created_by_user_object_id   varchar(256) NOT NULL,
                created_by_display_name     varchar(256),
                created_at                  timestamptz  NOT NULL DEFAULT now(),
                updated_at                  timestamptz,
                channel_share_point_url     varchar(2048),
                channel_upload_status       varchar(64),
                channel_upload_error        text
            );
            CREATE INDEX IF NOT EXISTS ix_generated_document_history_type ON generated_document_history (document_type);
            CREATE INDEX IF NOT EXISTS ix_generated_document_history_created_by ON generated_document_history (created_by_user_object_id);
            CREATE INDEX IF NOT EXISTS ix_generated_document_history_created_at ON generated_document_history (created_at);
            CREATE INDEX IF NOT EXISTS ix_generated_document_history_generation_id ON generated_document_history (generation_id);

            CREATE TABLE IF NOT EXISTS planner_plans (
                id              uuid PRIMARY KEY,
                graph_plan_id   varchar(128) NOT NULL,
                graph_group_id  varchar(128),
                title           varchar(512) NOT NULL,
                owner_name      varchar(256),
                last_sync_at    timestamptz NOT NULL DEFAULT now()
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_planner_plans_graph_plan_id ON planner_plans (graph_plan_id);

            CREATE TABLE IF NOT EXISTS planner_buckets (
                id               uuid PRIMARY KEY,
                plan_id          uuid NOT NULL REFERENCES planner_plans (id) ON DELETE CASCADE,
                graph_bucket_id  varchar(128) NOT NULL,
                name             varchar(256) NOT NULL,
                order_hint       int NOT NULL DEFAULT 0
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_planner_buckets_graph_bucket_id ON planner_buckets (graph_bucket_id);

            CREATE TABLE IF NOT EXISTS planner_tasks (
                id                 uuid PRIMARY KEY,
                plan_id            uuid NOT NULL REFERENCES planner_plans (id) ON DELETE CASCADE,
                graph_task_id      varchar(128) NOT NULL,
                graph_bucket_id    varchar(128),
                title              varchar(512) NOT NULL,
                description        text,
                start_date         timestamptz,
                due_date           timestamptz,
                percent_complete   int NOT NULL DEFAULT 0,
                bucket_name        varchar(256),
                status             varchar(32) NOT NULL DEFAULT 'NotStarted',
                assigned_users     varchar(1024),
                is_delayed         boolean NOT NULL DEFAULT false,
                last_sync_at       timestamptz NOT NULL DEFAULT now(),
                graph_created_at   timestamptz,
                graph_modified_at  timestamptz
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_planner_tasks_graph_task_id ON planner_tasks (graph_task_id);
            CREATE INDEX IF NOT EXISTS ix_planner_tasks_due_date ON planner_tasks (due_date);
            CREATE INDEX IF NOT EXISTS ix_planner_tasks_is_delayed ON planner_tasks (is_delayed);

            CREATE TABLE IF NOT EXISTS planner_task_snapshots (
                id                  uuid PRIMARY KEY,
                snapshot_date       date NOT NULL,
                total_tasks         int NOT NULL DEFAULT 0,
                completed           int NOT NULL DEFAULT 0,
                in_progress         int NOT NULL DEFAULT 0,
                not_started         int NOT NULL DEFAULT 0,
                delayed             int NOT NULL DEFAULT 0,
                completion_percent  double precision NOT NULL DEFAULT 0,
                health_score        int NOT NULL DEFAULT 0,
                captured_at         timestamptz NOT NULL DEFAULT now()
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ix_planner_task_snapshots_date ON planner_task_snapshots (snapshot_date);

            ALTER TABLE IF EXISTS token_usage ADD COLUMN IF NOT EXISTS duration_ms int NOT NULL DEFAULT 0;
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    var sync = scope.ServiceProvider.GetRequiredService<IDocumentSyncService>();
    var graph = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<GraphSyncSettings>>().Value;
    if (graph.AllowDevSeedWithoutGraph)
    {
        await sync.SeedPilotDocumentsAsync();
    }

    var localDocs = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalDocsSettings>>().Value;
    if (localDocs.IndexOnStartup)
    {
        var localSync = scope.ServiceProvider.GetRequiredService<ILocalDocumentSyncService>();
        try
        {
            await localSync.SyncAsync();
        }
        catch (Exception ex)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("LocalDocsStartup");
            logger.LogWarning(ex, "Local docs index on startup failed.");
        }
    }

    var plannerOpts = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlannerSyncSettings>>().Value;
    if (plannerOpts.Enabled)
    {
        try
        {
            var planner = scope.ServiceProvider.GetRequiredService<IPlannerSyncService>();
            await planner.SyncAsync();
        }
        catch (Exception ex)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PlannerStartup");
            logger.LogWarning(ex, "Planner sync on startup failed.");
        }
    }

    var recurringJobs = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurringJobs.RegisterRecurringJobs();
}

app.Run();
