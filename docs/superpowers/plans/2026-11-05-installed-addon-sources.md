# Installed addon sources Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cover the user's installed addons with verified catalog sources and release 0.4.3.
**Architecture:** Extend the existing declarative community source manifest to describe root addons and multi-folder repositories; reuse pinned Git blobs, installation transactions and online checks.
**Tech Stack:** Python standard library, .NET 10, Avalonia, xUnit, GitHub Actions.
**Spec:** `docs/superpowers/specs/2026-11-05-installed-addon-sources-design.md`

## Global Constraints
- Never write to the installed addons or WTF.
- Keep compatible TOCs, dependency closure, path protection and pinned hashes.
- Version 0.4.3; no invented sources or implied in-game validation.

## Review Focus
- Root vs multi-folder repositories must preserve destination paths.
- Existing archive folders must not be owned by two packages.
- Optional modules and protected client folders must not become unsafe dependencies.
- Branch-specific backports must not follow retail default branches.
- Locally edited or unknown versions must not be silently adopted/downgraded.

### Task 1: Inventory and verify sources
- [x] Compare installed folders with catalog; inspect TOCs and verify upstream repositories/branches/layouts.
- [x] Record coverage and exceptions in `docs/addon-source-coverage.md`.

### Task 2: Support and populate source layouts
Files: `tools/community-addons.json`, `tools/build_catalog.py`, `src/ChromieLauncher.Core/AddonUpdateService.cs`, generated `Data/catalog.json`, tests.
- [x] Add failing tests for a multi-folder source; retain root-source and security tests. No non-default branches selected (see ruling).
- [x] Extend source mapping minimally, including source folder prefix where needed.
- [x] Regenerate catalog; verify unique owners, dependency closure, TOC compatibility and runtime file references.
- [x] Run Python self-checks and .NET tests, including downgrade and incomplete-pack checks.

### Task 3: Release and verification
Files: `Directory.Build.props`, `README.md`, `docs/releases/v0.4.3.md`.
- [x] Update version, provenance, source coverage and release notes.
- [x] Run all tests and Native AOT publication; desktop smoke test and package.
- [ ] Review diff, commit/tag and publish through existing signed workflow when credentials permit; verify release assets or report the exact blocker.

## Execution ledger

- Baseline: 132 .NET tests pass. Work proceeds in-place on `release/0.4.3-addon-sources`, not main; no addon/game writes.
- Task 1: complete. 88 non-Blizzard directories, 83 catalog owners; protected/resource directories and three manual addons recorded in coverage document.
- Ruling: no branch option — every selected backport is on its repository's default branch; adding unused configuration would add risk without coverage.
- Ruling: keep APIDocumentation protected and boss_heads outside addon catalog — client protection and absence of TOC; these resources are not installed by the launcher.
- Ruling: leave RaidIDsChromie, CountPack-HotS and DBM-WorldEvents manual — respectively no public source, incompatible upstream TOC, and missing declared runtime file. Costs: these three cannot be installed/updated through the library until upstream/source issues are resolved.
- Ruling: use historical compatible MySlot/NiceDamage variants with explicit warnings — remote source presence does not reproduce customized local code/fonts; conservative version checks and confirmations remain unchanged.
- Ruling: accept ordinary 100755 Lua/XML/media Git blobs only for explicit sources and a seven-extension allowlist — DBM marks required runtime/media files executable in Git; no OS executable, symlink or submodule is accepted or executed.
- Task 2: complete. Multi-folder, prefix, case-insensitive TOC references, DBM-style comment, regular-blob mode and symlink/submodule tests exercised. 137 .NET tests and 1 Python test pass. The traversal regression from filtering dot-paths was caught by the existing test and corrected by validating paths before filtering.
- Online verification: all 3,764 external files downloaded; 332,199,254 bytes, sizes and Git hashes match. No game files modified.
- Task 3 first local validation: Native AOT publication and desktop smoke test succeed (84 detected addons, 150 packs, version 0.4.3). Rebuild required after final-review correction below; first package superseded.
- Final review: independent reviewer found one Important issue, MinimapButtonFrame's upstream LibDataBroker directory name differs from its XML reference. Verified from pinned XML/tree. Fixed using declarative destination-prefix mapping, preserving SourcePath/hash/bytes. Two .NET regression tests and Python mapping check failed first, then passed; full suite 139/139 plus Python 1/1.
- Final: minor (deferred): upstream MBF locale.xml references missing optional Locale-esMX.lua; documented incomplete translation, no invented localized file.
- Ruling: add PathMappings only for the proven MBF library mismatch — required for standalone loading; mappings apply to destinations only and retain path/duplicate/hash checks. All other source contents stay untouched.
- Final local validation after the correction: Native AOT publish succeeds; desktop smoke test passes in all three languages (84 addons, 150 packs). Final local ZIP SHA-256 `c2bfa5d9119dccfb300631f9e649d137e179ae9df21b500c8398232be0fb4cae`. Signed release remains a separate workflow output, with its own verified manifest/checksum.
