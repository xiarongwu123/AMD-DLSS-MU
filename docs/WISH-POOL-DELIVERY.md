# Wish pool delivery and preview correction

Verified on 2026-10-08 (Asia/Shanghai).

The first `2.0.8-wishes-preview.1` package used the older `57b6e5c` development baseline. Its page 0 still pointed to compatibility instead of the current home page, and its refresh request could be cancelled before persisting a rotated token. The online wish API was not yet deployed; empty HTTP 404 responses were displayed as a generic account-service error. These are confirmed package defects; the refresh race is covered by a regression test but was not correlated with a specific customer session failure.

The corrected `2.0.8-wishes-preview.2` package is built from `63ff0f8` (the current service integration branch) plus the wish changes and account refresh/error corrections, in the isolated managed worktree `/Users/it/.codex/worktrees/wishes-service/AMD-DLSS-MU`. It retains the home page, game telemetry, SMTP integration, disconnect grace, login form preservation and business-page-only heartbeat. Refresh rotation completes and saves credentials after a caller cancels, while the caller still receives cancellation. Empty HTTP failures display their status; missing wish service responses no longer masquerade as account outages.

Do not deploy the original checkout's server directly: it is still based on account schema 3 and contains unrelated website edits. Use the isolated worktree for further integration and service builds; its account schema remains 4.

## Service deployment

- Previous release: `20261007-game-monitor-e85e024`, DLL SHA-256 `fff2900d671c5331caa869458b898c21f831708bc2e0c4def79e25a7c347b30e`.
- Activated release: `20261008-wishes-5b49c434`, DLL SHA-256 `5b49c4349772d13efcc890479add0721daf6c4be9c382b1ece4f165bd1092076`; running container hash matches the local publish.
- Rehearsal backup: `/home/xrw/amd-dlss-mu-account/backups/wishes-rehearsal-20261008.sqlite` and its paired `.mail.sqlite` file. Normal activation also generated a fresh pre-activation backup through the existing deployment script.
- Candidate binary and production .NET runtime were rehearsed against a copy of the production account database. `verify-wishes-upgrade.py` proved every row in all 19 existing tables unchanged, `integrity_check=ok`, no foreign-key violations, schema version 4 and eight labelled demo wishes.
- Activation used the existing `activate.sh` with the rehearsal's expected release and binary hash guard. Existing environment and mail/telemetry functionality were preserved. `--seed-wishes` inserted the eight examples without creating users or comments.
- After activation: public health 200, anonymous wishes 401 (previously 404), administrator wishes 302 to login, anonymous telemetry upload 401. Account heartbeats continued returning 200. Production integrity is `ok`, version 4 and eight demo wishes.

## Validation

- Portable account tests: 72 assertions, including cancellation during both restore and authenticated refresh, persistent replacement credentials, and empty 404 retaining login.
- Integrated service branch HTTP suite: 421 assertions, including 62 wish assertions.
- Administrator/backup suite: 123 assertions, retaining telemetry and mail coverage.
- Service suites: 29 metadata, 86 compatibility and 156 account assertions.
- Windows x64 self-contained single-file cross-publish and embedded-resource inspection. Actual Windows UI, login and wish actions still need device acceptance; anonymous public probes do not establish an authenticated end-to-end production flow.

The test EXE is under `artifacts/v2.0.8-wishes-preview.2-win-x64/`; it does not replace website/GitHub release files. Exit any running old MU process before launching it because the client uses a per-user single-instance mutex.

Final EXE: 203312806 bytes, SHA-256 `0c7164774bbc0743ea09ff43c9399aa2dea82094d30e96c9ce3936e9bb2a3483`. Bundle inspection verified the 2.0.8.0 assembly, embedded wishing-pool artwork and the expected DLSS component hash. Publish completed without warnings or errors.
