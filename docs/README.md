# Local document corpus

Drop RFP, proposal, business-case, and knowledge files here (`.docx`, `.pdf`, `.pptx`, `.xlsx`, `.txt`, `.md`).

BD Copilot indexes this folder when you choose **Local** on RFP / Business Case / Proposal generators, Knowledge Search, or Document Library.

- Default root: `BD_Copilot/docs` (this folder and subfolders)
- Excluded: `teams-app/` (Teams package scaffolding)
- Re-index: Document Library → **Index local docs**, or `POST /api/sync/local-docs`

You can also put files in subfolders (e.g. `docs/rfp/`, `docs/proposals/`).
