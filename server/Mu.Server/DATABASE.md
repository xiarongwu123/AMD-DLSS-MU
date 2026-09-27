# Account database lifecycle

The account service owns a separate SQLite database. Never use the website's
`analytics.sqlite` or mount the website's data directory into this service.

Schema version 2 is recorded in `DatabaseSchemas` (row ID 1). On an empty database,
the generated EF schema and this marker are committed in one transaction. On an
existing database, startup upgrades recognized version 1 to version 2 in an explicit
transaction: add `FeatureDefinitions.Version INTEGER NOT NULL DEFAULT 1`, then update
the schema marker. Historical users, rules, timestamps, sessions and audit records
are preserved. Failure rolls back both the column change and marker; repeating startup
at version 2 does not rerun the migration. Unknown databases or unsupported versions
stop startup before serving traffic. Feature seeds only insert
missing known keys; existing operator settings remain unchanged.

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
