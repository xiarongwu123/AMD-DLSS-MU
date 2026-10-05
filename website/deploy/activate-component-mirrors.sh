#!/usr/bin/env bash
set -euo pipefail
site=/home/xrw/amd-dlss-mu-site
bundle=$(cd "${1:?Usage: activate-component-mirrors.sh BUNDLE}" && pwd)
cd "$site"
exec 9>backups/.website-compatibility.lock
flock -n 9 || { echo 'Another website deployment is running.' >&2; exit 1; }
[[ "$(sha256sum mirrors.mjs | cut -d' ' -f1)" == "$(cat "$bundle/baseline.sha256")" ]]
(cd "$bundle" && sha256sum -c payload.sha256)
stamp=$(date -u +%Y%m%dT%H%M%SZ)
backup="$site/backups/component-mirrors-$stamp"
mkdir -p "$backup/build"
old_image=$(docker inspect --format '{{.Image}}' amd-dlss-mu-web)
cp -p mirrors.mjs "$backup/mirrors.mjs"
cp -p data/release.json "$backup/release.json"
printf '%s\n' "$old_image" > "$backup/image-id"
for module in server.mjs analytics.mjs survey.mjs packages.mjs compatibility.mjs mirrors.mjs seo.mjs updates.mjs Dockerfile; do
  cp -p "$module" "$backup/build/"
done
cp "$bundle/mirrors.mjs" "$backup/build/mirrors.mjs"
candidate="mu-website-component-mirrors:$stamp"
docker build --pull=false --tag "$candidate" "$backup/build"
for kind in dlss-installer optiscaler; do
  mkdir -p "data/mirrors/$kind"
  cp -a "$bundle/data/mirrors/$kind/." "data/mirrors/$kind/"
done
rollback() {
  trap - ERR
  cp -p "$backup/mirrors.mjs" mirrors.mjs
  docker image tag "$old_image" amd-dlss-mu-site-website:latest
  docker compose up -d --no-build --no-deps --force-recreate website
  echo "Mirror handler rolled back. Backup: $backup" >&2
  exit 1
}
trap rollback ERR
cp "$bundle/mirrors.mjs" mirrors.mjs
docker image tag "$candidate" amd-dlss-mu-site-website:latest
docker compose up -d --no-build --no-deps --force-recreate website
healthy=0
for attempt in {1..20}; do
  if curl -fsS http://127.0.0.1:8088/api/health >/dev/null; then healthy=1; break; fi
  sleep 1
done
[[ "$healthy" == 1 ]]
while IFS=$'\t' read -r path size hash; do
  curl -fsSI "http://127.0.0.1:8088$path" > "$backup/headers.txt"
  tr -d '\r' < "$backup/headers.txt" | grep -iFx "Content-Length: $size"
  tr -d '\r' < "$backup/headers.txt" | grep -iFx "ETag: \"$hash\""
  curl -fsS -H 'Range: bytes=0-1023' "http://127.0.0.1:8088$path" > "$backup/range.bin"
  [[ "$(wc -c < "$backup/range.bin")" == 1024 ]]
done < "$bundle/probes.tsv"
cmp data/release.json "$backup/release.json"
trap - ERR
printf 'Component mirrors activated. Backup: %s\n' "$backup"
