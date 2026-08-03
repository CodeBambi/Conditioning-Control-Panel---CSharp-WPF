# Modular Installer + Content Packs Plan

**Goal:** shrink the installer from 1.66 GB to ~600 MB by moving version-stable audio and mods
out of the box, hosted as assets on the `vX.Y.0` GitHub release of each minor cycle,
downloaded once and re-used across every patch of that cycle. First-run onboarding gets a
mod picker (only CCP Default ships in-box).

Status: **APPROVED — implementation in progress** on `feat/content-packs` (worktree `ccp-wt-packs`). (2026-08-03)

---

## 1. What ships where

### Stays in the installer (~1.36 GB uncompressed → ~600 MB installer)
- exe (900 MB single-file), dlls, PNG art, models (.onnx/.fst), web code (JS/HTML/CSS)
- **All manifests** even when their audio leaves: `bark_rules.json`, `app_clusters.json`,
  per-mod `bark_rules.json`/`mantras.json`, `vo_manifest.json`, `sfx_manifest.json`,
  DTRH `barks/manifest.js` (ES-module import at load time — missing = silent infinite loader spin),
  `vn/manifest.json`
- **Small instant-need SFX (~13 MB):** `Resources\sounds` root (chimes, giggles, lvup…),
  `bubbles\`, `chaos\` (fallback chains in `ChaosSfx.cs:20-41` would otherwise play
  wrong-but-plausible sounds), `sub_audio\` (21 files, base64-inlined into intake page),
  `AwarenessPresets\audio\`, `mindwipe\`/`braindrain\` (user-fillable)
- Tutorial videos (83 MB) — candidate for a later pack, not this phase

### Moves to release-hosted packs (~1.36 GB of payload)

| Pack | Contents | ~Size |
|---|---|---|
| `audio-base` | `Resources\sounds\flashes_audio` (baseline avatar voice, 118 files) | 46 MB |
| `audio-web` | `Resources\web\intake\assets\{vo,sfx,music}` + `Resources\web\dtrh\assets` non-persona audio (bubbles, drone1.mp3, **cheshire tutorial VO**) | ~151 MB |
| `mod-bambi` | `Resources\sounds\companion_audio\mods\builtin-bambisleep` | 77 MB |
| `mod-sissy` | `mods\builtin-sissyhypno` (incl. event_audio, flashes_audio, portraits) + DTRH `assets\barks\sissy` | ~331 MB |
| `mod-locked` | `locked-resources.ccpmod` art + `mods\builtin-locked` audio + DTRH `assets\barks\circe` | ~308 MB |
| `mod-drone` | `drone-mode.ccpmod` (184 MB: 162 MB PNG + 22 MB audio) | 184 MB |

Per-persona DTRH barks travel with their mod pack, so a user who never downloads Sissy never
pays for Sissy's 160+ MB of DTRH barks.

**Download profile:** fresh install = 600 MB installer + 46 MB base (auto) + chosen mod.
`audio-web` downloads lazily (first DTRH/Intake entry, or idle background). Typical first-run
total ≈ 850 MB–1 GB vs 1.66 GB today — and patches (X.Y.1+) are installer-only forever after.

## 2. Hosting: GitHub release per minor cycle

- Packs + `content-manifest.json` uploaded as assets on the `vX.Y.0` release
  (this cycle: the **existing v6.6.0 release**, next cycle v6.7.0, etc.).
- Client derives the URL from its own version — **no GitHub API call, no rate limits**:
  `https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases/download/v{major}.{minor}.0/<asset>`
- `content-manifest.json` per pack: `{ id, file, sizeBytes, sha256, contentVersion, targetRoot }`.
  `contentVersion` only bumps when the pack's bytes actually change → a user who has
  6.6-cycle packs and updates into 6.7 re-downloads **nothing** unless we changed the audio.
- GitHub per-asset limit is 2 GiB; largest pack (`mod-sissy`, ~331 MB) is fine.
- Fallback: manifest 404 (X.Y.0 not published yet) → retry previous minor, then give up
  quietly and retry next launch. Never block startup.

## 3. Runtime: where downloaded content lives + path resolution

Target: `%LOCALAPPDATA%\ConditioningControlPanel\content\` (`App.UserDataPath`), mirroring the
install-dir layout (`content\Resources\sounds\...`, `content\Resources\web\...`).
Install dir is not writable under Program Files — non-negotiable.

### C# side — one probe helper
New `ContentLocator.Resolve(relPath)`: BaseDirectory first (dev builds + legacy installs),
then `UserDataPath\content\`. Route through it:
- `ModResourceResolver.ResolveAudioPath` (already central for shared sounds)
- `CompanionPhraseService` (VoiceLineFolder/EventAudioFolder/CompanionAudioFolder anchor)
- `BarkService.ResolveBarkAudio` tier 2, `BarkRuleLoader`, `MantraVoiceService`
- `SubliminalService`, `MindWipe`/`BrainDrain`, `KeywordTriggerService`, misc one-offs

### Web side — second virtual host
WebView2 hosts pin `ccp.game` → `{exe}\Resources\web` in 6 places. Add `ccp.content` →
`UserDataPath\content\Resources\web` (precedent: `ccp.mod` in `DtrhHostService.cs:106`), and a
tiny JS base-URL shim: audio URL builders (`intake/render/audio.js`, `dtrh/engine/driftChain.js`,
`cheshireVn.js`, `vnPortrait.js`, `menuMusic.js`) prefix an `AUDIO_BASE` that probes
`ccp.content` first. Missing audio already degrades silently in all three JS engines.

### Mods (drone/locked)
Downloaded mod packs extract via the **bundled-builtin path** (`SanitizeManifest` + forced-id,
`ModService.cs:1435-1444`) into `builtin_mods\<id>\` — not the user-mod path (no id-forcing).
Replace the mtime-based re-extract trigger with a `pack.json` contentVersion stamp.
Extraction moves off the startup thread (today 310 MB unzips synchronously in the
`ModService` ctor).

## 4. First-run mod picker (onboarding)

- Hook: the designed-out slot at `MainWindow.xaml.cs:441`
  (`// v6.0: ... No first-launch mod picker.`) — after `WelcomeDialog.ShowIfNeeded()` returns
  true, before `StartTutorial()`. `App.Mods` already exists there; `ContentPackService`
  ordering issue is moot at this phase.
- New `ModPickerDialog`: card per mod (art, size, one-liner, download-size badge), CCP Default
  pre-selected as "installed", multi-select, prominent **Skip — download later**. Reuse
  `ModManagerDialog`'s row/detail rendering.
- Copy honesty: mods are free (no gate on selection anywhere), but `firmware_install` (Drone)
  and `kept` (Circe) programs are Premium — say so on the card. Drone currently has **zero
  companion barks** (no companion_audio in the ccpmod) — don't promise a voiced companion.
- Post-onboarding access: Mod Manager gets a per-mod **Download** button for not-yet-fetched
  builtins; `InitializeModSelector()` rebuilt + `ModResourceResolver.ClearCache()` on install
  (null misses are cached per `ActiveModId`).
- New setting: `InstalledContentPacks` (map id → {contentVersion, sha256}) — a set, not a bool.
- Downloads run in background with progress in the picker and a quiet toast on completion;
  skipping = everything degrades to CCP baseline exactly as the graceful-missing behavior
  already does today.

## 5. Upgraders (existing installs)

Inno leaves files it didn't install, so upgraders would keep their old bundled audio in the
app dir — which then **shadows** downloaded packs forever (BaseDirectory probes first) and
goes stale. **Decision: converge.** Add `[InstallDelete]` entries for the stripped audio
folders + the two ccpmod files. Upgraders auto-download the base pack + packs for mods they
had used (`ActiveModId` history) on first launch after update — a one-time download comparable
to what every single update used to cost them.

## 6. Build/release changes

- **csproj:** every group is declared twice — `<Content>` itemgroups AND the
  `CopyContentAfterPublish` target (`csproj:436-454`). Strip audio by **extension** within the
  affected globs (manifests stay), remove the two ccpmod `<Content>` entries. Stripping at
  csproj level (not installer Excludes) keeps publish tree = truth, kills the
  builtin-sissyhypno >MAX_PATH staging problem, and shrinks robocopy staging.
- New `Scripts\build-content-packs.ps1`: zips the 6 groups from the source tree, computes
  sha256, emits `content-manifest.json`. Run on X.Y.0 releases only.
- `/release` skill: X.Y.0 → also build + upload packs; X.Y.Z → nothing new.
- First rollout: upload 6.6-cycle packs to the existing **v6.6.0** release before shipping the
  client feature in a 6.6.x patch.

## 7. Bug fixes riding along (found during mapping)

1. `SubliminalService.cs:83` — unguarded `Directory.CreateDirectory` under install dir in the
   ctor → startup crash under Program Files once `sub_audio` handling changes. Wrap it.
   (Fix regardless of this project.)
2. Positional phrase identity: `DisabledPhraseIds`/`RemovedPhraseIds` key on the **index** into
   the sorted voiceline file list (`CompanionPhraseService.cs:481`) — audio arriving
   post-first-run silently re-targets every user toggle. Migrate keys to filename-based.
3. `drone-mode` id escapes the `builtin-` squat guard in `InstallModAsync`
   (`ModService.cs:344`) — a user .ccpmod claiming `id: "drone-mode"` overwrites the builtin
   registration. Guard exact builtin ids.

## 8. Verification

- Fresh-install VM run: skip-all path (text-only barks, generic voice, silent DTRH — no
  crashes), then download-one-mod path, then download-after-onboarding via Mod Manager.
- Upgrade-in-place run over a real 6.6.3 install: `[InstallDelete]` sweep + auto-redownload.
- Kill mid-download → resume (ranged download); corrupt a pack → sha256 reject + re-fetch.
- `AudioDiag` startup log (`AudioService.cs:486`) is free telemetry for missing groups.
- Offline first run: app fully usable, picker says "download later", retry next launch.

## 8.5 Wave-1 implementation corrections (2026-08-03, supersede §1/§6 where they differ)

- **Measured moving payload is 1,097 MB** (8,429 files), not ~1.36 GB. Pack sizes:
  audio-base 46 / audio-web 151 / mod-bambi 77 / mod-sissy 331 / mod-locked 308 / mod-drone 184.
- **`vn\vo` is per-persona except `cheshire`.** `builtin-bambisleep`, `builtin-locked` and
  `builtin-sissyhypno` ship in their mod packs. **`vn\vo\cheshire` (173 clips, 21 MB) ships in
  `audio-web`, NOT mod-locked** — despite the folder layout, Cheshire is the DTRH FTUE narrator for
  *every* run (`cheshire_script.js` hardcodes `voFolder: 'cheshire'`; `chaosRun.js` mounts it
  regardless of active mod), so routing her with Circe would give every Default/Bambi/Sissy user a
  silent tutorial. There are **no DTRH barks for Bambi** (`assets\barks` = circe + sissy only).
- **`.ccpmod` files live at `ConditioningControlPanel\DroneMod\` / `LockedMod\`** (published to
  `{app}\DroneMod\`, `{app}\LockedMod\`; `ModService.cs:1402/:1484` reads those paths). In packs they
  sit at in-zip `packs/<name>.ccpmod` → land at `content\packs\`; every manifest entry uses
  `targetRoot: ""` plus a `ccpmods: [...]` array Phase C consumes.
- `flashes_audio` = 68 mp3 + 50 wav; sissyhypno has a third manifest `avatar_manifest.json`
  (stays in-box; `AvatarPortraitSet.LoadBucket` has a `ContentLocator.Mirror` fallback so the
  manifest can live install-side while its portraits are content-side).
- **§7 gained a 4th fix:** `FlashService.cs:282` had the same unguarded ctor
  `Directory.CreateDirectory` as SubliminalService — worse, because its target (`flashes_audio`)
  actually leaves the box. Wrapped.
- Only **4** hosts pin `ccp.game` (Dtrh, DtrhSpike, Loom, Intake — all via `ChaosWebViewHost.Mappings`);
  `ccp.content` maps with access kind **Allow** (Deny would block cross-origin fetch from ccp.game
  pages; precedent `ccp.mod`). Pages get `window.CCP_CONTENT_READY` injected at document creation.
- Intake's chimes/pop are borrowed from `dtrh/assets/bubbles/sfx` → intake audio depends on the
  dtrh half of `audio-web`.
- Pack zips are **byte-deterministic** (entries sorted **ordinally**, fixed timestamps) so sha256
  survives rebuilds on different machines → cross-cycle contentVersion carry-over actually works.
  The sort must not be `Sort-Object`: that collates with the current culture, and PS 5.1 (NLS) and
  pwsh 7 (ICU) order the same 118 `flashes_audio` entries differently, so building next cycle's
  packs in the other shell would re-hash all six zips and re-download ~1.1 GB for zero content
  change. `build-content-packs.ps1` uses `[Array]::Sort(..., [StringComparer]::Ordinal)`.
- **Inno `Type: files` wildcards do NOT recurse.** `builtin-sissyhypno\portraits` nests four
  subfolders (`0_base`, `1_l1`, `2_beach`, `3_fishnet`), so `portraits\*.png` swept 0 of its 312
  PNGs and every one would have survived to shadow `mod-sissy` forever on upgraders. That entry is
  now `Type: filesandordirs` on the folder — safe only because nothing but PNGs lives under
  `portraits\` (`avatar_manifest.json` is one level up). Any other moving folder with subfolders
  needs the same treatment or an explicit per-subfolder line.
- **`build-content-packs.ps1` now hard-fails on strip/pack drift.** It carries
  `$CsprojStripPatterns`, a hand-maintained mirror of the csproj `ContentPack*Exclude` properties,
  expands it against the source tree and refuses to build if any file is stripped-but-unpacked
  (ships nowhere) or packed-but-unstripped (double-ships, and the in-box copy shadows the pack
  forever). Editing the csproj excludes without editing `$PackSpecs` is now a build error at pack
  time instead of a silent content bug in the wild. Today: 8,427 stripped files, all packed once
  (+2 `.ccpmod` archives, which left the build by deleting their `<Content>` item, not by a glob).

## 9. Phases

- **A. Plumbing** — ContentLocator probe + call-site routing; `ccp.content` host + JS
  AUDIO_BASE shim; bug fixes §7.
- **B. Pack service** — `ReleaseContentService` (derive-URL, ranged/resumable download,
  sha256 verify, temp-extract-swap into `content\`, events). Reuses ContentPackService's
  transport patterns; skips its encryption (machine-bound AES + obfuscated names break every
  direct-file-path consumer; these are free-tier files anyway).
- **C. Mod install path** — builtin-slot extraction from packs, contentVersion stamps,
  async extraction, ClearCache/selector refresh.
- **D. Picker UI** — ModPickerDialog + WelcomeDialog hook + Mod Manager download buttons +
  9-language loc keys.
- **E. Build/installer** — csproj strip, build-content-packs.ps1, installer `[InstallDelete]`,
  /release skill update, upload 6.6 packs to v6.6.0.
- **F. Play-test** — §8 matrix.
