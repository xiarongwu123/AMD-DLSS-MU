#!/usr/bin/env bash
set -euo pipefail
stage=$(realpath "${1:?staging directory required}")
root=/home/xrw/amd-dlss-mu-site
cd "$root"
exec 9>backups/apple-static.lock
flock -n 9
while IFS= read -r file; do
  case "$file" in
    public/index.html|public/compatibility.html|public/download.html|public/guide.html|public/feedback.html|public/survey.html|public/dlss5.html|public/robots.txt|public/apple*.css|public/apple*.js|public/styles.css|public/common.js|public/catalog.js) ;;
    public/assets/*)
      [[ "$file" != *..* && "$file" != public/assets/admin* && "$file" != public/assets/dashboard* && "$file" != public/assets/metrics* ]] ;;
    *) echo "Unexpected path: $file" >&2; exit 1 ;;
  esac
done < "$stage/files.txt"
(cd "$stage" && sha256sum -c SHA256SUMS >/dev/null)
stamp=$(date -u +%Y%m%dT%H%M%SZ)
backup="$root/backups/apple-static-$stamp"
mkdir -m 700 "$backup"
cp "$stage/files.txt" "$backup/files.txt"
sha256sum ./*.mjs Dockerfile compose.yml .env public/admin.html public/assets/admin* public/assets/dashboard* public/assets/metrics* public/release.json data/release.json public/files/AMD-DLSS-MU.exe > "$backup/protected.sha256"
docker inspect --format '{{.Id}} {{.Image}}' amd-dlss-mu-web amd-dlss-mu-account > "$backup/containers.before"
while IFS= read -r file; do
  if [[ -f "$file" ]]; then
    mkdir -p "$backup/original/$(dirname "$file")"
    cp -p "$file" "$backup/original/$file"
  else
    printf '%s\n' "$file" >> "$backup/new-files.txt"
  fi
done < "$stage/files.txt"
rollback() {
  trap - ERR
  while IFS= read -r file; do
    if [[ -f "$backup/original/$file" ]]; then cp -p "$backup/original/$file" "$file"; else rm -f -- "$file"; fi
  done < "$backup/files.txt"
  echo "Deployment failed; restored static files from $backup" >&2
}
trap rollback ERR
# Resources precede HTML so concurrent visitors can resolve every new reference.
while IFS= read -r file; do
  mkdir -p "$(dirname "$file")"
  cp "$stage/$file" "$file.apple-new"
  chmod 644 "$file.apple-new"
  mv -f "$file.apple-new" "$file"
done < "$stage/files.txt"
sha256sum -c "$backup/protected.sha256" > "$backup/protected-check.txt"
sha256sum -c "$stage/SHA256SUMS" > "$backup/deployed-check.txt"
docker inspect --format '{{.Id}} {{.Image}}' amd-dlss-mu-web amd-dlss-mu-account > "$backup/containers.after"
cmp "$backup/containers.before" "$backup/containers.after"
curl -fsS --max-time 10 http://127.0.0.1:8088/api/health > "$backup/health.json"
[[ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 http://127.0.0.1:8088/admin)" == 200 ]]
[[ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 http://127.0.0.1:8088/api/admin/overview)" == 401 ]]
trap - ERR
echo "BACKUP=$backup"
echo "Static deployment verified; admin, runtime, packages, containers unchanged."
