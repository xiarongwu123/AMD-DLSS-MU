#!/usr/bin/env bash
set -euo pipefail
app_root=/home/xrw/amd-dlss-mu-account
config="$app_root/current/appsettings.json"
release=$(readlink "$app_root/current")
umask 077
stamp=$(date -u +%Y%m%dT%H%M%SZ)
backup="$app_root/backups/request-logging-$stamp.json"
cp -p -- "$config" "$backup"
temporary=$(mktemp "$(dirname "$config")/.request-logging.XXXXXX")
trap 'rm -f -- "$temporary"' EXIT
jq '.Logging.LogLevel["Microsoft.AspNetCore.Hosting.Diagnostics"] = "Information"' "$config" > "$temporary"
jq -e '.Logging.LogLevel["Microsoft.AspNetCore.Hosting.Diagnostics"] == "Information"' "$temporary" > /dev/null
chmod --reference="$config" "$temporary"
[[ "$(readlink "$app_root/current")" == "$release" ]]
cmp -s "$config" "$backup"
mv -f -- "$temporary" "$config"
printf 'Request logging enabled without restarting. Release: %s. Backup: %s\n' "$release" "$backup"
