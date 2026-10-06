#!/usr/bin/env python3
"""Compare schema-3 history with a schema-4 migration without logging user data."""
import sqlite3
import sys
from collections import Counter

before = sqlite3.connect('file:' + sys.argv[1] + '?mode=ro', uri=True)
after = sqlite3.connect('file:' + sys.argv[2] + '?mode=ro', uri=True)
for connection in (before, after):
    assert connection.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
    assert not connection.execute('PRAGMA foreign_key_check').fetchall()
assert before.execute('SELECT Version FROM DatabaseSchemas WHERE Id=1').fetchone()[0] == 3
assert after.execute('SELECT Version FROM DatabaseSchemas WHERE Id=1').fetchone()[0] == 4
tables = before.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'").fetchall()
for (table,) in tables:
    if table == 'DatabaseSchemas':
        continue
    query = 'SELECT * FROM "' + table.replace('"', '""') + '"'
    original = before.execute(query).fetchall()
    migrated = after.execute(query).fetchall()
    assert Counter(original) == Counter(migrated), table + ' changed during migration'
    print(table + ': preserved ' + str(len(original)) + ' rows')
assert after.execute('SELECT COUNT(*) FROM GameTelemetry').fetchone()[0] == 0
print('Schema 3 -> 4, all historical rows, integrity and empty telemetry verified.')
