import Foundation

public struct M001_Initial: Migration {
    public let version: Int32 = 1

    public init() {}

    public func apply(to db: SQLiteDatabase) throws {
        let sql = """
        CREATE TABLE apps (
          id            INTEGER PRIMARY KEY,
          canonical_id  INTEGER NULL REFERENCES apps(id),
          exe_path      TEXT NOT NULL COLLATE NOCASE UNIQUE,
          package_family TEXT NULL,
          display_name  TEXT NOT NULL,
          category      TEXT NOT NULL DEFAULT 'Other',
          state         TEXT NOT NULL DEFAULT 'tracked' CHECK (state IN ('tracked','retired')),
          added_utc     INTEGER NOT NULL,
          retired_utc   INTEGER NULL
        );

        CREATE TABLE sessions (
          id            INTEGER PRIMARY KEY,
          app_id        INTEGER NOT NULL REFERENCES apps(id),
          start_utc     INTEGER NOT NULL,
          end_utc       INTEGER NULL,
          last_seen_utc INTEGER NOT NULL,
          pid           INTEGER NULL,
          proc_start_utc INTEGER NULL,
          end_reason    TEXT NULL CHECK (end_reason IN ('exit','sleep','shutdown','crash','removed','restore'))
        );
        CREATE INDEX ix_sessions_app_start ON sessions(app_id, start_utc);
        CREATE UNIQUE INDEX ux_sessions_app_start ON sessions(app_id, start_utc);

        CREATE TABLE daily_usage (
          app_id  INTEGER NOT NULL REFERENCES apps(id),
          day     TEXT NOT NULL,
          seconds INTEGER NOT NULL,
          PRIMARY KEY (app_id, day)
        ) WITHOUT ROWID;

        CREATE TABLE icons    (app_id INTEGER PRIMARY KEY REFERENCES apps(id), png BLOB NOT NULL, hash TEXT NOT NULL);
        CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE goals    (app_id INTEGER NULL REFERENCES apps(id), kind TEXT NOT NULL CHECK (kind IN ('limit','target')),
                               seconds_per_day INTEGER NOT NULL, PRIMARY KEY (app_id, kind));
        CREATE TABLE ignored_suggestions (exe_path TEXT PRIMARY KEY COLLATE NOCASE, utc INTEGER NOT NULL);
        """
        try db.exec(sql)
    }
}
