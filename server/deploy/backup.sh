#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
mkdir -p backups
if [[ ${1:-} == --lock-held && $# == 1 ]]; then
  # activate.sh passes its already locked descriptor so it remains held during restart.
  [[ "/proc/$$/fd/9" -ef backups/.backup.lock ]] || { echo 'Missing inherited backup lock' >&2; exit 1; }
  flock -n 9 || { echo 'Inherited backup lock is unavailable' >&2; exit 1; }
elif (( $# == 0 )); then
  exec 9>backups/.backup.lock
  flock -n 9 || exit 0
else
  echo 'Usage: backup.sh [--lock-held]' >&2
  exit 1
fi
stamp=$(date -u +%Y%m%dT%H%M%SZ)
while [[ -e "backups/accounts-${stamp}.sqlite" ]]; do
  sleep 1
  stamp=$(date -u +%Y%m%dT%H%M%SZ)
done
docker compose exec -T account dotnet Mu.Server.dll --backup "/backups/accounts-${stamp}.sqlite"
# Restrict rotation to snapshots produced by this script, after a successful backup.
mapfile -t snapshots < <(find backups -maxdepth 1 -type f -name 'accounts-????????T??????Z.sqlite' | sort -r)
if (( ${#snapshots[@]} > 7 )); then
  for snapshot in "${snapshots[@]:7}"; do rm -- "$snapshot"; done
fi
