# v2.0.1 Magpie mirror delivery

## Source and client

- Base: `origin/main` at `e274f8549c83c6074fdc666be83cd8840712a8d3`.
- Client/release commit: `bc65325a9892eece1db0fe6612ee3854bc430e47`.
- The existing account/compatibility working tree was left unchanged. Work used a separate managed worktree and branch `codex/magpie-download-mirror`.
- Client order: MU HTTPS mirror, pinned GitHub Release URL, GitHub asset API. All attempts enforce the existing package size and SHA-256 before installation. Existing Magpie installations/configuration are reused.
- MU Windows x64 single-file package: version `2.0.1.0`, 201095999 bytes, SHA-256 `f30315f9bdef2701c4a6d91c96e0dc43a6d8ad26152a01441e24ac0e9e26fd95`. Informational version records the release commit above.

## Deployed mirror

- Host: existing VPS website at `/home/xrw/amd-dlss-mu-site`; container remains bound to `127.0.0.1:8088` behind the existing Cloudflare Tunnel.
- Route: `https://amd-dlss-mu.claude-api.cn/mirrors/magpie/v0.6.8-experimental.1/efb41e5177a628c0742566a887660a5a50e159f824cbfbf9f0620b2cc3b6803c/Magpie-Experimental-x64.zip`.
- Origin package: upstream Releases original ZIP, 489787536 bytes, SHA-256 `efb41e5177a628c0742566a887660a5a50e159f824cbfbf9f0620b2cc3b6803c`.
- Original package fetched directly on VPS in 7.20 seconds at 68046678 bytes/s. No repacking or executable launch.
- Server and Dockerfile backup: `/home/xrw/amd-dlss-mu-site/backups/magpie-mirror-20260927T154735Z`.
- Previous Docker image retained as `amd-dlss-mu-site-website:before-mirror-20260927T154735Z`.

## Verification

- 102 client management/download/Magpie assertions passed. macOS tests used a real-path `TMPDIR` to avoid `/var` symlink rejection.
- Windows x64 publish succeeded; 17 updater/PE version assertions passed against the final EXE.
- Mirror HTTP tests passed: concurrent full downloads, integrity check, HEAD, immutable caching, ETag, byte/suffix/open-ended ranges, invalid ranges, If-Range fallback, unknown routes/methods, corrupted/missing files.
- Public full-file download returned 200, exactly 489787536 bytes and the upstream SHA-256; 221.80 seconds, 2208269 bytes/s (about 2.1 MiB/s).
- Subsequent public HEAD reported Cloudflare `HIT`; byte ranges returned 206 and an out-of-bounds request returned 416.
- Sequential 16 MiB samples from the same development network: GitHub 1718775 bytes/s in 9.76 seconds; mirror 2005122 bytes/s in 8.37 seconds, about 17% higher average throughput for this sample. This is a single local-network comparison, not an all-ISP benchmark.
- Existing website health, compatibility search and old active EXE download remained healthy after the mirror deployment.
- Website v2.0.1 publish script was exercised against a disposable copy before production activation. It verifies the final package before switching release metadata and preserves the earlier announcement.
- GitHub v2.0.1 is published as the latest stable release, targeting client commit `bc65325`; its asset digest and size match the final package above. Code and delivery docs are pushed to `main`.
- Website activation completed: public metadata reports `v2.0.1` / `stable`, download page shows the new mirror announcement, and EXE HEAD returns the expected size and ETag. A full public HTTPS EXE transfer initiated from the VPS returned 200 with 201095999 bytes and the exact final SHA-256.
- Website metadata/page rollback backup: `/home/xrw/amd-dlss-mu-site/backups/release-v2.0.1-1790524650610`. The v2.0 EXE remains intact.
- Windows GUI launch, real game use and mainland ISP-specific throughput require device acceptance; compilation and network verification do not establish those results.

Future website deployments must retain `mirrors.mjs` and the added server/Dockerfile hooks; the website source is maintained separately from this client repository. See `deploy/magpie-mirror/README.md` for reproduction and rollback.
