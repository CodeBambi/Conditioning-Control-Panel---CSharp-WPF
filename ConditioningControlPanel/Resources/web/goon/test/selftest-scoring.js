// Self-contained pass over the points model (core/points.js) and its wiring.
//
//   node Resources/web/goon/test/selftest-scoring.js
//
// What is asserted:
//   1. the ledger: weights, held shares, the pop limiter, the combo, the duel pot, heat.
//   2. A SCRIPTED 10-MINUTE MATCH lands around 70/30 send/own, and a duel pot is about 20%.
//   3. the engine: the caps gate, a rejected throw scores nothing, a receipt scores once,
//      the held share rides the receipt, the tick carries `sc`, a legacy match is untouched.

import {
  GoonAttentionMode, GoonElement, GoonMatchPhase, GoonPayloadKind, GoonTransportState, SCORE_CAP_VERSION, makeHello, makePayload,
  makePayloadReceipt, makeTick, peerScoresPoints,
} from '../core/contracts.js';
import { local as localCaps } from '../core/caps.js';
import { parse, serialize } from '../core/wire.js';
import { GoonMatchService } from '../core/match.js';
import { GoonReceiptStatus, GoonScoring } from '../core/scoring.js';
import {
  POINTS, PointsLedger, duelPoints, heatTarget, heldShareOf, readWireStats, sendWeight, smoothHeat,
} from '../core/points.js';
import { GoonRng } from '../core/rng.js';

let n = 0;
let failures = 0;
function ok(cond, what, detail) {
  n++;
  if (!cond) { failures++; console.error('FAIL: ' + what + (detail !== undefined ? ' (' + detail + ')' : '')); }
}
const near = (a, b, eps = 1e-9) => Math.abs(a - b) <= eps;
const quiet = { debug() {}, info() {}, warn() {}, error() {} };

// ================================================================ 1. the ledger
{
  ok(sendWeight(GoonPayloadKind.FlashBurst) < sendWeight(GoonPayloadKind.Video), 'a flash pays less than a video');
  ok(sendWeight(GoonPayloadKind.Spiral) === sendWeight(GoonPayloadKind.LockCard), 'spiral and lock card are the big pair');
  ok(sendWeight(99) === POINTS.SEND_WEIGHT_DEFAULT, 'an unknown kind pays the default');
  ok(heldShareOf('survived') === 1 && heldShareOf('completed') === POINTS.LEGACY_COMPLETED_HELD, 'legacy statuses map to full / partial');
  ok(heldShareOf('completed', 0.25) === 0.25 && heldShareOf('survived', 7) === 1 && heldShareOf('completed', -1) === 0, 'an explicit held wins and is clamped');
  ok(heldShareOf('rejected_rate') === 0, 'a rejection holds nothing');

  const L = new PointsLedger();
  const hit = L.landed(GoonPayloadKind.Video, 0.5);
  ok(hit && near(hit.points, sendWeight(GoonPayloadKind.Video) * 0.5) && hit.type === 'hit', 'a half-held video pays half its weight');
  const held = L.held(GoonPayloadKind.Video, 1, 2);
  ok(held && near(held.points, sendWeight(GoonPayloadKind.Video) * POINTS.OWN_HELD_RATE * 2), 'held = weight x share x rate x chip');
  ok(L.landed(GoonPayloadKind.Video, 0) === null && L.sent.landed === 2, 'a zero share lands but pays nothing');

  const P = new PointsLedger();
  const a1 = P.pop(0);
  ok(a1.combo === 1 && near(a1.points, POINTS.POP_TICK), 'first pop: combo 1, the plain tick');
  const a2 = P.pop(500);
  ok(a2.combo === 2 && a2.points > a1.points, 'a quick second pop climbs the combo and pays a little more');
  const a3 = P.pop(500 + POINTS.COMBO_GAP_MS + 1);
  ok(a3.combo === 1, 'a gap resets the combo');
  ok(P.bestCombo === 2, 'best combo is kept');
  const F = new PointsLedger();
  let paid = 0;
  for (let i = 0; i < 40; i++) if (F.pop(i * 10).points > 0) paid++;
  ok(paid === POINTS.POP_BURST, 'a 40-pop spam inside 0.4 s scores only the limiter burst', paid);
  ok(F.pops === 40 && F.bestCombo === 40, 'but every pop still counts for the combo and the stats');
  ok(F.comboLeft(390) === 1 && F.comboLeft(390 + POINTS.COMBO_GAP_MS * 2) === 0, 'comboLeft drains to 0');

  ok(duelPoints('win') === POINTS.DUEL_POT && duelPoints('lose') === POINTS.DUEL_POT / 2 && duelPoints('tie') === POINTS.DUEL_POT / 2, 'duel: winner the pot, loser and tie half');
  ok(heatTarget(0, 0) === 0 && heatTarget(-500, 0) === 0, 'no lead, no combo: cold');
  ok(near(heatTarget(POINTS.HEAT_LEAD_FULL * 9, POINTS.HEAT_COMBO_FULL * 9), 1), 'huge lead + combo: full heat');
  const h = smoothHeat(0, 1, POINTS.HEAT_TAU_S);
  ok(h > 0.6 && h < 0.66, 'heat moves about 63% per tau', h);

  const w = readWireStats({ s: '12', o: -3, d: 1e12, p: 4.7, b: 'x' });
  ok(w && w.s === 12 && w.o === 0 && w.d === 1e7 && w.p === 4 && w.b === 0, 'an untrusted sc is clamped', JSON.stringify(w));
  ok(readWireStats(null) === null && readWireStats([1]) === null, 'absent or junk sc reads null');
}

// ============================================================ 2. the 10-min sim
//
// One "typical" 10-minute match as the balance is meant for: each side throws about every 20 s
// (a mix weighted toward flashes, one heavy), the receiver holds most of it (70% all the way,
// the rest closed early), bubbles are always on and get popped in short chains every few
// seconds, NoCam (chip x1.0). Seeded, so a change to POINTS moves the numbers deterministically.
function simMatch(seed) {
  const rng = new GoonRng(seed);
  const r = () => rng.nextDouble();
  const MIX = [
    [GoonPayloadKind.FlashBurst, 0.30], [GoonPayloadKind.SubliminalStorm, 0.15], [GoonPayloadKind.BubbleSwarm, 0.15],
    [GoonPayloadKind.Video, 0.12], [GoonPayloadKind.Spiral, 0.12], [GoonPayloadKind.LockCard, 0.10],
    [GoonPayloadKind.ToyPattern, 0.06],
  ];
  const pick = () => { let x = r(); for (const [k, p] of MIX) { if ((x -= p) <= 0) return k; } return GoonPayloadKind.FlashBurst; };
  const A = new PointsLedger(), B = new PointsLedger();
  const MATCH_S = 600;
  // throws: both directions, ~every 20 s each, plus one heavy each
  for (const [from, to] of [[A, B], [B, A]]) {
    let t = 5 + r() * 10;
    let heavy = false;
    while (t < MATCH_S) {
      const kind = !heavy && t > 300 ? GoonPayloadKind.BrainDrain : pick();
      if (kind === GoonPayloadKind.BrainDrain) heavy = true;
      const share = r() < 0.7 ? 1 : 0.2 + r() * 0.7;
      from.landed(kind, share);
      to.held(kind, share, 1);
      t += 14 + r() * 12;
    }
  }
  // pops: a chain of 1..5 pops 250-450 ms apart, every 4-8 s, per side
  for (const L of [A, B]) {
    let ms = 2000;
    while (ms < MATCH_S * 1000) {
      const chain = 1 + Math.floor(r() * 5);
      for (let i = 0; i < chain; i++) { L.pop(ms); ms += 250 + r() * 200; }
      ms += 4000 + r() * 4000;
    }
    L.trickle(MATCH_S, 1);
  }
  return { A, B };
}
{
  const shares = [];
  for (const seed of [1, 7, 42, 2026, 9001]) {
    const { A, B } = simMatch(seed);
    for (const L of [A, B]) {
      const base = L.sentPts + L.ownPts;
      shares.push({ send: L.sentPts / base, pot: POINTS.DUEL_POT / base, base, pops: L.pops });
    }
  }
  const avg = (f) => shares.reduce((s, x) => s + f(x), 0) / shares.length;
  const send = avg((x) => x.send), pot = avg((x) => x.pot), base = avg((x) => x.base), pops = avg((x) => x.pops);
  console.log(`sim: send ${(send * 100).toFixed(1)}% / own ${((1 - send) * 100).toFixed(1)}%, typical total ${base.toFixed(0)}, duel pot ${(pot * 100).toFixed(1)}% of it, ${pops.toFixed(0)} pops`);
  ok(send > 0.65 && send < 0.75, 'a typical match lands about 70/30 send/own', send.toFixed(3));
  ok(pot > 0.16 && pot < 0.24, 'a duel pot is about 20% of a typical match total', pot.toFixed(3));
  ok(shares.every((x) => x.send > 0.58 && x.send < 0.82), 'no seed strays far from the split');
}

// ============================================================== 3. the engine
{
  ok(peerScoresPoints({ score: 1 }) && peerScoresPoints({ score: '1' }) && !peerScoresPoints({ score: true }) && !peerScoresPoints({}), 'peerScoresPoints reads a revision');
  ok(localCaps({ score: SCORE_CAP_VERSION }).score === 1 && localCaps({}).score === 0, 'caps.local passes score through, default 0');

  const mk = (caps) => {
    const m = new GoonMatchService({
      state: GoonTransportState.ConnectedP2P,
      send() { return Promise.resolve(true); },
      onMessageReceived() { return () => {}; },
      onStateChanged() { return () => {}; },
    }, true, { logger: quiet, tag: 'GG:score', caps });
    m.sent = [];
    m._send = (msg) => m.sent.push(JSON.parse(serialize(msg)));
    return m;
  };

  // legacy: our build speaks it, theirs does not
  const old = mk(localCaps({ score: SCORE_CAP_VERSION }));
  old._handleHello(makeHello({ caps: localCaps({}) }));
  ok(old._peerScoresPoints === false, 'an older peer does not score points');
  const s0 = new GoonScoring(GoonAttentionMode.NoCam, 0);
  s0.tick(10);
  ok(near(s0.scoreExact, 10), 'legacy scoring is untouched: 1 pt/s', s0.scoreExact);
  ok(s0.awardPop(0) === null && s0.awardLanded(GoonPayloadKind.Video, 1) === null, 'legacy scoring ignores points awards');

  const m = mk(localCaps({ score: SCORE_CAP_VERSION }));
  m._handleHello(makeHello({ caps: localCaps({ score: SCORE_CAP_VERSION }) }));
  ok(m._peerScoresPoints === true, 'a peer with caps.score turns the gate on');
  m._scoring.setPointsModel(m._peerScoresPoints && peerScoresPoints(m._localCaps));
  ok(m.pointsModel === true, 'both seats speak it: the points model scores');
  m._phase = GoonMatchPhase.Live;
  m._localAllowed = Object.values(GoonElement);

  const awards = [];
  m.onPointsAwarded((a) => awards.push(a));
  const f1 = m.tryFirePayload({ kind: GoonPayloadKind.Video, durationMs: 10000 });
  ok(f1.ok, 'a throw goes out', f1.error);
  m._handleReceipt(makePayloadReceipt({ id: f1.id, status: GoonReceiptStatus.RejectedRate }));
  ok(awards.length === 0 && m._scoring.score === 0, 'a rejected throw scores nothing');
  m._handleReceipt(makePayloadReceipt({ id: f1.id, status: GoonReceiptStatus.Completed, held: 1 }));
  ok(awards.length === 0, 'and a closing receipt after the rejection scores nothing either');

  m._outboundLimiter.reset();
  const f2 = m.tryFirePayload({ kind: GoonPayloadKind.Spiral, durationMs: 10000 });
  const rc = parse(serialize(makePayloadReceipt({ id: f2.id, status: GoonReceiptStatus.Completed, held: 0.5 })));
  ok(rc.held === 0.5, 'held survives the wire');
  m._handleReceipt(rc);
  ok(awards.length === 1 && awards[0].type === 'hit' && near(awards[0].points, sendWeight(GoonPayloadKind.Spiral) * 0.5), 'a half-held spiral pays half', JSON.stringify(awards[0]));
  m._handleReceipt(rc);
  ok(awards.length === 1, 'a duplicate receipt scores once');

  const inbound = makePayload({ id: 'x1', kind: GoonPayloadKind.LockCard, duration_ms: 8000, fire_at_match_ms: 0 });
  m._handleInboundPayload(inbound);
  m.sent.length = 0;
  m.notifyInboundPayloadFinished('x1', false, 0.4);
  const closing = m.sent.find((x) => x.t === 'payload_receipt');
  ok(closing && closing.status === 'completed' && closing.held === 0.4, 'our closing receipt carries the held share', JSON.stringify(closing));
  ok(awards.length === 2 && awards[1].type === 'held', 'holding their throw pays us');
  m.notifyInboundPayloadFinished('x1', false, 0.4);
  ok(awards.length === 2, 'finishing the same throw twice pays once');

  const p = m.notePop(1000);
  ok(p && p.type === 'pop' && p.points > 0, 'a Live pop pays the tick');
  m.sent.length = 0;
  m._lastTickSentLocalMs = -1e12;
  m._maybeSendStateTick();
  const tk = m.sent.find((x) => x.t === 'tick');
  ok(tk && tk.sc && tk.sc.s > 0 && tk.sc.o > 0 && tk.sc.p === 1, 'the tick carries the split', JSON.stringify(tk && tk.sc));
  ok(tk.score === m._scoring.score, 'and the score is the whole total');

  m._handleTick(parse(serialize(makeTick({ score: 77, sc: { s: 50, o: 20, d: 7, p: 9, b: 4, c: 0 } }))));
  const st = m.matchStats();
  ok(st.pointsModel && st.me.sent.landed === 1 && st.me.received.byKind[GoonPayloadKind.LockCard] === 1 && st.me.pops === 1, 'matchStats: me', JSON.stringify(st.me));
  ok(st.them.score === 77 && st.them.pops === 9 && st.them.bestCombo === 4 && st.them.split.sent === 50, 'matchStats: them off their tick', JSON.stringify(st.them));

  const d = m.noteDuel('win');
  ok(d && d.points === POINTS.DUEL_POT && m.matchStats().me.duels.won === 1, 'a duel win pays the pot');

  m._phase = GoonMatchPhase.Recap;
  ok(m.notePop(5000) === null, 'no pop scores outside Live');

  // a legacy match never puts held / sc on the wire
  const lg = mk(localCaps({ score: SCORE_CAP_VERSION }));
  lg._handleHello(makeHello({ caps: localCaps({}) }));
  lg._phase = GoonMatchPhase.Live;
  lg._handleInboundPayload(makePayload({ id: 'y1', kind: GoonPayloadKind.FlashBurst, duration_ms: 3000 }));
  lg.sent.length = 0;
  lg.notifyInboundPayloadFinished('y1', false, 0.3);
  lg._lastTickSentLocalMs = -1e12;
  lg._maybeSendStateTick();
  const lr = lg.sent.find((x) => x.t === 'payload_receipt');
  const lt = lg.sent.find((x) => x.t === 'tick');
  ok(lr && !('held' in lr) && lt && !('sc' in lt), 'a legacy match sends neither held nor sc', JSON.stringify([lr, lt]));
  ok(lg.notePop(1) === null, 'and scores no pops');
}

if (failures) {
  console.error(`\nselftest-scoring: ${n - failures}/${n} checks passed`);
  console.error(`${failures} FAILURE(S)`);
  process.exit(1);
}
console.log(`selftest-scoring: ${n}/${n} checks passed`);
