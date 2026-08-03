// Self-contained sanity pass over exec/ — the element/payload renderers and the
// executor that fans out to them.
//   node Resources/web/goon/test/selftest-exec.js
//
// exec/ is the one tier that touches the DOM, so this file carries a MINIMAL DOM
// stub (below) instead of pulling in jsdom: enough element surface for the
// renderers to build, class, style, mount and tear down their nodes, plus event
// dispatch so the lock card can be typed into. Anything the stub does not
// implement (matchMedia, requestAnimationFrame, HTMLVideoElement.play) is absent
// on purpose — every module must already guard for a host that lacks it, and a
// throw here means that guard is missing.

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const quiet = { info() {}, warn() {}, error() {} };
// NOT unref'd on purpose: every timer inside exec/ is unref'd (so the page's
// renderers can never hold node's loop open), which means this await chain is
// the only thing keeping the process alive between checks.
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/* ===========================================================================
 * THE DOM STUB
 * ========================================================================= */
class StubStyle {
  constructor() { this._p = new Map(); }
  setProperty(k, v) { this._p.set(k, String(v)); }
  getPropertyValue(k) { return this._p.get(k) || ''; }
}
class StubEl {
  constructor(tag) {
    this.tagName = String(tag).toUpperCase();
    this.childNodes = [];
    this.parentNode = null;
    this.isConnected = false;
    this.style = new StubStyle();
    this.textContent = '';
    this.value = '';
    this.offsetWidth = 180;
    this.offsetHeight = 32;
    this._cls = new Set();
    this._attrs = new Map();
    this._listeners = new Map();
    this._focused = false;
  }
  get className() { return Array.from(this._cls).join(' '); }
  set className(v) { this._cls = new Set(String(v).split(/\s+/).filter(Boolean)); }
  get classList() {
    const set = this._cls;
    return {
      add: (...c) => c.forEach((x) => set.add(x)),
      remove: (...c) => c.forEach((x) => set.delete(x)),
      contains: (c) => set.has(c),
      toggle: (c, on) => (on ? set.add(c) : set.delete(c)),
    };
  }
  get firstChild() { return this.childNodes[0] || null; }
  appendChild(c) {
    if (c.parentNode) c.parentNode.removeChild(c);
    c.parentNode = this;
    c.isConnected = this.isConnected;
    markConnected(c, this.isConnected);
    this.childNodes.push(c);
    return c;
  }
  prepend(c) { this.appendChild(c); }
  removeChild(c) {
    const i = this.childNodes.indexOf(c);
    if (i >= 0) this.childNodes.splice(i, 1);
    c.parentNode = null;
    markConnected(c, false);
    return c;
  }
  remove() { if (this.parentNode) this.parentNode.removeChild(this); else markConnected(this, false); }
  replaceChildren(...kids) {
    for (const c of this.childNodes.slice()) { c.parentNode = null; markConnected(c, false); }
    this.childNodes = [];
    for (const k of kids) this.appendChild(k);
  }
  setAttribute(k, v) { this._attrs.set(k, String(v)); }
  getAttribute(k) { return this._attrs.has(k) ? this._attrs.get(k) : null; }
  removeAttribute(k) { this._attrs.delete(k); if (k === 'src') this.src = ''; }
  addEventListener(t, fn) {
    if (!this._listeners.has(t)) this._listeners.set(t, []);
    this._listeners.get(t).push(fn);
  }
  removeEventListener(t, fn) {
    const a = this._listeners.get(t);
    if (!a) return;
    const i = a.indexOf(fn);
    if (i >= 0) a.splice(i, 1);
  }
  /** test-only: fire a listener set synchronously */
  fire(t) {
    for (const fn of (this._listeners.get(t) || []).slice()) fn({ type: t, preventDefault() {} });
  }
  focus() { this._focused = true; }
  /** test-only: every descendant with a class */
  findAll(cls, out = []) {
    for (const c of this.childNodes) {
      if (c._cls && c._cls.has(cls)) out.push(c);
      if (c.findAll) c.findAll(cls, out);
    }
    return out;
  }
  countDescendants() {
    let k = this.childNodes.length;
    for (const c of this.childNodes) k += c.countDescendants ? c.countDescendants() : 0;
    return k;
  }
}
function markConnected(el, on) {
  el.isConnected = !!on;
  for (const c of el.childNodes || []) markConnected(c, on);
}

const LAYER_IDS = ['gg-fx-flash', 'gg-fx-sub', 'gg-fx-bubbles', 'gg-fx-spiral', 'gg-fx-bounce',
  'gg-fx-drain', 'gg-stage', 'gg-fx'];
const byId = new Map();
for (const id of LAYER_IDS) {
  const el = new StubEl('div');
  el.setAttribute('id', id);
  el.isConnected = true;
  byId.set(id, el);
}
const documentElement = new StubEl('html');
documentElement.isConnected = true;

globalThis.document = {
  documentElement,
  body: (() => { const b = new StubEl('body'); b.isConnected = true; return b; })(),
  createElement: (tag) => new StubEl(tag),
  createTextNode: (t) => { const e = new StubEl('#text'); e.textContent = t; return e; },
  getElementById: (id) => byId.get(id) || null,
};
globalThis.window = { innerWidth: 1280, innerHeight: 720 };
// Deliberately NOT defined: matchMedia, requestAnimationFrame, Image.

/* ===========================================================================
 * IMPORTS (after the stub — layers.js resolves the DOM lazily, but the modules
 * must also be import-safe without it; that is what selftest's node -e sweep
 * covers, and this file covers the with-DOM path).
 * ========================================================================= */
const { createExecutor, PAYLOAD_ELEMENT } = await import('../exec/executor.js');
const { createLockCardView } = await import('../exec/lockCards.js');
const layers = await import('../exec/layers.js');
const { pickSpiralUrl, SPIRAL_POOL } = await import('../exec/spiral.js');
const { GoonElement, GoonPayloadKind } = await import('../core/contracts.js');
const { localMonotonicMs } = await import('../core/clock.js');

/* ===========================================================================
 * FAKES
 * ========================================================================= */
function fakeMedia() {
  return {
    drawKind: (kind) => ({ kind, name: `${kind}-1`, url: `https://ccp.assets/${kind}/1` }),
    draw: () => ({ kind: 'image', name: 'i', url: 'https://ccp.assets/image/1' }),
    acquire: (e) => (e ? { url: e.url, release() {} } : null),
    hasMedia: () => true,
  };
}

function fakeMatch() {
  const subs = { start: [], intensity: [], stop: [], payload: [] };
  const receipts = [];
  const sub = (bag, fn) => { bag.push(fn); return () => { const i = bag.indexOf(fn); if (i >= 0) bag.splice(i, 1); }; };
  return {
    subs,
    receipts,
    subCount: () => subs.start.length + subs.intensity.length + subs.stop.length + subs.payload.length,
    onElementStartRequested: (fn) => sub(subs.start, fn),
    onElementIntensityChanged: (fn) => sub(subs.intensity, fn),
    onElementStopRequested: (fn) => sub(subs.stop, fn),
    onPayloadAccepted: (fn) => sub(subs.payload, fn),
    notifyInboundPayloadFinished: (id, endured) => receipts.push({ id, endured }),
    emitStart: (cue) => subs.start.slice().forEach((f) => f(cue)),
    emitIntensity: (cue) => subs.intensity.slice().forEach((f) => f(cue)),
    emitStop: (cue) => subs.stop.slice().forEach((f) => f(cue)),
    emitPayload: (evt) => subs.payload.slice().forEach((f) => f(evt)),
  };
}

function spyOn(ex, code) {
  const r = ex.rendererFor(code);
  const calls = { start: 0, setIntensity: 0, stop: 0, renderPayload: 0 };
  for (const k of Object.keys(calls)) {
    const orig = r[k].bind(r);
    r[k] = (...a) => { calls[k]++; return orig(...a); };
  }
  return calls;
}

const payloadOf = (o) => Object.assign({
  id: 'p1h', kind: GoonPayloadKind.ToyPattern, duration_ms: 1000,
  tags: null, text: '', voice: false, pattern: null, intensity: 0.5,
}, o);

/* ===========================================================================
 * TESTS
 * ========================================================================= */
async function main() {
  // ------------------------------------------------------------- attach/detach
  {
    const ex = createExecutor({ media: fakeMedia(), layers, audio: { sfx() {} }, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ok(m.subCount() === 0, 'no subscriptions before attach');
    ex.attach(m);
    ok(m.subCount() === 4, 'attach subscribes to all 4 seam events', String(m.subCount()));
    ok(ex.activeCount() === 0, 'nothing active after attach');
    ex.detach();
    ok(m.subCount() === 0, 'detach unsubscribes everything', String(m.subCount()));

    // Post-detach cues must not reach a renderer (nobody is listening).
    m.emitStart({ element: GoonElement.Flashes, intensity: 0.5, durationMs: 0, elapsedMs: 0 });
    ok(ex.activeCount() === 0, 'cue after detach is a no-op');
  }

  // ------------------------------------------------------------ element routing
  {
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);

    const codes = [
      GoonElement.Flashes, GoonElement.Videos, GoonElement.Subliminals, GoonElement.Bubbles,
      GoonElement.LockCards, GoonElement.ToyPatterns, GoonElement.BrainDrain, GoonElement.BouncingText,
      GoonElement.Spiral,
    ];
    const spies = new Map(codes.map((c) => [c, spyOn(ex, c)]));

    for (const element of codes) {
      m.emitStart({ element, intensity: 0.4, durationMs: 0, elapsedMs: 0 });
      ok(spies.get(element).start === 1, `element ${element} routed to its renderer.start`);
    }
    ok(ex.activeCount() === codes.length, 'every element registered active', String(ex.activeCount()));
    ok(documentElement.getAttribute('data-gg-fx') === 'hot', 'fx heat hot at every element running',
      String(documentElement.getAttribute('data-gg-fx')));

    for (const element of codes) {
      m.emitIntensity({ element, intensity: 0.9, durationMs: 0, elapsedMs: 1000 });
      ok(spies.get(element).setIntensity === 1, `element ${element} intensity re-tunes the running renderer`);
    }
    // A second start for an already-running element re-tunes instead of restarting.
    m.emitStart({ element: GoonElement.Flashes, intensity: 0.7, durationMs: 0, elapsedMs: 1200 });
    ok(spies.get(GoonElement.Flashes).start === 1, 'duplicate start does not restart the renderer');
    ok(spies.get(GoonElement.Flashes).setIntensity === 2, 'duplicate start re-tunes instead');

    // An intensity cue for something never started IS a start (ramp is authority).
    ex.stopAll();
    m.emitIntensity({ element: GoonElement.Bubbles, intensity: 0.5, durationMs: 0, elapsedMs: 2000 });
    ok(spies.get(GoonElement.Bubbles).start === 2, 'intensity for an unstarted element starts it');
    ok(ex.activeCount() === 1, 'registry tracks the implicit start');

    for (const element of codes) m.emitStop({ element, intensity: 0, durationMs: 0, elapsedMs: 3000 });
    ok(ex.activeCount() === 0, 'stop cues drain the registry', String(ex.activeCount()));
    ok(documentElement.getAttribute('data-gg-fx') === 'idle', 'fx heat back to idle');
    ex.detach();
  }

  // -------------------------------------------------------- payload: completion
  {
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);

    const p = payloadOf({ id: 'pA', kind: GoonPayloadKind.ToyPattern, duration_ms: 1000 });
    m.emitPayload({ payload: p, fireAtLocalMs: localMonotonicMs() + 40 });
    ok(ex.activeCount() === 1, 'accepted payload registers immediately (pending counts as load)');
    ok(m.receipts.length === 0, 'no receipt before the payload finishes');

    await sleep(1400);
    ok(m.receipts.length === 1, 'exactly one receipt after completion', JSON.stringify(m.receipts));
    ok(m.receipts[0] && m.receipts[0].id === 'pA' && m.receipts[0].endured === true,
      'completion receipts endured=true', JSON.stringify(m.receipts[0]));
    ok(ex.activeCount() === 0, 'payload deregisters on completion');
    ex.detach();
  }

  // ------------------------------------------------------ payload: interruption
  {
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);

    m.emitPayload({ payload: payloadOf({ id: 'pB', duration_ms: 60000 }), fireAtLocalMs: localMonotonicMs() });
    await sleep(60);
    ok(ex.activeCount() === 1, 'long payload is running');
    ex.stopAll();
    ok(m.receipts.length === 1, 'interrupted payload receipts exactly once', JSON.stringify(m.receipts));
    ok(m.receipts[0].endured === false, 'interruption receipts endured=false');
    ok(ex.activeCount() === 0, 'stopAll clears the registry');
    ok(documentElement.getAttribute('data-gg-fx') === 'idle', 'stopAll drops heat to idle');

    // Nothing may arrive late from the cancelled render.
    await sleep(300);
    ok(m.receipts.length === 1, 'no late second receipt after cancel', String(m.receipts.length));
    ex.detach();
  }

  // ------------------------------------------- payload: cancelled while PENDING
  {
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);
    m.emitPayload({ payload: payloadOf({ id: 'pC' }), fireAtLocalMs: localMonotonicMs() + 30000 });
    ok(ex.activeCount() === 1, 'pending payload is registered');
    ex.stopAll();
    ok(m.receipts.length === 1 && m.receipts[0].endured === false,
      'a payload cancelled before it fires still receipts once', JSON.stringify(m.receipts));
    await sleep(120);
    ok(m.receipts.length === 1, 'pending cancel does not double-receipt');
    ex.detach();
  }

  // --------------------------------------------------- payload: dupe + unknown
  {
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);

    const p = payloadOf({ id: 'pD', duration_ms: 1000 });
    m.emitPayload({ payload: p, fireAtLocalMs: localMonotonicMs() + 30 });
    m.emitPayload({ payload: p, fireAtLocalMs: localMonotonicMs() + 30 });   // duplicate id
    ok(ex.activeCount() === 1, 'duplicate payload id is ignored', String(ex.activeCount()));
    await sleep(1400);
    ok(m.receipts.filter((r) => r.id === 'pD').length === 1, 'duplicate id receipts exactly once',
      JSON.stringify(m.receipts));

    m.emitPayload({ payload: payloadOf({ id: 'pE', kind: 99 }), fireAtLocalMs: localMonotonicMs() });
    const unknown = m.receipts.filter((r) => r.id === 'pE');
    ok(unknown.length === 1 && unknown[0].endured === false,
      'unknown payload kind receipts completed once (never strands the sender)', JSON.stringify(unknown));
    ok(ex.activeCount() === 0, 'unknown kind leaves nothing registered');
    ex.detach();
  }

  // ------------------------------------------------- payload -> element mapping
  {
    const kinds = Object.values(GoonPayloadKind);
    let mapped = 0;
    for (const k of kinds) if (PAYLOAD_ELEMENT[k] !== undefined) mapped++;
    ok(mapped === kinds.length, 'every payload kind maps to an element', `${mapped}/${kinds.length}`);
  }

  // ------------------------------------------------------- detach settles a run
  {
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);
    m.emitStart({ element: GoonElement.Bubbles, intensity: 0.8, durationMs: 0, elapsedMs: 0 });
    m.emitPayload({ payload: payloadOf({ id: 'pF', duration_ms: 60000 }), fireAtLocalMs: localMonotonicMs() });
    await sleep(60);
    ex.detach();
    ok(m.receipts.length === 1 && m.receipts[0].endured === false,
      'detach receipts the in-flight payload before dropping the match', JSON.stringify(m.receipts));
    ok(ex.activeCount() === 0, 'detach clears the registry');
    ok(byId.get('gg-fx-bubbles').childNodes.length === 0, 'detach cleared the bubble layer');
  }

  // ------------------------------------------- renderers actually mount nodes
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);
    for (const element of [GoonElement.Subliminals, GoonElement.Bubbles, GoonElement.BrainDrain,
      GoonElement.BouncingText, GoonElement.Videos, GoonElement.LockCards, GoonElement.Spiral]) {
      m.emitStart({ element, intensity: 0.8, durationMs: 0, elapsedMs: 0 });
    }
    await sleep(120);
    ok(byId.get('gg-fx-sub').childNodes.length > 0, 'subliminals mounted a word node');
    ok(byId.get('gg-fx-bubbles').childNodes.length > 0, 'bubbles mounted a bubble node');
    ok(byId.get('gg-fx-drain').childNodes.length === 1, 'brain drain mounted exactly one veil pane',
      String(byId.get('gg-fx-drain').childNodes.length));
    ok(byId.get('gg-fx-spiral').childNodes.length === 1, 'spiral mounted exactly one pane',
      String(byId.get('gg-fx-spiral').childNodes.length));
    ok(byId.get('gg-fx-bounce').childNodes.length > 0, 'bouncing text mounted a phrase');
    ok(byId.get('gg-stage').findAll('gg-vid').length === 1, 'video mounted one surface on the stage');
    ok(byId.get('gg-stage').findAll('gg-lock').length === 1, 'lock card mounted one card on the stage');

    ex.stopAll();
    let leftovers = 0;
    for (const id of LAYER_IDS) leftovers += byId.get(id).childNodes.length;
    ok(leftovers === 0, 'stopAll leaves every layer empty', String(leftovers));
    ex.detach();
  }

  // ------------------------------------------------------------ toy bridge fan-out
  {
    const sent = [];
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: (msg) => sent.push(msg) });
    const m = fakeMatch();
    ex.attach(m);
    m.emitStart({ element: GoonElement.ToyPatterns, intensity: 0.5, durationMs: 0, elapsedMs: 0 });
    ok(sent.length === 1 && sent[0].type === 'toy-pattern' && sent[0].source === 'element',
      'toy element forwards toy-pattern', JSON.stringify(sent[0]));
    m.emitPayload({ payload: payloadOf({ id: 'pG', duration_ms: 1000, intensity: 0.9 }), fireAtLocalMs: localMonotonicMs() });
    await sleep(80);
    ok(sent.some((s) => s.source === 'payload'), 'toy payload forwards with source=payload');
    await sleep(1200);
    ok(sent[sent.length - 1].source === 'element', 'toy payload hands the bed back instead of stopping it',
      JSON.stringify(sent[sent.length - 1]));
    m.emitStop({ element: GoonElement.ToyPatterns, intensity: 0, durationMs: 0, elapsedMs: 0 });
    ok(sent[sent.length - 1].type === 'toy-stop', 'toy element stop forwards toy-stop');
    ex.detach();
  }

  // ------------------------------------------------------- spiral registration
  {
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    ok(ex.rendererFor(GoonElement.Spiral) !== null, 'GoonElement.Spiral has a renderer');
    ok(ex.rendererFor(GoonElement.Spiral).name === 'spiral', 'the spiral renderer is named lowercase',
      String(ex.rendererFor(GoonElement.Spiral).name));
    ok(PAYLOAD_ELEMENT[GoonPayloadKind.Spiral] === GoonElement.Spiral,
      'GoonPayloadKind.Spiral routes to GoonElement.Spiral', String(PAYLOAD_ELEMENT[GoonPayloadKind.Spiral]));

    // The pool is the DtRH bundle, read off the same ccp.game origin.
    ok(SPIRAL_POOL.length === 7, 'the bundled spiral pool has all 7 entries', String(SPIRAL_POOL.length));
    let poolHits = 0;
    for (let i = 0; i < 40; i++) {
      const u = pickSpiralUrl();
      if (u.startsWith('/dtrh/assets/bubbles/effects/spirals/') && SPIRAL_POOL.some((f) => u.endsWith(f))) poolHits++;
    }
    ok(poolHits === 40, 'every picked spiral url is a bundled DtRH spiral', String(poolHits));
  }

  // ------------------------------------------------------------ spiral renders
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);

    m.emitPayload({
      payload: payloadOf({ id: 'pSp', kind: GoonPayloadKind.Spiral, duration_ms: 1000, intensity: 0.9 }),
      fireAtLocalMs: localMonotonicMs(),
    });
    await sleep(120);
    const panes = byId.get('gg-fx-spiral').findAll('gg-spiral');
    ok(panes.length === 1, 'spiral payload mounts exactly one pane', String(panes.length));
    ok(String(panes[0].style.getPropertyValue('background-image')).includes('/dtrh/assets/bubbles/effects/spirals/'),
      'the pane draws a bundled DtRH spiral', panes[0].style.getPropertyValue('background-image'));
    const op = parseFloat(panes[0].style.getPropertyValue('--gg-spiral-op'));
    ok(op >= 0.25 && op <= 0.7, 'spiral opacity stays inside the 0.25..0.70 band', String(op));

    // A running element must not stack a SECOND pane under the payload.
    m.emitStart({ element: GoonElement.Spiral, intensity: 0.4, durationMs: 0, elapsedMs: 0 });
    ok(byId.get('gg-fx-spiral').findAll('gg-spiral').length === 1,
      'element + payload share one pane', String(byId.get('gg-fx-spiral').findAll('gg-spiral').length));

    await sleep(1100);
    ok(m.receipts.length === 1 && m.receipts[0].endured === true,
      'a spiral payload that ran its duration receipts endured', JSON.stringify(m.receipts));
    ex.detach();
  }

  // -------------------------------------------------- bubbles: the DtRH field
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);

    m.emitPayload({
      payload: payloadOf({ id: 'pBub', kind: GoonPayloadKind.BubbleSwarm, duration_ms: 1000, intensity: 0.9 }),
      fireAtLocalMs: localMonotonicMs(),
    });
    await sleep(300);
    const host = byId.get('gg-fx-bubbles');
    const bubbles = host.findAll('gg-bubble');
    ok(bubbles.length > 0, 'the swarm mounted real bubbles', String(bubbles.length));
    ok(bubbles.every((b) => /gg-bubble--/.test(b.className)),
      'every bubble carries a DtRH kind class', bubbles.map((b) => b.className).join('|'));
    const wrap = host.findAll('gg-bubble-wrap')[0];
    ok(!!wrap && /s$/.test(wrap.style.getPropertyValue('--gg-rise')),
      'the wrap rises on a seconds-valued --gg-rise', wrap && wrap.style.getPropertyValue('--gg-rise'));

    // POPPING IS COSMETIC: it must not touch the payload's receipt.
    const b = bubbles[0];
    b.fire('pointerdown');
    ok(b._cls.has('is-pop'), 'a bubble pops on pointerdown');
    ok(host.findAll('gg-spark').length === 9, 'the pop threw its sparkle burst',
      String(host.findAll('gg-spark').length));
    ok(m.receipts.length === 0, 'popping does not settle the swarm early');

    await sleep(1500);
    ok(m.receipts.length === 1 && m.receipts[0].endured === true,
      'the swarm still receipts endured after its full duration', JSON.stringify(m.receipts));
    ex.detach();
  }

  // -------------------------------------- flashes: the DtRH scatter + the hydra
  {
    const { MAX_LIVE, HYDRA_CHILDREN, BASE_HOLD_MS } = await import('../exec/flashes.js');
    ok(MAX_LIVE === 20 && HYDRA_CHILDREN === 2 && BASE_HOLD_MS === 5000,
      'flash budget is 20 on screen, 2 per split, ~5s each',
      `${MAX_LIVE}/${HYDRA_CHILDREN}/${BASE_HOLD_MS}`);

    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const host = byId.get('gg-fx-flash');
    const heard = [];
    const ex = createExecutor({
      media: fakeMedia(), layers, logger: quiet, toyBridge: null,
      audio: { sfx: (id) => heard.push(id) },
    });
    const m = fakeMatch();
    ex.attach(m);
    // A dismissed flash lingers in the DOM for its pop-out; "on screen" is the
    // un-popped population, which is also what the cap counts.
    const shotsOf = () => host.findAll('gg-flash').filter((el) => !el._cls.has('is-popped'));

    m.emitStart({ element: GoonElement.Flashes, intensity: 1, durationMs: 0, elapsedMs: 0 });
    await sleep(60);
    const shots = shotsOf();
    ok(shots.length > 0, 'the flash bed mounted a scattered image', String(shots.length));
    const f = shots[0];
    ok(/vw$/.test(f.style.getPropertyValue('left')) && /vh$/.test(f.style.getPropertyValue('top')),
      'flashes scatter in vw/vh like payloadFx', `${f.style.getPropertyValue('left')} ${f.style.getPropertyValue('top')}`);
    ok(/deg$/.test(f.style.getPropertyValue('--gg-flash-rot')), 'flashes carry a rotation',
      f.style.getPropertyValue('--gg-flash-rot'));
    const dur = parseFloat(f.style.getPropertyValue('--gg-flash-dur'));
    ok(/ms$/.test(f.style.getPropertyValue('--gg-flash-dur')) && dur >= 4600 && dur <= 5400,
      'a flash holds ~5s on screen', f.style.getPropertyValue('--gg-flash-dur'));
    ok(f._cls.has('gg-flash--hydra'),
      'flashes carry the pointer opt-in modifier (bare .gg-flash stays click-through)', f.className);

    // Freeze the bed: the hydra arithmetic below must not race the cadence.
    // Airborne flashes stay up (and stay clickable) for the rest of their 5s.
    m.emitStop({ element: GoonElement.Flashes, intensity: 0, durationMs: 0, elapsedMs: 0 });
    const atStop = shotsOf().length;
    await sleep(320);                              // longer than any pending beat stagger
    const before = shotsOf().length;
    ok(before === atStop, 'a stopped bed spawns nothing more (the beat stagger honours stop)',
      `${atStop} -> ${before}`);
    ok(before > 0, 'stopping the bed leaves the airborne flashes on screen', String(before));

    const target = shotsOf()[0];
    target.fire('pointerdown');
    ok(target._cls.has('is-popped'), 'a clicked flash pops out');
    ok(heard.includes('flash-pop'), 'the click hits the flash-pop sfx hook', heard.join(','));
    await sleep(320);                              // both children hatch by 210ms
    ok(shotsOf().length === before + 1,
      'one click dismisses one and hatches two', `${before} -> ${shotsOf().length}`);
    const kid = shotsOf().find((el) => el._cls.has('gg-flash--hatch'));
    ok(!!kid, 'hydra children carry the pop-in class');
    ok(kid && /vmin$/.test(kid.style.getPropertyValue('--gg-flash-size')),
      'children size through the CSS var, never a stacked transform',
      kid && kid.style.getPropertyValue('--gg-flash-size'));
    await sleep(420);
    ok(host.findAll('gg-flash').filter((el) => el._cls.has('is-popped')).length === 0,
      'the dismissed node is torn out of the DOM (no leak if animationend never lands)');

    // Hammer the split: every round doubles the field until MAX_LIVE bites.
    for (let round = 0; round < 6 && shotsOf().length < MAX_LIVE; round++) {
      for (const el of shotsOf()) el.fire('pointerdown');
      await sleep(280);
    }
    await sleep(300);
    const atCap = shotsOf();
    ok(atCap.length === MAX_LIVE, 'the hydra fills to the cap and refuses to pass it',
      String(atCap.length));

    atCap[0].fire('pointerdown');
    await sleep(320);
    ok(shotsOf().length === MAX_LIVE - 1,
      'at the cap a click only dismisses — no children', String(shotsOf().length));

    ex.stopAll();
    ex.detach();
  }

  // ------------------------------- flashes: clicking is COSMETIC (receipt intact)
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const host = byId.get('gg-fx-flash');
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);
    // renderPayload floors a burst at 3000ms whatever duration_ms says.
    m.emitPayload({
      payload: payloadOf({ id: 'pFB', kind: GoonPayloadKind.FlashBurst, duration_ms: 3000, intensity: 0.8 }),
      fireAtLocalMs: localMonotonicMs(),
    });
    await sleep(140);
    const shots = host.findAll('gg-flash').filter((el) => !el._cls.has('is-popped'));
    ok(shots.length > 0, 'a FlashBurst puts flashes on screen', String(shots.length));
    shots[0].fire('pointerdown');
    ok(m.receipts.length === 0, 'clicking a flash does not settle the burst early');
    await sleep(3300);
    ok(m.receipts.length === 1 && m.receipts[0].endured === true,
      'the burst still receipts endured after its full duration', JSON.stringify(m.receipts));
    ex.stopAll();
    ex.detach();
  }

  // -------------------------------------------------- brain drain: the DtRH veil
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);
    m.emitStart({ element: GoonElement.BrainDrain, intensity: 1, durationMs: 0, elapsedMs: 0 });
    await sleep(60);
    const veils = byId.get('gg-fx-drain').findAll('gg-drain');
    ok(veils.length === 1, 'the drain is exactly one veil pane', String(veils.length));
    ok(String(veils[0].style.getPropertyValue('background-image')).includes('ccp.assets'),
      'the veil washes an image from the player own pool', veils[0].style.getPropertyValue('background-image'));
    const dop = parseFloat(veils[0].style.getPropertyValue('--gg-drain-op'));
    ok(dop >= 0.35 && dop <= 0.62, 'drain opacity stays inside the 0.35..0.62 band', String(dop));
    ex.stopAll();
    ex.detach();
  }

  // ------------------------------------------------------------------ lock card
  {
    const host = byId.get('gg-stage');
    host.replaceChildren();
    let solved = null;
    let mistakes = 0;
    let abandoned = 0;
    const view = createLockCardView(host, {
      phrase: 'hold on',
      repeats: 2,
      onSolved: (r) => { solved = r; },
      onMistake: (c) => { mistakes = c; },
      onAbandoned: () => { abandoned++; },
    });
    const card = host.childNodes[0];
    ok(!!card && card._cls.has('gg-lock'), 'lock card mounted into its container');
    const input = card.findAll('gg-lock-input')[0];
    ok(!!input, 'lock card has a real input (IME/AltGr path)');

    const type = (v) => { input.value = v; input.fire('input'); };
    type('h'); type('ho'); type('hol');
    const fill = card.findAll('gg-lock-fill')[0];
    ok(!!fill && parseFloat(fill.style.width) > 40 && parseFloat(fill.style.width) < 50,
      'typed-progress fill tracks the typed prefix (3/7)', fill && fill.style.width);
    ok(mistakes === 0, 'correct typing raises no mistake');
    type('holx');
    ok(mistakes === 1, 'a wrong keystroke reports one mistake', String(mistakes));
    ok(input.value === 'hol', 'the wrong char is rolled back to the last good prefix', input.value);
    type('hold on');
    ok(solved === null, 'one completion of a 2-repeat card does not solve it');
    ok(input.value === '', 'a completed repeat clears the input');
    type('HOLD ON');           // non-strict: case-insensitive
    ok(solved && solved.mistakes === 1, 'second completion solves and reports mistakes',
      JSON.stringify(solved));

    const give = card.findAll('gg-btn')[0];
    give.fire('click');
    ok(abandoned === 1, 'the dismiss affordance reports onAbandoned');
    view.dispose();
    ok(host.childNodes.length === 0, 'dispose removes the card');
    view.dispose();            // idempotent
    ok(true, 'dispose is idempotent');

    // strict mode rejects the case-folded variant
    let strictSolved = false;
    const sv = createLockCardView(host, {
      phrase: 'ab', repeats: 1, strict: true, onSolved: () => { strictSolved = true; },
    });
    const si = host.childNodes[0].findAll('gg-lock-input')[0];
    si.value = 'AB'; si.fire('input');
    ok(!strictSolved, 'strict mode rejects a case mismatch');
    si.value = 'ab'; si.fire('input');
    ok(strictSolved, 'strict mode accepts the exact phrase');
    sv.dispose();
  }

  console.log(failures === 0 ? `PASS — ${n} checks` : `FAILED — ${failures}/${n} checks`);
  process.exit(failures === 0 ? 0 : 1);
}

main().catch((e) => { console.error(e); process.exit(1); });
