# Account database lifecycle

The account service owns a separate SQLite database. Never use the website's
`analytics.sqlite` or mount the website's data directory into this service.

Schema version 3 is recorded in `DatabaseSchemas` (row ID 1). On an empty database,
the generated EF schema and this marker are committed in one transaction. On an
existing database, startup upgrades recognized versions 1 and 2 in a single explicit
transaction. Version 1 first adds `FeatureDefinitions.Version INTEGER NOT NULL DEFAULT 1`;
version 2 adds `CompatibilityGames`, `CompatibilityTests`, their indexes and account/game
foreign keys. The schema marker advances to 3 only after those changes succeed.
Historical users, rules, timestamps, sessions and audit records are preserved. Failure
rolls back all schema changes and the marker, including the intermediate version 1
column change. Repeating startup at version 3 does not rerun the migration. Unknown
databases or unsupported versions stop startup before serving traffic. Feature seeds
only insert missing known keys; existing operator settings remain unchanged.

The compatibility catalog seed has four Steam game identities and zero test results.
Reports retain immutable environment JSON, result, server timestamp, the reporting
account's foreign key and submission/environment hashes. Unique indexes enforce
one `SubmissionId` per account and one environment per account/game/UTC day. Steam
App IDs are unique, while games without a Steam identity use normalized names for
directory lookup. Counts are computed from persisted reports rather than cached or
seeded compatibility claims. See [COMPATIBILITY.md](COMPATIBILITY.md) for contracts
and the limits of community reports.

Each feature has a persistent integer configuration version starting at 1. Every
successful administrator configuration save increments it in the same transaction
as its rule and audit entry; multiple changes within one second remain distinguishable.
Account and authorization snapshots include this version. An older client may ignore
the additive JSON field. Historical audit entries are retained without being rewritten.

Before a future schema change, increment the version and add an explicit ordered
upgrade in `DatabaseSetup` that runs the schema/data change and version update in
one transaction. Test both an upgrade from each supported predecessor and a failed
upgrade rollback. Deployments must take a verified backup before migration. A
rollback that changes schema uses the matching server image and its pre-upgrade
database backup together; never start older binaries against a newer schema.

For the first version 2 to 3 release, validate the published Linux binary against
a working copy of a verified version 2 backup first. `--backup` runs initialization
before making its output, so it migrates its input database; never use the only
rollback snapshot as that input. Compare all historical row content and existing
feature rules, not only row counts, between the untouched snapshot and migrated
copy. Expect the schema marker, compatibility tables and missing compatibility
feature keys to be the only additions/changes. Do not print account rows, hashes
of credentials, tokens or security stamps into deployment logs.

For a lossless pre-opening rollback, stop the old service and take a final
consistent version 2 backup using the old binary against the now-quiescent
database. Start the new service with `Maintenance__Enabled=true`, retaining the
existing server secrets and Data Protection keys. Maintenance permits only GET
health and public compatibility reads; all other requests return 503 before
authentication, authorization, routing or business handlers can mutate data.
Check schema, historical row preservation, health and public responses before
setting `Maintenance__Enabled=false` and recreating the container to open traffic.

If verification fails, stop the new container, preserve its database/WAL/SHM as
incident artifacts, restore the final pre-upgrade snapshot and old release
together, then verify before reopening. Do not leave new-schema WAL/SHM beside a
restored old snapshot. Keep the final rollback snapshot under a release-specific
name outside the seven-snapshot automatic rotation pattern. The existing
`activate.sh` automatically restores only the previous release symlink on error;
it is not a schema-aware rollback and does not replace this database recovery.
After normal traffic resumes, the old snapshot is no longer a lossless rollback
point: preserve new writes and fix forward or plan an explicit data migration.

Run `dotnet Mu.Server.dll --backup /backups/accounts-UNIQUE.sqlite` to create an
online SQLite backup. It uses SQLite's backup API, includes committed WAL changes,
checks `PRAGMA integrity_check`, and refuses to overwrite a destination. Do not
copy only the live `.sqlite` file while writes are active. Protect and back up the
Data Protection key directory separately: administrator cookies depend on these
keys. Keep the verification-code pepper alongside the server secrets. Store backups and runtime secret environment files
outside the Git repository.

All database timestamps are Unix seconds. Session tokens are random opaque values;
only SHA-256 digests are stored. Verification-code digests use HMAC-SHA256 with a
deployment-specific secret pepper and challenge context. Identity owns password
hashes and authentication security stamps. No plaintext passwords, emailed codes,
access tokens, refresh tokens or payment secrets are stored in these tables.

Payment support is intentionally disabled. `IPaymentProvider` has only the
`DisabledPaymentProvider` production implementation. Orders reserve immutable
plan-name, duration, amount and currency snapshots; provider transaction uniqueness
and a payment-event ledger are available for a later real provider integration.
There is no payment callback, fake success route or automatic paid membership grant.
