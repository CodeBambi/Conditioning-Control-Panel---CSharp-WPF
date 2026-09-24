// Self-contained sanity pass over GAME NIGHT DUELS (the Deep End game card).
//
//   node Resources/web/goon/test/selftest-duel.js
//
// Pins: the shared cap contract (caps.night, peerSpeaksNight), the `t:'duel'` frame's clamps in
// both directions, the engine's send/receive gates, the pure 2048 rules copied from the arcademy,
// same-seed boards on both sides, a symmetric winner, the drop eligibility rule, the arsenal slot,
// and a full two-controller duel over a fake wire with a virtual clock.

import { serialize, parse } from '../core/wire.js';
import {
  makeCaps, makeDuel, makeHello, GoonMatchPhase, GoonTransportState,
  NIGHT_CAP_VERSION, VOICE_CAP_VERSION, peerSpeaksNight, clampDuelLen, DUEL_LENGTHS_SEC,
} from '../core/contracts.js';
import { local as localCaps } from '../core/caps.js';
import { GoonMatchService } from '../core/match.js';
import { GoonScoring } from '../core/scoring.js';
import { createBoard, openingSpawn, move, play, deepest, serialize as boardText, isLocked } from '../ui/duel/board.js';
import {
  duelSeed, duelOutcome, bonusFor, cardEligible, pickLength, DUEL_WIN_BONUS, DUEL_INTRO_MS, DUEL_REPORT_GRACE_MS,
} from '../ui/duel/rules.js';
import { createDuelController, duelSummary } from '../ui/duel/duelController.js';
import { mountArsenal } from '../ui/arsenal.js';
import { pickDrop } from '../ui/drops.js';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const quiet = { info() {}, warn() {}, error() {}, log() {} };

// A fake localStorage for nightProgress / the hint flag / the length pref.
const mem = new Map();
globalThis.localStorage = {
  getItem: (k) => (mem.has(k) ? mem.get(k) : null),
  setItem: (k, v) => { mem.set(k, String(v)); },
  removeItem: (k) => { mem.delete(k); },
};
const { finishedMatches, noteMatchFinished } = await import('../ui/nightProgress.js');

// ================================================= 1. the shared cap contract
{
  ok(NIGHT_CAP_VERSION === 1, 'NIGHT_CAP_VERSION is 1');
  ok(makeCaps().night === 0, 'absent caps.night reads 0');
  ok(makeCaps({ night: 1 }).night === 1, 'caps.night round-trips through makeCaps');
  ok(localCaps({ night: NIGHT_CAP_VERSION, voice: VOICE_CAP_VERSION }).night === 1, 'core/caps local() passes night through');
  ok(localCaps().night === 0, 'and a caller that omits it advertises 0');
  ok(peerSpeaksNight({ night: 1 }) && peerSpeaksNight({ night: '1' }), 'a revision (quoted or not) speaks night');
  ok(!peerSpeaksNight({ night: true }) && !peerSpeaksNight({}) && !peerSpeaksNight(null), 'a boolean, absent or null does not');
}

// ================================================= 2. the frame and its clamps
{
  const f = makeDuel({ sub: 'start', idx: 2, len_s: 90 });
  ok(f.t === 'duel' && f.sub === 'start' && f.idx === 2 && f.len_s === 90, 'factory builds a start');
  ok(makeDuel({ sub: 'nope' }).sub === '', 'an unknown sub collapses to empty');
  ok(clampDuelLen(45) === DUEL_LENGTHS_SEC[0] && clampDuelLen(120) === 120 && clampDuelLen('90') === 90, 'lengths clamp to 60/90/120');
  const inbound = parse('{"t":"duel","v":1,"sub":"score","idx":-4,"score":"12.7","tile":true,"len_s":7}');
  ok(inbound && inbound.idx === 0 && inbound.score === 12 && inbound.tile === 0 && inbound.len_s === 60,
    'inbound numbers are clamped (negative, quoted, boolean, off-menu)', JSON.stringify(inbound));
  const out = JSON.parse(serialize(Object.assign(makeDuel({ sub: 'score' }), { score: -5, idx: 'x', tile: 1e12 })));
  ok(out.score === 0 && out.idx === 0 && out.tile === 10000000, 'outbound numbers are clamped too', JSON.stringify(out));
  ok(parse(serialize(makeDuel({ sub: 'score', idx: 3, score: 1234, tile: 7 }))).score === 1234, 'a sane score round-trips');
}

// ================================================= 3. the engine's gates
{
  const mk = () => new GoonMatchService({
    state: GoonTransportState.ConnectedP2P,
    send() { return Promise.resolve(true); },
    onMessageReceived() { return () => {}; },
    onStateChanged() { return () => {}; },
  }, true, { logger: quiet, tag: 'GG:duel' });

  const m = mk();
  const sent = [];
  m._send = (msg) => sent.push(msg);
  m._phase = GoonMatchPhase.Live;
  ok(m.sendDuel({ sub: 'start', idx: 0, len_s: 60 }) === false && sent.length === 0, 'no frame to a peer that never said caps.night');
  m._phase = GoonMatchPhase.Lobby;
  m._handleHello(makeHello({ caps: localCaps({ night: NIGHT_CAP_VERSION }) }));
  ok(m.peerSupportsNight === true, 'hello with caps.night >= 1 turns peerSupportsNight on');
  sent.length = 0;
  ok(m.sendDuel({ sub: 'start', idx: 0, len_s: 60 }) === false, 'pre-Live sends nothing');
  m._phase = GoonMatchPhase.Live;
  ok(m.sendDuel({ sub: 'start', idx: 0, len_s: 60 }) === true && sent[0].t === 'duel', 'Live + night peer sends');
  ok(m.sendDuel({ sub: 'bogus' }) === false, 'an unknown sub never leaves');

  const got = [];
  m.onDuelFrame((f) => got.push(f));
  m._onMessageReceived(parse(serialize(makeDuel({ sub: 'score', idx: 1, score: 9, tile: 3 }))));
  ok(got.length === 1 && got[0].score === 9, 'Live delivers an inbound duel frame');
  m._phase = GoonMatchPhase.Recap;
  m._onMessageReceived(parse(serialize(makeDuel({ sub: 'start', idx: 1 }))));
  ok(got.length === 1, 'outside Live/SuddenDeath an inbound duel frame is dropped');

  const old = mk();
  old._handleHello(makeHello({ caps: localCaps({ voice: 1 }) }));
  ok(old.peerSupportsNight === false, 'an older peer (voice but no night) does not speak night');
}

// ================================================= 4. scoring seam
{
  const s = new GoonScoring(1, 0, {});
  const before = s.score;
  s.awardBonus(DUEL_WIN_BONUS);
  ok(s.score === before + DUEL_WIN_BONUS, 'awardBonus adds to this side\'s score');
  s.awardBonus(-50); s.awardBonus('x');
  ok(s.score === before + DUEL_WIN_BONUS, 'and ignores junk');
}

// ================================================= 5. the board
{
  const b = createBoard(123n);
  b.tiles = [{ id: 1, tier: 1, r: 0, c: 0 }, { id: 2, tier: 1, r: 0, c: 1 }, { id: 3, tier: 1, r: 0, c: 2 }, { id: 4, tier: 1, r: 0, c: 3 }];
  b.nextId = 5;
  const res = move(b, 'left');
  ok(res.moved && res.merges.length === 2, 'four in a row merge into two');
  ok(boardText(b).split('\n')[0] === '2 2 . .', 'merges resolve toward the move', boardText(b));
  ok(res.score === 8 && b.score === 8, 'score = sum of new tile values');
  const m2 = move(b, 'left');
  ok(m2.moved && deepest(b) === 3 && b.tiles.length === 1, 'one merge per tile per move, then the next move merges again');
  const noop = move(b, 'left');
  ok(!noop.moved, 'a move that slides nothing is a no-op');

  const a1 = createBoard(duelSeed(0xABCDEFn, 0)); openingSpawn(a1);
  const a2 = createBoard(duelSeed(0xABCDEFn, 0)); openingSpawn(a2);
  ok(boardText(a1) === boardText(a2), 'same duel seed = same opening board');
  for (const dir of ['left', 'up', 'right', 'down', 'left', 'left', 'up']) { play(a1, dir); play(a2, dir); }
  ok(boardText(a1) === boardText(a2), 'same seed + same moves = same board');
  const c1 = createBoard(duelSeed(0xABCDEFn, 1)); openingSpawn(c1);
  ok(duelSeed(0xABCDEFn, 0) !== duelSeed(0xABCDEFn, 1), 'the next duel gets its own seed');
  ok(duelSeed(null, 0) === duelSeed(0n, 0), 'a missing match seed does not throw');

  const full = createBoard(1n);
  let id = 1;
  for (let r = 0; r < 4; r++) for (let c = 0; c < 4; c++) full.tiles.push({ id: id++, tier: ((r + c) % 2) + 1, r, c });
  ok(isLocked(full), 'a checkerboard is locked');
}

// ================================================= 6. rules
{
  const pairs = [[{ tile: 5, score: 10 }, { tile: 4, score: 999 }], [{ tile: 4, score: 50 }, { tile: 4, score: 40 }], [{ tile: 3, score: 20 }, { tile: 3, score: 20 }]];
  for (const [a, b] of pairs) {
    const x = duelOutcome(a, b); const y = duelOutcome(b, a);
    ok((x === 'win' && y === 'lose') || (x === 'lose' && y === 'win') || (x === 'tie' && y === 'tie'), 'outcome is symmetric', x + '/' + y);
  }
  ok(duelOutcome({ tile: 5, score: 1 }, { tile: 4, score: 9999 }) === 'win', 'highest tile wins before score');
  ok(duelOutcome({ tile: 1, score: 4 }, null) === 'win', 'a peer that never reported counts as 0');
  ok(duelOutcome({ tile: 0, score: 0 }, null) === 'tie', 'two empty boards tie');
  ok(bonusFor('win') === DUEL_WIN_BONUS && bonusFor('tie') === 0 && bonusFor('lose') === 0, 'only a win pays');
  ok(!cardEligible({ peerNight: true, finished: 0, inDuel: false, held: 0 }), 'first match: no game cards');
  ok(cardEligible({ peerNight: true, finished: 1, inDuel: false, held: 0 }), 'second match: game cards drop');
  ok(!cardEligible({ peerNight: false, finished: 5, inDuel: false, held: 0 }), 'never against a peer without night');
  ok(!cardEligible({ peerNight: true, finished: 5, inDuel: true, held: 0 }), 'never during a duel');
  ok(!cardEligible({ peerNight: true, finished: 5, inDuel: false, held: 1 }), 'never a second one held');
  ok(pickLength(90) === 90 && pickLength(null) === 60 && pickLength(999) === 60, 'pickLength clamps');
}

// ================================================= 7. finished-match count (stub contract)
{
  ok(finishedMatches() === 0, 'fresh device: 0 finished');
  noteMatchFinished();
  ok(finishedMatches() === 1, 'noteMatchFinished counts one');
  mem.set('goon.night.finished.v1', 'garbage');
  ok(finishedMatches() === 0, 'junk storage reads 0');
  mem.delete('goon.night.finished.v1');
}

// ================================================= 8. a whole duel, two controllers, one fake wire
{
  let now = 0;
  const timers = [];
  const later = (fn, ms) => { const t = { at: now + ms, fn, dead: false }; timers.push(t); return () => { t.dead = true; }; };
  const advance = (ms) => {
    const end = now + ms;
    for (;;) {
      timers.sort((a, b) => a.at - b.at);
      const t = timers.find((x) => !x.dead && x.at <= end);
      if (!t) break;
      now = t.at; t.dead = true; t.fn();
    }
    now = end;
  };

  const mk = (isHost) => {
    const m = new GoonMatchService({
      state: GoonTransportState.ConnectedP2P,
      send() { return Promise.resolve(true); },
      onMessageReceived() { return () => {}; },
      onStateChanged() { return () => {}; },
    }, isHost, { logger: quiet, tag: isHost ? 'GG:h' : 'GG:g' });
    m._peerSupportsNight = true;
    m._matchSeed = 0x1234567890ABCDEFn;
    m._phase = GoonMatchPhase.Live;
    return m;
  };
  const host = mk(true);
  const guest = mk(false);
  const wireLog = [];
  host._send = (msg) => { wireLog.push(msg); guest._onMessageReceived(parse(serialize(msg))); };
  guest._send = (msg) => { wireLog.push(msg); host._onMessageReceived(parse(serialize(msg))); };

  const views = { h: [], g: [] };
  const view = (tag) => new Proxy({}, { get: (_t, k) => (...a) => views[tag].push(k) });
  const H = createDuelController({ match: host, view: view('h'), now: () => now, later, finished: () => 3, duelLength: () => 90 });
  const G = createDuelController({ match: guest, view: view('g'), now: () => now, later, finished: () => 3, duelLength: () => 120 });

  ok(wireLog.some((f) => f.sub === 'cfg' && f.len_s === 90), 'host announces its duel length at Live');
  ok(H.arsenalHook.eligible(0) && H.arsenalHook.visible(), 'second match against a night peer: a card can drop');

  // The GUEST throws: the host's pick (90) must win over the guest's own (120).
  ok(G.arsenalHook.throwCard() === true, 'guest throws a game card');
  ok(G.state && H.state && G.state.idx === 0 && H.state.idx === 0, 'both sides are in duel 0');
  ok(G.state.len === 90 && H.state.len === 90, "the host's length wins", `${G.state.len}/${H.state.len}`);
  ok(H.busy() && G.busy() && !H.arsenalHook.eligible(0), 'throws pause and no card drops mid-duel');
  ok(H.state.stage === 'intro' && views.h.includes('intro'), 'both show the incoming card first');
  ok(G.arsenalHook.throwCard() === false, 'a second card cannot start over a running duel');

  advance(DUEL_INTRO_MS + 1);
  ok(H.state.stage === 'play' && G.state.stage === 'play', 'then the board');
  ok(boardText(H.board) === boardText(G.board), 'same board on both screens');

  // Host plays well, guest does nothing.
  for (let i = 0; i < 40; i++) for (const dir of ['left', 'down', 'right', 'down']) H.input(dir);
  ok(H.board.score > 0, 'host scored on its board');
  const hostBefore = host.scoring.score;
  const guestBefore = guest.scoring.score;

  advance(90 * 1000 + 300);
  ok(H.state && H.state.stage === 'result' && G.state && G.state.stage === 'result', 'both resolved once both scores crossed');
  ok(host.scoring.score === hostBefore + DUEL_WIN_BONUS, 'the winner gets the bonus', `${host.scoring.score}`);
  ok(guest.scoring.score === guestBefore, 'the loser gets nothing');
  ok(duelSummary().won >= 0, 'the recap summary is readable');
  advance(5000);
  ok(!H.busy() && !G.busy(), 'the overlay closes and throws come back');

  // A silent peer: the host throws, the guest never reports.
  guest._send = () => {};   // the guest's frames vanish
  ok(H.arsenalHook.throwCard() === true && H.state.idx === 1 && G.state && G.state.idx === 1, 'duel 1 on the next index');
  advance(DUEL_INTRO_MS + 90 * 1000 + 300);
  ok(H.state.stage === 'wait', 'host waits for a report that never comes');
  const before = host.scoring.score;
  H.input('left');
  advance(DUEL_REPORT_GRACE_MS + 10);
  ok(H.state.stage === 'result', 'the grace window resolves it');
  ok(host.scoring.score >= before, 'silence counts as 0 (host board >= 0)');
  advance(5000);

  // Mercy mid-duel: leaving Live ends the duel with no bonus.
  host._send = (msg) => { guest._onMessageReceived(parse(serialize(msg))); };
  guest._send = (msg) => { host._onMessageReceived(parse(serialize(msg))); };
  H.arsenalHook.throwCard();
  advance(DUEL_INTRO_MS + 1);
  const s0 = host.scoring.score;
  host._phase = GoonMatchPhase.Recap;
  host._ev.phaseChanged.emit(GoonMatchPhase.Recap, () => {});
  ok(!H.busy() && host.scoring.score === s0, 'leaving Live closes the duel, no bonus');

  H.dispose(); G.dispose();
}

// ================================================= 9. the arsenal slot (headless)
{
  const fakeMatch = { phase: GoonMatchPhase.Live, localCaps: null, onPhaseChanged() { return () => {}; }, onPayloadReceiptReceived() { return () => {}; } };
  const noDuel = mountArsenal({ match: fakeMatch });
  ok(!noDuel.droppable().some((c) => c.id === 'gamecard'), 'no duel hook, no game card in the drop pool');
  noDuel.unmount();

  let eligible = false;
  let busy = false;
  let thrown = 0;
  const hook = { eligible: () => eligible, visible: () => true, busy: () => busy, throwCard: () => { thrown++; return true; }, firstHint: () => false };
  // mountArsenal builds tiles only with a host node; headless it builds none, so exercise the pool rule via a stub DOM.
  const stubNode = () => ({ appendChild() {}, setAttribute() {}, classList: { add() {}, remove() {}, toggle() {} }, style: {}, remove() {}, addEventListener() {}, removeEventListener() {} });
  globalThis.document = { createElement: () => stubNode(), body: stubNode(), activeElement: null, addEventListener() {}, removeEventListener() {} };
  const host = stubNode();
  const ars = mountArsenal({ match: fakeMatch, leftHost: host, rightHost: host, duel: hook });
  ok(!ars.droppable().some((c) => c.id === 'gamecard'), 'not eligible: the card is not in the pool');
  eligible = true;
  const pool = ars.droppable();
  const card = pool.find((c) => c.id === 'gamecard');
  ok(!!card, 'eligible: the card joins the pool');
  let total = 0; for (const c of pool) total += 1 / Math.pow(Math.max(1, c.cost), 1.6);
  const share = (1 / Math.pow(card.cost, 1.6)) / total;
  ok(share > 0.02 && share < 0.08, 'the card is a rare drop', (share * 100).toFixed(1) + '%');
  ok(pickDrop([card], 0.5).id === 'gamecard', 'the roller can pick it');
  ok(ars.armDrop('gamecard', { silent: true }) === true && ars.armDrop('gamecard', { silent: true }) === false, 'one card held at a time');
  busy = true;
  ok(ars.fire('flash').ok === false, 'during a duel other throws refuse');
  busy = false;
  const r = ars.fire('gamecard');
  ok(r.ok && thrown === 1 && ars.armedCount('gamecard') === 0, 'throwing the card starts a duel and spends it');
  ars.unmount();
  delete globalThis.document;
}

console.log(failures === 0 ? `PASS - ${n} checks` : `FAILED - ${failures}/${n} checks`);
process.exit(failures === 0 ? 0 : 1);
