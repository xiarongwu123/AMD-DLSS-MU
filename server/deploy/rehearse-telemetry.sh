#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
release=${1:?Usage: rehearse-telemetry.sh RELEASE_ID}
[[ "$release" =~ ^[A-Za-z0-9][A-Za-z0-9._-]+$ ]]
test -s "releases/$release/Mu.Server.dll"
umask 077
exec 9>>backups/.backup.lock
flock -x 9
directory="backups/telemetry-rehearsal-$(date -u +%Y%m%dT%H%M%SZ)"
mkdir "$directory"
readlink current >"$directory/baseline-release.txt"
sha256sum current/Mu.Server.dll | cut -d ' ' -f 1 >"$directory/baseline-dll.sha256"
sha256sum "releases/$release/Mu.Server.dll" | cut -d ' ' -f 1 >"$directory/candidate-dll.sha256"
docker compose exec -T account dotnet Mu.Server.dll --backup "/backups/$(basename "$directory")/baseline.sqlite"
cp -p "$directory/baseline.sqlite" "$directory/work.sqlite"
cp -p "$directory/baseline.mail.sqlite" "$directory/mail-logs.sqlite"
image=$(docker inspect amd-dlss-mu-account --format '{{.Config.Image}}')
docker run --rm --network none --user 1000:1000 --env-file .env \
  -e Database__Path=/rehearsal/work.sqlite -e DataProtection__Path=/rehearsal/keys \
  -v "$PWD/releases/$release:/app:ro" -v "$PWD/$directory:/rehearsal" \
  -w /app "$image" dotnet Mu.Server.dll --backup /rehearsal/migrated.sqlite
python3 verify-telemetry-migration.py "$directory/baseline.sqlite" "$directory/migrated.sqlite"
touch "$directory/verified"
printf 'Verified rehearsal: %s\n' "$directory"
