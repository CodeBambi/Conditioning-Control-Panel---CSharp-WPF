# Goon Game Wire Protocol — v1 (2026-08-03)

Language-neutral spec for Goon Game clients (Windows/WPF today; Expo/React Native and web
planned). The C# binding is `Services/GoonGame/GoonContracts.cs` + `GoonWire.cs`; the
executable server contract is `GoonFakeSignalingServer` in `GoonSignalingClient.cs`. If an
implementation and this document disagree, one of them is a bug — fix and mirror.

Normative words: MUST / MUST NOT / SHOULD.

## 0. Principles
1. **No media on any wire, ever.** Payloads are references (kind + tags + params) resolved
   against the RECEIVER's own local library. No URLs. Mic/webcam data never leaves a device;
   only derived numbers (attention %, blink deltas) do.
2. **Receiver enforces everything.** Rate limits, charge costs, capability filtering, text
   caps, toy intensity caps are applied by the receiving client regardless of what arrives.
   The wire is untrusted.
3. **Nothing fires on message arrival.** All synchronized actions carry a shared-clock
   timestamp ≥ now + 1000 ms (senders SHOULD add a full RTT on top).
4. **Local measurement, exchanged deltas.** Reaction times are measured on a local monotonic
   clock; only the delta in ms crosses the wire. Deltas < 100 ms are scored but flagged
   `suspect`.
5. **Capability negotiation, not platform detection.** Clients advertise what they support;
   nobody may be sent what they didn't advertise.

## 1. Encoding
- JSON, snake_case keys. Times on the wire are **integer milliseconds** (no ISO dates, no
  .NET ticks) except server timestamps which are ISO-8601 UTC strings (e.g. `next_pass_utc`).
- Enums are **frozen integer codes** — append-only, never renumbered (see §9). An
  unrecognized code MUST be treated as unknown, not an error: payloads → reject with
  `rejected_filtered`; other messages → ignore field/message and log.
- **64-bit seeds are decimal STRINGS** on the wire (`"seed_contribution":"1234..."`), and
  readers MUST accept string or number. Rationale: JSON numbers above 2^53 silently lose
  low bits in `JSON.parse`, which would give a JS peer a different round layout. JS/TS reads
  with `BigInt(s)`.
- Every P2P frame is one JSON object with a `"t"` type discriminator and `"v"` protocol
  version (currently 1). Max frame size: **16 KB**. Unknown `"t"` → ignore + log.
- No polymorphic typing metadata (no `$type`), no C#-isms.

## 2. Server surface (signaling / relay / ledger)
Base: `https://codebambi-proxy.vercel.app`, all POST, auth = `X-Auth-Token` header +
`unified_id` in body + `X-Client-Version` / `User-Agent` headers. 40/min per-user budget;
429 body `{error:"rate_limited", cap, count, retry_after_seconds}`. Full field-level spec
with all error cases lives at the top of `GoonSignalingClient.cs` — summary:

| Endpoint | Purpose | Key semantics |
|---|---|---|
| `/v2/goon/invite` | host mints room | server checks premium OR burns weekly free pass (`pass:"premium"\|"weekly_free"`; 402 `no_pass` + `next_pass_utc`); crockford32 code, ~5 min TTL |
| `/v2/goon/join` | guest redeems code | 404 `unknown_code`, 409 `already_joined`; returns peer display name/version |
| `/v2/goon/signal` | SDP/ICE mailbox | post-and-drain in ONE call, ~2 s short-poll, setup only; `data` is opaque (browser-identical `toJSON()` blobs — JS does `JSON.parse` straight into `setRemoteDescription`/`addIceCandidate`); append-only list, exclusive `since` cursor, callers never receive their own messages |
| `/v2/goon/relay` | fallback transport | same shape; `data` = whole GoonMessage frame; `wait_ms` long-poll hint; own rate budget (~1 call/2 s per player, match-length TTL), 16 KB/frame, 128-frame ring; server MUST NOT inspect or persist `data` |
| `/v2/goon/ledger` | end-of-match write | both clients post their signed result; server stores the pair under both unified_ids, flags mismatches as disputed; private to the two players |

A bare 404 (no error body) on any route = "server not deployed" → clients show a
warming-up message, never "bad code".

## 3. Transport
- Primary: WebRTC **data channel**, ordered + reliable, one channel, no media tracks.
  ICE via public STUN (Google, Cloudflare). No TURN.
- ICE not complete within **10 s** → fall back to relay **adopting the same room**
  (same code + token; the invite/pass is never double-charged).
- Disconnect grace: **5 s** reconnect-with-resume before the wobbly/abandon flow.
- Known WebRTC portability note: some stacks surface the answering side's channel already
  open (`ondatachannel` fires with `readyState == "open"`); open-handling MUST be
  idempotent and MUST NOT wait for a further `onopen`.

## 4. Clock sync (MatchClock)
On channel open: **10** ping/pong rounds (`clock_ping {seq, sent_local_ms}` /
`clock_pong {seq, echo_sent_local_ms, pong_local_ms}`), local clocks are monotonic (never
wall time). Offset = median of per-round estimates; RTT = median round-trip. Re-sync every
**30 s**. Match time ("match ms") is the host-anchored shared axis; all `*_match_ms`
fields use it. Schedulers MUST refuse an instant closer than 1000 ms (drop + log — never
"fire now") and senders SHOULD schedule at `now + 1000 + RTT`.

## 5. Seeds & deterministic RNG
- Contributions: each side mints a cryptographically random ulong; combined seed =
  **XOR** of both. Exchanged simultaneously (no commit-reveal in v1). Per-round
  contributions are minted ONCE per round and reused across schedule retries (re-proposing
  a FireAt never changes the seed).
- RNG: **xoshiro256\*\*** seeded via **splitmix64** expansion of the 64-bit seed. Streams:
  `Derive(seed, purpose)` hashes a purpose string into a sub-seed (see `GoonRng.cs` for the
  exact construction — transcribe bit-for-bit; determinism across languages depends on it).
- All round content derives from the round seed with a FIXED draw order (§8). Draws are
  `NextDouble` (53-bit), `NextInt(min, maxExcl)`, `NextULong` — semantics per `GoonRng.cs`.

## 6. Session flow & message catalog
Phases: Lobby → Consent → Draft → Countdown → Live → SuddenDeath (optional) → Recap.

| `t` | Direction | Purpose / rules |
|---|---|---|
| `hello` | both, once | display_name, attention_mode (0=cam, 1=nocam), toy_connected, app_version, `caps` (§7) |
| `consent` | both | identical sheet both confirm: live_duration_sec (default 720), toy_cap (≤ receiver's own cap, can only lower), payload_min_gap_ms; `confirmed:true` from BOTH advances; either side leaving cancels cleanly |
| `draft` | both | exactly 3 elements from the caps-intersected pool; both visible; `locked:true` freezes |
| `match_start` | host→guest (guest echoes contribution) | start_match_ms (≥ now + buffer + countdown), seed_contribution (string) |
| `tick` | both, every 3000 ms | at_match_ms, score, attention_pct, attention_mode, active_effects[], toy, closeness (0–3 or null; self-reported, bluffable BY DESIGN), charges. No tick 15 s → "wobbly" UI; 60 s → abandon flow |
| `payload` | attacker→victim | id, kind, fire_at_match_ms, duration_ms, tags[], text (≤200 chars, stripped receiver-side), voice, pattern, intensity (0..1 pre-cap). Receiver validates: kind ∈ own advertised caps, rate (1 / 30 s, burst 2), charge cost (§10), heavy-once rule, then resolves against own library |
| `payload_receipt` | victim→attacker | id + status: `accepted` \| `rejected_rate` \| `rejected_filtered` \| `completed` \| `survived` (fully endured → attacker sees it, victim gains +1 charge). v1.1 candidate: distinct `rejected_cost` |
| `round` | host→guest (guest echoes contribution) | round_no, kind, fire_at_match_ms, seed_contribution (string), difficulty. Host MUST propose kinds from the caps intersection (§8); guest validates, warns on mismatch, follows (no deadlock). Late guest seed → host re-proposes once at +2 s; missing half/result → abort, never a fabricated win |
| `round_result` | both | round_no, completed, elapsed_ms, reaction_ms (nullable), suspect, progress (round-specific tally: bubbles cleared / avg attention / mistakes / false-start) |
| `mercy` | either | at_match_ms; graceful end. The local panic/Esc path maps here; a client MUST always honor its own user's mercy immediately |
| `emote` | either | text ≤ 60 chars, icon ≤ 8 chars |
| `result` | both | end_reason (§9), winner_is_host (null = draw), both scores, survived_ms, `agree` countersign. Both sides also write this to `/v2/goon/ledger`. Disagreement = disputed; uncontested cosmetics still grant |

## 7. Capability negotiation (`caps` in hello)
`{platform: "windows"|"android"|"ios"|"web", payloads:[int], elements:[int], rounds:[int], min_v:int}`
- Draft pool offered to BOTH players = intersection of the two `elements` sets.
- A sender MUST only send payload kinds present in the RECEIVER's `payloads`; receivers
  drop everything else with `rejected_filtered`.
- Sudden-death kinds come from the `rounds` intersection; **ReactionDuel (2) is the
  universal fallback every client MUST support**.
- Empty/absent caps = "supports everything the sender supports" (v1 compat).
- Version gate: peer `min_v` > own version, peer `v` < own `min_v`, or elements
  intersection < 3 → fail the lobby with a reason; never start and desync.

## 8. Sudden death
Ladder (pure function both sides compute identically — no extra message):
```
step       = (max(1, round_no) - 1) % 3
difficulty = 1 + (max(1, round_no) - 1) / 3     // integer division
base(step) = 0: QuickDrawLockCard
             1: both cam ? StaringContest : ReactionDuel
             2: BubbleRace
kind       = allowed(base) ? base : ReactionDuel
allowed(k) = caps-intersection contains k   (empty/absent intersection → only ReactionDuel)
```
Net score: win +1 / loss −1; reaching **−3** net = match loss. False start (input before the
real stimulus, feints included) = automatic round loss. Mercy remains available mid-round.

Deterministic generation per round (draw ORDER is normative; constants in
`Rounds/*.cs` — transcribe exactly):
- **QuickDraw (0)**: phrase = index draw into the FIXED 16-phrase in-code pool (shared by
  all clients; NOT the user's phrase pool); repeats = clamp(difficulty,1,3), no draw.
- **StaringContest (1)**: per beat, in order: offset jitter, duration, intensity, normX,
  normY, scale. Duration 20 s + 5 s/level (cap 45); beats 12 + 6/level (cap 48).
- **ReactionDuel (2)**: real-delay draw 2000–6000 ms FIRST, then one draw per feint
  (feints exist from difficulty 2, max 3, each ≥ 400 ms before the real stimulus).
- **BubbleRace (3)**: per bubble, in order: normX, normY, scale, spawn offset, drift angle,
  drift speed. Count 6 + 3·(difficulty−1) cap 18; 30 s timeout, most-cleared wins
  (`progress` = cleared count).

## 9. Frozen enum codes
- GoonElement: 0 Flashes · 1 Videos · 2 Subliminals · 3 Bubbles · 4 LockCards ·
  5 ToyPatterns · 6 BrainDrain · 7 BouncingText · 8 Spiral
- GoonPayloadKind: 0 FlashBurst · 1 SubliminalStorm · 2 BubbleSwarm · 3 Video ·
  4 LockCard · 5 ToyPattern · 6 BrainDrain · 7 Spiral
- GoonRoundKind: 0 QuickDrawLockCard · 1 StaringContest · 2 ReactionDuel · 3 BubbleRace
- GoonEndReason: 0 Mercy · 1 SuddenDeathLoss · 2 Abandon · 3 Draw
- GoonAttentionMode: 0 Cam · 1 NoCam

## 10. Economy & scoring constants (v1)
Charge costs: FlashBurst/SubliminalStorm/BubbleSwarm 1 · Video/LockCard/ToyPattern/Spiral 2 ·
BrainDrain 3 (once per match; Spiral is sustained but repeatable). Charge cap 3; +1 per
clean 90 s, +1 per payload fully endured, +1 per event won. Score: 1 pt/s × (1 + 0.15 ×
draft risk sum) × attention multiplier (cam 0.5–1.5 rolling; no-cam flat 1.0, failed check
→ 0.6 for 60 s). Risk tiers: Flashes/BouncingText 0 · Subliminals/Bubbles 1 ·
Videos/LockCards/ToyPatterns/Spiral 2 · BrainDrain 3 (draft sum clamped to 7). Ramp shapes:
BrainDrain enters at 0.35 of the live phase and ramps 0.25→0.75; Spiral enters at 0.25 and
ramps 0.20→0.65. Payload rate: 1 / 30 s, burst 2 (receiver-enforced token bucket).

## 11. Safety invariants (every client, every platform)
- The local user's panic/escape gesture maps to Mercy and MUST work in every phase.
- No message may alter the receiver's panic, lockdown, tray, or session settings — no such
  message exists in this protocol, and none may be added.
- Toy output goes through the receiving client's own intensity cap; the consent sheet can
  only lower it for a match.
- Opponent text is length-capped and sanitized ON THE RECEIVER before display.
- Content resolution is receiver-side only; a client MUST ignore any future field that
  attempts to name a concrete asset or URL.

## Changelog
- v1 (2026-08-03): initial spec, matches Wave-1 (`d0402126`). Open v1.1 candidates:
  `rejected_cost` receipt status; commit-reveal for per-round seeds; abandon = loser vs
  no-result (play-test call).
