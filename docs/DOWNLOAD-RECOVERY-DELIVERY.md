# Component download and session cancellation repair

Delivered on 2026-10-06 (Asia/Shanghai). The publication API records dates in UTC.

- Source commit: `f57f77a6d1e817a222829a53221573d54e66f7ce`.
- Branch: `codex/dlss-download-recovery`.
- Release: `v2.0.7`, Windows x64, self-contained single executable.
- Size: `201145147` bytes.
- SHA-256: `cb7b4cc9826fa0fa6d546f97dec4ec8a3f8386d7a1a7b8c046050da29481e998`.
- GitHub: https://github.com/xiarongwu123/AMD-DLSS-MU/releases/tag/v2.0.7
- Website manifest: https://amd-dlss-mu.claude-api.cn/api/updates/latest

## Download repair

Mode 1 previously tried two GitHub entries that both redirect to
`release-assets.githubusercontent.com`. It now starts with the independently
hosted website mirror and uses the reviewed release without a GitHub metadata
request. OptiScaler also starts with its website mirror. Fixed sizes and
SHA-256 values remain mandatory, with no installation of incomplete files.

Connection waiting is 45 seconds and read inactivity is 90 seconds. Transient
connection/read failures and HTTP 408/5xx retry once before trying the next
source. Permanent HTTP and digest failures move directly to the next source.
Caller cancellation stops all remaining attempts and removes partial files.
Failure details identify the hostname and processing stage.

The new mirror handler retains the existing Magpie URL and verifies each open
file before HEAD/range/full responses. Deployment matched the live module
baseline and verified staged package hashes before replacement. The old code,
image identity and release metadata are retained at
`/home/xrw/amd-dlss-mu-site/backups/component-mirrors-20261005T165041Z`.
The account service was not restarted or reconfigured.

## Session cancellation repair

An in-flight heartbeat could rotate the refresh token while account-page
navigation canceled its response. The new client finishes and persists a
started refresh before surfacing caller cancellation; no subsequent business
request is sent for that canceled caller. Rotation remains serialized and
bounded by the HTTP timeout and a separate 30-second maximum.

Regression tests reproduce cancellation after rotation starts, for both
heartbeat and startup restoration, then verify the next heartbeat uses the
new token. This addresses UI cancellation. It does not make transport loss,
process termination or server-side replay recovery idempotent, and does not
establish attribution for every historical refresh-401 record.

## Verification and publication

- Account client: 76 assertions passed, including cancellation during rotation.
- Component/download/management/Magpie integration: 136 assertions passed.
- Website: 49 tests passed, including multiple mirrors, ranges, HEAD and tampering.
- Update validation: 41 assertions passed against the final Windows EXE.
- Windows UI compilation: zero warnings and errors.
- Public live mode 1 and mode 2 downloads completed through the actual client
  downloader with GitHub deliberately disabled; all bytes matched the pinned hashes.
- Public HEAD for mode 1, mode 2 and Magpie returned the expected size and ETag.
- The final EXE was uploaded through the authenticated website chunk API;
  publication verified its version, size and SHA-256. GitHub has the same binary
  as a non-draft, non-prerelease release targeting the source commit above.
- The public immutable v2.0.7 download returned all 201145147 bytes with the
  final SHA-256. HEAD returned 200 with matching length/ETag, and a byte-range
  request returned 206 with the PE `MZ` prefix. The public manifest is stable.

Older immutable website update packages and GitHub releases remain available
for rollback. Actual Windows UI navigation, updater replacement/restart,
installation and gameplay were not executed on this macOS host.
