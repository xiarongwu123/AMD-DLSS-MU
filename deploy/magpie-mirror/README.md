# Magpie release mirror

The website source currently lives outside this repository. These additive deployment files integrate with its verified 2026-09-27 baseline without replacing account, compatibility, analytics or survey routes. `install.mjs` refuses a changed server baseline.

The original upstream package is stored at:

`/home/xrw/amd-dlss-mu-site/data/mirrors/magpie/v0.6.8-experimental.1/Magpie-Experimental-x64.zip`

Upstream: https://github.com/SAOG0721/Magpie/releases/tag/v0.6.8-experimental.1

- Size: 489787536 bytes
- SHA-256: `efb41e5177a628c0742566a887660a5a50e159f824cbfbf9f0620b2cc3b6803c`
- Public path: `/mirrors/magpie/v0.6.8-experimental.1/<sha256>/Magpie-Experimental-x64.zip`

Download the ZIP on the VPS using HTTPS and a `.partial` name, verify size and SHA-256, then rename atomically. Preserve the original ZIP including upstream licenses; never execute it on the server. Package files must be readable by the existing website container. The HTTP handler never fetches upstream on demand and only exposes the pinned path. It verifies file identity and digest, supports HEAD, ETag, If-Range and single byte ranges, and allows immutable public caching. Errors are not cacheable.

Run `node --test deploy/magpie-mirror/mirrors.test.mjs` locally. Upload `mirrors.mjs`, `install.mjs`, and `deploy.sh` together, then run `bash deploy.sh` on the existing VPS. The script backs up the website source and current Docker image, builds before restarting, probes the mirror, and restores the previous code/image on failure. It never restores the database. Keep `mirrors.mjs` and its server/Dockerfile hooks in all future website deployments.

`publish-v2.0.1.mjs SITE_ROOT SHA256 SIZE` verifies the already staged immutable MU EXE under `data/packages/v2.0.1-<sha256>.exe`, requires active v2.0, preserves previous public files in `backups`, adds the v2.0.1 announcement, and switches `data/release.json` last. Publish the GitHub release before this step. Old EXEs remain for rollback. Roll back metadata/pages from this specific backup; do not restore a database snapshot.

The client tries the mirror, then the original release URL, then GitHub API. Each attempt enforces the same size/digest. Existing installs and configuration are reused. Server HTTP Range support does not imply cross-session resume in the MU client, which still restarts a failed source.
