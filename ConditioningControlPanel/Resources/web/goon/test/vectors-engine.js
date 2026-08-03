// Engine parity + headless smoke for the Wave-2 match engine.
//
//   node Resources/web/goon/test/vectors-engine.js
//
// Part 1 — RAMP PARITY: replays the `ramp` section of rng-vectors.json (dumped by the C#
//          GoonVectorDumper, the REFERENCE implementation) through core/draft.js buildRamp and
//          deep-compares every cue, in order, field by field. Then does the same for every NAMED
//          case in `ramps` (baseline + spiral/braindrain), so each sustained profile's entry
//          fraction and intensity band is covered, not just the ones the baseline draft happens
//          to use. `ramps` is optional only so an un-regenerated vectors file fails loudly on
//          content rather than confusingly on a missing key.
// Part 2 — SMOKE: two GoonMatchService instances over an in-file fake transport pair (this file
//          owns the fake; net/ owns the real ones) run hello -> consent -> draft -> match_start ->
//          live ticks, and both sides must agree on the combined seed AND on the ramp it plans.
//
// Exit 0 PASS, 1 on the first mismatch (with detail), 2 when the vectors file is missing.

import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

import { GoonAttentionMode, GoonElement, GoonMatchPhase, GoonTransportState, isClockMessage } from '../core/contracts.js';
import { GoonRng } from '../core/rng.js';
import { MatchClock } from '../core/clock.js';
import { parse, serialize } from '../core/wire.js';
import { GoonCueAction, buildRamp } from '../core/draft.js';
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
for (const r of namedRamps) {
  rampCues += checkRamp(r, `ramps[${r.name || '?'}]`);
  rampCaseCount++;
  if ((r.elements || []).includes(GoonElement.Spiral)) sawSpiral = true;
}
if (!sawSpiral) {
  console.error(`no ramps case drafts Spiral (element ${GoonElement.Spiral}) — stale vectors file`);
  process.exit(2);
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

  // draft
  const hostPicks = [GoonElement.Flashes, GoonElement.Bubbles, GoonElement.LockCards];
  const guestPicks = [GoonElement.Subliminals, GoonElement.Videos, GoonElement.BouncingText];
  expect(host.setDraft(hostPicks).ok, 'host setDraft');
  expect(guest.setDraft(guestPicks).ok, 'guest setDraft');
  await waitFor('drafts exchanged', () => host.remoteDraft.length === 3 && guest.remoteDraft.length === 3);
  expect(host.lockDraft().ok, 'host lockDraft');
  expect(guest.lockDraft().ok, 'guest lockDraft');

  // match_start + countdown -> live (host proposes on its phase timer once both drafts lock)
  await waitFor('countdown', () => host.phase === GoonMatchPhase.Countdown && guest.phase === GoonMatchPhase.Countdown);
  expect(host.startMatchMs > 0 && guest.startMatchMs === host.startMatchMs, 'start instant agreed', {
    host: host.startMatchMs, guest: guest.startMatchMs,
  });
  await waitFor('live phase', () => host.phase === GoonMatchPhase.Live && guest.phase === GoonMatchPhase.Live, 15000);

  // Both sides combined the same seed, and therefore plan the same shape for the same draft.
  expect(host.matchSeed === guest.matchSeed && host.matchSeed !== 0n, 'combined seed', {
    host: host.matchSeed.toString(), guest: guest.matchSeed.toString(),
  });
  for (const picks of [hostPicks, guestPicks]) {
    const a = buildRamp(picks, host.matchSeed, 60, (s) => new GoonRng(s));
    const b = buildRamp(picks, guest.matchSeed, 60, (s) => new GoonRng(s));
    expect(JSON.stringify(a) === JSON.stringify(b), 'ramp agreement', { picks: picks.join('+') });
    expect(a.length > 0, 'ramp non-empty', { picks: picks.join('+') });
  }

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

console.log(`PASS — ramp parity: ${rampCues} cues matched across ${rampCaseCount} draft(s), ` +
  `Spiral covered (seed ${data.ramp.seed}); ` +
  `smoke: full phase run over a fake transport pair, combined seed ${smokeResult.seed}, host score ${smokeResult.score}`);
process.exit(0);
