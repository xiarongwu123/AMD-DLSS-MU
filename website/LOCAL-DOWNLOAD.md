# Server-hosted downloads

The public `/download/file` and legacy `/files/AMD-DLSS-MU.exe` now stream the current verified server-side EXE, without a redirect to GitHub. The app still listens through the existing Docker mapping `127.0.0.1:8088:8080` and Cloudflare Tunnel.

`data/packages/<tag>-<sha256>.exe` stores immutable versioned packages outside the public directory. The current package is v1.3.0, 331222369 bytes, SHA-256 `86d18fe88e59f01df05d80265bf2e65c5154480855a5cd48072d668359f8ead2`. An older unrelated EXE in `public/files` is preserved but no longer served through generic static EXE paths.

Downloads support GET 200, HEAD, single byte ranges/open-ended/suffix ranges (206), invalid ranges (416), ETag/If-Range, attachment headers and streaming with bounded memory. Corrupt or absent packages return 503 instead of redirecting to GitHub. Each unchanged local file is hash-checked before its first delivery in a process; size/mtime/ctime/inode changes invalidate the verification cache. No executable is run on the server.

## Administrator workflow

1. Log in to `/admin#release` and validate an official release tag.
2. Confirm **同步到服务器并发布**. POST `/api/admin/release` returns 202 with a job; GET `/api/admin/release-status` returns progress (both require existing admin auth).
3. Server downloads only from the official repo and approved GitHub asset redirect hosts, checks disk space, enforces a 15-minute timeout and a maximum 2 GiB declared size, verifies complete byte count and SHA-256, then atomically switches `data/release.json`.
4. A running job blocks overlapping releases (409). Failures leave the old active version intact. The page polls status and resumes observing a running job after refresh. Closing the browser does not cancel the authorized server job.

GitHub is used only by the server to obtain releases; end users download from the website domain. `release.json` exposes the local `downloadUrl` and records the original `sourceUrl`. No changes to the Windows app are required.

Jobs are held in memory. A server restart before the release-file switch leaves the old version active; retry the release after restarting. A process killed during transfer can leave a uniquely named `.part-*` file in `data/packages`; such files are never served. Keep old verified versions for rollback and review disk usage before publishing large packages. Do not delete active packages.

Statistics now record `download_request` for accepted full-file/first-range starts; follow-up ranges and HEAD checks do not count as new starts. Historical `download_redirect` events remain included in totals/trends. Counts do not prove completed downloads or installs. GitHub's upstream cumulative downloads are distinct from website downloads.

## Verification / recovery

`node --test tests/*.test.mjs`: 11 test groups cover streaming/ranges/integrity, failed synchronization, asynchronous publish and existing analytics/survey/feedback. `tests/preview.mjs` uses disposable test data and a mocked upstream; it must never be used as production package data.

Before rollout, code backup: `/home/xrw/amd-dlss-mu-site-backups/before-local-download-20260922T0900.tar.gz`; previous Docker image: `amd-dlss-mu-site-website:before-local-download-20260922T0900`. Original iCloud placeholder source is unchanged. Runtime database and credentials are not included in the source archive.

The initial mirror operation does not change `data/release.json`. For an immediate rollout rollback, restore old code/image while keeping runtime data. If rolling back after later local-release publications, old redirect-only code also needs metadata adapted to the official `sourceUrl` (otherwise the new local `downloadUrl` could redirect to itself). Never restore the whole database or remove survey responses as a code rollback.

## Live acceptance — 2026-09-22

- All 11 automated test groups passed. Browser tests on disposable data verified successful publishing, visible progress, failed checksum preserving the prior version, and failure status after refresh.
- Public HTTPS full download through Cloudflare returned HTTP 200 with no redirect. Streamed all 331222369 bytes and computed SHA-256 `86d18fe88e59f01df05d80265bf2e65c5154480855a5cd48072d668359f8ead2`; no EXE was executed or retained on the local machine.
- Public byte range returned 206 and the expected PE `MZ` prefix; invalid range returned 416. Legacy URL HEAD returned 200 with correct length/ETag and `Accept-Ranges: bytes`. Cloudflare reported cache BYPASS.
- Production administrator, survey, feedback, release metadata and database integrity smoke checks passed. Unauthenticated release-status requests returned 401. No production survey answers were submitted; active version remains v1.3.0.
- Docker logs show no application errors, binding remains `127.0.0.1:8088`, package storage is approximately 316 MiB and disk free space was 9.6 GiB at verification.
- Production browser shows the new server-download wording and local download targets. The existing Cloudflare Insights script remains blocked by the site's self-only CSP; no CSP or tunnel changes were made.
- Package hosting removes the visitor's dependency on GitHub for this EXE download. This verification is not a guarantee of throughput or reachability on every mainland ISP, and does not change network dependencies inside the Windows application.
