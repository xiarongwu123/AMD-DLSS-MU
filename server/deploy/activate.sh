#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
release=${1:?Usage: activate.sh RELEASE_ID [--expected-baseline REHEARSAL_DIRECTORY]}
shift
expected_baseline=
if (( $# != 0 )); then
  [[ $# == 2 && "$1" == --expected-baseline && -n "$2" ]] || {
    echo 'Usage: activate.sh RELEASE_ID [--expected-baseline REHEARSAL_DIRECTORY]' >&2
    exit 1
  }
  expected_baseline=$2
fi
[[ "$release" =~ ^[A-Za-z0-9][A-Za-z0-9._-]+$ ]] || { echo 'Invalid release ID' >&2; exit 1; }
test -s "releases/$release/Mu.Server.dll"
test -f .env
if [[ -n "$expected_baseline" ]]; then
  # A completed rehearsal already created these paths. Do not create them on rejection.
  test -d data && test -d backups && test -f backups/.backup.lock
else
  mkdir -p data backups
fi
# Keep the same lock through backup, replacement, health check, and rollback.
exec 9>>backups/.backup.lock
flock -x 9
previous=$(readlink current || true)
if [[ -n "$expected_baseline" ]]; then
  test -s "$expected_baseline/baseline-release.txt"
  test -s "$expected_baseline/baseline-dll.sha256"
  expected_release=$(<"$expected_baseline/baseline-release.txt")
  expected_hash=$(<"$expected_baseline/baseline-dll.sha256")
  [[ "$expected_release" =~ ^releases/[A-Za-z0-9][A-Za-z0-9._-]+$ && "$expected_hash" =~ ^[a-f0-9]{64}$ ]] || {
    echo 'Invalid rehearsal baseline; no activation performed.' >&2
    exit 1
  }
  [[ "$previous" == "$expected_release" ]] || {
    echo 'Account release changed since rehearsal; no activation performed.' >&2
    exit 1
  }
  actual_hash=$(sha256sum current/Mu.Server.dll); actual_hash=${actual_hash%% *}
  [[ "$actual_hash" == "$expected_hash" ]] || {
    echo 'Account binary changed since rehearsal; no activation performed.' >&2
    exit 1
  }
fi
account_running=0
if docker compose ps --status running --services | grep -qx account; then account_running=1; fi
if [[ -n "$expected_baseline" ]]; then
  (( account_running == 1 )) || { echo 'Rehearsed account service is not running; no activation performed.' >&2; exit 1; }
  running_hash=$(docker exec amd-dlss-mu-account sha256sum /app/Mu.Server.dll); running_hash=${running_hash%% *}
  [[ "$running_hash" == "$expected_hash" ]] || {
    echo 'Running account binary differs from rehearsal; no activation performed.' >&2
    exit 1
  }
fi
chmod 700 data backups
chmod 600 .env
if (( account_running == 1 )); then
  ./backup.sh --lock-held
fi
rollback() {
  result=$?
  trap - EXIT
  if (( result != 0 )) && [[ -n "$previous" ]]; then
    ln -s "$previous" "current.rollback.$$"
    mv -Tf "current.rollback.$$" current
    docker compose up -d --force-recreate account || true
    echo 'Restored previous release; database backup retained' >&2
  fi
  exit "$result"
}
trap rollback EXIT
ln -s "releases/$release" "current.next.$$"
mv -Tf "current.next.$$" current
# Discover only this application's bridge. Forwarded headers from other peers remain untrusted.
docker compose create --no-recreate account
gateway=$(docker network inspect mu-account_default --format '{{(index .IPAM.Config 0).Gateway}}')
[[ "$gateway" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'Invalid Docker gateway' >&2; exit 1; }
umask 077
grep -v '^Proxy__KnownProxies__0=' .env >.env.next || true
printf 'Proxy__KnownProxies__0=%s\n' "$gateway" >>.env.next
mv .env.next .env
docker compose up -d --force-recreate account
for attempt in {1..30}; do
  if curl --fail --silent --max-time 2 http://127.0.0.1:8089/api/health >/dev/null; then
    printf 'Activated %s\n' "$release"
    exit 0
  fi
  sleep 1
done
echo 'Account service health check failed' >&2
exit 1
