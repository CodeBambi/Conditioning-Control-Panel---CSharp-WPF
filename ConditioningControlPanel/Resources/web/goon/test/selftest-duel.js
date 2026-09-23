// Self-contained sanity pass over GAME NIGHT DUELS (the Deep End game card).
//
//   node Resources/web/goon/test/selftest-duel.js
//
// Pins: the shared cap contract (caps.night, peerSpeaksNight), the `t:'duel'` frame's clamps in
// both directions (now with the `game` id), the engine's send/receive gates, the games table (which
// real Arcademy class a card holds), the fenced arcademy stylesheet, the host's pure seams (the deck
// as a provider manifest, the in-memory store), a symmetric winner for tiled and untiled games, the
// drop eligibility rule, the arsenal slot, and a full two-controller duel over a fake wire with a
// virtual clock and a FAKE class runner (the real class needs a DOM: the CDP shot proves that half).

import { serialize, parse } from '../core/wire.js';
import {
  makeCaps, makeDuel, makeHello, GoonMatchPhase, GoonTransportState,
  NIGHT_CAP_VERSION, VOICE_CAP_VERSION, peerSpeaksNight, clampDuelLen, DUEL_LENGTHS_SEC,
} from '../core/contracts.js';
import { local as localCaps } from '../core/caps.js';
import { GoonMatchService } from '../core/match.js';
import { GoonScoring } from '../core/scoring.js';
import {
  DUEL_GAMES, DEFAULT_DUEL_GAME, duelGame, knownGame, normalizeGameId, pickDuelGame, botResult,
} from '../ui/duel/games.js';
import { scopeArcCss } from '../ui/duel/arcCss.js';
import { manifestFromDeck, memoryStore } from '../ui/duel/arcademyHost.js';
import {
  duelSeed, duelOutcome, bonusFor, cardEligible, pickLength, DUEL_WIN_BONUS, DUEL_INTRO_MS, DUEL_REPORT_GRACE_MS,
} from '../ui/duel/rules.js';
import { createDuelController, duelSummary, DUEL_GAP_EXTRA_MS, DUEL_MAX_PER_MATCH } from '../ui/duel/duelController.js';
import { createBotDuel } from '../ui/duel/botDuel.js';
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
  ok(out.score === 0 && out.idx === 0 && out.tile === 17, 'outbound numbers are clamped too (tile tops out at 17)', JSON.stringify(out));
  const big = parse('{"t":"duel","v":1,"sub":"score","idx":1,"score":99999999,"tile":40}');
  ok(big.score === 1000000 && big.tile === 17, 'score caps at 1,000,000 and tile at 17, separately', JSON.stringify(big));
  ok(parse('{"t":"duel","v":1,"sub":"busy","idx":2}').sub === 'busy', 'busy is a known sub');
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
  const g2 = mk.call(null);
  Object.defineProperty(g2, '_isHost', { value: false, writable: true });
  g2._phase = GoonMatchPhase.Countdown;
  g2._onMessageReceived(parse(serialize(makeDuel({ sub: 'cfg', len_s: 120 }))));
  ok(g2.peerDuelLen === 120, "the host's cfg is kept in Countdown too");
  g2._onMessageReceived(parse(serialize(makeDuel({ sub: 'start', idx: 0, len_s: 90 }))));
  ok(g2.peerDuelLen === 120, 'but a start in Countdown is dropped');

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

// ================================================= 5. the games table and the host's pure seams
{
  ok(DUEL_GAMES.some((g) => g.id === 'the-deep-end') && DUEL_GAMES.some((g) => g.id === 'sort'), 'the table holds The Deep End and Sort');
  ok(DEFAULT_DUEL_GAME === 'the-deep-end' && normalizeGameId(undefined) === 'the-deep-end' && normalizeGameId('') === 'the-deep-end',
    'a frame with no game id means The Deep End (older builds)');
  ok(knownGame('sort') && knownGame(null) && !knownGame('instant-recall-2099'), 'knownGame: table ids and the default, nothing else');
  for (const g of DUEL_GAMES) {
    ok(typeof g.path === 'string' && g.path.indexOf('../../../arcademy/games/' + g.id + '/') === 0, 'each row points at the real class', g.id);
    ok(typeof g.result === 'function' && typeof g.name === 'string' && g.name.length > 0, 'each row reads its own result', g.id);
  }
  const deep = duelGame('the-deep-end');
  ok(deep.tiled === true && duelGame('sort').tiled === false, 'The Deep End is tiled, Sort is not');
  const snap = { snapshot: () => ({ bestDeepest: 7, score: 412 }) };
  const dr = deep.result(snap);
  ok(dr.tile === 7 && dr.score === 412, 'the Deep End result is its own best tier + score', JSON.stringify(dr));
  const sortRow = duelGame('sort');
  const lib = { gradeClass: (i) => ({ composite: i.correct / (i.correct + i.wrong) }) };
  const sr = sortRow.result({ diagnostics: () => ({ live: true, correct: 3, wrong: 1, perfect: 0, passed: 0, bestRung: 1, rungCap: 8, longestChain: 3 }) }, { lib });
  ok(Math.round(sr.score) === 750, 'the Sort result is its own composite x1000', JSON.stringify(sr));
  ok(sortRow.result({ diagnostics: () => ({ live: false }) }, { lib }).score === 0, 'a Sort class that never started scores 0');

  const picks = new Set();
  for (let i = 0; i < 6; i++) picks.add(pickDuelGame(0xABCDEFn, i));
  ok(picks.size === DUEL_GAMES.length, 'cards alternate across the whole table', [...picks].join(','));
  ok(pickDuelGame(0xABCDEFn, 3) === pickDuelGame(0xABCDEFn, 3) && pickDuelGame(null, 0) === pickDuelGame(0n, 0), 'the pick is deterministic and null-safe');

  let x = 99;
  const rand = () => { x = (Math.imul(x, 1664525) + 1013904223) >>> 0; return x / 4294967296; };
  const tiles = []; const scores = [];
  for (let i = 0; i < 40; i++) { const r = botResult('the-deep-end', 60, rand); tiles.push(r.tile); scores.push(r.score); }
  ok(Math.min(...tiles) >= 5 && Math.max(...tiles) <= 7 && Math.min(...scores) > 0, 'the bot reports a believable Deep End result', tiles.join(''));
  const s1 = botResult('sort', 60, rand);
  ok(s1.game === 'sort' && s1.tile === undefined && s1.score >= 320 && s1.score <= 760, 'and a believable Sort result', JSON.stringify(s1));
  ok(botResult('nope', 60, () => 0.5).game === 'the-deep-end', 'an unknown game falls back to the default row');

  ok(duelSeed(0xABCDEFn, 0) !== duelSeed(0xABCDEFn, 1), 'the next duel gets its own seed');
  ok(duelSeed(null, 0) === duelSeed(0n, 0), 'a missing match seed does not throw');

  // The fenced arcademy sheet: palette + class rules scoped, the page's own html/body rules dropped.
  const css = ':root { --pink:#f0f; }\nhtml, body { margin:0 }\nbody { color:red }\n.arc-stamp { color:var(--pink) }\n'
    + '.btn.primary, #arc-fx { x:1 }\n@keyframes arc-pop { from { opacity:0 } }\n@keyframes other { from { opacity:0 } }\n'
    + '@media (max-width: 600px) { .arc-meter { gap:2px } body { y:1 } }\n@font-face { src:url(fonts/a.woff2) }';
  const out = scopeArcCss(css, '.gg-arc', 'https://ccp.game/arcademy/styles.css');
  ok(out.includes('.gg-arc{') && out.includes('--pink:#f0f'), ':root becomes the scope (the palette)', out);
  ok(out.includes('.gg-arc .arc-stamp{') && out.includes('.gg-arc .btn.primary'), 'class rules are fenced under the scope');
  ok(!/(^|[\s,])(html|body)\b/.test(out.replace(/\.gg-arc/g, '')) && !out.includes('#arc-fx'), 'html, body and ids never leak onto the goon page');
  ok(out.includes('@keyframes arc-pop') && !out.includes('@keyframes other'), 'arc- keyframes kept, others dropped');
  ok(out.includes('@media (max-width: 600px){') && out.includes('.gg-arc .arc-meter'), 'media blocks are filtered the same way');
  ok(out.includes('https://ccp.game/arcademy/fonts/a.woff2'), 'font urls are made absolute to the sheet');

  const man = manifestFromDeck([
    { kind: 'image', url: 'https://ccp.assets/a.jpg' }, { kind: 'video', url: 'https://ccp.assets/.temp/c.webm', clip: true },
    { kind: 'image', url: 'blob:http://x/1' }, null, { kind: 'image' },
  ]);
  ok(man.length === 3 && man[0] === 'https://ccp.assets/a.jpg', 'an image with an extension is the provider\'s to classify');
  ok(man[1].kind === 'loop' && man[2].kind === 'still', 'videos deal as loops, extensionless blobs as stills', JSON.stringify(man));

  const st = memoryStore();
  st.mergeGameMeta('sort', { bestChain: 4 });
  st.mergeGameMeta('sort', { bestRung: 2 });
  const gm = st.gameMeta('sort');
  ok(gm.bestChain === 4 && gm.bestRung === 2 && st.gameMeta('x') && Object.keys(st.gameMeta('x')).length === 0, 'the duel store keeps meta in memory only');
  gm.bestChain = 99;
  ok(st.gameMeta('sort').bestChain === 4, 'and hands out copies');

  const df = makeDuel({ sub: 'start', idx: 1, len_s: 60, game: 'sort' });
  ok(df.game === 'sort', 'the start frame carries the game id');
  ok(makeDuel({ sub: 'start', game: 'Sort; drop' }).game === '' && makeDuel({ sub: 'start' }).game === '', 'a junk or missing game id clamps to empty');
  const round = parse(serialize(makeDuel({ sub: 'score', idx: 0, score: 7, tile: 0, game: 'sort' })));
  ok(round.game === 'sort', 'the game id survives the wire');
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
  ok(duelOutcome({ game: 'sort', score: 500 }, { game: 'sort', score: 400 }) === 'win', 'an untiled game is score alone');
  ok(duelOutcome({ game: 'sort', tile: 1, score: 400 }, { game: 'sort', tile: 9, score: 500 }) === 'lose', 'a stray tile never decides an untiled game');
  ok(duelOutcome({ game: 'the-deep-end', tile: 6, score: 1 }, { tile: 5, score: 900 }) === 'win', 'The Deep End stays tile first, then score');
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
  // The class runner: the real one mounts an Arcademy class (a DOM), this one is scripted.
  const runs = { h: [], g: [] };
  const runner = (tag) => (spec) => {
    const r = { spec, tile: 0, score: 0, destroyed: 0, result() { return { game: spec.game, tile: this.tile, score: this.score }; }, destroy() { this.destroyed++; } };
    runs[tag].push(r);
    return r;
  };
  const H = createDuelController({ match: host, view: view('h'), runGame: runner('h'), now: () => now, later, finished: () => 3, duelLength: () => 90 });
  const G = createDuelController({ match: guest, view: view('g'), runGame: runner('g'), now: () => now, later, finished: () => 3, duelLength: () => 120 });

  ok(wireLog.some((f) => f.sub === 'cfg' && f.len_s === 90), 'host announces its duel length at Live');
  ok(H.arsenalHook.eligible(0) && H.arsenalHook.visible(), 'second match against a night peer: a card can drop');

  // The GUEST throws: the host's pick (90) must win over the guest's own (120).
  ok(G.arsenalHook.throwCard({ game: 'the-deep-end' }) === true, 'guest throws a game card');
  ok(wireLog.some((f) => f.sub === 'start' && f.game === 'the-deep-end'), 'the start frame names the game');
  ok(G.state && H.state && G.state.idx === 0 && H.state.idx === 0, 'both sides are in duel 0');
  ok(G.state.len === 90 && H.state.len === 90, "the host's length wins", `${G.state.len}/${H.state.len}`);
  ok(H.busy() && G.busy() && !H.arsenalHook.eligible(0), 'throws pause and no card drops mid-duel');
  ok(H.state.stage === 'intro' && views.h.includes('intro'), 'both show the incoming card first');
  ok(G.arsenalHook.throwCard() === false, 'a second card cannot start over a running duel');

  advance(DUEL_INTRO_MS + 1);
  ok(H.state.stage === 'play' && G.state.stage === 'play', 'then the game');
  ok(runs.h.length === 1 && runs.g.length === 1 && H.run === runs.h[0], 'each side started its class');
  ok(runs.h[0].spec.game === 'the-deep-end' && runs.g[0].spec.game === 'the-deep-end', 'the same game on both screens');
  ok(runs.h[0].spec.seed === runs.g[0].spec.seed && /^goon-duel\|\d+$/.test(runs.h[0].spec.seed), 'the same seed on both screens', runs.h[0].spec.seed);
  ok(runs.h[0].spec.len === 90 && typeof runs.h[0].spec.onEnd === 'function', 'the class gets the duel length and an end hook');
  ok(views.h.includes('play'), 'the view opens its stage');

  // Host plays well, guest does nothing.
  runs.h[0].tile = 7; runs.h[0].score = 640;
  const hostBefore = host.scoring.score;
  const guestBefore = guest.scoring.score;

  advance(90 * 1000 + 300);
  ok(H.state && H.state.stage === 'result' && G.state && G.state.stage === 'result', 'both resolved once both scores crossed');
  ok(runs.h[0].destroyed >= 1 && runs.g[0].destroyed >= 1 && !H.run, 'the duel bell tears both classes down');
  ok(wireLog.some((f) => f.sub === 'score' && f.tile === 7 && f.score === 640 && f.game === 'the-deep-end'), 'the host reports its class\'s own result');
  ok(host.scoring.score === hostBefore + DUEL_WIN_BONUS, 'the winner gets the bonus', `${host.scoring.score}`);
  ok(guest.scoring.score === guestBefore, 'the loser gets nothing');
  ok(duelSummary().won >= 0, 'the recap summary is readable');
  advance(5000);
  ok(!H.busy() && !G.busy(), 'the overlay closes and throws come back');

  // The gap: no new duel until the last one's length + 30 s has passed.
  ok(H.arsenalHook.throwCard() === false && !H.arsenalHook.eligible(0), 'right after a duel the card is gated (len + 30 s)');
  advance(90 * 1000 + DUEL_GAP_EXTRA_MS);
  // A silent peer: the host throws, the guest never reports. Silence is a TIE, never a win.
  guest._send = () => {};   // the guest's frames vanish
  ok(H.arsenalHook.throwCard() === true && H.state.idx === 1 && G.state && G.state.idx === 1, 'duel 1 on the next index');
  advance(DUEL_INTRO_MS + 90 * 1000 + 300);
  ok(H.state.stage === 'wait', 'host waits for a report that never comes');
  const before = host.scoring.score;
  const tiedBefore = duelSummary(host).tied;
  advance(DUEL_REPORT_GRACE_MS + 10);
  ok(H.state.stage === 'result', 'the grace window resolves it');
  ok(host.scoring.score === before && duelSummary(host).tied === tiedBefore + 1, 'a missing report is a tie: no bonus');
  advance(5000);
  ok(duelSummary(host).won === 1, 'the summary is keyed on the match object', JSON.stringify(duelSummary(host)));
  advance(90 * 1000 + DUEL_GAP_EXTRA_MS);

  // Mercy mid-duel: leaving Live ends the duel with no bonus.
  host._send = (msg) => { wireLog.push(msg); guest._onMessageReceived(parse(serialize(msg))); };
  guest._send = (msg) => { wireLog.push(msg); host._onMessageReceived(parse(serialize(msg))); };
  H.arsenalHook.throwCard({ game: 'sort' });
  advance(DUEL_INTRO_MS + 1);
  const live = runs.h[runs.h.length - 1];
  ok(live.spec.game === 'sort' && G.state && G.state.game === 'sort', 'a Sort card plays Sort on both sides');
  const s0 = host.scoring.score;
  host._phase = GoonMatchPhase.Recap;
  host._ev.phaseChanged.emit(GoonMatchPhase.Recap, () => {});
  ok(!H.busy() && host.scoring.score === s0, 'leaving Live closes the duel, no bonus');
  ok(live.destroyed >= 1, 'and tears the class down');
  host._phase = GoonMatchPhase.Live;
  guest._phase = GoonMatchPhase.Recap;
  guest._ev.phaseChanged.emit(GoonMatchPhase.Recap, () => {});
  guest._phase = GoonMatchPhase.Live;

  // The class ends on its own (a Deep End ceiling): its end is our end, before the duel clock.
  advance(90 * 1000 + DUEL_GAP_EXTRA_MS);
  ok(H.arsenalHook.throwCard({ game: 'the-deep-end' }) === true, 'another duel');
  advance(DUEL_INTRO_MS + 1);
  const early = runs.h[runs.h.length - 1];
  early.tile = 11; early.score = 9000;
  early.spec.onEnd({ metrics: { composite: 1 } });
  ok(H.state && H.state.stage !== 'play', 'the class ringing its own bell ends this side at once', JSON.stringify(H.state));
  ok(wireLog.some((f) => f.sub === 'score' && f.tile === 11), 'and reports the result it ended on');

  // An unknown game id is refused like any other bad start: the thrower gets its card back.
  advance(2 * (90 * 1000 + DUEL_GAP_EXTRA_MS) + DUEL_REPORT_GRACE_MS + 10000);
  ok(!H.busy() && !G.busy(), 'both sides are quiet again', JSON.stringify([H.state, G.state]));
  const nIdx = G.nextIdx;
  const before2 = wireLog.length;
  host._send(makeDuel({ sub: 'start', idx: nIdx, len_s: 60, game: 'instant-recall-2099' }));
  ok(wireLog.slice(before2).some((f) => f.sub === 'busy' && f.idx === nIdx) && !G.busy(), 'an unknown game is refused with busy');

  // An old frame with no game id means The Deep End.
  const before3 = runs.g.length;
  host._send({ t: 'duel', sub: 'start', idx: nIdx, len_s: 60 });
  advance(DUEL_INTRO_MS + 1);
  ok(G.state && G.state.game === 'the-deep-end' && runs.g.length === before3 + 1 && runs.g[before3].spec.game === 'the-deep-end',
    'a start without a game id plays The Deep End', JSON.stringify(G.state));

  H.dispose(); G.dispose();
}

// ================================================= 8b. the receiver's gates, busy, collisions, practice
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
    }, isHost, { logger: quiet, tag: isHost ? 'GG:h2' : 'GG:g2' });
    m._peerSupportsNight = true;
    m._matchSeed = 77n;
    m._phase = GoonMatchPhase.Live;
    return m;
  };
  /* A wire that can be HELD: frames queue until flush(), so two throws can cross. */
  function pair({ hostFinished = 3, guestFinished = 3, guestPractice = false } = {}) {
    const host = mk(true);
    const guest = mk(false);
    const queue = [];
    let held = false;
    const deliver = (to, msg) => { const f = parse(serialize(msg)); if (held) queue.push([to, f]); else to._onMessageReceived(f); };
    const log = [];
    host._send = (msg) => { log.push(['h', msg]); deliver(guest, msg); };
    guest._send = (msg) => { log.push(['g', msg]); deliver(host, msg); };
    let hostCard = 0; let guestCard = 0;
    const H = createDuelController({ match: host, now: () => now, later, finished: () => hostFinished, duelLength: () => 120 });
    const G = createDuelController({ match: guest, now: () => now, later, finished: () => guestFinished, duelLength: () => 60, isPractice: () => guestPractice });
    H.setReturnCard(() => { hostCard++; });
    G.setReturnCard(() => { guestCard++; });
    return {
      host, guest, H, G, log,
      hold() { held = true; },
      flush() { held = false; while (queue.length) { const [to, f] = queue.shift(); to._onMessageReceived(f); } },
      get hostCard() { return hostCard; }, get guestCard() { return guestCard; },
    };
  }

  // 1. the RECEIVER's own gate: first match on the receiving side -> busy, card back.
  {
    const p = pair({ guestFinished: 0 });
    ok(p.H.arsenalHook.throwCard() === true, 'host throws');
    ok(!p.G.busy(), 'a receiver on its first match refuses the start');
    ok(p.log.some(([who, f]) => who === 'g' && f.sub === 'busy' && f.idx === 0), 'and answers busy');
    ok(!p.H.busy() && p.hostCard === 1 && p.H.nextIdx === 0, 'the thrower cancels, gets the card back and rolls its idx back');
    p.H.dispose(); p.G.dispose();
  }
  // 1. wrong idx -> busy
  {
    const p = pair();
    p.host._onMessageReceived(parse(serialize(makeDuel({ sub: 'start', idx: 3, len_s: 60 }))));
    ok(!p.H.busy() && p.log.some(([who, f]) => who === 'h' && f.sub === 'busy' && f.idx === 3), 'a start with an unexpected idx is refused');
    p.H.dispose(); p.G.dispose();
  }
  // 1. the gap and the per-match cap, on the receiver
  {
    const p = pair();
    p.H.arsenalHook.throwCard();
    advance(DUEL_INTRO_MS + 120 * 1000 + DUEL_REPORT_GRACE_MS + 3000);
    ok(!p.H.busy() && !p.G.busy(), 'duel 0 done on both sides');
    // A forged/early start from the peer inside the gap is refused by the receiver.
    p.guest._onMessageReceived(parse(serialize(makeDuel({ sub: 'start', idx: 1, len_s: 60 }))));
    ok(!p.G.busy() && p.log.some(([who, f]) => who === 'g' && f.sub === 'busy' && f.idx === 1), 'a start inside the gap is refused');
    let played = 1;
    for (let i = 0; i < 10; i++) {
      advance(120 * 1000 + DUEL_GAP_EXTRA_MS + 10);
      if (!p.H.arsenalHook.throwCard()) break;
      played++;
      advance(DUEL_INTRO_MS + 120 * 1000 + DUEL_REPORT_GRACE_MS + 3000);
    }
    ok(played === DUEL_MAX_PER_MATCH, 'a match holds at most five duels', String(played));
    p.guest._onMessageReceived(parse(serialize(makeDuel({ sub: 'start', idx: DUEL_MAX_PER_MATCH, len_s: 60 }))));
    ok(!p.G.busy(), 'and the receiver refuses a sixth even at the right idx');
    p.H.dispose(); p.G.dispose();
  }
  // 2. busy while the receiver is holding a result
  {
    const p = pair();
    p.H.arsenalHook.throwCard();
    advance(DUEL_INTRO_MS + 120 * 1000 + 300);    // both reported, both in the result hold
    ok(p.G.state && p.G.state.stage === 'result', 'guest is in its result hold');
    p.guest._onMessageReceived(parse(serialize(makeDuel({ sub: 'start', idx: 1, len_s: 60 }))));
    ok(p.G.state.idx === 0 && p.log.some(([who, f]) => who === 'g' && f.sub === 'busy' && f.idx === 1), 'a start during the result hold gets busy');
    p.H.dispose(); p.G.dispose();
  }
  // 2. practice (owner, 2026-09-23): the card drops and duels run from the FIRST practice match
  {
    const p = pair({ guestPractice: true, guestFinished: 0 });
    ok(p.G.arsenalHook.eligible(0) && p.G.arsenalHook.visible(), 'practice shows and drops the card on the first match');
    ok(p.H.arsenalHook.throwCard() === true && p.G.busy() && p.hostCard === 0, 'a start into practice is played, not refused');
    p.H.dispose(); p.G.dispose();
  }
  {
    // ...while a REAL peer on its first match still shows no card (the rule practice skips is unchanged).
    const p = pair({ guestPractice: false, guestFinished: 0 });
    ok(!p.G.arsenalHook.eligible(0) && !p.G.arsenalHook.visible(), 'a real first match still shows no card');
    p.H.dispose(); p.G.dispose();
  }
  // 3. collision: both throw at once, the host's start and length win
  {
    const p = pair();
    p.hold();
    ok(p.H.arsenalHook.throwCard() && p.G.arsenalHook.throwCard(), 'both throw before either frame lands');
    ok(p.H.state.len === 120 && p.G.state.len === 120, "the guest already carries the host's cfg length", p.G.state.len + '');
    p.flush();
    ok(p.H.state && p.G.state && p.H.state.idx === 0 && p.G.state.idx === 0, 'both stay in duel 0');
    ok(!p.log.some(([, f]) => f.sub === 'busy'), 'a collision is not a refusal');
    p.H.dispose(); p.G.dispose();
  }
  {
    // A guest that never got the cfg starts on its own length (60); the host's start (120) wins.
    const p = pair();
    p.guest._peerDuelLen = 0;
    p.hold();
    ok(p.H.arsenalHook.throwCard() && p.G.arsenalHook.throwCard(), 'crossing throws without a cfg');
    ok(p.G.state.len === 60, 'the guest started on its own length', String(p.G.state.len));
    p.flush();
    ok(p.H.state && p.G.state && p.H.state.idx === p.G.state.idx, 'both stay in the SAME duel');
    ok(p.G.state.len === 120 && p.H.state.len === 120, "the guest adopts the host's start and length", p.G.state.len + '/' + p.H.state.len);
    ok(!p.log.some(([, f]) => f.sub === 'busy'), 'still no refusal');
    p.H.dispose(); p.G.dispose();
  }
}

// ================================================= 8c. the practice bot plays duels (ui/duel/botDuel.js)
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
    }, isHost, { logger: quiet, tag: isHost ? 'GG:ph' : 'GG:pbot' });
    m._peerSupportsNight = true;
    m._matchSeed = 4242n;
    m._phase = GoonMatchPhase.Live;
    return m;
  };
  /** The player's HOST controller (practice, first match ever) against the bot on the guest match. */
  function practicePair(seed = 1, { hold = false } = {}) {
    const host = mk(true);
    const guest = mk(false);
    const log = [];
    const queue = [];
    let held = hold;
    const deliver = (to, msg) => { const f = parse(serialize(msg)); if (held) queue.push([to, f]); else to._onMessageReceived(f); };
    host._send = (msg) => { log.push(['h', msg]); deliver(guest, msg); };
    guest._send = (msg) => { log.push(['bot', msg]); deliver(host, msg); };
    let x = seed >>> 0;
    const rand = () => { x = (Math.imul(x, 1664525) + 1013904223) >>> 0; return x / 4294967296; };
    let card = 0;
    const result = { tile: 0, score: 0 };
    const runGame = (spec) => ({ result: () => ({ game: spec.game, tile: result.tile, score: result.score }), destroy() {} });
    const H = createDuelController({ match: host, runGame, now: () => now, later, finished: () => 0, duelLength: () => 60, isPractice: () => true });
    H.setReturnCard(() => { card++; });
    const B = createBotDuel({ match: guest, rand, now: () => now, later, throws: false });
    return {
      host, guest, H, B, log, result,
      flush() { held = false; while (queue.length) { const [to, f] = queue.shift(); to._onMessageReceived(f); } },
      get card() { return card; },
    };
  }
  const booked = (m) => { const s = duelSummary(m); return s.won + s.lost + s.tied; };

  {
    const p = practicePair(7);
    ok(p.H.arsenalHook.visible() && p.H.arsenalHook.eligible(0), 'practice, first match: the game card is live');
    ok(p.H.arsenalHook.throwCard({ game: 'the-deep-end' }) === true, 'the player throws a duel at the bot');
    ok(p.B.busy && p.B.state.idx === 0 && p.B.state.len === 60, 'the bot accepts the next idx at the host length', JSON.stringify(p.B.state));
    ok(!p.log.some(([w, f]) => w === 'bot' && f.sub === 'busy'), 'accepting is silence (no busy frame)');
    advance(DUEL_INTRO_MS + 60 * 1000);
    ok(p.H.state && p.H.state.stage === 'wait', 'the player board closed and waits for the bot', JSON.stringify(p.H.state));
    advance(1500);   // BOT_REPORT_MS tops out at 1400, well inside DUEL_REPORT_GRACE_MS
    const score = p.log.find(([w, f]) => w === 'bot' && f.sub === 'score');
    ok(!!score && score[1].idx === 0, 'the bot reports its score inside the grace window');
    ok(!!score && score[1].tile >= 2 && score[1].score > 0 && score[1].game === 'the-deep-end', 'the bot reports a believable result for the card\'s game', JSON.stringify(score && score[1]));
    ok(p.H.state && p.H.state.stage === 'result', 'the result lands through the same controller', JSON.stringify(p.H.state));
    const sum = duelSummary(p.host);
    ok(booked(p.host) === 1 && sum.lost === 1, 'an idle player (no moves) loses to the bot, booked in the recap tally', JSON.stringify(sum));
    advance(5000);
    ok(!p.H.busy() && !p.B.busy, 'both sides close');
    ok(p.H.arsenalHook.throwCard() === false, 'the player side keeps the gap');
    p.host._send(makeDuel({ sub: 'start', idx: 1, len_s: 60 }));
    ok(p.log.some(([w, f]) => w === 'bot' && f.sub === 'busy' && f.idx === 1), 'the bot refuses a start inside its gap');
    p.H.dispose(); p.B.dispose();
  }
  {
    const p = practicePair(3);
    p.host._send(makeDuel({ sub: 'start', idx: 4, len_s: 60 }));
    ok(p.log.some(([w, f]) => w === 'bot' && f.sub === 'busy' && f.idx === 4) && !p.B.busy, 'the bot refuses an unexpected idx');
    p.H.dispose(); p.B.dispose();
  }
  {
    // A steady player wins some and loses some: the bot is beatable and not a pushover.
    let wins = 0; let losses = 0;
    for (let s = 1; s <= 24; s++) {
      const p = practicePair(s * 97);
      p.H.arsenalHook.throwCard({ game: s % 2 ? 'the-deep-end' : 'sort' });
      advance(DUEL_INTRO_MS + 10);
      // A steady player: Deep End tier 5..8, Sort composite .3...8.
      p.result.tile = 5 + (s % 4);
      p.result.score = s % 2 ? 250 + (s * 37) % 900 : 300 + (s * 53) % 500;
      advance(60 * 1000 + DUEL_REPORT_GRACE_MS + 5000);
      const sum = duelSummary(p.host);
      wins += sum.won; losses += sum.lost;
      p.H.dispose(); p.B.dispose();
    }
    ok(wins > 0 && losses > 0, 'a steady player wins some and loses some against the bot', wins + 'W ' + losses + 'L');
  }
  {
    const p = practicePair(11);
    ok(p.B.tryThrow() === true && p.B.state.by === 'bot', 'the bot throws a card');
    ok(!!p.H.state && p.H.state.by === 'them' && p.H.state.idx === 0, 'the player sees an incoming duel', JSON.stringify(p.H.state));
    advance(DUEL_INTRO_MS + 60 * 1000 + DUEL_REPORT_GRACE_MS + 5000);
    ok(!p.H.busy() && !p.B.busy && booked(p.host) === 1, 'and it resolves');
    p.H.dispose(); p.B.dispose();
  }
  {
    const p = practicePair(5, { hold: true });
    ok(p.H.arsenalHook.throwCard() && p.B.tryThrow(), 'player and bot throw before either frame lands');
    p.flush();
    ok(!!p.H.state && !!p.B.state && p.H.state.idx === 0 && p.B.state.idx === 0 && p.B.state.by === 'them', 'both stay in duel 0, the bot yields');
    ok(!p.log.some(([, f]) => f.sub === 'busy'), 'a collision is not a refusal');
    advance(DUEL_INTRO_MS + 60 * 1000 + DUEL_REPORT_GRACE_MS + 5000);
    ok(booked(p.host) === 1, 'exactly one duel booked');
    p.H.dispose(); p.B.dispose();
  }
  {
    // Wiring: the solo driver builds the bot, boot's practice opponent speaks night and holds a card.
    const fs = await import('node:fs');
    const solo = fs.readFileSync(new URL('../ui/soloDriver.js', import.meta.url), 'utf8');
    const boot = fs.readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
    ok(solo.includes('createBotDuel('), 'ui/soloDriver.js builds the bot duel side');
    ok(!boot.includes("displayName: 'Practice', night: false"), 'boot no longer mutes night on the practice bot');
    ok(boot.includes("armDrop('gamecard'"), 'practice seeds a game card at Live');
  }
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
