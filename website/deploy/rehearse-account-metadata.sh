#!/usr/bin/env bash
set -euo pipefail
app_root=${1:?Usage: rehearse-account-metadata.sh APP_ROOT RELEASE_ID ARCHIVE}
release_id=${2:?Missing release ID}
archive=${3:?Missing archive}
[[ "$release_id" =~ ^[A-Za-z0-9][A-Za-z0-9._-]+$ ]] || exit 1
for command in docker sha256sum curl jq flock python3 tar realpath; do command -v "$command" >/dev/null; done
archive=$(realpath -- "$archive")
app_root=$(cd -- "$app_root" && pwd -P)
cd -- "$app_root"
umask 077
mkdir "releases/$release_id"
tar -xzf "$archive" -C "releases/$release_id"
test -s "releases/$release_id/Mu.Server.dll"
mkdir -p backups
exec 9>backups/.backup.lock
flock -n 9 || { printf '%s\n' 'Another account backup or activation is running.' >&2; exit 1; }
test -L current
previous=$(readlink current)
[[ "$previous" =~ ^releases/[A-Za-z0-9][A-Za-z0-9._-]+$ ]]
baseline_hash=$(sha256sum current/Mu.Server.dll); baseline_hash=${baseline_hash%% *}
running_hash=$(docker exec amd-dlss-mu-account sha256sum /app/Mu.Server.dll); running_hash=${running_hash%% *}
[[ "$baseline_hash" == "$running_hash" ]] || { printf '%s\n' 'Running account binary differs from current; refusing stale rehearsal.' >&2; exit 1; }
./backup.sh --lock-held
snapshot=$(find backups -maxdepth 1 -name 'accounts-????????T??????Z.sqlite' -type f | sort | tail -1)
test -s "$snapshot"
rehearsal="$app_root/backups/rehearsal-$release_id"
mkdir "$rehearsal"
cp -p "$snapshot" "$rehearsal/input.sqlite"
cp -p "$snapshot" "$rehearsal/original.sqlite"
printf '%s\n' "$previous" > "$rehearsal/baseline-release.txt"
printf '%s\n' "$baseline_hash" > "$rehearsal/baseline-dll.sha256"
sha256sum "$snapshot" > "$rehearsal/source-snapshot.sha256"
sha256sum "$archive" > "$rehearsal/archive.sha256"
flock -u 9
exec 9>&-
docker run --rm --user 1000:1000 --read-only --tmpfs /tmp:size=64m \
  -v "$app_root/releases/$release_id:/app:ro" -v "$rehearsal:/check" -w /app \
  -e ASPNETCORE_ENVIRONMENT=Testing -e Database__Path=/check/input.sqlite -e DataProtection__Path=/check/keys \
  mcr.microsoft.com/dotnet/aspnet:10.0.12 dotnet Mu.Server.dll --backup /check/upgraded.sqlite
container="mu-metadata-rehearsal-$release_id"
container_id=
cleanup() {
  if [[ -n "$container_id" ]]; then
    docker stop --time 10 "$container_id" >/dev/null 2>&1 || true
    docker rm -f "$container_id" >/dev/null 2>&1 || true
    container_id=
  fi
}
if docker container inspect "$container" >/dev/null 2>&1; then
  printf 'Candidate container already exists; left untouched: %s\n' "$container" >&2
  exit 1
fi
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
# Capture ownership before start: a port/start failure must only remove this container.
container_id=$(docker create --name "$container" --user 1000:1000 --read-only --tmpfs /tmp:size=64m \
  -p 127.0.0.1:18089:8080 -v "$app_root/releases/$release_id:/app:ro" -v "$rehearsal:/check" -w /app \
  -e ASPNETCORE_ENVIRONMENT=Testing -e ASPNETCORE_URLS=http://0.0.0.0:8080 \
  -e Database__Path=/check/upgraded.sqlite -e DataProtection__Path=/check/keys -e Maintenance__Enabled=true \
  mcr.microsoft.com/dotnet/aspnet:10.0.12 dotnet Mu.Server.dll)
docker start "$container_id" >/dev/null
healthy=0
for attempt in {1..30}; do
  if curl --fail --silent --max-time 2 http://127.0.0.1:18089/health | jq -e '.status == "ok" and .service == "mu-accounts"' >/dev/null; then healthy=1; break; fi
  sleep 1
done
(( healthy == 1 ))
for route in /api/v1/account/me /api/v1/auth/refresh /admin; do
  status=$(curl --silent --show-error --max-time 5 -o "$rehearsal/maintenance.json" -w '%{http_code}' "http://127.0.0.1:18089$route")
  [[ "$status" == 503 ]]
  jq -e '.code == "service_maintenance" and .retryAfterSeconds == 30' "$rehearsal/maintenance.json" >/dev/null
done
status=$(curl --silent --show-error --max-time 5 -X POST -H 'Content-Type: application/json' --data '{}' \
  -o "$rehearsal/maintenance.json" -w '%{http_code}' http://127.0.0.1:18089/api/v1/auth/refresh)
[[ "$status" == 503 ]]
jq -e '.code == "service_maintenance" and .retryAfterSeconds == 30' "$rehearsal/maintenance.json" >/dev/null
curl --fail --silent --show-error --max-time 15 http://127.0.0.1:18089/api/v1/compatibility/public/games > "$rehearsal/games.json"
jq -e '.catalogTotal >= 2000 and .total == .catalogTotal and .coverage.requirements > 1000
  and .coverage.requirements <= .catalogTotal and .coverage.modCompatibility == 239
  and (.items | length > 0 and all(.[]; (.evidence.hasRequirements | type) == "boolean"))' "$rehearsal/games.json" >/dev/null
for game in cyberpunk-2077 black-myth-wukong gta-v-enhanced; do
  curl --fail --silent --show-error --max-time 15 "http://127.0.0.1:18089/api/v1/compatibility/public/games/$game" > "$rehearsal/$game.json"
  jq -e --arg game "$game" '.game.id == $game and .requirements.status == "available" and (.requirements.minimum.graphics | length > 0)
    and (.requirements.sourceSha256 | test("^[a-f0-9]{64}$"))
    and (.modCompatibility | length > 0 and all(.[]; .muVerified == false and (.sourceCommit | test("^[a-f0-9]{40}$"))))' "$rehearsal/$game.json" >/dev/null
done
curl --fail --silent --show-error --max-time 15 http://127.0.0.1:18089/api/v1/compatibility/public/games/gta-v-legacy > "$rehearsal/gta-v-legacy.json"
jq -e '.game.steamAppId == "271590" and .requirements.status == "available"' "$rehearsal/gta-v-legacy.json" >/dev/null
curl --fail --silent --show-error --max-time 15 'http://127.0.0.1:18089/api/v1/compatibility/public/games/cyberpunk-2077?gpu=unmatched-rehearsal-gpu' > "$rehearsal/unknown-gpu.json"
jq -e '.counts.total == 0 and .status == "untested" and .tests == [] and .requirements.status == "available"' "$rehearsal/unknown-gpu.json" >/dev/null
cleanup
trap - EXIT INT TERM
python3 - "$rehearsal/original.sqlite" "$rehearsal/upgraded.sqlite" <<'PY'
import sqlite3
import sys
from collections import Counter

before = sqlite3.connect('file:' + sys.argv[1] + '?mode=ro', uri=True)
after = sqlite3.connect('file:' + sys.argv[2] + '?mode=ro', uri=True)
assert before.execute('SELECT Version FROM DatabaseSchemas WHERE Id=1').fetchone()[0] == 3
assert after.execute('SELECT Version FROM DatabaseSchemas WHERE Id=1').fetchone()[0] == 3
assert before.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
assert after.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
assert not before.execute('PRAGMA foreign_key_check').fetchall()
assert not after.execute('PRAGMA foreign_key_check').fetchall()
tables = before.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'").fetchall()
assert set(tables) == set(after.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'").fetchall())
for (table,) in tables:
    assert before.execute('PRAGMA table_info("' + table.replace('"', '""') + '")').fetchall() == after.execute('PRAGMA table_info("' + table.replace('"', '""') + '")').fetchall()
    query = 'SELECT * FROM "' + table.replace('"', '""') + '"'
    assert Counter(before.execute(query).fetchall()) == Counter(after.execute(query).fetchall()), table + ' changed'
    print(table + ': preserved ' + str(after.execute('SELECT COUNT(*) FROM "' + table.replace('"', '""') + '"').fetchone()[0]) + ' rows')
print(f'All {len(tables)} tables preserved; schema 3 unchanged, metadata runtime verified.')
PY
exec 9>backups/.backup.lock
flock -n 9 || { printf '%s\n' 'Account activation in progress; recheck rehearsal baseline before activation.' >&2; exit 1; }
[[ "$(readlink current)" == "$previous" ]] || { printf '%s\n' 'Account current changed during rehearsal; do not activate against a stale baseline.' >&2; exit 1; }
current_hash=$(sha256sum current/Mu.Server.dll); current_hash=${current_hash%% *}
[[ "$current_hash" == "$baseline_hash" ]] || { printf '%s\n' 'Account baseline binary changed during rehearsal.' >&2; exit 1; }
printf 'Rehearsal passed; no production activation. Snapshot: %s\n' "$snapshot"
printf 'Activation baseline: %s; SHA-256: %s\n' "$previous" "$baseline_hash"
