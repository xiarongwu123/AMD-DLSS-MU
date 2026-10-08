# Apple-style public website delivery

Deployed on 2026-10-06 (Asia/Shanghai) to https://amd-dlss-mu.claude-api.cn/ from the supplied `AMD-DLSS-MU-Apple` directory.

Only 60 allowlisted static files were deployed: seven public pages, their scripts/styles, images/videos and robots.txt. Existing admin HTML/assets, server modules, Docker/configuration files, release metadata and executable packages were preserved. Both website and account container IDs/images remained unchanged; no restart was needed. Source changes are retained under `website/public`.

Deployment adaptations:

- Externalized inline styles/scripts to comply with the existing CSP without changing server/admin behavior.
- Kept the live v2.0.7 package/hash and loaded current release information from `/release.json`; the attachment's v2.0.6 metadata was not deployed.
- Initialized the existing survey cookie before submission.
- Connected the new compatibility results to existing detail/hardware APIs instead of reopening the same query page.
- Added the existing favicon reference. Kept runtime-generated sitemap and SEO behavior.

Validation:

- All 60 deployed files returned HTTP 200 publicly; non-HTML file hashes matched the candidate. Public HTML contained the new scripts and no inline executable scripts/styles.
- Seven pages loaded in a real browser at 390px width with no horizontal overflow or uncaught JavaScript exceptions.
- Compatibility details retrieved live official requirements and upstream records. Feedback and survey submission were tested against an isolated local copy of the deployed server with separate databases; no test submissions were made to production.
- `/admin` returned 200 with noindex, protected overview returned 401, and 26 protected host-file checks passed. This did not exercise an authenticated admin write workflow.
- `/release.json` and `/api/updates/latest` still identified v2.0.7, 201145147 bytes, SHA-256 `cb7b4cc9826fa0fa6d546f97dec4ec8a3f8386d7a1a7b8c046050da29481e998`.
- EXE Range returned 206 and `MZ`; video Range returned 206. Executable content was not replaced.
- Cloudflare's existing injected analytics beacon remains blocked by the unchanged CSP; this is separate from application script execution.

Server backups:

- Original website before replacement: `/home/xrw/amd-dlss-mu-site/backups/apple-static-20261006T100127Z`.
- Website before the final favicon reference: `/home/xrw/amd-dlss-mu-site/backups/apple-static-20261006T100323Z`.

Each backup contains original replaced files, new-file list, protected checksums, deployed checksums and before/after container identities. Restoring a backup must also remove only files named in its `new-files.txt`; retain all admin/data/runtime files.

Reproduction tools: `website/deploy/import-apple-site.mjs`, `prepare-static-apple.mjs`, `activate-static-apple.sh`, and `verify-apple-static.mjs`. Import requires a currently verified release JSON; staging excludes attached release.json, admin, backend and package files.
