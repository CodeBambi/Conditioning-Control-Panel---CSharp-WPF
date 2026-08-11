// Engine parity + headless smoke for the Wave-2 match engine.
//
//   node Resources/web/goon/test/vectors-engine.js
//
// Part 1 — RAMP PARITY: replays the `ramp` section of rng-vectors.json (dumped by the C#
//          GoonVectorDumper, the REFERENCE implementation) through core/draft.js buildRamp and
//          deep-compares every cue, in order, field by field. Then does the same for every NAMED
//          case in `ramps` (baseline + spiral + a RESTRICTED two-element pool + the full toggle
//          pool + always-on-only), so the roll's pass/stride maths and every intensity band are
//          covered. Since the 2026-08-03 redesign the roll is seeded, so this section is also the
//          proof that the two languages consume the rng in the same ORDER and COUNT.
// Part 1b — SHARED POOL PARITY: the `shared_pools` section — two allowed sets in, one canonical
//          intersection out — replayed through core/draft.js sharedPool.
// Part 1c — REDESIGN INVARIANTS: always-on bubbles t=0 -> end at 0.15 -> 1.00, nothing outside the
//          pool in the roll, balanced per-element active time, and one schedule for both players.
// Part 2 — SMOKE: two GoonMatchService instances over an in-file fake transport pair (this file
//          owns the fake; net/ owns the real ones) run hello -> consent -> draft agreement ->
//          match_start -> live ticks. Both sides must agree on the combined seed, on the shared
//          pool and on the ramp; the agreement's confirm-clearing, minimum-intersection refusal
//          and the creditCharges seam are exercised here too.
//
// Exit 0 PASS, 1 on the first mismatch (with detail), 2 when the vectors file is missing.

import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

import {
  GoonAttentionMode, GoonConsts, GoonElement, GoonMatchPhase, GoonTransportState, isClockMessage,
} from '../core/contracts.js';
import { GoonRng } from '../core/rng.js';
import { MatchClock } from '../core/clock.js';
import { parse, serialize } from '../core/wire.js';
import {
  ALWAYS_ON_ELEMENT, GoonCueAction, MIN_ALLOWED_ELEMENTS, buildRamp, normalizeAllowed, sharedPool,
} from '../core/draft.js';
import { GoonMatchService } from '../core/match.js';

const here = dirname(fileURLToPath(import.meta.url));
const vectorsPath = join(here, 'rng-vectors.json');

const quietLogger = { info() {}, warn() {}, error(m) { console.error(m); }, debug() {} };

function fail(section, detail) {
  console.error('FAIL');
  console.error(`  section: ${section}`);
  for (const [k, v] of Object.entries(detail)) console.error(`  ${k}: ${v}`);
  process.exit(1);
}

// =========================================================================== part 1: ramp parity

if (!existsSync(vectorsPath)) {
  console.error('vectors file missing — run --goon-vectors');
  process.exit(2);
}

let data;
try {
  data = JSON.parse(readFileSync(vectorsPath, 'utf8'));
} catch (e) {
  console.error(`vectors file unreadable: ${e.message}`);
  process.exit(2);
}

const ACTION_NAMES = {
  [GoonCueAction.Start]: 'start',
  [GoonCueAction.Intensity]: 'intensity',
  [GoonCueAction.Stop]: 'stop',
};

function checkRamp(ramp, label) {
  if (!ramp) {
    console.error('vectors file has no `ramp` section — regenerate with --goon-vectors');
    process.exit(2);
  }

  const seed = BigInt(ramp.seed);
  const liveMs = ramp.live_duration_ms;
  // C# BuildRamp takes SECONDS; the vector records the live phase in ms.
  const liveSec = liveMs / 1000;
  const expected = ramp.cues || [];

  const got = buildRamp(ramp.elements, seed, liveSec, (s) => new GoonRng(s));

  if (got.length !== expected.length) {
    fail(label, {
      seed: ramp.seed,
      elements: JSON.stringify(ramp.elements),
      'cue count expected': expected.length,
      'cue count got': got.length,
    });
  }

  for (let i = 0; i < expected.length; i++) {
    const e = expected[i];
    const g = got[i];
    const gotAction = ACTION_NAMES[g.action];
    const mismatch =
      e.element !== g.element ? 'element'
        : e.offset_ms !== g.offsetMs ? 'offset_ms'
          : e.type !== gotAction ? 'type'
            : Math.abs(Number(e.intensity) - g.intensity) > 1e-12 ? 'intensity'
              : e.duration_ms !== g.durationMs ? 'duration_ms'
                : null;
    if (mismatch) {
      fail(label, {
        seed: ramp.seed,
        elements: JSON.stringify(ramp.elements),
        index: i,
        field: mismatch,
        expected: JSON.stringify(e),
        got: JSON.stringify({ element: g.element, offset_ms: g.offsetMs, type: gotAction, intensity: g.intensity, duration_ms: g.durationMs }),
      });
    }
  }

  return expected.length;
}

let rampCues = checkRamp(data.ramp, 'ramp');
let rampCaseCount = 1;

// Named cases. Spiral (element 8) MUST be exercised by one of them — it is the whole reason
// `ramps` exists, and a vectors file regenerated from a build that predates it would otherwise
// pass silently while testing nothing new.
const namedRamps = Array.isArray(data.ramps) ? data.ramps : [];
if (namedRamps.length === 0) {
  console.error('vectors file has no `ramps` section — regenerate with --goon-vectors');
  process.exit(2);
}
let sawSpiral = false;
let sawRestricted = false;
for (const r of namedRamps) {
  rampCues += checkRamp(r, `ramps[${r.name || '?'}]`);
  rampCaseCount++;
  if ((r.elements || []).includes(GoonElement.Spiral)) sawSpiral = true;
  // A pool at the legal minimum drives the pass/stride branch hardest; it also proves the two
  // languages agree on a schedule that ISN'T the wide-open one.
  if (normalizeAllowed(r.elements || []).length === MIN_ALLOWED_ELEMENTS) sawRestricted = true;
}
if (!sawSpiral) {
  console.error(`no ramps case drafts Spiral (element ${GoonElement.Spiral}) — stale vectors file`);
  process.exit(2);
}
if (!sawRestricted) {
  console.error(`no ramps case uses a RESTRICTED pool of exactly ${MIN_ALLOWED_ELEMENTS} elements — stale vectors file`);
  process.exit(2);
}

// ================================================================= part 1b: shared pool parity

const poolCases = Array.isArray(data.shared_pools) ? data.shared_pools : [];
if (poolCases.length === 0) {
  console.error('vectors file has no `shared_pools` section — regenerate with --goon-vectors');
  process.exit(2);
}
for (const c of poolCases) {
  const got = sharedPool(c.a || [], c.b || []);
  const want = c.pool || [];
  if (JSON.stringify(got) !== JSON.stringify(want)) {
    fail(`shared_pools[${c.name || '?'}]`, {
      a: JSON.stringify(c.a), b: JSON.stringify(c.b),
      expected: JSON.stringify(want), got: JSON.stringify(got),
    });
  }
  if (c.min_allowed !== undefined && c.min_allowed !== MIN_ALLOWED_ELEMENTS) {
    fail(`shared_pools[${c.name || '?'}]`, {
      field: 'min_allowed', expected: c.min_allowed, got: MIN_ALLOWED_ELEMENTS,
    });
  }
}

// ============================================================ part 1c: redesign invariants
//
// These fail loudly against the PRE-redesign engine: it emitted no Bubbles cue unless Bubbles was
// drafted, it built a ramp from ONE player's three picks, and its per-element time was whatever
// the gap jitter happened to produce.

{
  const liveSec = 720;
  const liveMs = liveSec * 1000;
  const seed = 0xA5A5C0FFEEn;
  const pool = [GoonElement.Flashes, GoonElement.Spiral, GoonElement.BrainDrain, GoonElement.Videos];

  const ramp = buildRamp(pool, seed, liveSec, (s) => new GoonRng(s));

  // (1) Always-on bubbles: one Start at t=0 at 0.15, a Stop at the very end, and a monotonic climb
  //     that reaches for 1.00 — whatever the pool says.
  const bub = ramp.filter((c) => c.element === ALWAYS_ON_ELEMENT);
  const bubStarts = bub.filter((c) => c.action === GoonCueAction.Start);
  const bubStops = bub.filter((c) => c.action === GoonCueAction.Stop);
  expect(bubStarts.length === 1 && bubStarts[0].offsetMs === 0, 'bubbles start once, at t=0', {
    starts: bubStarts.length, at: bubStarts[0] && bubStarts[0].offsetMs,
  });
  expect(Math.abs(bubStarts[0].intensity - 0.15) < 1e-12, 'bubbles open at 0.15', {
    got: bubStarts[0].intensity,
  });
  expect(bubStarts[0].durationMs === 0, 'bubbles are sustained (durationMs 0), not a burst');
  expect(bubStops.length === 1 && bubStops[0].offsetMs === liveMs, 'bubbles stop at the final whistle', {
    stops: bubStops.length, at: bubStops[0] && bubStops[0].offsetMs,
  });
  const bubRamped = bub.filter((c) => c.action === GoonCueAction.Intensity);
  let monotonic = true;
  let prev = bubStarts[0].intensity;
  for (const c of bubRamped) { if (c.intensity < prev - 1e-12) monotonic = false; prev = c.intensity; }
  expect(bubRamped.length > 0 && monotonic, 'bubbles climb monotonically', { steps: bubRamped.length });
  const wantLast = 0.15 + 0.85 * (bubRamped[bubRamped.length - 1].offsetMs / liveMs);
  expect(Math.abs(prev - wantLast) < 1e-12 && prev > 0.9, 'bubbles reach for 1.00 by the end', {
    last: prev, want: wantLast,
  });
  // ...and it is there even when NOTHING is in the pool.
  const bare = buildRamp([], seed, liveSec, (s) => new GoonRng(s));
  expect(bare.length > 0 && bare.every((c) => c.element === ALWAYS_ON_ELEMENT),
    'an empty pool still runs the bubbles baseline and nothing else', { cues: bare.length });

  // (2) The roll never reaches outside the pool (and never rolls the always-on element).
  const rolled = ramp.filter((c) => c.element !== ALWAYS_ON_ELEMENT);
  const strays = rolled.filter((c) => !pool.includes(c.element));
  expect(strays.length === 0, 'the roll stays inside the agreed pool', { strays: JSON.stringify(strays.slice(0, 3)) });

  // (3) Comparable total active time per element — the fairness requirement.
  const active = new Map(pool.map((e) => [e, 0]));
  const open = new Map();
  for (const c of rolled) {
    if (c.action === GoonCueAction.Start) open.set(c.element, c.offsetMs);
    else if (c.action === GoonCueAction.Stop && open.has(c.element)) {
      active.set(c.element, active.get(c.element) + (c.offsetMs - open.get(c.element)));
      open.delete(c.element);
    }
  }
  const totals = Array.from(active.values());
  const lo = Math.min(...totals);
  const hi = Math.max(...totals);
  expect(lo > 0 && hi - lo <= 0.15 * hi, 'every element gets comparable active time', {
    totals: JSON.stringify(Array.from(active.entries())), lo, hi,
  });

  // (4) One schedule, two players: same pool + same seed = the same cue list, and the order the
  //     pool is written in must not matter (both engines normalize).
  const asHost = buildRamp(pool, seed, liveSec, (s) => new GoonRng(s));
  const asGuest = buildRamp(pool.slice().reverse(), seed, liveSec, (s) => new GoonRng(s));
  expect(JSON.stringify(asHost) === JSON.stringify(asGuest), 'both players roll the identical schedule');
  const otherSeed = buildRamp(pool, seed + 1n, liveSec, (s) => new GoonRng(s));
  expect(JSON.stringify(otherSeed) !== JSON.stringify(asHost), 'a different seed rolls a different schedule');
}

// ============================================================================ part 2: fake wire

/**
 * The smallest thing that satisfies match.js's transport surface: a pair of endpoints that hand
 * each other SERIALIZED frames (so wire.js is exercised too), give the MatchClock first refusal
 * exactly as GoonTransportBase does, and deliver on a microtask so a handler can answer inline
 * without recursing.
 */
class FakeTransport {
  constructor(isHost, { testSkewMs = 0 } = {}) {
    this.isHost = isHost;
    this._state = GoonTransportState.Disconnected;
    this._peer = null;
    this._msgListeners = new Set();
    this._stateListeners = new Set();
    this.clock = new MatchClock({
      isClockMaster: isHost,
      tag: isHost ? 'FakeHostClock' : 'FakeGuestClock',
      logger: quietLogger,
      testSkewMs,
    });
    this.clock.attach((m) => this.sendAsync(m));
    this.sent = 0;
  }

  static pair({ guestSkewMs = 3517 } = {}) {
    const host = new FakeTransport(true);
    const guest = new FakeTransport(false, { testSkewMs: guestSkewMs });
    host._peer = guest;
    guest._peer = host;
    return { host, guest };
  }

  get state() { return this._state; }

  onMessageReceived(fn) { this._msgListeners.add(fn); return () => this._msgListeners.delete(fn); }
  onStateChanged(fn) { this._stateListeners.add(fn); return () => this._stateListeners.delete(fn); }

  markConnected() {
    this._state = GoonTransportState.ConnectedP2P;
    for (const fn of Array.from(this._stateListeners)) fn(this._state);
  }

  async sendAsync(message) {
    this.sent++;
    const json = serialize(message);
    const peer = this._peer;
    if (!peer) return;
    queueMicrotask(() => peer._receiveRaw(json));
  }

  _receiveRaw(json) {
    const msg = parse(json, { logger: quietLogger });
    if (!msg) return;
    if (this.clock.tryHandleMessage(msg)) return;   // clock traffic is private to the clock
    if (isClockMessage(msg)) return;
    for (const fn of Array.from(this._msgListeners)) fn(msg);
  }

  async closeAsync() {
    this._state = GoonTransportState.Closed;
    this.clock.dispose();
  }
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function waitFor(label, predicate, timeoutMs = 20000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (predicate()) return;
    await sleep(25);
  }
  fail('smoke', { step: label, error: `timed out after ${timeoutMs}ms` });
}

function expect(cond, step, detail) {
  if (!cond) fail('smoke', Object.assign({ step }, detail || {}));
}

async function smoke() {
  const { host: hostT, guest: guestT } = FakeTransport.pair();
  const opts = { rngFactory: (s) => new GoonRng(s), logger: quietLogger };

  const host = new GoonMatchService(hostT, true, Object.assign({ displayName: 'Host', appVersion: 'test' }, opts));
  const guest = new GoonMatchService(guestT, false, Object.assign({ displayName: 'Guest', appVersion: 'test' }, opts));
  host.localAttentionMode = GoonAttentionMode.NoCam;
  guest.localAttentionMode = GoonAttentionMode.NoCam;

  const phases = { host: [], guest: [] };
  host.onPhaseChanged((p) => phases.host.push(p));
  guest.onPhaseChanged((p) => phases.guest.push(p));

  // Lobby BEFORE the channel comes up — the real order (an invite is minted, then the peer
  // arrives). A hello that lands while the service is still Idle is consumed and never replayed.
  host.adoptLobby();
  guest.adoptLobby();

  hostT.markConnected();
  guestT.markConnected();

  // The countdown refuses to act on an unsynced clock.
  const [hostSynced, guestSynced] = await Promise.all([hostT.clock.sync(), guestT.clock.sync()]);
  expect(hostSynced && guestSynced, 'clock sync', { hostSynced, guestSynced });
  // The guest carries a 3517 ms fake skew; if the offset were ignored the two match clocks would
  // disagree by that much and every fire-at timestamp would land in the wrong place.
  const clockDelta = Math.abs(hostT.clock.nowMatchMs() - guestT.clock.nowMatchMs());
  expect(clockDelta < 250, 'clock offset applied', { clockDelta });

  await waitFor('consent phase', () => host.phase === GoonMatchPhase.Consent && guest.phase === GoonMatchPhase.Consent);

  // consent: the host proposed on hello; the guest adopts that sheet and confirms.
  host.proposeConsent(60, 0.7, 30000);
  await waitFor('guest sees sheet', () => guest.consentSheet.live_duration_sec === 60);
  guest.confirmConsent();
  await waitFor('host sees guest confirm', () => host.remoteConsentConfirmed);
  host.confirmConsent();
  await waitFor('draft phase', () => host.phase === GoonMatchPhase.Draft && guest.phase === GoonMatchPhase.Draft);

  // ---- the draft agreement -------------------------------------------------------------
  // Everything starts ON, on both sides, and each side has seen the other's default set.
  await waitFor('default allowed sets exchanged', () => host.remoteAllowedElements.length > 0
    && guest.remoteAllowedElements.length > 0);
  expect(!host.localAllowedElements.includes(GoonElement.Bubbles),
    'the always-on element is not in the toggle set', { allowed: host.localAllowedElements.join('+') });
  expect(JSON.stringify(host.sharedElementPool) === JSON.stringify(guest.sharedElementPool),
    'both sides compute the same intersection', {
      host: host.sharedElementPool.join('+'), guest: guest.sharedElementPool.join('+'),
    });

  // A veto removes the element for BOTH of them.
  expect(host.toggleAllowedElement(GoonElement.BrainDrain).ok, 'host vetoes the heavy');
  await waitFor('veto crossed the wire', () => !guest.remoteAllowedElements.includes(GoonElement.BrainDrain));
  expect(!guest.sharedElementPool.includes(GoonElement.BrainDrain),
    'the vetoed element leaves the shared pool on the OTHER side too');

  // Signatures: the guest signs, the host then moves a toggle, and BOTH signatures die.
  expect(guest.confirmDraft().ok, 'guest signs');
  await waitFor('host sees the guest signature', () => host.remoteDraftConfirmed);
  expect(host.toggleAllowedElement(GoonElement.Videos).ok, 'host moves another toggle');
  expect(!host.localDraftConfirmed && !host.remoteDraftConfirmed, 'a toggle clears BOTH signatures locally', {
    local: host.localDraftConfirmed, remote: host.remoteDraftConfirmed,
  });
  await waitFor('guest signature cleared by the change', () => !guest.localDraftConfirmed);
  expect(!host.draftResolved && !guest.draftResolved, 'and the draft is un-resolved again');

  // The minimum bites: a pool below MIN_ALLOWED_ELEMENTS cannot be signed.
  const tiny = host.setAllowedElements([GoonElement.Flashes]);
  expect(!tiny.ok, 'you cannot allow fewer than the minimum', { error: tiny.error });
  const pair = [GoonElement.Flashes, GoonElement.Spiral];
  expect(host.setAllowedElements(pair).ok, 'host narrows to the legal minimum');
  await waitFor('narrow set crossed', () => guest.remoteAllowedElements.length === 2);
  const disjoint = guest.setAllowedElements([GoonElement.Videos, GoonElement.LockCards]);
  expect(disjoint.ok, 'guest narrows to a DISJOINT pair');
  await waitFor('disjoint set crossed', () => host.sharedElementPool.length === 0);
  const refused = host.confirmDraft();
  expect(!refused.ok, 'an empty intersection refuses the signature', { error: refused.error });
  expect(!host.localDraftConfirmed, 'and nothing was signed');

  // Back to something workable, then both sign.
  expect(guest.setAllowedElements([GoonElement.Flashes, GoonElement.Spiral, GoonElement.Subliminals]).ok,
    'guest re-opens an overlapping set');
  await waitFor('overlap restored', () => host.sharedElementPool.length >= MIN_ALLOWED_ELEMENTS);
  expect(host.confirmDraft().ok, 'host signs');
  await waitFor('guest sees the host signature', () => guest.remoteDraftConfirmed);
  expect(guest.confirmDraft().ok, 'guest signs');
  await waitFor('draft resolved on both sides', () => host.draftResolved && guest.draftResolved);
  const agreedPool = host.sharedElementPool;
  expect(JSON.stringify(agreedPool) === JSON.stringify(guest.sharedElementPool),
    'the agreed pool is identical on both sides', {
      host: agreedPool.join('+'), guest: guest.sharedElementPool.join('+'),
    });

  // match_start + countdown -> live (host proposes on its phase timer once both drafts lock)
  await waitFor('countdown', () => host.phase === GoonMatchPhase.Countdown && guest.phase === GoonMatchPhase.Countdown);
  expect(host.startMatchMs > 0 && guest.startMatchMs === host.startMatchMs, 'start instant agreed', {
    host: host.startMatchMs, guest: guest.startMatchMs,
  });
  await waitFor('live phase', () => host.phase === GoonMatchPhase.Live && guest.phase === GoonMatchPhase.Live, 15000);

  // Both sides combined the same seed, and therefore roll the same schedule from the same pool.
  expect(host.matchSeed === guest.matchSeed && host.matchSeed !== 0n, 'combined seed', {
    host: host.matchSeed.toString(), guest: guest.matchSeed.toString(),
  });
  const hostRamp = buildRamp(host.sharedElementPool, host.matchSeed, 60, (s) => new GoonRng(s));
  const guestRamp = buildRamp(guest.sharedElementPool, guest.matchSeed, 60, (s) => new GoonRng(s));
  expect(JSON.stringify(hostRamp) === JSON.stringify(guestRamp), 'both players run ONE schedule', {
    pool: agreedPool.join('+'),
  });
  expect(hostRamp.some((c) => c.element === ALWAYS_ON_ELEMENT), 'the live ramp carries the bubbles baseline');
  expect(hostRamp.some((c) => c.element !== ALWAYS_ON_ELEMENT), 'and the rolled pool on top of it');

  // creditCharges: the seam the bubble economy consumes. Live only, integer >= 1, capped.
  expect(host.creditCharges(0, 'zero') === false, 'creditCharges refuses a count below 1');
  expect(host.creditCharges(1, 'smoke') === true, 'creditCharges credits in the Live phase');
  const capped = host.scoring.charges;
  expect(host.creditCharges(99, 'flood') === true, 'creditCharges accepts a big credit');
  expect(host.scoring.charges === GoonConsts.ChargeCap && host.scoring.charges >= capped,
    'and clamps at the charge cap', { charges: host.scoring.charges, cap: GoonConsts.ChargeCap });

  // A few live ticks: score accrues on both sides and state ticks cross the wire.
  await sleep(3200);
  expect(host.scoring.score > 0 && guest.scoring.score > 0, 'score accrues', {
    host: host.scoring.score, guest: guest.scoring.score,
  });
  expect(host.opponent.hasSeenTick && guest.opponent.hasSeenTick, 'state ticks exchanged');
  expect(host.liveElapsedMs > 0 && guest.liveElapsedMs > 0, 'live watch running');

  // mercy ends both sides and produces an agreed, ledger-counting result.
  guest.declareMercy();
  await waitFor('both in recap', () => host.phase === GoonMatchPhase.Recap && guest.phase === GoonMatchPhase.Recap);
  await waitFor('result handshake', () => !!host.result && !!guest.result && host.result.agreed && guest.result.agreed, 12000);
  expect(host.result.winnerIsHost === true && guest.result.winnerIsHost === true, 'mercy loser is the guest', {
    host: String(host.result.winnerIsHost), guest: String(guest.result.winnerIsHost),
  });
  expect(host.result.countsForLedger && guest.result.countsForLedger, 'live mercy counts for the ledger');
  expect(!host.result.disputed && !guest.result.disputed, 'result undisputed');

  // ...and the charge seam is inert once the match is over.
  expect(host.creditCharges(1, 'after the whistle') === false, 'creditCharges no-ops outside Live');

  const wanted = [GoonMatchPhase.Lobby, GoonMatchPhase.Consent, GoonMatchPhase.Draft,
    GoonMatchPhase.Countdown, GoonMatchPhase.Live, GoonMatchPhase.Recap];
  for (const side of ['host', 'guest']) {
    expect(JSON.stringify(phases[side]) === JSON.stringify(wanted), `${side} phase sequence`, {
      expected: JSON.stringify(wanted), got: JSON.stringify(phases[side]),
    });
  }

  host.dispose();
  guest.dispose();
  await hostT.closeAsync();
  await guestT.closeAsync();

  return { seed: host.matchSeed.toString(), score: host.scoring.score };
}

const smokeResult = await smoke();

console.log(`PASS — ramp parity: ${rampCues} cues matched across ${rampCaseCount} pool(s), ` +
  `Spiral + a restricted pool covered (seed ${data.ramp.seed}); ` +
  `shared-pool parity: ${poolCases.length} case(s); ` +
  `invariants: always-on bubbles, in-pool roll, balanced time, one schedule; ` +
  `smoke: full phase run over a fake transport pair, combined seed ${smokeResult.seed}, host score ${smokeResult.score}`);
process.exit(0);
