# SP-055 — One active-pool definition (asset deselection parity): record

Wave-15 lane-1. Review Level 2. Contract: verify.mjs + build 0W/0E + both test projects (floor 795/33, SP-054's exit counts). Enabler 2: the three hot docs are NOT worker-edited; orchestrator reconciles at land.

## Step 1 — archaeology (READ-ONLY, WPF current `main` content, File.cs:line)

### The active-pool contract (`ConditioningControlPanel/Services/Chaos/DtrhAssetManifest.cs`)

- `EnumerateActive()` (:85-105): the user's active pool as flat tuples `(Full, Rel, Bytes, IsImage)` — "shared so the transfer compression planner and the game can never disagree about what 'the active pool' is". Lazy; never throws (setup failure → empty).
- `BuildDisabledSet()` (:116-122): `HashSet<string>` from `App.Settings?.Current?.DisabledAssetPaths ?? new()`, each `p.Replace('\\', '/')`, `StringComparer.OrdinalIgnoreCase`. Comment (:110-115): "Matches FlashService.GetMediaFiles' normalization exactly (case-insensitive, separator-agnostic) so the same unchecks that hide a flash hide it here too."
- `FlashService.GetMediaFiles` (`Services/Flash/FlashService.cs:2855-2867`): the normalization the above matches — `Norm(p) => p.Replace('\\','/')`, set with `OrdinalIgnoreCase`, lookup on `Norm(Path.GetRelativePath(basePath, f))`, gated `DisabledAssetPaths.Count > 0`.
- `Scan(root, disabled)` (:125-165): images/ then videos/, in walk order (:128-131 comment: "The accepted-count bound spans BOTH folders (it used to be a shared running total across the two Collect calls) — keep it that way, the manifest's downsampling assumes a bounded but unbiased-by-folder input"). Per-file order:
  1. Extension filter FIRST: non-matching ext → media-like (`.wmv .avi .mkv .mov .flv .mpg .mpeg .bmp .tiff .heic`) yields `ScanItem(Skipped:true)` (COUNTED); other junk silently ignored.
  2. `rel = Path.GetRelativePath(root, f).Replace('\\','/')` in a try (catch → filename fallback).
  3. Deselect check SECOND: `disabled.Count > 0 && disabled.Contains(rel)` → `continue` — "Deselected in the Assets tree -> skip (silently, not 'skipped'): it's user intent, not an unsupported file" (:148-151).
  4. Size caps THIRD: `len <= 0 || len > cap` → `ScanItem(Skipped:true)` (counted). Image cap 50MB, video cap 500MB (:21-22).
  5. `accepted++` only on accepted entries; bound `accepted >= MaxEntries * 2` (10000) → `yield break` — accepted-only, combined across both folders.
- `ScanItem` (:41-45): "Skipped == true means 'media-looking but not usable' (unsupported container, zero-length, over the size cap) — the manifest counts those so the game can be honest; silently-ignored junk and user-deselected files are never yielded." — the two skip meanings stay distinct.
- Extension lists: images `.jpg .jpeg .png .webp .gif` (:19), videos `.mp4 .webm .m4v` (:20); `MaxEntries = 5000` (:23); walk depth 8, dot-dirs skipped (:24, Walk :167-181); downsample ratio-preserving partial Fisher-Yates (:183-194).

### The intake guard (`ConditioningControlPanel/Services/Quiz/IntakeHostService.cs`)

- `BuildDisabledAssetSet(IEnumerable<string>?)` (:776-781): same normalization — `(p ?? "").Replace('\\','/')`, `OrdinalIgnoreCase` — "so the same uncheck that hides an image from the flashes hides it from the intake page too".
- `IsAssetActive(disabled, root, fullPath)` (:783-790): **`disabled.Count == 0` → `true`** ("Empty set = nothing deselected = everything active (identical to the pre-fix behavior)"); `Path.GetRelativePath` throw → **`return true`** ("unrelatable path: never silently drop content over a path quirk"); else `!disabled.Contains(rel)`.
- `BuildMediaManifest()` (:792+): the verbatim #762/#798/#619 comment — "this walk used to list the images folder RAW, so anything the user unchecked in the Assets tree still rode the manifest and showed up in the intake page's effect layer / captcha grid. It now applies the same DisabledAssetPaths filter the flash pool uses; users with nothing deselected see exactly what they saw before."

### The persisted settings (`ConditioningControlPanel/CCP.Core/Models/AppSettings.cs`)

- `DisabledAssetPaths` (:1613-1635): `HashSet<string>`, "Set of relative paths to DISABLED assets. Items NOT in this set are active... relative to EffectiveAssetsPath, stored with forward-slash separators and matched case-insensitively"; setter re-normalizes (Replace `\`→`/`, drops null/empty).
- `UseAssetWhitelist` (:1637-1646, default `false`): "When true, files in DisabledAssetPaths are excluded from use. When false, all files are active (default behavior)." **Grep-verified: no WPF reader outside AppSettings consults the flag** — the current consumers gate on set-empty only. The port implements the flag's documented contract (PROMPT framing c is binding); with the shipped defaults (flag false, set empty) behavior is identical to WPF today, and the future Assets-tree row owns the write path that pairs them.

## Step 1 — consumer inventory (grep-verified, not guessed)

Every client touch-point of the user-media pool (`<dataDir>/assets`):

| Consumer | Site | Pool access |
|---|---|---|
| DTRH manifest at ready | `Features/Dtrh/DtrhHostWindow.axaml.cs:1195` | `DtrhUserMedia.Build(...)` → page manifest message |
| DTRH probe media | `Features/Dtrh/DtrhHostWindow.axaml.cs:713` (SendProbeMedia) | `DtrhUserMedia.Build(...)` → probe-img messages |
| Intake init media | `Features/Intake/IntakeHostWindow.axaml.cs:586-587` → `Features/Intake/IntakeMediaManifest.cs:21` | routes through `DtrhUserMedia.Build(...)` (SP-054 landed it that way) |
| `/umedia/*` serving | `Features/Dtrh/LoopbackServer.cs:320,369-389` | SERVE-only route, not an enumeration; the page learns URLs only from the manifest (WPF's ccp.assets host likewise serves the raw folder — the filter is at enumeration, never at serving) |

Non-consumers (grep false positives, verified): `AvatarAnimationEngine.cs`/`AvatarTubeParticipant.cs` ("images" = avatar-pack platform bitmaps), `IntakeDraft.cs` ("videos-on" = a knob comment). Root definitions: `DtrhParticipant.cs:92` + `IntakeParticipant.cs:61` — both `Path.Combine(DataDirectory, "assets")`, one shared `<dataDir>` per install (IntakeParticipant.cs:54-56 comment).

**Conclusion: exactly ONE enumeration exists (`DtrhUserMedia.Build`), both consuming surfaces route through it. The seam is that method plus the persisted set feeding it.**

## Step 1 — design (pre-consult)

**Seam home (justified):** the active-pool definition lands in `Features/Dtrh/DtrhUserMedia.cs` — both consumers already route through that file; upstream's split definition (DtrhAssetManifest + IntakeHostService duplicates) is the defect class this row kills; a new `Media/` folder for one concept is ceremony. The persisted document lands in `Persistence/AssetSelectionDocument.cs` (app-global user selection spanning features — WPF AppSettings parity; not DTRH-scoped, not intake-scoped).

**The one definition (public static, `DtrhUserMedia`):**
- `BuildDisabledSet(IEnumerable<string>?)` — normalization verbatim from `IntakeHostService.cs:776-781` / `DtrhAssetManifest.cs:116-122` (`(p ?? "").Replace('\\','/')`, `OrdinalIgnoreCase`).
- `IsAssetActive(HashSet<string> disabled, string root, string fullPath, bool useWhitelist)` — gate order: `!useWhitelist → true` (AppSettings.cs:1637 documented contract, framing c) → `disabled.Count == 0 → true` (:784) → `GetRelativePath` throw → `true` (:789, framing b) → `!disabled.Contains(rel)`.
- `Build(userMediaRoot, mediaOrigin, log, HashSet<string>? disabled = null, bool useWhitelist = false)` — defaults preserve today's all-active behavior for any caller without the store. `Collect` gains the deselect check BETWEEN the extension filter and the size check (upstream Scan order :134-158): a deselected valid-ext file is `continue`d silently (never `Skipped++`); a deselected media-like-unsupported file is still counted skipped (ext check first — pinned by test); the combined accepted bound (`m.Images.Count + m.Videos.Count >= MaxEntries * 2`) spanning both folders is untouched (framing e).
- The `:13` divergence comment is rewritten: deselection honored via the seam; set persisted, empty until the Assets-tree row.

**Persistence (SP-005 machinery, additive, own named owner):**
- `Persistence/AssetSelectionDocument.cs`: schema v1 { `List<string> DisabledAssetPaths` (default empty), `bool UseAssetWhitelist` (default false — :1640 parity), `[JsonExtensionData]` (contract §6) }. NEW file `asset_selection.json` in the shared `<dataDir>` — a new document, so no schema bump and no absent-member case exists (Missing outcome = fresh defaults; the absent-member-flag discipline applies to additive members on EXISTING documents).
- `AssetSelectionStore.Start(...)` static helper beside the document: owner `"AssetSelection"`, starts the store, logs typed Degraded, returns it. Called by (a) `DtrhHostWindow` at window init (the `_barkStore` precedent :203-213) into `_disabledAssets`/`_useAssetWhitelist` fields feeding both Build call sites; (b) `IntakeHostContext.Start` (the settingsStore/punchStore precedent) exposed on the context, feeding `IntakeMediaManifest.Build` (signature gains `disabled`/`useWhitelist` pass-throughs — never a second scan).
- Load-at-host-open is sufficient: nothing writes the set until the Assets-tree row; that row owns any live-reload concern (recorded).

**Fixture matrix (tests, `client/tests/CcpClient.Tests/AssetActivePoolTests.cs`):** temp root `images/{a.jpg, sub/b.png, CASE.JPG, big.gif(>50MB sparse), photo.wmv}` + `videos/{v.mp4, v2.mkv}`.
1. Empty set + whitelist on → all active.
2. Exact rel `images/a.jpg` → excluded, `Skipped` unchanged.
3. Case difference `IMAGES/A.JPG` → excluded (OrdinalIgnoreCase).
4. Separator `images\sub\b.png` stored → excluded (separator-agnostic).
5. Nested `images/sub/b.png` → excluded.
6. Unrelatable path (empty root → GetRelativePath throws) → `IsAssetActive` true (never a silent drop).
7. Whitelist OFF + non-empty set → ALL active (the flag gates the mechanism).
8. Both consumers agree on one fixture: DTRH manifest and intake sample built with the same set — the deselected asset appears in NEITHER; intake's "pool of N" log count matches the DTRH manifest count.
9. Skip-vs-deselect distinct: deselected oversized `big.gif` → silently dropped, `Skipped` unchanged (deselect check before size); deselected `photo.wmv` → counted `Skipped` (ext check first); non-deselected oversized → `Skipped++`.
10. Both-folders bound: with a small injected walk bound (see below), images alone saturating the bound → zero videos walked — the bound is combined, not per-folder.
11. `AssetSelectionDocument` round-trip on SP-005 machinery (schema v1, defaults, ExtensionData preserved, typed Missing on absent file).

**Walk-bound test seam:** `Build` gains an optional `int? walkBound = null` (null → `MaxEntries * 2`). Cheaper and more honest than staging 10,001 files; the bound's SEMANTIC (combined across folders) is what the test pins. (Alternative rejected: 10k-file fixture — minutes of NTFS churn to prove an arithmetic property.)

**Headed proof plan (Step 3, Windows):** stage `sp055_*`-named media into the real greenfield profile `<dataDir>/assets/` (the b4 WH "staged COPIES" precedent; scoped names, removed after — recorded; no data-dir override flag exists) + write `asset_selection.json` {disabled:[the staged image], useAssetWhitelist:true}. Run A: `--dtrh-demo --dtrh-quick --dtrh-auto-close` → transcript `dtrh-media: manifest — 1 image(s), 1 video(s)` (deselected absent) + dimension-validated capture (windowId quirk rule). Run B (control): set empty → `2 image(s), 1 video(s)`. Run C: `--intake-demo --intake-auto-close` with the deselection store → `intake: media manifest sampled (... from a pool of 1)`; Run D (control): pool of 2. File-content proof: the seeded `asset_selection.json` + staged pool listings committed under `evidence/`.

## Step 1 — pre-approach consult

**Mode:** solo (T-7: council banned by PROMPT Do-NOT). **Requested route:** Opus 5 main (2026-08-04 rewire), Fable 5 fallback. **Actual answering model:** NOT surfaced by the consult tool response (no model identity header — recorded honestly, the SP-022…027/049…054 provenance discipline). **Verdict: design APPROVED with corrections — all adopted:**

1. **Whitelist-flag trap mitigation (adopted):** the `UseAssetWhitelist`-off + non-empty-set state silently defeats deselection, and upstream RUNTIME honors a non-empty set regardless of the flag (grep-verified no WPF reader consults it) — so the gate is a documented-contract port (PROMPT framing c is binding authority), NOT an observed-parity claim. Mitigation: `Build` logs a counts-only diagnostic when the set is non-empty AND the flag is false ("deselection set present but the whitelist gate is off — all assets active"), converting the silent trap into a self-evidencing state. Recorded here as a deliberate documented-contract decision; the future Assets-tree row owns the write path that pairs flag + set.
2. **Store lifecycle (adopted):** mirror the existing per-host store conventions exactly — DtrhHostWindow starts the store at window init beside `_barkStore` (:203-213) and stops it best-effort at teardown (:239); IntakeHostContext starts it beside settingsStore/punchStore and disposes it with them (:116-118). The store is read-only this row (never Mutate → never dirty → FlushAsync no-ops). Recorded: **two readers, zero writers** over one file; the Assets-tree row must resolve single-writer ownership when it adds the write path. **Degraded load → empty set + flag false → ALL ACTIVE** (never silently drop content over a store fault — the framing-b principle applied to the store).
3. **walkBound test seam (adopted):** accepted over a 10k-file fixture (no InternalsVisibleTo exists → public optional `int? walkBound = null`). Test #10 shape pinned by the consult: N images + 1 video with `walkBound: N` → `images == N`, `videos == 0` (the bound is combined AND aborts the second folder's Collect — the port's per-Collect `return` preserves combined-ness because the second Collect's first check sees the saturated combined count; the test must exercise exactly that). No downsample interference at small N (downsample triggers at total > 5000).
4. **Real-profile staging hardening (adopted):** the profile may be non-empty → count-based transcript proof must be DELTA-based: a pre-stage baseline run's counts are captured first; control = baseline+2 images/+1 video, deselect = baseline+1. Staging uses a dedicated nested subfolder `<dataDir>/assets/images/sp055/` + `<dataDir>/assets/videos/sp055/` (doubles as the nested-path deselection cell; removal is one subtree + `asset_selection.json`, zero chance of deleting pre-existing user content). Before/after inventory snapshots committed.
5. **Intake small-pool honesty (adopted):** with pools of 1-2 the intake sample is min(pool, 18) → the discriminating evidence is the `intake: media manifest sampled (… from a pool of N)` line's pool count, not the sample size.
6. **ToMediaUrl recompute-throw asymmetry (recorded, parity kept):** `IsAssetActive` tolerates an unrelatable path (true), but `ToMediaUrl`/`ToAssetUrl` recompute `GetRelativePath` WITHOUT a catch in BOTH the port and upstream — a path that passed the tolerance could still fault the whole Build (caught by Build's outer try → partial manifest). Upstream-latent, defensive-only in practice (GetRelativePath throws only on null/empty args; walked files under a valid root never hit it). Kept verbatim; the tolerance branch is unit-tested via `IsAssetActive` with an empty root and is recorded as NOT walk-reachable end-to-end (never implied otherwise).

**Adopted design changes vs the pre-consult table:** the whitelist-off diagnostic line (correction 1); Degraded-load → all-active encoding (correction 2); nested `sp055/` staging subtree + baseline-delta proof (correction 4); test #10's exact both-folders assertion (correction 3); the ToMediaUrl asymmetry recorded (correction 6).

## Step 2 — implementation

(pending)

## Step 3 — headed evidence + engine-review presence (T-2)

(pending)

## Step 3 — pre-completion consult

(pending)

## Step 4 — testing & verification

(pending)
