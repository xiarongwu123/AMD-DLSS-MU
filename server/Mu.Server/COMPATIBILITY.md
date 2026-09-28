# Compatibility V1

The API stores player-reported outcomes for a complete game/hardware/software
environment. A successful report is an observation, not a guarantee that another
version, driver, GPU or setting works. Authentication identifies a reporting account;
it does not prove that the account ran a game or that DLSS was actually active.
No sample compatibility reports, success rates or verified hardware claims are seeded.

## Routes and authorization

The original client routes require the existing `Client` bearer policy. The service
also reads live account/feature state on every request; the admin feature console
may disable either client key or change its minimum tier to Pro. Standard is the
initial minimum tier. The separate public GET routes described below permit
anonymous website queries and do not grant submission or account access.

| Method | Route | Feature | Response |
| --- | --- | --- | --- |
| GET | `/api/v1/compatibility/games?q=&gpu=` | `compatibility.read` | `CompatibilitySearchResponse` |
| GET | `/api/v1/compatibility/games/{id}?gpu=` | `compatibility.read` | `CompatibilityDetail` |
| POST | `/api/v1/compatibility/tests` | `compatibility.submit` | `CompatibilityTest` |
| GET | `/api/v1/compatibility/public/games?q=&gpu=` | `compatibility.public.read` | `CompatibilitySearchResponse` |
| GET | `/api/v1/compatibility/public/games/{id}?gpu=` | `compatibility.public.read` | `CompatibilityDetail` |
| GET | `/api/v1/compatibility/public/gpus` | `compatibility.public.read` | `{ "items": ["GPU model"] }` |

`compatibility.public.read` is an independent, initially enabled visibility switch.
Public routes check its current `Enabled` value (missing/disabled returns `403
feature_disabled`) without evaluating account membership or `MinimumTier`. The
admin UI exposes only public access and disabled options for this key. Disabling
client `compatibility.read` does not close public visibility; disable the public
key to close website access. The public switch does not affect health, client
permissions, account APIs or submission. A `Pro` tier accidentally written to the
public feature does not turn an anonymous route into a membership-gated route.

Public search/detail reuse exactly the authenticated query implementation, filters
and aggregation. The GPU selector contains only names present in real report rows,
grouped by case-insensitive exact model key, excluding `unknown`, ordered by that
key and capped at 200. It is empty until a real GPU has been reported. Public routes
accept only GET; there is no anonymous submission endpoint. Returned details contain
environment snapshots, outcomes, notes, timestamps and stable tester pseudonyms,
never an account ID, email, session, password or user profile.

Public GETs share a dedicated 60-requests-per-minute fixed window per effective
client IP, with no queue, on top of the existing 180-request global per-IP limiter.
Exhaustion returns the standard `429 rate_limited` body and `Retry-After`; it does
not exhaust the dedicated budget of private routes or readiness. `q` is limited
to 200 characters, `gpu` to 160 and game IDs to 64. Searches use literal bound
parameters rather than SQL patterns. The website should call these routes through
its own same-origin read-only backend proxy; no permissive cross-origin policy is
enabled on the account service.

DTOs are shared with the desktop client via `src/CompatibilityModels.cs`, linked into
the server project. Query searches names, seeded aliases and Steam App IDs. Search
accepts `page` / `pageSize` (default 1 / 50, maximum page size 50), ordered by name
then stable ID. It returns `items`, `total`, `page`, `pageSize` and `catalogTotal`.
A blank query pages through the full catalog. Invalid or overflowing pagination
returns 400. The optional GPU filter is an exact, case-insensitive model-name match;
it is never a substring match that could mix XT and XTX hardware.

Counts and recent tests in both search/detail respect that GPU filter. Detail's
`gpus` always summarizes every reported GPU for the selected game, including when
the current GPU has no reports. `tests` contains at most the newest 50 matching
reports, while counts and last-tested time use all matching reports. Games with no
matching reports have zero counts, null last-tested time, and `untested` status.
All successful, all failed or all partial reports produce `success`, `failure` or
`partial`; any mixture produces `mixed`. `untested` is never an accepted report result.

## Published requirements and upstream adaptation evidence

Search items additionally expose `evidence: { hasRequirements, modStatus }`.
`hasRequirements` means the Steam source supplied minimum or recommended requirement
text. `modStatus` is an OptiScaler upstream state (`working`, `not_working`,
`platform_limited`, or `mixed` when matched source entries disagree), or null when
there is no exact reviewed Steam identity match. These fields are independent of
player observations and do not change `counts`, `status`, or the GPU report filter.
Lists do not include full requirement/adaptation text.

Search responses also expose optional global `coverage: { requirements,
modCompatibility }`. These are counts of distinct games in the current database
catalog: `requirements` counts only Steam records with `available` requirements;
`modCompatibility` counts only exact Steam matches with at least one upstream
adaptation entry, regardless of its working/non-working/platform-limited status.
Unmatched upstream titles, failed/pending requirements collection and custom games
without matched sources do not inflate coverage. Query text, GPU filter, page and
page size do not affect these global counts. They must be displayed separately
from `catalogTotal`. Older responses can omit `coverage`; absence means unknown,
not zero coverage.

Detail responses add optional `requirements` and `modCompatibility` fields.
`requirements` contains `provider`, `status`, `reason`, `sourceUrl`,
`sourceRetrievedAt`, `sourceSha256`, `minimum`, and `recommended`. Status is
`available`, `not_provided`, or `unavailable`; a retrieval failure cannot become a
claim that the game has no hardware requirements. Each tier contains nullable
`text`, `os`, `processor`, `memory`, `graphics`, `directX`, `storage`,
`additionalNotes`, `memoryMb`, and `storageMb`. Text is plain source text, not HTML.
Missing or ambiguous extracted values remain null. Numeric memory/storage values
must retain their corresponding source text; they are not performance estimates.

`modCompatibility` is an array of exact-matched upstream entries with `id`, `name`,
`sourceProvider`, `sourceUrl`, `sourceRetrievedAt`, `sourceCommit`, `status`,
`upscalerInputs`, `requiredMod`, `notes`, `testEnvironment`, and `muVerified`.
`requiredMod` is `none`, `third_party_upscaler`, or `luma_ue`. The optional test
environment retains nullable `optiscalerVersion`, `gpu`, and `os` facts from the
source. `muVerified` is always false: an OptiScaler Wiki observation is not an
AMD-DLSS-MU test or a guarantee about the requesting machine. Source links use the
fixed, audited upstream Wiki commit. Unmatched upstream titles remain in the audit
resource but are not assigned a guessed Steam ID or inserted as new games.

Both resources are embedded release assets, validated and looked up by Steam ID;
requests never scrape external sites or write requirements into account tables.
This addition keeps schema version 3 and accepts older DTO responses with no new
fields. Custom games have null requirements and an empty adaptation array. The
website must render supplied text as text, retain source attribution, and avoid
claiming that a GPU model alone establishes whole-machine playability or FPS.

## Submission and environment

The submission includes a client-generated GUID `submissionId`, optional `gameId`,
required `gameName`, optional numeric `steamAppId`, full `environment`, `result`,
optional `failureReason` and optional `notes`. Environment includes:

- GPU name/vendor, optional VRAM in MB and architecture, driver version.
- OS version, system DirectX information, game version, user-confirmed render API.
- DLSS, DLSS 5 and MU tool versions, relevant settings and other Mods.

Unknown values must be explicit `unknown` strings where a required string cannot be
read. Optional VRAM/architecture remain null if unknown. The render API accepts
`DX11`, `DX12`, `Vulkan`, or `unknown`; system DirectX capability does not establish
the game's runtime API. Reports accept `success`, `partial`, or `failure` and these
problem reasons: `startup_crash`, `load_failed`, `menu_missing`, `game_update`,
`anti_cheat`, `visual_artifacts`, `performance`, `unknown`. A missing reason for a
partial/failed report becomes `unknown`; successful reports cannot have a problem reason.

The service validates null environments, nested GPUs, enums, numeric Steam IDs,
GUIDs, control characters and string limits before storage. GPU names are limited
to 160 characters; game names to 200; version fields to 100; OS to 200; settings
and notes to 2000 each; other Mods to 1000. Request bodies retain the server's 16 KiB
Kestrel limit. The server chooses account and timestamp; neither comes from client
data. Returned `tester-...` identifiers are stable pseudonyms derived from random
account IDs. Raw account IDs and emails are never returned with tests.

With `gameId`, a supplied Steam ID must match that exact game. Without a Steam ID,
the name must match the selected catalog name. Mismatches return `409
game_identity_mismatch`. Clients should explicitly match the local installation
to the intended game edition and submit its selected canonical catalog name.
Without `gameId`, an existing Steam ID reuses its catalog game even if the local
display name is translated. New Steam IDs create a catalog entry. Non-Steam titles
reuse an exact normalized name, rejecting ambiguous names instead of guessing.
User-created directory metadata is not official game support metadata.

## Deduplication and quotas

Insertion uses one SQLite immediate transaction for live feature checks, replay
lookup, quotas, catalog resolution and storage. Database unique indexes additionally
enforce both duplicate boundaries. Identical `submissionId` replays return the
original report without changing counts; a changed normalized payload returns
`409 submission_conflict`. Store the same GUID and payload across network retries.
Changing the result or notes with an already-used GUID is not an edit operation.

The same account/game/full environment may be reported only once per UTC day,
regardless of result or notes (`409 environment_already_reported`). Text case is
ignored in the environment fingerprint. This is basic duplicate resistance, not
Sybil protection or proof of trustworthy outcomes. Different accounts may test
the same environment. Limits are 20 new reports per rolling hour and 100 per
rolling 24 hours per account (`429 compatibility_rate_limited`), in addition to
the existing server-wide per-IP request limiter. An identical replay does not
consume a new report slot but still requires current submission permission.

V1 omits screenshots, rankings, reported compatibility percentages, recommendations,
test orchestration and moderation UI. The website supports anonymous read-only
queries; submitting observations still requires a logged-in client. The user
explicitly reviews/submits one observation; there is no background telemetry upload.

## Seed catalog provenance

The embedded catalog now contains 2,001 official Steam Windows game identities,
763 distinct official Simplified Chinese names, and 307 exact matched NVIDIA
list-membership references. See `Data/Catalog/README.md` for source URLs, audit
hashes, exclusions and deterministic replay. `game.catalog` returns optional
metadata/source links; references do not create or alter compatibility reports.
The four original game identities and links are retained:

- [Cyberpunk 2077, 1091500](https://store.steampowered.com/app/1091500/).
- [Grand Theft Auto V Legacy, 271590](https://store.steampowered.com/app/271590/).
- [Grand Theft Auto V Enhanced, 3240220](https://store.steampowered.com/app/3240220/).
- [Black Myth: Wukong, 2358720](https://store.steampowered.com/app/2358720/).

Developer/engine fields remain null; neither title identity nor a Steam listing
establishes rendering API, mod compatibility or DLSS availability.

## Verification

Run the three existing server test projects serially. Service tests exercise real
SQLite, concurrent idempotent submission, concurrent same-environment rejection,
both account quotas, stable anonymous identity, filtering, mixed status and the
50-row detail cap. HTTP tests verify all three authentication boundaries, immediate
admin feature/Pro denial, invalid nested JSON, game edition mismatch and wire
round trips. Migration tests start from the frozen version 1 schema and a derived
version 2 schema, check preserved accounts/credentials/sessions/audit/rules, and
inject failure at marker update to prove transactional rollback and successful retry.
Public HTTP tests verify fresh anonymous reads, no seeded reports/GPUs, unchanged
private authentication, independent live visibility switches, GPU filtering and
deduplication, unknown hardware, hostile literal queries, input bounds, absence of
account identifiers, and the dedicated rate limit without affecting health/private reads.
These are automated local checks, not Windows gameplay or production deployment acceptance.
