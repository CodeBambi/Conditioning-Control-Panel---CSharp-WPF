/* ============================================================================
 * games/sort/smoke/wall-live-check.mjs - THE WALL'S LIVE BUDGET (ccp-bugs #1197).
 *
 *   node games/sort/smoke/wall-live-check.mjs     (from Resources/web/arcademy; 0 on pass)
 *
 * The bug: "The sort room mini-game in The Arcademy begins to lag out from too
 * many gifs playing in the background to the point the timer starts only
 * updating at like 1 frame every few seconds and some of the images don't load
 * in at all." The wall held one live <img> per landed card, up to WALL.CAP of
 * them, and in a gif-heavy library every one of those is an animated decode
 * that never stops - on the main thread, next to the class's own 250ms clock.
 *
 * This drives the REAL `wall.js` against a DOM double and a fake clock with a
 * library of 300 gifs, and holds it to the budget the fix put in:
 *
 *   - at most WALL.LIVE_FACES tiles hold a live <img>, at every single landing;
 *   - at most WALL.DECODE_LANES urls are ever in flight;
 *   - every older tile is a frozen <canvas>, one drawImage and never again;
 *   - a url that errors is SKIPPED and COUNTED, and leaves no <img> behind;
 *   - a recycled slot lets go of its old face;
 *   - destroy() takes every decoder with it.
 *
 * There is no test seam in the shipped file: the double is a document, the
 * clock is globalThis.setTimeout, and wall.js resolves both at call time.
 * ==========================================================================*/

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');
const le = (got, want, what) => ok(got <= want, what + ' (got ' + got + ', want <= ' + want + ')');

/* ------------------------------------------------------------ the clock -- */
const timers = new Map();
let nextTimer = 1;
globalThis.setTimeout = (fn, ms) => { const id = nextTimer++; timers.set(id, { fn, ms }); return id; };
globalThis.clearTimeout = (id) => { timers.delete(id); };

/* -------------------------------------------------------------- the DOM -- */
const pending = [];          // every <img> whose src was set and has not answered

function makeClassList(node) {
  const set = new Set();
  const sync = () => { node.className = [...set].join(' '); };
  return {
    add(...c) { c.forEach((x) => set.add(x)); sync(); },
    remove(...c) { c.forEach((x) => set.delete(x)); sync(); },
    contains(c) { return set.has(c); },
  };
}

function makeNode(tag) {
  const t = String(tag).toLowerCase();
  const node = {
    tagName: t.toUpperCase(),
    className: '',
    children: [],
    parentNode: null,
    attrs: Object.create(null),
    listeners: Object.create(null),
    style: { props: Object.create(null), setProperty(k, v) { this.props[k] = v; } },
    appendChild(c) {
      if (c && c.parentNode) c.parentNode.removeChild(c);
      if (c) c.parentNode = node;
      node.children.push(c);
      return c;
    },
    insertBefore(c, ref) {
      if (c && c.parentNode) c.parentNode.removeChild(c);
      const at = node.children.indexOf(ref);
      if (c) c.parentNode = node;
      if (at < 0) node.children.push(c); else node.children.splice(at, 0, c);
      return c;
    },
    removeChild(c) {
      const at = node.children.indexOf(c);
      if (at >= 0) node.children.splice(at, 1);
      if (c) c.parentNode = null;
      return c;
    },
    remove() { if (node.parentNode) node.parentNode.removeChild(node); },
    setAttribute(k, v) { node.attrs[k] = String(v); },
    removeAttribute(k) { delete node.attrs[k]; if (k === 'src') node._src = ''; },
    addEventListener(type, fn) { (node.listeners[type] || (node.listeners[type] = [])).push(fn); },
    dispatch(type) { (node.listeners[type] || []).slice().forEach((fn) => { try { fn(); } catch (e) { /* noop */ } }); },
    get textContent() { return ''; },
    set textContent(v) { if (!v) node.children.slice().forEach((c) => node.removeChild(c)); },
  };
  node.classList = makeClassList(node);
  if (t === 'img') {
    node._src = '';
    node.naturalWidth = 64;
    node.naturalHeight = 48;
    Object.defineProperty(node, 'src', {
      get() { return node._src; },
      set(v) { node._src = String(v); pending.push(node); },
    });
  }
  if (t === 'canvas') {
    node.width = 0;
    node.height = 0;
    node.draws = 0;
    node.getContext = () => ({ drawImage() { node.draws += 1; } });
  }
  return node;
}

globalThis.document = { createElement: makeNode };

const { createWall, WALL } = await import('../wall.js');

/* ------------------------------------------------------------- counters -- */
function walk(node, out) {
  if (!node) return out;
  out.push(node);
  (node.children || []).forEach((c) => walk(c, out));
  return out;
}
function liveImgs(root) {
  return walk(root, []).filter((n) => n.tagName === 'IMG' && n._src);
}
function stills(root) {
  return walk(root, []).filter((n) => n.tagName === 'CANVAS');
}
/** Answer every <img> that has been handed a src. */
function flush(kind) {
  const batch = pending.splice(0, pending.length);
  batch.forEach((img) => img.dispatch(kind || 'load'));
  return batch.length;
}

const cards = [];
for (let i = 0; i < 300; i++) cards.push({ i, url: 'https://ccp.assets/lib/gif-' + i + '.gif', mime: 'image/gif', tag: i % 3 === 0 ? 'noise' : 'target' });

/* ================================================== 1. THE LIVE BUDGET === */
{
  const mount = makeNode('div');
  const wall = createWall({ mount, tier: 4, reduced: false, seed: 'smoke' });
  let worstLive = 0;
  let worstInFlight = 0;
  for (const card of cards) {
    wall.land(card, { wrong: card.i % 7 === 0 });
    worstInFlight = Math.max(worstInFlight, pending.length);
    flush('load');
    worstLive = Math.max(worstLive, liveImgs(wall.el).length);
  }
  const d = wall.diagnostics();
  le(worstLive, WALL.LIVE_FACES, '300 gifs land and the wall never holds more than LIVE_FACES live <img>');
  le(worstInFlight, WALL.DECODE_LANES, 'never more than DECODE_LANES urls in flight at once');
  eq(d.live, WALL.LIVE_FACES, 'the live window is full at the end');
  eq(d.painted, 300, 'every one of the 300 urls painted');
  eq(d.frozen, 300 - WALL.LIVE_FACES, 'everything older than the window froze');
  eq(d.skipped, 0, 'nothing was skipped on a clean library');
  eq(d.stuck, 0, 'no tile refused to freeze');
  eq(d.queued, 0, 'the queue drained');
  eq(d.landed, 300, 'the ledger still counts every landing');
  eq(d.tiles, WALL.CAP, 'the wall recycled at CAP and grew no further');
  /* the collage is intact: every tile the recycle left alone still shows a face */
  ok(stills(wall.el).length === 300 - WALL.LIVE_FACES - (300 - WALL.CAP),
    'a frozen still per surviving tile, minus the live window (got ' + stills(wall.el).length + ')');
  ok(stills(wall.el).every((c) => c.draws === 1), 'each still was drawn exactly once');
  ok(stills(wall.el).every((c) => c.width === WALL.STILL_PX && c.height === WALL.STILL_PX),
    'a still is a square STILL_PX backing store');

  wall.destroy();
  eq(liveImgs(wall.el).length, 0, 'destroy() lets go of every decoder');
}

/* ============================================ 2. A SLOW LIBRARY QUEUES === */
{
  pending.length = 0;
  const mount = makeNode('div');
  const wall = createWall({ mount, tier: 4, reduced: false, seed: 'slow' });
  for (const card of cards) wall.land(card, {});     // nothing answers
  const d = wall.diagnostics();
  le(liveImgs(wall.el).length, WALL.DECODE_LANES,
    'a library that never answers still has only DECODE_LANES <img> on the wall at once');
  eq(d.lanes, WALL.DECODE_LANES, 'the lanes are all claimed');
  eq(d.live, 0, 'nothing is live until something loads');
  ok(d.queued > 0, 'the rest is queued, not in flight');
  /* the lane timeout hands a lane back WITHOUT touching the <img>: it is a
   * concurrency claim, not a deadline, so a slow url may still land. */
  const armed = [...timers.values()];
  const held = liveImgs(wall.el).length;
  ok(armed.length >= WALL.DECODE_LANES, 'each lane armed its LANE_MS timeout');
  ok(armed.every((t) => t.ms === WALL.LANE_MS), 'the lane timeout is LANE_MS');
  const queuedBefore = d.queued;
  armed.forEach((t) => t.fn());
  const after = wall.diagnostics();
  ok(after.queued < queuedBefore, 'a timed-out lane goes back to the pool and the queue moves on');
  ok(liveImgs(wall.el).length > held, 'the next urls started');
  wall.destroy();
}

/* ================================================ 3. A DEAD URL SKIPS === */
{
  pending.length = 0;
  timers.clear();
  const mount = makeNode('div');
  const wall = createWall({ mount, tier: 4, reduced: false, seed: 'dead' });
  const some = cards.slice(0, 40);
  some.forEach((card, i) => {
    wall.land(card, {});
    flush(i % 4 === 0 ? 'error' : 'load');
  });
  const d = wall.diagnostics();
  eq(d.skipped, 10, 'every dead url was counted');
  eq(wall.skipped, 10, 'and the class can read the count off the wall');
  eq(d.painted, 30, 'the healthy ones still painted');
  le(liveImgs(wall.el).length, WALL.LIVE_FACES, 'a skip does not leak a live <img>');
  /* a skipped tile keeps its drawn back and its slot - never a hole */
  eq(d.landed, 40, 'a skipped card still landed');
  eq(d.tiles, 40, 'and still owns its slot');
  wall.destroy();
}

/* ============================================ 4. THE RECYCLE LETS GO === */
{
  pending.length = 0;
  timers.clear();
  const mount = makeNode('div');
  const wall = createWall({ mount, tier: 4, reduced: false, seed: 'recycle' });
  for (let i = 0; i < WALL.CAP + 5; i++) { wall.land(cards[i % cards.length], {}); flush('load'); }
  const d = wall.diagnostics();
  eq(d.tiles, WALL.CAP, 'the wall recycles instead of growing');
  le(liveImgs(wall.el).length, WALL.LIVE_FACES, 'a recycled slot did not leave its old <img> behind');
  /* slot 0 was landed twice: exactly one face, the newest */
  const slot0 = walk(wall.el, []).find((n) => n.attrs && n.attrs['data-slot'] === '0');
  ok(!!slot0, 'slot 0 exists');
  le(walk(slot0, []).filter((n) => n.tagName === 'IMG' || n.tagName === 'CANVAS').length, 1,
    'a recycled tile holds exactly one face');
  wall.destroy();
}

/* ===================================== 5. VIDEO STILL GETS NO ELEMENT === */
{
  pending.length = 0;
  timers.clear();
  const mount = makeNode('div');
  const wall = createWall({ mount, tier: 4, reduced: false, seed: 'video' });
  wall.land({ i: 1, url: 'https://ccp.assets/lib/clip.mp4', mime: 'video/mp4' }, {});
  wall.land({ i: 2, url: 'https://ccp.assets/lib/clip2.webm' }, {});
  eq(pending.length, 0, 'a video url mints no <img> (owner 2026-08-24, unchanged)');
  eq(wall.diagnostics().landed, 2, 'but the card still landed');
  wall.destroy();
}

console.log(fails ? '\n' + fails + ' FAILED' : '\nall green');
process.exit(fails ? 1 : 0);
