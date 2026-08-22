# Goon Game (GG) — Master Plan (2026-08-03)

1v1 multiplayer endurance duel: two players run mixed-and-matched CCP sessions against each
other; first to come loses (self-declared via the **Mercy** button), otherwise the higher
score at the end wins. "GG" is the sign-off on every recap.

Target branch: `feat/goon-game` (to be created off origin/main). **Blocked on the haptics
overhaul landing first** — toy seizure is a launch payload and is built on the v2 mixer
(`docs/HAPTICS_OVERHAUL_PLAN.md` in worktree `C:\Projects\ccp-wt-haptics`).
Status legend: [ ] todo, [x] done. Update this file as phases land.

## Locked decisions (owner-approved, survey 2026-08-03)
1. **Name: "Goon Game"** — recap/share card signs off "GG".
2. **Invite-only v1** — short invite code shared out-of-band (Discord/DM). Public queue is v2.
3. **Premium perk + one free battle per week for everyone** — weekly free pass is
   server-granted (client never decides), premium = `App.Patreon?.HasPremiumAccess`.
4. **Launch with toy seizure** — built on the haptics-overhaul mixer from day one; GG ships
   after `feat/haptics-overhaul` merges. No toyless interim release.
5. **Format: continuous endurance (10–15 min) → escalating synchronized sudden-death rounds**
   if nobody cracks. No round-based mode, no lobby format picker in v1.
6. **Stakes: cosmetic only** — win streaks, titles, recap badges. No currency ante. Honest
   mercies earn their own prestige track ("Graceful", "Iron Edge") — see §Scoring.
7. **Webcam optional** — no-cam players get periodic interaction checks instead of the
   attention multiplier; the lobby shows each player's mode.
8. **Ledger: private + opt-in share card** — match history visible only to the two players;
   recap offers an anonymized-by-default share-card image for Discord.
9. **No media through the server, ever** — payloads are *references* resolved against the
   receiver's own local library. Server does matchmaking/signaling + one ledger write.
10. Only Opus agents implement; Fable coordinates. (Same rule as haptics.)

## Platform-agnostic mandate (added 2026-08-03, mid-Wave-1)
Mobile (CCP-Mobile, Expo/React Native) and a webapp are planned follow-on clients.
The C# implementation is ONE client of the protocol, not the protocol itself:
- **The protocol is the product.** After Wave-1, Fable writes
  `docs/GOON_GAME_PROTOCOL.md`: a language-neutral spec of every message schema,
  the signaling + relay + ledger REST contracts (lifted from Phase A's
  documented JSON), the clock-sync procedure, the seed-XOR commit, the scoring
  formula/constants, and the deterministic round-generation algorithms
  (transcribable to TypeScript — Phase D must report its algorithms precisely).
  Any schema change in `GoonContracts.cs` must be mirrored there.
- **Capability negotiation** (`GoonCaps` in `HelloMsg`): every client advertises
  platform + supported payload kinds / draft elements / round kinds. Draft pool =
  intersection of both players' elements; senders may only send payload kinds the
  receiver advertised (others → `rejected_filtered`); round kinds from the rounds
  intersection, with ReactionDuel as the universal fallback every client MUST
  support. A phone with no BrainDrain overlay or LAN toy path simply advertises
  less — the match still works.
- **Wire enum codes are frozen integers** (explicit values in contracts; append
  only, never renumber). snake_case JSON, no .NET-specific serialization
  behaviors (no TypeNameHandling, no ticks-based dates — ms integers only).
- **WebRTC + REST are already cross-platform** (browsers native, RN via
  react-native-webrtc); the server never needs to know the client platform
  except as ledger metadata.
- WPF-isms stay INSIDE this client (dispatcher marshaling, DispatcherTimer,
  XAML) — that's fine; they must simply never leak into a wire schema or into
  protocol-visible behavior.

## Non-negotiable safety rails
- **Esc/panic always works** and maps to Mercy (graceful concede + full local stop via the
  existing `StopAllRemoteEffects` fan-out shape). GG must NEVER touch `PanicKeyEnabled`,
  `StrictLockEnabled`, or any lockdown/tray verb — those Remote Control commands are
  **banned from the GG payload set**.
- **Receiver-side content resolution = receiver-side filtering.** A payload names a
  category/tags + duration; the receiving client picks the concrete asset from its OWN
  library, so the receiver's blocklists/level gates apply by construction. Never ship a
  payload type that carries raw media or arbitrary URLs. Opponent-supplied text (lock-card
  phrases, subliminal lines) goes through the same cap/strip path as
  `trigger_custom_subliminal` (≤200 chars, tags stripped).
- **Toy intensity is capped by the receiver's mixer** — seize payloads post into the haptics
  v2 mixer, so `MasterCap`/`GlobalIntensity` and per-device trims apply unconditionally.
  The lobby consent sheet can only lower the cap for the match, never raise it.
- **Payload rate limit** enforced on the RECEIVING client (default: 1 offensive payload per
  30 s, burst 2), regardless of what the wire delivers.
- **Consent lobby**: both players see and confirm the same sheet (draft loadouts, toy cap,
  payload rate, webcam modes, tag blocklists ACK'd as "applied receiver-side") before the
  countdown can start. Either player backing out cancels cleanly.

## Architecture

```
 PLAYER A                         PROXY (codebambi-proxy.vercel.app)              PLAYER B
 GoonMatchService ── create ────▶ /v2/goon/invite  (mint code, store offer) ◀─── join ── GoonMatchService
        │        ◀── signaling poll (SDP/ICE exchange, ~2s cadence, match setup ONLY) ──▶ │
        │                                                                                 │
        ├────────────── WebRTC DataChannel (SIPSorcery), DTLS, P2P ──────────────────────┤
        │   clock-sync pings · state ticks (2–5s) · payload envelopes · round seeds       │
        │   reaction deltas · emotes · mercy/result handshake                             │
        │                                                                                 │
        └── end of match: ONE ledger write ──▶ /v2/goon/ledger ◀── confirming write ──────┘

 Fallback (P2P fails after ~10s ICE): proxy relay — /v2/goon/relay long-poll, 2s cadence.
 Sync design (fire-at-timestamp, ≥1s schedule buffer) is latency-tolerant, so relay mode
 degrades gracefully; only reaction-duel *stimulus* stays fair because deltas are local.
```

Client layout (all new code under `Services/GoonGame/`):

```
Services/GoonGame/
  GoonContracts.cs        — message schemas, enums, consts (Fable-authored; DO NOT redesign)
  GoonTransport.cs        — SIPSorcery peer connection + data channel, signaling client,
                            relay fallback, reconnect, MatchClock (NTP-style sync)
  GoonMatchService.cs     — App.GoonGame; match state machine:
                            Idle → Lobby → Consent → Draft → Countdown → Live →
                            SuddenDeath → Recap → Idle
  GoonPayloadExecutor.cs  — incoming payload → local service fan-out (Remote Control
                            dispatch-table shape), receiver-side resolve + rate limit
  GoonScoring.cs          — score ticks, attention/interaction multiplier, charge economy
  GoonSuddenDeath.cs      — synchronized round harness (shared seed + shared clock)
  GoonLedger.cs           — local match history store + share-card renderer + /ledger write
Views/Tabs/GoonGameTabView.xaml   — lobby/invite UI, live HUD is an overlay window
Windows/GoonRecapWindow.xaml      — recap + GG sign-off + share card
```

### Transport decisions
- **WebRTC via SIPSorcery** (pure .NET, MIT). Data channel only — no media tracks, no
  mic/cam ever crosses the wire (webcam stays local; only derived attention numbers ship).
  **Verify at coding time**: SIPSorcery data-channel maturity on .NET 8 + Windows 11
  (message size limits, DTLS handshake time). If it disappoints, fall back to the relay
  path as primary for v1 — the game design survives (see fire-at-timestamp).
- **STUN**: public Google/Cloudflare STUN for ICE. **No TURN server** in v1 (that would be
  media-adjacent infra we don't want to run); NAT-blocked pairs use the proxy relay.
- **Signaling** rides the existing proxy with `X-Auth-Token` auth (same header discipline as
  `RemoteControlService.AuthPostAsync`). Invite code is short (6 chars, crockford32); the
  join secret travels in the URL **hash fragment** if we ever render a link (PIN precedent,
  Remote primer §10.6).
- **MatchClock**: on channel open, 10 ping/pong rounds → median offset + RTT; re-sync every
  30 s. Everything synchronized fires at `matchClock` timestamps ≥1 s in the future —
  NEVER on message arrival. Countdown UI comes free.
- **Shared seeds**: each synchronized round carries a 64-bit seed; both clients generate
  identical round content from it (same bubble spawns, same lock card, same flash order).
  Seed = XOR of one random contribution per player, exchanged at round schedule time
  (neither side can pick the round).
- **Reaction fairness**: stimulus render time and input time are stamped on the LOCAL
  monotonic clock (`Stopwatch`); only the delta (ms) is exchanged. Network latency never
  touches the measurement. Deltas < 100 ms are flagged suspect (scored but badged).

### Payload model
A payload is a small JSON envelope, never media:

```json
{ "t": "payload", "id": "p-17", "kind": "video|flash_burst|subliminal_storm|bubble_swarm|lock_card|toy_pattern|braindrain",
  "fireAt": 812345, "durationMs": 45000,
  "args": { "tags": ["…"], "text": "…", "voice": true, "pattern": "wave", "intensity": 0.6 } }
```

Execution lands in the same service entry points as the Remote Control dispatch table
(`RemoteControlService.ExecuteCommand`, primer §4b) — `App.Flash`, `App.Video` (attention
checks ON), `App.Subliminal`, `App.Bubbles`, `App.LockCard` (voice with typed fallback),
`App.Haptics` (v2 `PlayPatternAsync`, role-routed), `App.Overlay` BrainDrain. GG does NOT
reuse `RemoteControlService` itself (different transport, different trust model, and RC's
engine-takeover semantics don't apply) — it reuses the *shape*.

**Banned from GG**: panic/strict-lock/tray/session-start verbs, `play_hypnotube` (URL =
media-adjacent + WebView2), wallpaper, autonomy, mind-wipe start (audio pool too
user-specific — revisit v2).

### State tick (every 2–5 s, P2P)
`{ t:"tick", score, attentionPct, mode:"cam|nocam", activeEffects:[…], toy:true, closeness:0-3|null, chargeMeter }`
— rendered as the opponent status panel: their avatar as spectator/commentator, current
effect + time left, attention %, self-reported closeness dial (bluffable on purpose).
Missing ticks > 15 s → "connection wobbly" UI; > 60 s → abandon flow (recorded as such,
never auto-declared a mercy).

## Match flow

1. **Invite**: host creates code → shares out-of-band. Joiner enters code. Both must be
   logged in (`UnifiedUserId`); server checks premium OR burns the weekly free pass at
   invite-create/join time (server-side counter, resets Monday 00:00 UTC).
2. **Consent lobby** (§safety rails): webcam mode per player, toy cap, rate limits, draft.
3. **Draft**: each player picks **3 elements from a rotating pool of ~8** — the draft
   defines what YOU endure (your session mix), each element carrying a risk multiplier.
   Both drafts are visible before confirm. Pool v1: Flashes, Videos, Subliminals, Bubbles,
   Lock Cards, Toy Patterns, BrainDrain, Bouncing Text.
4. **Countdown → Live (12 min default)**: drafted elements run as a session ramp
   (SessionEngine-style pacing, but driven by GoonMatchService — deterministic from the
   match seed). Score accrues per tick survived × draft risk × attention/interaction
   multiplier. Charges earn from attention streaks, minigame events, payloads survived;
   spend them to fire payload envelopes at the opponent.
5. **Mercy** at any time: dignified concede — match ends, recap celebrates survival time,
   Graceful prestige track credited. Esc ladder = Mercy.
6. **Sudden death** (timer expires, no mercy): escalating synchronized rounds until someone
   cracks or loses 3 rounds net — quick-draw lock card → staring contest (cam pairs) or
   reaction-check duel (mixed/no-cam) → bubble race (shared seed) → repeat harder.
7. **Recap**: two-way payload log, attention graph, charges spent, titles earned, winner,
   avatars say "GG". One ledger write; opt-in share card (anonymized by default).

## Scoring & charges (initial tuning — expect play-test iteration)
- Base: 1 pt/s survived × (1 + 0.15 × draft risk tier) × attention multiplier
  (cam: 0.5–1.5 from rolling attention %; no-cam: 1.0 flat, interaction checks failed
  → 0.6 for 60 s).
- Charge meter: +1 per 90 s clean (no failed checks), +1 per payload fully endured,
  +1 per sudden-death/mini event won. Cap 3 held.
- Payload costs: 1 = flash burst / subliminal storm / bubble swarm · 2 = mandatory video /
  lock card / toy pattern · 3 = BrainDrain combo (one "heavy" per match).
- **The bubble swarm is no longer throwable (2026-08-04).** Bubbles became the always-on
  baseline field (t=0 → end, both players, a locked tile in the agreement), so a bubble
  payload threw more of what was already running. `ui/arsenal.js` has no bubble slot,
  `ui/drops.js` can never roll one and the practice bot never sends one — the arsenal is
  7 payload slots + emote, keys 1-7. The WIRE is unchanged: `GoonPayloadKind.BubbleSwarm`
  (2) stays a frozen contract int at cost 1, stays in our advertised caps, and an inbound
  swarm from any other client still renders through `exec/bubbles.js` as before.

### Risk tiers are ENGINE-INTERNAL (2026-08-04)
The 0–3 per-element risk tier and the 0–7 match sum above are still exactly what
`GoonScoring` / `core/scoring.js` multiply the per-second score by, and they are still
C#-parity — nothing about the numbers moved. What changed is that **no player ever sees
them again**: the draft's three seven-segment risk meters, the per-tile risk pips, the
"Match risk 4 / 7" label, the duplicated risk table in `ui/strings.js` and the HUD's
"×1.30 risk" readout are all gone (owner: the tier was incomprehensible). What survives on
screen is the *consequence* — one honest "you both score ×1.60" in the draft footer and
"×1.60 score" in the HUD. Do not re-surface the tier; do not re-copy it into `ELEMENTS`.

### Item drops are paced by HEAT, not a flat roll (2026-08-04)
Popping a bubble used to be a flat `12% × worth` coin flip per pop — invisible, memoryless
and unreadable. It now banks **heat** (`ui/drops.js` `DROP_TUNING`), and the drop chance
ramps with the gauge: `CHANCE_FLOOR` 0.02 at empty to `CHANCE_PEAK` 0.60 at full, along a
`fill^3.4` curve, with a landed drop spending `HEAT_DROP_COST` back. The rail-side gauge
(`.gg-heat`, HUD right column) is the visible half of the feature.

The pacing is **conserved by construction**: heat in must equal heat out, so drops-per-pop
settles at `HEAT_PER_WORTH / HEAT_DROP_COST × worth` = `7.5 / 58` ≈ **12.9%/worth**
regardless of the curve's shape (decay and the ceiling shave it to a measured 12.0–13.1%
for a plain bubble, ~28% for an effect bubble). So the curve only buys feel; that ratio
alone is the economy, and `selftest-hud` pins it against the old flat 12%.
- Titles (cosmetic stakes): win streaks; "Graceful" (mercy with ≥ 8 min survived),
  "Iron Edge" (12+ min), "Stone Wall" (win taking 5+ payloads), "Untouchable" (win, zero
  charges spent), "GG" (first match played).
- XP: payload-triggered CCP effects grant XP through their normal pipelines automatically;
  GG adds only a flat match-complete bonus via `ProgressionService` (no parallel economy).
  Optional v2: `QuestCategory` for GG.

## Server surface (private repo `CC-Labs-llc/CCP-Server` — separate task there)
- `POST /v2/goon/invite` (mint code; premium-or-weekly-pass check), `POST /v2/goon/join`,
  `POST /v2/goon/signal` (SDP/ICE mailbox, short-poll, TTL ~5 min),
  `POST /v2/goon/relay` (fallback message mailbox, long-poll),
  `POST /v2/goon/ledger` (both clients write; server stores the pair, flags mismatches).
- Upstash: invite/signal keys with TTL; ledger under both `unified_id`s, private.
  Weekly-pass counter per `unified_id`. Reuse the existing 40/min per-user rate-limit
  discipline; relay mode gets its own budget.

## Phases & task list

### Phase 0 — Contracts (Fable, before any agent)
- [ ] `Services/GoonGame/GoonContracts.cs`: envelopes, enums, tick/payload/round schemas,
      banned-verb list, tuning consts. Agents extend via new members only, noted here.

### Phase A — Transport (Agent A)
- [x] SIPSorcery peer + data channel; signaling client against `/v2/goon/*`; ICE with
      public STUN; 10 s ICE timeout → relay fallback; reconnect-with-resume (5 s grace).
- [x] `MatchClock` (ping rounds, median offset, 30 s re-sync) + fire-at-timestamp scheduler.
- [x] Seed exchange (XOR commit) + deterministic PRNG helper.
- [x] Mock transport (loopback pair in-process) for offline dev/play-test of B–E.

**Decisions made in Phase A (integration + Phases B/C/E/F need these):**
- **SIPSorcery 10.0.13 VERDICT: VIABLE, WebRTC stays primary.** Verified by execution
  (in-process peers: channel open 0.97 s; full invite→join→SDP/ICE→open 2.6 s; 60 KB frame
  intact; srflx candidate from public STUN). Pure managed — publish unaffected. Trap fixed:
  answering side gets `ondatachannel` with the channel ALREADY open, so `onopen` never fires
  there — `HandleChannelOpen` is idempotent and called directly when `readyState == open`.
- **64-bit seeds ride the wire as decimal STRINGS** (read back from string or number) —
  bare JSON numbers above 2^53 silently lose low bits in `JSON.parse`, which would hand a
  web/RN player a different bubble layout. TS reads with `BigInt(s)`. APPROVED by Fable;
  this is the one non-obvious line for the protocol doc.
- Files: `GoonWire.cs` (t-discriminated serializer, 16 KB frame cap, never throws),
  `GoonRng.cs` (xoshiro256** + splitmix64, `NewSeedContribution` CSPRNG, `CombineSeeds`,
  `Derive(seed, purpose)`), `MatchClock.cs`, `GoonTransportBase.cs`, `GoonSignalingClient.cs`
  (+ `GoonFakeSignalingServer` = executable server contract), `GoonWebRtcTransport.cs`,
  `GoonRelayTransport.cs`, `GoonLoopbackTransport.cs`.
- **Scheduling rule**: never hand-roll `now + 1000` — use `MatchClock.SafeFireAt(extraMs)`
  (adds buffer + a full RTT). `ScheduleAt` returns null (refuses) for in-buffer instants or
  an unsynced clock — null = drop and log, NEVER "fire now". Dispose the handle to cancel.
- **Loopback presets**: `P2P()` 25 ms, `Relay()` 900 ms (features must survive this),
  `Instant()`; guest carries a deliberate 3517 ms clock skew so raw-local-timestamp bugs are
  impossible to miss; `SimulateOutage(ms)` for wobbly/abandon paths. `await ConnectAsync()`
  before scheduling anything.
- **P2P→relay handoff**: on IceFailed, build `GoonRelayTransport` with the SAME
  `GoonSignalingClient` + `AdoptRoom(code, token)` — no second invite/join, weekly pass not
  double-burned.
- **Forward-compat**: unknown integer enum codes deserialize to out-of-range values, not
  exceptions — Phase C treats unrecognized kinds as `rejected_filtered`.
- **Signaling contract highlights** (full spec in `GoonSignalingClient.cs` header, lift into
  protocol doc): invite returns `{code, token, role, pass: premium|weekly_free}` (402
  `no_pass` + `next_pass_utc`); `/signal` is post-and-drain in one call with opaque `data`
  (SIPSorcery `toJSON()` output is byte-identical to the browser's — no translation layer);
  relay needs own rate budget (~1 call/2 s, 20 min), 16 KB/frame, 128-frame ring, never
  inspects `data`. Bare 404 with no body = "not deployed yet" → warming-up UI, not "bad code".

### Phase B — Match engine (Agent B)
- [x] `GoonMatchService` state machine + consent lobby model + draft model + drafted-element
      session ramp (deterministic from match seed) + mercy/abandon/result handshake
      (result = both clients sign; mismatch recorded as disputed, cosmetics still granted
      to the uncontested parts).
- [x] `GoonScoring` + charge economy + attention/interaction multiplier (GazeFocusService
      when cam; interaction-check prompts when not).

**Decisions made in Phase B (integration + Phases C/D/E need these):**
- Files: `GoonMatchService.cs`, `GoonScoring.cs` (+ `GoonPayloadRateLimiter` token bucket),
  `GoonDraft.cs` (risk tiers + pacing profiles + `BuildRamp`), `GoonMatchTypes.cs`
  (cue/payload event args, `GoonOpponentState`, `GoonMatchResult`, `GoonCapabilities`,
  `GoonSuddenDeathContext`, `IGoonSuddenDeathRunner`).
- **Risk tiers v1**: 0 = Flashes, BouncingText · 1 = Subliminals, Bubbles · 2 = Videos,
  LockCards, ToyPatterns · 3 = BrainDrain. 3-pick draft sums 1..7 → ×1.15..×2.05.
- **Phase C surface**: subscribe `ElementStartRequested` / `ElementIntensityChanged` /
  `ElementStopRequested` (`GoonElementCueEventArgs {Element, Intensity, DurationMs,
  ElapsedMs}`) + `PayloadAccepted` (`{PayloadMsg, FireAtLocalMs}`), and call back
  `NotifyInboundPayloadFinished(id, endured)` (endured → +1 charge + `survived` receipt).
  Engine already truncates/strips payload text and clamps intensity/duration/fire-at
  defensively; Phase C still owns resolution, level gates, mixer cap. BrainDrain is behind
  the withheld-content flag — executor must degrade gracefully, not silently no-op.
- **Caps retrofit done**: draft pool = elements intersection; inbound non-advertised kind →
  `rejected_filtered`; outbound gated on peer's caps; `AllowedRoundKinds` (rounds
  intersection + forced ReactionDuel) rides `GoonSuddenDeathContext`; version/pool-size
  incompatibility → `FailLobby(reason)` + `LobbyFailed` event, clean reset. Empty peer caps
  = "everything we support" (v1-peer compat).
- **Message routing rule**: during SuddenDeath the SERVICE forwards RoundSchedule/
  RoundResult/Mercy to the runner via `HandleMessage` — the runner must NOT subscribe to
  the transport. No runner attached → Live expiry settles on score comparison.
- **Phase E surface**: `ReportAttention(pct)` (cam), `InteractionCheckDue` event +
  `ReportInteractionCheck(passed)` (no-cam, 90 s cadence), `LocalAttentionMode`/
  `LocalToyConnected` before hosting/joining, Esc ladder → `DeclareMercy()` (works in every
  phase, never touches panic/lockdown).
- **Integration (Fable)**: inject `seed => new GoonRng(seed)` at construction (service has a
  private splitmix64 fallback for standalone use); wire runner ← `GoonSuddenDeathRunner`.
- **Economy receipt gap**: cost-violating payloads currently report `rejected_rate` (the
  documented status set has no `rejected_cost`) — decide with the protocol doc whether to
  add the distinct status (leaning yes).
- **Abandon semantic (current)**: 60 s without ticks records the DISCONNECTING side as the
  loser (`Abandon`). Confirm vs. a no-result abandon during play-test (open question).

### Phase C — Payload executor (Agent C)
- [ ] `GoonPayloadExecutor`: envelope → service fan-out; receiver-side resolve (tags →
      own library), rate limiter, text cap/strip, haptics via v2 mixer only; stop-all on
      match end (RC `StopAllRemoteEffects` shape, minus RC-specific state).

### Phase D — Sudden death (Agent D)
- [x] Round harness on shared clock + seed; quick-draw lock card; staring contest
      (cam/cam) with reaction-check fallback; bubble race; escalation ladder + net-3 exit.

**Decisions made in Phase D (integration + Phase E need these):**
- Files: `GoonSuddenDeath.cs` (`GoonSuddenDeathRunner` implements Agent B's
  `IGoonSuddenDeathRunner` directly — no adapter; + pure antisymmetric `GoonRoundJudge`),
  `Rounds\GoonRoundModel.cs` (specs, input-feed interfaces `IGoonRoundInputs`
  {LockCard, Attention, Reaction, Bubbles}, `IGoonRoundPresenter`, fake feeds for tests),
  `Rounds\{QuickDraw,StaringContest,ReactionDuel,BubbleRace}Round.cs`.
- **Contract addition (the only one)**: `RoundResultMsg.progress` (int) — round-specific
  tally for judging when neither side "completed" (bubbles cleared / avg attention / typed
  mistakes / false-start flag).
- **Determinism**: round seed = XOR of per-round contributions (minted once, reused across
  schedule retries — bumping FireAt never changes the seed); fixed draw order per round is
  documented in the agent report → transcribe into the protocol doc. Ladder is pure:
  `KindFor(roundNo, modes)`, `DifficultyFor = 1 + (roundNo-1)/3`; guest validates the
  host's proposed kind, warns on mismatch, but follows (no deadlock). Host retries a late
  seed-half once with +2 s; missing half/result → `Aborted`, never a fabricated win.
- **Quick-draw phrases come from a fixed in-code pool of 16**, NOT the user's lock-card
  phrase pool (that's per-mod/per-user → two players would get different cards). Cards run
  `Strict=false` so Esc stays mapped to Mercy. `LockCardService` untouched — Phase E renders
  via `ShowLockCard(spec.Phrase, spec.Repeats, customStrict:false)` + `LockCardCompleted`.
- **Phase E wiring table** (render hook + input feed per round) is in the agent report:
  staring beats through Flash/compositor + webcam blink feed; reaction duel needs a GG HUD
  overlay (`ArmReactionDuel`/`FireReactionStimulus`, feints from difficulty 2, false start =
  round loss); bubble race spawns spec bubbles via BubbleService + pop-index callback; plus
  `ShowRoundIntro` countdown and `ShowRoundVerdict` (badge Suspect reactions).
- **Caps note (added post-mandate)**: round-kind selection must additionally respect the
  `GoonCaps.SupportedRounds` intersection — enforce in the host's ladder proposal at
  integration (guest already follows host).

### Phase E — UI (after A–D)
- [ ] Tab (invite/join, consent sheet, draft picker), live HUD overlay (own score, charge
      pips, opponent panel w/ avatar spectator), Mercy button (big, dignified, always
      hit-testable), recap window + share-card PNG render. App visual language (#252542
      cards, #FF69B4). Converters in Window.Resources; `DispatcherPriority.Normal`.
- [ ] Logo/branding assets (nano-banana batch `goon-game-logo`, in progress).

### Phase F — Server (separate repo, parallel with A)
- [ ] Endpoints + TTL keys + weekly pass + ledger. Deploy from `proxy/` dir ONLY.

### Phase G — Ship prep
- [ ] Gating: tab overlay for non-premium WITHOUT a weekly pass (server tells the client
      pass availability at invite time; gate is server-enforced, UI mirrors it).
- [ ] Loc keys → all 9 language files (strict JSON, `\n` escaped, no line-ending flips).
- [ ] Two-instance play-test: mock transport first, then real P2P on LAN + relay-forced
      run (automated UIA method — ABORT if an unowned instance is running).
- [ ] Patch notes (no em-dashes), primer `docs/primers/GOON_GAME_PRIMER.md`.

## Verification bar (every agent)
- `dotnet build` clean in the GG worktree.
- No settings resets; new settings nested + `[JsonProperty]` everywhere (haptics lesson).
- WPF traps: converters in Window.Resources; `IsLoaded`/`Template != null` before
  animations; no fire-and-forget without dispatcher-null + try/catch; screen-enum guard.
- Safety rails of this doc are asserted in code review for every phase — especially the
  banned-verb list and receiver-side rate limit.

## Open questions (decide before Phase B freeze)
- Exact live-phase length (12 min default — lobby-visible but fixed in v1?).
- Draft pool risk-tier values per element (needs a balance pass with real session ramps).
- Abandon semantics vs. disconnect grace (5 s resume window enough on flaky Wi-Fi?).
- Share-card content when exactly one player opts in (show only the opting player's side).
