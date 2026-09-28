# Requirements and adaptation metadata release

This release adds embedded Steam requirements and OptiScaler source facts. It keeps
SQLite schema version 3. It does not generate reports, alter existing game IDs, or
require account data conversion. The original client DTO fields remain supported.

## Reviewed baseline

The remote account application root is `/home/xrw/amd-dlss-mu-account`. At review,
`current` pointed to `releases/20260927-catalog-2001`, using
`mcr.microsoft.com/dotnet/aspnet:10.0.12`. The running `/app/Mu.Server.dll` and host
`current/Mu.Server.dll` had the same SHA-256:
`ac652c91ecf785c638be5e84ed2adb1f2263acfac0abb3b9698755b53f80a3d4`.
Recheck these facts immediately before an actual release; they are not a lock on
production state.

Do not reuse `rehearse-account-compatibility.sh` or
`activate-account-compatibility.sh` unchanged: both were written for schema 2 to 3
and assert that the old database is version 2. The metadata rehearsal must assert
3 to 3 and compare every pre-existing table, including `CompatibilityGames`,
`CompatibilityTests`, `FeatureDefinitions`, and `DatabaseSchemas`.

## Freeze and publish

1. Finish or explicitly stop the requirements collector at an atomic checkpoint.
   Record processed/available/not-provided/unavailable/pending counts. Do not imply
   that all 2,001 games have published requirements when collection is incomplete.
2. Finish OptiScaler source review, including per-game versus shared Luma-page
   environment attribution. Record exact matched Steam coverage and the fixed Wiki
   commit. Unmatched entries must remain unmatched.
3. Record SHA-256 for `steam-games.json`, `steam-requirements.json`,
   `optiscaler-compatibility.json`, and their audit manifests. A running collector
   must not replace source files while testing or publishing the final snapshot.
4. Run the service, HTTP, and admin projects serially with the configured .NET 10
   SDK. Rebuild to embed the frozen resource bytes; `--no-build` alone does not
   incorporate updated JSON. These tests cover the real embedded resources as well
   as malformed metadata and old-response DTO compatibility.
5. Publish a new Linux artifact into a fresh output directory and record archive
   and DLL hashes. Do not overwrite a release already referenced by `current`.
   Uploading/extracting a prepared candidate is separate from activation.

## Copy-only rehearsal

1. Confirm the current release and running binary hash. Run the established
   `backup.sh` after its normal lock acquisition. The script takes a SQLite backup
   through the running service and only rotates its seven timestamped snapshots.
   It must not be invoked with `--lock-held` unless descriptor 9 is actually
   inherited from the holder of `backups/.backup.lock`.
2. Under that backup lock, copy the newest completed snapshot into a uniquely named
   `backups/metadata-rehearsal-RELEASE/` directory and record the original snapshot
   path/hash. Release the lock after the private copy is complete. Keep the source
   snapshot and mutable rehearsal input as separate files.
3. Extract the verified archive to a new `releases/RELEASE/` directory. Run the
   candidate with `ASPNETCORE_ENVIRONMENT=Testing`, an isolated database path and
   isolated data-protection directory against the rehearsal copy only. Use the
   same ASP.NET 10.0.12 container, UID 1000, read-only release mount, temporary
   `/tmp`, no production `.env`, and no production data/keys mounts. Execute
   `dotnet Mu.Server.dll --backup /check/validated.sqlite`.
4. Open source and validated copies read-only. Assert schema 3 on both,
   `PRAGMA integrity_check = ok`, and no foreign-key violations. Compare column
   definitions and `Counter(SELECT * FROM table)` for every non-internal table.
   Output table names/counts or pass/fail only, never identity/session/audit row
   contents. Existing report counts must be preserved, not reset to a hard-coded
   zero. A newly created test database should still contain zero reports.
5. Start the candidate against the copied database in maintenance mode on an unused
   loopback-only port. This runtime check is mandatory: `--backup` initializes the
   schema/catalog but does not itself load the lazy requirements/adaptation assets.
   Verify health, list evidence, and detail requirements/adaptation for the four
   original game IDs. Verify a game without collected data stays unknown, the
   source URLs/digests are retained, `muVerified` is false, and account/auth/admin
   requests return `503 service_maintenance`. Compare all table rows again after
   these read-only probes. Stop/remove this candidate container when finished.

## Activation and rollback boundary

Activation requires a separate explicit release decision after the rehearsal.
The existing general `activate.sh` takes a backup and keeps its lock through
replacement and health checks. Its automated rollback changes the binary symlink
only. That is appropriate for this verified schema-3, data-preserving metadata
release, but was not sufficient for the earlier schema upgrade.

For this release, use the optional baseline guard with the directory produced by
the completed rehearsal:

```sh
./activate.sh RELEASE --expected-baseline /home/xrw/amd-dlss-mu-account/backups/rehearsal-RELEASE
```

After acquiring descriptor 9's existing backup lock, the guard reads
`baseline-release.txt` and `baseline-dll.sha256`, validates their formats, and
compares the current relative release target, disk DLL SHA-256, and running
container DLL SHA-256. Any mismatch, missing baseline, or stopped account service
rejects the operation before backup, permission changes, `.env` edits, or `current`
replacement. The lock stays held throughout backup, activation and rollback.
Checking before the lock would allow another activation to change the baseline in
between. Do not hold the same lock in an outer process when invoking this script;
it acquires its own descriptor and would wait on that outer lock.

The original `./activate.sh RELEASE` invocation remains supported without a
baseline guard, including its stopped-service behavior. Deploy the updated script
only after verifying the remote script's expected old hash and retaining a backup;
updating this local source does not update the production script automatically.
Run the isolated guard tests with
`node --test server/deploy/activate.test.mjs`. They use temporary application roots,
mock Docker/network calls, and real advisory locks; no production service is used.

Health alone is insufficient because metadata loads lazily. Immediately after any
activation, verify the public list/detail evidence on the running binary and the
website's same-origin proxy. Confirm private access still requires authentication
and that the website displays source-based hardware assessment without converting
upstream facts into MU reports. Record the actual activated release/hash separately
from local tests and the copy-only rehearsal.

If a metadata/API regression occurs after writes have reopened, keep the current
schema-3 database and switch back to the last compatible binary; do not restore an
older account snapshot and lose intervening writes. A database restore is only
valid when writes were frozen before the final snapshot and stayed frozen through
the failed attempt. Preserve any failed candidate and current database for review.

The local browser-acceptance instance is independent of this process: it uses a
temporary SQLite database, `Testing`, `Maintenance__Enabled=true`, loopback binding,
and no production secrets. Stop it after browser acceptance is complete.
