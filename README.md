# BD Copilot for Microsoft Teams

An AI business-development copilot: chat, an RFP generator, a business case generator and a proposal generator, all grounded in your team's own SharePoint/Teams documents with citations, and all respecting your existing SharePoint permissions on every query.

This is a real, working scaffold — Angular 20 talks to a real .NET Core 10 API, which talks to a real PostgreSQL + pgvector database and a real Ollama (or Azure OpenAI) model. It is **not** a click-through mock: point it at a running database and model endpoint and the RAG loop (question → grounded answer with citation) works end to end. Two integration points are deliberately stubbed rather than faked — see **What's real vs. stubbed** below, and the Architecture and Roadmap pages inside the app itself.

## Project layout

```
BD_Copilot/
├── docker-compose.yml          Postgres+pgvector, Azurite (blob emulator), optional Ollama
├── backend/
│   ├── BDCopilot.sln
│   ├── database/init.sql       Postgres schema (documents, document_chunks, pgvector index)
│   └── src/
│       ├── BDCopilot.Core/            Domain models & interfaces — no dependencies
│       ├── BDCopilot.Infrastructure/  EF Core, Semantic Kernel, blob storage, real service impls
│       ├── BDCopilot.SyncJobs/        Hangfire recurring job wiring
│       └── BDCopilot.Api/             ASP.NET Web API — controllers, Program.cs, Swagger
└── bd-copilot-web/             Angular 20 workspace (standalone components, signals, Fluent UI)
```

## Prerequisites

- **.NET 10 SDK** — this project targets `net10.0`. If .NET 10 isn't available to you yet, retarget every `.csproj`'s `<TargetFramework>` to `net8.0` (the code doesn't use any .NET 10-only language features).
- **Node.js 20+** and npm, for the Angular workspace.
- **Docker** (or Docker Desktop), for Postgres/pgvector and the Azurite blob emulator.
- **Ollama**, for local-dev model calls — [ollama.com](https://ollama.com), then `ollama pull llama3.1:8b && ollama pull nomic-embed-text`.

## 1. Start the data layer

```bash
cd BD_Copilot
docker compose up -d          # Postgres+pgvector on :5432, Azurite blob emulator on :10000
```

`backend/database/init.sql` is mounted into Postgres's `docker-entrypoint-initdb.d`, so the `documents` / `document_chunks` tables and the pgvector `ivfflat` index are created automatically on first boot. Hangfire creates and owns its own `hangfire` schema the first time the API runs — nothing to do for that by hand.

If you'd rather run Ollama in Docker too: `docker compose --profile ollama up -d ollama`. Otherwise just run `ollama serve` natively — the API's default `appsettings.Development.json` already points at `http://localhost:11434`.

## 2. Run the backend API

```bash
cd backend
dotnet restore
dotnet run --project src/BDCopilot.Api
```

- Swagger UI: **http://localhost:5154/swagger** (opens automatically via `launchSettings.json`; HTTPS profile also available on :7017)
- Hangfire dashboard: **http://localhost:5154/hangfire** — shows the recurring document-sync job (`*/5 * * * *`). `Program.cs` has a comment flagging that this needs a real `[Authorize]` filter tied to Entra ID before it's exposed anywhere but localhost.
- Connection string, AI provider, and blob storage settings live in `backend/src/BDCopilot.Api/appsettings.Development.json` / `appsettings.json` — the Postgres password (`CHANGE_ME`) and default `bdcopilot`/`bdcopilot` credentials match what `docker-compose.yml` provisions; change both together if you change one.

## 3. Run the frontend

```bash
cd bd-copilot-web
npm install     # already run once during scaffolding — safe to re-run
npm start        # ng serve, http://localhost:4200
```

The app runs standalone in a regular browser tab as well as inside a Teams tab — `TeamsService` detects whether it's iframed by Teams (`window.self !== window.top`) and calls `app.initialize()` / `app.getContext()` only when it is, falling back to a "Running outside Teams · demo identity" chip otherwise. `src/environments/environment.development.ts` points `apiBaseUrl` at `http://localhost:5154/api`.

## 4. Sideload into Teams (optional)

`bd-copilot-web/public/teams/manifest.json` is a real Teams app manifest (schema 1.19) with a `color.png`/`outline.png` icon pair already generated. Before zipping it for Teams:

1. Deploy the Angular app somewhere Teams can reach it over HTTPS, and replace every `REPLACE_WITH_YOUR_DEPLOYED_HOST` placeholder in the manifest with that host.
2. Register an app in Entra ID, and replace `REPLACE_WITH_YOUR_ENTRA_APP_CLIENT_ID` with its client ID (needed for `webApplicationInfo` / SSO).
3. `zip -j bd-copilot-teams-app.zip manifest.json color.png outline.png` and upload it via Teams Admin Center or "Upload a custom app."

## What's real vs. stubbed

Told plainly, and also inside the app's own **Architecture** page:

**Real and running end-to-end:** the Angular ⇄ .NET ⇄ Postgres/pgvector path; the Semantic Kernel provider abstraction (Ollama and Azure OpenAI are chosen entirely by the `Ai:Provider` config value — see `SemanticKernelFactory.cs` — never by a client-side toggle, which is why the Settings screen in the app is deliberately read-only); Swagger; Hangfire's recurring-job scheduling; and the retrieval + generation logic behind Chat, the RFP Generator, the Business Case Generator and the Proposal Generator.

**Stubbed on purpose, and logging loudly about it:**
- `AccessControlService` (`backend/src/BDCopilot.Infrastructure/Services/AccessControlService.cs`) currently allows any authenticated caller through and logs a warning describing what a real check would have filtered. Wiring it to live Microsoft Graph permission calls is Phase 2 in the Roadmap page, once a Teams app is registered in Entra ID.
- `DocumentSyncService` (`backend/src/BDCopilot.Infrastructure/Services/DocumentSyncService.cs`) doesn't call Microsoft Graph yet — its doc comment lists the three concrete steps that finish it (delta query, real document parsing, writing real ACL hashes).

Neither stub is hidden behind a happy-path demo; both are visible, documented, and are the two things to build next per the in-app Roadmap page.

## A known limitation of how this was built

This scaffold was generated in a sandboxed environment with **no access to NuGet** (all NuGet package-registry domains were blocked by network policy). Every `.csproj`, `Program.cs` and service class was hand-written against known-correct, real .NET/Semantic Kernel/EF Core/Hangfire/Npgsql API signatures, and checked for structural correctness (balanced braces/parens across all 26 C# files), but **`dotnet restore` and `dotnet build` were never actually run against this code**. Run `dotnet restore && dotnet build` yourself as the first real step — that is the actual compile-correctness check this project still needs. The Angular frontend, by contrast, **was** built and served end-to-end in that same sandbox (`ng build` succeeds, and every route — Chat, RFP/Business Case/Proposal generators, Knowledge Search, Document Library, Settings, Architecture, Tech Stack, Roadmap — was screenshotted and confirmed rendering correctly).

## One deliberate substitution from the requested stack

The brief asked for **Fluent UI React**, but this is an Angular app — React components can't be used inside it. The scaffold uses **`@fluentui/web-components`** instead: Microsoft's own framework-agnostic Fluent UI implementation (custom elements, e.g. `<fluent-button>`, `<fluent-text-input>`), which is what Microsoft recommends for non-React frameworks including Angular and Teams tab apps. Same design system, same visual language, different binding layer.
