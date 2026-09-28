#!/usr/bin/env bash
set -euo pipefail

# Run from the extracted patch bundle. Checking is the safe default.
# Usage: activate-website-compatibility.sh APP_ROOT [--check|--activate]
# Rollback: activate-website-compatibility.sh APP_ROOT --rollback BACKUP_DIRECTORY
app_root=${1:?Usage: activate-website-compatibility.sh APP_ROOT [--check|--activate|--rollback BACKUP_DIRECTORY]}
mode=${2:---check}
bundle=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
app_root=$(cd -- "$app_root" && pwd -P)
service=website
container=amd-dlss-mu-web
health_url=http://127.0.0.1:8088/api/health
stamp=$(date -u +%Y%m%dT%H%M%SZ)-$$
backup=
changed=0
manifest="$bundle/manifest.tsv"
files=(server.mjs compatibility.mjs Dockerfile compose.yml
  public/index.html public/download.html public/guide.html public/feedback.html public/survey.html public/compatibility.html
  public/assets/compatibility.css public/assets/compatibility.js
  public/assets/hardware-check.js public/assets/hardware-reference.json)
dependencies=(analytics.mjs survey.mjs packages.mjs mirrors.mjs public/assets/app.css public/assets/app.js)

fail() { printf '%s\n' "$*" >&2; exit 1; }
for command in docker sha256sum curl jq flock; do command -v "$command" >/dev/null || fail "Missing command: $command"; done
[[ "$mode" == --check || "$mode" == --activate || "$mode" == --rollback ]] || fail 'Unknown operation.'
cd -- "$app_root"

validate_manifest() {
  local path before after extra allowed item count=0
  declare -A seen=()
  while IFS=$'\t' read -r path before after extra; do
    allowed=0
    for item in "${files[@]}"; do [[ "$path" != "$item" ]] || allowed=1; done
    [[ "$allowed" == 1 && -z "$extra" && -z "${seen[$path]:-}" ]] || fail "Invalid manifest path: $path"
    [[ "$before" == MISSING || "$before" =~ ^[a-f0-9]{64}$ ]] || fail "Invalid baseline hash: $path"
    [[ "$after" =~ ^[a-f0-9]{64}$ ]] || fail "Invalid payload hash: $path"
    seen[$path]=1
    count=$((count + 1))
  done < "$manifest"
  [[ "$count" == "${#files[@]}" ]] || fail 'Incomplete patch manifest.'
}

check_file() {
  local root=$1 path=$2 expected=$3 actual
  if [[ "$expected" == MISSING ]]; then
    [[ ! -e "$root/$path" && ! -L "$root/$path" ]] || fail "Concurrent change: $path was expected to be absent."
  else
    [[ -f "$root/$path" && ! -L "$root/$path" ]] || fail "Missing or linked file: $path"
    actual=$(sha256sum -- "$root/$path"); actual=${actual%% *}
    [[ "$actual" == "$expected" ]] || fail "Hash mismatch / concurrent change: $path"
  fi
}

check_baseline() {
  local path before after expected extra item allowed count=0
  declare -A seen=()
  while IFS=$'\t' read -r path before after; do check_file "$app_root" "$path" "$before"; done < "$manifest"
  while IFS=$'\t' read -r path expected extra; do
    allowed=0
    for item in "${dependencies[@]}"; do [[ "$path" != "$item" ]] || allowed=1; done
    [[ "$allowed" == 1 && -z "$extra" && -z "${seen[$path]:-}" && "$expected" =~ ^[a-f0-9]{64}$ ]] || fail 'Invalid dependency manifest.'
    seen[$path]=1; count=$((count + 1))
    check_file "$app_root" "$path" "$expected"
  done < "$bundle/baseline-checks.tsv"
  [[ "$count" == "${#dependencies[@]}" ]] || fail 'Incomplete dependency manifest.'
}

wait_health() {
  local attempt
  for attempt in {1..30}; do
    if curl --fail --silent --max-time 2 "$health_url" | jq -e '.ok == true and .service == "amd-dlss-mu"' >/dev/null; then return 0; fi
    sleep 1
  done
  return 1
}

restore() {
  local path before after temp failed=0
  # Preserve the failed candidate sources for diagnosis before restoring originals.
  mkdir -p -- "$backup/failed-source"
  while IFS=$'\t' read -r path before after; do
    if [[ -f "$app_root/$path" && ! -L "$app_root/$path" ]]; then
      mkdir -p -- "$backup/failed-source/$(dirname -- "$path")"
      cp -p -- "$app_root/$path" "$backup/failed-source/$path" || failed=1
    fi
    if [[ "$before" == MISSING ]]; then
      rm -f -- "$app_root/$path" || failed=1
    else
      temp="$app_root/$path.rollback.$$"
      cp -p -- "$backup/source/$path" "$temp" && mv -fT -- "$temp" "$app_root/$path" || failed=1
    fi
  done < "$manifest"
  docker image tag "$old_image_id" "$old_image_name" || failed=1
  docker compose up -d --no-build --no-deps --force-recreate "$service" || failed=1
  wait_health || failed=1
  [[ "$failed" == 0 ]]
}

on_exit() {
  local status=$?
  trap - EXIT INT TERM
  if (( status != 0 && changed == 1 )); then
    set +e
    if restore; then
      printf 'Activation failed; prior website source and image restored. Backup: %s\n' "$backup" >&2
    else
      printf 'Automatic rollback did not fully recover health. Inspect backup: %s\n' "$backup" >&2
    fi
  fi
  exit "$status"
}

if [[ "$mode" == --rollback ]]; then
  backup=${3:?Rollback requires the backup directory printed by activation.}
  backup=$(cd -- "$backup" && pwd -P)
  [[ "$backup" == "$app_root"/backups/website-compatibility-* ]] || fail 'Backup must belong to this website.'
  manifest="$backup/manifest.tsv"
  validate_manifest
  old_image_id=$(<"$backup/old-image-id")
  old_image_name=$(<"$backup/old-image-name")
  [[ "$old_image_id" =~ ^sha256:[a-f0-9]{64}$ && "$old_image_name" =~ ^[A-Za-z0-9][A-Za-z0-9._/:@-]*$ ]] || fail 'Invalid saved image identity.'
  exec 9>backups/.website-compatibility.lock
  flock -n 9 || fail 'Another website activation is running.'
  while IFS=$'\t' read -r path before after; do
    check_file "$app_root" "$path" "$after"
    [[ "$before" == MISSING ]] || check_file "$backup/source" "$path" "$before"
  done < "$manifest"
  [[ "$(docker inspect --format '{{.Image}}' "$container")" == "$(<"$backup/new-image-id")" ]] || fail 'A different image is running; refusing stale rollback.'
  restore || fail "Rollback needs inspection: $backup"
  printf 'Prior website restored; runtime data and downloads preserved. Backup: %s\n' "$backup"
  exit 0
fi

validate_manifest
while IFS=$'\t' read -r path before after; do check_file "$bundle/payload" "$path" "$after"; done < "$manifest"
check_baseline
[[ "$(docker compose config --services)" == "$service" ]] || fail 'Unexpected compose service configuration.'
[[ "$(docker inspect --format '{{index .Config.Labels "com.docker.compose.service"}}' "$container")" == "$service" ]] || fail 'Container is not the expected compose service.'
old_container_id=$(docker inspect --format '{{.Id}}' "$container")
old_image_id=$(docker inspect --format '{{.Image}}' "$container")
old_image_name=$(docker inspect --format '{{.Config.Image}}' "$container")
[[ "$old_image_id" =~ ^sha256:[a-f0-9]{64}$ && "$old_image_name" =~ ^[A-Za-z0-9][A-Za-z0-9._/:-]*$ && "$old_image_name" != sha256:* ]] || fail 'Unsupported image identity.'
wait_health || fail 'Current website is not healthy.'
if [[ "$mode" == --check ]]; then
  printf 'Baseline and payload hashes match. Service: %s; current image: %s (%s). No activation performed.\n' "$service" "$old_image_name" "$old_image_id"
  exit 0
fi

umask 077
mkdir -p backups
exec 9>backups/.website-compatibility.lock
flock -n 9 || fail 'Another website activation is running.'
check_baseline
backup="$app_root/backups/website-compatibility-$stamp"
mkdir -- "$backup"
cp -- "$manifest" "$backup/manifest.tsv"
printf '%s\n' "$old_image_id" > "$backup/old-image-id"
printf '%s\n' "$old_image_name" > "$backup/old-image-name"
printf '%s\n' "$old_container_id" > "$backup/old-container-id"
docker image tag "$old_image_id" "mu-website-rollback:$stamp"
printf '%s\n' "mu-website-rollback:$stamp" > "$backup/rollback-image-tag"
mkdir -- "$backup/source" "$backup/build"
while IFS=$'\t' read -r path before after; do
  if [[ "$before" != MISSING ]]; then
    mkdir -p -- "$backup/source/$(dirname -- "$path")"
    cp -p -- "$app_root/$path" "$backup/source/$path"
    check_file "$backup/source" "$path" "$before"
  fi
done < "$manifest"
for path in analytics.mjs survey.mjs packages.mjs mirrors.mjs; do cp -p -- "$app_root/$path" "$backup/build/$path"; done
for path in server.mjs compatibility.mjs Dockerfile; do cp -- "$bundle/payload/$path" "$backup/build/$path"; done
candidate_image="mu-website-compatibility:$stamp"
docker build --pull=false --tag "$candidate_image" "$backup/build"
docker image inspect --format '{{.Id}}' "$candidate_image" > "$backup/new-image-id"

# Build first; reject concurrent source or running-image changes before replacing anything.
check_baseline
[[ "$(docker inspect --format '{{.Id}}' "$container")" == "$old_container_id" ]] || fail 'Container changed during preparation.'
[[ "$(docker inspect --format '{{.Image}}' "$container")" == "$old_image_id" ]] || fail 'Image changed during preparation.'
trap on_exit EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
changed=1
while IFS=$'\t' read -r path before after; do
  mkdir -p -- "$app_root/$(dirname -- "$path")"
  temp="$app_root/$path.next.$$"
  install -m 644 -- "$bundle/payload/$path" "$temp"
  mv -fT -- "$temp" "$app_root/$path"
done < "$manifest"
docker image tag "$candidate_image" "$old_image_name"
docker compose up -d --no-build --no-deps --force-recreate "$service"
wait_health

curl --fail --silent --show-error --max-time 12 http://127.0.0.1:8088/compatibility > "$backup/page-check.html"
grep -F '/assets/compatibility.js' "$backup/page-check.html" >/dev/null
curl --fail --silent --show-error --max-time 12 http://127.0.0.1:8088/api/compatibility/games > "$backup/games-check.json"
curl --fail --silent --show-error --max-time 12 http://127.0.0.1:8088/api/compatibility/gpus > "$backup/gpus-check.json"
# Metadata release checks evidence coverage while accepting existing real reports.
jq -e '.total >= 2000 and .catalogTotal >= 2000 and .coverage.requirements > 1000
  and .coverage.requirements <= .catalogTotal and .coverage.modCompatibility == 239
  and (.items | length > 0 and all(.[];
    .counts.total == (.counts.success + .counts.partial + .counts.failure)
    and (.counts | [.success, .partial, .failure] | all(.[]; type == "number" and . >= 0 and . == floor))
    and (.evidence.hasRequirements | type) == "boolean"))' "$backup/games-check.json" >/dev/null
jq -e '.items | type == "array" and length <= 200 and all(.[]; type == "string" and length > 0)' "$backup/gpus-check.json" >/dev/null
curl --fail --silent --show-error --max-time 12 http://127.0.0.1:8088/api/compatibility/games/black-myth-wukong > "$backup/requirements-check.json"
jq -e '.requirements.status == "available" and (.requirements.minimum.graphics | length > 0)
  and (.modCompatibility | length > 0 and all(.[]; .muVerified == false))' "$backup/requirements-check.json" >/dev/null
curl --fail --silent --show-error --max-time 12 http://127.0.0.1:8088/assets/hardware-reference.json > "$backup/hardware-check.json"
jq -e '.schemaVersion == 1 and (.gpu | length > 50) and (.cpu | length > 50)' "$backup/hardware-check.json" >/dev/null
game_id=$(jq -r '.items[0].game.id' "$backup/games-check.json")
[[ "$game_id" =~ ^[A-Za-z0-9_-]{1,64}$ ]]
curl --fail --silent --show-error --max-time 12 "http://127.0.0.1:8088/api/compatibility/games/$game_id" > "$backup/detail-check.json"
jq -e '.counts.total == (.counts.success + .counts.partial + .counts.failure)
  and (.tests | type == "array" and length <= 50) and (.gpus | type == "array")' "$backup/detail-check.json" >/dev/null
changed=0
printf 'Website activated; health, page, published requirements and adaptation coverage verified. Backup: %s\n' "$backup"
printf 'Manual rollback: bash %q %q --rollback %q\n' "${BASH_SOURCE[0]}" "$app_root" "$backup"
