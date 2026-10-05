# MU self-hosted outbound SMTP

`smtp.claude-api.cn` identifies the outbound server at `72.11.138.132`.
Submission is on `mu-smtp:587` inside `mu-account_default`; no host ports are
published. Only this network may relay, and only the `smtp.claude-api.cn`
sender domain is allowed. This is a service for MU transactional emails, not
a public SMTP account or a replacement for the Cloudflare inbox UI.

## Deploy

Copy `compose.smtp.yml`, `start-smtp.sh`, `smtp-init.sh`, and `smtp-tls-policy` into the existing
account deployment directory, then run `bash start-smtp.sh`. The script pins
the tested image digest and discovers the account network subnet. DKIM keys
and queues use named persistent volumes; never use `docker compose down -v`.
The container restarts automatically after a host restart.

DNS records (DNS-only):

| Type | Name | Value |
| --- | --- | --- |
| A | smtp.claude-api.cn | 72.11.138.132 |
| TXT | smtp.claude-api.cn | v=spf1 ip4:72.11.138.132 -all |
| TXT | _dmarc.smtp.claude-api.cn | v=DMARC1; p=reject; adkim=s; aspf=s |
| TXT | mu202610._domainkey.smtp.claude-api.cn | Public key from the container |

Read only the public DKIM record with:

```sh
docker exec amd-dlss-mu-smtp cat /etc/opendkim/keys/smtp.claude-api.cn.txt
docker exec amd-dlss-mu-smtp opendkim-testkey -d smtp.claude-api.cn -s mu202610 -vvv
```

Set the VPS IP's PTR to `smtp.claude-api.cn` through the provider's control
panel. Existing root-domain MX records and Cloudflare inbox routes stay intact.
Back up the `mu-smtp_smtp-keys` volume to private storage before changing hosts.
Do not commit private keys or queue contents.

## Switch the account service

First verify DNS, DKIM, PTR, rejected untrusted relay, and actual QQ/Gmail inbox
delivery. Back up the account `.env` and database. Deploy a release containing
`SmtpVerificationEmailSender`, then use these settings in the server `.env`:

```dotenv
Email__Provider=smtp
Smtp__Host=mu-smtp
Smtp__Port=587
Smtp__From=MU <noreply@smtp.claude-api.cn>
Smtp__StartTls=false
Smtp__AllowPrivatePlaintext=true
```

Cleartext application submission is limited to the same-host trusted Docker network. External relays
must use STARTTLS and should not enable `AllowPrivatePlaintext`. This adapter
supports STARTTLS, not implicit TLS on port 465. It does not retry uncertain
submissions or silently fall back to Resend. Missing configuration, rejection,
and timeout produce `503 email_unavailable` without provider error details.

SMTP `250` means a queue/server accepted the mail; it does not prove inbox
delivery. Watch `docker logs amd-dlss-mu-smtp` and `postqueue -j` for deferred or
bounced messages. Queue lifetime is 10 minutes, matching the verification-code
lifetime; logs rotate at 3 x 5 MB. MX acceptance and inbox placement must be
verified separately. Low sending rate is intentional for the new server IP.

Outbound SMTP requires TLS by default. On 2026-10-05, after explaining the
plaintext-transmission tradeoff, the user authorized a compatibility fix for
`sina.com`, whose three MX nodes did not advertise STARTTLS from this VPS.
The read-only `smtp-tls-policy` map sets only that exact destination to `may`:
TLS is preferred but plaintext is allowed when unavailable. Other domains
retain `encrypt`. This does not bypass recipient validation or anti-spam checks.
Verify with `postmap -q sina.com texthash:/etc/postfix/mu-tls-policy` inside
the SMTP container; unrelated domains must return no override. To roll back,
remove the exception from the policy file and reload Postfix. Do not delete
queued mail or DKIM volumes.

Rollback: restore the previous account `.env` and release, then recreate only
the account container. Leave queued emails and DKIM volumes intact. Stop SMTP
only after inspecting its queue. Never discard pending mail implicitly.

## Verification

```sh
dotnet run --project server/Mu.Server.HttpTests -- --smtp
dotnet run --project server/Mu.Server.HttpTests
```

The provider test uses a local SMTP fixture, including a rejected recipient;
it never sends internet mail. Production acceptance is a separate step.

## Production cutover: 2026-10-05

- RackNerd ticket BT49072 set the PTR to `smtp.claude-api.cn`. Queries from
  the VPS to both 1.1.1.1 and 8.8.8.8 confirmed it; the A record matched.
- Activated `20261005-smtp-prepared` with `Email__Provider=smtp` and public
  registration enabled. Preserved the existing production logging settings.
- The cutover snapshot is `backups/smtp-cutover-20261005T064145Z`; it holds
  the previous environment, release pointer, and private DKIM-key backup.
  Database backup and integrity checking completed before activation.
- Public API health and the SMTP container health passed. The live privacy
  page now describes self-hosted SMTP. Registration-code delivery to Gmail
  and password-reset-code delivery to QQ returned HTTP 200, were DKIM-signed,
  and received downstream SMTP 250. The mail queue was empty afterward.
- The user confirmed both post-cutover API messages arrived in the inbox.
  Unauthenticated account access returned 401; admin access redirected to
  login, and payments remained disabled. No password was changed by this test.

The former Resend settings remain only for rollback. The active SMTP provider
does not call Resend or fall back to it automatically.

## Administrator email monitoring

`/admin/mail` requires the existing administrator role and completed MFA. It
lists actual send attempts, with email/purpose/status/date filtering and 50-row
pagination. Rejected API requests and account-condition suppression are not
send attempts. Historical messages sent before monitoring cannot be reconstructed.

The service creates `mail-logs.sqlite` alongside `accounts.sqlite`, without
changing the account schema. It records submission before calling the provider,
never stores codes or message bodies, and distinguishes submission from delivery.
An interrupted submission is uncertain; administrators must not assume it failed
or resend it automatically. These records currently have no automatic deletion.

Copy `collect-mail-events.py` to the deployment root and set
`EmailMonitor__SnapshotPath=/data/smtp-events.json`. Run the collector as the
existing deployment user once per minute, using Python 3 and Docker CLI:

```cron
* * * * * /usr/bin/python3 /home/xrw/amd-dlss-mu-account/collect-mail-events.py >/dev/null 2>&1
```

The collector reads only the SMTP container logs and exports bounded metadata
atomically into the private data directory. It preserves 48 hours of correlation
state, rereads available logs from the last 24 hours, and locks against overlapping
runs. The API imports every 30 seconds; no Docker socket, public ingest endpoint,
or additional admin credential is exposed. The UI flags snapshots older than
three minutes. Prolonged collector outages or Docker log rotation can leave
delivery unconfirmed; they must not be interpreted as successful delivery.

Each SMTP message has a random request Message-ID mapped to a Postfix queue ID.
`sent` means the receiving mail server accepted it, not inbox placement/read.
`deferred` may later succeed or bounce. Terminal statuses do not regress when
logs are replayed. Resend submissions do not receive SMTP delivery events.

The updated `--backup` command creates a matching `.mail.sqlite` sidecar backup;
the updated `backup.sh` rotates it with the seven account snapshots. Restore
mail logs independently with the account container stopped, preserving the
current database and removing only stale WAL/SHM after archiving them. Rollback
to an older release leaves mail logs intact but pauses their collection into
the database; retain the collector snapshot for later import.

Monitoring was activated as `20261005-mail-monitor` on 2026-10-05, with the
previous environment and cron saved under `backups/mail-monitor-20261005T070356Z`.
HTTP integration passed 348 assertions and admin/backup integration passed 100.
Three collector tests passed on the VPS's Python 3.10 as well as locally; Docker
nanosecond timestamps are normalized before parsing. Production acceptance
confirmed a real Gmail registration message joined to queue `0B2F7160063` as
`delivered`, with three accepted messages and one recipient rejection persisted
at verification time. These are transport results, not inbox/read receipts.
