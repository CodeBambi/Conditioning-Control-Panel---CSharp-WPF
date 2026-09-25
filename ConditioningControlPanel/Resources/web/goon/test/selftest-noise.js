// Self-contained pass over the SORT DUEL'S VS REVEAL + NOISE PICK (2026-09-25).
//
//   node Resources/web/goon/test/selftest-noise.js
//
// Pins: the noise table (and its C# twin), the `sub:'noise'` frame (known ids only, byte-identical
// old frames), the night revision 2 gate, two controllers through reveal / pick / lock / play on a
// virtual clock, the roll for a player who does not pick, the fallback against a revision 1 peer,
// the practice bot's pick, the two tagged piles the Sort class is handed (through the class's own
// deck.js), the media store, and the browser stand-in's noise fetch.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { serialize, parse } from '../core/wire.js';
import {
  makeDuel, makeHello, GoonMatchPhase, GoonTransportState, NIGHT_CAP_VERSION, peerPicksNoise, peerSpeaksNight,
} from '../core/contracts.js';
import { local as localCaps } from '../core/caps.js';
import { GoonMatchService } from '../core/match.js';
import {
  NOISE_SETS, NOISE_SET_IDS, NOISE_REVEAL_MS, NOISE_PICK_MS, NOISE_LOCK_MS, NOISE_PRE_PLAY_MS, clampNoiseSet, rollNoiseSet, noiseSet,
} from '../core/noiseSets.js';
import { DUEL_INTRO_MS } from '../ui/duel/rules.js';
import { createDuelController } from '../ui/duel/duelController.js';
import { createBotDuel } from '../ui/duel/botDuel.js';
import { createPilePool, pileRows } from '../ui/duel/piles.js';
import { nicheLabel, noiseName, noiseTiles, flavourOfNiches } from '../ui/duel/noisePick.js';
import { createGoonMediaPool } from '../exec/media.js';
import { createWebMediaHost } from '../net/webMedia.js';
import { buildDeck, judge } from '../../arcademy/games/sort/deck.js';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const quiet = { info() {}, warn() {}, error() {}, log() {} };
const here = path.dirname(fileURLToPath(import.meta.url));

const mem = new Map();
globalThis.localStorage = {
  getItem: (k) => (mem.has(k) ? mem.get(k) : null),
  setItem: (k, v) => { mem.set(k, String(v)); },
  removeItem: (k) => { mem.delete(k); },
};

// ================================================= 1. the table, and its C# twin
{
  ok(NOISE_SETS.length === 7 && new Set(NOISE_SET_IDS).size === 7, 'seven boards, unique ids');
  ok(clampNoiseSet('cats') === 'cats' && clampNoiseSet('Cats') === '' && clampNoiseSet('r/cats') === '' && clampNoiseSet(3) === '' && clampNoiseSet(null) === '',
    'clampNoiseSet takes a known id only');
  const seen = new Set();
  for (let i = 0; i < 70; i++) seen.add(rollNoiseSet(() => i / 70));
  ok(seen.size === 7 && rollNoiseSet(() => 1) && rollNoiseSet(() => NaN) && NOISE_SET_IDS.includes(rollNoiseSet(() => -3)), 'the roll reaches every board and never leaves the list');
  ok(NOISE_PRE_PLAY_MS === NOISE_REVEAL_MS + NOISE_PICK_MS + NOISE_LOCK_MS && NOISE_REVEAL_MS === 2000 && NOISE_PICK_MS === 8000, 'reveal 2 s, pick 8 s');
  const cs = fs.readFileSync(path.join(here, '../../../../Services/GoonGame/GoonOnlineMedia.cs'), 'utf8');
  const csRows = [...cs.matchAll(/\["([a-z]+)"\] = "([A-Za-z0-9_]+)"/g)].map((m) => m[1] + '=' + m[2]).sort();
  const jsRows = NOISE_SETS.map((s) => s.id + '=' + s.sub).sort();
  ok(csRows.join(',') === jsRows.join(','), 'the C# GoonNoiseSets table matches core/noiseSets.js', csRows.join(',') + ' vs ' + jsRows.join(','));
  ok(noiseTiles().length === 7 && noiseName('food') === 'Food' && noiseName('nope') === '', 'every tile has a name from the copy deck');
  ok(noiseTiles().every((t) => t.name && t.glyph && /^#[0-9a-f]{6}$/i.test(t.tint)), 'and a glyph and a tint');
}

// ================================================= 2. the frame, the revision gate
{
  const f = makeDuel({ sub: 'noise', idx: 2, set: 'space' });
  ok(f.sub === 'noise' && f.set === 'space' && f.idx === 2, 'the noise frame carries a known set');
  ok(makeDuel({ sub: 'noise', set: 'porn' }).set === '' && makeDuel({ sub: 'noise', set: 'ArchitecturePorn' }).set === '', 'an unknown id (or a board name) clamps to empty');
  const old = serialize(makeDuel({ sub: 'score', idx: 1, score: 3, tile: 0, game: 'sort' }));
  ok(!old.includes('"set"'), 'every other sub leaves `set` off the wire (byte-identical to revision 1)', old);
  const back = parse(serialize(makeDuel({ sub: 'noise', idx: 1, set: 'cats' })));
  ok(back.sub === 'noise' && back.set === 'cats', 'a pick survives the wire');
  const junk = parse('{"t":"duel","v":1,"sub":"noise","idx":1,"set":"<b>x</b>"}');
  ok(junk && junk.sub === 'noise', 'a junk set still parses as a frame (the match drops it)');

  ok(NIGHT_CAP_VERSION === 2, 'this build advertises night revision 2');
  ok(peerPicksNoise({ night: 2 }) && peerPicksNoise({ night: '2' }) && !peerPicksNoise({ night: 1 }) && !peerPicksNoise({}), 'noise is revision 2, not 1');
  ok(peerSpeaksNight({ night: 2 }), 'revision 2 still speaks night');

  const mk = () => new GoonMatchService({
    state: GoonTransportState.ConnectedP2P, send() { return Promise.resolve(true); },
    onMessageReceived() { return () => {}; }, onStateChanged() { return () => {}; },
  }, true, { logger: quiet, tag: 'GG:nz' });
  const m1 = mk();
  m1._handleHello(makeHello({ caps: localCaps({ night: 1 }) }));
  ok(m1.peerSupportsNight && !m1.peerPicksNoise, 'a revision 1 peer speaks night but picks no noise');
  const m2 = mk();
  m2._handleHello(makeHello({ caps: localCaps({ night: NIGHT_CAP_VERSION }) }));
  ok(m2.peerPicksNoise, 'a revision 2 peer picks noise');
  m2._phase = GoonMatchPhase.Live;
  const got = [];
  m2.onDuelFrame((x) => got.push(x));
  m2._onMessageReceived(parse(serialize(makeDuel({ sub: 'noise', idx: 0, set: 'cars' }))));
  m2._onMessageReceived(parse('{"t":"duel","v":1,"sub":"noise","idx":0,"set":"hentai"}'));
  ok(got.length === 1 && got[0].set === 'cars', 'the match delivers a known pick and drops an unknown one', JSON.stringify(got));
  m1._phase = GoonMatchPhase.Live;
  const got1 = [];
  m1.onDuelFrame((x) => got1.push(x));
  m1._onMessageReceived(parse(serialize(makeDuel({ sub: 'noise', idx: 0, set: 'cars' }))));
  ok(got1.length === 0, 'a noise frame from a peer that never said revision 2 is dropped');
}

// ================================================= 3. the niche labels
{
  ok(flavourOfNiches(['bimbofication']).id === 'pink' && flavourOfNiches(['nothing_here']) === null, 'niches find their flavour');
  const a = nicheLabel(['sissyhypno', 'SissyInspiration']);
  ok(a.name === 'Frills' && a.tint.startsWith('#'), 'a flavour reads as its name and tint', JSON.stringify(a));
  const b = nicheLabel(['someNiche', 'other']);
  ok(b.name === 'r/someNiche' && b.more === '+1 more', 'otherwise the first niche and a count', JSON.stringify(b));
  ok(nicheLabel([]).name === 'house mix' && nicheLabel(['x1'], 'bot').name === 'the bot', 'no niches = house mix; the bot is the bot');
}

// ================================================= 4. the piles the Sort class is handed
{
  const niche = [
    { kind: 'image', url: 'https://ccp.assets/n1.jpg' }, { kind: 'image', url: 'https://ccp.assets/n2.jpg' },
    { kind: 'video', url: 'https://ccp.assets/n3.webm', clip: true }, { kind: 'image', url: 'https://ccp.assets/n1.jpg' },
  ];
  const noise = [{ kind: 'image', url: 'https://ccp.assets/z1.jpg' }, { kind: 'image', url: 'https://ccp.assets/z2.jpg' }, { kind: 'video', url: 'https://ccp.assets/z3.mp4' }];
  const t = pileRows(niche, 'target');
  ok(t.length === 2 && t.every((r) => r.kind === 'still' && r.tag === 'target'), 'target serves its stills (deduped) while it has any', JSON.stringify(t));
  ok(pileRows([{ kind: 'video', url: 'https://ccp.assets/v.webm' }], 'target')[0].kind === 'loop', 'and a clip only when it has no still');
  ok(pileRows(noise, 'noise').length === 2, 'the noise pile is stills only');

  let x = 5;
  const rand = () => { x = (Math.imul(x, 1664525) + 1013904223) >>> 0; return x / 4294967296; };
  const pool = createPilePool({ target: niche, noise: () => noise, rand, makeImage: () => null });
  ok(!pool.empty('target') && !pool.empty('noise') && pool.thin('noise'), 'both piles live (and thin, which Sort only notes)');
  const deck = buildDeck({ pool, seed: 'goon-duel|1', tier: 1, quick: false });
  const tags = new Set(deck.cards.map((c) => c.tag));
  ok(deck.cards.length > 20 && tags.has('target') && tags.has('noise'), "Sort's own deck.js deals both kinds from it", JSON.stringify(deck.counts));
  ok(deck.cards.every((c) => (c.tag === 'noise') === c.url.includes('/z')), 'every card wears the pile it came from');
  const nc = deck.cards.find((c) => c.tag === 'noise');
  const tc = deck.cards.find((c) => c.tag === 'target');
  ok(judge(tc, 'right', false) && !judge(tc, 'left', false) && judge(nc, 'left', false) && !judge(nc, 'right', false),
    'keep the niche (right), bin the noise (left)');
  const empty = createPilePool({ target: niche, noise: [], rand });
  ok(empty.empty('noise') && empty.next('noise') === null, 'no noise = an empty pile (Sort then falls back to its QUICK SORT floor)');
  pool.markBroken('https://ccp.assets/z1.jpg');
  let sawBroken = false;
  for (let i = 0; i < 6; i++) if (pool.next('noise').url.endsWith('z1.jpg')) sawBroken = true;
  ok(!sawBroken, 'a broken picture is skipped');

  const sortSrc = fs.readFileSync(path.join(here, '../../arcademy/games/sort/index.js'), 'utf8');
  ok(/spec\.sources/.test(sortSrc) && /claimPool\(specSources\.length \? \{ sources: specSources, quick: false \} : \{ sources: \[\], quick: true \}\)/.test(sortSrc),
    'the Sort class takes piles from the spec only when a host names them (its own start is untouched)');
}

// ================================================= 5. two controllers, a Sort card, the whole pick
let now = 0;
const timers = [];
const later = (fn, ms) => { const t = { at: now + ms, fn, dead: false }; timers.push(t); return () => { t.dead = true; }; };
const advance = (ms) => {
  const end = now + ms;
  for (;;) {
    timers.sort((a, b) => a.at - b.at);
    const t = timers.find((q) => !q.dead && q.at <= end);
    if (!t) break;
    now = t.at; t.dead = true; t.fn();
  }
  now = end;
};
const mkMatch = (isHost, rev = 2, niches = []) => {
  const m = new GoonMatchService({
    state: GoonTransportState.ConnectedP2P, send() { return Promise.resolve(true); },
    onMessageReceived() { return () => {}; }, onStateChanged() { return () => {}; },
  }, isHost, { logger: quiet, tag: isHost ? 'GG:nh' : 'GG:ng', caps: localCaps({ night: NIGHT_CAP_VERSION, niches }) });
  m._peerSupportsNight = true;
  m._peerPicksNoise = rev >= 2;
  m._matchSeed = 0x1234567890ABCDEFn;
  m._phase = GoonMatchPhase.Live;
  return m;
};
const fakeNoise = () => {
  const asked = [];
  const rows = new Map();
  return {
    asked, rows,
    requestNoise(set) { asked.push(set); },
    listNoise(set) { return rows.get(set) || []; },
  };
};
const viewLog = (arr) => new Proxy({}, { get: (_t, k) => (...a) => arr.push([k, a[0]]) });

{
  const host = mkMatch(true, 2, ['bimbofication']);
  const guest = mkMatch(false, 2, ['cats_but_not']);
  host._remoteHello = { caps: guest.localCaps };
  guest._remoteHello = { caps: host.localCaps };
  const wire = [];
  host._send = (msg) => { wire.push(['h', msg]); guest._onMessageReceived(parse(serialize(msg))); };
  guest._send = (msg) => { wire.push(['g', msg]); host._onMessageReceived(parse(serialize(msg))); };
  const vh = []; const vg = [];
  const runs = { h: [], g: [] };
  const runner = (tag) => (spec) => { const r = { spec, result: () => ({ game: spec.game, score: tag === 'h' ? 600 : 400 }), destroy() {} }; runs[tag].push(r); return r; };
  const nh = fakeNoise(); const ng = fakeNoise();
  const H = createDuelController({ match: host, view: viewLog(vh), runGame: runner('h'), now: () => now, later, finished: () => 3, duelLength: () => 60, noise: nh, rand: () => 0.01 });
  const G = createDuelController({ match: guest, view: viewLog(vg), runGame: runner('g'), now: () => now, later, finished: () => 3, duelLength: () => 60, noise: ng, rand: () => 0.99 });

  ok(H.arsenalHook.throwCard({ game: 'sort' }) === true, 'the host throws a Sort card');
  ok(H.state.stage === 'reveal' && G.state.stage === 'reveal', 'both sides open on the VS reveal, not the old intro');
  const rv = vh.find(([k]) => k === 'reveal');
  ok(rv && rv[1].you.name === 'Pink' && rv[1].them.name === 'r/cats_but_not', 'both niches on both screens', JSON.stringify(rv && rv[1]));
  const rvg = vg.find(([k]) => k === 'reveal');
  ok(rvg && rvg[1].them.name === 'Pink', 'and mirrored on the other one');
  ok(!vh.some(([k]) => k === 'intro'), 'no intro card on a noise duel');
  ok(nh.asked.length === 1 && nh.asked[0] === H.state.noise.roll && ng.asked[0] === G.state.noise.roll, 'each side starts fetching its roll at once');
  ok(H.arsenalHook.blocksThrows(), 'the reveal blocks throws');
  ok(H.pickNoise('cats') === false, 'no pick during the reveal');

  advance(NOISE_REVEAL_MS + 1);
  ok(H.state.stage === 'pick' && G.state.stage === 'pick', 'then both pick, each on its own clock');
  ok(vh.some(([k]) => k === 'pick'), 'the tiles come up');
  ok(H.pickNoise('hentai') === false && H.state.noise.mine === '', 'an id off the list is refused');
  ok(H.pickNoise('space') === true, 'a tap picks a board');
  ok(H.pickNoise('cats') === false && H.state.noise.mine === 'space', 'one pick, then the tiles lock');
  const pickFrame = wire.find(([w, f]) => w === 'h' && f.sub === 'noise');
  ok(pickFrame && pickFrame[1].set === 'space' && pickFrame[1].idx === 0, 'the pick crosses as a set id, nothing else', JSON.stringify(pickFrame && pickFrame[1]));
  ok(G.state.noise.theirs === 'space' && vg.some(([k, o]) => k === 'picked' && o.who === 'them' && o.set === 'space'), 'the other screen shows it');
  ok(nh.asked.includes('space'), 'and the picker fetches its board');

  advance(NOISE_PICK_MS);
  ok(G.state.noise.mine === G.state.noise.roll && G.state.noise.rolled, 'the guest never picked: it gets its roll', JSON.stringify(G.state.noise));
  ok(wire.some(([w, f]) => w === 'g' && f.sub === 'noise' && f.set === G.state.noise.roll), 'and the roll is told too');
  ok(H.state.noise.theirs === G.state.noise.roll, 'so both screens show both picks');
  ok(H.state.stage === 'lock', 'a short beat shows both picks');
  ng.rows.set(G.state.noise.roll, [{ kind: 'image', url: 'https://ccp.assets/r.jpg' }]);
  advance(NOISE_LOCK_MS + 1);
  ok(H.state.stage === 'play' && G.state.stage === 'play', 'then the class, on both sides');
  ok(runs.h.length === 1 && runs.h[0].spec.noise && runs.h[0].spec.noise.set === 'space', 'the class is handed the chosen board');
  nh.rows.set(H.state.noise.roll, [{ kind: 'image', url: 'https://ccp.assets/roll.jpg' }]);
  ok(runs.h[0].spec.noise.rows()[0].url.endsWith('roll.jpg'), 'while the chosen board has not landed the roll stands in (still noise)');
  nh.rows.set('space', [{ kind: 'image', url: 'https://ccp.assets/space.jpg' }]);
  ok(runs.h[0].spec.noise.rows()[0].url.endsWith('space.jpg'), 'and once it lands, the chosen board is the pile');
  ok(runs.g[0].spec.noise.rows()[0].url.endsWith('r.jpg'), "the guest's pile is its own board");
  advance(60 * 1000 + 50);
  ok(H.state && H.state.stage === 'result', 'the duel resolves as before', JSON.stringify(H.state));
  H.dispose(); G.dispose();
}

// ================================================= 6. a revision 1 peer: the old Sort
{
  now = 0; timers.length = 0;
  const host = mkMatch(true, 1);
  const guest = mkMatch(false, 2);
  guest._peerPicksNoise = true;   // the new side knows; the old side (host here) does not
  host._peerPicksNoise = false;
  guest._peerPicksNoise = false;  // what a real hello from a revision 1 peer produces
  const wire = [];
  host._send = (msg) => { wire.push(msg); guest._onMessageReceived(parse(serialize(msg))); };
  guest._send = (msg) => { wire.push(msg); host._onMessageReceived(parse(serialize(msg))); };
  const runs = [];
  const runGame = (spec) => { runs.push(spec); return { result: () => ({ game: spec.game, score: 1 }), destroy() {} }; };
  const nz = fakeNoise();
  const H = createDuelController({ match: host, runGame, now: () => now, later, finished: () => 3, duelLength: () => 60, noise: nz });
  const G = createDuelController({ match: guest, runGame, now: () => now, later, finished: () => 3, duelLength: () => 60, noise: nz });
  ok(G.arsenalHook.throwCard({ game: 'sort' }), 'a Sort card against an older build');
  ok(G.state.stage === 'intro' && H.state.stage === 'intro' && !G.state.noise, 'plain intro, no reveal, no pick');
  advance(DUEL_INTRO_MS + 1);
  ok(G.state.stage === 'play' && runs.length === 2 && runs.every((s) => !s.noise), 'the class starts after the old intro with no noise (moving vs still)');
  ok(!wire.some((f) => f.sub === 'noise') && nz.asked.length === 0, 'no noise frame and no fetch');
  H.dispose(); G.dispose();
}

// ================================================= 7. The Deep End never picks
{
  now = 0; timers.length = 0;
  const host = mkMatch(true, 2);
  const guest = mkMatch(false, 2);
  host._send = (msg) => guest._onMessageReceived(parse(serialize(msg)));
  guest._send = (msg) => host._onMessageReceived(parse(serialize(msg)));
  const runGame = (spec) => ({ spec, result: () => ({ game: spec.game, tile: 1, score: 1 }), destroy() {} });
  const H = createDuelController({ match: host, runGame, now: () => now, later, finished: () => 3, duelLength: () => 60 });
  const G = createDuelController({ match: guest, runGame, now: () => now, later, finished: () => 3, duelLength: () => 60 });
  H.arsenalHook.throwCard({ game: 'the-deep-end' });
  ok(H.state.stage === 'intro' && !H.state.noise, 'a Deep End card keeps its intro between revision 2 builds');
  H.dispose(); G.dispose();
}

// ================================================= 8. practice: the bot picks too
{
  now = 0; timers.length = 0;
  const host = mkMatch(true, 2, ['EroticHypnosis']);
  const guest = mkMatch(false, 2);
  const log = [];
  host._send = (msg) => { log.push(['h', msg]); guest._onMessageReceived(parse(serialize(msg))); };
  guest._send = (msg) => { log.push(['bot', msg]); host._onMessageReceived(parse(serialize(msg))); };
  let x = 11;
  const rand = () => { x = (Math.imul(x, 1664525) + 1013904223) >>> 0; return x / 4294967296; };
  const vh = [];
  const runGame = (spec) => ({ spec, result: () => ({ game: spec.game, score: 0 }), destroy() {} });
  const H = createDuelController({ match: host, view: viewLog(vh), runGame, now: () => now, later, finished: () => 0, duelLength: () => 60, isPractice: () => true, noise: fakeNoise() });
  const B = createBotDuel({ match: guest, rand, now: () => now, later, throws: false });
  ok(H.arsenalHook.throwCard({ game: 'sort' }), 'the player throws Sort at the bot');
  const rv = vh.find(([k]) => k === 'reveal');
  ok(rv && rv[1].them.name === 'the bot' && rv[1].you.name === 'Trance', 'the reveal names the bot', JSON.stringify(rv && rv[1]));
  advance(NOISE_REVEAL_MS + NOISE_PICK_MS - 1);
  const botPick = log.find(([w, f]) => w === 'bot' && f.sub === 'noise');
  ok(botPick && NOISE_SET_IDS.includes(botPick[1].set), 'the bot picks a board at random inside the pick window', JSON.stringify(botPick && botPick[1]));
  ok(H.state.noise.theirs === botPick[1].set && B.state.noise === botPick[1].set, 'and the player sees it');
  advance(1 + NOISE_LOCK_MS + 60 * 1000 + 1);
  ok(H.state.stage === 'wait', 'the player plays the whole clock after the pick', JSON.stringify(H.state));
  advance(1500);
  ok(log.some(([w, f]) => w === 'bot' && f.sub === 'score'), 'the bot reports after the pre-play time as well, inside the grace window');
  ok(H.state && H.state.stage === 'result', 'and the duel resolves');
  H.dispose(); B.dispose();
}

// ================================================= 9. the media store
{
  const m = createGoonMediaPool();
  const sent = [];
  ok(m.requestNoise('cats') === false, 'no requester, no ask');
  m.setNoiseRequester((s) => sent.push(s));
  ok(m.requestNoise('cats') === true && m.requestNoise('cats') === false && sent.join(',') === 'cats', 'one ask per board');
  ok(m.setNoiseLibrary({ set: 'cats', state: 'loading', images: [{ name: 'online:a', url: 'https://ccp.assets/.temp/a.jpg' }, { url: '' }] }) === 1, 'a noise frame lands per board');
  ok(m.listNoise('cats').length === 1 && m.listNoise('food').length === 0, 'listNoise reads one board');
  ok(!m.list().some((r) => r.url.endsWith('a.jpg')), 'and noise never enters the deck');
  m.setNoiseLibrary({ set: 'cats', state: 'declined', images: [] });
  ok(m.requestNoise('cats') === true, 'a declined board may be asked again later');
  m.clearNoise();
  ok(sent[sent.length - 1] === '' && m.listNoise('cats').length === 0, 'match over: everything forgotten and the host told');
}

// ================================================= 10. the browser stand-in fetches a board
{
  const calls = [];
  const frames = [];
  const fetch = async (url, o) => {
    const body = JSON.parse(o.body);
    calls.push(body.variables);
    return { ok: true, json: async () => ({ data: { getSubreddit: { children: { iterator: null, items: [
      { id: '1', mediaSources: [{ url: 'https://x.scrolller.com/a.jpg', width: 800, height: 600 }] },
      { id: '2', mediaSources: [{ url: 'https://x.scrolller.com/b.webp', width: 640, height: 480 }] },
    ] } } } }) };
  };
  const host = createWebMediaHost({ emit: (f) => frames.push(f), fetch, online: true, now: () => 0, sleep: async () => {} });
  ok(host.handle({ type: 'noise-want', set: 'nope' }) === true && calls.length === 0, 'an id off the list fetches nothing');
  host.handle({ type: 'noise-want', set: 'cats' });
  await new Promise((r) => setTimeout(r, 20));
  ok(calls.length >= 1 && calls[0].url === '/r/cats' && calls[0].filter === 'PICTURE', 'the board name comes from the table, stills only', JSON.stringify(calls[0]));
  const last = frames.filter((f) => f.type === 'noise-media').pop();
  ok(last && last.set === 'cats' && last.images.length === 2 && last.videos.length === 0, 'answered as noise-media for that set', JSON.stringify(last));
  host.handle({ type: 'noise-want', set: '' });
  ok(frames.pop().state === 'off', "'' releases the boards");
  const off = createWebMediaHost({ emit: (f) => frames.push(f), fetch, online: false });
  off.handle({ type: 'noise-want', set: 'food' });
  ok(frames.pop().state === 'declined', 'online pictures off: declined, nothing fetched');
}

if (failures) {
  console.error(`selftest-noise: ${n - failures}/${n} checks passed, ${failures} FAILURE(S)`);
  process.exit(1);
}
console.log(`selftest-noise: ${n}/${n} checks passed`);
