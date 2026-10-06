# v2.0.2 release delivery

- Date: 2026-09-28.
- Base: merged `main` at `7ffc73b`; client build commit: `3b450673264daf59a47ca2f820ab3f7734c73523`.
- Windows x64 self-contained single-file EXE; internal version `2.0.2.0`, informational version `2.0.2+3b450673264daf59a47ca2f820ab3f7734c73523`.
- Size: 201139636 bytes (191.8 MiB).
- SHA-256: `fa55df9fa00b146d483557d46fe5e2d158601a4a3205699a138a1b99a264c473`.
- GitHub: https://github.com/xiarongwu123/AMD-DLSS-MU/releases/tag/v2.0.2 ; published as latest, not draft or prerelease. GitHub asset size/digest match the local EXE.
- Website: https://amd-dlss-mu.claude-api.cn/download ; metadata reports v2.0.2 / stable, with the compatibility-library announcement and previous release notes preserved.
- Immutable VPS package: `/home/xrw/amd-dlss-mu-site/data/packages/v2.0.2-fa55df9fa00b146d483557d46fe5e2d158601a4a3205699a138a1b99a264c473.exe`.
- Metadata/page backup: `/home/xrw/amd-dlss-mu-site/backups/release-v2.0.2-1790561903563`. The v2.0.1 EXE remains available on disk for rollback. No database restore or replacement was performed.

Validation: Windows publish succeeded; 102 management/download/Magpie assertions, 54 compatibility protocol assertions, 30 account assertions and 18 updater/PE-version assertions passed. Node website/catalog/mirror tests passed 76/76; the 7 website UI tests were repeated after announcement changes and passed.

The website publisher was rehearsed on a disposable local copy, verifies package size/digest, refuses a changed active release or concurrent page edits, stages page replacements, and switches active metadata last. Public HEAD returned 200 with the expected size/ETag. A complete HTTPS EXE download initiated from the VPS returned 200 and exactly 201139636 bytes with the expected SHA-256 in 13.61 seconds. Public homepage and download page showed v2.0.2 and the new announcement.

Windows GUI/device execution and real game compatibility were not validated on this macOS build host. Hardware reference conclusions and upstream OptiScaler records remain distinct from MU/DLSS5 real-world validation. This release packages the merged client; unrelated ongoing SEO source edits remain in their original working tree.
