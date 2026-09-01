-- BD Copilot — PostgreSQL + pgvector schema
--
-- Run automatically by docker-compose (mounted into /docker-entrypoint-initdb.d), or manually:
--   psql "$ConnectionStrings__Postgres" -f database/init.sql
--
-- IMPORTANT: the vector(768) width below must match Ai:EmbeddingDimensions in appsettings.
-- 768 = Ollama's nomic-embed-text. If you switch production to Azure OpenAI's
-- text-embedding-3-large, either request 768-dimensional output via the API's "dimensions"
-- parameter (recommended, keeps one schema for both environments) or re-create this column
-- and re-index everything at the new width. Mixing dimensions in one column is not possible.

CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS documents (
    document_id          uuid PRIMARY KEY,
    file_name            varchar(512) NOT NULL,
    file_type            varchar(16)  NOT NULL,
    owner                varchar(256),
    created_date         timestamptz  NOT NULL,
    modified_date        timestamptz  NOT NULL,
    teams_channel        varchar(256) NOT NULL,
    graph_drive_id       varchar(512),
    graph_drive_item_id  varchar(512),
    share_point_url      varchar(2048) NOT NULL,
    acl_hash             varchar(128),
    index_status         varchar(32)  NOT NULL DEFAULT 'Pending',
    last_indexed_at      timestamptz
);

CREATE INDEX IF NOT EXISTS ix_documents_graph_drive_item_id ON documents (graph_drive_item_id);
CREATE INDEX IF NOT EXISTS ix_documents_graph_drive_id ON documents (graph_drive_id);
CREATE INDEX IF NOT EXISTS ix_documents_teams_channel ON documents (teams_channel);

CREATE TABLE IF NOT EXISTS document_chunks (
    id            uuid PRIMARY KEY,
    document_id   uuid NOT NULL REFERENCES documents (document_id) ON DELETE CASCADE,
    chunk_index   int  NOT NULL,
    content       text NOT NULL,
    locator       varchar(64),
    embedding     vector(768)
);

CREATE INDEX IF NOT EXISTS ix_document_chunks_document_id ON document_chunks (document_id);

-- Approximate nearest-neighbour index for cosine similarity search.
-- `lists` should be roughly sqrt(row_count); 100 is a sane pilot default (up to ~1M chunks).
-- Re-run `ANALYZE document_chunks;` after the first large bulk load so the planner picks it up.
CREATE INDEX IF NOT EXISTS ix_document_chunks_embedding
    ON document_chunks
    USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 100);

-- Per-site Graph delta cursor (Phase 1 pilot + Phase 4 multi-site).
CREATE TABLE IF NOT EXISTS sync_site_state (
    id                 uuid PRIMARY KEY,
    site_id            varchar(512) NOT NULL,
    site_display_name  varchar(512),
    delta_link         text,
    last_success_at    timestamptz,
    last_attempt_at    timestamptz,
    last_error         text,
    documents_indexed  int NOT NULL DEFAULT 0,
    is_enabled         boolean NOT NULL DEFAULT true
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_sync_site_state_site_id ON sync_site_state (site_id);

-- Quiet accept/edit/discard signal for future voice tuning (Phase 3).
CREATE TABLE IF NOT EXISTS generation_feedback (
    id               uuid PRIMARY KEY,
    generation_id    uuid NOT NULL,
    user_object_id   varchar(256) NOT NULL,
    action           varchar(32) NOT NULL,
    section_title    varchar(512),
    notes            text,
    created_at       timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_generation_feedback_generation_id ON generation_feedback (generation_id);

-- Token usage for cost attribution (Phase 4).
CREATE TABLE IF NOT EXISTS token_usage (
    id                  uuid PRIMARY KEY,
    user_object_id      varchar(256) NOT NULL,
    team_id             varchar(256),
    initiative          varchar(512),
    operation           varchar(32) NOT NULL,
    provider            varchar(64) NOT NULL,
    model               varchar(128) NOT NULL,
    prompt_tokens       int NOT NULL DEFAULT 0,
    completion_tokens   int NOT NULL DEFAULT 0,
    total_tokens        int NOT NULL DEFAULT 0,
    created_at          timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_token_usage_user_object_id ON token_usage (user_object_id);
CREATE INDEX IF NOT EXISTS ix_token_usage_created_at ON token_usage (created_at);

-- Document access audit trail (Phase 2+).
CREATE TABLE IF NOT EXISTS access_audit (
    id               uuid PRIMARY KEY,
    user_object_id   varchar(256) NOT NULL,
    document_id      uuid NOT NULL REFERENCES documents (document_id) ON DELETE CASCADE,
    allowed          boolean NOT NULL,
    reason           varchar(512) NOT NULL,
    created_at       timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_access_audit_user_object_id ON access_audit (user_object_id);
CREATE INDEX IF NOT EXISTS ix_access_audit_document_id ON access_audit (document_id);
CREATE INDEX IF NOT EXISTS ix_access_audit_created_at ON access_audit (created_at);

-- RFP generation history (section text in columns; not a Word blob).
CREATE TABLE IF NOT EXISTS rfp_documents (
    id                                uuid PRIMARY KEY,
    generation_id                     uuid NOT NULL,
    title                             varchar(512) NOT NULL,
    customer                          varchar(256) NOT NULL,
    tone                              varchar(64)  NOT NULL DEFAULT 'Formal',
    status                            varchar(32)  NOT NULL DEFAULT 'Draft',
    created_by_user_object_id         varchar(256) NOT NULL,
    created_by_display_name           varchar(256),
    created_at                        timestamptz  NOT NULL DEFAULT now(),
    updated_at                        timestamptz,
    executive_summary                 text,
    understanding_of_requirements     text,
    proposed_solution_architecture    text,
    security_compliance               text,
    delivery_timeline_team            text,
    commercials_pricing               text,
    channel_share_point_url           varchar(2048),
    channel_upload_status             varchar(64),
    channel_upload_error              text
);

CREATE INDEX IF NOT EXISTS ix_rfp_documents_created_by ON rfp_documents (created_by_user_object_id);
CREATE INDEX IF NOT EXISTS ix_rfp_documents_created_at ON rfp_documents (created_at);
CREATE INDEX IF NOT EXISTS ix_rfp_documents_generation_id ON rfp_documents (generation_id);

-- Business Case / Proposal generation history (sections as JSONB).
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

-- Microsoft Planner snapshots (Project Intelligence Phase 1)
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

-- Hangfire creates and owns its own "hangfire" schema automatically on first run
-- (see Program.cs UseHangfireServer / Hangfire.PostgreSql) — nothing to do here for it.
