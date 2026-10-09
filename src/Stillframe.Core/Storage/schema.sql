-- Actual v0.1 runtime schema. The 13-table docs/database-schema.sql remains a design candidate.
PRAGMA journal_mode=WAL;
CREATE TABLE IF NOT EXISTS sources(id TEXT PRIMARY KEY,name TEXT NOT NULL,kind TEXT NOT NULL,address TEXT NOT NULL,enabled INTEGER NOT NULL DEFAULT 1);
CREATE TABLE IF NOT EXISTS items(
 id TEXT PRIMARY KEY,title TEXT NOT NULL,source_id TEXT NOT NULL REFERENCES sources(id),source_name TEXT NOT NULL,
 original_url TEXT,original_file TEXT,thumbnail_file TEXT,sha256 TEXT,width INTEGER NOT NULL DEFAULT 0,height INTEGER NOT NULL DEFAULT 0,
 author TEXT,source_page TEXT,favorite INTEGER NOT NULL DEFAULT 0,added_at TEXT NOT NULL,
 license_name TEXT,license_url TEXT,preview_url TEXT,
 UNIQUE(source_id,original_url));
CREATE INDEX IF NOT EXISTS ix_items_hash ON items(sha256);
CREATE TABLE IF NOT EXISTS daily(local_date TEXT PRIMARY KEY,item_id TEXT NOT NULL REFERENCES items(id));
CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
INSERT OR IGNORE INTO sources VALUES('local','本地图库','local','',1);
PRAGMA user_version=2;
