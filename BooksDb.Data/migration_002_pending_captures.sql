-- Migration 002: phone capture staging (Helper companion).
-- Applies to existing DBs (user_version 1 -> 2) and as the second step on fresh installs.

CREATE TABLE IF NOT EXISTS PendingCaptures (
                                               CaptureId   INTEGER PRIMARY KEY,
                                               SessionId   TEXT,
                                               RawIsbn     TEXT,
                                               Isbn13      TEXT,
                                               Shelf       TEXT,
                                               CapturedAt  TEXT,
                                               ReceivedAt  TEXT NOT NULL,
                                               Status      TEXT NOT NULL DEFAULT 'pending'
                                               CHECK (Status IN ('pending','committed','discarded'))
    );

CREATE TABLE IF NOT EXISTS PendingCapturePhotos (
                                                    PhotoId    INTEGER PRIMARY KEY,
                                                    CaptureId  INTEGER NOT NULL REFERENCES PendingCaptures(CaptureId) ON DELETE CASCADE,
    Path       TEXT NOT NULL,
    Ordinal    INTEGER NOT NULL DEFAULT 0   -- 0 = primary/front, then 1, 2, ...
    );

CREATE INDEX IF NOT EXISTS ix_pendingcaptures_status       ON PendingCaptures(Status);
CREATE INDEX IF NOT EXISTS ix_pendingcapturephotos_capture ON PendingCapturePhotos(CaptureId);

PRAGMA user_version = 2;
