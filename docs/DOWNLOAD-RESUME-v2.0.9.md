# v2.0.9 download recovery

Based on the official v2.0.8 tag (`cd39f4f`). This local Windows x64 package repairs the download regression without changing the website or server.

## Behavior

- Restore 45-second header timeout and 90-second read-idle timeout. Each request retains a 30-minute limit.
- Retry transient network failures and HTTP 408/429/5xx up to three attempts per source, with cancellable 1/2-second backoff. Permanent HTTP errors immediately fall back.
- Persist the partial beside the destination with the pinned SHA-256 in its name. Hold an exclusive download lock. Resume across attempts, sources and subsequent invocations; Magpie uses a stable destination across client restarts.
- Validate 206 Content-Range offset, total size, response length and byte bounds. Replace the prefix on a full 200 response; discard stale partials on 416. Never promote bytes without full size and SHA-256 validation.
- Distinguish network read failures from cache write/storage failures. Preserve host, attempt, phase, bytes, HTTP status and nested error messages. Task details can be scrolled and copied.
- Cancellation preserves unverified bytes and stops retries. Corrupt or oversized caches are discarded; existing installed/destination files survive failed downloads.

## Validation

Using `/Volumes/SamsungPSS/mu/.tools/dotnet-8/dotnet`, with `TMPDIR` and `--artifacts-path` on the external disk:

- `tests/Management.Tests.csproj`: 136 assertions passed, including 26 new resume/retry/error assertions.
- `tests-ui/Ui.Compile.csproj`: compiled with zero warnings/errors.
- Windows x64 single-file self-contained publish succeeded, with bundled DLL hash `8270b350cd82de5ce89806872cdd6b6a9249b80836b91bbeb3573470744cc206`.
- `tests-update/ReleaseValidation.csproj`: 18 assertions passed against the final EXE, including Windows x64 architecture and RT_VERSION `2.0.9.0`.

Real Windows execution and the native details dialog require device acceptance; cross-compilation is not evidence of that acceptance.

## Local package

- File: `artifacts/v2.0.9-download-fix/AMD-DLSS-MU.exe` in the primary checkout.
- Size: 201147598 bytes.
- SHA-256: `c92608b62ded9e988d50bb05bf309cf96b936340839df3257f2057a042ea0c25`.
- Local delivery only; no website update, GitHub release or server deployment was performed.
