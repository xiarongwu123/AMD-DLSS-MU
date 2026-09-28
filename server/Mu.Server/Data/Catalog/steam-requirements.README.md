# Official Windows requirements snapshot

`steam-requirements.json` contains publisher-stated PC requirements retrieved from Valve's public Steam Store appdetails endpoint, English locale and US region. It records requirements, not benchmarks, guaranteed frame rates, or tested DLSS/AMD compatibility. Requirements may be incomplete or outdated; the linked official store page is the source to consult.

The collector verifies a unique `data.steam_appid` matching the requested catalog ID, `type=game`, and `platforms.windows=true`. The response root key is not trusted: observed official responses can have a different root key, so these anomalies are recorded in the provenance file. No requirements from a mismatched game are attached to the requested ID.

Minimum and recommended tiers contain plain `text` plus optional publisher-labelled OS, processor, memory, graphics, DirectX, storage and additional notes. Missing fields and tiers are `null` and must be displayed as unknown. `status=not_provided` means a valid official game response did not publish requirements; `unavailable` means the response could not be verified or retrieved. Neither status means the user's PC cannot run the game. Unknown pending games are counted separately in `coverage.pending`.

Only unambiguous MB/GB/TB memory and storage quantities are normalized into `memoryMb`/`storageMb`, using factors of 1024. These are unit conversions of declared requirements, not measured disk usage or performance. Multiple capacities or numeric ranges in one field remain unknown rather than selecting an arbitrary value. CPU and GPU strings retain the original alternatives and must not be ranked from their model numbers alone. Literal legacy labels such as `CPU`, `Video Card`, and `Hard Drive Space` map to the same fields; unstructured requirements remain available as full original text.

Original HTML is retained only in SHA-256-addressed response files in the `.tools/steam-requirements-source/<run>/responses/` audit directory. Repository JSON contains stripped plain text; consumers must still render it as text, never `innerHTML`. `steam-requirements.provenance.json` records per-game URL, time, source hash, raw filename, HTTP status, matched inner AppID, and any root-key anomaly, together with the parser source SHA-256. The JSON sidecar hashes the complete exported requirements file.

The serial collector starts requests at least 1.6 seconds apart, honors bounded 429 backoff, retries transient errors, and stops after repeated rate limiting. It saves each processed game atomically and exports a checkpoint after the four initial catalog games, every 20 further results, and exit. The first four games are followed by NVIDIA reference matches, OptiScaler reference matches when available, and the remaining catalog. Reference priorities are re-read while the capture continues.

```sh
node --test scripts/catalog/collect-steam-requirements.test.mjs
node scripts/catalog/collect-steam-requirements.mjs --raw-dir ../.tools/steam-requirements-source
node scripts/catalog/collect-steam-requirements.mjs --resume-dir /path/to/captured/run
node scripts/catalog/collect-steam-requirements.mjs --resume-dir /path/to/captured/run --retry-unavailable
node scripts/catalog/collect-steam-requirements.mjs --resume-dir /path/to/captured/run --replay-only --output /tmp/steam-requirements.json
```

Resumption verifies the catalog hash and saved raw response hashes before continuing. Existing successful or source-confirmed absent responses are reparsed from those verified bytes, allowing reviewed parser corrections without repeat requests. Missing or modified saved source bytes fail the replay. `--retry-unavailable` explicitly revisits failed entries. Offline replay performs no network requests. Keep the audit directory, including `records/`, `run.json`, and `attempts.jsonl`, and the collector version for reproducible checkpoints.
