#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
release=${1:?Usage: activate.sh RELEASE_ID}
[[ "$release" =~ ^[A-Za-z0-9][A-Za-z0-9._-]+$ ]] || { echo 'Invalid release ID' >&2; exit 1; }
test -s "releases/$release/Mu.Server.dll"
test -f .env
mkdir -p data backups
chmod 700 data backups
chmod 600 .env
# Keep the same lock through backup, replacement, health check, and rollback.
exec 9>backups/.backup.lock
flock -x 9
previous=$(readlink current || true)
if docker compose ps --status running --services | grep -qx account; then
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
