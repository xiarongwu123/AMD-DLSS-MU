# Pro survey

Public page: `/survey`. Private results: `/admin#survey` using the existing admin account.

- Twelve planned features, exclusive `none`, priority selected from chosen features, four payment intentions and optional suggestion (1,000 UTF-16 code units). No email, price or billing model.
- `GET /api/survey` returns `definition` and this browser's `response`; establishes/renews a necessary, one-year HttpOnly/SameSite=Strict cookie, Secure on HTTPS. No analytics consent or visit cookie required. All responses are no-store.
- `POST /api/survey`: `{version:'pro-v1',features:[...],priority:'...',payment:'willing|depends|free_only|unsure',suggestion:''}`. For `features:['none']`, priority must be empty. Requires same Origin, JSON and the cookie. At most 30 attempts per IP per ten minutes in bounded process memory. IP is never stored in an answer. Returns `{response}` only after commit; 400 validation, 403 origin, 415 content type, 428 missing cookie, 429 throttling, 503 storage failure.
- `GET /api/admin/survey?days=all|1|7|30|90&page=1` requires the existing admin session and returns ranking, payment distribution, counts and 20-row page within one SQLite read transaction. No public aggregates.
- `survey_responses` lives in the existing persistent `data/analytics.sqlite`, independently of optional analytics initialization. `UNIQUE(version,respondent_hash)` and transactional upsert preserve the original ID/created time. Only SHA-256 of the random 256-bit browser token is stored. No raw token is included in reports or logs. No deletion/retention task touches survey data.
- Date ranges are Beijing calendar days, based on first submission. All statistics reflect latest answers. Selection percentages include `none` responses in the denominator and may total above 100%. `willingRatio` counts only `willing`; no responses means unknown, not a claimed zero willingness.
- Cookie deletion, expiration or another browser permits another response; this is not verified one-person-one-vote. No fingerprinting or cross-device identity.
- Survey text is a versioned server-side catalogue. Do not repurpose option IDs in a live version. Frontend descriptions come from the same catalogue as validation and admin statistics.

## Validation

`node --test tests/*.test.mjs` runs isolated temporary-database tests, including existing analytics/download/feedback regression. `node tests/preview.mjs` starts a localhost-only disposable preview on port 8097 with a test-only admin password; never deploy its data. Browser acceptance artifacts are under `output/playwright/` and are excluded from deployment.

## Deployment and recovery

Site stays at `/home/xrw/amd-dlss-mu-site`, Docker `website`, `127.0.0.1:8088:8080`, existing CF tunnel. Preserve `.env`, `data/`, `public/release.json` and downloadable EXEs. Before upload, back up source and take an online SQLite backup with `node:sqlite` backup API, then verify `PRAGMA integrity_check`. Build the image before restarting the service. The new table is additive; rollback old code/image without replacing the current database, so collected answers survive. Do not restore a pre-survey database as a code rollback.
