#!/usr/bin/env bash
set -euo pipefail
site=/home/xrw/amd-dlss-mu-site
deployment=$(cd "$(dirname "$0")" && pwd)
stamp=$(date -u +%Y%m%dT%H%M%SZ)
backup="$site/backups/magpie-mirror-$stamp"
mkdir -p "$backup"
cp "$site/server.mjs" "$site/Dockerfile" "$backup/"
old_image=$(docker inspect --format '{{.Image}}' amd-dlss-mu-web)
docker image tag "$old_image" "amd-dlss-mu-site-website:before-mirror-$stamp"
printf '%s\n' "$old_image" > "$backup/image-id"
rollback() {
  cp "$backup/server.mjs" "$backup/Dockerfile" "$site/"
  docker image tag "$old_image" amd-dlss-mu-site-website:latest
  (cd "$site" && docker compose up -d --no-build website)
}
trap rollback ERR
docker run --rm -v "$deployment:/patch:ro" -v "$site:/site" node:22-alpine node /patch/install.mjs /site
cd "$site"
docker compose build website
docker compose up -d --no-build website
for attempt in {1..15}; do
  if curl -fsS http://127.0.0.1:8088/api/health; then break; fi
  sleep 1
done
curl -fsSI http://127.0.0.1:8088/mirrors/magpie/v0.6.8-experimental.1/efb41e5177a628c0742566a887660a5a50e159f824cbfbf9f0620b2cc3b6803c/Magpie-Experimental-x64.zip
trap - ERR
printf '\nBackup: %s\n' "$backup"
