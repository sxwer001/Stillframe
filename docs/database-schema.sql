-- 拾景静态壁纸应用：候选 v1 数据库结构（设计稿，尚未接入应用）
-- UTC 使用 ISO 8601 文本；local_date 为 YYYY-MM-DD；路径仅指应用管理的文件。
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
BEGIN IMMEDIATE;
CREATE TABLE sources (
  id TEXT PRIMARY KEY, name TEXT NOT NULL, provider_kind TEXT NOT NULL,
  config_json TEXT NOT NULL DEFAULT '{}', enabled INTEGER NOT NULL DEFAULT 1 CHECK(enabled IN (0,1)),
  deleted_at TEXT, last_sync_at TEXT, last_error_code TEXT, created_at TEXT NOT NULL
);
CREATE UNIQUE INDEX ux_sources_name ON sources(name COLLATE NOCASE) WHERE deleted_at IS NULL;
CREATE TABLE wallpapers (
  id TEXT PRIMARY KEY, source_id TEXT NOT NULL REFERENCES sources(id), remote_id TEXT NOT NULL,
  title TEXT NOT NULL, author TEXT, attribution_url TEXT, source_page_url TEXT,
  license_name TEXT, license_url TEXT, rights_status TEXT NOT NULL DEFAULT 'unknown',
  tags_json TEXT NOT NULL DEFAULT '[]', published_at TEXT, fetched_at TEXT NOT NULL,
  original_width INTEGER CHECK(original_width > 0), original_height INTEGER CHECK(original_height > 0),
  metadata_json TEXT NOT NULL DEFAULT '{}', hidden INTEGER NOT NULL DEFAULT 0 CHECK(hidden IN (0,1)),
  UNIQUE(source_id, remote_id)
);
CREATE INDEX ix_wallpapers_source_date ON wallpapers(source_id, published_at DESC, id);
CREATE TABLE variants (
  id TEXT PRIMARY KEY, wallpaper_id TEXT NOT NULL REFERENCES wallpapers(id),
  kind TEXT NOT NULL CHECK(kind IN ('original','preview')), remote_uri TEXT, local_source_path TEXT,
  width INTEGER, height INTEGER, mime_type TEXT, expires_at TEXT,
  UNIQUE(wallpaper_id, kind)
);
CREATE TABLE assets (
  id TEXT PRIMARY KEY, sha256 TEXT NOT NULL, relative_path TEXT NOT NULL UNIQUE,
  kind TEXT NOT NULL CHECK(kind IN ('original','thumbnail','applied')),
  bytes INTEGER NOT NULL CHECK(bytes >= 0), width INTEGER NOT NULL CHECK(width > 0),
  height INTEGER NOT NULL CHECK(height > 0), mime_type TEXT NOT NULL,
  state TEXT NOT NULL DEFAULT 'ready' CHECK(state IN ('ready','missing','corrupt')),
  created_at TEXT NOT NULL, last_access_at TEXT NOT NULL,
  UNIQUE(sha256,kind)
);
CREATE TABLE wallpaper_assets (
  wallpaper_id TEXT NOT NULL REFERENCES wallpapers(id), asset_id TEXT NOT NULL REFERENCES assets(id),
  role TEXT NOT NULL, PRIMARY KEY(wallpaper_id,asset_id,role)
);
CREATE TABLE favorites (
  wallpaper_id TEXT PRIMARY KEY REFERENCES wallpapers(id), created_at TEXT NOT NULL
);
CREATE TABLE daily_selections (
  profile_id TEXT NOT NULL, local_date TEXT NOT NULL, wallpaper_id TEXT NOT NULL REFERENCES wallpapers(id),
  revision INTEGER NOT NULL DEFAULT 1 CHECK(revision >= 1),
  reason TEXT NOT NULL CHECK(reason IN ('daily','manual_next','offline_fallback')),
  time_zone_id TEXT NOT NULL, selected_at TEXT NOT NULL, source_published_at TEXT,
  PRIMARY KEY(profile_id,local_date)
);
CREATE TABLE download_jobs (
  id TEXT PRIMARY KEY, wallpaper_id TEXT NOT NULL REFERENCES wallpapers(id), variant_id TEXT NOT NULL REFERENCES variants(id),
  state TEXT NOT NULL CHECK(state IN ('queued','resolving','transferring','verifying','committing','completed','cancelled','failed')),
  received_bytes INTEGER NOT NULL DEFAULT 0, total_bytes INTEGER, attempt_count INTEGER NOT NULL DEFAULT 0,
  temp_relative_path TEXT, result_asset_id TEXT REFERENCES assets(id), error_code TEXT,
  created_at TEXT NOT NULL, updated_at TEXT NOT NULL
);
CREATE UNIQUE INDEX ux_active_download ON download_jobs(variant_id)
  WHERE state IN ('queued','resolving','transferring','verifying','committing');
CREATE TABLE export_records (
  id TEXT PRIMARY KEY, asset_id TEXT NOT NULL REFERENCES assets(id),
  destination_path TEXT NOT NULL, exported_at TEXT NOT NULL
);
CREATE TABLE apply_runs (
  id TEXT PRIMARY KEY, wallpaper_id TEXT NOT NULL REFERENCES wallpapers(id),
  asset_id TEXT REFERENCES assets(id), requested_position TEXT NOT NULL, previous_position TEXT,
  trigger_kind TEXT NOT NULL CHECK(trigger_kind IN ('manual','automatic','restore')),
  state TEXT NOT NULL CHECK(state IN ('preparing','applying','succeeded','partial_failed','failed','cancelled')),
  error_code TEXT, started_at TEXT NOT NULL, completed_at TEXT
);
CREATE TABLE apply_targets (
  run_id TEXT NOT NULL REFERENCES apply_runs(id), monitor_device_path TEXT NOT NULL,
  previous_file_path TEXT, applied_asset_id TEXT REFERENCES assets(id),
  outcome TEXT NOT NULL CHECK(outcome IN ('pending','succeeded','failed','restored','restore_failed','disconnected')),
  error_code TEXT, PRIMARY KEY(run_id,monitor_device_path)
);
CREATE TABLE asset_pins (
  asset_id TEXT NOT NULL REFERENCES assets(id), owner_kind TEXT NOT NULL,
  owner_key TEXT NOT NULL, created_at TEXT NOT NULL,
  PRIMARY KEY(asset_id,owner_kind,owner_key)
);
CREATE TABLE app_settings (
  key TEXT PRIMARY KEY, value_json TEXT NOT NULL, updated_at TEXT NOT NULL
);
PRAGMA user_version = 1;
COMMIT;
-- 生产实现必须由迁移器检查 user_version 后执行，禁止每次启动直接重跑本文件。
-- 数据库与文件系统非原子：启动时通过 reconciliation 修复中断下载/孤立文件。
