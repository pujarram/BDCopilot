# WealthBD RFP sync checklist

Goal: index SharePoint folder  
`sites/WealthBD/Shared Documents/General/RFP DataBase/IWM`  
into BD Copilot **Document Library** (`http://localhost:4200/library`) and RAG.

> **Temporary local pilot:** Ikione **BDTeam** site values are in `appsettings.Development.json`  
> (site `BDTeam`, folder `RFP`). See **§0 Temporary Ikione pilot** below. Switch to WealthBD when Tietoevry Graph is ready.

---

## 0. Temporary Ikione pilot (current Development config)

| Item | Value |
|------|--------|
| Tenant | `83928f87-96d8-4ebf-b095-7c3bf0c53193` (`ikione.com`) |
| Site | `https://ikione.sharepoint.com/sites/BDTeam` |
| RFP folder | `Shared Documents/RFP` → `SyncFolderPaths: ["RFP"]` |
| `Graph:PilotSiteId` | leave empty (path lookup preferred) |
| `Graph:PilotSitePath` | `ikione.sharepoint.com:/sites/BDTeam` |
| `Graph:ClientId` / `AzureAd:ClientId` | your Entra **Application (client) ID** (user-secrets) |
| `SyncFolderPaths` | `["RFP"]` — only index the RFP library folder |

**Still required before sync works:**

1. **Client secret** for the app (Entra → Certificates & secrets). Store via user-secrets — **do not commit**:

```powershell
cd backend/src/BDCopilot.Api
dotnet user-secrets set "Graph:ClientSecret" "<paste-secret-here>"
```

2. **Application permissions** on that app + **admin consent**:
   - Prefer `Sites.Selected` (then grant this app on site **BDTeam**), **or**
   - `Sites.Read.All` + `Files.Read.All`
3. Confirm ClientId is the **Application (client) ID**, not a user object id.
4. Restart API → `/library` → **Run sync now**.

Optional — resolve site id / list RFP folder (Graph Explorer):

```http
GET https://graph.microsoft.com/v1.0/sites/ikione.sharepoint.com:/sites/BDTeam
GET https://graph.microsoft.com/v1.0/sites/ikione.sharepoint.com:/sites/BDTeam:/drive/root:/RFP:/children
```

---

## 1. Resolve the Graph site id (WealthBD later)

In a browser (signed into the correct M365 tenant), open Graph Explorer:  
https://developer.microsoft.com/graph/graph-explorer

Run:

```http
GET https://graph.microsoft.com/v1.0/sites/tietoevry.sharepoint.com:/sites/WealthBD
```

Copy the response `id` (looks like `tietoevry.sharepoint.com,guid,guid`).  
That value goes into **`Graph:PilotSiteId`**.

Optional — confirm the IWM folder exists under the default document library:

```http
GET https://graph.microsoft.com/v1.0/sites/{site-id}/drive/root:/General/RFP DataBase/IWM:/children
```

---

## 2. Entra app (Identity / Azure team)

| Item | Value |
|------|--------|
| App type | Single-tenant |
| Application permissions | Prefer **`Sites.Selected`** (+ grant on WealthBD). Fallback: `Sites.Read.All`, `Files.Read.All` |
| Delegated (Teams / user SSO later) | `User.Read`, Sites/Files read as policy allows |
| Consent | **Admin consent** required |
| Secret | Client secret or certificate → store in Key Vault / user-secrets, not git |

For `Sites.Selected`, after consent an admin must grant the app access to the WealthBD site (Graph `sites/{id}/permissions` or SharePoint admin UI).

---

## 3. Exact `appsettings` keys

Fill **`Graph`** and **`AzureAd`** (same tenant). Example for WealthBD IWM only:

```json
"Graph": {
  "TenantId": "<entra-tenant-id>",
  "ClientId": "<app-client-id>",
  "ClientSecret": "<app-client-secret>",
  "PilotSiteId": "<site-id-from-step-1>",
  "SiteIds": [],
  "SyncFolderPaths": [
    "General/RFP DataBase/IWM"
  ],
  "BdChannelDriveId": "",
  "RfpFolderPath": "General/RFP DataBase/IWM",
  "AllowDevSeedWithoutGraph": false,
  "AllowDevBypass": false
},

"AzureAd": {
  "Instance": "https://login.microsoftonline.com/",
  "TenantId": "<entra-tenant-id>",
  "ClientId": "<api-or-same-app-client-id>",
  "EnforceAcl": true,
  "RequireAuthOnApi": true
}
```

| Key | Purpose |
|-----|---------|
| `Graph:TenantId` / `ClientId` / `ClientSecret` | App-only Graph client for background sync |
| `Graph:PilotSiteId` | WealthBD site id from step 1 |
| `Graph:SiteIds` | Extra sites (optional multi-site) |
| `Graph:SyncFolderPaths` | **Folder filter** — only index under these library-relative paths. Empty = whole drive |
| `Graph:RfpFolderPath` | Target folder for “Save to BD channel” uploads |
| `Graph:BdChannelDriveId` | Drive id if upload should target a specific drive |
| `Graph:AllowDevSeedWithoutGraph` | `false` once real Graph is configured (stops demo seed) |
| `Graph:AllowDevBypass` | `false` in pilot/prod |
| `AzureAd:EnforceAcl` | `true` → live permission check per user on query |
| `AzureAd:RequireAuthOnApi` | `true` → JWT required on API |

**Local secrets (recommended):**

```powershell
cd backend/src/BDCopilot.Api
dotnet user-secrets set "Graph:ClientSecret" "<secret>"
dotnet user-secrets set "Graph:TenantId" "<tenant>"
dotnet user-secrets set "Graph:ClientId" "<client-id>"
dotnet user-secrets set "Graph:PilotSiteId" "<site-id>"
```

---

## 4. Folder path rules (`SyncFolderPaths`)

- Paths are **relative to the document library root** (do **not** include `Shared Documents`).
- WealthBD RFPs: `"General/RFP DataBase/IWM"`
- Matching is prefix-based (case-insensitive), including nested files.
- If the list is **empty**, the whole site drive(s) are indexed.
- Deleted files (`@removed` in delta) are still removed from the index even if outside the filter.

---

## 5. Run sync and verify

1. Restart `BDCopilot.Api` after config changes.  
2. Open `http://localhost:4200/library`.  
3. Click **Run sync now** (or wait for Hangfire ~5 min).  
4. Confirm files from IWM appear; open one SharePoint URL from the row.  
5. Ask chat a question that should cite an IWM RFP — citations should point at those files.

Admin: `GET /api/sync/health` for last success / errors / document counts.

---

## 6. What still needs product work (not blocked by this checklist)

- **Interactive Entra SSO** in the Angular app (MSAL / Teams SSO) so `EnforceAcl` uses the real user object id.  
- Until SSO is wired, local demo identity + `AllowDevBypass` may still be used on developer machines only.

---

## Quick reference — your SharePoint URL

| Browser URL piece | Config / Graph |
|-------------------|----------------|
| Host `tietoevry.sharepoint.com` + `/sites/WealthBD` | Site lookup → `PilotSiteId` |
| Library `Shared Documents` | Default site drive |
| Folder `General/RFP DataBase/IWM` | `SyncFolderPaths[0]` |



cd c:\Projects\0_BusinessDevelopment\Business_Development\BD_Copilot\backend\src\BDCopilot.Api
dotnet user-secrets set "Graph:ClientSecret" "7d1ed01a-2b66-4958-bf24-32b606d7e658"