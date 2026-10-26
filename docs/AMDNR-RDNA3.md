# RX 7000 lmxxf integration

MU's **兼容版** includes `lmxxf-rdna3`, using **AMDNR by 3zwr1**:
https://github.com/3zwr1/AMD-NR---OptiScaler

The neural network port and original HIP kernels are **lmxxf (Kien)**:
https://github.com/lmxxf/dlss5-on-amd-9070xt-porting

## Selection and scope

- Compatibility mode defaults to the AMDNR RDNA3 route on RX 7000 when no remote rule selects another backend. The advanced selector can explicitly choose `lmxxf · RX 7000（AMDNR）` or Daniel.
- RX 6000 keeps Daniel. RX 9000 keeps the existing routes. The original lmxxf and Mochizuki hardware guards remain intact.
- This package is for 64-bit games using the supported DX11, DX12 or Vulkan paths. Bridged interfaces and actual performance still require game testing.
- Generic selection is experimental; a downloaded package or a running menu does not establish successful NR execution or game stability.
- Existing quality, frame-rate, custom, author, Magpie and legacy installers are preserved. This adds a backend inside the compatibility route.

## Package contract

`assets/amdnr-rdna3-0.3.5.8.json` pins AMDNR `Alpha0.3.5.8`:

- Download the original 1,008,053,921-byte archive on demand from the author's GitHub release. Nothing is embedded into MU's EXE except the pins and attribution notice.
- Verify the complete archive and all 27 selected files by SHA-256. Extract the matched `OptiScaler.dll`, `LmxxfNrRuntime.dll`, authenticated `LmxxfNrRuntime.pak`, dependencies and original notices together.
- Do not substitute TheAutomatic's runtime/host, modify the pak, import the source-available launcher, run Setup scripts, or deploy the optional FSR4Fast/HelixSR/d4r packages.
- Keep the original in-game credits. Copy the licence, upstream README and known limits into the game's `Licenses` directory; MU's About page credits AMDNR by 3zwr1 and lmxxf.

The package is an experimental upstream release. Its release notes state that final in-game qualification and the corresponding-source release are pending. This integration does not assert otherwise.

## Configuration and recovery

Write only MU-managed INI keys: `Enabled`, `NrBackend=lmxxf`, `Passes=1..3`, `AmdLmxxfFastMode` and `AmdLmxxfTierSnap`. The two performance settings default to true and are adjustable in advanced settings. Preserve FG, XeFG, proxy, custom sections, comments and encoding.

Refuse a foreign proxy, mismatched package files or an existing external `AmdRuntimePath`. Use the existing transactional manifest, atomic file writes, installation-time hashes and backup/recovery flow. Manifest backend is `lmxxf-rdna3`; package version is `0.3.5.8`; record version is `AMDNR 0.3.5.8`. Track the pak explicitly and protect files later changed by other tools.

Shared presets retain the host family through the component version and preserve the Fast mode/tier settings. Applying one on another GPU still goes through hardware validation. Server code includes the new settings/schema; deploy that server update before sharing these presets through an older server.

## Remote rules

New smart rules use `scheme=compat`, `backend=lmxxf-rdna3`, `componentVersion=0.3.5.8`, `architectures=[rdna3]`, `bitness=64`. Dependencies are `hip` and `amdnr-rdna3-package`. The original host's Inline/early-wrapper/enhanced-barrier switches do not apply. Publish these rules only to updated clients; older clients reject unknown backends.

## Verification

Automated tests cover selection, unchanged existing hardware routes, bounded passes, host isolation, pinned resources, INI preservation, sharing, foreign-file conflicts, cancellation rollback and restoration. A separate test command checks the actual archive and file transactions:

```sh
dotnet run --project tests/Management.Tests.csproj -- verify-amdnr-rdna3-archive /absolute/AMDNR-v0.3.5.8.zip /absolute/test-output
```

Run GPU qualification separately on a Windows RX 7000 machine. Record the exact GPU, driver, game build, API, startup, completed NR output, frame pacing and restore result. File-only tests do not replace that qualification.
