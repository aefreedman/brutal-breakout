# Contributing

Discuss substantial gameplay or architecture changes in an issue first. Keep pull requests focused and preserve the classic catch-line → aim → separate relaunch interaction.

- Original contributions are offered under GPL-3.0-or-later **with the BRUTAL linking permission** in [LICENSE-EXCEPTION](LICENSE-EXCEPTION). By submitting an original contribution, you offer it under those terms, including that permission. Disclose third-party provenance; do not submit material you cannot license that way. See [LICENSING.md](LICENSING.md) for asset/dependency exclusions.
- Use the SDK in `global.json` and the exact CrushKit revision in `.github/crushkit-revision`. See [source builds](README.md#quick-start-source-build) and [development guidance](docs/engineering/development.md).
- Run `pwsh -File .github/scripts/ci.ps1 -CrushKitRoot <kit-checkout>` for the source build/headless test baseline. Ordinary hosted CI does not run the game window, GPU smoke tests, controller/audio checks or benchmarks.
- Keep authoritative defaults in validated JSON, simulation independent of graphics, and immutable publications/per-life identity intact. Add focused deterministic regressions for changed behavior; avoid elapsed-time performance assertions.
- Commit source locks when changing the source dependency graph, not ignored `packages.dev.lock.json` files. Update the kit revision deliberately; never substitute a floating branch in CI or overwrite an existing NuGet version.
- Do not commit private paths, credentials, generated outputs or raw diagnostic dumps. BRUTAL credits/logo/notice requirements and other release obligations remain separate from CI.
