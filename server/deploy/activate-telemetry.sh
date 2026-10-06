#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
release=${1:?Usage: activate-telemetry.sh RELEASE_ID REHEARSAL_DIRECTORY}
rehearsal=${2:?Usage: activate-telemetry.sh RELEASE_ID REHEARSAL_DIRECTORY}
[[ "$release" =~ ^[A-Za-z0-9][A-Za-z0-9._-]+$ ]]
test -s "releases/$release/Mu.Server.dll"
test -f "$rehearsal/verified"
umask 077
exec 9>>backups/.backup.lock
flock -x 9
previous=$(readlink current)
[[ "$previous" == "$(<"$rehearsal/baseline-release.txt")" ]]
actual_hash=$(sha256sum current/Mu.Server.dll); actual_hash=${actual_hash%% *}
[[ "$actual_hash" == "$(<"$rehearsal/baseline-dll.sha256")" ]]
running_hash=$(docker exec amd-dlss-mu-account sha256sum /app/Mu.Server.dll); running_hash=${running_hash%% *}
[[ "$running_hash" == "$actual_hash" ]]
candidate_hash=$(sha256sum "releases/$release/Mu.Server.dll"); candidate_hash=${candidate_hash%% *}
[[ "$candidate_hash" == "$(<"$rehearsal/candidate-dll.sha256")" ]]
stamp=$(date -u +%Y%m%dT%H%M%SZ)
cutover="backups/telemetry-cutover-$stamp"
mkdir "$cutover"
cp -p .env compose.yml "$cutover/"
printf '%s\n' "$previous" >"$cutover/previous-release.txt"
printf 'services:\n  account:\n    environment:\n      Maintenance__Enabled: "true"\n' >"$cutover/maintenance.yml"
printf 'services:\n  account:\n    environment:\n      Maintenance__Enabled: "false"\n' >"$cutover/reopen.yml"
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
      echo 'Shutdown unconfirmed; database left intact.' >&2
      exit "$result"
    fi
    if (( snapshot_ready == 1 )); then
      mkdir "$cutover/failed-data"
      for database in accounts mail-logs; do
        for suffix in '' '-wal' '-shm'; do
          if [[ -f "data/$database.sqlite$suffix" ]]; then
            mv "data/$database.sqlite$suffix" "$cutover/failed-data/"
          fi
        done
      done
      cp -p "$cutover/accounts.sqlite" data/accounts.sqlite
      cp -p "$cutover/accounts.mail.sqlite" data/mail-logs.sqlite
    fi
    ln -s "$previous" "current.rollback.$$"
    mv -Tf "current.rollback.$$" current
    docker compose up -d --force-recreate account || true
    echo 'Restored prior binary and final pre-upgrade databases.' >&2
  elif (( result != 0 )); then
    echo 'Verification failed after reopening; preserve new writes and fix forward.' >&2
  fi
  exit "$result"
}
trap rollback EXIT
stop_account
docker compose run --rm --no-deps account dotnet Mu.Server.dll --backup "/backups/telemetry-cutover-$stamp/accounts.sqlite"
test -s "$cutover/accounts.sqlite"
test -s "$cutover/accounts.mail.sqlite"
snapshot_ready=1
ln -s "releases/$release" "current.next.$$"
mv -Tf "current.next.$$" current
docker compose -f compose.yml -f "$cutover/maintenance.yml" up -d --force-recreate account
wait_healthy() {
  for attempt in {1..30}; do
    if curl --fail --silent --max-time 2 http://127.0.0.1:8089/api/health >"$cutover/health.json"; then return 0; fi
    sleep 1
  done
  return 1
}
wait_healthy
maintenance_status=$(curl --silent --max-time 8 -o /dev/null -w '%{http_code}' -H 'X-Forwarded-Proto: https' http://127.0.0.1:8089/api/v1/account/me)
[[ "$maintenance_status" == 503 ]]
python3 verify-telemetry-migration.py "$cutover/accounts.sqlite" data/accounts.sqlite
curl --fail --silent --max-time 8 -H 'X-Forwarded-Proto: https' http://127.0.0.1:8089/api/v1/compatibility/public/games >"$cutover/compatibility.json"
writes_opened=1
docker compose -f compose.yml -f "$cutover/reopen.yml" up -d --force-recreate account
wait_healthy
private_status=$(curl --silent --max-time 8 -o /dev/null -w '%{http_code}' -H 'X-Forwarded-Proto: https' http://127.0.0.1:8089/api/v1/account/me)
[[ "$private_status" == 401 ]]
printf 'Activated %s; final backups: %s/%s\n' "$release" "$PWD" "$cutover"
