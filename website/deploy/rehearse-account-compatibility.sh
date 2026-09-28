#!/usr/bin/env bash
set -euo pipefail
app_root=${1:?Usage: rehearse-account-compatibility.sh APP_ROOT RELEASE_ID ARCHIVE}
release_id=${2:?Missing release ID}
archive=${3:?Missing archive}
[[ "$release_id" =~ ^[A-Za-z0-9][A-Za-z0-9._-]+$ ]] || exit 1
cd -- "$app_root"
umask 077
mkdir "releases/$release_id"
tar -xzf "$archive" -C "releases/$release_id"
test -s "releases/$release_id/Mu.Server.dll"
./backup.sh
snapshot=$(find backups -maxdepth 1 -name 'accounts-????????T??????Z.sqlite' -type f | sort | tail -1)
test -s "$snapshot"
rehearsal="$app_root/backups/rehearsal-$release_id"
mkdir "$rehearsal"
cp -p "$snapshot" "$rehearsal/input.sqlite"
docker run --rm --user 1000:1000 --read-only --tmpfs /tmp:size=64m \
  -v "$app_root/releases/$release_id:/app:ro" -v "$rehearsal:/check" -w /app \
  -e ASPNETCORE_ENVIRONMENT=Testing -e Database__Path=/check/input.sqlite -e DataProtection__Path=/check/keys \
  mcr.microsoft.com/dotnet/aspnet:10.0.12 dotnet Mu.Server.dll --backup /check/upgraded.sqlite
python3 - "$snapshot" "$rehearsal/upgraded.sqlite" <<'PY'
import sqlite3
import sys
from collections import Counter

before = sqlite3.connect('file:' + sys.argv[1] + '?mode=ro', uri=True)
after = sqlite3.connect('file:' + sys.argv[2] + '?mode=ro', uri=True)
assert before.execute('SELECT Version FROM DatabaseSchemas WHERE Id=1').fetchone()[0] == 2
assert after.execute('SELECT Version FROM DatabaseSchemas WHERE Id=1').fetchone()[0] == 3
assert after.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
assert not after.execute('PRAGMA foreign_key_check').fetchall()
for (table,) in before.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"):
    if table == 'DatabaseSchemas':
        continue
    query = 'SELECT * FROM "' + table.replace('"', '""') + '"'
    original = Counter(before.execute(query).fetchall())
    migrated = Counter(after.execute(query).fetchall())
    if table == 'FeatureDefinitions':
        assert original <= migrated
    else:
        assert original == migrated, table + ' changed'
    print(table + ': preserved')
assert after.execute('SELECT COUNT(*) FROM CompatibilityTests').fetchone()[0] == 0
print('Production backup migration rehearsal passed; live database remains schema 2.')
PY
