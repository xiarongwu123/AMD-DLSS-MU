# MU account service

The account API and Razor administration console live in this repository. They run independently of the existing download and survey website. The desktop client remains a Windows application and calls the service over HTTPS.

## Build and test

Use the .NET 10 SDK for the server. The desktop project uses .NET 8.

```sh
dotnet build server/Mu.Server/Mu.Server.csproj -c Release
dotnet run --project server/Mu.Server.Tests/Mu.Server.Tests.csproj
dotnet run --project server/Mu.Server.HttpTests/Mu.Server.HttpTests.csproj
dotnet run --project server/Mu.Server.AdminTests/Mu.Server.AdminTests.csproj
dotnet publish server/Mu.Server/Mu.Server.csproj -c Release -r linux-x64 --self-contained false -o server/artifacts/linux-x64
```

The API uses `/api/v1`; `/api/health` is an unauthenticated readiness check. Registration remains disabled until the sending domain and real delivery have been verified. Production has no fake-email or fake-payment provider. Payment collection remains disabled until a real merchant integration has been implemented and tested.

Feature snapshots include a monotonically increasing `version` for each implemented feature. Every successful administrative configuration write increments that version in the same transaction as the policy and audit entry. Permission checks still read current server state before each client operation; the version is not an offline authorization grant.

## Production layout

The deployment root is `/home/xrw/amd-dlss-mu-account`. Copy the files from `deploy/` into that directory, and copy the published Linux files into `releases/<release-id>/`. `current` points to the active immutable release. The container exposes only `127.0.0.1:8089` and runs as the host user with UID/GID 1000. The configured Cloudflare Tunnel forwards `mu-api.claude-api.cn` to `http://127.0.0.1:8089`.

Run `initialize.sh` on the server to create private data directories and generate `.env` with a unique random `Auth__CodePepper`. It preserves an existing configuration. Set `Resend__ApiKey` only on the server. The database, verification-code pepper, and data-protection keys must be retained across releases. None belong in Git or client packages. Activation discovers this application's Docker bridge gateway and trusts only that gateway for forwarded HTTPS and client IP headers.

Before enabling registration, verify `mu-mail.claude-api.cn` in Resend, add its exact DNS records in Cloudflare, and confirm receipt of a test email sent as `MU <noreply@mu-mail.claude-api.cn>`. Keep root-domain MX records and unrelated mail routes unchanged. Set `Registration__Enabled=true` only after verification. Mail-provider acceptance is distinct from inbox delivery.

Activate a tested release:

```sh
chmod +x activate.sh backup.sh
./activate.sh <release-id>
curl --fail http://127.0.0.1:8089/api/health
```

Activation backs up an already running database and restores the previous application symlink if the new service fails its health check. This does not automatically reverse a database schema migration. Inspect migration compatibility and use the pre-deployment backup for a coordinated database rollback when necessary.

## Administrator setup

Create the first administrator through the container's `--create-admin` command; enter its password on stdin, never in a shell argument or a committed file. There is no default administrator password and public registration cannot create an administrator.

```sh
docker compose exec account dotnet Mu.Server.dll --create-admin admin@example.com
```

Visit `https://mu-api.claude-api.cn/admin`, sign in, and complete authenticator setup. The management console requires a separate administrator session and TOTP. Securely retain the authenticator setup key; automated MFA recovery is not implemented. Pro membership does not grant administrator access.

## Backups and recovery

`backup.sh` invokes the application's SQLite online-backup command. Do not copy only the main database while WAL writes are active. The script rotates only its own snapshots and retains the latest seven successful backups. Run `schedule-backup.sh` to install the following daily cron entry under the deployment user, preserving existing entries. The schedule uses the server timezone:

```text
0 3 * * * /home/xrw/amd-dlss-mu-account/backup.sh >>/home/xrw/amd-dlss-mu-account/backups/backup.log 2>&1
```

For recovery, stop only the account container, preserve the current database plus any WAL/SHM files in a separate incident directory, restore a verified snapshot as `data/accounts.sqlite`, retain the corresponding secrets and `data/keys`, and start the compatible release. Run the health check and verify account authentication and membership state. Test recovery against an isolated database before relying on a backup. Local snapshots protect against accidental changes, not loss of the VPS itself.

## Email-provider error references

Resend documents `daily_quota_exceeded`, `monthly_quota_exceeded`, and `rate_limit_exceeded` as HTTP 429 errors in its [error reference](https://resend.com/docs/api-reference/errors). The [usage limits](https://resend.com/docs/api-reference/rate-limit) specify midnight UTC for the daily quota reset and seconds for `Retry-After` and `ratelimit-reset`. MU reports quota failures separately, never returns the provider's raw message, and does not invent a monthly reset time when no retry hint is supplied. Rate-limit retry hints have a 60-second minimum to respect the local verification-code cooldown. These paths are covered by isolated provider-response tests; no real email is sent by those tests.

## Acceptance boundaries

API tests, UI compilation, package generation, real email receipt, public deployment, and Windows execution are separate checks. A successful Mac build does not establish Windows DPAPI, desktop interaction, game installation, or update recovery acceptance. Already deployed game components and older desktop clients are not remotely locked by this service.
