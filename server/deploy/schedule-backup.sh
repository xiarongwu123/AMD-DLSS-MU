#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
test -x "$root/backup.sh"
entry="0 3 * * * $root/backup.sh >>$root/backups/backup.log 2>&1"
existing=$(crontab -l 2>/dev/null || true)
if ! printf '%s\n' "$existing" | grep -Fxq "$entry"; then
  { printf '%s\n' "$existing"; printf '%s\n' "$entry"; } | crontab -
fi
printf 'Daily account backup installed at 03:00 in the server timezone.\n'
