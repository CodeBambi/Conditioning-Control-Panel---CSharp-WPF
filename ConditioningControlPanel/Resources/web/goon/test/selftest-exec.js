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

// Document-level events. exec/bubbles.js dispatches `gg-bubble-pop` on the
// document (the drop-economy seam) and ui/hud.js is its only listener in
// production; the stub carries just enough EventTarget for that one seam.
const docListeners = new Map();
globalThis.document = {
  documentElement,
  body: (() => { const b = new StubEl('body'); b.isConnected = true; return b; })(),
  createElement: (tag) => new StubEl(tag),
  createTextNode: (t) => { const e = new StubEl('#text'); e.textContent = t; return e; },
  getElementById: (id) => byId.get(id) || null,
  addEventListener(type, fn) {
    if (!docListeners.has(type)) docListeners.set(type, new Set());
    docListeners.get(type).add(fn);
  },
  removeEventListener(type, fn) { const s = docListeners.get(type); if (s) s.delete(fn); },
  dispatchEvent(evt) {
    const s = docListeners.get(evt && evt.type);
    if (s) for (const fn of Array.from(s)) { try { fn(evt); } catch (_e) { /* a listener is never load-bearing */ } }
    return true;
  },
};
globalThis.CustomEvent = class CustomEvent {
  constructor(type, init) { this.type = type; this.detail = init && init.detail; this.bubbles = !!(init && init.bubbles); }
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

    // THEIR SWARM PAYS NOTHING. With no element bed of our own running, every
    // bubble on screen belongs to the inbound swarm: it is poppable clutter, it
    // is tagged as theirs, and its pop is dispatched with worth 0 so the drop
    // roller skips it. (Otherwise firing a swarm would gift them free items.)
    ok(bubbles.every((b2) => b2.getAttribute('data-gg-mint') === 'payload'),
      'a swarm with no field of our own mints ONLY payload-tagged bubbles',
      bubbles.map((b2) => b2.getAttribute('data-gg-mint')).join(','));

    const swarmPops = [];
    const onSwarmPop = (e) => swarmPops.push(e.detail);
    document.addEventListener('gg-bubble-pop', onSwarmPop);

    // POPPING IS COSMETIC FOR THE RECEIPT: it must not settle the payload.
    const b = bubbles[0];
    b.fire('pointerdown');
    ok(b._cls.has('is-pop'), 'a bubble pops on pointerdown');
    ok(host.findAll('gg-spark').length === 9, 'the pop threw its sparkle burst',
      String(host.findAll('gg-spark').length));
    ok(m.receipts.length === 0, 'popping does not settle the swarm early');

    ok(swarmPops.length === 1, 'every pop dispatches exactly one gg-bubble-pop', String(swarmPops.length));
    ok(swarmPops[0] && swarmPops[0].worth === 0,
      'a payload-minted pop is worth NOTHING to the economy', JSON.stringify(swarmPops[0]));
    ok(swarmPops[0] && swarmPops[0].payload === true, 'and says so on the detail');
    ok(swarmPops[0] && typeof swarmPops[0].kind === 'string', 'the detail carries the bubble kind');
    document.removeEventListener('gg-bubble-pop', onSwarmPop);

    await sleep(1500);
    ok(m.receipts.length === 1 && m.receipts[0].endured === true,
      'the swarm still receipts endured after its full duration', JSON.stringify(m.receipts));
    ex.detach();
  }

  /* ------------------------------ bubbles: the ALWAYS-ON field + the drop seam
   * The field now runs t=0 -> match end and ramps 0.15 -> 1.0, so intensity has
   * to be a real density dial (sparse open, dense finish) and every pop has to
   * announce what it is worth. Both are asserted here; ui/drops.js does the
   * economics and selftest-hud covers that half. */
  {
    const bub = await import('../exec/bubbles.js');

    // the ramp itself: monotonic in every direction that matters
    const steps = [0, 0.15, 0.35, 0.55, 0.8, 1];
    const tunes = steps.map((i) => bub.bubbleTuning(i, false, 3, 22));
    let mono = true;
    let why = '';
    for (let i = 1; i < tunes.length; i++) {
      if (tunes[i].targetCount < tunes[i - 1].targetCount) { mono = false; why = 'count fell at ' + steps[i]; }
      if (tunes[i].topupMs > tunes[i - 1].topupMs) { mono = false; why = 'cadence slowed at ' + steps[i]; }
      if (tunes[i].riseMaxS > tunes[i - 1].riseMaxS) { mono = false; why = 'rise slowed at ' + steps[i]; }
    }
    ok(mono, 'intensity -> density/cadence/speed is monotonic across the ramp', why);
    ok(tunes[0].targetCount * 3 < tunes[tunes.length - 1].targetCount,
      'the field opens SPARSE and finishes at least 3x denser',
      `${tunes[0].targetCount} -> ${tunes[tunes.length - 1].targetCount}`);
    ok(tunes[tunes.length - 1].targetCount + 4 <= bub.MAX_LIVE,
      'a full-intensity field plus a swarm still fits under the 26-node cap',
      String(tunes[tunes.length - 1].targetCount + 4));
    ok(tunes[tunes.length - 1].topupMs < tunes[0].topupMs / 2,
      'and it spawns at least twice as fast late as early',
      `${tunes[0].topupMs} -> ${tunes[tunes.length - 1].topupMs}`);

    // the worth table exec/bubbles.js stamps on a pop
    ok(bub.popWorthOf('normal', false) === bub.POP_WORTH_NORMAL, 'a plain bubble is worth 1');
    ok(bub.popWorthOf('flash', false) === bub.POP_WORTH_EFFECT, 'an effect bubble is worth more');
    ok(bub.POP_WORTH_EFFECT > bub.POP_WORTH_NORMAL, 'strictly more', String(bub.POP_WORTH_EFFECT));
    ok(bub.popWorthOf('flash', true) === 0, 'and nothing at all when THEY minted it');
    ok(bub.POP_EVENT === 'gg-bubble-pop', 'the seam is the documented event name', bub.POP_EVENT);

    // …and the same thing through the live renderer
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);
    const pops = [];
    const onPop = (e) => pops.push(e.detail);
    document.addEventListener('gg-bubble-pop', onPop);

    m.emitStart({ element: GoonElement.Bubbles, intensity: 0.15, durationMs: 0, elapsedMs: 0 });
    await sleep(700);
    const host = byId.get('gg-fx-bubbles');
    const early = host.findAll('gg-bubble');
    ok(early.length > 0, 'the always-on field seeds itself at the opening intensity', String(early.length));
    ok(early.length <= bub.bubbleTuning(0.15, false, 3, 22).targetCount + 1,
      'and stays sparse there', `${early.length} vs ${bub.bubbleTuning(0.15, false, 3, 22).targetCount}`);
    ok(early.every((b2) => b2.getAttribute('data-gg-mint') === 'field'),
      'every bubble our own ramp minted is tagged as ours');

    // our own pops pay: worth follows the kind, never zero
    const target = early[0];
    target.fire('pointerdown');
    ok(pops.length === 1, 'the field dispatches a pop event too', String(pops.length));
    const kind = String(target.className).replace(/.*gg-bubble--(\w+).*/, '$1');
    ok(pops[0].worth === (kind === 'normal' ? bub.POP_WORTH_NORMAL : bub.POP_WORTH_EFFECT),
      'a field pop is worth what its kind is worth', `${kind}=${pops[0].worth}`);
    ok(pops[0].payload === false, 'and is not flagged as clutter');
    ok(typeof pops[0].x === 'number' && typeof pops[0].y === 'number',
      'the detail carries where it burst, for the drop flourish');

    // the ramp climbing must actually thicken the field
    m.emitIntensity({ element: GoonElement.Bubbles, intensity: 1, durationMs: 0, elapsedMs: 60000 });
    await sleep(1600);
    const late = host.findAll('gg-bubble').filter((b2) => !b2._cls.has('is-pop'));
    ok(late.length > early.length,
      'ramping the intensity up puts MORE bubbles on the field', `${early.length} -> ${late.length}`);
    ok(late.length <= bub.MAX_LIVE, 'and never breaches the hard cap', String(late.length));

    // …and a pop after teardown must not keep paying: no listener, no field.
    document.removeEventListener('gg-bubble-pop', onPop);
    const seen = pops.length;
    ex.detach();
    ok(host.findAll('gg-bubble').length === 0, 'detach tore the whole field down',
      String(host.findAll('gg-bubble').length));
    ok(pops.length === seen, 'and nothing popped on the way out', `${seen} -> ${pops.length}`);
  }

  // -------------------------------------- flashes: the DtRH scatter + the hydra
  //
  // FLASH-BLOCK POINTER HELPER. The stub's fire() carries no coordinates and no
  // button, which was fine while the hydra lived on a bare pointerdown. Grab /
  // fling / wheel are all functions of WHERE the pointer was and WHEN, so the
  // three flash blocks below dispatch their own events instead.
  let PID = 0;
  function ptr(el, type, x, y, extra) {
    const e = Object.assign({
      type, button: 0, pointerId: PID, clientX: x, clientY: y, defaulted: false,
      preventDefault() { e.defaulted = true; }, stopPropagation() {},
    }, extra || {});
    for (const fn of ((el._listeners && el._listeners.get(type)) || []).slice()) fn(e);
    return e;
  }
  // A press that never moves: the click the hydra still answers to.
  const tap = (el, x = 100, y = 100) => { PID++; ptr(el, 'pointerdown', x, y); ptr(el, 'pointerup', x, y); };
  const dxOf = (el) => {
    const mm = /translate\((-?[\d.]+)px, (-?[\d.]+)px\)/.exec(el.style.getPropertyValue('transform') || '');
    return mm ? parseFloat(mm[1]) : NaN;
  };
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
    // The hydra now answers to pointerUP, so that a press can still turn into a
    // drag. A press on its own must therefore do nothing at all.
    PID++;
    ptr(target, 'pointerdown', 100, 100);
    ok(!target._cls.has('is-popped'), 'pointerdown alone no longer pops — the press is still deciding');
    ptr(target, 'pointermove', 104, 103);      // 5px: inside DRAG_SLOP_PX, still a click
    ok(!target._cls.has('gg-flash--grabbed'), 'a wobble inside the slop is not a grab', target.className);
    ptr(target, 'pointerup', 104, 103);
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
      for (const el of shotsOf()) tap(el);
      await sleep(280);
    }
    await sleep(300);
    const atCap = shotsOf();
    ok(atCap.length === MAX_LIVE, 'the hydra fills to the cap and refuses to pass it',
      String(atCap.length));

    tap(atCap[0]);
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
    tap(shots[0]);
    ok(m.receipts.length === 0, 'clicking a flash does not settle the burst early');
    // ...and neither does dragging one across the screen and throwing it.
    PID++;
    ptr(shots[shots.length - 1], 'pointerdown', 300, 300);
    ptr(shots[shots.length - 1], 'pointermove', 420, 360);
    ptr(shots[shots.length - 1], 'pointerup', 460, 380);
    ok(m.receipts.length === 0, 'dragging and flinging a flash does not settle the burst either');
    await sleep(3300);
    ok(m.receipts.length === 1 && m.receipts[0].endured === true,
      'the burst still receipts endured after its full duration', JSON.stringify(m.receipts));
    ex.stopAll();
    ex.detach();
  }

  // ------------------------------ flashes: grab, fling, wheel-to-resize (owner)
  //
  // One block on purpose: the long wait that proves a HELD flash's clock is
  // paused is the same wall-clock time the fling needs to glide to a stop, so
  // the two ride together instead of costing the suite two lifetimes.
  {
    const F = await import('../exec/flashes.js');
    ok(F.DRAG_SLOP_PX === 6 && F.WHEEL_STEP === 1.08 && F.SIZE_MIN_FACTOR === 0.5
      && F.SIZE_MAX_FACTOR === 2.5 && F.FLING_FRICTION === 0.94,
      'the drag/fling/wheel dials are the shipped numbers',
      `${F.DRAG_SLOP_PX}px slop, x${F.WHEEL_STEP} per notch, ${F.SIZE_MIN_FACTOR}..${F.SIZE_MAX_FACTOR}x, ${F.FLING_FRICTION} friction`);

    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const host = byId.get('gg-fx-flash');
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);
    const shotsOf = () => host.findAll('gg-flash').filter((el) => !el._cls.has('is-popped'));

    let t0 = Date.now();
    m.emitStart({ element: GoonElement.Flashes, intensity: 1, durationMs: 0, elapsedMs: 0 });
    await sleep(1600);                       // two beats at i=1 -> four flashes to play with
    m.emitStop({ element: GoonElement.Flashes, intensity: 0, durationMs: 0, elapsedMs: 0 });
    await sleep(340);
    // The cadence owes us four (beats at ~0 and <=1235ms, two flashes each), but
    // breed the rest rather than throw if a slow host cost us one — and re-base
    // the lifetime clock when we do, since the newcomers are younger.
    for (let i = 0; shotsOf().length < 4 && i < 4; i++) { tap(shotsOf()[0]); await sleep(300); t0 = Date.now(); }
    const cast = shotsOf();
    ok(cast.length >= 4, 'the bed left enough flashes to drag around', String(cast.length));
    // Grabbing re-appends a node (that is how it floats above its siblings without
    // touching z-index), so every reference is taken BEFORE anyone touches one.
    const held = cast[0];        // goes into the hand and stays there
    const ctrl = cast[1];        // never touched: the control for "the clock is real"
    const flung = cast[2];
    const cancelled = cast[3];
    const durHeld = parseFloat(held.style.getPropertyValue('--gg-flash-dur'));

    // ---- the press that becomes a grab
    const anchorX = parseFloat(flung.style.getPropertyValue('left')) / 100 * 1280;
    const anchorY = parseFloat(flung.style.getPropertyValue('top')) / 100 * 720;
    PID++;
    const down = ptr(flung, 'pointerdown', 500, 500);
    ok(down.defaulted === true, 'the press preventDefaults (no native image-drag ghost)');
    ok(!flung._cls.has('gg-flash--grabbed'), 'the press is not a grab yet');
    ptr(flung, 'pointermove', 503, 504);     // 5px: still inside the slop
    ok(!flung._cls.has('gg-flash--grabbed'), 'movement inside the slop is still a click', flung.className);
    ptr(flung, 'pointermove', 600, 600);     // past it: this is a grab
    ok(flung._cls.has('gg-flash--grabbed') && flung._cls.has('is-held'),
      'past the slop the flash is in hand', flung.className);
    const t = flung.style.getPropertyValue('transform');
    ok(/translate\(-50%, -50%\) translate\(100\.0px, 100\.0px\)/.test(t),
      'the held flash follows the pointer, one-to-one', t);
    ok(/rotate\(-?[\d.]+deg\)/.test(t) && /scale\(1\.06\)/.test(t),
      'the drag transform still carries the tilt, plus the lift', t);
    ok(dxOf(flung) === 100, 'drag offset is px on top of the vw/vh anchor, which never moves',
      `${flung.style.getPropertyValue('left')} + ${dxOf(flung)}px`);

    // ---- wheel while holding: --gg-flash-size only, clamped, and never scrolls
    const sizeOf = (el) => parseFloat(el.style.getPropertyValue('--gg-flash-size')) || 43.7;
    const base = sizeOf(flung);
    const w = ptr(flung, 'wheel', 600, 600, { deltaY: -100 });
    ok(w.defaulted === true, 'a wheel while held preventDefaults so the page can never scroll');
    ok(Math.abs(sizeOf(flung) - base * F.WHEEL_STEP) < 0.06,
      'one notch up grows it by exactly one wheel step', `${base} -> ${sizeOf(flung)}`);
    ok((flung.style.getPropertyValue('transform').match(/scale\(/g) || []).length === 1
      && /scale\(1\.06\)/.test(flung.style.getPropertyValue('transform')),
      'the wheel never stacks a transform scale on top of the tilt/drag — size is the var alone',
      flung.style.getPropertyValue('transform'));
    for (let i = 0; i < 30; i++) ptr(flung, 'wheel', 600, 600, { deltaY: -120 });
    ok(Math.abs(sizeOf(flung) - base * F.SIZE_MAX_FACTOR) < 0.06,
      'growth clamps at 2.5x its own base size', String(sizeOf(flung)));
    for (let i = 0; i < 60; i++) ptr(flung, 'wheel', 600, 600, { deltaY: 120 });
    ok(Math.abs(sizeOf(flung) - base * F.SIZE_MIN_FACTOR) < 0.06,
      'and shrink clamps at 0.5x', String(sizeOf(flung)));

    // ---- the other hand is busy: a second pointer is ignored, not half-driven
    PID++;
    ptr(cancelled, 'pointerdown', 200, 200);
    ptr(cancelled, 'pointermove', 300, 300);
    ok(!cancelled._cls.has('gg-flash--grabbed'),
      'a second finger during a live drag is ignored outright', cancelled.className);
    PID--;

    // ---- the throw. Park it mid-screen first so the glide has room to run
    // without kissing a wall, let the parking move age out of the velocity
    // window, THEN flick: 40px in ~50ms is the only momentum it should keep.
    // (pointer x -> node centre: centre = anchor + (x - 500), the press origin.)
    const px = (centre) => centre - anchorX + 500;
    const py = (centre) => centre - anchorY + 500;
    ptr(flung, 'pointermove', px(400), py(360));
    await sleep(150);
    ptr(flung, 'pointermove', px(400), py(360));
    await sleep(25);
    ptr(flung, 'pointermove', px(420), py(360));
    await sleep(25);
    ptr(flung, 'pointermove', px(440), py(360));
    const up = ptr(flung, 'pointerup', px(440), py(360));
    ok(up.defaulted === true, 'the release preventDefaults too');
    ok(!flung._cls.has('is-popped'),
      'a press that became a drag is NOT a click — no hydra on release', flung.className);
    ok(!flung._cls.has('is-held') && flung._cls.has('gg-flash--grabbed'),
      'released: out of hand, still JS-owned', flung.className);

    const g0 = dxOf(flung);
    await sleep(130);
    const g1 = dxOf(flung);
    ok(g1 > g0 + 4, 'a release with velocity throws it — the glide is running', `${g0} -> ${g1}`);
    await sleep(170);
    const g2 = dxOf(flung);
    ok((g2 - g1) < (g1 - g0) && g2 > g1, 'friction: each stretch is shorter than the last',
      `${g0} -> ${g1} -> ${g2}`);
    await sleep(950);
    const g3 = dxOf(flung);
    await sleep(150);
    ok(Math.abs(dxOf(flung) - g3) < 1, 'and it comes to rest instead of gliding forever',
      `${g3} -> ${dxOf(flung)}`);
    const restX = dxOf(flung) + anchorX;
    ok(flung.isConnected && restX >= 0.07 * 1280 - 1 && restX <= 0.93 * 1280 + 1,
      'it landed inside the bounce box rather than sailing off (that needs a real hurl)',
      String(Math.round(restX)));

    // ---- pointercancel is a gentle drop: no pop, no stuck hold, hand empty
    PID++;
    ptr(cancelled, 'pointerdown', 200, 200);
    ptr(cancelled, 'pointermove', 280, 250);
    ok(cancelled._cls.has('is-held'), 'the third flash is in hand', cancelled.className);
    ptr(cancelled, 'pointercancel', 280, 250);
    ok(!cancelled._cls.has('is-held') && !cancelled._cls.has('is-popped') && cancelled.isConnected,
      'pointercancel drops it where it lies: no pop, no stuck hold', cancelled.className);
    ok(Math.abs(dxOf(cancelled) - 80) < 0.01,
      'a cancelled drag keeps the ground it covered', String(dxOf(cancelled)));
    // The hand is empty again — the very next press is a clean click.
    tap(cancelled, 280, 250);
    ok(cancelled._cls.has('is-popped'),
      'and the next press still works, so no drag state was left stuck');

    // ---- the long hold: the lifetime clock PAUSES in the hand
    PID++;
    ptr(held, 'pointerdown', 700, 400);
    ptr(held, 'pointermove', 760, 440);
    ok(held._cls.has('is-held'), 'the first flash is in hand for the rest of this block');
    // Past its own death: spawn + --gg-flash-dur + the module's 600ms safety net.
    const waitMs = (t0 + durHeld + 1300) - Date.now();
    if (waitMs > 0) await sleep(waitMs);
    ok(!ctrl.isConnected,
      'the untouched control flash retired on schedule (the clock really did run out)');
    ok(held.isConnected && !held._cls.has('gg-flash--expire'),
      'a HELD flash outlives its own lifetime — the clock is paused, not merely stretched',
      held.className);
    ptr(held, 'pointerup', 760, 440);
    ok(held.isConnected && !held._cls.has('is-held') && !held._cls.has('is-popped'),
      'letting go hands it back to the clock without popping it', held.className);
    await sleep(150);
    ok(held.isConnected, 'and it does not vanish the instant it is released');

    ex.stopAll();
    ex.detach();
  }

  // ------------------------ flashes: stop() never leaves one stuck in your hand
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const host = byId.get('gg-fx-flash');
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);
    m.emitStart({ element: GoonElement.Flashes, intensity: 1, durationMs: 0, elapsedMs: 0 });
    await sleep(60);
    const g = host.findAll('gg-flash')[0];
    ok(!!g, 'a flash to grab');
    PID++;
    ptr(g, 'pointerdown', 300, 300);
    ptr(g, 'pointermove', 380, 340);
    ok(g._cls.has('is-held'), 'held when the element stops');
    m.emitStop({ element: GoonElement.Flashes, intensity: 0, durationMs: 0, elapsedMs: 0 });
    ok(!g._cls.has('is-held') && !g._cls.has('is-popped') && g.isConnected,
      'stop() takes it out of your hand gently — no pop, still on screen', g.className);
    tap(g, 300, 300);
    ok(g._cls.has('is-popped'), 'and the hand is empty: the next press works');
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
