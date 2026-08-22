# Upstream sync ledger (WPF `main` → `feat/crossplatform`)

The WPF product keeps shipping while the greenfield client is being built. This file is the
durable record of every sync: what upstream changed, what that costs the port, and what was
filed so nothing is silently left behind.

**Ground rules**

1. The `ConditioningControlPanel/**` tree on this branch is a **read-only archaeology reference**
   and must track `main` exactly. Conflicts inside it resolve to `main` (`--theirs`).
2. `client/**` is the port and is never touched by a sync merge (verify it, every time).
3. Release notes (`notes-v*.txt`) are the feature-level summary; **rows cite code, not notes**.
4. Every sync ends with: client build + tests green, an entry in this ledger, and board rows for
   every port obligation the delta created.
5. The procedure lives in the project skill **`wpf-upstream-sync`** (`.pi/skills/wpf-upstream-sync/`).

---

## Sync 2026-08-14 — v6.7.4 → v6.8.0 (merge `db3e842f`)

| | |
|---|---|
| Previous baseline | `0c9947a6` (main tip 2026-08-11, in-tree `<Version>6.7.4</Version>`) |
| New baseline | `79feea84` (main tip 2026-08-14, in-tree `<Version>6.8.0</Version>`) |
| Upstream commits | 125 |
| Merge | `db3e842f` — 384 files, +54 757 / −11 206 |
| Conflicts | **none** (0 unresolved, 0 markers) |
| Port health after merge | `client/CcpClient.sln` 0W/0E, **1010/1010 unit + 35/35 headless**, skip set exactly the 2 pinned Linux-gated names — no drift breakage |
| Port files touched by the merge | **none** (verified: `git diff HEAD~1 HEAD -- client/ spine-tasks/ .spine/` empty) |
| Release notes | **none in this delta** — v6.7.5…v6.8.0 shipped without notes files; the version claim cites `ConditioningControlPanel.csproj`, per the standing rule |
| Batch in flight | **yes** — wave 29 (`20260814T090009`, SP-072) was executing. Checkout-free path used (`git fetch origin main:main`, never `git checkout main`). See §E |

**Verified BEFORE landing, not after.** The merge was performed and fully verified in scratch worktree
`/c/Code/ccp-sync-v680`, then landed on the branch with `git diff <verified> HEAD` **EMPTY** — so the tree
that moved the base is byte-identical to the tree that was built and tested.

### A. New product surfaces (each got its own board row)

| Upstream surface | Evidence | Port obligation |
|---|---|---|
| **THE DESCENT** | `Services/Descent/` (**8 new files**: `DescentService`, `DescentReader`, `DescentModels`, `DescentMigration`, `DescentMigrationService`, `DescentCeremonyCopy`, `VatFaucetHold`, `VatFillCoordinator`) + `Resources/programs/`, `Resources/achievements/` additions; 6 new upstream test files (`DescentPhase1DormancyTests`, `VatFillCoordinatorTests`, `VatFaucetHoldTests`, …) | **Owner-decision shaped, not merely unported.** Its own doc comment: *"the desktop app's reader for the server's `descent` block… rides inside the `user` node of `GET /v2/user/profile`… X-Auth-Token"* — i.e. a NETWORK + AUTH surface. The constitution forbids broadening network/secret boundaries without owner approval, so this cannot be scheduled as ordinary parity work. Row filed as **BLOCKED on an owner decision**, with the ceremony/vat UI half named separately |
| **JUST DROP** | `Services/JustDrop/` (**4 new files**: `JustDropService`, `JustDropHostService`, `JustDropOrdersService`, `CreditedOrders`) | Its own doc comment says *"the web session shop, **hosted rather than ported**"* — upstream itself treats it as a hosted web surface, so the port obligation is a web-core host plus order crediting, not a native rebuild. Same network/auth question as Descent. Row filed |
| **For You Feed grew** | `Services/Fyp/` +2 files (`FypAssetManifest`, `FypGhostOverlay`) and a new `Online/` subdirectory | **The existing FYP row already exists and stays `not-ported`** (payload tree `web/fyp/` unchanged at 8 files). Recorded on that row rather than as a new one — the surface did not appear, it deepened |

### B. Behavior changes to code the port has ALREADY ported (parity drift — defect-class)

> **CORRECTED 2026-08-14 (completeness audit — see §F). The first pass of this section said "12 files hit"
> and that number was WRONG**: it came from scanning `client/src/**` code comments only. The port's
> **authority documents** under `client/docs/**` cite WPF by `File.cs:line` too, and a landed contract's
> citation matters more than a code comment's. True figures: **297 WPF files are cited by the port, 106 of
> them changed at this sync, 19 of those are tier-1** (cited by port code or a landed contract). The full
> set now lives in `client/docs/upstream-citation-inventory.json` — data, keyed by real path, regenerated
> and diffed at every sync — precisely so a wrong count in prose cannot hide the remainder again.

Measured mechanically, not by reading notes. The dangerous ones are fixes to code the port already copied —
the port now carries a bug upstream retired, and no test on either side says so. The table below records the
first pass; §F carries what the audit added.

| Change | Evidence | Impact on landed port code |
|---|---|---|
| **Companion reply hygiene missed the TRANSCRIPT shape** — the sharpest finding of this sync | `Services/AIService/AiTextHygiene.cs` (**+61/−1**): new `EmbeddedSpokenSigil` regex + `SalvageSigilTranscript`, and `UnwrapSpokenSigil` gained an `activeSpeaker` parameter. Upstream comment dates it *"0813, first seen after a mod switch"* | **Direct hit on SP-069 (wave 26), on a LIVE user-visible path.** The port ported the start-anchored wrapper strip only, so a multi-speaker reply (several `«X said aloud: "…"»` blocks) has its first opener stripped and **the rest reaches the chat bubble verbatim** — exactly the mangled shape upstream fixed. Worse, blocks attributed to a *different* companion (stale bark echoes from a previous mod) are shown as if the current one said them. **Defect-class row, P1** |
| **`AppSettings` +441/−5** | `Models/AppSettings.cs` | The port's settings/companion state cite it in three files. Most of the +441 is new-feature surface (Descent/JustDrop/Programs), but any *changed* existing key is silent divergence in landed persistence. Itemized in the backlog row |
| **`VideoService` +364/−11** | `Services/Video/VideoService.cs` | Cited by `DtrhHostWindow` and `DtrhNativeEffects`. The unified-video row is still BLOCKED, so this is not a landed-code defect — it is a **moved baseline for a row not yet started**. Recorded, not filed as a defect |
| **`DtrhAssetManifest` +270/−2** | `Services/Chaos/DtrhAssetManifest.cs` | Cited by `DtrhUserMedia` (landed, SP-055 active-pool work). Second consecutive sync in which this file moved — the previous sync's `EnumerateActive()` row came from here. Needs a re-read against the landed active-pool code |
| **`MainWindow.Presets` +213/−63** | `MainWindow/MainWindow.Presets.cs` | Cited by `QuickToggleDispatch` (**landed, and the row's WPF ground truth**), `FeaturePopupManager`, `FeaturePopupWindow`. A 63-line deletion in the port's reference-identity dispatch evidence is exactly the kind of change that silently invalidates a landed parity claim |
| **`IntakeHostService` +106/−1** | `Services/Quiz/IntakeHostService.cs` | Cited by three landed Intake files (`IntakeDraft`, `IntakeDraftSink`, `DtrhUserMedia`). Third consecutive sync touching the Intake host contract |
| **`ChaosWebViewHost` +43/−6** | `Chaos/ChaosWebViewHost.cs` | The class both port web-core hosts are modeled on. Moved in the previous sync too (+19 then) |
| **`CompanionBrain` +27/−2**, **`AiService` +8/−2**, **`KeywordTriggerService` +11/−1** | as cited | All three are cited by landed AI pipeline / awareness code. Small, but on the same live path as the transcript defect |
| **`MainWindow.Settings` +16/−0**, **`MainWindow.Lab` +0/−2** | as cited | Smallest of the twelve; itemized only |

### C. Smaller deltas (tracked in the backlog row, itemized here)

- **384 files, 91 added / 292 modified / 1 renamed**, spread over `Services` (67), `Views` (53),
  `MainWindow` (45), `Resources` (37), `Windows` (34), `Dialogs` (33), `Features` (31),
  `Localization` (11), `Controls` (10), `Models` (9).
- **37 new upstream test files** under `Tests/ConditioningControlPanel.Tests/` — they are the cheapest
  available map of what upstream considers behavior worth pinning this release (`VatFillCoordinatorTests`,
  `ProgramHeatTests`, `SpiralGlyphProgressTests`, `NavRailFlyoutTests`, `ModAware*Tests` ×5,
  `TierBadgeRenderTests`, `TeaseCardRenderTests`, `WebNudgeTests`, `VocabTokensTests`, …).
- New non-web resource trees: `Resources/programs/` (4), `Resources/achievements/` (4),
  `Resources/features/` (3), `Resources/sounds/` (2). **None are under `Resources/web/`**, so the payload
  inventory guard is correctly silent about them — they are native WPF assets, not served payloads.
- Installer/modding touched: `installer.iss`, `build-installer.bat`, `MODDING.md`.
- `Localization/` ×11 files — the port still has **no localization system at all** (A-014 honest absence),
  so every localization delta is a standing non-obligation, recorded so it is never re-derived as work.

### D. Gaps this sync exposed in the port's own guards

- **The payload-inventory guard (SP-056) worked exactly as designed and proved a real negative.** All 7
  `Resources/web/` trees are present and **byte-count identical** to the previous baseline
  (dtrh 1542, fyp 8, goon 184, intake 2138, player 3, tunnel 9, vendor 9). The green is honest, not blind.
- **But the guard covers only `Resources/web/`.** Four new resource trees appeared outside it
  (`programs`, `achievements`, `features`, `sounds`) with zero test signal. That is correct for *payload*
  purposes and a blind spot for *asset* purposes; folded into the backlog row rather than widened blindly.
- **Nothing in this repository detects that upstream changed a file the port cites as its evidence.** The
  intersection in §B was computed by hand this sync. Every future sync will need it, and a stale
  `File.cs:line` citation is how a landed parity claim rots silently. **Filed as tooling row T-19**, and
  now half-implemented: `client/docs/upstream-citation-inventory.json` is the data the check needs.

### F. Completeness audit — what the first pass of this sync MISSED

The owner asked for assurance that nothing was lost. Re-running the intersection properly found that the
first pass had lost a great deal, and the miss is recorded here rather than quietly corrected.

**The defect in my own method:** I scanned `client/src/**` only. The port's parity claims live at least as
much in `client/docs/**` — `startup-shutdown-contract.md`, `persistence-migration-contract.md`,
`quick-toggle-dispatch.md`, `capability-inventory.md` and the rest cite WPF by `File.cs:line`, and those
citations back **landed, in some cases RATIFIED** claims. A second, smaller defect: my "is it already
covered?" check tested `task-board.md.includes(filename)`, which matches rows from **earlier** syncs — so
it reported coverage that did not exist. Both are now impossible to repeat the same way: the inventory is
keyed by **real path** (basenames collide — `MainWindow.xaml` is the live example) and carries an explicit
`tier` and `verdict` per entry.

| | first pass | after the audit |
|---|---|---|
| Cited WPF files considered | 84 (src comments only) | **297** (src + docs) |
| Cited **and changed** at this sync | 12 | **106** |
| Tier-1 (port code or a landed contract) changed | not distinguished | **19** |
| With a recorded verdict | 12 | **10 verdicts + 9 owed to a named row** (tier 1); tiers 2 and 3 carry standing dispositions |

**What the audit surfaced that the first pass did not:**

1. **A persistence-contract defect candidate.** `Services/Settings/SettingsService.cs` (+124/−48) adds
   `MergeBuiltInPresetInto` — upstream now **merges bundled built-in presets into the user's stored
   document on load**, a concept the port's landed persistence contract does not have at all. `App.xaml.cs`
   adds `EnsureInstallDateRecorded`/`ResolveInstallDateUtc`, a new persisted field written at startup.
   **P1 row filed.**
2. **A RATIFIED row's ground truth moved.** `MainWindow.Presets.cs` **removed and re-added**
   `CardJustDrop_Click` with a different body; `FeatureCard.xaml.cs` (+142) gained a tease-tier
   `DependencyProperty` and `ApplyTeaseState`; `MainWindow.UiUpdates.cs` (+166) gained a mod-aware surface
   sweep. The quick-toggle row is **DONE + RATIFIED** and cites exactly these files. **P1 escalation row
   filed** — an unattended run does not silently re-open a ratified row, it files the notice.
3. **A cross-cutting theme split across buckets and lost: "remote media".** `VideoService.cs` gained
   `IsRemoteMediaPath`/`RemoteMediaEnabled`/`RemoteChannels`/`TakeRemoteVideo`; `App.xaml.cs` gained
   `OfferRemoteMediaSource`/`FlushPendingRemoteMediaOffer`. The first pass filed THE DESCENT and JUST DROP
   as two unrelated rows and dismissed `VideoService` as "a moved baseline". They are **one owner-level
   network/credential boundary across four surfaces**, and the Decisions-needed entry was widened to say so
   — because a partial approval (say, JUST DROP alone) would leave a remote video path unsanctioned.
4. **Two repeat offenders.** `DtrhAssetManifest.cs` moved at **two consecutive syncs**;
   `IntakeHostService.cs` at **three**. A file that moves every sync is a continuously drifting copy, not a
   one-off delta — called out as the priority inside the tier-1 review row.
5. **`window-behavior-manifest.md` is stale** — 74 of its cited files moved. Filed as its own
   re-verification row rather than dismissed as "inventory churn", which is what the first pass assumed.

**No dangling citations:** zero cited files were deleted or renamed upstream, so every `File.cs:line` in the
port still resolves to a real file. That was checked, not assumed.

### E. In-flight batch — what this sync does and does not cost wave 29

- The lane worktree was branched from `02ae3017` at batch start, so the running worker is **insulated**;
  no in-flight packet was retargeted (standing rule).
- **SP-072 has no WPF archaeology dependency** — verified, not assumed: its only two mentions of
  `ConditioningControlPanel` are *exclusions* (File Scope "NOT in scope" and `fileScopeMustNotChange`).
  So the merge does not move its baseline and **no follow-up row is owed** for it.
- **No file collision is possible at integrate:** the merge touches only `ConditioningControlPanel/**`,
  `Tests/**` and installer files; the lane touches only `client/**` and its own packet directory. The one
  file that could have collided is `client/tests/floor/floor.json`, and this sync does not touch it — no
  test moved (1010 before, 1010 after), because the payload guard is data-driven (11 fixed test methods
  reading `upstream-payload-inventory.json`) and needed no new entries.
- **The real cost, paid deliberately:** wave 29's integrate is no longer a fast-forward. The land must use
  the SCOPED tree-identity form (`git diff <verified> HEAD -- client/ scripts/ ConditioningControlPanel/ docs/`)
  and name the non-code deltas, per the wave-25 precedent. `.spine/handoff.md` was rewritten at this sync
  so the fresh land session does not inherit a false fast-forward premise.

---

## Sync 2026-08-11 — v6.6.3 → v6.7.4 (merge `42286638`)

| | |
|---|---|
| Previous baseline | `b35facb6` (main tip 2026-08-04, in-tree `<Version>6.6.3</Version>`) |
| New baseline | `0c9947a6` (main tip 2026-08-11, in-tree `<Version>6.7.4</Version>`) |
| Upstream commits | 403 (147 fix / 97 feat) |
| Merge | `42286638` — 938 files, +221 198 / −13 173 |
| Conflicts | 5, all in the WPF tree: `.gitignore` (kept both), `CCP.Core/Models/AppSettings.cs`, `MainWindow/MainWindow.UiUpdates.cs`, `Services/TutorialService.cs` (took main), `Models/HapticSettings.cs` (delete/modify — restored main's file; the `CCP.Core` copy is abandoned first-attempt residue) |
| Port health after merge | `client/CcpClient.sln` 0W/0E, **683/683** unit green — no drift breakage |
| Port files touched by the merge | **none** (verified) |
| Release notes | `notes-v6.7.1.txt`, `notes-v6.7.2.txt` (v6.7.3/v6.7.4 shipped without notes files) |

### A. New product surfaces (each got its own board row)

| Upstream surface | Evidence | Port obligation |
|---|---|---|
| **Goon Game — 1v1 duels** | `Services/GoonGame/` (25 new files), `Resources/web/goon/` (**184 new payload files**) | Entire new web-core host: P2P media (64 MB video cap, supporter-gated send, ephemeral partner media), 10 s voice notes with consent + push-to-talk, share-link join, Discord rich presence, solo practice |
| **For You Feed on desktop** | `Services/Fyp/` (3 new), `Resources/web/fyp/` (5 M + 1 A) | Mosaic reel host + **ghost mode (see-through AND click-through)** — lands on the port's overlay/click-through work, webcam gaze scrolling, opacity, any monitor, survives Show Desktop |
| **Her Room (companion redesign) + Awareness** | `Services/Companion/` (14 new), `Services/Awareness/` (23 new), `Views/Controls/Companion/`, `Resources/sounds/companion_audio/awareness_apps.json` | **Reconcile against the port's own AI companion (SP-034/035/038/040/042/044/046) and awareness slice** — upstream shipped its own brain, memory, in-character behavior, opt-in awareness with a plain-words privacy dial (nothing / app names / + page titles), app picker, incognito detection, one-hour pause, and a Mute Voice Lines switch |
| **Trainer Card profile redesign** | `Views/Controls/` (113 new), `Resources/banners/` (12), `Resources/cosmetics/` (94), `Resources/achievements/` (6), `Services/Profile/` (3) | Card rebuild, 12 scene banners, 60 wardrobe adornments + editor + click-to-pin, achievement-gated unlocks, 12 blank-subject avatars, leaderboard privacy dialog |
| **Haptics v2 overhaul** | `Models/HapticSettings.cs` (+65: `SetLegacyProviderMirror`, per-provider v2 flags as a SET, schema-3 migration), rebuilt Lovense + Buttplug providers | Multi-toy concurrent providers, FunScript playback, temperament dial, Deeper editor parity |

### B. Behavior changes to code the port has ALREADY ported (parity drift — defect-class)

| Change | Evidence | Impact on landed port code |
|---|---|---|
| **Asset deselection honored everywhere** (#762 #798 #619) | `Services/Chaos/DtrhAssetManifest.cs` (+126/−46: new `EnumerateActive()` as the single "active pool" definition, `ScanItem` skip semantics, `BuildDisabledSet()`), `Services/Quiz/IntakeHostService.cs` (+83: `IsAssetActive(disabled, root, fullPath)`, `TopMarksPercent = 90.0`) | The port's DTRH asset manifest and intake asset provisioning **predate this fix** — user deselection in the Assets tree is not honored, and chaos overlays / Graded Intake were the exact surfaces upstream had to fix. **P0 row filed.** |
| **Graded Intake payload gained accents + AI rework** | `Resources/web/intake/core/accents.js` (**new, +350**), `Resources/web/intake/core/ai.js` (+79/−22) | SP-054 (in flight at merge time) was built against the v6.6.3 intake payload and the pre-merge `IntakeHostService`. Baseline is internally consistent — **delta row filed**, packet not retargeted mid-wave. **SERVING half landed at the SP-054 land (2026-08-11):** the payload glob copies the post-sync tree and the manifest now carries `payload/intake/core/accents.js` (count tripwire 3681 → 3682, bumped WITH the reason). **Provisioning half still open** under the delta row: nothing yet reads what `accents.js` needs, and `ai.js`'s rework is unported. |
| **`ChaosWebViewHost` +19** | `Chaos/ChaosWebViewHost.cs` | The class the port's web-core hosts are modeled on; re-read before the next host slice. |
| **Chaos overlays** | `Chaos/ChaosFlashOverlay.cs` (+17 — avoid-the-center exclusion box), `Chaos/ChaosGifCascadeOverlay.cs` (+72) | Flash exclusion box is a new user-facing knob on a ported surface. |
| **Video engine now default (out-of-process)** | browser-video engine promoted to default; grace pause on first panic press for mandatory video | The port landed the browser-video handoff spike; the *default* + grace-pause semantics are new. |

### C. Smaller deltas (tracked in the backlog row, itemized here)

- Spiral opacity cap raised 50% → **100%**
- Velvet Vault / **Exclusives tab** replaces the launcher popup (spotlight shelf, backdrop, coming-next peek)
- **Modular installer**: ~1.1 GB moved into downloadable content packs; wedged pack download self-heals on next launch (ties into the port's open content-pack decision)
- Lockdown no longer swallows bare Esc system-wide (#680)
- Audio: playback runaway that killed the app when Windows audio died (#778 #779); ducking no longer silences the app's own new video engine (v6.7.2 hotfix)
- Video: blurred background fits the true aspect ratio (#786)
- Freeze fixes across the board; Brain Drain captures off the UI thread (#777)
- Lock card: paste/undo cheats blocked, AltGr + IME input fixed (#734)
- Quests: voice-command spiral/pink time counts (#719); streaks stop flashing 0
- Programs: spoken mantras credit correctly, session bubble rate, per-task how-to line
- Companion: replies with effects no longer truncated, raw JSON can never leak into the speech bubble
- FYP: undecodable clips show a notice and swap out instead of black tiles
- Localization: **701 keys × 7 languages + 364 zh-Hans** backfilled
- `redist/MicrosoftEdgeWebview2Setup.exe` is now committed (WebView2 load-bearing for FYP, Exclusives, DtRH, Goon Game, video engine)

### D. Gaps this sync exposed in the port's own guards

- **The client asset-manifest parity test only covers trees the client already ships.** A brand-new
  upstream payload tree (`web/goon/`, 184 files) produces **zero** test signal — the suite stayed
  683/683 green while an entire product surface appeared upstream. Coverage of "trees that exist
  upstream but not in the client manifest" is missing; folded into the backlog row.
- **CLOSED 2026-08-12 (SP-056):** the blind spot is now a guard — `client/docs/upstream-payload-inventory.json`
  lists all 7 trees with dispositions and `UpstreamPayloadInventoryTests` fails the suite on an unlisted
  or stale tree (RED demo in the packet). Two consequences: **(1)** every future sync MUST update the
  inventory (the suite stays red until it does — that is the guard working); **(2)** `fileCountAtBaseline`
  is record data, not an assertion, so a *not-ported* tree can grow by hundreds of files with no signal —
  acceptable because its owning row carries that surface, but never mistake the guard for content-drift
  coverage on unported trees.
- **The enumeration found what nobody was looking for:** `tunnel` (9) + `vendor` (9) — upstream's opaque
  WebView2 three.js backdrop below every Topmost window (`Chaos/ChaosTunnelService.cs`) — were never
  enumerated by the DTRH host row's *completed* b1–b5 slice cut. Filed as its own P1 row (after a
  client-side grep confirmed zero handling) plus a ratification qualifier on the DTRH row.
- **The sync's first casualty was a mid-flight packet's GENERATED manifest.** SP-054 enumerated the
  intake tree into 2137 manifest entries while this sync added a 2138th file to that same tree —
  each side's suite was green, the merged state was red (`unmanifested-copied-asset` +
  the pinned copied-entry count). Caught only because the land ran the suite on `base + merge orch`
  in a scratch worktree rather than trusting two green branches. Any in-flight packet that copies,
  globs, or enumerates the WPF reference tree must be re-tested against the merged state before it
  lands; the rule is now in the `wpf-upstream-sync` skill.
- **First-attempt residue keeps manufacturing merge conflicts.** `CCP.Core/` (and the `CCP.Avalonia.*`
  projects) hold stale forks of WPF models, so upstream edits to `Models/*.cs` land as
  delete/modify conflicts forever. Resolution rule is in the skill; a cleanup is not port work.

## 2026-08-15 — v6.8.0 → v6.8.1 (merge `1d1f8997`)

**Baseline pair:** `79feea84` (in-tree `<Version>6.8.0</Version>`) → `87035e9a` (`<Version>6.8.1</Version>`). **54 commits, 121 files, ZERO conflicts.** Checkout-free path used (`git fetch origin main:main`); `main` was never checked out.

**Port untouched, proven not assumed:** `git diff c9e7fb91 HEAD -- client/ spine-tasks/ .spine/` is EMPTY. Port health on the merged tree: build **0W/0E**, floor **1052/1052 unit + 35/35 headless**, the two pinned Linux skips, unchanged by the merge.

**FIRST REAL USE OF THE CITATION-DRIFT DETECTOR (SP-088, landed hours earlier the same night).** This is the sync T-19 was filed for, and the tool that closed it ran on its first live delta: `node client/tools/citations/detect.mjs --since 79feea84 --until 87035e9a`, self-test 15/15 green immediately before. Inventory: 297 entries, 294 real paths regenerated, universe 1031 shipping / 1865 full WPF files.

### Bucket 1 — NEW product surfaces

- **THE DESCENT expanded hard.** 10 new files under `Services/Descent/` (`DescentCountdownService`, `DescentFuseChrome`, `DescentFuseCopy`, `DescentFuseHandoff`, `DescentFuseSequence`, `DescentHeartbeat`, `DescentRoomSfx`, `DescentShowDirector`, `SpiralFirstLightTimeline`, `SpiralRoom`) plus a **Descent Fuse UI layer**: `AvatarTube/AvatarTubeWindow.DescentFuse.cs`, `Controls/DescentFuseRailChip.cs`, `Controls/DescentFuseStageVisual.cs`, `MainWindow/MainWindow.DescentFuse.cs`. Upstream also added `feat(descent): the restore preview prices the server's basis, credit and all`.
- **A window became a tab.** `Windows/SpiralMapWindow.cs` **DELETED**; `Views/Tabs/SpiralTabView.xaml{,.cs}` + `MainWindow/MainWindow.SpiralRoom.cs` + `Controls/SpiralFirstLightVisual.cs` added. A surface the port has not built changed shape before the port reached it.
- **Mod art framing** — `Services/ModArtFraming.cs` + `feat(mod-art): slot geometry + framing contract for mod-supplied art`, with follow-up fixes for wrong shapes, dead bindings and card-plate misdeclaration. This is a CONTRACT for third-party art, i.e. a modding-surface obligation.
- **Bubbles**: `Services/BubbleSizing.cs`, a Size slider for the ambient field, and `bubbleScale` for mods.

### Bucket 2 — parity drift on code the port has ALREADY ported (the dangerous bucket)

**9 tier-1 cited files changed and hold NO verdict.** Those cited by port SOURCE (not merely docs) are the sharp ones:

| Upstream file changed | Port source citing it |
|---|---|
| `Chaos/ChaosWebViewHost.cs` | `Features/Dtrh/DtrhHostWindow.axaml.cs`, `Features/Intake/IntakeHostWindow.axaml.cs`, `Features/Intake/IntakeProtocol.cs` |
| `Services/AiService.cs` | `Ai/AiAwarenessService.cs`, `Ai/AiOperationPipeline.cs`, `Ai/AiOperationVocabulary.cs`, `Ai/AiProviderSeam.cs` |
| `Services/Companion/Brain/CompanionBrain.cs` | `Ai/AiOperationPipeline.cs`, `Ai/AiPrivacyFilters.cs` |
| `Services/KeywordTriggerService.cs` | `Ai/AiAwarenessService.cs` |
| `MainWindow/MainWindow.Lab.cs` | `Features/Dtrh/DtrhLaunchCoordinator.cs` |
| `MainWindow/MainWindow.Settings.cs` | `Features/Intake/IntakeSettingsDocument.cs` |

Docs-only tier-1 changes: `AvatarTube/AvatarTubeWindow.Avatar.cs`, `AvatarTube/AvatarTubeWindow.ChatInput.cs`, `MainWindow/MainWindow.Patreon.cs`.

**`ChaosWebViewHost.cs` read in detail (+284/−7), and the honest verdict is GAP, not defect.** Upstream's `fix(justdrop): the shop window stays a window, and fullscreen has a door` adds an **opt-in** host-owned fullscreen mode: a page's `requestFullscreen()` fills only the WebView client area, a host-drawn toggle strip rides above the page in both modes, and Esc exits from either focus state. **The default is `false` — explicitly the behaviour "the tunnel backdrop and the DtRH game were built on" — so the port's DTRH and tunnel usage matches the unchanged default.** The trap upstream fixed was a REMOTE page (JUST DROP) stripping the shop window's title bar with no affordance to undo it while the taskbar painted over the page's own exit. **This is therefore an obligation of the JUST DROP row, not a silent defect in landed port code.** The OPEN port-side question, filed rather than asserted: whether the port's own WebView hosts can be driven into the same chrome-stripped state by a page, which was NOT determined here.

### Bucket 3 — smaller deltas

Three "triage 0817" fix batches (`the fullscreen trap, the buried nav rail, and three more`; `the corner GIF stops bricking the app, plus four smalls`; `make the #956 breadcrumb reachable`), a JustDrop windowed fix, profile VAT share, a bubbles Size-slider inertness fix, mod-editor Takeover label ordering, 9 localization files, and the v6.8.0 subtitle becoming "Relapse". Itemized here rather than on the board.

### Bucket 4 — gaps this sync exposed in the port's own guards

- **The detector caught three citations added THE SAME NIGHT and absent from the inventory:** `Services/Settings/ProfileSyncService.cs`, `Services/Companion/Brain/CompanionTurn.cs`, `Dialogs/AwarenessPresetDetailDialog.xaml.cs` — all cited by `task-board.md` rows written hours earlier in this session. The tool caught its own author's drift on its first run, which is the strongest evidence it works that this sync could have produced.
- **4 UNRESOLVED entries are the known basename-collision class**, not new: `Models/AiCommandData.cs` and `Models/AppSettings.cs` recorded at shipping paths that no longer exist, whose basenames resolve into the first-attempt `CCP.Core/` tree. SP-088 documented this shape; it needs a re-key or a retirement, not a fix here.
- **Reporting ambiguity worth fixing in the tool, not worked around:** the header prints `297 entries (106 changed in window)`, but only ~88 non-test WPF files changed in this window — the 106 is the inventory's recorded `changedAtSync` from the PREVIOUS sync, not this window's count. The number is stale-by-construction and should either be recomputed for the passed window or labelled as the inventory's own field.

## 2026-08-22 — v6.8.1 -> v6.8.3 MEASURED AND **NOT MERGED**

| | |
|---|---|
| baseline | `87035e9a7` (`<Version>6.8.1</Version>`) |
| upstream tip | `c35cd309e` (`<Version>6.8.3</Version>`) |
| commits | **58** |
| files | **97** |
| payload trees | **unchanged** — 1542 dtrh / 2138 intake / 9 tunnel / 9 vendor |

**THE MERGE WAS NOT PERFORMED. It was tested end to end in a scratch worktree and reverted.**
Two lanes were in flight (SP-142 in code review, SP-143 implementing) and the merge reds two guards,
so pushing it would have put a red base on the remote — the one thing the skill's in-flight section
forbids. The merge itself plus the re-anchoring is the next wave's packet.

### What the scratch merge proved

- **Clean.** Zero conflicts, zero unresolved paths. The `CCP.*` delete/modify class that used to
  dominate these merges is gone with the tree (SP-141).
- **The port was untouched**: `git diff --name-only feat/crossplatform HEAD` listed nothing under
  `client/`, `spine-tasks/` or `.spine/`.
- **`ConditioningControlPanel/` stayed byte-identical to `main`** by subtree object identity. SP-141's
  invariant survives a 58-commit sync, which is the first evidence that it holds under motion.

### The cost: EXACTLY TWO GUARDS, TEN CITATIONS — all already derived so the packet need not re-derive

`HapticSiteCensusTests.TheLadderConstantsAreStillAtTheLinesTheCensusCites` and
`GoonGameCensusTests.EveryPinnedCitation_IsOnTheExactLineItClaims`. Every replacement below was
verified by needle identity against the merged bytes, never by proximity:

| citation | was | is | verified by |
|---|---|---|---|
| goon `cli-goon` | `App.xaml.cs:2436` | **2517** | `Services.GoonGame.GoonHostService.Launch();` |
| goon `cli-goon-test` | `App.xaml.cs:2364` | **2445** | `try { new GoonTestWindow().Show(); }` |
| goon `cli-goon-vectors` | `App.xaml.cs:2447` | **2528** | `var vectorsPath = ...GoonVectorDumper.Run();` |
| goon `assets-hook` | `MainWindow.Assets.cs:1504` | **1493** | `...TransferCompressionService.Instance.OnPresetChanged(preset.Id);` |
| goon `assets-hook-comment` | `MainWindow.Assets.cs:1502` | **1491** | `// The Goon Game transfer cache plans against the ACTIVE pool...` |
| haptic ladder `~2s` | `HapticService.cs:761` | **817** | `Vibe that decays over ~2s` (NOT `:994`, which is a different `~2s`) |
| haptic ladder `i < 8` | `:782` | **838** | `for (int i = 0; i < 8; i++)` |
| haptic ladder `Math.Pow(0.7, i)` | `:784` | **840** | `Math.Max(start * Math.Pow(0.7, i), MinPerceptibleIntensity)` |
| haptic ladder `250` | `:786` | **842** | `HapticPatterns.Render(rule.Mode, intensity, 250, priority: 1, ...)` |
| haptic ladder `i * 450` | `:787` | **843** | `offsetMs: i * 450` |

The haptic block shifts a uniform **+56**. Four of the five confirmed that on their own; the fifth
(`250`) was **read at line 842 rather than inferred from the pattern**, because a consistent shift is
exactly the evidence that makes a proximity guess feel safe.

### Bucket 1 — PARITY DEFECTS IN ALREADY-LANDED PORT CODE. **This is the dangerous bucket**

**18 `fix(...)` commits land under `ConditioningControlPanel/Services` and `Chaos`**, several in
surfaces this port has already copied. Where upstream fixed a bug the port faithfully reproduced, the
port now carries a bug upstream has retired, and **no test on either side will say so**:

- `4835d200b fix(haptics): stop the trim slider snapping to 0 mid-drag; make Test work on strokers (#977)`
- `97a353288 fix(braindrain): the blur was invisible on machines whose driver leaves the capture's alpha byte at 0 (#960, #975)`
- `3082157e2 fix(braindrain): the blur could go silently dead with nothing in the log (#975, #960)`
- `3fcf1190a fix(braindrain): the legacy path ignored the strength dial's alpha half`
- `ad426360f fix(cornergif): the session-scoped overlay never got the #221 freeze fix`
- `c35cd309e fix(auth): heal a diverged auth token instead of retrying the dead one (#240)` — the port
  has this surface (`Entitlement/HostAuthTokenReader.cs`), so this is a live parity question, not archaeology.

`BrainDrain` is an `OwnedSessionEffect` subclass in the port, so three of these land on ported code.

### Bucket 2 — new surfaces

New files, none of them a new payload tree: `Services/Update/NativeBundleGuard.cs`,
`Services/UI/HangContext.cs` + `UiHangWatchdog.cs`, `Services/Compositor/*` (7 files touched),
`Resources/Theme/*`, and six new upstream test files.

### Bucket 3 — in-flight packets whose baseline this moves

**SP-142 cites `Services/Flash/FlashService.cs:367-380`, `:345-351`, `:3910` and
`Services/Notifications/OverlayService.cs:398` as upstream evidence — and BOTH files changed in this
delta.** Per the standing rule the packet is **not retargeted**; its archaeology is internally
consistent against `87035e9a7`. The delta is a follow-up row instead.

### Bucket 4 — what this sync exposed in the port's own guards

Release notes stop at `notes-v6.7.2.txt` while the csproj says **6.8.3** — the documented lag, so the
notes were read as a map and cited for nothing. And the two census guards were again the ONLY
mechanism that noticed a WPF-tree change: they caught this because they re-derive line numbers from
the shipping bytes, which remains an accident of how they were built rather than a rule anyone
enforces.
