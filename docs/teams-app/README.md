# BD Copilot — Teams app package

Microsoft Teams app for **BD Copilot**: personal **tabs** (Chat / RFP / Search / Library) + **chat bot** (RAG Q&A and search).

| Field | Value |
| --- | --- |
| Tenant | `83928f87-96d8-4ebf-b095-7c3bf0c53193` (ikione.com) |
| App / Bot ID | Same Entra **Application (client) ID** used for Graph sync (`Graph:ClientId`) |
| Messaging endpoint | `https://<api-ngrok>/api/channels/teams/messages` |

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
| `REPLACE_WITH_IKIONE_APP_CLIENT_ID` | Entra Application (client) ID (must exist in tenant `83928f87-…`) |
| `YOUR-NGROK-4200.ngrok-free.app` | ngrok host for Angular (`ngrok http 4200`) |
| `YOUR-NGROK-5154.ngrok-free.app` | ngrok host for API (`ngrok http 5154`) — only needed in `validDomains` if tabs call that host |

Also set API config:

```powershell
cd backend\src\BDCopilot.Api
dotnet user-secrets set "TeamsBot:Enabled" "true"
dotnet user-secrets set "TeamsBot:WebBaseUrl" "https://YOUR-NGROK-4200.ngrok-free.app"
# MicrosoftAppId / password fall back to Graph:ClientId + Graph:ClientSecret when empty
```

`TeamsBot:Enabled` is already `true` in `appsettings.Development.json`.

## 2. Azure Bot (same app id)

1. Azure Portal → create **Azure Bot** (or Bot Channels Registration).
2. **Microsoft App ID** = your Ikione Application (client) ID (same as Graph).
3. Create / paste the **client secret Value** (same secret as `Graph:ClientSecret`).
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
.\generate-icons.ps1
# or:
Compress-Archive -Path manifest.json, color.png, outline.png -DestinationPath BDCopilot-teams-app.zip -Force
```

Zip root must contain **only** those three files (no parent folder).

## 5. Install in Teams

| Audience | Method |
| --- | --- |
| Pilot (if policy allows) | Teams → Apps → Manage your apps → Upload a custom app |
| Org | Teams Admin Center → Manage apps → Upload new app |

Open **BD Copilot** → try bot: `help`, `search wealth`, or a free-text BD question. Use tabs for RFP / Library.

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
