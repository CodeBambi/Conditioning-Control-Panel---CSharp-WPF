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

**Entitlement (2026-08-04).** Exactly one route is gated: **hosting is tier 2**
(`computeEffectiveTier(user) >= 2`, into which the whitelist folds as permanent tier 2), and
`/invite` answers **403 `no_host_access`** below it. **Joining is free for everyone** — no tier
check at all, and no account required (see *Anonymous guests* below). The launch model (tier ≥ 1
free play plus a weekly free pass for tier 0) and its **402 `no_pass`** are **retired**; clients
still parse `no_pass` so an old server keeps producing a sentence, but nothing sends it. The
in-app host answers the same question locally as `caps.canHost` on its `init` frame, so the
desktop title screen can dim Host before the round-trip; a standalone web page cannot know the
answer before asking and leaves Host live, relying on the 403.

| Endpoint | Purpose | Key semantics |
|---|---|---|
| `/v2/goon/invite` | host mints room | **TIER 2 ONLY** — 403 `no_host_access` below it; crockford32 code, ~5 min TTL; `pass:"premium"` is legacy-shaped and charges nothing (reaching a 200 at all IS the entitlement answer); answers `media_send` — the caller's premium SEND verdict for media transfer (tier≥1, a rung BELOW the host bar). The standalone web client defaults sending OFF against a real server and adopts this answer; the C# host computes the same verdict locally, so to it the field is advisory |
| `/v2/goon/join` | guest redeems code | **FREE — no tier check, and no account required**; 404 `unknown_code`; 409 `already_joined` (a DIFFERENT uid holds the seat) or `self_join` (the code is your own room — one account cannot hold both seats); the SAME uid **reclaims** its seat instead of being refused: fresh token, `rejoin:true`, stale SDP dropped; **whitelisted** accounts are the one exception to `self_join` and get a **self-duel** instead (`self_duel:true`) — see below; `pass` survives as an ADVISORY LABEL only, one of `"free" \| "rejoin" \| "self_duel"`; a body with **no `unified_id` field at all** mints an anonymous guest and answers `guest_id` (see below); returns peer display name/version + the caller's own `media_send` verdict (see `/invite`; always `false` for a guest) |
| `/v2/goon/leave` | release the seat | best-effort, fire-and-forget, `{code, token, role}`; a guest hands the seat back and the ROOM stays up, a host folds the room outright; 409 `match_started` once the ledger has a row (releasing the seat invalidates the token that row is written with). Optional by design — the room TTL is still the backstop, and a client treats any failure as "never mind" |
| `/v2/goon/signal` | SDP/ICE mailbox | post-and-drain in ONE call, ~2 s short-poll, setup only; `data` is opaque (browser-identical `toJSON()` blobs — JS does `JSON.parse` straight into `setRemoteDescription`/`addIceCandidate`); append-only list, exclusive `since` cursor, callers never receive their own messages |
| `/v2/goon/relay` | fallback transport | same shape; `data` = whole GoonMessage frame; `wait_ms` long-poll hint; own rate budget (~1 call/2 s per player, match-length TTL), 16 KB/frame, 128-frame ring; server MUST NOT inspect or persist `data` |
| `/v2/goon/ledger` | end-of-match write | both clients post their signed result; server stores the pair under both unified_ids, flags mismatches as disputed; private to the two players |

A bare 404 (no error body) on any route = "server not deployed" → clients show a
warming-up message, never "bad code".

### Anonymous guests (free join, 2026-08-04)

Joining does not require an account. A `/join` body with **no `unified_id` field at all** — not an
empty one, which is a 400 — is an invite-link click from somebody who has never seen the app. The
server mints them a guest identity, **`g_` + 8 random bytes hex** (`^g_[a-f0-9]{16}$`), and returns
it as **`guest_id`** on that one response.

The client MUST then present that value as `unified_id` on **every** subsequent room-scoped call —
`/signal`, `/relay`, `/leave`, `/ledger`, `/report`, `/blocked`, `/peercard` — alongside the room
`token` it also received. **No `X-Auth-Token` header** goes with any of them: a guest has no
account token, and the per-seat room token is the whole of its authority (the uid check is the
secondary lock, exactly as it is for accounts).

- Unified ids are `u_[a-z0-9]{8,24}`, so a `g_` id can never collide with — or be spelled by — a
  real account, even though it travels in the same field.
- The `g_` id **doubles as the seat-reclaim key**, which is why it is random and disclosed only
  once. A guest that reloads re-presents its stored id with the **same room code** and reclaims its
  seat through the normal rejoin path (`pass:"rejoin"`, fresh token). Clients persist it with the
  room code and re-present it **only for that room** — carrying one across rooms would turn a
  throwaway seat into a durable handle on somebody who never signed up for one.
- A guest has **no user record**: `media_send` is always `false` for it, `/peercard` shares
  nothing, and the ledger writes no `goon:ledger:g_*` history.
- Guests never host: `/invite` is tier-2 gated and has no anonymous branch.

Implemented on the server and in the **web** client (`net/signaling.js` `anonymous`, persisted
through `bridge.savePrefs`). The C# client never takes this path — the desktop app always has an
account — and documents it without implementing it.

### Self-duel (whitelist only, 2026-08-04)

One account may hold **both** seats of its own room, but only if it is **whitelisted**
(`patreon_is_whitelisted` / `substar_is_whitelisted` on the user record — the same
fields `computeEffectiveTier` folds into its permanent-tier-2 override). It is a
testing affordance: the owner play-tests with one account on two devices (the desktop
app hosts, the phone joins the standalone web build through a link that passes the
same `uid`), and `self_join` otherwise refuses the whole run. **Tier is deliberately
not enough** — a paying tier-2 patron is still refused, because a self-duel would
otherwise be a ledger-farming and self-report surface.

Everyone else's `self_join` 409 is unchanged, byte for byte.

The guest seat of a self-duel is stored under a **shadow identity**, `<uid>#self`.
Unified ids are `u_[a-z0-9]{8,24}`, so a shadow can never collide with — or be spelled
by — a real caller, and every uid-keyed structure keeps seeing two identities:

- `goon:pair:<code>` records `guest_uid: "<uid>#self"`, so a self-report accuses an
  account that does not exist instead of the reporter;
- the ledger **skips** shadow seats entirely — one recap row per match, under the real
  uid, never two;
- `goon:lookup:<uid>` stays the host's room-by-uid mapping; the shadow writes none;
- `/peercard` for a shadow peer answers `not_shared` (`user:<uid>#self` cannot exist);
- room auth resolves the shadow back to its base uid, so the device holding the seat
  authenticates as the real account on `/signal`, `/relay`, `/ledger` and `/leave`;
- a self-duel guest **reclaims the shadow seat** on rejoin (never a second shadow), and
  `/leave` frees it like any other seat.

A self-duel is labelled `pass:"self_duel"`. Nothing is charged for it, or for any join — whitelist
is permanent tier 2 and the account already cleared the only gate there is, at `/invite`.

> **Seat identity, `/leave` and self-duel (2026-08-04)** are implemented on the server
> and in the **web** client (`Resources/web/goon/net/`). The C# client
> (`Services/GoonGame/GoonSignalingClient.cs` and its `GoonFakeSignalingServer`) has
> not been taught any of the three: it never sends `/leave`, its fake server still
> models the seat as a bare `joined` flag, and it neither knows a shadow id nor reads
> `self_duel`. None of it is a break — the additions are backward-compatible and the
> desktop app is the HOST in the owner's setup, which needs no client-side change —
> but the C# fake no longer models everything the real server does, so port it before
> trusting it for a rejoin or self-duel case. The **host gate** and **free join** (v1.5)
> ARE ported to both: the C# client maps 403 `no_host_access` and its fake refuses a
> non-tier-2 `/invite` (`HostGate` + `SetLabAccess`, off by default) and charges nothing
> for a join. **Anonymous guests are web-only** by design — the desktop app always has an
> account and can never take that path.

## 3. Transport
- Primary: WebRTC **data channel**, ordered + reliable, one channel, no media tracks.
  ICE via public STUN (Google, Cloudflare). No TURN.
- ICE not complete within **10 s** → fall back to relay **adopting the same room**
  (same code + token; the room is never re-minted and `/join` is never re-issued).
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
| `media_prep` | either, pre-live | `preparing:bool` — "I am still assembling a library". A **presence hint, not a term**: it clears no confirmation, gates no phase and is never folded into the consent fingerprint. Sent when a client interposes its first-run media step (a link joiner with an empty deck) and again when they finish, so the other side's lobby can say "they joined — picking their media…" instead of "waiting for them". **Append-only on v1: absent = not preparing**, so a client that never heard of it reads as ready, and a client that RECEIVES it unknown drops the frame as an unknown `t` (§1) and stays up. Edge-triggered — an unchanged value MUST NOT be re-sent. No capability bit and no version bump: nothing waits on it, so there is nothing to negotiate |
| `result` | both | end_reason (§9), winner_is_host (null = draw), both scores, survived_ms, `agree` countersign. Both sides also write this to `/v2/goon/ledger`. Disagreement = disputed; uncontested cosmetics still grant |

### Invite links (client-side only, 2026-08-04)

The room code is also expressible as a URL, so a host can hand a room over with a
paste instead of six characters read down a phone line. **This adds nothing to the
server surface** — the link is redeemed through the same `/v2/goon/join` as a typed
code, and a client that ignores the parameter is simply a client that asks the
player to type.

```
<goon client page>?join=<code>          e.g.  https://cclabs.app/goon-beta/?join=ABC123
```

- **The base is the standalone client, never the app's page.** A standalone page
  builds it from `location.origin + location.pathname`; a HOSTED page (WebView2
  serves the client from the virtual host `https://ccp.game/goon/index.html`,
  which resolves on exactly one machine) MUST substitute the public web
  deployment instead. The web client keeps that address in one constant,
  `GOON_PUBLIC_URL` in `Resources/web/goon/ui/inviteLink.js`.
- **Reading is tolerant, exactly as far as `normalizeCode` is** (§2): trim,
  uppercase, strip dashes and spaces, clamp to six. Crockford's `I`/`L`→`1` and
  `O`→`0` are deliberately NOT folded — the server mints the alphabet.
- **A consumed link MUST be removed from the address bar** (`history.replaceState`,
  keeping every other parameter — `server`, `token`, `uid`, `debug`). A room TTL is
  ~5 min, and a link left in place is re-joined by every refresh, every back
  button and every home-screen pin, forever.
- **Failure lands on the join screen, not on the menu**: the code stays prefilled
  and the reason (`unknown_code`, `expired`, …) is shown, because "the room is
  gone, ask for a fresh one" is the one thing a bounced-to-menu link joiner
  cannot work out alone.

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
  attempts to name a concrete asset or URL. **The single sanctioned exception is the
  `xfer:<sha256>` tag namespace (§12): it names content the receiver ALREADY accepted over
  the mutually-consented media channel — never a URL, never a path, never anything the
  receiver has not verified byte-for-byte. Unknown tag namespaces are still ignored.**

## 12. Media channel (v1.1 — sender-media transfer)

Optional second data channel carrying the SENDER's own media so the receiver can display it.
Everything here is additive: a client without it interoperates unchanged.

- **Channel**: label `goon-media`, `negotiated: true, id: 1, ordered: true`, created by BOTH
  roles immediately after the RTCPeerConnection is constructed (no renegotiation; no
  `ondatachannel` involvement). A client that cannot create it simply has no media channel.
  In-band `ondatachannel` with any label other than `goon` MUST be closed and ignored.
- **Relay exclusion (hard rule)**: media frames exist ONLY on this P2P channel. They MUST
  never ride the game channel, the relay ring, or the signaling mailbox. On relay fallback
  the feature is silently absent; payloads fall back to receiver-library resolution.
- **Gating (all four AND'd)**: `caps.transfer` in hello (build speaks this protocol and will
  accept offers — a version discriminator, not an entitlement) · `consent.media_transfer`
  per-side declaration on the consent sheet (means "the sender opts in"; it is NOT part of
  the sheet-equality fingerprint — adding it there wedges lobbies against older peers) ·
  the local host's send entitlement (platform concern, not wire) · a live P2P channel.
- **Frame taxonomy**: a string frame is ONE control JSON `{"t":"xfer_*","v":1,...}`; a binary
  frame is ONE chunk: 8-byte little-endian header (`uint32 tid`, `uint32 offset`) + up to
  16376 payload bytes (total ≤ 16384). tid spaces are disjoint by role: host mints odd,
  guest even.
- **Verbs**: `xfer_hello {proto, max_artifact_bytes, max_session_bytes, max_concurrent,
  accepts[]}` (each side, on every channel open; no hello within 3 s → dormant for the match)
  · `xfer_offer {tid, sha256, bytes, mime, kind, dur_ms?, w?, h?}` · `xfer_accept {tid,
  from_offset}` · `xfer_decline {tid, why: have|blocked|too_big|bad_mime|busy|quota|off}` ·
  `xfer_ack {tid, offset}` (every 256 KiB) · `xfer_end {tid, sha256}` · `xfer_done {tid,
  sha256}` · `xfer_fail {tid, why: hash_mismatch|magic|too_big|store_full|blocked|io}` ·
  `xfer_cancel {tid, why: peer_gone|match_over|superseded|timeout|user|stray}`.
- **Receiver offer gate, in order, before any byte flows**: feature off → mime/kind allowlist
  (`video/mp4`, `video/webm`, `image/png`, `image/jpeg`, `image/gif`, `image/webp`) → sha
  shape → size (64 MiB artifact / 8 MiB un-transcoded original; 512 MiB per match per
  direction) → locally-known blocklist → already-have (`decline:'have'` — the cross-session
  reuse SUCCESS path) → concurrency (1 in / 1 out) → session quota → accept.
- **Integrity is receiver-side**: the offered sha256 is a claim. The receiver hashes the
  committed bytes itself and sniffs magic bytes; disagreement with the claimed mime/hash is
  `xfer_fail` and the sha is refused for the rest of the match. The artifact's identity
  everywhere (dedupe, cache, blocklist, report) is the sha256 of its bytes.
- **Resolution**: the attacker MAY attach `tags: ["xfer:<sha256>", ...]` (≤ 3) to `payload`
  kinds Video and FlashBurst. The receiver resolves each tag against its accepted-artifact
  store; any miss (not landed, blocked, evicted, kind mismatch) falls back to its own
  library. Receipts are IDENTICAL either way — the sender never learns whether its media
  was displayed.
- **Backpressure/liveness**: sender stops filling above 1 MiB buffered and resumes at
  256 KiB; no ack progress for 20 s → cancel; offer unanswered for 8 s → cancel; an
  artifact that cannot plausibly land within 90 s at measured throughput is never offered.
  Chunks for an unaccepted tid are dropped (≥ 64 strays → `xfer_cancel:'stray'`); bytes
  past the offered size are refused.
- **Abuse handling is out-of-band**: `/v2/goon/report` (reporter uploads evidence at report
  time; the server resolves the accused from its own pair record — the wire never carries
  either player's account identity) and `/v2/goon/blocked` (sha256 blocklist; checked
  locally at offer time when known, authoritatively at render time).

## Changelog
- v1 (2026-08-03): initial spec, matches Wave-1 (`d0402126`). Open v1.1 candidates:
  `rejected_cost` receipt status; commit-reveal for per-round seeds; abandon = loser vs
  no-result (play-test call).
- v1.1 (2026-08-04): §12 media channel (sender-media transfer over a second negotiated
  data channel); `caps.transfer` + `consent.media_transfer` (append-only, absent = false);
  `payload.tags` `xfer:` namespace for Video/FlashBurst; §11 exception carved for
  hash-named, receiver-verified transferred content.
- v1.2 (2026-08-04): §2 seat identity (`self_join`, same-uid reclaim), `/v2/goon/leave`,
  and the whitelist-only **self-duel** on a `<uid>#self` shadow seat. Server + web client
  only; the C# client and its fake server are unported (see the note in §2).
- v1.3 (2026-08-04, beta hardening): `media_send` on `/invite` and `/join` — the
  server-authoritative premium SEND verdict the standalone web client adopts (it no
  longer self-grants `caps.mediaTransfer`; only a server-less pure-local dev launch
  keeps the affordance). Guest-side `NoOfferTimeoutMs` (20 s): a joined guest that
  never receives an offer fails over to the relay ladder as `no_offer` instead of
  waiting on "joining…" forever (the host's untimed lobby wait is unchanged).
- v1.4 (2026-08-04, shareable invites): `media_prep` (§6) — an append-only, v1-safe
  presence hint saying "still assembling a library", raised by the web client's
  first-run media step and rendered by the peer's lobby. Plus the `?join=<code>`
  invite-link format (§6, client-side only — no server change, no new endpoint).
  Web client only; the C# client neither sends nor reads `media_prep` and, per §1,
  ignores the frame as an unknown `t`.
- v1.5 (2026-08-04, host gate + free join): §2 **hosting is tier 2** — `/invite` answers
  403 `no_host_access` below `computeEffectiveTier >= 2` — and **joining is free for
  everyone**, including people with no account, who play on a server-minted `g_` guest
  seat (`guest_id` on the `/join` response, presented as `unified_id` on every later
  room-scoped call, no `X-Auth-Token`). The weekly free pass and its 402 `no_pass` are
  retired; clients keep parsing `no_pass` for old-server tolerance. `pass` on `/join`
  becomes the advisory label `"free" | "rejoin" | "self_duel"`. New host→page `init`
  cap `caps.canHost` (§2) lets the desktop title screen dim Host before the round-trip;
  it is NOT a peer-negotiated hello cap and never crosses the wire between players.
  No wire-version change: every addition is a new response field or a new refusal on an
  existing route, and §1's unknown-field rules already cover both.
