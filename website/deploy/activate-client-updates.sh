#!/usr/bin/env bash
set -euo pipefail
site=/home/xrw/amd-dlss-mu-site
bundle=$(cd "${1:?Usage: activate-client-updates.sh BUNDLE}" && pwd)
cd "$site"
mkdir -p backups
exec 9>backups/.website-compatibility.lock
flock -n 9 || { echo 'Another website deployment is running.' >&2; exit 1; }
files=(server.mjs packages.mjs updates.mjs Dockerfile public/admin.html public/assets/admin.js public/assets/admin.css)
while IFS=$'\t' read -r relative expected; do
  if [[ "$expected" == MISSING ]]; then
    [[ ! -e "$relative" ]] || { echo "Unexpected existing file: $relative" >&2; exit 1; }
  else
    [[ "$(sha256sum "$relative" | cut -d' ' -f1)" == "$expected" ]] || { echo "Live file changed: $relative" >&2; exit 1; }
  fi
done < "$bundle/baseline.tsv"
(cd "$bundle/payload" && sha256sum -c ../payload.sha256)
stamp=$(date -u +%Y%m%dT%H%M%SZ)
backup="$site/backups/client-updates-$stamp"
mkdir -p "$backup/files" "$backup/build"
old_image=$(docker inspect --format '{{.Image}}' amd-dlss-mu-web)
docker image tag "$old_image" "amd-dlss-mu-site-website:before-updates-$stamp"
printf '%s\n' "$old_image" > "$backup/image-id"
cp -p data/release.json "$backup/release.json"
for relative in "${files[@]}"; do
  if [[ -f "$relative" ]]; then mkdir -p "$backup/files/$(dirname "$relative")"; cp -p "$relative" "$backup/files/$relative"; fi
done
for relative in server.mjs analytics.mjs survey.mjs packages.mjs compatibility.mjs mirrors.mjs seo.mjs Dockerfile; do cp -p "$relative" "$backup/build/"; done
for relative in server.mjs packages.mjs updates.mjs Dockerfile; do cp "$bundle/payload/$relative" "$backup/build/"; done
candidate="mu-website-client-updates:$stamp"
docker build --pull=false --tag "$candidate" "$backup/build"
rollback() {
  trap - ERR
  for relative in "${files[@]}"; do
    if [[ -f "$backup/files/$relative" ]]; then cp -p "$backup/files/$relative" "$relative";
    else rm -f -- "$relative"; fi
  done
  docker image tag "$old_image" amd-dlss-mu-site-website:latest
  docker compose up -d --no-build --no-deps --force-recreate website
  echo "Website code rolled back; runtime data preserved. Backup: $backup" >&2
  exit 1
}
trap rollback ERR
for relative in "${files[@]}"; do cp "$bundle/payload/$relative" "$relative"; done
docker image tag "$candidate" amd-dlss-mu-site-website:latest
docker compose up -d --no-build --no-deps --force-recreate website
healthy=0
for attempt in {1..20}; do
  if curl -fsS http://127.0.0.1:8088/api/health > /dev/null; then healthy=1; break; fi
  sleep 1
done
[[ "$healthy" == 1 ]]
curl -fsS http://127.0.0.1:8088/api/updates/latest > "$backup/manifest-after.json"
jq -e '.channel == "stable" and (.downloadUrl | contains("/updates/"))' "$backup/manifest-after.json" > /dev/null
curl -fsS http://127.0.0.1:8088/admin | grep -q 'data-upload-form'
[[ "$(curl -s -o /dev/null -w '%{http_code}' -X POST -H 'Origin: https://amd-dlss-mu.claude-api.cn' http://127.0.0.1:8088/api/admin/uploads)" == 401 ]]
cmp data/release.json "$backup/release.json"
trap - ERR
printf 'Website upload and update API activated. Backup: %s\n' "$backup"
