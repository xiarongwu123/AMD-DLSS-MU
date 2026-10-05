# Website client updates delivery

Delivered on 2026-10-05 (Asia/Shanghai).

- Binary source: `cc227a3d4e451d512eeb2848ef93e66064e3f6c3`.
- Release: `v2.0.6`, Windows x64, self-contained single executable.
- Size: `201144451` bytes.
- SHA-256: `f5fdb60460a6075668eba14dab03f617b56ab875beffd147eed19de4ad340b2a`.
- GitHub: https://github.com/xiarongwu123/AMD-DLSS-MU/releases/tag/v2.0.6
- Admin: https://amd-dlss-mu.claude-api.cn/admin#release
- Client manifest: https://amd-dlss-mu.claude-api.cn/api/updates/latest

## Verified delivery

The website patch was deployed after matching all six modified live files
against the baseline. The previous image and source remain under
`/home/xrw/amd-dlss-mu-site/backups/client-updates-20261005T081004Z`.
The activation health checks passed without changing the existing release.

The real EXE was first uploaded and published through the browser UI against
an isolated local server. The final committed build was uploaded to production
through the same authenticated HTTPS chunk API, with 4 MiB requests, using
`website/deploy/publish-upload.mjs`. No EXE was copied to the VPS over SSH.
The resulting website metadata and package SHA-256 matched the local file.

The identical EXE was uploaded to GitHub; the API asset digest and byte count
matched. The release is latest, non-draft and non-prerelease. Public HTTPS
download of the complete website update package returned all 201144451 bytes
with the expected SHA-256. HEAD returned 200 with matching length and ETag;
a byte-range GET returned 206 with the PE `MZ` prefix.

Tests covered authenticated and session-bound uploads, same-origin checks,
chunk limits/order/retries, incomplete and malformed PE rejection, staging
without publication, atomic release switching, immutable older downloads,
tampered package rejection, same-version replacement and rollback rejection.
The existing website suite passed, followed by all three final upload test
groups. Client update validation passed 41 assertions including the real EXE;
management/download integration passed 130 assertions. Windows UI compilation
and the Windows x64 publish succeeded.

## Operational contract

In the admin Software Release tab, select `AMD-DLSS-MU.exe`, enter release
notes, upload, inspect the detected version/size/hash and confirm publication.
The EXE internal version must increase for every distinct update. Failed
uploads or validation leave the currently published version unchanged.

From 2.0.6, metadata checks and downloads prefer the website. Metadata
unavailability or invalid responses fall back to GitHub; download transport,
size or hash failure tries the GitHub release asset. A website-only release
works for new clients. For a usable GitHub fallback, publish the same version
and exact EXE on GitHub as well. The website does not hold a GitHub write token
and does not automatically publish future uploads to GitHub.

Existing 2.0.5 and older clients obtain this bootstrap update through GitHub.
Actual Windows update-helper replacement, restart/rollback and gameplay have
not been exercised on this macOS host.
