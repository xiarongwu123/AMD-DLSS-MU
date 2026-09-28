# Steam game catalog

`steam-games.json` is a dated snapshot of real Steam Store catalog entries, collected by `scripts/catalog/generate-steam-catalog.mjs`. The source is Valve's public store search endpoint, not a third-party AppID list. The collection uses the Games category (`category1=998`), Windows filter (`os=win`), top-sellers ordering, US store region, and English/Simplified Chinese locales. This is a bounded popular-game catalog, not the complete Steam catalog or an enduring popularity ranking.

Each entry contains a verified single Steam AppID, its official English-locale display name, an official Simplified Chinese-locale alternative when different (`nameZhCn`, otherwise `null`), an exactly parsed date displayed by its cited source no later than the snapshot date, a canonical Steam store URL, and retrieval time. A null Chinese alternative does not mean the game lacks Chinese language support. `releaseDate` is a Steam catalog displayed date, not a verified original launch date or a game build/version. Steam search listings can disagree with app-details metadata or an original release date; for example, this snapshot's Half-Life 2 search listing shows 2007-10-10 while its official app details shows 2004-11-16. The snapshot preserves the cited search response rather than silently reconciling different sources. Users should consult the linked official game details for authoritative current release information. Early Access titles with an already-past displayed release date may be included; future or ambiguous displayed dates are excluded.

The importer excludes packages/bundles/multiple AppIDs, rows without a Windows platform icon, future or unresolved dates, and conservative demo/playtest/soundtrack/dedicated-server title matches. Classification as a game comes from the official Games search filter. No engine, developer, rendering API, DLL, GPU compatibility, test result, or game version is inferred. Catalog membership never establishes DLSS or AMD compatibility.

Explicit continuity AppIDs are included in addition to the bounded top-seller selection. By default this includes GTA V Legacy (`271590`), whose prior catalog link must remain available even when it no longer appears in top-seller results. Both locale names and release facts are fetched from the official `store.steampowered.com/api/appdetails` endpoint. Supplements require `type=game`, `platforms.windows=true`, `release_date.coming_soon=false`, an exact past release date, and the requested AppID in the response. They are not inserted from a handwritten name list. A 2,000-game selection plus one distinct verified supplement produces 2,001 catalog entries.

`steam-games.provenance.json` records every source URL, locale, retrieval timestamp, full SHA-256, byte length, source response filename, collection rule, and exclusion count. Original responses are stored outside the repository in the selected `.tools/steam-catalog-source/<run>/responses/` directory alongside `run.json`. Keep that directory to reproduce the exact snapshot; refreshing live data can legitimately change ordering, names, dates, and available results.

The catalog's `source.snapshotSha256` hashes the compact `JSON.stringify(games)` UTF-8 representation. `source.manifestSha256` hashes the exact provenance file bytes. `steam-games.json.sha256` contains the full catalog file hash. Replaying checks every raw response hash before generating output.

From the repository root with the existing Node 22 runtime:

```sh
node --test scripts/catalog/generate-steam-catalog.test.mjs
node scripts/catalog/generate-steam-catalog.mjs --raw-dir /Volumes/SamsungPSS/mu/.tools/steam-catalog-source --count 2000 --minimum 1500
node scripts/catalog/generate-steam-catalog.mjs --replay-dir /path/to/captured/run --count 2000 --minimum 1500
node scripts/catalog/generate-steam-catalog.mjs --supplement-run /path/to/captured/run --include-appids 271590 --count 2000 --minimum 1500
```

Requests are sequential with at least 1.1 seconds between attempts by default. HTTP 429, network failures, and server errors receive bounded retries; no user cookies, credentials, API keys, or private Steam data are used. The default collection stops after obtaining 2,000 eligible top-seller entries or 40 pages per language, then verifies the explicit supplemental AppIDs. It refuses to write fewer than 1,500 games. Online supplementation preserves a copy of the preceding run manifest and retains its raw response files. Offline replay performs no network requests.

## Official technology reference links

`technology-references.json` is a partial layer of factual source references for games in this Steam catalog. It preserves the unambiguously matched title from [NVIDIA's official RTX feature list](https://www.nvidia.com/en-us/geforce/news/nvidia-rtx-games-engines-apps/), its retrieval time and source link. Originally this layer exposed list membership only; it now adds concise, column-specific feature facts for those same matched games. It does not reproduce the full source table, explanatory prose, images, or unmatched games, and does not assert an open-data license.

The source page uses [this JSON document](https://www.nvidia.com/content/dam/en-zz/Solutions/geforce/news/nvidia-rtx-games-engines-apps/dlss-rt-games-apps-overrides.json). The audit snapshot retrieved at `2026-09-27T15:38:26.179Z` contains 867 Game rows and 172 App rows, with SHA-256 `7c54c6f00aa3e93bc4985635825851a7eeb0ca0e22975827fd184b62bd8c02c3`. The live page stated September 15th, 2026 as its update date. Raw source and its manifest remain outside the repository under `/Volumes/SamsungPSS/mu/.tools/compatibility-reference-20260927/`; they are research evidence and are not published with the site. [NVIDIA's terms](https://www.nvidia.com/en-eu/about-nvidia/terms-of-service/) are not an open bulk-republication license; this bounded layer provides minimal facts and attribution links.

The feature mapping follows the captured source page's column legend (HTML lines 6733-6738) and NVIDIA App prerequisites (lines 6498-6506). The local `nvidia-rtx-source.html` SHA-256 is `589027ca2700e337e7b413f48f043453edcc3a83a8dabfa01375e7e96cb9fa22`. It distinguishes:

- `Yes`: the corresponding in-game DLSS/DLAA feature, or listed ray tracing.
- `NV, T` in Super Resolution / Ray Reconstruction: native feature support plus a possible NVIDIA App model upgrade after enabling that feature in-game.
- `NV, T` in DLAA: NVIDIA App can activate DLAA via its Super Resolution override; this marker does not establish native DLAA support.
- `NV, U` in Frame Generation: native Frame Generation with a possible NVIDIA App model upgrade, for RTX 40/50 users after enabling Frame Generation in-game.
- `NV, 4X` / `NV, 6X` in Multi Frame Generation: up to 4X MFG / 6X Dynamic MFG for RTX 50, either natively or through NVIDIA App. The override path requires Frame Generation enabled in-game. The marker does not distinguish native support from override support and never means native DLSS 5.
- `Path Tracing` in Ray Tracing: listed path-tracing support. Blank cells are omitted, never converted to unsupported claims. An entry with no recognized feature facts explicitly says the source did not list those facts.

Every entry retains an explicit statement that NVIDIA feature facts do not establish AMD GPU or AMD-DLSS-MU compatibility. No frame rates, minimum hardware, versions, or test outcomes are inferred from these markers. Unknown non-empty markers in a matched game's reviewed columns abort regeneration rather than guessing a meaning. Labels remain within the shared API limit of 20 facts, each at most 100 characters.

The matcher makes no network requests. It verifies the local source hash and official source URLs against the audit manifest, excludes App rows, and joins unique complete names only. Normalization removes trademark glyphs, folds letter case and Unicode typography, and collapses whitespace; it preserves punctuation, subtitles, edition names, numbers, and words. If either catalog has multiple entries with the same normalized name, all such matches are omitted. It does not use Chinese alternatives, substring matching, fuzzy matching, inferred Steam IDs, or feature markers to force matches.

```sh
node --test scripts/catalog/match-nvidia-references.test.mjs
node scripts/catalog/match-nvidia-references.mjs --source /Volumes/SamsungPSS/mu/.tools/compatibility-reference-20260927/nvidia-rtx-source.json --manifest /Volumes/SamsungPSS/mu/.tools/compatibility-reference-20260927/nvidia-manifest.json
```

For a byte-identical replay, also pass the original output's `generatedAt` value with `--generated-at`. A missing reference means no unique match in this snapshot, not that a game lacks a feature. NVIDIA's list mixes native features and driver overrides and can include announced titles; the output preserves these distinctions where the legend establishes them. This file provides no rendering API, game/runtime version, AMD GPU support, AMD-DLSS-MU compatibility status, user report, or successful test count. The reference layer must remain separate from community test aggregates.
