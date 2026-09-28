# OptiScaler compatibility facts

`optiscaler-compatibility.json` contains factual observations from the OptiScaler project's public [compatibility Wiki](https://github.com/optiscaler/OptiScaler/wiki/Compatibility-List/67f573d5acd9a719cc799159c798fb978b702446), pinned to commit `67f573d5acd9a719cc799159c798fb978b702446`. The snapshot was retrieved at `2026-09-27T16:22:15.598Z`. Source links include that immutable revision so a later edit cannot silently change the evidence behind a record.

The table has 707 parsed rows and one identical duplicate: 706 unique records, comprising 701 `working`, three `not_working`, and two `platform_limited` source statuses. These counts are computed from rows, not the manually maintained totals in the Wiki heading. This snapshot matches 239 entries in the local 2,001-game Steam catalog by unique normalized complete title. Unmatched records remain in the external facts file with `steamAppId: null`; they do not create new game identities. Edition names, subtitles and numbers are retained, so GTA V Enhanced is not merged with GTA V Legacy. There is no fuzzy matching.

The statuses describe upstream OptiScaler reports. They do not establish native DLSS/FSR support, current support on every GPU, or AMD-DLSS-MU verification. Every record has `muVerified: false`. `upscalerInputs` describes the Wiki's input hooks, not a promise that each output technology runs on AMD hardware. `requiredMod` identifies the source's separate third-party-upscaler or Luma UE categories and explicit REFramework requirement. `none` does not assert that the game has no other conditions; notes and the linked source still apply.

Where an individual game page identifies an OptiScaler version, GPU and OS, those values are retained as the upstream test environment. They are not a test date, driver version, or a MU test. A configuration page shared by multiple games cannot establish a separate test environment for each game; its environment is deliberately omitted. This yields 191 records with attributable environment details, including 111 matched Steam games. Reporter handles and screenshots are not exported.

Notes are short factual paraphrases of technical constraints. They preserve known anti-cheat blocks, API/input restrictions, extra configuration and platform limitations; they do not copy whole source explanations. The manual fact summaries in `scripts/catalog/optiscaler-notes.mjs` are tied to the exact source commit. Refreshing the clone requires reviewing those summaries before collection can proceed. The collector ignores struck-through resolved issue text and marks shared configuration links explicitly.

The project repository has a GPL-3.0 license, but the captured Wiki has no separate license declaration. This dataset does not assert that the Wiki is openly licensed for bulk republication. Original prose and images are not published here. The official project also does not endorse an official manager app or website; MU must not represent itself as such.

## Reproduction and audit

Raw source is a clean shallow clone of `https://github.com/optiscaler/OptiScaler.wiki.git` under `/Volumes/SamsungPSS/mu/.tools/optiscaler-source-20260928/wiki`. The neighboring `optiscaler-provenance.json` records original file hashes, source table lines, catalog hash, matches and summary provenance. Raw source remains outside the public deployment.

```sh
node --test scripts/catalog/collect-optiscaler.test.mjs
node scripts/catalog/collect-optiscaler.mjs --wiki-dir /Volumes/SamsungPSS/mu/.tools/optiscaler-source-20260928/wiki --audit-dir /Volumes/SamsungPSS/mu/.tools/optiscaler-source-20260928
```

The collector is offline: it verifies the clone's official remote, clean state and reviewed commit before parsing. An unknown table status or a conflicting duplicate fails collection. Use `--generated-at` with the original timestamp and `--output` for an independent byte-identical replay. Do not treat missing source entries as incompatibility, or the upstream working state as a guarantee for a different game build, driver, operating system or mod configuration.
