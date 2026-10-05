#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
export SMTP_IMAGE=boky/postfix@sha256:aafc772384232497bed875e1eb66b4d3e54ba1ebc86e2e185a6dc1dbc48182ef
SMTP_TRUSTED_NETWORKS=$(docker network inspect mu-account_default --format '{{(index .IPAM.Config 0).Subnet}}')
[[ "$SMTP_TRUSTED_NETWORKS" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+/[0-9]+$ ]] || exit 1
export SMTP_TRUSTED_NETWORKS
docker compose -f compose.smtp.yml up -d
