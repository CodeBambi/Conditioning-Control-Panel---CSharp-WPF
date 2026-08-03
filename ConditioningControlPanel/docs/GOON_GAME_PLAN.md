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
- [ ] SIPSorcery peer + data channel; signaling client against `/v2/goon/*`; ICE with
      public STUN; 10 s ICE timeout → relay fallback; reconnect-with-resume (5 s grace).
- [ ] `MatchClock` (ping rounds, median offset, 30 s re-sync) + fire-at-timestamp scheduler.
- [ ] Seed exchange (XOR commit) + deterministic PRNG helper.
- [ ] Mock transport (loopback pair in-process) for offline dev/play-test of B–E.

### Phase B — Match engine (Agent B)
- [ ] `GoonMatchService` state machine + consent lobby model + draft model + drafted-element
      session ramp (deterministic from match seed) + mercy/abandon/result handshake
      (result = both clients sign; mismatch recorded as disputed, cosmetics still granted
      to the uncontested parts).
- [ ] `GoonScoring` + charge economy + attention/interaction multiplier (GazeFocusService
      when cam; interaction-check prompts when not).

### Phase C — Payload executor (Agent C)
- [ ] `GoonPayloadExecutor`: envelope → service fan-out; receiver-side resolve (tags →
      own library), rate limiter, text cap/strip, haptics via v2 mixer only; stop-all on
      match end (RC `StopAllRemoteEffects` shape, minus RC-specific state).

### Phase D — Sudden death (Agent D)
- [ ] Round harness on shared clock + seed; quick-draw lock card; staring contest
      (cam/cam) with reaction-check fallback; bubble race; escalation ladder + net-3 exit.

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
