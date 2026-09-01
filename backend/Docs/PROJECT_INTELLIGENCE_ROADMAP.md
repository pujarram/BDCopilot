# Project Intelligence Platform — Roadmap

Extends BD Copilot into an **AI Project Intelligence Dashboard** on the same stack  
(.NET 10 · Angular · PostgreSQL · Graph · Teams Bot · SharePoint · Azure OpenAI / Ollama).

## Architecture (target)

```
Teams Channel
 ├── SharePoint docs ──► Graph ──► existing document sync / RAG
 ├── Planner tasks ───► Graph ──► planner_* tables (this work)
 └── Messages ───────► Teams Bot ──► NL queries over both corpora
                              │
                              ▼
                         .NET API
                    ┌─────┴─────┐
                    ▼           ▼
               PostgreSQL   Azure OpenAI / Ollama
                    │           │
                    ▼           ▼
            Angular Dashboard   AI Analysis Engine
            ├── Gantt / Timeline
            ├── Sprint & workload
            ├── Delay prediction
            └── Health + stakeholder reports
```

## Extra ideas (beyond the brief)

| Idea | Why |
|------|-----|
| **Cross-link Planner ↔ SharePoint** | Match task titles/keywords to indexed RFP/docs; “delayed task + related docs” in one answer |
| **BD Copilot reuse** | Stakeholder reports reuse the same grounded-generation path as Find & reuse / RFP |
| **CorpusSource: Planner** | Treat task descriptions as a third RAG source (Local / Online / Planner) |
| **Snapshot history** | Daily `planner_task_snapshots` for burndown & “progress stalled 7 days” |
| **Workload heat** | Capacity vs assignments (not just counts) |
| **Teams Adaptive Cards** | Bot returns sprint cards with Open in Dashboard deep links |
| **Optional Power BI** | Same Postgres views; keep Angular for in-Teams UX |

## Phases

### Phase 1 — Data foundation *(shipped)*
- [x] Roadmap doc
- [x] `planner_plans` / `planner_buckets` / `planner_tasks` in Postgres
- [x] Graph Planner sync (group/plan ids) + demo seed
- [x] API: list plans, tasks, sync health/trigger
- [x] Angular **Project Intelligence** shell: summary KPIs + task table

**Entra app permissions (Application):** `Tasks.Read.All`, `Group.Read.All` (+ admin consent).  
Config: `Planner:GroupIds` / `Planner:PlanIds`.

### Phase 2 — Visual dashboards *(shipped)*
- [x] CSS Gantt from Start / Due / % (today marker)
- [x] Timeline / swimlane by bucket
- [x] Team workload (assignments) + `GET /api/planner/workload`
- [x] View tabs + delayed-only filter

### Phase 3 — Teams Bot NL *(shipped)*
- [x] Commands: `delayed`, `tasks for <name>`, `sprint`/`health`, `due next week`/`this week`, `workload`, `projects`
- [x] Grounded free-form project NL using Planner snapshot + SharePoint RAG (`ChatRequest.ExtraContext`)
- [x] `ListTasksAsync` / `GET /api/planner/tasks` dueFrom·dueTo filters
- [x] Angular `/projects` deep links: `view`, `delayedOnly`, `assignee`, `dueFrom`, `dueTo`

### Phase 4 — AI Project Manager *(shipped)*
- [x] Health score (0–100) + risk level via `GET /api/planner/insights`
- [x] Rule-based delay prediction + modules at risk + staffing recommendations
- [x] Stakeholder weekly report (`GET /api/planner/report`, optional LLM narrative)
- [x] DOCX export (`GET /api/planner/report.docx`)
- [x] Teams: `insights` / `predict` / `report`
- [x] Angular **AI PM** tab on `/projects`

### Phase 5 — Unified intelligence *(shipped)*
- [x] `planner_task_snapshots` daily capture (on sync + Hangfire 06:00 UTC)
- [x] `GET /api/planner/burndown` trend series
- [x] `POST /api/planner/unified` — delayed tasks + SharePoint hits + management brief
- [x] Teams: `unified` / `unified <focus>`
- [x] Angular **Burndown** + **Unified** tabs on `/projects`

### Optional later
- Power BI semantic model on the same Postgres tables  
- Adaptive Cards for Teams sprint / unified briefs  

## Config sketch

```json
"Planner": {
  "Enabled": true,
  "SeedDemoData": true,
  "GroupIds": [],
  "PlanIds": []
}
```

## Success criteria (Phase 1)

1. Sync or seed fills `planner_tasks`.  
2. Dashboard shows counts: total / completed / in progress / delayed.  
3. `POST /api/planner/sync` refreshes data.  
4. Nav entry **Project Intelligence** under Explore.
