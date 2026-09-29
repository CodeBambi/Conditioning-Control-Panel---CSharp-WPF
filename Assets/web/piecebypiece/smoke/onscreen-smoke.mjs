/* ============================================================================
 * smoke/onscreen-smoke.mjs - Distraction never shows the same picture twice at
 * once (bug hunt 2026-09-29, CHESS-7).
 *
 * The flash, the rain, the overlay, the spiral veil, the grab sticker and the
 * video card all draw from one pool, and nothing checked what was already up:
 * with a small library the rain wore the same gif on two or three drops while
 * the flash and the overlay showed it too, and the stills never rained. Now a
 * draw skips every picture on screen and shows one fewer rather than a copy,
 * the rule the Back Room bursts and the welcome show follow.
 *
 *   A  the pool: draw(kind, avoid) never answers an avoided url and keeps it in
 *      the deck for later; it answers null when every picture of the kind is
 *      up. drawTile(avoid) takes a still once every gif is up, and a noise tile
 *      only when the pool has no pictures at all.
 *   B  the layer stack on a fake DOM and a fake clock, the meter pinned at .95
 *      with extra spawns every 1.8 s (the hunt's dist.mjs, run in node): no
 *      duplicate in any sample, pictures still up, stills rain once the gifs
 *      are all up, no noise tile while the pool has pictures, and nothing is
 *      left held after a clear.
 *
 *   node smoke/onscreen-smoke.mjs        (no server, no browser)
 * ==========================================================================*/

import { createFixtureMedia, createHostMedia, HOST_MSG } from '../ramp/media.js';
import { createLayerStack } from '../ramp/layers/index.js';
import { createSchedule, makeRng } from '../ramp/schedule.js';
import { RAMP_TUNING } from '../ramp/meter.js';
import { isClip, _resetClips } from '../ramp/layers/clip.js';

let failed = 0;
let passed = 0;
function expect(cond, name) {
  if (cond) { passed++; console.log('  ok   ' + name); }
  else { failed++; console.error('  FAIL ' + name); }
}

/* ---- a fake clock: every timer the layers set runs on it ------------------- */
const clock = { t: 0, seq: 0, timers: new Map() };
globalThis.setTimeout = (fn, ms = 0, ...a) => {
  const id = ++clock.seq;
  clock.timers.set(id, { at: clock.t + Math.max(0, Number(ms) || 0), fn: () => fn(...a) });
  return id;
};
globalThis.clearTimeout = (id) => { clock.timers.delete(id); };
globalThis.setInterval = (fn, ms = 0) => {
  const id = ++clock.seq;
  const every = Math.max(1, Number(ms) || 0);
  const arm = () => clock.timers.set(id, { at: clock.t + every, fn: () => { arm(); fn(); } });
  arm();
  return id;
};
globalThis.clearInterval = globalThis.clearTimeout;
globalThis.performance = { now: () => clock.t };
const thrown = [];
function advance(ms) {
  const end = clock.t + ms;
  for (;;) {
    let next = null;
    for (const [id, tm] of clock.timers) {
      if (tm.at > end) continue;
      if (!next || tm.at < next.tm.at || (tm.at === next.tm.at && id < next.id)) next = { id, tm };
    }
    if (!next) break;
    clock.timers.delete(next.id);
    clock.t = next.tm.at;
    try { next.tm.fn(); } catch (e) { thrown.push(e); }
  }
  clock.t = end;
}

/* ---- a fake DOM: just what the layers touch ------------------------------------ */
class El {
  constructor(tag) {
    this.tagName = String(tag).toUpperCase();
    this.className = '';
    this.children = [];
    this.parentNode = null;
    this.src = '';
    const props = {};
    this.style = { setProperty: (k, v) => { props[k] = String(v); }, getPropertyValue: (k) => props[k] || '' };
    const names = () => this.className.split(' ').filter(Boolean);
    const add = (c) => { if (!names().includes(c)) this.className = [...names(), c].join(' '); };
    const remove = (c) => { this.className = names().filter((n) => n !== c).join(' '); };
    this.classList = {
      add: (...cs) => cs.forEach(add),
      remove: (...cs) => cs.forEach(remove),
      contains: (c) => names().includes(c),
      toggle: (c, on) => { const want = on === undefined ? !names().includes(c) : !!on; if (want) add(c); else remove(c); return want; },
    };
  }
  appendChild(c) { if (c.parentNode) c.remove(); c.parentNode = this; this.children.push(c); return c; }
  remove() { const p = this.parentNode; if (!p) return; p.children = p.children.filter((x) => x !== this); this.parentNode = null; }
  get isConnected() { let n = this; while (n.parentNode) n = n.parentNode; return n === body; }
  // animationend / error / loadedmetadata never fire here: each layer's own timer is the net
  addEventListener() {}
  setAttribute() {}
  removeAttribute(k) { if (k === 'src') this.src = ''; }
  play() { return Promise.resolve(); }
  pause() {}
  load() {}
}
const body = new El('body');
globalThis.document = { createElement: (t) => new El(t), getElementById: () => null, body, head: body };

/** What is on screen now: every picture a layer shows, one row per surface. */
const BOXES = ['pbp-overlay', 'pbp-spiral', 'pbp-grab'];
function onScreenNow(roots) {
  const out = [];
  const walk = (el, plane, box, hidden) => {
    const own = BOXES.find((b) => el.classList.contains(b));
    const inBox = own || box;
    // a box that is off is faded out; its picture (or clip child) is not up
    const off = hidden || (!!own && !el.classList.contains('is-on'));
    if (!off) {
      const cls = el.className || plane;
      if ((el.tagName === 'IMG' || el.tagName === 'VIDEO') && el.src) out.push({ url: el.src, cls, plane, box: inBox, tag: el.tagName });
      const bg = String(el.style.backgroundImage || '');
      const at = bg.indexOf('url("');
      if (at > -1) out.push({ url: bg.slice(at + 5, bg.lastIndexOf('")')), cls, plane, box: inBox });
    }
    for (const c of el.children) walk(c, plane, inBox, off);
  };
  roots.forEach((r, i) => { for (const c of r.children) walk(c, i ? 'front' : 'root', '', false); });
  return out;
}
const isNoise = (u) => String(u).startsWith('data:');

/* ---- A: the pool -------------------------------------------------------------- */
{
  const list = [
    { kind: 'image', url: '/s0.png' }, { kind: 'image', url: '/s1.png' }, { kind: 'image', url: '/s2.png' },
    { kind: 'gif', url: '/g0.gif' }, { kind: 'gif', url: '/g1.gif' },
  ];
  const pool = createFixtureMedia(list, { rnd: makeRng('pool-a') });

  let hit = 0;
  for (let i = 0; i < 40; i++) if (pool.draw('gif', new Set(['/g0.gif'])) === '/g0.gif') hit++;
  expect(hit === 0, 'a gif on screen is never drawn again (' + hit + '/40)');
  expect(pool.draw('gif', new Set(['/g0.gif', '/g1.gif'])) === null, 'every gif up: draw answers null, never a copy');

  const allGifs = new Set(['/g0.gif', '/g1.gif']);
  const tile = pool.drawTile(allGifs);
  expect(typeof tile === 'string' && /\/s\d\.png$/.test(tile), 'every gif up: the tile is a still (' + tile + ')');
  const every = new Set(list.map((e) => e.url));
  expect(pool.drawTile(every) === null, 'every picture up: the tile is one fewer, not a noise tile');
  const empty = createFixtureMedia([]);
  expect(isNoise(empty.drawTile(new Set())), 'an empty pool still rains noise tiles');

  // a skipped picture stays in the deck: freed, it comes up again in the same deal
  const deck = createFixtureMedia([{ kind: 'image', url: '/a.png' }, { kind: 'image', url: '/b.png' }, { kind: 'image', url: '/c.png' }], { rnd: makeRng('pool-deck') });
  const busy = new Set(['/a.png']);
  const firstTwo = [deck.draw('image', busy), deck.draw('image', busy)];
  expect(!firstTwo.includes('/a.png') && new Set(firstTwo).size === 2, 'the rest of the deck is dealt around a busy picture (' + firstTwo + ')');
  expect(deck.draw('image', new Set()) === '/a.png', 'and the busy one is next once it leaves the screen');
  const plain = createFixtureMedia([{ kind: 'image', url: '/x.png' }, { kind: 'image', url: '/y.png' }, { kind: 'image', url: '/z.png' }]);
  expect(new Set([plain.draw('image'), plain.draw('image'), plain.draw('image')]).size === 3, 'no avoid list: the deck deals as before');
}

{
  // the host pool: local + online. A side with every picture up hands over to the other side.
  const bridge = { postMessage() {}, addEventListener() {}, removeEventListener() {} };
  const host = createHostMedia([], { bridge, requestGapMs: 0, rnd: makeRng('host-a') });
  host.handleMessage({ type: 'pbp:media', images: ['/l0.png'], gifs: ['/lg0.gif'], videos: [] });
  host.handleMessage({ type: HOST_MSG.online, state: 'ready', share: 70, images: ['/o0.jpg', '/o1.jpg'], clips: ['/c0.webm'] });
  const localGifUp = new Set(['/lg0.gif']);
  let bad = 0;
  for (let i = 0; i < 40; i++) { const u = host.draw('gif', localGifUp); if (u !== '/c0.webm') bad++; }
  expect(bad === 0, 'the local gif is up: every gif draw takes the online clip (' + bad + ' misses)');
  const up = new Set(['/l0.png', '/lg0.gif', '/o0.jpg', '/o1.jpg', '/c0.webm']);
  expect(host.draw('image', up) === null && host.draw('gif', up) === null, 'everything up on both sides: null');
  expect(host.drawTile(up) === null, 'and the host tile is one fewer, not a noise tile');
  const bare = createHostMedia([], { bridge });
  expect(isNoise(bare.drawTile(new Set())), 'a host pool with nothing in it still hands out noise tiles');
}

/* ---- B: the layer stack, the hunt's dist.mjs in node -------------------------- */
const LOCAL = {
  images: ['/lib/flash.png', '/lib/pinkfilter.png', '/lib/braindrain.png', '/lib/subliminal.png'],
  gifs: ['/lib/sp6.gif', '/lib/sp7.gif'],
  videos: [],
};
const ONLINE_IMAGES = Array.from({ length: 10 }, (_, i) => `https://ccp.assets/.temp/o${i}.jpg`);
const ONLINE_CLIPS = ['https://ccp.assets/.temp/c0.webm', 'https://ccp.assets/.temp/c1.webm', 'https://ccp.assets/.temp/c2.webm'];
const NONE = { images: [], gifs: [], videos: [] };

function run(name, { local, online, secs = 16 }) {
  _resetClips();
  body.children = [];
  const root = new El('div'); body.appendChild(root);
  const front = new El('div'); body.appendChild(front);
  const stage = new El('div');
  const bridge = { postMessage() {}, addEventListener() {}, removeEventListener() {} };
  const media = createHostMedia([], { bridge, requestGapMs: 0, rnd: makeRng(name + '-pool') });
  media.handleMessage({ type: 'pbp:media', images: local.images, gifs: local.gifs, videos: local.videos });
  if (online) media.handleMessage({ type: HOST_MSG.online, state: 'ready', share: 70, images: online.images, clips: online.clips });
  const schedule = createSchedule({ tuning: RAMP_TUNING, seed: name });
  const stack = createLayerStack({ root, front, stage, media, tuning: RAMP_TUNING, rng: schedule.rng });

  const METER = 0.95;
  const r = { samples: 0, dupSamples: 0, maxCopies: 0, visible: 0, noiseSamples: 0, stillRain: 0, cardSeen: 0, grabSeen: 0, clipInImg: 0, example: '' };
  const start = clock.t;
  let nextPrime = start, nextSample = start + 200, nextCard = start + 900, nextGrab = start + 1500, dropAt = Infinity;
  while (clock.t - start < secs * 1000) {
    const out = schedule.tick(clock.t, METER, METER, { cardLive: stack.cardLive });
    for (const kind of out.fire) stack.oneshot(kind, { heat: out.heat });
    for (const layer of ['melt', 'blur', 'spiral', 'overlay']) stack.setSustained(layer, out.sustained[layer]);
    // dist.mjs primes three flashes and three drops every 1.8 s on top of the schedule
    if (clock.t >= nextPrime) { nextPrime += 1800; for (let i = 0; i < 3; i++) { stack.oneshot('flash', { heat: METER }); stack.oneshot('gifRain', { heat: METER }); } }
    // a turn every 5 s brings the card; a drag every 3 s wears the sticker for 1.2 s
    if (clock.t >= nextCard) { nextCard += 5000; stack.videoCard({ holdMs: 3500 }); }
    if (clock.t >= nextGrab) { nextGrab += 3000; dropAt = clock.t + 1200; stack.grab({ screen: { x: 400, y: 300 } }); }
    if (clock.t >= dropAt) { dropAt = Infinity; stack.drop({ ok: true }); }
    if (clock.t >= nextSample) {
      nextSample += 200;
      const shown = onScreenNow([root, front]);
      const media2 = shown.filter((s) => !isNoise(s.url));
      r.samples++;
      r.visible += media2.length;
      if (shown.some((s) => isNoise(s.url))) r.noiseSamples++;
      if (media2.some((s) => s.cls.includes('pbp-rain') && !isClip(s.url) && !/\.gif$/.test(s.url))) r.stillRain++;
      if (media2.some((s) => s.plane === 'front')) r.cardSeen++;
      if (media2.some((s) => s.box === 'pbp-grab')) r.grabSeen++;
      if (media2.some((s) => s.tag === 'IMG' && isClip(s.url))) r.clipInImg++;
      const counts = new Map();
      for (const s of media2) counts.set(s.url, (counts.get(s.url) || 0) + 1);
      const dups = [...counts].filter(([, n]) => n > 1);
      if (dups.length) {
        r.dupSamples++;
        r.maxCopies = Math.max(r.maxCopies, ...dups.map(([, n]) => n));
        if (!r.example) r.example = dups.map(([u, n]) => u + ' x' + n + ' [' + media2.filter((s) => s.url === u).map((s) => s.cls).join(' | ') + ']').join(' ; ');
      }
    }
    advance(90);
  }
  // a clear lets every picture go once the fades are over
  stack.clear();
  advance(2000);
  const dbg = stack.debug();
  r.heldAfterClear = dbg.onScreen;
  r.leftOnScreen = onScreenNow([root, front]).filter((s) => !isNoise(s.url)).length;
  stack.dispose();
  advance(2000);
  r.avg = r.visible / Math.max(1, r.samples);
  console.log(`  ${name}: samples=${r.samples} avgVisible=${r.avg.toFixed(1)} samplesWithDuplicate=${r.dupSamples} maxCopies=${r.maxCopies} noiseSamples=${r.noiseSamples} stillRain=${r.stillRain} card=${r.cardSeen} sticker=${r.grabSeen} heldAfterClear=${r.heldAfterClear}`);
  if (r.example) console.log('     first duplicate: ' + r.example);
  return r;
}

{
  const r = run('dup', { local: LOCAL, online: { images: ONLINE_IMAGES, clips: ONLINE_CLIPS } });
  expect(r.dupSamples === 0, 'library + online set: no picture twice on screen (' + r.dupSamples + '/' + r.samples + ' samples had one)');
  expect(r.avg >= 6, 'and the screen stays busy (' + r.avg.toFixed(1) + ' pictures on average)');
  expect(r.stillRain > 0, 'stills rain once every gif is up (' + r.stillRain + ' samples)');
  expect(r.noiseSamples === 0, 'a busy pool shows one fewer, never a noise tile');
  expect(r.grabSeen > 0, 'the grab sticker took part (' + r.grabSeen + ' samples)');
  expect(r.clipInImg === 0, 'a flash never takes a clip it cannot play (' + r.clipInImg + ' samples)');
  expect(r.heldAfterClear === 0 && r.leftOnScreen === 0, 'a clear lets every picture go (held ' + r.heldAfterClear + ')');
}
{
  // local videos too: the card shares the online clips with the rain, the overlay and the sticker
  const r = run('withVideos', { local: { ...LOCAL, videos: ['/lib/tape0.mp4', '/lib/tape1.mp4'] }, online: { images: ONLINE_IMAGES, clips: ONLINE_CLIPS } });
  expect(r.dupSamples === 0, 'with videos: no picture twice on screen (' + r.dupSamples + '/' + r.samples + ')');
  expect(r.cardSeen > 0, 'with videos: the card took part (' + r.cardSeen + ' samples)');
  expect(r.heldAfterClear === 0, 'with videos: nothing held after a clear');
}
{
  const r = run('onlineOnly', { local: NONE, online: { images: ONLINE_IMAGES, clips: ONLINE_CLIPS } });
  expect(r.dupSamples === 0, 'online only: no picture twice on screen (' + r.dupSamples + '/' + r.samples + ')');
  expect(r.avg >= 5, 'online only: pictures still up (' + r.avg.toFixed(1) + ')');
  expect(r.noiseSamples === 0, 'online only: no noise tile');
  expect(r.clipInImg === 0, 'online only: a flash never takes a clip it cannot play (' + r.clipInImg + ')');
  expect(r.heldAfterClear === 0, 'online only: nothing held after a clear');
}
{
  const r = run('coldOnline', { local: LOCAL, online: { images: ONLINE_IMAGES.slice(0, 2), clips: [] } });
  expect(r.dupSamples === 0, 'cold online set: no picture twice on screen (' + r.dupSamples + '/' + r.samples + ')');
  expect(r.avg >= 4, 'cold online set: fewer pictures, but pictures (' + r.avg.toFixed(1) + ' of 8 distinct)');
  expect(r.stillRain > 0, 'cold online set: stills rain once both gifs are up (' + r.stillRain + ' samples)');
  expect(r.noiseSamples === 0, 'cold online set: no noise tile');
  expect(r.heldAfterClear === 0, 'cold online set: nothing held after a clear');
}
{
  const r = run('emptyAll', { local: NONE, online: null, secs: 6 });
  expect(r.noiseSamples > 0, 'no pictures at all: the rain still falls as noise tiles');
  expect(r.heldAfterClear === 0, 'no pictures at all: nothing held after a clear');
}
{
  // a flash is an <img>: with the one still up and only a clip free, it shows one fewer, never a clip it cannot play
  _resetClips();
  body.children = [];
  const root = new El('div'); body.appendChild(root);
  const media = createFixtureMedia([{ kind: 'image', url: '/only.png' }, { kind: 'gif', url: '/only.webm' }]);
  const stack = createLayerStack({ root, media, tuning: RAMP_TUNING, rng: makeRng('flash-clip') });
  stack.oneshot('flash', { heat: 0.5 });
  stack.oneshot('flash', { heat: 0.5 });
  const imgs = onScreenNow([root]).filter((s) => s.tag === 'IMG').map((s) => s.url);
  expect(imgs.length === 1 && imgs[0] === '/only.png', 'the one still is up and a clip is free: the flash skips (' + imgs.join(',') + ')');
  stack.dispose();
  advance(3000);
}
expect(thrown.length === 0, 'no layer threw on the fake clock' + (thrown.length ? ' (' + thrown[0] + ')' : ''));

console.log(`\nonscreen-smoke: ${passed} passed, ${failed} failed`);
process.exit(failed ? 1 : 0);
