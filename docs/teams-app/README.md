# BD Copilot — Teams app package

Microsoft Teams app for **BD Copilot**: personal **tabs** (Chat / RFP / Search / Library) + **chat bot** (RAG Q&A and search).

| Field | Value |
| --- | --- |
| Tenant | `83928f87-96d8-4ebf-b095-7c3bf0c53193` (ikione.com) |
| Azure Bot (BDAgent) App ID | `2088516d-bf3b-40d6-88b5-6acc44929b6d` — use **only** in manifest `bots[].botId` |
| Teams app package ID | `7c4e9a2b-1d8f-4e6a-9b3c-a1b2c3d4e5f6` — manifest top-level `id` (not the bot id) |
| Graph / SSO App ID | `96c378fc-3d41-452c-85cb-f9f742f9c037` — `Graph:ClientId` + `webApplicationInfo.id` |
| Messaging endpoint | `https://<api-ngrok>/api/channels/teams/messages` |

**Do not** put the Graph client ID in `botId`. Teams “Invalid Bot” means `botId` ≠ Azure Bot Microsoft App ID.

## Contents

| File | Purpose |
| --- | --- |
| `manifest.json` | Tab + bot Teams manifest |
| `color.png` | 192×192 Tieto-branded color icon |
| `outline.png` | 32×32 outline icon |
| `generate-icons.ps1` | Regenerates icons + zip |
| `BDCopilot-teams-app/` | Copy of package ready to zip (optional) |

## 1. Fill placeholders in `manifest.json`

Replace every:

| Placeholder | With |
| --- | --- |
| Manifest top-level `id` | Teams **app package** GUID (`7c4e9a2b-…` — do not change unless creating a new app) |
| `bots[].botId` | Azure Bot **Microsoft App ID** (`2088516d-…` for BDAgent) |
| `webApplicationInfo.id` | Graph / SSO Entra app (`96c378fc-…`) |
| `YOUR-NGROK-4200.ngrok-free.app` | ngrok host for Angular (`ngrok http 4200`) |
| `YOUR-NGROK-5154.ngrok-free.app` | ngrok host for API (`ngrok http 5154`) — only needed in `validDomains` if tabs call that host |

Also set API config (bot credentials = BDAgent app, **not** Graph):

```powershell
cd backend\src\BDCopilot.Api
dotnet user-secrets set "TeamsBot:Enabled" "true"
dotnet user-secrets set "TeamsBot:MicrosoftAppId" "2088516d-bf3b-40d6-88b5-6acc44929b6d"
dotnet user-secrets set "TeamsBot:MicrosoftAppPassword" "<BDAgent client secret Value>"
dotnet user-secrets set "TeamsBot:WebBaseUrl" "https://YOUR-NGROK-4200.ngrok-free.app"
```

`TeamsBot:Enabled` is already `true` in `appsettings.Development.json`.

## 2. Azure Bot (BDAgent)

1. Azure Portal → **BDAgent** (or create Azure Bot).
2. **Microsoft App ID** = `2088516d-bf3b-40d6-88b5-6acc44929b6d` (must match manifest `botId`).
3. Create / paste the **client secret Value** into `TeamsBot:MicrosoftAppPassword` (Graph secret stays on `Graph:ClientSecret`).
4. Channels → enable **Microsoft Teams**.
5. Configuration → **Messaging endpoint**:
   `https://<api-ngrok-host>/api/channels/teams/messages`
6. Save. Probe: `GET https://<api-ngrok>/api/channels/teams/health`

## 3. Local tunnels

```powershell
# Terminal A — Angular tabs
ngrok http 4200

# Terminal B — API + bot webhook
ngrok http 5154
```

Update manifest URLs + `TeamsBot:WebBaseUrl`, restart API, update Azure Bot messaging endpoint.

## 4. Build upload zip

```powershell
cd docs\teams-app
.\build-teams-package.ps1
```

This validates manifest JSON, ensures **flat-root** zip entries (`manifest.json`, `color.png`, `outline.png` only), and writes `BDCopilot-teams-app.zip`.

## 5. Install in Teams

| Audience | Method |
| --- | --- |
| Pilot (if policy allows) | Teams → Apps → Manage your apps → Upload a custom app |
| Org | Teams Admin Center → Manage apps → Upload new app |

Open **BD Copilot** → try bot: `help`, `search wealth`, or a free-text BD question. Use tabs for RFP / Library.

## Troubleshooting: "Invalid Bot"

Teams shows this when `bots[].botId` is not a registered Azure Bot with **Microsoft Teams channel enabled** — not because of zip folder layout.

| Check | Action |
| --- | --- |
| Wrong id in `botId` | Must be **2088516d-bf3b-40d6-88b5-6acc44929b6d** (BDAgent). Never use Graph id `96c378fc-…` here. |
| Bot not in tenant | Azure Portal → App registrations → search `2088516d-…` — must exist in **your** Entra tenant. |
| Teams channel off | Azure Portal → **Azure Bot** (BDAgent) → **Channels** → Microsoft Teams → **Apply** (Enabled). |
| Stale sideload | Remove old "BD Copilot" from Teams → Manage your apps, then upload fresh `BDCopilot-teams-app.zip` (v0.1.3+). |
| Messaging endpoint | Azure Bot → Configuration → `https://<api-ngrok>/api/channels/teams/messages` (API must be running). |

Rebuild zip after manifest URL changes:

```powershell
cd docs\teams-app
.\build-teams-package.ps1
```

## Bot commands (v1)

| Message | Behavior |
| --- | --- |
| `help` | Commands + tab links |
| `search <keywords>` | Vector search over indexed docs |
| Anything else | RAG chat via `/api/chat` + citations |
| `rfp` / `library` | Points to tab URLs |

## Related

- Backend: `POST /api/channels/teams/messages`, `GET /api/channels/teams/health`
- Config section: `TeamsBot` (+ fallbacks to `Graph:ClientId` / `Graph:ClientSecret`)
- SharePoint sync checklist: `backend/Docs/WEALTHBD_SYNC_CHECKLIST.md`
