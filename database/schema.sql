-- INSTAGERAM database schema - reference of the FINAL state (schema version 2).
--
-- IMPORTANT
--   This file is a human-readable mirror of Core\SchemaMigrator.cs, which is
--   what the application actually executes. The migrator applies versioned,
--   additive steps stored in SQLite's own "PRAGMA user_version", so an
--   existing database is upgraded in place and never recreated.
--
--   Verified reality (2026-09-29):
--     * projects.instagram_url has NO unique index on purpose - older data
--       contains duplicate URLs, so a UNIQUE constraint would fail to apply.
--     * campaign_countries / campaign_statistics / profile_statistics /
--       reports have no FOREIGN KEY clauses, because they were added to a
--       database that already existed (SQLite cannot add constraints later).
--       Referential integrity for these is enforced in application code.
--
-- Version history
--   v1 - projects, campaigns (with instagram_url and countries)
--   v2 - countries, campaign_countries, campaign_statistics,
--        profile_statistics, reports, app_settings, logs
--        + campaigns.end_date / created_at / updated_at / notes
--        + projects.updated_at / status / notes

PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;

-- ------------------------------------------------------------------ v1 ----
CREATE TABLE IF NOT EXISTS projects (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    project_name       TEXT NOT NULL,
    instagram_username TEXT NOT NULL,
    instagram_url      TEXT NOT NULL,
    created_at         TEXT NOT NULL,
    updated_at         TEXT,
    status             TEXT NOT NULL DEFAULT 'Active',
    notes              TEXT
);

CREATE TABLE IF NOT EXISTS campaigns (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id     INTEGER NOT NULL,
    campaign_name  TEXT NOT NULL,
    countries      TEXT NOT NULL DEFAULT '',
    target_number  INTEGER NOT NULL DEFAULT 0,
    current_number INTEGER NOT NULL DEFAULT 0,
    status         TEXT NOT NULL DEFAULT 'Draft',
    start_date     TEXT NOT NULL DEFAULT '',
    instagram_url  TEXT NOT NULL DEFAULT '',
    created_at     TEXT,
    updated_at     TEXT,
    end_date       TEXT,
    notes          TEXT,
    FOREIGN KEY(project_id) REFERENCES projects(id)
);

-- ------------------------------------------------------------------ v2 ----
CREATE TABLE IF NOT EXISTS countries (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    country_code     TEXT NOT NULL UNIQUE,
    country_name     TEXT NOT NULL,
    name_fa          TEXT,
    language         TEXT,
    timezone         TEXT,
    region           TEXT,
    is_target_market INTEGER NOT NULL DEFAULT 0,
    priority         INTEGER NOT NULL DEFAULT 999,
    enabled          INTEGER NOT NULL DEFAULT 1,
    excluded         INTEGER NOT NULL DEFAULT 0,
    updated_at       TEXT
);

CREATE TABLE IF NOT EXISTS campaign_countries (
    campaign_id INTEGER NOT NULL,
    country_id  INTEGER NOT NULL,
    is_excluded INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY(campaign_id, country_id)
);

CREATE TABLE IF NOT EXISTS campaign_statistics (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    campaign_id     INTEGER NOT NULL,
    date            TEXT NOT NULL,
    followers       INTEGER NOT NULL DEFAULT 0,
    engagement_rate REAL NOT NULL DEFAULT 0,
    growth          INTEGER NOT NULL DEFAULT 0,
    country         TEXT,
    notes           TEXT
);

CREATE TABLE IF NOT EXISTS profile_statistics (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id      INTEGER NOT NULL,
    date            TEXT NOT NULL,
    followers       INTEGER NOT NULL DEFAULT 0,
    following       INTEGER NOT NULL DEFAULT 0,
    post_count      INTEGER NOT NULL DEFAULT 0,
    engagement_rate REAL NOT NULL DEFAULT 0,
    notes           TEXT
);

CREATE TABLE IF NOT EXISTS reports (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    campaign_id   INTEGER,
    report_name   TEXT NOT NULL,
    report_format TEXT NOT NULL,
    file_path     TEXT NOT NULL,
    generated_at  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS app_settings (
    setting_key   TEXT PRIMARY KEY,
    setting_value TEXT,
    updated_at    TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS logs (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    logged_at TEXT NOT NULL,
    level     TEXT NOT NULL,
    module    TEXT,
    message   TEXT NOT NULL,
    details   TEXT
);

CREATE INDEX IF NOT EXISTS idx_campaigns_project_id
    ON campaigns(project_id);

CREATE INDEX IF NOT EXISTS idx_campaign_statistics_campaign_date
    ON campaign_statistics(campaign_id, date);

CREATE INDEX IF NOT EXISTS idx_profile_statistics_project_date
    ON profile_statistics(project_id, date);

CREATE INDEX IF NOT EXISTS idx_logs_logged_at
    ON logs(logged_at);
