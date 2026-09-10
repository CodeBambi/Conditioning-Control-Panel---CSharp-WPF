/* ============================================================================
 * smoke/ramp-smoke.mjs - node tests for the ramp's math.
 *
 *   node ConditioningControlPanel/Resources/web/piecebypiece/smoke/ramp-smoke.mjs
 *
 * Everything under test is pure or clock-injected, so there is no DOM, no
 * timer and no browser here. The seeded RNG means a failure is reproducible and
 * a passing run is a promise about the numbers, not about a lucky draw.
 *
 * The layers are NOT tested here - they are pixels, and the headless shoot pass
 * (smoke/shoot.mjs) is what looks at those.
 * ==========================================================================*/

import { createMeter, RAMP_TUNING, plainShare, clamp01 } from '../ramp/meter.js';
import { createSchedule, makeRng, cadenceMs, sustainedFor, videoHoldMs } from '../ramp/schedule.js';
import { createFixtureMedia, createHostMedia, HOST_MSG, noiseTileUrl } from '../ramp/media.js';

let passed = 0;
const failures = [];

function check(name, cond, detail) {
  if (cond) { passed += 1; return; }
  failures.push(name + (detail ? ' -> ' + detail : ''));
}
const near = (a, b, eps = 1e-9) => Math.abs(a - b) <= eps;
function eq(name, got, want, eps = 1e-9) {
  // numbers compare with a tolerance, anything else exactly: a string compared
  // as a number is NaN, which used to fail a passing check
  const same = typeof got === 'number' && typeof want === 'number'
    ? near(got, want, eps)
    : got === want;
  check(name, same, `got ${got}, want ${want}`);
}

/* ---- the meter ----------------------------------------------------------- */

const TOTAL = 300000;
function freshMeter() {
  const m = createMeter({});
  m.setClock({ w: TOTAL, b: TOTAL, total: TOTAL, active: 'w' });
  return m;
}

{
  const m = freshMeter();
  eq('fresh meter is zero', m.meterFor('w'), 0);
  eq('no clock at all reads as a full clock', createMeter({}).meterFor('w'), 0);
}

{
  const m = freshMeter();
  m.setClock({ w: TOTAL / 2, b: TOTAL });
  eq('half a clock is 0.35 * 0.5', m.meterFor('w'), 0.175);
  eq('the other side is untouched', m.meterFor('b'), 0);
  m.setClock({ w: 0 });
  eq('a dead clock is the full clock weight', m.meterFor('w'), RAMP_TUNING.clockWeight);
}

{
  // the owner rule, stated as a test: taking ramps you MORE than losing
  const taker = freshMeter();
  taker.noteCapture({ by: 'w', victimSide: 'b' }, 0);
  eq('one capture ramps the taker 0.15', taker.meterFor('w'), 0.15);
  eq('one capture ramps the victim 0.10', taker.meterFor('b'), 0.10);
  check('capturing ramps harder than losing', taker.meterFor('w') > taker.meterFor('b'));
  eq('the weights are 1.5 and 1.0',
    taker.meterFor('w') / taker.meterFor('b'), RAMP_TUNING.captureWeight / RAMP_TUNING.lossWeight, 1e-9);
}

{
  const m = freshMeter();
  for (let i = 0; i < 30; i++) m.noteCapture({ by: 'w', victimSide: 'b' }, 0);
  eq('the meter clamps at 1', m.meterFor('w'), 1);
  eq('and so does the victim side', m.meterFor('b'), 1);
}

{
  // a capture missing victimSide still has to move both sides
  const m = freshMeter();
  m.noteCapture({ by: 'b' }, 0);
  eq('victimSide is inferred from the taker', m.meterFor('w'), 0.10);
  eq('and the taker still got a capture', m.meterFor('b'), 0.15);
}

{
  const m = freshMeter();
  m.noteCapture({ by: 'w', victimSide: 'b' }, 1000);
  const d = RAMP_TUNING.burst.decayMs;
  eq('the kick lands at full amplitude', m.kickFor('w', 1000), RAMP_TUNING.burst.taker);
  eq('the victim kick is smaller', m.kickFor('b', 1000), RAMP_TUNING.burst.victim);
  check('the taker kick is the bigger one', m.kickFor('w', 1000) > m.kickFor('b', 1000));
  eq('the kick is half gone at half the decay', m.kickFor('w', 1000 + d / 2), RAMP_TUNING.burst.taker / 2, 1e-9);
  eq('the kick is gone at the decay', m.kickFor('w', 1000 + d), 0);
  eq('and stays gone after it', m.kickFor('w', 1000 + d * 4), 0);
  eq('heat is meter plus kick', m.heatFor('w', 1000), 0.15 + RAMP_TUNING.burst.taker, 1e-9);
  eq('heat falls back to the meter', m.heatFor('w', 1000 + d), 0.15);
}

{
  const m = freshMeter();
  m.setTurn({ side: 'b', ply: 7, clocks: { w: 1000, b: 2000 }, total: TOTAL });
  check('turn sets the acting side', m.active === 'b');
  check('turn carries the ply', m.ply === 7);
  check('turn carries the clocks', m.meterFor('w') > m.meterFor('b'));
  m.reset();
  eq('reset zeroes the model', m.meterFor('w') + m.meterFor('b'), 0);
}

{
  // nothing in the model may throw on a malformed payload
  const m = freshMeter();
  let threw = false;
  try {
    m.setClock(null); m.setTurn(null); m.noteCapture(null, 0);
    m.setClock({ w: 'x', total: -1 }); m.noteCapture({ by: 'q' }, 0);
  } catch { threw = true; }
  check('a malformed payload never throws', !threw);
  eq('and never corrupts the meter', m.meterFor('w'), 0);
}

/* ---- the schedule -------------------------------------------------------- */

{
  eq('plainShare is 0.80 with no heat', plainShare(0), 0.80);
  eq('plainShare is 0.30 at full heat', plainShare(1), 0.30);
  check('plainShare only ever falls', plainShare(0.2) > plainShare(0.8));
}

{
  eq('flash cadence at rest', cadenceMs('flash', 0), RAMP_TUNING.flash.slowMs);
  eq('flash cadence at full heat', cadenceMs('flash', 1), RAMP_TUNING.flash.fastMs);
  check('cadence shortens as heat rises', cadenceMs('gifRain', 0.2) > cadenceMs('gifRain', 0.9));
  check('an unknown kind never spawns', cadenceMs('nope', 1) === Infinity);
}

{
  const u = RAMP_TUNING.unlock;
  for (const [name, at] of Object.entries(u)) {
    check(name + ' is off just under its unlock', sustainedFor(at - 0.001)[name].on === false);
    check(name + ' is on at its unlock', sustainedFor(at)[name].on === true);
  }
  check('nothing is on at meter zero', Object.values(sustainedFor(0)).every((s) => !s.on));
  check('everything is on at meter one', Object.values(sustainedFor(1)).every((s) => s.on));
}

{
  // THE cap. A sweep, not a spot check: this is the one that keeps the game playable.
  let worst = 0;
  for (let i = 0; i <= 2000; i++) worst = Math.max(worst, sustainedFor(i / 2000).blur.px);
  check('blur never passes its cap', worst <= RAMP_TUNING.blurMaxPx + 1e-12, 'worst ' + worst);
  eq('blur reaches the cap at meter one', sustainedFor(1).blur.px, RAMP_TUNING.blurMaxPx);
  // and a caller handing in nonsense still cannot get past the clamp
  check('an out-of-range meter is clamped', sustainedFor(9).blur.px <= RAMP_TUNING.blurMaxPx);
}

{
  // the melt only ever deepens
  let prev = -1, ok = true;
  for (let i = 0; i <= 100; i++) {
    const v = sustainedFor(i / 100).melt.alpha;
    if (v < prev - 1e-12) ok = false;
    prev = v;
  }
  check('melt alpha only ever rises', ok);
  check('the spiral hold grows with the meter', sustainedFor(1).spiral.holdMs > sustainedFor(0.6).spiral.holdMs);

  // THE VEIL BUDGET. The two full-screen veils are the only layers that can
  // hide the board outright, so their SUM is what has to behave: it may rise
  // with the meter, but it may never pass the budget, and a card over the
  // board pushes it back down. This is the other half of the blur cap.
  const veil = (m, o) => { const s2 = sustainedFor(m, RAMP_TUNING, o); return s2.spiral.alpha + s2.overlay.alpha; };
  let worst = 0, rises = true, last = -1;
  for (let i = 0; i <= 1000; i++) {
    const v = veil(i / 1000);
    worst = Math.max(worst, v);
    if (v < last - 1e-9) rises = false;
    last = v;
  }
  check('the veils never pass their budget', worst <= RAMP_TUNING.veilBudget + 1e-9, 'worst ' + worst);
  check('the combined veil only ever rises', rises);
  check('a card over the board pushes the veils back',
    veil(1, { cardLive: true }) < veil(1) * 0.7,
    veil(1, { cardLive: true }).toFixed(3) + ' vs ' + veil(1).toFixed(3));
  check('a card over the board damps the melt as well',
    sustainedFor(1, RAMP_TUNING, { cardLive: true }).melt.alpha < sustainedFor(1).melt.alpha);
  check('the board is never fully covered at a full meter',
    veil(1) + sustainedFor(1).melt.alpha < 1.2);
}

{
  eq('the card holds its floor at meter zero', videoHoldMs(0), RAMP_TUNING.videoCard.minHoldSec * 1000);
  eq('and its ceiling at meter one', videoHoldMs(1), RAMP_TUNING.videoCard.maxHoldSec * 1000);
  check('the hold grows with the meter', videoHoldMs(0.8) > videoHoldMs(0.2));
}

/* ---- determinism --------------------------------------------------------- */

{
  const a = makeRng('seed-a'), b = makeRng('seed-a'), c = makeRng('seed-b');
  const draw = (r, n) => Array.from({ length: n }, () => r());
  const av = draw(a, 12), bv = draw(b, 12), cv = draw(c, 12);
  check('the same seed replays exactly', av.every((v, i) => v === bv[i]));
  check('a different seed does not', av.some((v, i) => v !== cv[i]));
  check('every draw is inside 0..1', av.every((v) => v >= 0 && v < 1));
}

/** Run a schedule over a fixed timeline and collect what it fired. */
function replay(seed, heat, steps = 400, stepMs = 90) {
  const s = createSchedule({ seed });
  const fired = [];
  for (let i = 1; i <= steps; i++) {
    const out = s.tick(i * stepMs, heat, heat);
    for (const k of out.fire) fired.push(i + ':' + k);
  }
  return fired;
}

{
  const one = replay('replay', 0.6);
  const two = replay('replay', 0.6);
  check('a seeded schedule replays exactly', one.join('|') === two.join('|'));
  check('a different seed diverges', replay('other', 0.6).join('|') !== one.join('|'));
}

{
  const cold = replay('heat', 0.05).length;
  const warm = replay('heat', 0.5).length;
  const hot = replay('heat', 0.95).length;
  check('a cold board is quiet', cold < warm, `cold ${cold} warm ${warm}`);
  check('a hot board is busy', warm < hot, `warm ${warm} hot ${hot}`);
  check('even a cold board is not silent', cold > 0);
}

{
  const s = createSchedule({ seed: 'burst' });
  const taker = s.burstFor('taker', 0.8);
  const victim = s.burstFor('victim', 0.8);
  check('a capture burst spends more on the taker', taker.flashes > victim.flashes);
  check('and shakes the taker harder', taker.shakeMs > victim.shakeMs);
  check('a burst at rest still fires something', s.burstFor('taker', 0).flashes > 0);
}

/* ---- media --------------------------------------------------------------- */

{
  const empty = createFixtureMedia([]);
  check('an empty pool has no images', empty.has('image') === false);
  check('an empty pool has no videos', empty.has('video') === false);
  check('an empty pool draws nothing', empty.draw('image') === null);
  check('the video card gets nothing to play', empty.draw('video') === null);
  const tile = empty.drawTile();
  check('but a tile is always available', typeof tile === 'string' && tile.startsWith('data:image/svg+xml'));
  check('the noise tile is a real data uri', noiseTileUrl(2).length > 80);
  check('stats survive an empty pool', empty.stats().total === 0);
}

{
  const list = [
    { kind: 'image', url: '/a.png' }, { kind: 'image', url: '/b.png' },
    { kind: 'gif', url: '/c.gif' }, { kind: 'video', url: '/d.webm' },
  ];
  const m = createFixtureMedia(list);
  check('images and gifs both count as images', m.has('image'));
  check('the video is found', m.draw('video') === '/d.webm');
  check('a gif draw is a gif', m.draw('gif') === '/c.gif');
  const deck = Array.from({ length: 3 }, () => m.draw('image'));
  check('a deck hands out every entry before repeating', new Set(deck).size === 3, deck.join(','));
  check('stats count each kind', m.stats().video === 1 && m.stats().gif === 1 && m.stats().image === 2);
}

{
  // kinds may be omitted: the extension has to be enough
  const m = createFixtureMedia(['/x.mp4', '/y.gif', '/z.jpg']);
  check('an mp4 is inferred as a video', m.draw('video') === '/x.mp4');
  check('a gif is inferred as a gif', m.draw('gif') === '/y.gif');
  check('anything else is an image', m.has('image'));
  check('junk entries are dropped', createFixtureMedia([null, {}, 3, '']).stats().total === 0);
}

{
  // no WebView2 around (a plain browser, or node): the host source has to
  // behave exactly like an empty fixture pool and never throw
  const host = createHostMedia();
  check('with no bridge the host pool starts empty', host.size === 0);
  check('and never hands out a clip', host.draw('video') === null);
  check('and still answers with a generated tile', typeof host.drawTile() === 'string');
  check('and asking the host that is not there is simply false', host.request() === false);
  host.adopt([{ kind: 'image', url: 'https://ccp.assets/img/1.png' }]);
  check('adopt() is the door the manifest uses', host.size === 1);
  check('and the pool answers after it', host.draw('image') === 'https://ccp.assets/img/1.png');
}

/* ---- the host bridge ----------------------------------------------------- */

/** A stand-in for window.chrome.webview: records what the page posts. */
function fakeBridge() {
  const listeners = new Set();
  const sent = [];
  return {
    sent,
    postMessage(o) { sent.push(o); },
    addEventListener(type, fn) { if (type === 'message') listeners.add(fn); },
    removeEventListener(type, fn) { listeners.delete(fn); },
    /** the host answering */
    push(data) { for (const fn of [...listeners]) fn({ data }); },
    get listening() { return listeners.size; },
  };
}

{
  const bridge = fakeBridge();
  const host = createHostMedia([], { bridge });
  const ask = bridge.sent[0];
  check('the page asks the host at attach', !!ask);
  eq('and asks with the agreed type', ask && ask.type, HOST_MSG.request);
  check('the ask names all three kinds',
    ask && ['image', 'gif', 'video'].every((k) => ask.kinds.includes(k)),
    JSON.stringify(ask && ask.kinds));
  check('and asks for a batch, not one at a time', ask && ask.count >= 8);

  bridge.push({
    type: HOST_MSG.media,
    images: ['https://ccp.assets/a.png', 'https://ccp.assets/b.png'],
    gifs: ['https://ccp.assets/c.gif'],
    videos: ['https://ccp.assets/d.mp4'],
  });
  eq('the answer lands in the pool', host.size, 4);
  eq('videos are drawable', host.draw('video'), 'https://ccp.assets/d.mp4');
  check('gifs are drawable', host.draw('gif') === 'https://ccp.assets/c.gif');

  // a second frame adds rather than replaces, and never duplicates
  bridge.push({ type: HOST_MSG.media, images: ['https://ccp.assets/a.png', 'https://ccp.assets/e.png'] });
  eq('a later frame adds only what is new', host.size, 5);

  // empty lists are a normal answer, not an error
  bridge.push({ type: HOST_MSG.media, images: [], gifs: [], videos: [] });
  eq('an empty answer changes nothing', host.size, 5);

  // and junk on the wire cannot take the page down
  for (const junk of [null, undefined, 7, 'hello', {}, { type: 'other' }, { type: HOST_MSG.media, images: 3 }]) {
    bridge.push(junk);
  }
  eq('junk frames are ignored', host.size, 5);

  let got = null;
  host.onSettings((s2) => { got = s2; });
  bridge.push({ type: HOST_MSG.settings, videoHoldSec: 7, reducedMotion: true });
  check('settings reach a subscriber', !!got);
  eq('videoHoldSec comes through', got && got.videoHoldSec, 7);
  eq('reducedMotion comes through', got && got.reducedMotion, true);

  let late = null;
  host.onSettings((s2) => { late = s2; });
  check('a late subscriber is told at once', late && late.videoHoldSec === 7);

  host.dispose();
  eq('dispose lets go of the bridge', bridge.listening, 0);
}

{
  // the low-water ask: a nearly spent deck must send the page back to the host
  const bridge = fakeBridge();
  // requestGapMs 0: the real throttle is four seconds and a test cannot wait
  const host = createHostMedia([], { bridge, requestGapMs: 0 });
  bridge.push({ type: HOST_MSG.media, images: ['/1.png', '/2.png', '/3.png'] });
  const before = bridge.sent.length;
  for (let i = 0; i < 12; i++) host.draw('image');
  check('a drained deck asks the host for more', bridge.sent.length > before,
    before + ' -> ' + bridge.sent.length);

  // and with the real throttle in place, the same drain cannot spam the host
  const b2 = fakeBridge();
  const host2 = createHostMedia([], { bridge: b2 });
  b2.push({ type: HOST_MSG.media, images: ['/1.png', '/2.png', '/3.png'] });
  for (let i = 0; i < 12; i++) host2.draw('image');
  eq('the throttle holds the asks to the one at attach', b2.sent.length, 1);
}

{
  // the host's own hold setting moves the floor, and the meter still stretches it
  eq('no host setting keeps the tuning floor', videoHoldMs(0, RAMP_TUNING, null), RAMP_TUNING.videoCard.minHoldSec * 1000);
  eq('a host hold of 6s is the floor', videoHoldMs(0, RAMP_TUNING, 6), 6000);
  check('and a full meter still stretches it', videoHoldMs(1, RAMP_TUNING, 6) > 6000);
  eq('a nonsense hold falls back to the tuning', videoHoldMs(0, RAMP_TUNING, -3), RAMP_TUNING.videoCard.minHoldSec * 1000);
}

/* ---- report -------------------------------------------------------------- */

const total = passed + failures.length;
if (failures.length) {
  console.error(`FAIL  ${failures.length}/${total} checks failed:`);
  for (const f of failures) console.error('  - ' + f);
  process.exit(1);
}
console.log(`ok  ${passed}/${total} ramp checks passed`);
