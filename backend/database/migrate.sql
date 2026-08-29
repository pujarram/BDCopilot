-- BD Copilot — incremental schema upgrade for existing databases
-- Safe to re-run. Use when the DB was created before Phase 1–4 columns/tables existed.
--
--   psql "Host=localhost;Port=5432;Database=bdcopilot;Username=..." -f database/migrate.sql
-- Or via docker:
--   docker exec -i bd-copilot-postgres psql -U bdcopilot -d bdcopilot < backend/database/migrate.sql

CREATE EXTENSION IF NOT EXISTS vector;

ALTER TABLE documents ADD COLUMN IF NOT EXISTS graph_drive_id varchar(512);

CREATE INDEX IF NOT EXISTS ix_documents_graph_drive_id ON documents (graph_drive_id);
CREATE INDEX IF NOT EXISTS ix_documents_graph_drive_item_id ON documents (graph_drive_item_id);
CREATE INDEX IF NOT EXISTS ix_documents_teams_channel ON documents (teams_channel);

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

