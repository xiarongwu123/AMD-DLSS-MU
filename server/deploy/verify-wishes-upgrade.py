#!/usr/bin/env python3
"""Verify an offline rehearsal preserves all pre-existing SQLite rows."""
import hashlib
import json
import sqlite3
import sys
from pathlib import Path


def connect(path):
    return sqlite3.connect(Path(path).resolve().as_uri() + "?mode=ro", uri=True)


def digest(db, table):
    quoted = '"' + table.replace('"', '""') + '"'
    rows = [json.dumps(row, ensure_ascii=True, separators=(",", ":"),
                       default=lambda value: {"bytes": value.hex()})
            for row in db.execute("SELECT * FROM " + quoted)]
    return len(rows), hashlib.sha256("\n".join(sorted(rows)).encode()).hexdigest()


with connect(sys.argv[1]) as before, connect(sys.argv[2]) as after:
    assert after.execute("PRAGMA integrity_check").fetchone()[0] == "ok"
    assert not after.execute("PRAGMA foreign_key_check").fetchall()
    tables = [row[0] for row in before.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")]
    for table in tables:
        assert digest(before, table) == digest(after, table), "Changed historical rows: " + table
    assert after.execute("SELECT Version FROM DatabaseSchemas WHERE Id=1").fetchone()[0] == 4
    assert after.execute("SELECT COUNT(*) FROM Wishes WHERE IsDemo=1").fetchone()[0] == 8
    assert after.execute("SELECT COUNT(*) FROM WishVotes").fetchone()[0] == 0
    assert after.execute("SELECT COUNT(*) FROM WishComments").fetchone()[0] == 0
    print("Wish upgrade verified: integrity ok; all rows in", len(tables),
          "historical tables unchanged; schema 4; eight labelled demo wishes.")
