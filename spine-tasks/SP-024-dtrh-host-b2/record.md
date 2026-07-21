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
