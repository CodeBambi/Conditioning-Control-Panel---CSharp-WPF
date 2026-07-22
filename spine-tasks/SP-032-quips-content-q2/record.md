# SP-032 — Quips/sound arbitration q2: bark content pipeline + DTRH bark wiring — worker record

**Started:** 2026-07-22 · **Lane:** spine-20260722T122014/lane-2

## Step 1 — WPF content-pipeline archaeology (READ-ONLY, File.cs:line)

Extracted via wpf-archaeologist subagent (34 tool uses, sections 1–7 verified against the WPF tree) + worker direct reads (sections 8–9, self-echo).

### Rules engine (`Services/Bark/`)

- **BarkRule** (`Bark/BarkRule.cs:42-118`): Id, Trigger, Conditions, Priority, CooldownMs, Repeatable (default **true**), Scope (Session/Tier/Lifetime — meaningful only when non-repeatable, `:28-32`), Mood, Class (Normal/EasterEgg/Safety — Safety = "panic/abort lines… never preempted", `:15-20`), VariantPool, PoolRef (reference to a CompanionPhraseService category), Chance (default 1.0, rolled LAST in the gate). `IsValid()` = id + trigger + (variant_pool OR pool_ref) (`:111-114`).
- **Loader** (`BarkRuleLoader.cs:72-101,117-149`): embedded base manifest; mod override preferred; FIELD-LEVEL merge by rule id (JObject.Merge, arrays Replace, nulls Ignore — mod owns variant_pool outright, omitted fields inherit, `:62-66`); bad manifests log + partial set, never throws.
- **RuleSet** (`BarkRuleSet.cs:15-16,35-36,40-43`): bucketed by trigger (case-insensitive), each trigger list sorted **priority-descending**; matcher takes the FIRST condition-passing rule (`BarkService.cs:790-794`) — the gate is evaluated for that winner ONLY; a cooldown-blocked winner means NO fallback to lower-priority rules (VERIFIED).
- **Conditions** (`BarkService.cs:1062-1156`): operator by key suffix `_gte/_lte/_gt/_lt/_eq` (bare = eq); unresolvable field → condition FAILS (`:1079`); eq: bool → numeric (eps 0.0001) → ordinal-ignore-case string; ordered ops need both sides numeric (bool→0/1, numeric-string invariant).
- **Variant selection** (`BarkService.cs:1403-1419,1501-1534`): repeatable+pool>1 = **random among per-rule-unused** (line ids); exhaustion → recycle, reseed excluding the just-played line, rotation persisted. One-shot = uniform random over the pool. `PickVariant` layers: hard per-rule no-repeat, then soft global recency — prefer audio NOT in the last-**8** spoken set (`RecentlySpokenMemory = 8`, `:58`); all-recent → fall back to unused (preference, never silence).
- **Line identity**: `BarkLineId = "Bark:" + ruleId + ":" + (audio filename stem | "t_"+Slugify(text))` (`BarkService.cs:1203-1209`); Slugify drops {tokens}, lowercases, non-alnum→space (`CompanionPhraseService.cs:84-95`).
- **Latches**: session one-shot `_firedOnceSession`; tier/lifetime persisted in `AppSettings.BarkLifetimeFired` (tier latch key `id@PatreonTier`, `BarkService.cs:1433-1436`). Variant rotation persisted to `AppSettings.BarkVariantRotation` + `BarkIdleRotation` (`:1538-1577`). `ReloadRules` clears session latches + cooldowns, preserves rotation + lifetime (`:406-425`).

### Gate evaluation order (VERIFIED, `BarkService.cs:1305-1422`)

1. empty-pool → blocked.
2. bypass = Safety-class OR guaranteed.
3. one-shot latch (NOT bypassed by guaranteed; bypassed by Safety) → "already-fired ({Scope})".
4. Non-bypass, in order: (a) **safety-hold** 6000 ms after any Safety fire (`:87,:1338-1340,:1456-1457`) → "safety-active"; (b) **whisper-active** (`IsWhisperAudioPlaying`, `:1343-1345`); (c) narrator-active (ChaosNarrator, `:1349-1350`); (d) **chat-suppressed** (`BarkChatSuppressionMs` fallback 10000, `:1351-1355`); (e) **anti-stale**: ordinary (`!willPreempt`) while `IsSpeaking` → dropped "speaking" (`:1358-1365`); (f) **global min-gap 60s** (exempt: AttentionCheckFail, `:68,:78,:1369-1375`); (g) **per-rule cooldown** (`:1378-1383`); (h) **chance roll LAST** (`:1386-1388`).
5. Priority routing (`Speak`, `:1624`): `Class != Normal || Priority >= 100` → preempt (clear queue + show now, `Speech.cs:319-360`); else queue.

### Payload assembly / mute / persistence

- **Payload unit**: BarkVariant {Text, Audio?} (`BarkVariant.cs:19-27`); audio resolved at speak time (`ResolveBarkAudio`, `:1280-1299`); substitutions `{0}` → focused app name (fallback "that") + every ctx fill as `{key}` (`:1638-1656`); emotion = `emotionLineId` (audio filename stem) + rule.Mood string, carried through the queue tuple `(text, source, emotionLineId, mood)` (`Speech.cs:180,:253-255,:313-315`) to the portrait emote system (`:462-467`). PersonalityService = AI-prompt presets only, NOT bark payload (VERIFIED).
- **Mute text-only (FULL surface)**: `_isMuted` — audio gated, bubble ALWAYS renders (`Speech.cs:483-505`, "#445"); MasterVolume==0 → every voice path early-returns, bubble still shows (`:2188-2189,:2208-2209,:1562-1564,:2243`); mute Easter egg — EasterEgg bark at volume 0 → text-only, no audio resolution (`BarkService.cs:1590-1598`); DispatchIdle hard-returns at volume 0 (`:857`). Volume formula: `Pow(master/100,1.5)` × per-path scale (bark 0.85, phrase/event 0.56, idle 0.8, giggle 0.7).
- **SelfEchoMuteMs 8000** (`:84,:1598,:1627`): mutes the KEYWORD-ECHO (KeywordTriggerService hearing its own bark) — NOT a bark gate. Lands with the keyword-trigger row; named limit here.
- **Disabled phrases** (`AppSettings.cs:4488,4491`; `BarkService.cs:1172-1245`; `CompanionPhraseService.cs:169-300`): `HashSet<string> DisabledPhraseIds` (+ RemovedPhraseIds) in settings.json; enabled iff in NEITHER set; bark lines share the sets via the `"Bark:"` id prefix; pool filtered at EVERY fire (`:1172-1180`); all-disabled → "empty-pool" gate; re-enable = remove from set, effective next ResolvePool (no caching); ids keyed by audio stem → content reorder never shifts disables.
- **Pacing** (`Speech.cs:108-119,148-168,570-573`): `delay = 2.0 + (lastSource==AI ? 5.0 : 0) + max(0, lastLen-100)*0.02` — computed from the **PREVIOUS** (just-ended) bubble's source+length, stored at hide time; AI-ness is the queued item's SpeechSource enum (Giggle→Preset, GigglePriority→AI `:351`), not a text heuristic. Lead-in 0.6 s is a UI pose concern (not pacing).
- **Rapid click cues** (`ChatInput.cs:31,:59-67,:81-83`; `Speech.cs:2271-2345`): rolling 60 s click window, ≥50 → Clear + collapse trigger (priority line + audio); pop SFX only 1-in-25 clicks; other consumers: achievements, `Bark.NotifyAvatarClicked` (clicks_60s fill). Latency demand on the SFX pool is low by design (the 1/25 gate).

### Stale-device UX surface (worker direct read — archaeologist section 8 closed)

- WPF has NO toast/dialog: settings combo restore = ID match → name match → **silent fallback to "System default"** (`MainWindow/MainWindow.UiUpdates.cs:934-967`, `pick ?? devices[0]`); audio layer warns in log + defaults (`AudioService.cs:292-293`, SP-029 archaeology). q2 UX parity = q1's typed fallback + logged session facts (already landed); a settings-surface device picker is the settings row's, not this slice.

### DTRH bark path (worker direct read — archaeologist section 9 closed)

- `DtrhHostService.cs` `RouteBark` (~:618-650): `bark {event, ...}` → switch over ~35 event names → `App.Bark.NotifyChaos*(...)`; unrouted → Debug log, never a crash; `_testMode` returns early. Notify* → `Raise(trigger, fills)` (e.g. "wave-cleared" → `ChaosWaveCleared` {wave}; "detonated" → `ChaosBubbleDetonated` {payload,strength,run_detonations,combo,difficulty}, `BarkService.cs:262-363`). Voice stays native; BarkService's own gates decide what speaks.

## Step 1 — Design (q2 over q1's core — CONSUMES `Audio/SoundArbitration.cs`)

New home `client/src/CcpClient.Desktop/Companion/` (contract-named `BarkPipeline.cs`).

### q1-surface changes (per-change justification, packet framing a)

1. **ADD `SoundArbitration.VoiceActive` (bool, gate-checked `_voice is not null`)** — the anti-stale gate (WPF `IsSpeaking`, BarkService.cs:1358-1365) needs a speaking state; q1 exposed only events/counts. Pure addition, no behavior change. (No other q1 change: pacing enters via the per-item `pacing` parameter exactly as the q1/q2 boundary recorded; freshness stays caller-supplied — see policy below.)

### Companion/BarkRules.cs — content model + loader + ruleset

- `BarkRule` (WPF schema field names: id, trigger, conditions, priority, cooldown_ms, repeatable, scope, mood, class, variant_pool, pool_ref, chance); `BarkVariant { Text, Audio? }` (bare-string or object form, BarkVariant.cs:31-54).
- Loader: System.Text.Json, per-rule tolerance (invalid rule → logged + skipped, partial set — BarkRuleLoader.cs:117-149 parity). **pool_ref: loads but resolves to an empty pool → "empty-pool" gate** (the phrase-category service is unported — named limit, logged at load; never invented).
- RuleSet: trigger bucket (OrdinalIgnoreCase), priority-desc per trigger, first-condition-passing wins, no gate fallback (WPF :790-794).
- Condition eval: `_gte/_lte/_gt/_lt/_eq` + bare=eq; unknown field → fail; numeric coercion incl. bool→0/1 + invariant numeric strings (WPF :1062-1156).
- **DefaultBarkRules**: compact BUILT-IN rule set as a C# string constant (no csproj/asset-scope change needed) — one rule per semantic (safety Panic; conditioned ChaosWaveCleared ×3 variants; lifetime one-shot ChaosDefuseFirst; easter_egg; chance-gated; cooldown rule). Full WPF content migration = content row (named limit); the loader is schema-compatible so it drops in.

### Companion/BarkPipeline.cs — trigger → gate → variant → payload → arbitration

- `Raise(trigger, fills?, guaranteed)` → typed `BarkOutcome` (Surfaced / SurfacedTextOnly(reason) / Gated(reason) / NoRule / Unavailable). Gate = the VERIFIED WPF order above with greenfield signal mapping (consult binding 3 — the bypass matrix pinned: the one-shot latch is bypassed by **Safety only, NOT `guaranteed`**; gates 4a–4h are skipped when `bypass = isSafety || guaranteed`; the AttentionCheckFail min-gap exemption stays orthogonal to `guaranteed`):
  - whisper-active ← `SoundArbitration.WhisperBusy` (real-event, replaces the WPF estimate — q1).
  - anti-stale "speaking" ← `SoundArbitration.VoiceActive` (q1 addition #1). Ordinary + speaking → typed Gated("speaking") — never queued (WPF gate-level drop, :1358-1365); this IS WPF's freshness surfacing.
  - narrator-active: OMITTED (no narrator exists in greenfield — never a permanently-false gate; recorded).
  - chat-suppressed: seam `Func<bool>? ChatBusy` (default null = not busy — no chat UI exists; the chat row wires it; mechanism present, signal honest).
  - min-gap 60 s, exempt set {AttentionCheckFail}; safety-hold 6000 ms after Safety fires; per-rule cooldown; chance LAST.
- Variant selection: WPF verbatim semantics (unused-rotation with recycle-reseed-excluding-last; one-shot uniform; recency-8 soft preference never silences; rotation persisted).
- **Payload integrity**: ONE immutable `BarkPayload` (Text after substitution, AudioPath?, EmotionLineId = audio stem, Mood, LineId, RuleId, Priority, Class, IsAi=false) assembled BEFORE any hand-off; `BarkSurfaced` event always carries the full unit (never a torn payload — text/audio/emotion travel together).
- **Hand-off**: priority (`Class != Normal || Priority >= 100`) → `PlayVoicePriority`; ordinary → `QueueVoice` (starts immediately when idle; waits out pacing debt otherwise — q1's ScheduleNextVoiceLocked). **Freshness policy = null (no ms-age expiry) — WPF VERIFIED has none (SP-029 record); the mechanism stays caller-supplied, q2's cited policy is gate-level anti-stale.** Audio gain = `Pow(master/100, 1.5) × 0.85` (bark scale).
- **Pacing (q1 seam, TEXT-derived)**: pipeline tracks the previous surfaced line (length + IsAi) and computes `2.0 + (prev.IsAi ? 5.0 : 0) + max(0, prevLen-100)*0.02` (Speech.cs:148-168 — PREVIOUS line's properties) into `QueueVoice(pacing)`. Barks are never AI in WPF Speak (aiGenerated:false) — the AI bonus arms when a chat row surfaces AI lines through the same path.
- **Mute text-only (typed, surfaced, never silent)**: `Muted` flag OR MasterVolume==0 → `BarkSurfaced` fires with `Suppression=Muted|VolumeZero`, NO voice call (Speech.cs:483-505/:2188-2189 parity). EasterEgg+volume0 → text-only without audio resolution (BarkService.cs:1590-1598). Self-echo mute = keyword-row named limit.
- **Resolver seam** `IBarkAudioResolver` (rule audio name → file path; missing → typed `SurfacedTextOnly(AudioMissing)`, logged — never silent, never a crash). No voice assets ship in this slice (named limit); the DTRH host's resolver roots at a companion sounds dir.
- Panic: pipeline clears NO arbitration state itself (WPF: BarkService clears nothing on panic — the avatar channel owns it, SP-029 archaeology); `SoundArbitration.PanicReset()` remains the single cleanup. Safety-class "Panic" trigger routed through the normal gate bypass.

### Companion/CompanionState.cs — disabled-phrase + latch persistence on SP-005 machinery

- `CompanionStateDocument { DisabledPhraseIds, LifetimeFired, VariantRotation, IdleRotation }` (schemaVersion 1) via `PersistenceStore<CompanionStateDocument>` — schema-versioned, atomic writes, quarantine → typed Degraded + flagged defaults (never a parallel settings file, never silent).
- Disable/re-enable round-trips: `Mutate` + `SaveImmediate`; effective at next selection (no caching — WPF :1172-1180). Lifetime one-shot latches + variant rotation persist through the same document (WPF AppSettings parity).
- Tier latch key `id@tier` simplified: no Patreon tier exists in greenfield → key = id (tier suffix lands with the premium row; recorded, never invented).

### DTRH wiring (Features/Dtrh/** — outcome + dispatch seam ONLY)

- `DtrhProtocol.Classify`: Bark → `Handled.Instance` (comment updated: q2 owns the arbitration); `DtrhProtocolTests` vocabulary row updated (null = Handled).
- `DtrhBarkRouting.cs`: static event→(trigger, fills) table — the full ~35-event WPF RouteBark switch (DtrhHostService.cs:618-650 → BarkService.cs:262-363), fills extracted from the message JsonElement by the WPF field names; unrouted event → typed log (WPF default branch parity); never logs bark TEXT (presence+shape: event name + field count only — SP-016 content-free class).
- `DtrhHostWindow`: `InitBarkPipeline()` at Opened — SoundFlowAudioBackend + SoundArbitration (defaults; UnavailableDuckSink named limit stands) + `PersistenceStore<CompanionStateDocument>` (`<DataDirectory>/companion.json`, owner `Registry.OwnerFor("DtrhBarkCompanion")`) + BarkPipeline; dispatch `case Bark` → routing → pipeline.Raise; outcomes logged presence+shape (rule id + gate reason — manifest shape, never content). Teardown in Closing beside TeardownNativeEffects (TryStart-pattern compliance = arbitration's own guard; the pipeline adds no player-start paths). m2Test: pipeline still routes (barks are not meta state; WPF RouteBark's _testMode early-return guards XP-adjacent Notify paths — recorded; greenfield keeps routing honest in test mode since no side effects persist beyond the companion store, which the m2 window shares — flagged in evidence).

### Rapid click cues under voice/video (Step 3 evidence)

- The cue = SFX through arbitration's bounded pool (WPF: no dedicated queue, 1/25 pop gate — latency demand low by design). Evidence = REAL backend harness: voice + whisper active + rapid SFX burst (>8) → no starvation (voice/whisper complete via backend events), overflow typed PoolOverflow only, slots reclaim. Video = LibVLC separate engine (SP-025 b3 coexistence stands; arbitration has no video channel — mechanism fact, no new claim). The 50/min collapse trigger + 1/25 pop gate are avatar-click-INPUT behaviors (no avatar input surface in greenfield) — named limit, lands with the avatar-input row.

### Verification shape

- Unit: loader tolerance, condition ops, gate ORDER (each gate + short-circuit), one-shot latches (session vs persisted lifetime), variant rotation/recycle/recency, disabled-phrase round-trip + corrupt→quarantine on a real temp store, payload integrity (one unit, substitution, LineId stability), mute degradation (muted/vol0/egg), pacing math vs WPF cases (2.0 floor / +5 AI / 0.02·(len-100)), priority routing, TryStart-compliance (pipeline adds no start paths; panic mid-raise → typed outcome, never throws), DTRH routing table (all events + unrouted + malformed fields), protocol classification green.
- Headless: none new (service-core slice, q1 precedent — floor 29 stands).
- Harness: `evidence/harness/` console (out of sln, SP-029 shape) on the REAL SoundFlow backend; Windows backend-event-verified + WSL2 mechanism facts (no timing claims).

## Consult log

### Pre-approach (Step 1) — APPROVE with 5 bindings + 1 addendum

Requested route: solo Fable 5. Actual answering model: NOT surfaced in the consult tool output (SP-028/SP-029 precedent). Provenance note: the first consult call's verdict text TRUNCATED mid-binding-2 in delivery; bindings 2–5 were recovered via a follow-up gut-check on the same design (advisor reconstruct, consistent with the APPROVE) — recorded honestly as such.

Verdict: APPROVE all six design decisions as framed. Bindings (ALL adopted into the design):

1. **Pacing source tracking** — WPF computes delay from the previous line at DEQUEUE time (Speech.cs:148-168); q1's seam supplies pacing at ENQUEUE. Track the **last line handed to the voice channel** (queued OR played), not the last surfaced — otherwise a second bark queued inside the pacing window derives its delay from the wrong line.
2. **Freshness=null approved — do NOT invent a window.** The anti-stale gate drop must be TYPED + logged (`Gated("speaking")`), never silent. Record the gate→QueueVoice race (voice activates between the `VoiceActive` check and `QueueVoice` → ordinary bark queues behind the speaking voice; WPF tolerates this, Speech.cs:271-288 — noted so it isn't read as a parity bug).
3. **Bypass matrix pinned exactly** (BarkService.cs:1305-1422): the one-shot latch is bypassed by **Safety only, NOT `guaranteed`**; gates 4a–4h (safety-hold, whisper, chat, anti-stale, min-gap, cooldown, chance) are skipped when `bypass = isSafety || guaranteed`. The AttentionCheckFail min-gap exemption stays orthogonal to `guaranteed`.
4. **Mute decomposition**: (a) a Safety-class bark at volume 0 STILL sets the 6 s safety-hold — mute degrades the audio channel, never the gate state (CommitFire is unconditional, BarkService.cs:1456-1457); (b) `NoAudioAsset` (rule has no audio variant — WPF plays a giggle sfx we have no asset for) and `AudioResolveFailed` (asset listed, resolver misses) are SEPARATE typed text-only reasons — never collapsed.
5. **Companion-store flush gap (REAL)**: the host-local store is NOT in CompositionRoot's preDrainFlush list (CompositionRoot.cs out of File Scope) — bind `SaveImmediate()` into the window `Closing` handler beside TeardownNativeEffects; record that app-wide flush coverage stays incomplete until a future row lifts the store.
5b. **m2Test**: WPF `RouteBark` early-returns on `_testMode` (DtrhHostService.cs:622) — greenfield does the SAME (no bark routing at all in m2Test, typed log) rather than risking rotation/lifetime state leaking into the live companion document.

## Step 2 — implementation (landed)

- `Companion/BarkRules.cs` — rule model (WPF schema field names verbatim), tolerant loader (per-rule skip, partial set, never throws), RuleSet (trigger bucket OrdinalIgnoreCase, priority-desc, first-condition-passing, no gate fallback), BarkConditions (`_gte/_lte/_gt/_lt/_eq`, unresolvable → fail, live-field precedence, bool→0/1 + invariant numeric strings), BarkText (LineId `Bark:{rule}:{stem|t_slug}`, WPF Slugify verbatim, `{0}`/`{key}` substitution).
- `Companion/BarkPipeline.cs` (contract-named) — the gate in the VERIFIED WPF order with the consult-pinned bypass matrix (latch bypassed by Safety ONLY; floor gates skip on `isSafety || guaranteed`); anti-stale = typed `Gated("speaking")` via the new `SoundArbitration.VoiceActive` (the ONLY q1-surface change — additive); whisper gate via `WhisperBusy` (real-event, q1); variant rotation + recycle-reseed-excluding-last + recency-8 soft preference (never silence); ONE immutable BarkPayload (text substituted / audio path / emotion stem / mood / line id) assembled BEFORE hand-off; suppression BEFORE audio resolution (Muted / VolumeZero / NoAudioAsset / AudioResolveFailed / AudioUnavailable — typed, surfaced, never silent); CommitFire unconditional (Safety at volume 0 still sets the 6 s hold); priority (`Class != Normal || Priority >= 100`) → PlayVoicePriority, ordinary → QueueVoice(freshness: null — WPF has NO ms-age expiry; pacing = TEXT-derived from the last line HANDED to the voice channel, `ComputePacingSeconds` pure for direct WPF-case tests); gain `Pow(master/100, 1.5) × 0.85`.
- `Companion/CompanionState.cs` — `CompanionStateDocument` v1 (DisabledPhraseIds / LifetimeFired / VariantRotation + JsonExtensionData) on `PersistenceStore<>` — schema-versioned, atomic, quarantine → typed Degraded (never a parallel store). IdleRotation dropped (no idle dispatcher exists — schema migration when that row lands).
- `Companion/DefaultBarkRules.cs` — compact built-in set (8 rules, one per semantic: safety / conditioned / lifetime one-shot / easter_egg / chance / cooldown / priority / min-gap-exempt trigger). Full WPF content migration = content row (named limit); the loader is schema-compatible.
- **q1-surface change justification (packet framing a):** ONE additive property `SoundArbitration.VoiceActive` (gate-checked `_voice is not null`) — the anti-stale gate needs a speaking state and q1 exposed only events/counts. WPF IsSpeaking is bubble-visible; greenfield has no bubble → voice-player-active is the outcome-mapping (recorded, not line parity).
- Tests: `BarkPipelineTests.cs` — 29 new (loader tolerance, condition ops + precedence, matching/no-fallback, gate order + bypass matrix + safety-hold-at-vol0, rotation/recycle/recency + cross-instance persistence, payload integrity + slug stability, mute/vol0/egg suppression split, torn-down-arbitration never-throws, priority preempt vs queue, pacing formula cases + last-handed tracking through the real arbitration, disabled round-trip + corrupt→quarantine + lifetime latch persistence).
- Contract gate after Step 2: build 0W/0E; **441/441** (floor 412; +29) + **29/29** headless (floor 29 — no new headless tests: service-core slice, q1 precedent).

## Engine-review presence log (T-2)

- Step 1 plan review: `spine_review_step(step=1, type=plan)` → **SKIPPED by engine** (SP-195 nested-spawn block; `spawnFailed=false`, artifact `.reviews/1-20260722T124259.md`). Engine-run code+final reviews expected after `.DONE`.
- Step 2 plan review: `spine_review_step(step=2, type=plan)` → **SKIPPED by engine** (SP-195; `spawnFailed=false`, artifact `.reviews/2-20260722T130825.md`).

## Step 3 — rapid cues + DTRH wiring + backend-event evidence (landed)

- **Rapid click cues under voice/video (A11 coexistence class, backend-event-verified):** `evidence/run-windows.log` — voice (2500 ms) + whisper (1500 ms) active + 12-cue SFX burst through q1's bounded pool: **8 Started / 4 typed PoolOverflow (never silent)**; voice AND whisper completed NATURALLY via backend events under the burst (no starvation); pool drained to 0 on real events. Video = LibVLC separate engine (SP-025 b3 mechanism fact; arbitration has no video channel — no new claim). The 50/min collapse trigger + 1/25 pop gate are avatar-click-INPUT behaviors (no avatar input surface in greenfield) — named limit, lands with the avatar-input row.
- **DTRH `bark` Deferred → Handled:** `DtrhProtocol.Classify` → `Handled.Instance`; `DtrhBarkRouting.cs` = the full 32-event WPF RouteBark table (DtrhHostService.cs:618-650 → BarkService.cs:262-365) with WPF fill keys + reused-voice call-site constants (rabbit-caught → quick=true/combo=0); unrouted/malformed typed + logged, never thrown. `DtrhHostWindow` dispatch seam: `case Bark` → TryRoute → `BarkPipeline.Raise`; presence+shape logging ONLY (event name + outcome shape + rule id — NEVER bark text, SP-016 content-free class). m2Test skips pipeline construction + routing entirely (WPF `_testMode` early-return parity, DtrhHostService.cs:622 — no rotation/lifetime leakage into the live companion document; consult binding 5b). Store flush bound into `Closing` beside TeardownNativeEffects (consult binding 5 — the host-local store is NOT in CompositionRoot's preDrainFlush, which is out of File Scope). b1–b5 regression discipline: the DTRH contract suite green (446/446 incl. the updated vocabulary/deferral rows + 5 new DtrhBarkRoutingTests; 29/29 headless).
- **Headed DTRH host evidence: NOT used** (the DISPLAY3/rect/modal/orphan bindings do not arm) — the wiring is proven by the contract suite (routing table + classification + outcome shape) and the pipeline by the backend-event harness; the host glue is a thin dispatch case. Recorded honestly; a headed host run remains available to a future UI row that surfaces the bubble.

### Backend-event harness (console, out of sln — SP-029 shape; REAL SoundFlow backend)

- **Windows** (`evidence/run-windows.log`, EXIT=0, **29 PASS / 0 FAIL**): stale device NAME → typed fallback → Ready (AudioService.cs:292-293 parity); payload integrity as ONE unit (text/audio/emotion/mood/line-id); backend-event completion (never call-return); TEXT-derived pacing hand-off (rotation-aware — a text-only variant pick is the typed NoAudioAsset degradation, evidence point not failure); priority ≥100 preempt (interrupted gen NEVER completes, F2 generation filter); whisper gate via real-event WhisperBusy (set at play, cleared ONLY by the completion event); muted → typed SurfacedTextOnly(Muted) with payload surfaced + NO voice call; SFX burst coexistence (above); disabled-all → typed empty-pool + disabled ids round-tripped through the SP-005 store (fresh PersistenceStore instance); panic cleanup (all channels stopped, busy cleared) + recovery; teardown leak delta 0/0.
- **Linux (WSL2, `~/ccp-sp032`, native ext4, never /mnt/e)** (`evidence/run-linux.log`, EXIT=0, **29 PASS / 0 FAIL**): full matrix green on the REAL Linux backend; session facts `1 render endpoint(s): RDP Sink` (SP-017 A6 class fact — enumeration mechanism, not a selection claim). **NO timing/latency claims** (WSLg jitter); no audibility claims; Wayland never claimed.
- **WSL2 gate** (`evidence/wsl-gate.log`): build **0W/0E**; CcpClient.Tests **446/446**; HeadlessTests **29/29**.
  - Gate repair note (honest): the first WSL run failed 4 payload-asset tests (DtrhBridgeDiffTests ×2, AssetManifestTests ×2) — the rsync copied `client/` only, but the DTRH payload is a LINKED glob from the READ-ONLY legacy tree (`ConditioningControlPanel/Resources/web/dtrh/**`, csproj:49-53); adding that subtree to the rsync fixed it (pre-existing linkage, not an SP-032 defect). verify.mjs runs on Windows (verifies the lane's pi-spine INSTALL state — platform-independent; WSL has no node): initial FAIL (reinstall had removed all 6 patches) → `apply.mjs` re-applied → **exit 0** (SP-028/SP-029 precedent, same lane condition).
- Harness fix during Step 3 (harness-only, product untouched): the deterministic rotation alternates the 2-variant pool audio/text-only — sections 3/4 now accept the typed NoAudioAsset outcome as an evidence point and raise again for the audio variant (rotation + suppression semantics VERIFIED, not worked around).

### Windows contract re-run after Step 3

verify.mjs exit 0; build 0W/0E; CcpClient.Tests **446/446** (floor 412; +34 over floor: 29 Step-2 + 5 routing); HeadlessTests **29/29** (floor 29 — no new headless tests: service-core + dispatch-table slice, q1 precedent).

(pending)
