#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
umask 077
mkdir -p releases data backups
if [[ ! -e .env ]]; then
  pepper=$(openssl rand -hex 32)
  printf '%s\n' \
    "Auth__CodePepper=$pepper" \
    'Registration__Enabled=false' \
    'Resend__ApiKey=' \
    'Resend__From=MU <noreply@mu-mail.claude-api.cn>' \
    'AllowedHosts=mu-api.claude-api.cn;localhost;127.0.0.1' >.env
  unset pepper
fi
chmod 600 .env
chmod 700 data backups
echo 'Deployment directories and private configuration ready; registration remains disabled.'
