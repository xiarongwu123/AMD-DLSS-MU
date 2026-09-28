#!/usr/bin/env bash
set -euo pipefail

app_root=${1:?Usage: activate-account-compatibility.sh APP_ROOT RELEASE_ID}
release_id=${2:?Usage: activate-account-compatibility.sh APP_ROOT RELEASE_ID}
[[ "$release_id" =~ ^[A-Za-z0-9][A-Za-z0-9._-]+$ ]] || exit 1
cd -- "$app_root"
test -s "releases/$release_id/Mu.Server.dll"
test -s data/accounts.sqlite
test -L current
umask 077
mkdir -p backups
exec 9>backups/.backup.lock
flock -x 9
previous=$(readlink current)
stamp=$(date -u +%Y%m%dT%H%M%SZ)
snapshot="backups/accounts-before-compatibility-${stamp}.sqlite"
override="backups/maintenance-${stamp}.yml"
printf 'services:\n  account:\n    environment:\n      Maintenance__Enabled: "true"\n' > "$override"
snapshot_ready=0
writes_opened=0

stop_account() {
  docker compose stop account
  [[ "$(docker inspect --format '{{.State.Running}}' amd-dlss-mu-account)" == false ]]
}

rollback() {
  result=$?
  trap - EXIT
  if (( result != 0 && writes_opened == 0 )); then
    if ! stop_account; then
      printf 'Could not confirm account shutdown; database files left intact.\n' >&2
      exit "$result"
    fi
    if (( snapshot_ready == 1 )); then
      mkdir "backups/failed-compatibility-${stamp}"
      for suffix in '' '-wal' '-shm'; do
        if [[ -f "data/accounts.sqlite${suffix}" ]]; then
          cp -- "data/accounts.sqlite${suffix}" "backups/failed-compatibility-${stamp}/accounts.sqlite${suffix}"
        fi
      done
      # The maintenance gate excludes all writes after this final offline snapshot.
      cp -p -- "$snapshot" data/accounts.rollback.sqlite
      chmod 600 data/accounts.rollback.sqlite
      rm -f -- data/accounts.sqlite-wal data/accounts.sqlite-shm
      mv -f -- data/accounts.rollback.sqlite data/accounts.sqlite
    fi
    ln -s "$previous" "current.rollback.$$"
    mv -Tf "current.rollback.$$" current
    docker compose up -d --force-recreate account || true
    if (( snapshot_ready == 1 )); then
      printf 'Activation failed; restored prior binary and final offline database snapshot.\n' >&2
    else
      printf 'Activation failed before final snapshot; restarted prior binary with original database.\n' >&2
    fi
  elif (( result != 0 )); then
    printf 'Activation verification failed after reopening writes; database preserved for investigation.\n' >&2
  fi
  exit "$result"
}
trap rollback EXIT

stop_account
docker compose run --rm --no-deps account dotnet Mu.Server.dll --backup "/backups/$(basename "$snapshot")"
test -s "$snapshot"
snapshot_ready=1
ln -s "releases/$release_id" "current.next.$$"
mv -Tf "current.next.$$" current
docker compose -f compose.yml -f "$override" up -d --force-recreate account

healthy=0
for attempt in {1..30}; do
  if curl --fail --silent --max-time 2 http://127.0.0.1:8089/api/health >/dev/null; then healthy=1; break; fi
  sleep 1
done
(( healthy == 1 ))
maintenance_status=$(curl --silent --max-time 8 -o /dev/null -w '%{http_code}' -H 'X-Forwarded-Proto: https' http://127.0.0.1:8089/api/v1/account/me)
[[ "$maintenance_status" == 503 ]]
curl --fail --silent --max-time 8 -H 'X-Forwarded-Proto: https' http://127.0.0.1:8089/api/v1/compatibility/public/games > "backups/compatibility-check-${stamp}.json"

python3 - "$snapshot" data/accounts.sqlite "backups/compatibility-check-${stamp}.json" <<'PY'
import json
import sqlite3
import sys
from collections import Counter

before = sqlite3.connect('file:' + sys.argv[1] + '?mode=ro', uri=True)
after = sqlite3.connect('file:' + sys.argv[2] + '?mode=ro', uri=True)
assert after.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
assert before.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
assert not after.execute('PRAGMA foreign_key_check').fetchall()
assert before.execute('SELECT Version FROM DatabaseSchemas WHERE Id=1').fetchone()[0] == 2
assert after.execute('SELECT Version FROM DatabaseSchemas WHERE Id=1').fetchone()[0] == 3
tables = before.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'").fetchall()
for (table,) in tables:
    if table in ('DatabaseSchemas', 'FeatureDefinitions'):
        continue
    query = 'SELECT * FROM "' + table.replace('"', '""') + '"'
    old = before.execute(query).fetchall()
    current = after.execute(query).fetchall()
    assert Counter(old) == Counter(current), table + ' changed during migration'
    print(table + ': preserved ' + str(len(current)) + ' rows')
for row in before.execute('SELECT * FROM FeatureDefinitions ORDER BY Key'):
    assert after.execute('SELECT * FROM FeatureDefinitions WHERE Key=?', (row[0],)).fetchone() == row
with open(sys.argv[3], encoding='utf8') as source:
    games = json.load(source)['items']
assert len(games) >= 4
assert after.execute('SELECT COUNT(*) FROM CompatibilityTests').fetchone()[0] == 0
assert all(game['counts']['total'] == 0 and game['status'] == 'untested' for game in games)
print('Schema 3, original feature rules, catalog and zero real reports verified.')
PY

# Once writes reopen, never restore an older database automatically.
reopen="backups/reopen-${stamp}.yml"
printf 'services:\n  account:\n    environment:\n      Maintenance__Enabled: "false"\n' > "$reopen"
writes_opened=1
docker compose -f compose.yml -f "$reopen" up -d --force-recreate account
healthy=0
for attempt in {1..30}; do
  if curl --fail --silent --max-time 2 http://127.0.0.1:8089/api/health >/dev/null; then healthy=1; break; fi
  sleep 1
done
(( healthy == 1 ))
private_status=$(curl --silent --max-time 8 -o /dev/null -w '%{http_code}' -H 'X-Forwarded-Proto: https' http://127.0.0.1:8089/api/v1/account/me)
[[ "$private_status" == 401 ]]
printf 'Activated %s; final schema-2 backup: %s/%s\n' "$release_id" "$app_root" "$snapshot"
