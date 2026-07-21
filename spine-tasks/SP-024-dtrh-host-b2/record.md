# SP-024 record — DTRH host slice b2: save slots, picker/quick start, protocol v1

**Task:** spine-tasks/SP-024-dtrh-host-b2 · **Review Level:** 2 · **Binding spec:** `client/docs/dtrh-admission.md` §7 (b2 row)
**Engine-review presence (T-2):** recorded per `spine_review_step` call below.

---

## Step 1 — slots/picker/protocol archaeology + design + pre-approach consult

### WPF archaeology (READ-ONLY, File.cs:line)

**Three-slot model** (`ConditioningControlPanel/Services/Chaos/ChaosMetaStore.cs`):
- Three independent local saves, one file per slot: `chaos_meta.slot1.json`..`slot3.json` in the user-data folder (`:24` SlotCount=3, `:30-31` SlotFilePath); active slot remembered in app settings (`:38` ActiveSlot reads `AppSettings.ChaosActiveSlot`, clamp 1-3 `:40`).
- Load never throws: missing/corrupt → fresh default state, logged (`:52-81`); interrupted atomic write recovered via orphaned-temp adopt / stale-temp delete (`:147-158`); save = temp file + overwrite rename (`:215-229`).
- Legacy `chaos_meta.json` migrates into slot 1 on first load (`:133-144`) — N/A in greenfield (no legacy installs).
- `ReadSummary(slot)` (`:82-113`): cheap picker read WITHOUT migrations or live-state touch; missing file → `Exists=false`; **corrupt file → Exists=true with zeroed stats so it can still be deleted** (`:84-85`); `LastPlayedUtc` = file last-write time (`:88`); `HasRagdoll`/`HasPorcelain` from `CraftedItems` keys `"ragdoll"`/`"porcelain"` (`:105-108`).
- `Delete(slot)` (`:116-131`): deletes slot file + stray `.tmp`; slot 1 also clears the legacy file. Returns whether something was removed.
- Slot-state migrations are schema-stamped + idempotent (`MigrateToV3`, `:161-193`) — the greenfield maps this onto SP-005's journaled document migrations.

**Slot switch semantics** (`ConditioningControlPanel/Services/Chaos/ChaosUpgrades.cs`):
- `SwitchSlot(slot)` (`:230-243`): remember choice in settings (save), then load that slot's file AUTHORITATIVELY (fresh state if empty). Deliberately does NOT bank the outgoing slot first — the picker only opens when the hole is closed, and banking would resurrect a just-deleted slot. Loading is always file-driven so "delete the active save, then descend into it" correctly starts fresh (`:224-229`).
- `DeleteSlot` (`:259-263`) does not touch in-memory state; the next SwitchSlot reloads from disk.
- Rank spine for picker cards: `ChaosRanks.For(RunsCompleted)` thresholds `{0,3,10,25,50,100}` → Curious/Tempted/Slipping/Entranced/Devoted/Claimed (`ChaosRanks.cs:11-31`, `Name` `:45-55`).

**Save picker flow** (`ConditioningControlPanel/Chaos/ChaosSlotPickerWindow.xaml.cs`):
- Modal `Pick(owner)` right before the hole opens (`MainWindow.Lab.cs:123-127`); cancel backs out (returns null → no launch); the pick becomes the live slot via `ChaosMeta.SwitchSlot` then launch (`:125-135`).
- Cards in fixed slot order 1..3 (`:58-80`). Empty slot: "✨ New Journey — Empty slot, start fresh" (`:131-152`). Populated: rank name, `N descents`, `✦ sparks drops`, `🪙 gold`, best score if >0, "Last played MMM d, h:mm tt" local time (`:154-181`).
- **Stitch-lock:** slots 2/3 are locked ("🔒 Stitched Shut", dimmed 0.45, no click, no delete) unless ANY save's summary HasRagdoll (slot 2) / HasPorcelain (slot 3) — any save's craft unlocks globally; a pre-existing save keeps its slot open (`:60-80`, `:84-115`). Locked selected slot falls back to 1 (`:81`).
- Delete per card (existing saves only), confirm dialog "Erase Save N? ... can't be undone", default Cancel; for slots 2/3 the confirm warns when erasing re-locks the slot (no other surviving save holds the doll) (`:239-276`); after delete, cards rebuild (`:275`).
- Footer: the save folder path shown verbatim + "open folder" button (`:42`, `:278-287`). DESCEND commits `_selected` (`:289-294`); close cancels (`:296-300`). Selection visuals: accent border + glow (`:216-227`).

**Quick start** (`MainWindow.Lab.cs:152-183`): skips the save picker BY DESIGN ("that's the 'quick' part"), reuses the last-chosen slot already live, launches immediately (`:161-165`). Fresh profile (RunsCompleted==0) still drops into the scripted first run — progression, b4.

**Protocol v1 usage in the WPF host** (`ConditioningControlPanel/Services/Chaos/DtrhHostService.cs` + `Chaos/ChaosWebViewHost.cs` + `Services/Chaos/DtrhMetaBridge.cs`): full handler switch `OnPageMessage` (`:216-334`), ready-flush ordering (`ChaosWebViewHost.cs:301-305,319-326`), `Protocol = 1` (`DtrhHostService.cs:31`), meta-command sub-op switch (`DtrhMetaBridge.cs:91-383`: purchase-upgrade, toggle-upgrade, purchase-dial, set-lifetime-boon, equip-boon, add-gold, spend-gold, material-add, craft, consume-crafted, pin-boon, set-denial, buy-consumable-slot, bench-buy, first-time, lesson-progress, set-flag, add-to-set, remove-from-set, set-num, map-set, add-channel-seconds, reset-onboarding — all b4).

### Payload protocol archaeology (READ-ONLY; `Resources/web/dtrh/`, tree 40be29df)

`bridge.js` (blob 13af3f4d): `PROTOCOL = 1` (`:11`); handlers Map + preBuffer replay (`:15-37`); `send` requires string `type` (`:40-42`); `log` slices msg to 400 chars (`:44`); `announceReady` posts `{type:'ready', protocol:1}` (`:47`). Unknown types page-side buffer forever (preBuffer) — never crash (`:19-24`).

`boot.js` handler registrations (`:132-181`) + send sites (grep sweep over the whole tree; scene-graph `userData.type` values and Worker `{type:'module'}` options are NOT protocol):

**Page → host (21):**
| type | fields (send site) | b1 state |
|---|---|---|
| ready | protocol (bridge.js:47) | handled b1 |
| log | msg≤400 (bridge.js:44) | handled b1 |
| heartbeat | t (boot.js:178-183, rAF ~2s) | handled b1 (count) |
| exit | — (boot.js:180-190, Esc held 1.2s) | handled b1 (close) |
| exit-done | — (boot.js:126-130, after engine dispose) | b5 bounded wait |
| fullscreen-set | on (game/overlays.js) | handled b1 (WindowState) |
| boot-error | msg (boot.js:82-101) | b2: typed close (see design) |
| pong | t (boot.js:180, answers ping) | b5 watchdog |
| vn-speaking | on (game/cheshireVn.js) | b3 |
| sfx | name, scale (game/*; host DtrhHostService.cs:222-234) | b3 |
| fire-payload | kind, strength?, durationMult? (game/*; host :486-523) | b3 |
| freeze-state | on (game/*; host :235-236) | b3 |
| bark | event + per-event fields (game/*; host RouteBark :605-672) | b3 (voice) |
| meta-command | op + fields (engine/game *; DtrhMetaBridge.cs:91-383) | b4 |
| request-run | setup? (game/*; host :428-446) | b4 |
| run-started | difficulty, mode (game/chaosRun.js; host :238-256) | b4 |
| run-ended | score, durationSec, elapsedSec, difficultyMult, sparkGainMult, difficulty, trickleDrops, dripFeedMaxed, bestCombo, defused, sessionStats (game/*; host :525-602) | b4 |
| asset-stats | per-asset deltas (game/*; host :287-291) | b4 |
| loom-save | name, gifBase64, params, overwrite (game/loomStudio.js; host :294-301) | b4 |
| loom-delete | slug (game/loomStudio.js; host :303-309) | b4 |
| report-bug | — (hub dock; host :312) | unassigned (host UI) |

**Host → page (12):**
| type | fields (host post site; page handler) | b1 state |
|---|---|---|
| init | protocol, settings{masterVolume}, modId, modContent, runSetup{12 fields}, m2Test (DtrhHostService.cs:175-194; boot.js:132-146) | sent b1 |
| manifest | images[{name,url}], videos[{name,url}], skipped, truncated (:196-204; boot.js:147-160) | sent b1 |
| meta | state, rev (DtrhMetaBridge.cs:58-61; boot.js:163-167) | b4 |
| favorites | names[] (:206-211; boot.js:162) | b4 |
| run-config | runConfig{…} (:446; boot.js:168) | b4 |
| payout-result | baseXp, skillMult, finalXp, sparksEarned, previousBest, rankUp, dryRun (:588-597; boot.js:169-173) | b4 |
| payload-state | kind, on (:744,764; boot.js:174-179) | b3 |
| fullscreen | on (:430; boot.js via game) | b2 echo (WPF echoes after toggle; b1 toggles without echo) |
| end-run | reason (:140; boot.js:180) | b5 |
| loom-list | spirals[{slug,url,params}] (:341-355; boot.js:181-186) — `params` is a C# keyword: STJ needs [JsonPropertyName] | b4 |
| loom-result | op, ok, slug, error (:297,306; boot.js:187) | b4 |
| ping | t (page answers pong, boot.js:180; WPF host never sends today — defensive handler) | b5 |

### Design

1. **`DtrhSaveSlots`** (`Features/Dtrh/DtrhSaveSlots.cs`, contract-named): three slots on SP-005 machinery.
   - **Files:** `dtrh_slot1.json`..`dtrh_slot3.json` BESIDE settings.json (WPF one-file-per-slot identity, ChaosMetaStore.cs:30) + a DTRH-owned index document `dtrh_slots.json` holding `{activeSlot}`.
   - **Active-slot placement DECIDED BY FILE SCOPE (consult):** WPF keeps it in app settings, but `Persistence/DemoSettings.cs` is outside this packet's File Scope; a 4th `PersistenceStore<DtrhSlotIndex>` on the SAME SP-005 machinery is not a "second persistence path" (same store class, same contract). Index store + 3 slot stores, each with its OWN named `AsyncOperationOwner` (`AsyncOperationOwner.Begin()` cancels the previous generation, OperationRegistry.cs:148-159 — N stores sharing one owner would cancel each other's in-flight writes).
   - **Slot document** (`DtrhSlotDocument`, schema v1): `sparks`, `gold`, `runsCompleted`, `bestScore`, `craftedItems` (dict; WPF key names "ragdoll"/"porcelain" verbatim), `[JsonExtensionData]` for unknown-member preserve (contract §6). b4 adds progression members WITHOUT a schema bump (SP-007 unknown-member precedent); a rename/restructure would need a journaled migration (none planned).
   - **Eager load:** all four stores start in the participant's StartAsync. Missing → LoadOutcome.Missing → flagged defaults, NOT dirty → **no file created** (empty slot stays file-less, matching WPF Exists=false). Corrupt → SP-005 quarantine + typed Degraded. **Intentional divergence from WPF picker semantics (recorded):** WPF shows a corrupt slot as Exists=true zeroed + deletable (ChaosMetaStore.cs:84-85); greenfield quarantines at load (packet mandates SP-005 corruption quarantine) — the card surfaces the typed Degraded flag ("save unreadable — preserved aside") instead of silent zeros. Higher authority: packet honesty framing (a).
   - **Summaries** come from the loaded stores + file existence/mtime — NO parallel read path, no second format. LastPlayed = file mtime (ChaosMetaStore.cs:88).
   - **Descend into an empty slot persists a fresh document immediately** (consult: defines lastPlayed; gives the restart-persistence proof; WPF creates on first flush — divergence recorded, user-observable outcome identical).
   - **Switch semantics** port ChaosUpgrades.cs:224-243: select = set active in index (persist) + the slot store is already file-loaded; delete = store-independent file delete through the machinery (slot store Reload → Missing defaults); no banking of the outgoing slot.
   - **Stitch-lock:** slot 2 locked unless any summary HasRagdoll or slot 2 exists; slot 3 same with porcelain (ChaosSlotPickerWindow.xaml.cs:68-80).
2. **`DtrhProtocol`** (`Features/Dtrh/DtrhProtocol.cs`, contract-named): the full v1 vocabulary above as typed records.
   - `ParsePageMessage(json)` → typed message | Malformed; `Classify(msg)` → per-message outcome: **Handled** (b1/b2-owned: ready, log, heartbeat, exit, fullscreen-set, boot-error→typed close) / **Deferred(slice)** (typed, names b3/b4/b5/unassigned; logged presence+shape only — never silent, never crash) / **Unknown(type)** / **ForwardVersion(type, version)** (`protocol` field > 1).
   - boot-error product reaction (consult): WPF falls back to the classic game — **banned in greenfield (§5)**; the page already shows its own honest no-WebGL surface (boot.js:82-101); host outcome = typed diagnostic + close. Never silent.
   - Host→page builders for ALL 12 types with exact payload shapes (camelCase; `params` via [JsonPropertyName]); round-trip tests pin shapes against the payload handler reads. Only b1/b2-owned sends are wired (init, manifest, fullscreen echo); the rest exist as tested builders for b3…b5.
3. **Picker + quick start:** `DtrhSlotPickerWindow` (Avalonia) ports the WPF outcomes: three cards fixed order, New Journey empty state, rank/descents/✦/🪙/best/last-played populated state, Stitched Shut locked state (dimmed, unclickable, no delete), per-card delete with confirm + re-lock warning, footer with save folder path, DESCEND commits / close cancels, accent-border selection. Visual grammar follows the b1 host surface (dark #0b0b16, pink accent — DtrhHostWindow.axaml) per dashboard-design (no theme-token system exists in the greenfield yet; recorded, not invented). Quick start = `DtrhLaunchCoordinator.QuickStart()`: skips picker, uses active slot, boots host window (MainWindow.Lab.cs:161-165 outcome).
4. **File Scope amendment (SP-023 precedent, documented here + STATUS + evidence + board row; `fileScopeMustNotChange` untouched):** add `client/src/CcpClient.Desktop/Lifecycle/CompositionRoot.cs` (construct DtrhSaveSlots with four named owners + pass to DtrhParticipant — 2-line wiring) and `client/src/CcpClient.Desktop/Program.cs` (`--dtrh-quick` flag + picker-first routing in the existing --dtrh-demo path). The packet's checkboxes are unsatisfiable without these wiring files (constitution smallest-document rule).
5. **Composition:** `DtrhSaveSlots` constructed in CompositionRoot.DefaultParticipants (owners "DtrhSlotIndex", "DtrhSlot1..3"), owned by `DtrhParticipant` (StartAsync starts stores; StopAsync cancels + FlushAsync bounded per SP-005 §11).

### Consults

#### Pre-approach consult (Step 1)

**Mode:** solo (council route broken — T-7). **Requested:** Fable 5. **Actual answering model:** NOT surfaced by the consult tool response (no model identity header — recorded honestly, same provenance discipline as SP-022/SP-023).

**Verdict (decisive points folded into the design above):**
1. **Active-slot: DemoSettings route is BLOCKED BY FILE SCOPE** (`Persistence/DemoSettings.cs` not in scope) — DTRH-owned index document on the same SP-005 machinery is the call; a 4th PersistenceStore is NOT a second persistence path. CompositionRoot untouched beyond passing owners (DtrhParticipant already registered).
2. **Summary reads must NOT quarantine** — opening the picker must not move files aside; only load-for-play quarantines. (Resolved in design: stores load at participant start, so quarantine happens once at startup, never at picker-open; summaries read store state + file facts.)
3. **One AsyncOperationOwner per store** — `Begin()` cancels the previous generation (verified OperationRegistry.cs:148-159).
4. **Slot document:** b4 members land without schema bump (unknown-member preserve); keep WPF craftedItems key names verbatim; file naming fine.
5. **Empty-slot descend should persist the fresh document immediately** (defines lastPlayed; restart-persistence proof).
6. **Deferred mapping honest;** `exit` stays handled; **boot-error needs a typed non-silent outcome** (page shows its own no-WebGL surface; classic fallback banned §5) → typed close.
7. **`params` keyword:** STJ needs [JsonPropertyName("params")] on loom-list builder.
8. Quick start as coordinator + flag: acceptable honest port (no dashboard exists; recorded).

---

## Engine-review log (T-2)

| Step | Type | Result | Artifact |
|------|------|--------|----------|
| 1 | plan | **SKIPPED BY DESIGN** (nested reviewer spawn blocked in worker session; `skipped=true, spawnFailed=false` — engine runs reviews after `.DONE`, SP-195) | `.reviews/1-20260721T204234.md` |
| 2 | plan | SKIPPED BY DESIGN (same) | `.reviews/2-20260721T205239.md` |
| 3 | plan | SKIPPED BY DESIGN (same) | `.reviews/3-20260721T210045.md` |
| 4 | plan | SKIPPED BY DESIGN (same) | `.reviews/4-20260721T222632.md` |
| 5 | plan | SKIPPED BY DESIGN (same) | `.reviews/5-20260721T222959.md` |

---

## Step 5 — verification (final tree, post consult fixes)

- **Windows:** `dotnet build client/CcpClient.sln -c Debug -t:Rebuild` **0W/0E** (warnings measured on Rebuild per the xUnit1051 lesson); CcpClient.Tests **292/292**; CcpClient.HeadlessTests **27/27**.
- **WSL2 (`~/ccp-sp024`, re-synced final tree):** sln **0W/0E**; **292/292 + 27/27**.
- `git diff --check` clean; `git status --short` = File Scope paths only (record.md tail).

---

## Step 4 — picker + quick start + headed/WX evidence + board reconciliation

### Product surface

- **`DtrhSlotPickerWindow.axaml(.cs)`** — ports the WPF outcomes (per-block cites in code): three fixed-order cards, New Journey / populated (rank via the ChaosRanks threshold port `RankName` — ChaosRanks.cs:11-55) / Stitched-Shut locked (dimmed, unclickable, no delete), per-card delete with an IN-WINDOW confirm overlay (the MessageBox outcome, ported so the flow stays headless-testable — no second modal) incl. the stitch re-lock warning, folder footer + open-folder (explorer/xdg-open best-effort), Esc/Enter, cancel backs out. Visual grammar = the b1 host surface (#0b0b16 + pink accent) per dashboard-design (no theme-token system exists in the greenfield yet — recorded, not invented). Modal parity via ShowDialog.
- **`DtrhLaunchCoordinator`** — the two WPF entries: `LaunchWithPickerAsync` (hero card: picker → DESCEND commits → descend → host) and `QuickStartAsync` (skips the picker, reuses the live slot — MainWindow.Lab.cs:161-165). Descend writes are awaited SP-004 owned completions; failures are typed diagnostics, never swallowed. Events HostOpened/FlowEnded drive the demo harness (auto-close arm; one-shot lifetime shutdown).
- **File Scope amendment EXTENDED (same SP-023 norm, `fileScopeMustNotChange` untouched):** `Program.cs` (`--dtrh-quick`, `--dtrh-picker-timeout` flags), `App.axaml.cs` (the --dtrh-demo block routes through the coordinator; auto-close arms at HostOpened; one-shot FlowEnded → desktop.Shutdown). 3 wiring files total (CompositionRoot in Step 2).
- `DtrhHostWindow` gains an optional slot param (status + diagnostic); title now "Down the Rabbit Hole" (WPF parity DtrhHostService.cs:113).
- **Headless tests (4 new, draw-level):** fresh-profile card structure (3 cards, locked classes, New Journey, folder text, no delete buttons), populated card (rank/stats/last-played + delete → confirm overlay → erase → file gone → New Journey), commit/cancel outcomes, RankName thresholds. 26/26 green.

### WH (Windows headed, DISPLAY3 owner convention — SetWindowPos to (-2576,1091) + GetWindowRect verified before EVERY capture; transcripts in `evidence/wh/`)

- **Run A (picker open/list/select/confirm, `runA-picker.log`):** fresh profile → picker on DISPLAY3 (GetWindowRect `(-2576,1091)-(-1840,1610)` verified) → UIA dump = full content evidence (3 cards, New Journey, Stitched Shut ×2 with boudoir hints, folder path, Cancel/DESCEND) → `picker-fresh.png` (visually verified: selected-card accent glow, locked dimming) → UIA click DESCEND → host opens on slot 1 → **ENGINE LIVE** (`host-engine-live.png`: full Warren hub; dark=1.9%, ~200 colors) with the b2 dispatcher live in the log (`'MetaCommand' deferred to slice b4 (typed, not dropped)`) → real click + ESC-hold 1500ms → page `exit` → clean teardown. **File-content proof:** `dtrh_slot1.json` + `dtrh_slots.json` in the SP-005 document shape (schemaVersion + migrationJournal) — `slot1-file-proof.json`, `index-file-proof.json`.
- **Run B (restart persistence + delete flow, `runB-restart.log`):** relaunch → slot 1 POPULATED in the picker (Curious, 0 descents, ✦0 drops, 🪙0 gold, "Last played Jul 21, 11:13 pm", delete button — UIA dump + `picker-restart.png`) → delete button (UIA InvokePattern) → confirm overlay (`picker-delete-confirm.png`: "Erase Save 1? …can't be undone") → Erase → card rebuilt as New Journey + `dtrh_slot1.json` deleted (index survives — WPF parity) → Cancel → exit WITHOUT launch, clean teardown.
- **Run C (quick start end-to-end, `runC-quickstart.log`):** `--dtrh-quick` → `quick start — reusing slot 1 (picker skipped by design)` → slot file recreated → ENGINE LIVE (`quickstart-engine-live.png`, dark=1.9%) → window-close → FlowEnded → shutdown, exit clean. ESC-hold missed on this run (harness foreground/focus class — SP-023 recorded the same class; the ESC page-exit path itself is proven in run A; window-close teardown proven here).
- **Run D:** glyph-fix re-capture of the restart-populated picker (`picker-restart.png`, ✕ delete button renders).
- **Run E-series (select-by-click + slot-2 descend):** pre-seeded `dtrh_slot2.json` (WPF back-compat clause: a pre-existing save keeps its slot open) → picker shows slot 2 unlocked + populated (Tempted / 4 descents / ✦7 / 🪙3 / best score / Last played — UIA dump) → raw click on card 2 moves the selection glow (`picker-slot2-selected.png`) → E8 (forensics below): timed commit of the slot-2 selection → `descending into slot 2` → ENGINE LIVE → auto-close → **exit=0** (`runE8-slot2.log`); index file proof `activeSlot: 2` + slot-2 file proof. E3–E7 harness wedge class recorded under forensics below.

### WX (WSL2 Ubuntu, WSLg X11-via-XWayland; `~/ccp-sp024` native ext4, never /mnt/e; no input automation; no timing claims; Wayland untouched)

- **Contract testCommand on the synced tree:** sln 0W/0E; **291/291 + 26/26 green**.
- **Probe round-trip with the b2 dispatcher (`wx/probe-wx.log`, EXIT=0):** `probe-p2h-ica` arrived via invokeCSharpAction (unknown type tolerated, harness-channel verbatim log); `check3 host->page DELIVERED via bridge.on {"type":"probe-h2p","via":"inbox"}`; preBuffer REPLAY delivered. NOTE: the probe harness now requires `--dtrh-quick` (b2 opens the picker first — see surprises).
- **Picker render facts (`wx/picker-wx.log`, `wx/wx-picker.png`):** picker rendered on WSLg — xwd XGetImage of the window id (720x480, mean 10.9%, std 0.11 — content, not a dark surface; visually verified: full picker with Linux folder path). Timed-drive commit (30s, honestly labeled) → descend → dialog opens → **ENGINE LIVE on Linux** → auto-close → full teardown. Slot file proof on Linux: `wx/wx-slot1-proof.json`.
- **Quick start on Linux (`wx/quick-wx.log`, EXIT=0):** `--dtrh-quick` → reused slot 1 (= Linux restart persistence: the slot existed from the prior run) → ENGINE LIVE → timed close → teardown end, exit 0.

### Surprise ledger

1. **The b2 flow broke the b1 probe harness** (`--dtrh-demo` alone now opens the picker; the probe page never loads) — harness entry is `--dtrh-quick` now. b3–b5 packets must know.
2. **Coordinate click missed the delete button** (window not top at the point — SP-023's topmost lesson class); **UIA InvokePattern is the robust button driver** (name-matched, no coordinates); coordinate clicks remain fine for canvas/window bodies with a topmost raise.
3. **🗑 renders as a glyph-fallback box** in the picker (Segoe Emoji coverage) → `✕` (headless test + capture updated).
4. **`{0:N0}` group separators are culture-dependent** — headless assert on the label, not the formatted number.
5. **ESC-hold exit missed on run C** (harness focus class; heartbeats proved no wedge; run A proved the path; window-close teardown proven on run C).
6. **Run 2's exit-code echo was lost** (backgrounded subshell orphaned when the WSL session returned) — run 3 re-proved EXIT=0 in the foreground; recorded, not claimed for run 2.
7. Emoji names in UIA dumps print as `??` (console codepage) — cosmetic; UIA matching by the exact char works.

### Consults

#### Pre-completion consult (Step 4)

**Mode:** solo (council route broken — T-7). **Requested:** Fable 5. **Actual answering model:** NOT surfaced by the consult tool response (recorded honestly, same discipline as SP-022/SP-023).

**Verdict: TWO fix-first items (both fixed before .DONE), rest approved.**
1. **Select-by-click was unproven** (fresh profile locks cards 2/3; only default selection was exercised). Fixed two ways: (a) headless real-input test — ragdoll unlocks slot 2, `MouseDown`/`MouseUp` at card 2's center → `SelectedSlot` 1→2 + `sel` class moves + commit = 2 (`CardClick_ChangesSelection`); (b) headed: pre-seeded `dtrh_slot2.json` (the WPF back-compat clause — a pre-existing save keeps its slot open) → raw click on card 2 moved the selection glow (`picker-slot2-selected.png` / `e4-after-click.png`) → E8 descend into slot 2 end-to-end (below).
2. **Corrupt slot 2/3 + stitch-lock hid the Degraded flag** (quarantined slot → Exists=false → slot re-locks → Stitched-Shut card hides the quarantine; the WPF corrupt card stayed visible + deletable). Fixed: `IsSlotLocked` treats a Degraded slot as open (one line + `StitchLock_DegradedSlotStaysOpen_FlagNotHidden` test).
- Confirmed acceptable: eager-load at startup (quarantine once, consistent with the SP-005 main store); delete-via-store-recreation (owner accumulation bounded at 3/delete — recorded); modal ShowDialog flow (one-shot FlowEnded guard; WX empirically exercised); ForwardVersion-before-Unknown ordering.
- Record-attribution nit (fixed): `picker-restart.png` is run D's re-capture (glyph fix) — restart persistence proven by run B's UIA dump + run D's capture, both with the populated card.

### E-series forensics (harness-side wedge class, recorded honestly)

Runs E3–E7 hit a reproducible harness anomaly: after cross-process drive of the MODAL picker (SetWindowPos incl. an HWND_TOPMOST wrapper + raw clicks + backgrounded/tee'd stderr), the app ended in a state with **zero HWNDs on the pid, the picker visually gone, the UI thread alive in `Dispatcher.MainLoop`, and `LaunchWithPickerAsync`'s `ShowDialog<bool>` task pending forever** (dotnet-dump evidence: dumpasync STACK 3 + main-thread clrstack). The picker HWND vanished without Avalonia's Closed event — no product path observes that (the Closed handler never ran, so no descend, no FlowEnded; the lifetime never saw last-window-close). E6 self-resolved after ~25 min; E7's process died silently with a truncated log (tee-subshell stderr loss). **E8 (foreground, plain `>`, no cross-process manipulation, timed commit) ran the identical product flow CLEAN: `picker timeout — committing current selection` → `descending into slot 2` → `auto-close armed` → `ENGINE LIVE` → `auto-close firing` → `flow ended` → `teardown end`, exit=0** (`runE8-slot2.log`). Verdict: harness-side (cross-process z-order/position manipulation of a modal + backgrounded stderr plumbing), NOT a product defect — runs A–D (UIA InvokePattern, no topmost on the modal) and E8 (no manipulation) never wedged. Harness rule going forward: **UIA InvokePattern or timed drive for modal buttons; topmost raise only for canvas clicks on non-modal windows; foreground runs for exit-code evidence.**

### Budgets

Product sln build ~4-9s Windows incremental / ~15s WSL; tests 33s + 11s Windows, 19s + 10s WSL; headed runs A-D ~2min total; WX runs ~5min.

---

## Step 2 — three local save slots (SP-005 machinery)

- **`Features/Dtrh/DtrhSaveSlots.cs`** (contract-named): `DtrhSlotDocument` (schema v1: sparks/gold/runsCompleted/bestScore/craftedItems + ExtensionData), `DtrhSlotIndex` (activeSlot), `DtrhSlotSummary`, `DtrhSaveSlots : IBackgroundParticipant` — four `PersistenceStore<T>` instances (index + 3 slots), each with its OWN named owner (`Begin()` cancels the prior generation, OperationRegistry.cs:148-159). Files `dtrh_slot1..3.json` + `dtrh_slots.json` beside settings.json. Semantics per the Step-1 design: eager phase-3 load (missing stays file-less, corrupt quarantines once at startup — never at picker-open), summaries from loaded stores + file facts (no parallel read path), `SelectSlot` persists the index immediately (ChaosUpgrades.cs:230-243 parity, no banking of the outgoing slot), `DescendInto` persists a fresh document when empty, `DeleteSlot` deletes file+tmp, stops the old store (an in-flight write can't resurrect the file), reloads fresh. Stitch-lock ports ChaosSlotPickerWindow.xaml.cs:60-80 (any save's doll unlocks globally; a pre-existing save keeps its slot open).
- **File Scope amendment (documented here + STATUS + board row at land):** `Lifecycle/CompositionRoot.cs` — registers DtrhSaveSlots after the demo store (2 lines) + chains its flush into the reserved pre-drain slot. Unsatisfiable otherwise (constitution smallest-document rule; SP-023 precedent); `fileScopeMustNotChange` untouched.
- **Tests (10 new):** fresh-start empty+ordering+no-files; descend persists + restart reload; mutate/persist/reload round-trip; corruption → Quarantined + flagged defaults + preserved bytes + logged Degraded; unknown-member preserve across load+save; stitch-lock matrix incl. pre-existing-save back-compat; delete → fresh reload → descend-starts-over; activeSlot clamp; select-persists-immediately-but-does-not-create. **255/255 green Windows.**

## Step 3 — protocol v1 full vocabulary

- **`Features/Dtrh/DtrhProtocol.cs`** (contract-named): all 21 page→host types as typed records with payload-shaped fields (sfx scale default 0.6 per DtrhHostService.cs:226; request-run setup optional per :431); `ParsePageMessage` NEVER throws — Parsed / UnknownType / ForwardVersion (protocol field > 1, checked before the unknown-type branch) / Malformed; `Classify` maps every message to Handled (b1/b2-owned) or Deferred(b3/b4/b5/unassigned-host-ui) — typed deferral, never a stub. Host→page builders for all 12 types (DtrhRunSetup 12-field record per DtrhHostService.cs:483-510; DtrhLoomSpiral with [JsonPropertyName("params")] — the C# keyword trap the consult named).
- **Host window wired onto the dispatcher:** OnWebMessage → parse → dispatch; probe-* harness channel preserved (logged verbatim as both-directions evidence); Deferred messages logged presence+shape only; **boot-error = typed diagnostic + honest close (no classic fallback, §5 — the page shows its own no-WebGL surface)**; **fullscreen echo added** (WPF parity DtrhHostService.cs:430 — b1 toggled WindowState without echoing); boot messages now built via DtrhProtocol.BuildInit/BuildManifest (same values/shapes as b1's proven literals); SendToPage serializes via SerializeForPage (camelCase).
- **Tests (36 new):** theory over all 21 page literals (parse type + classification), typed-field shape asserts, unknown/forward-version/malformed tolerance (6 malformed cases, never throws), all 12 host→page builders round-tripped through SerializeForPage → JsonDocument with the payload handler's field names (incl. `params` keyword + payout-result flat shape). **291/291 + 22/22 green Windows, 0 warnings.**
