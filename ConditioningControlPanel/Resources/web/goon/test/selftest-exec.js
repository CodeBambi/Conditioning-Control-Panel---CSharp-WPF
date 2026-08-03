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
  'gg-fx-drain', 'gg-fx-vwin', 'gg-stage', 'gg-fx'];
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

/* LIVE windows only. Since 2026-08-04 a settled window leaves the pool
 * IMMEDIATELY and its node lingers a few hundred ms as a GHOST (.gg-vwin--out,
 * pointer-events off) to play out the exit animation. Every assertion about
 * "how many windows are up" and every click therefore has to skip the husks —
 * which is exactly the property being pinned: the record went first, the node
 * followed. Anything that wants the husks asks for them by name. */
const liveWins = (host) => host.findAll('gg-vwin').filter((el) => !el._cls.has('gg-vwin--out'));
const ghostWins = (host) => host.findAll('gg-vwin').filter((el) => el._cls.has('gg-vwin--out'));
/** Past the ghost clock (GHOST_REMOVE_MS 600 + slack): the layer is settled. */
const GHOST_WAIT = 700;

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

    // The pool is the DtRH bundle, read off the same ccp.game origin — MINUS
    // sp7.gif. Every entry is a dithered 256-colour file blown up to COVER the
    // window, and at 360x360 sp7's dither read as crawling grain rather than
    // shading (owner-approved cull, 2026-08-03). The FILE stays on disk: DtRH
    // owns it and still draws it.
    ok(SPIRAL_POOL.length === 6, 'the bundled spiral pool has 6 entries', String(SPIRAL_POOL.length));
    ok(!SPIRAL_POOL.includes('sp7.gif'), 'sp7.gif is culled from the pool (the worst grain offender)',
      SPIRAL_POOL.join(','));
    let poolHits = 0;
    let sp7 = 0;
    for (let i = 0; i < 200; i++) {
      const u = pickSpiralUrl();
      if (u.startsWith('/dtrh/assets/bubbles/effects/spirals/') && SPIRAL_POOL.some((f) => u.endsWith(f))) poolHits++;
      if (u.endsWith('sp7.gif')) sp7++;
    }
    ok(poolHits === 200, 'every picked spiral url is a bundled DtRH spiral', String(poolHits));
    ok(sp7 === 0, 'and 200 picks never drew the culled one', String(sp7));
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

  /* ------------------------------ bubbles: the fx.css contract the field rests on
   * Two things about this field are STYLESHEET facts that no amount of JS can
   * assert from the inside, and both of them were bugs:
   *   1. the kind sprites are DtRH art plus a coloured drop-shadow, full stop.
   *      A play-test note that braindrain/glitch "show in black n white" was
   *      once read as the BUBBLES being grey and answered with sepia/saturate/
   *      hue-rotate recolour chains; the owner corrected it (2026-08-03) — the
   *      grey thing was the fullscreen drain veil, and the sprites were fine.
   *      So the pins below are the other way round now: NO colour-space filter
   *      function may appear in a kind rule;
   *   2. `body:has(#gg-stage > *) .gg-bubble { pointer-events: none }` made the
   *      whole economy unreachable for the length of every video and lock card.
   * So the stylesheet is READ, the way selftest-hud reads hud.css for the monitor
   * z-order. Comments are stripped first — the banner above the rules quotes the
   * deleted selector verbatim, and a regression pin a comment can green is not a
   * pin. */
  {
    const fs = await import('node:fs/promises');
    const url = await import('node:url');
    const raw = await fs.readFile(url.fileURLToPath(new URL('../exec/fx.css', import.meta.url)), 'utf8');
    const css = raw.replace(/\/\*[\s\S]*?\*\//g, '');

    const esc = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const ruleOf = (sel) => {
      const m = new RegExp(esc(sel) + '\\s*\\{([^}]*)\\}').exec(css);
      return m ? m[1] : null;
    };
    const filterOf = (sel) => {
      const body = ruleOf(sel);
      const m = body && /filter:\s*([^;]+)/.exec(body);
      return m ? m[1].trim() : null;
    };
    // every colour-space filter function; drop-shadow is deliberately not here
    const COLOUR_FNS = ['brightness(', 'sepia(', 'saturate(', 'hue-rotate(', 'contrast(', 'grayscale(', 'invert(', 'opacity('];
    const shadowIsLast = (f) => {
      const d = f.indexOf('drop-shadow(');
      return d >= 0 && COLOUR_FNS.every((fn) => f.lastIndexOf(fn) < d);
    };
    const hueOf = (f) => { const m = /hue-rotate\(\s*(-?[\d.]+)deg\s*\)/.exec(f || ''); return m ? Number(m[1]) : null; };

    const bd = filterOf('.gg-bubble--braindrain');
    const gl = filterOf('.gg-bubble--glitch');
    const fl = filterOf('.gg-bubble--flash');
    ok(!!bd && !!gl && !!fl, 'fx.css still gives every kind its own filter declaration');
    // THE REGRESSION PIN, and it is the whole point of this block: the sprites
    // are HALO-ONLY. No sepia, no hue-rotate, no saturate — the greyscale the
    // owner reported was the drain veil, and tinting bubbles never fixed it.
    ok(bd === 'drop-shadow(0 0 9px rgba(180, 150, 255, 0.75))',
      'braindrain is its violet halo and nothing else', String(bd));
    ok(gl === 'drop-shadow(0 0 9px rgba(140, 255, 170, 0.75))',
      'glitch is its acid-green halo and nothing else', String(gl));
    ok(fl === 'drop-shadow(0 0 9px rgba(255, 240, 200, 0.75))',
      'flash is its warm-white halo and nothing else', String(fl));
    ok(bd !== gl, 'and the kinds are still told apart (different halo colours)');
    for (const [k, f] of [['braindrain', bd], ['glitch', gl], ['flash', fl]]) {
      const tint = COLOUR_FNS.filter((fn) => String(f).includes(fn));
      ok(tint.length === 0,
        `.gg-bubble--${k} declares NO colour-space filter function`, tint.join(' '));
      ok(hueOf(f) === null, `.gg-bubble--${k} is not hue-rotated`, String(f));
      ok(shadowIsLast(f), `.gg-bubble--${k} keeps its drop-shadow LAST`, String(f));
    }

    // the kinds that already read coloured are untouched art-wise, and NO kind
    // rule may declare `animation` — the shorthand would cancel the sway.
    for (const k of ['flash', 'spiral', 'glitch', 'braindrain', 'pinkfilter']) {
      const body = ruleOf(`.gg-bubble--${k}`);
      ok(!!body && body.includes(`/dtrh/assets/bubbles/effects/${k}.png`),
        `.gg-bubble--${k} still points at the DtRH sprite`, String(body));
      ok(!!body && !/(^|[^-])animation:/.test(body),
        `.gg-bubble--${k} does not redeclare the animation shorthand`, String(body));
      ok(!!body && /drop-shadow\(/.test(body), `.gg-bubble--${k} still carries a coloured halo`);
    }
    ok(filterOf('.gg-bubble--spiral') === 'drop-shadow(0 0 9px rgba(110, 225, 255, 0.75))',
      'spiral (cyan art, already saturated) is left alone', String(filterOf('.gg-bubble--spiral')));
    ok(/rgba\(var\(--gg-pink-rgb\)/.test(String(filterOf('.gg-bubble--pinkfilter'))),
      'pinkfilter (pink art, already saturated) is left alone', String(filterOf('.gg-bubble--pinkfilter')));

    // ---- the regression pin: the field stays poppable over a video / lock card
    ok(!/#gg-stage[^{]*\.gg-bubble\s*\{/.test(css),
      'NO rule takes .gg-bubble pointer-events away while #gg-stage is occupied',
      (/[^{}]*#gg-stage[^{]*\.gg-bubble\s*\{[^}]*\}/.exec(css) || [''])[0]);
    ok(!/[^.\w-]\.gg-bubble\s*\{[^}]*pointer-events:\s*none/.test(css),
      'and no plain .gg-bubble rule sets pointer-events:none');
    const base = ruleOf('.gg-bubble');
    ok(!!base && /pointer-events:\s*auto/.test(base), 'the bubble still opts INTO pointers', String(base));
    ok(/\.gg-bubble\.is-pop\s*\{[^}]*pointer-events:\s*none/.test(css),
      'a bubble mid-pop is still click-through (one pop per bubble)');

    // ---- and the hydra's identical rule is INTENDED and must survive
    ok(/body:has\(#gg-stage\s*>\s*\*\)\s*\.gg-flash--hydra\s*\{[^}]*pointer-events:\s*none/.test(css),
      'the .gg-flash--hydra :has() rule is still there — flashes ARE scenery');

    // ---- the heat split: the FIELD is exempt, the pop WASHES are not
    ok(!!base && /--gg-deco-play:\s*running/.test(base),
      'the bubble re-declares --gg-deco-play (gameplay, exempt from heat parking)');
    ok(/\.gg-bubble-wrap\s*\{[^}]*--gg-deco-play:\s*running/.test(css),
      'and so does its rising wrap');
    for (const wash of ['.gg-spiral', '.gg-pink', '.gg-drain']) {
      const body = ruleOf(wash);
      ok(body === null || !/--gg-deco-play:\s*running/.test(body),
        `${wash} is decoration and still parks at hot heat`, String(body));
    }

    /* ---- THE DRAIN VEIL SHOWS COLOUR (owner call 2026-08-03)
     * The pane blended the player's own wash at `luminosity` over #0a0410, and
     * luminosity keeps only the source's LIGHTNESS — every BrainDrain came out
     * black and white, and the glitch shudder riding the same pane came out
     * grey with it. The veil now dims + part-desaturates in the background
     * layer stack instead. Measured headlessly on a full-chroma wash: pane HSL
     * sat 0.083 -> 0.677, chroma 0.047 -> 0.406, lightness 0.511 -> 0.305, so
     * it got MORE colour and LESS light — still a veil, not a slideshow. */
    const drain = ruleOf('.gg-drain');
    ok(!!drain, 'fx.css still has the .gg-drain pane', String(drain));
    ok(!!drain && !/background-blend-mode:[^;]*luminosity/.test(drain),
      'the drain does NOT blend its wash at luminosity (that is the B&W bug)', String(drain));
    ok(!/background-blend-mode:[^;]*luminosity/.test(css),
      'and nothing else in fx.css reaches for luminosity either');
    ok(!!drain && /background-color:\s*#0a0410/.test(drain),
      'the drain keeps its near-black base colour — it is still a dark veil', String(drain));
    ok(!!drain && /var\(--gg-drain-img/.test(drain),
      'the wash comes in through --gg-drain-img, so the dim/desat layers survive it', String(drain));
    ok(!!drain && /background-blend-mode:\s*normal,\s*color,\s*normal/.test(drain),
      'the layer stack is dim(normal) over desat(color) over wash(normal)', String(drain));
    ok(!!drain && /--gg-drain-dim/.test(drain) && /--gg-drain-desat/.test(drain),
      'both veil knobs are named, and neither is the opacity dial', String(drain));
    ok(!!drain && /opacity:\s*var\(--gg-drain-op/.test(String(ruleOf('.gg-drain.is-on'))),
      '--gg-drain-op is still the ONE dial, and higher still means heavier',
      String(ruleOf('.gg-drain.is-on')));
    ok(!!drain && !/(^|[^-])filter:/.test(drain),
      'the pane declares no static filter — the glitch keyframes animate filter and would wipe it',
      String(drain));
    // the blur is the most expensive thing this page draws: exactly one pane,
    // exactly one backdrop-filter (+ its -webkit- twin), and the count is pinned.
    const bdf = (css.match(/backdrop-filter:/g) || []).length;
    ok(bdf === 2, 'fx.css still declares backdrop-filter exactly twice (one pane, one -webkit- twin)', String(bdf));
    ok(/\.gg-drain\s*\{[^}]*backdrop-filter:\s*blur\(7px\)/.test(css),
      'and the one that exists is the drain blur', String(bdf));
    // the shudder still rides the veil, and still recolours what is under it
    const glitchKf = /@keyframes\s+ggDrainGlitch\s*\{([\s\S]*?)\n\}/.exec(css);
    ok(!!glitchKf && /hue-rotate\(/.test(glitchKf[1]) && /saturate\(/.test(glitchKf[1]),
      'the glitch shudder still hue-rotates + saturates on top of the new composition',
      glitchKf ? glitchKf[1].replace(/\s+/g, ' ').trim() : 'none');
    ok(/\.gg-drain\.is-glitching\s*\{[^}]*animation:\s*ggDrainGlitch/.test(css),
      'and .is-glitching is what runs it');
  }

  /* ------------------------------ bubbles: the stage-avoidance spawn bias
   * Now that a bubble stays poppable over a video, the field has to stop DRIFTING
   * over one instead. spawnRanges() is the whole opinion, and it is pure, so it
   * gets pinned properly: rect + viewport in, allowed left-edge spans out. */
  {
    const bub = await import('../exec/bubbles.js');
    const { spawnRanges, pickSpawnPct, SPAWN_MIN_PCT, SPAWN_MAX_PCT, MIN_GUTTER_PX } = bub;
    const VW = 1280;
    const near = (a, b) => Math.abs(a - b) < 1e-6;
    const isFull = (r) => r.length === 1 && near(r[0][0], SPAWN_MIN_PCT) && near(r[0][1], SPAWN_MAX_PCT);
    const shape = (r) => JSON.stringify(r);

    ok(SPAWN_MIN_PCT === 2 && SPAWN_MAX_PCT === 92,
      'the spawn margins are the ones the field always used', `${SPAWN_MIN_PCT}..${SPAWN_MAX_PCT}`);
    ok(MIN_GUTTER_PX === 160, 'and the give-up threshold is the documented 160px', String(MIN_GUTTER_PX));

    // EMPTY STAGE -> full width, unchanged behaviour.
    ok(isFull(spawnRanges(null, VW)), 'no stage content spawns full width', shape(spawnRanges(null, VW)));
    ok(isFull(spawnRanges(undefined, VW)), 'and so does undefined');

    // A CENTRED VIDEO -> two gutters, and nothing in between.
    const vid = spawnRanges({ left: 320, right: 960 }, VW);
    ok(vid.length === 2, 'a centred stage yields exactly two gutters', shape(vid));
    ok(near(vid[0][0], 2) && near(vid[0][1], 25), 'the left gutter ends at the stage edge', shape(vid));
    ok(near(vid[1][0], 75) && near(vid[1][1], 92), 'the right gutter starts at the stage edge', shape(vid));
    ok(vid.every(([lo, hi]) => hi <= 25 || lo >= 75), 'and nothing overlaps the stage', shape(vid));

    // A FAT BUBBLE grows rightward from `left`, so only the LEFT gutter pays for it.
    const fat = spawnRanges({ left: 320, right: 960 }, VW, { bubblePx: 150 });
    ok(near(fat[0][1], ((320 - 150) / VW) * 100),
      "the left gutter leaves room for the bubble's own width", shape(fat));
    ok(near(fat[1][0], 75), 'the right gutter does not (the bubble grows away from the stage)', shape(fat));

    // width-driven rects work too (right is optional)
    ok(shape(spawnRanges({ left: 320, width: 640 }, VW)) === shape(vid),
      'a {left,width} rect is read the same as {left,right}', shape(spawnRanges({ left: 320, width: 640 }, VW)));

    // TOO THIN -> give up and spawn full width. A starved economy is the worse bug.
    ok(isFull(spawnRanges({ left: 40, right: 1240 }, VW)),
      'gutters under 160px total fall back to full width', shape(spawnRanges({ left: 40, right: 1240 }, VW)));
    ok(isFull(spawnRanges({ left: 0, right: VW }, VW)), 'a full-bleed stage falls back too');
    const edge = spawnRanges({ left: 80, right: 1200 }, VW);   // 80 + 80 = exactly 160
    ok(!isFull(edge), 'exactly 160px of gutter is still biased', shape(edge));
    ok(edge.every(([lo, hi]) => hi - lo >= 1.5), 'and no unusably narrow slice is returned', shape(edge));

    // A rect hanging off the viewport only counts for the part on screen.
    const off = spawnRanges({ left: -200, right: 400 }, VW);
    ok(off.length === 1 && near(off[0][0], 31.25) && near(off[0][1], 92),
      'a stage hanging off the left edge leaves only the right gutter', shape(off));

    // GARBAGE IN -> full width, never a throw and never an empty list.
    const junk = [
      [{ left: NaN, right: 5 }, VW], [{ left: 100, right: 50 }, VW], [{ left: 320, right: 960 }, 0],
      [{ left: 320, right: 960 }, NaN], [{ left: 320, right: 960 }, -5], [{}, VW],
      ['nope', VW], [42, VW], [{ left: 0, right: Infinity }, VW],
    ];
    let junkOk = true; let junkWhy = '';
    for (const [rect, vw] of junk) {
      let r = null;
      try { r = spawnRanges(rect, vw); } catch (e) { junkOk = false; junkWhy = `threw on ${JSON.stringify(rect)}: ${e.message}`; continue; }
      if (!Array.isArray(r) || !r.length) { junkOk = false; junkWhy = `empty for ${JSON.stringify(rect)}`; }
      else if (!r.every(([lo, hi]) => hi > lo && lo >= SPAWN_MIN_PCT && hi <= SPAWN_MAX_PCT)) {
        junkOk = false; junkWhy = `bad span for ${JSON.stringify(rect)}: ${shape(r)}`;
      }
    }
    ok(junkOk, 'every degenerate input still yields a usable in-bounds span', junkWhy);

    // ---- pickSpawnPct: width-weighted, in-bounds, deterministic under a fake rnd
    ok(near(pickSpawnPct(vid, () => 0), 2), 'rnd 0 lands at the start of the first gutter',
      String(pickSpawnPct(vid, () => 0)));
    // WIDTH-WEIGHTED, and this is the arithmetic that proves it: the gutters are
    // 23pct and 17pct wide (total 40), so rnd walks a 40-wide line, not two halves.
    // 0.5 -> t=20, still inside the 23-wide left gutter -> 2+20.
    // 0.7 -> t=28, past it by 5 -> 75+5. An even split would have given 12 and 82.
    ok(near(pickSpawnPct(vid, () => 0.5), 22), 'the pick is WIDTH-WEIGHTED across gutters',
      String(pickSpawnPct(vid, () => 0.5)));
    ok(near(pickSpawnPct(vid, () => 0.7), 80), 'and it walks on into the second gutter',
      String(pickSpawnPct(vid, () => 0.7)));
    ok(pickSpawnPct(vid, () => 1) >= 75 && pickSpawnPct(vid, () => 1) <= 92,
      'and rnd 1 (exclusive in theory) still lands in bounds', String(pickSpawnPct(vid, () => 1)));
    let inGutter = true;
    for (let i = 0; i < 400; i++) {
      const p = pickSpawnPct(vid);
      if (!((p >= 2 && p <= 25) || (p >= 75 && p <= 92))) { inGutter = false; break; }
    }
    ok(inGutter, '400 real draws all land inside a gutter, never over the stage');
    let hitBoth = 0;
    for (let i = 0; i < 400; i++) hitBoth |= (pickSpawnPct(vid) < 50 ? 1 : 2);
    ok(hitBoth === 3, 'and both gutters actually get used', String(hitBoth));
    for (const bad of [null, [], 'x', [[5, 5]], [[9, 2]], [[NaN, 3]]]) {
      const p = pickSpawnPct(bad, () => 0.5);
      ok(p >= SPAWN_MIN_PCT && p <= SPAWN_MAX_PCT,
        `garbage ranges still pick something legal (${JSON.stringify(bad)})`, String(p));
    }

    // ---- and the LIVE renderer honours it. The stub has no layout engine, so the
    // stage content is handed a getBoundingClientRect of its own — which is also
    // the proof that the measurement reads the CHILD, not the full-bleed layer.
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const stage = byId.get('gg-stage');
    const video = new StubEl('video');
    video.getBoundingClientRect = () => ({ left: 320, right: 960, top: 100, bottom: 620, width: 640, height: 520 });
    stage.appendChild(video);

    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);
    m.emitStart({ element: GoonElement.Bubbles, intensity: 1, durationMs: 0, elapsedMs: 0 });
    await sleep(1400);
    const wraps = byId.get('gg-fx-bubbles').findAll('gg-bubble-wrap');
    ok(wraps.length >= 3, 'the field still spawns while the stage is occupied', String(wraps.length));
    const lefts = wraps.map((w) => parseFloat(w.style.getPropertyValue('left')));
    ok(lefts.every((p) => p === p), 'every wrap got a numeric left', JSON.stringify(lefts));
    // 25% is the stage's left edge and 75% its right; a fat bubble is pushed
    // further left still, so <=25 / >=75 is the loosest correct assertion.
    ok(lefts.every((p) => p <= 25 || p >= 75),
      'and every NEW bubble spawned into a gutter, clear of the video',
      JSON.stringify(lefts));

    // Take the video away and the field goes back to using the whole width.
    stage.replaceChildren();
    await sleep(700);                                  // outlive the rect cache TTL
    byId.get('gg-fx-bubbles').replaceChildren();
    await sleep(2600);
    const after = byId.get('gg-fx-bubbles').findAll('gg-bubble-wrap')
      .map((w) => parseFloat(w.style.getPropertyValue('left')));
    ok(after.length > 0, 'the field keeps spawning after the stage clears', String(after.length));
    ok(after.some((p) => p > 25 && p < 75),
      'and it uses the middle of the screen again', JSON.stringify(after));
    ex.detach();
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

  /* ============================ FLOATING VIDEO WINDOWS (the video PAYLOAD) ====
   * The opponent's VHS used to mount a full-bleed surface on #gg-stage, which is
   * the one place in this page a leftover node becomes a full-screen click
   * shield (see selftest-flow's recap teardown). It is a drifting mini window on
   * #gg-fx-vwin now — handled, dismissable, and swept by layers.stopAll() like
   * the rest of the tier. The ramp's own mandatory video is STILL fullscreen on
   * the stage; only renderPayload moved.
   * ======================================================================== */

  // ------------------------------------- video windows: the pure dials + maths
  {
    const V = await import('../exec/videos.js');
    ok(V.MAX_WINDOWS === 4 && V.WINDOW_CAP_MS === 60000 && V.GLITCH_VARIANTS === 4,
      'the window budget is 4 on screen, 60s each, 4 spawn-in variants',
      `${V.MAX_WINDOWS}/${V.WINDOW_CAP_MS}/${V.GLITCH_VARIANTS}`);
    ok(V.DRAG_SLOP_PX === 6 && V.WHEEL_STEP === 1.08 && V.SIZE_MIN_FACTOR === 0.5 && V.SIZE_MAX_FACTOR === 2.5,
      'and the physical dials are exec/flashes.js\'s, verbatim',
      `${V.DRAG_SLOP_PX}px slop, x${V.WHEEL_STEP}, ${V.SIZE_MIN_FACTOR}..${V.SIZE_MAX_FACTOR}x`);
    ok(V.MERCY_KEEPOUT_PX === 96,
      'the mercy gutter is the same 96px ui/options.js clips off the drawer', String(V.MERCY_KEEPOUT_PX));

    // THE AUDIO INVARIANT, pure: exactly the newest window speaks. Four
    // soundtracks at once is mush, and silence is a bug too.
    ok(JSON.stringify(V.unmutedFlags(0)) === '[]', 'no windows, nobody speaks');
    ok(JSON.stringify(V.unmutedFlags(1)) === '[true]', 'one window speaks');
    ok(JSON.stringify(V.unmutedFlags(4)) === '[false,false,false,true]',
      'with four up, only the NEWEST speaks', JSON.stringify(V.unmutedFlags(4)));
    for (let n = 1; n <= 8; n++) {
      const f = V.unmutedFlags(n);
      ok(f.filter(Boolean).length === 1 && f[n - 1] === true, `exactly one speaker at n=${n}`, JSON.stringify(f));
    }

    /* THE FADES, pure (2026-08-04). The invariant above says WHO speaks; these
     * say how they get there and back. All of it is assertable without an audio
     * element, which is the point of extracting it: a ramp bug in a browser is a
     * bug you hear once and cannot reproduce.
     *
     * THE CEILING DID NOT MOVE. volumeFor() is still the only thing that decides
     * how loud a window is; the ramps merely chase it, and audioTargets() is
     * unmutedFlags() with the volume filled in. If a fade ever ended above
     * volumeFor(intensity) the fade would be setting the level, not the dial. */
    ok(V.AUDIO_FADE_IN_MS === 500 && V.AUDIO_FADE_OUT_MS === 300,
      'a window fades in over 500ms and out over 300 — arriving may take its time, leaving may not',
      `${V.AUDIO_FADE_IN_MS}/${V.AUDIO_FADE_OUT_MS}`);
    ok(V.AUDIO_RAMP_TICK_MS > 0 && V.AUDIO_RAMP_TICK_MS <= 50,
      'stepped often enough that nobody hears the steps', String(V.AUDIO_RAMP_TICK_MS));
    ok(V.volumeFor(0.7) === 0.421 && V.volumeFor(1) === 0.55 && V.volumeFor(0) === 0.12,
      'and volumeFor is untouched: 0.42 at the intensity the duel actually throws',
      `${V.volumeFor(0)}..${V.volumeFor(0.7)}..${V.volumeFor(1)}`);

    ok(JSON.stringify(V.audioTargets([])) === '[]', 'no windows, no targets');
    ok(JSON.stringify(V.audioTargets([0.7])) === '[0.421]',
      'one window heads for its own volumeFor', JSON.stringify(V.audioTargets([0.7])));
    ok(JSON.stringify(V.audioTargets([0.7, 0.7, 0.7, 0.7])) === '[0,0,0,0.421]',
      'with four up, three are heading to SILENCE and the newest to 0.421',
      JSON.stringify(V.audioTargets([0.7, 0.7, 0.7, 0.7])));
    let targetsAgree = 0;
    for (let k = 1; k <= 8; k++) {
      const t = V.audioTargets(new Array(k).fill(0.7));
      const fl = V.unmutedFlags(k);
      if (t.every((x, i) => (x > 0) === fl[i]) && t.filter((x) => x > 0).length === 1) targetsAgree++;
    }
    ok(targetsAgree === 8, 'and the targets and the mute flags never disagree at any depth', String(targetsAgree));
    ok(V.audioTargets([1, 1]).every((x) => x <= V.volumeFor(1)),
      'no target is ever above the ceiling volumeFor sets', JSON.stringify(V.audioTargets([1, 1])));

    ok(V.fadeMsFor(0, 0.4) === V.AUDIO_FADE_IN_MS, 'a rise takes the long fade', String(V.fadeMsFor(0, 0.4)));
    ok(V.fadeMsFor(0.4, 0) === V.AUDIO_FADE_OUT_MS, 'a fall takes the short one', String(V.fadeMsFor(0.4, 0)));
    ok(V.fadeMsFor(0.4, 0.4) === 0,
      'and going nowhere takes no time — which is what stops applyAudio restarting a live fade',
      String(V.fadeMsFor(0.4, 0.4)));

    ok(V.rampAt(0, 0.4, 0, 500) === 0, 'a ramp starts where it started', String(V.rampAt(0, 0.4, 0, 500)));
    ok(V.rampAt(0, 0.4, 250, 500) === 0.2, 'half way through is half way there', String(V.rampAt(0, 0.4, 250, 500)));
    ok(V.rampAt(0, 0.4, 500, 500) === 0.4, 'and it lands on its target', String(V.rampAt(0, 0.4, 500, 500)));
    ok(V.rampAt(0, 0.4, 900, 500) === 0.4,
      'an overrun lands ON the target, never past it (a clamp, not a line)', String(V.rampAt(0, 0.4, 900, 500)));
    ok(V.rampAt(0.4, 0, 150, 300) === 0.2, 'a fall is the same line backwards', String(V.rampAt(0.4, 0, 150, 300)));
    ok(V.rampAt(0.4, 0, 0, 0) === 0,
      'a zero-length ramp IS its target — no division by zero, no NaN into el.volume',
      String(V.rampAt(0.4, 0, 0, 0)));
    // Monotone and inside the band, both directions — a lerp that overshoots is
    // a click in the speakers.
    let rises = 0, falls = 0, strays = 0;
    let prevUp = -1, prevDown = 2;
    for (let i = 0; i <= 40; i++) {
      const el = (i / 40) * 600;                     // deliberately overruns 500
      const up = V.rampAt(0, 0.42, el, 500);
      const down = V.rampAt(0.42, 0, el, 300);
      if (up >= prevUp) rises++;
      if (down <= prevDown) falls++;
      if (up < 0 || up > 0.42 || down < 0 || down > 0.42) strays++;
      prevUp = up; prevDown = down;
    }
    ok(rises === 41 && falls === 41, 'a ramp never doubles back on itself', `${rises}/${falls}`);
    ok(strays === 0, 'and never leaves the band between where it started and where it is going', String(strays));

    // The exit's dials, so the CSS and the JS clock can be compared below.
    ok(V.OUT_CLASS === 'gg-vwin--out' && V.OUT_ANIM_MS === 300,
      'the exit is a 300ms one-shot hung off .gg-vwin--out', `${V.OUT_CLASS} ${V.OUT_ANIM_MS}ms`);
    ok(V.GHOST_REMOVE_MS >= V.OUT_ANIM_MS * 1.5,
      'and the backstop timer is comfortably past it — animationend is not a promise',
      `${V.GHOST_REMOVE_MS} vs ${V.OUT_ANIM_MS}`);

    // The cap arithmetic: never past 60s, never past what the payload asked,
    // never under a second, and a missing duration falls back to 30s.
    ok(V.windowCapMs(90000) === 60000, 'a 90s payload is capped at 60s', String(V.windowCapMs(90000)));
    ok(V.windowCapMs(20000) === 20000, 'a 20s payload keeps its own 20s', String(V.windowCapMs(20000)));
    ok(V.windowCapMs(0) === 30000, 'a payload with no duration falls back to 30s', String(V.windowCapMs(0)));
    ok(V.windowCapMs(200) === 1000, 'and nothing floats for less than a second', String(V.windowCapMs(200)));

    ok(V.baseWindowWidth(1280) >= 220 && V.baseWindowWidth(1280) <= 400,
      'the born width is phone-screen-ish', String(V.baseWindowWidth(1280)));
    ok(V.baseWindowWidth(600) === 220 && V.baseWindowWidth(4000) === 400,
      'and clamped at both ends', `${V.baseWindowWidth(600)} / ${V.baseWindowWidth(4000)}`);

    // PLACEMENT: never born on MERCY's gutter, never on the opponent's monitor,
    // never off-screen. Swept with a deterministic RNG so a green run is a green
    // run — 400 placements across three viewport shapes.
    let seed = 12345;
    const rnd = () => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return seed / 0x7fffffff; };
    const overlaps = (x, y, w, h, r) => !(x + w <= r.x0 || x >= r.x1 || y + h <= r.y0 || y >= r.y1);
    let onMercy = 0, onMon = 0, offScreen = 0, tried = 0;
    for (const [W, H] of [[1280, 720], [1920, 1080], [900, 1400]]) {
      const zones = V.keepOutRects(W, H);
      const w = V.baseWindowWidth(W), h = w * 9 / 16;
      for (let i = 0; i < 400; i++) {
        const p = V.placeWindow(W, H, w, h, rnd);
        tried++;
        if (overlaps(p.x, p.y, w, h, zones.mercy)) onMercy++;
        if (overlaps(p.x, p.y, w, h, zones.monitor)) onMon++;
        if (p.x < 0 || p.y < 0 || p.x + w > W || p.y + h > H) offScreen++;
      }
    }
    ok(tried === 1200, '1200 placements swept', String(tried));
    ok(onMercy === 0, 'not one window is born over the mercy gutter', String(onMercy));
    ok(onMon === 0, 'not one is born over the opponent monitor', String(onMon));
    ok(offScreen === 0, 'and not one is born off-screen', String(offScreen));
    // The mercy keep-out really is the bottom strip it claims to be.
    const z = V.keepOutRects(1280, 720);
    ok(Math.round(720 - z.mercy.y0) === V.MERCY_KEEPOUT_PX,
      'the mercy keep-out is exactly the bottom 96px', String(720 - z.mercy.y0));
    ok(z.monitor.x1 === 1280 && z.monitor.x0 > 640, 'the monitor keep-out hugs the right edge',
      `${z.monitor.x0}..${z.monitor.x1}`);

    const seen = new Set();
    for (let i = 0; i < 400; i++) seen.add(V.glitchVariant());
    ok(seen.size === V.GLITCH_VARIANTS && !seen.has(0) && !seen.has(5),
      'the spawn-in picker draws all four variants and only those', Array.from(seen).sort().join(','));
  }

  // ------------------------------- video windows: mounting, the cap, the pool
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const host = byId.get('gg-fx-vwin');
    const stage = byId.get('gg-stage');
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);
    const winsOf = () => liveWins(host);
    const vids = () => winsOf().map((w) => w.findAll('gg-vwin-vid')[0]).filter(Boolean);

    m.emitPayload({
      payload: payloadOf({ id: 'pV1', kind: GoonPayloadKind.Video, duration_ms: 1200, intensity: 0.8 }),
      fireAtLocalMs: localMonotonicMs(),
    });
    await sleep(80);
    ok(winsOf().length === 1, 'a video payload mounts ONE floating window', String(winsOf().length));
    ok(stage.childNodes.length === 0,
      'and NOTHING on #gg-stage — a husk there is a full-screen click shield', String(stage.childNodes.length));

    // The node shape IS the animation contract: four loops/one-shots, four
    // different elements (see the fx.css pins below).
    const w0 = winsOf()[0];
    const drift = w0.findAll('gg-vwin-drift')[0];
    const inner = w0.findAll('gg-vwin-inner')[0];
    const dot = w0.findAll('gg-vwin-dot')[0];
    ok(!!drift && !!inner && !!dot, 'the window is wrapper > drift > inner > (video + dot)');
    ok(drift.parentNode === w0 && inner.parentNode === drift && dot.parentNode === inner,
      'and it is nested in that order, so no two animations share an element');
    ok(/gg-vwin-in--[1-4]/.test(inner.className), 'the inner carries one of the four glitch-in variants',
      inner.className);
    ok(/px$/.test(w0.style.getPropertyValue('left')) && /px$/.test(w0.style.getPropertyValue('top')),
      'the window is anchored in px', `${w0.style.getPropertyValue('left')} ${w0.style.getPropertyValue('top')}`);
    ok(/px$/.test(w0.style.getPropertyValue('--gg-vwin-w')), 'sized through --gg-vwin-w (never a transform scale)',
      w0.style.getPropertyValue('--gg-vwin-w'));
    ok(parseFloat(drift.style.getPropertyValue('--gg-vwin-dy')) < 0,
      'the drift is biased UPWARD, away from the mercy gutter', drift.style.getPropertyValue('--gg-vwin-dy'));
    ok(vids()[0] && String(vids()[0].src).includes('ccp.assets'),
      'the clip comes from the player own library through media.acquire', String(vids()[0] && vids()[0].src));
    ok(vids()[0] && vids()[0].loop === false, 'it does NOT loop — a window dies when its clip ends');

    // The cap: 1200ms was asked for, so 1200ms is what it gets. Ran out = endured.
    await sleep(1500);
    ok(m.receipts.length === 1 && m.receipts[0].endured === true,
      'a window that ran its cap receipts endured', JSON.stringify(m.receipts));
    // THE POOL DEPARTS BEFORE THE NODE DOES, and this is the moment to catch it:
    // the receipt is already posted and the count is already 0, while the husk
    // is still on the layer playing out its exit.
    const videosR = ex.rendererFor(GoonElement.Videos);
    ok(videosR.windowCount() === 0 && winsOf().length === 0,
      'the pool is empty the instant it settles — nothing lingering holds a slot',
      `${videosR.windowCount()} / ${winsOf().length}`);
    ok(ghostWins(host).length === 1 && videosR.ghostCount() === 1,
      'and what lingers is a GHOST: out of the pool, still on the layer',
      `${ghostWins(host).length} node(s) / ${videosR.ghostCount()} tracked`);
    ok(ghostWins(host)[0].style.getPropertyValue('pointer-events') === 'none',
      'which can never eat a click on its way out',
      ghostWins(host)[0].style.getPropertyValue('pointer-events'));
    await sleep(GHOST_WAIT);
    ok(winsOf().length === 0, 'and the node is torn out afterwards', String(winsOf().length));
    ok(host.childNodes.length === 0 && videosR.ghostCount() === 0,
      'ghost and all — the exit removes itself, it does not merely fade',
      `${host.childNodes.length} / ${videosR.ghostCount()}`);

    // -------- the pool: 4 live, and a 5th settles the OLDEST first
    for (let i = 0; i < 4; i++) {
      m.emitPayload({
        payload: payloadOf({ id: 'pP' + i, kind: GoonPayloadKind.Video, duration_ms: 40000 }),
        fireAtLocalMs: localMonotonicMs(),
      });
      await sleep(40);
    }
    await sleep(80);
    ok(winsOf().length === 4, 'four payloads, four windows', String(winsOf().length));
    ok(m.receipts.length === 1, 'and none of them has finished yet', String(m.receipts.length));

    const oldest = winsOf()[0];
    m.emitPayload({
      payload: payloadOf({ id: 'pP4', kind: GoonPayloadKind.Video, duration_ms: 40000 }),
      fireAtLocalMs: localMonotonicMs(),
    });
    await sleep(120);
    ok(ex.rendererFor(GoonElement.Videos).windowCount() === 4,
      'a FIFTH window never makes it five', String(ex.rendererFor(GoonElement.Videos).windowCount()));
    const evicted = m.receipts.find((r) => r.id === 'pP0');
    ok(!!evicted && evicted.endured === true,
      'the OLDEST is the one that settles, and being displaced is enduring it', JSON.stringify(evicted));
    ok(winsOf().length === 4 && oldest.isConnected,
      'the slot the fifth window took was free BEFORE the evicted node left the DOM',
      `${winsOf().length} live, evicted still connected: ${oldest.isConnected}`);
    await sleep(GHOST_WAIT);
    ok(!oldest.isConnected, 'the evicted window is really gone from the DOM');
    ok(winsOf().length === 4, 'and the layer is back to four nodes once its exit lands',
      String(winsOf().length));

    // -------- only the newest speaks
    const live = vids();
    ok(live.length === 4, 'four videos are live', String(live.length));
    ok(live.slice(0, 3).every((v) => v.muted === true) && live[3].muted === false,
      'only the NEWEST window is unmuted', live.map((v) => (v.muted ? 'm' : 'SOUND')).join(','));
    ok(live[3].volume > 0 && live[3].volume <= 0.55, 'and it plays at a modest volume', String(live[3].volume));
    // …and the three it took the mic from ended their fade-out at real zero, not
    // merely muted at whatever they happened to be playing at.
    ok(live.slice(0, 3).every((v) => v.volume === 0),
      'the silenced three rode their ramp all the way to 0 before .muted flipped',
      live.slice(0, 3).map((v) => v.volume).join(','));

    // -------- stopAll: every window gone, nothing left on the stage
    ex.stopAll();
    ok(ex.rendererFor(GoonElement.Videos).windowCount() === 0,
      'executor.stopAll leaves ZERO windows (this is what clearForRecap rests on)',
      String(ex.rendererFor(GoonElement.Videos).windowCount()));
    ok(host.childNodes.length === 0, 'the vwin layer is empty', String(host.childNodes.length));
    ok(stage.childNodes.length === 0, 'and the stage was never touched by them', String(stage.childNodes.length));
    for (const id of ['pP1', 'pP2', 'pP3', 'pP4']) {
      const r = m.receipts.find((x) => x.id === id);
      ok(!!r && r.endured === false, `${id} was cancelled, so it receipts completed (no charge)`, JSON.stringify(r));
    }
    await sleep(300);
    ok(m.receipts.length === 6, 'no late second receipt from a cancelled window', String(m.receipts.length));
    ex.detach();
  }

  // -------------------- video windows: click, drag, wheel, and the clip ending
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const host = byId.get('gg-fx-vwin');
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);
    const winsOf = () => liveWins(host);
    const fire = (id) => m.emitPayload({
      payload: payloadOf({ id, kind: GoonPayloadKind.Video, duration_ms: 40000 }),
      fireAtLocalMs: localMonotonicMs(),
    });

    // ---- a press that never moves is a CLICK, and a click dismisses
    fire('pC1');
    await sleep(80);
    const clicked = winsOf()[0];
    ok(!!clicked, 'a window to click');
    PID++;
    ptr(clicked, 'pointerdown', 300, 300);
    ok(!clicked._cls.has('is-grabbed'), 'pointerdown alone is not a grab — the press is still deciding');
    ptr(clicked, 'pointermove', 304, 303);     // 5px: inside DRAG_SLOP_PX
    ok(!clicked._cls.has('is-grabbed'), 'a wobble inside the slop is still a click', clicked.className);
    ptr(clicked, 'pointerup', 304, 303);
    const c1 = m.receipts.find((r) => r.id === 'pC1');
    ok(!!c1 && c1.endured === true,
      'clicking a window closes it, and closing it yourself is ENDURED', JSON.stringify(c1));
    ok(clicked._cls.has('gg-vwin--out') && !clicked._cls.has('is-on'),
      'the dismissed node glitches OUT rather than vanishing', clicked.className);
    await sleep(GHOST_WAIT);
    ok(winsOf().length === 0, 'the clicked window is torn out', String(winsOf().length));
    ok(!clicked.isConnected, 'and the ghost with it — an exit that never removed the node is a leak');

    // ---- past the slop it is a DRAG: it follows the pointer and is NOT dismissed
    fire('pC2');
    await sleep(80);
    const dragged = winsOf()[0];
    PID++;
    const down = ptr(dragged, 'pointerdown', 500, 500);
    ok(down.defaulted === true, 'the press preventDefaults (no native drag ghost, no scroll)');
    ptr(dragged, 'pointermove', 600, 600);
    ok(dragged._cls.has('is-grabbed'),
      'past the slop the window is in hand — and .is-grabbed is what kills the drift keyframe',
      dragged.className);
    ok(dxOf(dragged) === 100, 'it follows the pointer one-to-one, in px on top of its anchor',
      dragged.style.getPropertyValue('transform'));
    ok(!/scale\(/.test(dragged.style.getPropertyValue('transform')),
      'and the drag transform carries NOTHING but the offset (size is the width var)',
      dragged.style.getPropertyValue('transform'));
    ptr(dragged, 'pointerup', 600, 600);
    ok(!dragged._cls.has('is-grabbed'), 'released: out of hand', dragged.className);
    ok(dragged.isConnected && !m.receipts.some((r) => r.id === 'pC2'),
      'a press that became a drag is NOT a click — dragging never dismisses');
    ok(dxOf(dragged) === 100, 'and it stays where it was dropped', String(dxOf(dragged)));

    // ---- wheel over it resizes, through the var, clamped both ways
    const sizeOf = (el) => parseFloat(el.style.getPropertyValue('--gg-vwin-w'));
    const V = await import('../exec/videos.js');
    const base = V.baseWindowWidth(1280);
    ok(Math.abs(sizeOf(dragged) - base) < 1.5, 'a window is born at the base width', String(sizeOf(dragged)));
    const wev = ptr(dragged, 'wheel', 600, 600, { deltaY: -100 });
    ok(wev.defaulted === true, 'a wheel over a window preventDefaults so the page can never scroll');
    ok(Math.abs(sizeOf(dragged) - base * V.WHEEL_STEP) < 1.5,
      'one notch up grows it by exactly one wheel step', `${base} -> ${sizeOf(dragged)}`);
    for (let i = 0; i < 30; i++) ptr(dragged, 'wheel', 600, 600, { deltaY: -120 });
    ok(Math.abs(sizeOf(dragged) - base * V.SIZE_MAX_FACTOR) < 1.5,
      'growth clamps at 2.5x its base', String(sizeOf(dragged)));
    for (let i = 0; i < 60; i++) ptr(dragged, 'wheel', 600, 600, { deltaY: 120 });
    ok(Math.abs(sizeOf(dragged) - base * V.SIZE_MIN_FACTOR) < 1.5,
      'and shrink clamps at 0.5x', String(sizeOf(dragged)));
    ok(!/scale\(/.test(dragged.style.getPropertyValue('transform')),
      'the wheel never stacks a transform scale on the drag offset',
      dragged.style.getPropertyValue('transform'));

    // ---- pointercancel is a gentle drop: no dismissal, no stuck hand
    PID++;
    ptr(dragged, 'pointerdown', 200, 200);
    ptr(dragged, 'pointermove', 280, 250);
    ok(dragged._cls.has('is-grabbed'), 'in hand again', dragged.className);
    ptr(dragged, 'pointercancel', 280, 250);
    ok(!dragged._cls.has('is-grabbed') && dragged.isConnected && !m.receipts.some((r) => r.id === 'pC2'),
      'pointercancel drops it where it lies: no dismissal, no stuck hold', dragged.className);
    // The hand is empty again — the very next press is a clean click.
    tap(dragged, 280, 250);
    const c2 = m.receipts.find((r) => r.id === 'pC2');
    ok(!!c2 && c2.endured === true, 'and the next press still dismisses, so no drag state was left stuck',
      JSON.stringify(c2));
    await sleep(GHOST_WAIT);

    // ---- the clip ending closes the window on its own
    fire('pC3');
    await sleep(80);
    const ending = winsOf()[0];
    const vid = ending.findAll('gg-vwin-vid')[0];
    ok(!!vid && typeof vid.onended === 'function', 'the window listens for its clip ending');
    vid.onended();
    const c3 = m.receipts.find((r) => r.id === 'pC3');
    ok(!!c3 && c3.endured === true, 'a clip that played out endures, without waiting for the 60s cap',
      JSON.stringify(c3));
    await sleep(GHOST_WAIT);
    ok(winsOf().length === 0, 'and the window goes with it', String(winsOf().length));

    // ---- the cancel fn the executor holds removes the window on the spot
    const videos = ex.rendererFor(GoonElement.Videos);
    let endured = null;
    const cancel = videos.renderPayload({ id: 'pC4', duration_ms: 40000, intensity: 0.5 }, (e) => { endured = e; });
    ok(typeof cancel === 'function', 'renderPayload returns a cancel fn (the uniform renderer shape)');
    await sleep(40);
    ok(videos.windowCount() === 1, 'the direct render put one window up', String(videos.windowCount()));
    cancel();
    ok(videos.windowCount() === 0 && endured === false,
      'and its cancel fn takes it down, receipting completed', `${videos.windowCount()} / ${endured}`);
    // THE CANCEL FN IS TEARDOWN, not a dismissal: it is what executor.stopAll
    // reaches a thrown window through, so it may not leave a ghost fading onto
    // the recap. Node gone in the same tick, no exit animation at all.
    ok(host.childNodes.length === 0 && videos.ghostCount() === 0,
      'and it leaves NOTHING behind — no husk, no ghost, no exit',
      `${host.childNodes.length} node(s) / ${videos.ghostCount()} ghost(s)`);
    cancel();
    ok(endured === false, 'a second cancel is a no-op (done() fires at most once)');
    ex.stopAll();
    ex.detach();
  }

  /* ---------------- video windows: the audio really ramps (2026-08-04)
   * The pure maths above is the curve; this is the wiring. Everything here is a
   * bug that used to be audible: the mic changing hands with a step, a window
   * arriving at full volume, a leaver muted mid-word, or two lerps fighting over
   * one element's volume because nobody cancelled the first.
   * ------------------------------------------------------------------------ */
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const host = byId.get('gg-fx-vwin');
    const V = await import('../exec/videos.js');
    const vids = V.createVideos({ layers, media: fakeMedia(), logger: quiet });
    const vidIn = (i) => (liveWins(host)[i] || { findAll: () => [] }).findAll('gg-vwin-vid')[0];
    const A = V.volumeFor(0.5), B = V.volumeFor(0.9);

    // ---- ARRIVING: born silent, unmuted first, and genuinely mid-flight after
    vids.renderPayload({ id: 'pA1', duration_ms: 40000, intensity: 0.5 }, () => {});
    const a = vidIn(0);
    ok(!!a && a.volume === 0,
      'a window is BORN silent — the mic is handed over by a ramp, not by a step', String(a && a.volume));
    ok(a.muted === false,
      'and UNMUTED first: ramping the volume of a muted element ramps nothing at all', String(a.muted));
    await sleep(150);
    const mid = a.volume;
    ok(mid > 0 && mid < A, '150ms in it is on its way, not already there', `${mid} of ${A}`);
    await sleep(500);
    ok(Math.abs(a.volume - A) < 1e-9,
      'and it lands exactly on volumeFor(intensity) — the ramp never sets the level', `${a.volume} / ${A}`);

    // ---- THE HANDOVER CROSSFADES: both ramps run at once, neither waits
    vids.renderPayload({ id: 'pA2', duration_ms: 40000, intensity: 0.9 }, () => {});
    const b = vidIn(1);
    await sleep(120);
    ok(a.volume > 0 && a.volume < A && b.volume > 0 && b.volume < B,
      'a handover crossfades: the old one is still falling while the new one is already rising',
      `${a.volume} down / ${b.volume} up`);
    ok(a.muted === false,
      'and the leaver stays UNMUTED while it falls — a mute flip would cut it dead mid-fade',
      String(a.muted));
    await sleep(500);
    ok(a.volume === 0 && a.muted === true,
      'only at the bottom of the ramp does the old window mute', `${a.volume} muted=${a.muted}`);
    ok(Math.abs(b.volume - B) < 1e-9 && b.muted === false,
      'while the new one is at its own full level', `${b.volume} / ${B}`);

    // ---- INHERITING: the mic goes back, and it fades back IN
    vids.stopWindows();                       // teardown of both, immediately
    ok(vids.windowCount() === 0 && host.childNodes.length === 0,
      'teardown leaves nothing at all', `${vids.windowCount()} / ${host.childNodes.length}`);

    vids.renderPayload({ id: 'pA3', duration_ms: 40000, intensity: 0.5 }, () => {});
    const c = vidIn(0);
    const cancelD = vids.renderPayload({ id: 'pA4', duration_ms: 40000, intensity: 0.5 }, () => {});
    await sleep(400);
    ok(c.volume === 0 && c.muted === true, 'the older window has gone quiet', String(c.volume));
    cancelD();
    ok(c.muted === false, 'the survivor is unmuted the instant it inherits the mic', String(c.muted));
    ok(c.volume < A, 'but not yet loud', `${c.volume} of ${A}`);
    await sleep(150);
    const back = c.volume;
    ok(back > 0 && back < A,
      'it fades back IN from where it actually was, rather than snapping to level', `${back} of ${A}`);
    await sleep(450);
    ok(Math.abs(c.volume - A) < 1e-9, 'and gets all the way home', String(c.volume));

    // ---- AN END YOU CAN SEE COMING IS FADED BEFORE IT ARRIVES. A clip that just
    // ENDS cannot be faded afterwards, so the cap arms its own ramp-out.
    vids.stopWindows();
    vids.renderPayload({ id: 'pA5', duration_ms: 1000, intensity: 0.5 }, () => {});
    const e = vidIn(0);
    await sleep(600);
    const peak = e.volume;
    ok(Math.abs(peak - A) < 1e-9, 'a 1s window reaches full level first', String(peak));
    await sleep(280);                          // 880ms: 180ms into the armed fade
    ok(e.volume > 0 && e.volume < peak,
      'and is already fading out BEFORE its cap, not cut off at it', `${e.volume} of ${peak}`);
    await sleep(120 + GHOST_WAIT);            // the last 120ms of the cap, then the ghost clock
    ok(vids.windowCount() === 0 && vids.ghostCount() === 0 && host.childNodes.length === 0,
      'then it goes, ghost and all', `${vids.windowCount()}/${vids.ghostCount()}/${host.childNodes.length}`);

    // ---- A DISMISSAL fades on the way out instead: nobody saw it coming, so the
    // ramp rides the GHOST while the exit animation plays.
    vids.renderPayload({ id: 'pA6', duration_ms: 40000, intensity: 0.5 }, () => {});
    const f = vidIn(0);
    await sleep(600);
    const node = liveWins(host)[0];
    PID++;
    ptr(node, 'pointerdown', 300, 300);
    ptr(node, 'pointerup', 300, 300);
    ok(vids.windowCount() === 0 && vids.ghostCount() === 1,
      'the record left the pool and the node became a ghost, in that order',
      `${vids.windowCount()} pooled / ${vids.ghostCount()} ghost`);
    ok(f.volume > 0 && f.muted === false, 'whose audio is still up at the moment of the click', String(f.volume));
    await sleep(150);
    ok(f.volume > 0 && f.volume < A, 'and fades out under the exit animation', `${f.volume} of ${A}`);
    await sleep(GHOST_WAIT);
    ok(vids.ghostCount() === 0 && host.childNodes.length === 0, 'before the node is torn out',
      `${vids.ghostCount()} / ${host.childNodes.length}`);

    // ---- TEARDOWN NEVER FADES AND NEVER GHOSTS.
    vids.renderPayload({ id: 'pA7', duration_ms: 40000, intensity: 0.5 }, () => {});
    const g = vidIn(0);
    await sleep(600);
    ok(g.volume > 0, 'one last window, up and audible', String(g.volume));
    vids.stopWindows();
    ok(host.childNodes.length === 0 && vids.ghostCount() === 0,
      'stopWindows takes the node in the same tick — the recap inherits an empty layer',
      `${host.childNodes.length} / ${vids.ghostCount()}`);
    const cut = g.volume;
    await sleep(120);
    ok(g.volume === cut,
      'and its ramp was cancelled with it: nothing keeps writing volume to a detached element',
      `${cut} -> ${g.volume}`);
  }

  /* ---------------- video windows: the CALM exit (prefers-reduced-motion)
   * fx.css switches the exit animation off for a calm player, and an animation
   * that never plays never fires animationend — which is exactly how a node gets
   * stranded. So the calm path does not wait for one: no .gg-vwin--out at all,
   * the plain opacity fade the base rule already transitions, and a SHORTER
   * clock than the 600ms backstop. "Never lingers un-animated" is the rule.
   *
   * matchMedia is absent from the stub on purpose (every module must survive a
   * host without it), so it is conjured for exactly one createVideos() call —
   * `calm` is read once, when the renderer is built — and taken away again.
   * ------------------------------------------------------------------------ */
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const host = byId.get('gg-fx-vwin');
    const V = await import('../exec/videos.js');
    let calmVids = null;
    try {
      globalThis.matchMedia = (q) => ({ matches: /prefers-reduced-motion/.test(String(q)) });
      calmVids = V.createVideos({ layers, media: fakeMedia(), logger: quiet });
    } finally {
      delete globalThis.matchMedia;
    }
    calmVids.renderPayload({ id: 'pCM', duration_ms: 40000, intensity: 0.5 }, () => {});
    const node = liveWins(host)[0];
    ok(!!node && !node.findAll('gg-vwin-drift')[0]._cls.has('gg-deco'),
      'a calm window is built without the decoration opt-in, as it always was');
    PID++;
    ptr(node, 'pointerdown', 300, 300);
    ptr(node, 'pointerup', 300, 300);
    ok(calmVids.windowCount() === 0, 'a click still dismisses it', String(calmVids.windowCount()));
    ok(!node._cls.has(V.OUT_CLASS),
      'and it is NOT given an exit class it would then wait forever on', node.className);
    ok(!node._cls.has('is-on') && node.isConnected,
      'it fades on the wrapper\'s own opacity transition instead', node.className);
    await sleep(400);
    ok(!node.isConnected && calmVids.ghostCount() === 0,
      'and it is gone well inside the backstop — a calm player never sits looking at a husk',
      `connected=${node.isConnected} ghosts=${calmVids.ghostCount()}`);
    ok(V.GHOST_REMOVE_MS > 400,
      '…and it beat the backstop outright: the removal came from the calm clock, not from the net',
      `gone by 400ms, backstop is ${V.GHOST_REMOVE_MS}ms`);
    calmVids.stopWindows();
  }

  /* --------------------------- video windows: the fx.css contract they rest on
   * Three of these are STYLESHEET facts no JS assertion can reach, and each one
   * is a bug waiting to come back:
   *   1. `animation` is a SHORTHAND. A one-shot glitch-in and a forever drift
   *      declared on ONE element cancel each other — so the wrapper, the drift,
   *      the glitch, the glow and the dot are five different animation slots on
   *      four different nodes, and the wrapper's is EMPTY (JS writes its
   *      transform inline for the drag, and keyframes outrank inline styles).
   *   2. every loop parks at html[data-gg-fx="hot"] through --gg-deco-play,
   *      because all of it is decoration — while drag/wheel/click, which are not
   *      animations, keep working parked.
   *   3. the layer is click-through and the WINDOW opts back in. A window you
   *      cannot dismiss is worse than one that ate a click.
   * ------------------------------------------------------------------------ */
  {
    const fs = await import('node:fs/promises');
    const url = await import('node:url');
    const raw = await fs.readFile(url.fileURLToPath(new URL('../exec/fx.css', import.meta.url)), 'utf8');
    const css = raw.replace(/\/\*[\s\S]*?\*\//g, '');
    const esc = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const ruleOf = (sel) => {
      const m = new RegExp(esc(sel) + '\\s*\\{([^}]*)\\}').exec(css);
      return m ? m[1] : null;
    };
    const animOf = (sel) => {
      const b = ruleOf(sel);
      const m = b && /(^|[;\s])animation:\s*([^;]+)/.exec(b);
      return m ? m[2].trim() : null;
    };
    const countAnim = (sel) => {
      const b = ruleOf(sel) || '';
      return (b.match(/(^|[;\s])animation:/g) || []).length;
    };

    // 1. THE SHORTHAND TRAP
    ok(ruleOf('.gg-vwin') !== null, 'fx.css has the .gg-vwin wrapper rule');
    ok(countAnim('.gg-vwin') === 0,
      'the wrapper declares NO animation — the inline drag transform must stay authoritative',
      String(animOf('.gg-vwin')));
    for (const sel of ['.gg-vwin-drift', '.gg-vwin-dot', '.gg-vwin-inner::after']) {
      ok(countAnim(sel) === 1, `${sel} declares exactly one animation`, String(countAnim(sel)));
      ok(/infinite/.test(animOf(sel) || ''), `${sel} is the LOOP it is supposed to be`, String(animOf(sel)));
    }
    ok(countAnim('.gg-vwin-inner') === 0,
      'the inner element leaves its animation slot for the one-shot variant classes',
      String(animOf('.gg-vwin-inner')));
    for (let i = 1; i <= 4; i++) {
      const a = animOf(`.gg-vwin-in--${i}`);
      ok(!!a, `the spawn-in variant .gg-vwin-in--${i} exists`, String(a));
      ok(/\b1\s*$/.test(String(a)) && !/infinite/.test(String(a)),
        `variant ${i} is a ONE-SHOT (a loop here would fight nothing but itself, forever)`, String(a));
      ok(!/forwards/.test(String(a)),
        `variant ${i} does not hold its last frame (that would outrank the drift + drag transforms)`, String(a));
      ok(new RegExp('@keyframes\\s+ggVwinIn' + i + '\\s*\\{').test(css), `and ggVwinIn${i} is defined`);
    }
    // The four variants really are four different animations.
    const names = [1, 2, 3, 4].map((i) => String(animOf(`.gg-vwin-in--${i}`)).split(/\s+/)[0]);
    ok(new Set(names).size === 4, 'the four spawn-ins are four DIFFERENT keyframes so 4 windows never look cloned',
      names.join(','));

    /* 1b. THE EXIT — the ONE animation the wrapper is allowed, and the whole
     * reason it is allowed is that by then the record has left the pool: there
     * is no drag to outrank, no slot to hold and no click to eat. Three ways it
     * could still go wrong, all pinned:
     *   · a TRANSFORM keyframe here would teleport a dismissed window back to
     *     its anchor for its last 300ms (the drag offset is inline on this very
     *     element — the .gg-flash--grabbed law, one element further out);
     *   · `forwards` would hold the husk's last frame forever if anything ever
     *     stopped the node being removed;
     *   · --gg-deco-play would PARK the exit at data-gg-fx="hot", i.e. freeze a
     *     corpse on screen. The exit is the one thing here that is not decoration.
     */
    const VV = await import('../exec/videos.js');
    const outSel = `.gg-vwin.${VV.OUT_CLASS}`;
    const out = ruleOf(outSel) || '';
    ok(!!out, `fx.css carries the exit rule ${outSel}`);
    ok(countAnim(outSel) === 1, 'with exactly one animation on it', String(countAnim(outSel)));
    const outAnim = String(animOf(outSel));
    ok(/^ggVwinOut\b/.test(outAnim) && /\b1\s*$/.test(outAnim) && !/infinite/.test(outAnim),
      'a ONE-SHOT ggVwinOut — a looping exit would never end and never remove anything', outAnim);
    ok(!/forwards/.test(outAnim),
      'and NOT `forwards`: the node is removed, so holding its last frame can only ever strand it', outAnim);
    ok(outAnim.includes(`${VV.OUT_ANIM_MS}ms`),
      'its duration is the one exec/videos.js budgets for (OUT_ANIM_MS)', `${outAnim} vs ${VV.OUT_ANIM_MS}ms`);
    ok(/pointer-events:\s*none/.test(out),
      'the ghost cannot eat a click on its way out — the husk-as-click-shield bug, one last time', out.replace(/\s+/g, ' ').trim());
    ok(!/animation-play-state/.test(out),
      'and it never parks at data-gg-fx="hot" — a parked exit is a frozen corpse', out.replace(/\s+/g, ' ').trim());
    const outKf = (/@keyframes\s+ggVwinOut\s*\{([\s\S]*?)\n\}/.exec(css) || [])[1] || '';
    ok(!!outKf, 'ggVwinOut is defined');
    ok(!/transform/.test(outKf),
      'and animates NO transform: the drag offset is an inline transform on this very element', outKf.replace(/\s+/g, ' ').trim());
    ok(/100%\s*\{[^}]*opacity:\s*0\b/.test(outKf),
      'it ends at opacity 0, so the frame before removal is already empty', outKf.replace(/\s+/g, ' ').trim());
    ok(/clip-path/.test(outKf), 'the tear is clip-path (a compositor property, like everything else here)');

    // 2. THE HEAT ARMOR — every loop parks, nothing functional does.
    for (const sel of ['.gg-vwin-drift', '.gg-vwin-dot', '.gg-vwin-inner::after']) {
      ok(/animation-play-state:\s*var\(--gg-deco-play/.test(ruleOf(sel) || ''),
        `${sel} parks at data-gg-fx="hot" (--gg-deco-play)`, String(ruleOf(sel)));
    }
    ok(!/animation-play-state/.test(ruleOf('.gg-vwin') || ''),
      'the wrapper has nothing to park — dragging a window works exactly the same when hot');
    // IN HAND the drift is switched off outright: a live keyframe on the drift
    // element would keep re-writing its transform under the drag.
    ok(/\.gg-vwin\.is-grabbed\s+\.gg-vwin-drift\s*\{[^}]*animation:\s*none/.test(css),
      'a grabbed window kills its drift keyframe (animation: none), the .gg-flash--grabbed law');

    // 3. POINTERS
    ok(/pointer-events:\s*auto/.test(ruleOf('.gg-vwin') || ''),
      'the window opts back into pointer events (the .gg-bubble pattern)');
    ok(/touch-action:\s*none/.test(ruleOf('.gg-vwin') || ''),
      'and claims the touch that starts on it — it is draggable');
    ok(!new RegExp('body:has\\(#gg-stage > \\*\\)[^{]*\\.gg-vwin\\b').test(css),
      'nothing takes the window pointer opt-in away while the stage is occupied (a window MUST stay dismissable)');

    // THE LOOK the owner asked for: a red live dot, top-right, and a house glow.
    const dot = ruleOf('.gg-vwin-dot') || '';
    ok(/position:\s*absolute/.test(dot) && /top:/.test(dot) && /right:/.test(dot),
      'the recording dot is pinned to the TOP-RIGHT of the window', dot.replace(/\s+/g, ' ').trim());
    ok(/background:\s*#ff2d4b/i.test(dot), 'and it is red', dot.replace(/\s+/g, ' ').trim());
    const inner = ruleOf('.gg-vwin-inner') || '';
    ok(/box-shadow:[^;]*--gg-pink-rgb/.test(inner) && /box-shadow:[^;]*--gg-violet-rgb/.test(inner),
      'the border glows in the house pink/violet, like .gg-plate', inner.replace(/\s+/g, ' ').trim());
    ok(/border:[^;]*--gg-pink-rgb/.test(inner), 'and the border itself is pink');

    // REDUCED MOTION: no drift, no glitch, no blink — but it still ARRIVES and
    // is still fully handleable, because none of that is a keyframe.
    const calmBlock = css.slice(css.indexOf('@media (prefers-reduced-motion: reduce)'));
    ok(/\.gg-vwin-drift[^}]*\{[^}]*animation:\s*none/.test(calmBlock)
      || /\.gg-vwin-drift,[\s\S]{0,120}?animation:\s*none/.test(calmBlock),
      'reduced motion stops the drift');
    ok(/\.gg-vwin-dot/.test(calmBlock) && /\.gg-vwin-inner/.test(calmBlock),
      'and the glitch-in, the glow and the dot pulse with it');
    ok(!/\.gg-vwin\s*\{[^}]*pointer-events:\s*none/.test(calmBlock),
      'but it never takes the window away from the player');
    // …and it still LEAVES. Switching an exit animation off is the one calm rule
    // that could strand a node — no keyframes, no animationend — so the calm
    // exit is a plain opacity drop and exec/videos.js runs its own short timer
    // rather than waiting on an event that is never coming.
    ok(new RegExp('\\.gg-vwin\\.' + VV.OUT_CLASS + '\\s*\\{[^}]*animation:\\s*none').test(calmBlock),
      'reduced motion switches the exit animation off');
    ok(new RegExp('\\.gg-vwin\\.' + VV.OUT_CLASS + '\\s*\\{[^}]*opacity:\\s*0').test(calmBlock),
      'and drops the ghost to opacity 0 instead — a calm player never sits looking at a husk');

    // TASK 2: the spiral grain. A CONSTANT blur is only safe because nothing
    // animates filter on that pane — ggSpiralSpin is transform-only.
    const spiral = ruleOf('.gg-spiral') || '';
    const blur = /filter:\s*blur\(([\d.]+)px\)/.exec(spiral);
    ok(!!blur, 'the spiral pane carries a constant blur (the dither is what reads as grain)', spiral.replace(/\s+/g, ' ').trim());
    const px = blur ? Number(blur[1]) : NaN;
    ok(px >= 0.8 && px <= 1.5, 'and it is in the 0.8..1.5px band that softens dither without smearing the spiral',
      String(px));
    const spin = /@keyframes\s+ggSpiralSpin\s*\{([\s\S]*?)\}\s*\n/.exec(css);
    ok(!!spin && !/filter/.test(spin[1]),
      'ggSpiralSpin still animates transform ONLY — a filter keyframe would outrank the blur', String(spin && spin[1]));
    ok(!/\.gg-spiral[^{]*\{[^}]*transition:[^;]*filter/.test(css),
      'and nothing transitions filter on it either');
  }

  /* ============================ VIDEO BUBBLES — the prism kind that opens one ==
   * The floating window was reachable ONE way: an opponent throwing a Video
   * payload at you. That makes the whole object untestable solo and unearnable
   * in a duel where nobody spends a charge on it. So the bubble field grew a
   * sixth effect kind — `video`, wearing the Fall's PRISM sprite — and popping
   * it opens a window off the player's OWN library.
   *
   * FOUR RULES, and every one of them is an exploit if it slips:
   *   1. the pop is an ordinary effect pop: worth 2.5 to the drop economy, no
   *      more and no less, whether or not a window actually appears;
   *   2. a SWARM may never mint it (PAYLOAD_MINT_EXCLUDED) — otherwise throwing
   *      one swarm papers the victim's screen in free windows;
   *   3. a self-pop NEVER EVICTS. At MAX_WINDOWS it fizzles, because an
   *      opponent's window is something you are being asked to close and popping
   *      your own bubble must not close it for you (and must not receipt it
   *      `survived` on the way past). A PAYLOAD still evicts the oldest, local
   *      windows included — one pool, one FIFO;
   *   4. nothing about it reaches the wire: no receipt, no charge, no id.
   *
   * The seam is exec/bubbles.js's existing `gg-bubble-pop` document event, read
   * by exec/executor.js and forwarded to videos.spawnLocal(). Renderers still do
   * not import each other; the fan-out is where two of them meet.
   * ======================================================================== */

  // ------------------------- video bubbles: the kind table + the mint exclusion
  {
    const bub = await import('../exec/bubbles.js');
    const EX = await import('../exec/executor.js');

    const vk = bub.KINDS.find((k) => k.id === 'video');
    ok(!!vk, 'exec/bubbles.js declares a `video` bubble kind');
    ok(!!vk && vk.w === 5, 'weighted 5 — a window is heavy, so it is the rare one', String(vk && vk.w));
    const effects = bub.KINDS.filter((k) => k.id !== 'normal');
    ok(effects.length === 6, 'there are six effect kinds now', effects.map((k) => k.id).join(','));
    ok(effects.every((k) => k.w >= vk.w),
      'and `video` is the RAREST of them (rarer than flash and glitch, both 9/7)',
      effects.map((k) => `${k.id}:${k.w}`).join(' '));

    // It is an ordinary effect bubble to the economy — 2.5, exactly like the
    // other five. The window is the flourish; the drop is the payment.
    ok(bub.POP_WORTH_EFFECT === 2.5, 'an effect pop is worth 2.5', String(bub.POP_WORTH_EFFECT));
    ok(bub.popWorthOf('video', false) === bub.POP_WORTH_EFFECT,
      'and a video bubble is worth exactly that', String(bub.popWorthOf('video', false)));
    ok(bub.popWorthOf('video', true) === bub.POP_WORTH_PAYLOAD,
      'while one THEY minted is worth nothing, like all their clutter');

    // ---- RULE 2, at the source: the swarm pool has no `video` in it at all
    ok(Array.isArray(bub.PAYLOAD_MINT_EXCLUDED) && bub.PAYLOAD_MINT_EXCLUDED.includes('video'),
      'the mint exclusion names `video`', JSON.stringify(bub.PAYLOAD_MINT_EXCLUDED));
    ok(bub.kindPool(false).some((k) => k.id === 'video'), 'our OWN field draws from a pool that has it');
    ok(bub.kindPool(true).every((k) => k.id !== 'video'),
      'an inbound swarm draws from one that does NOT',
      bub.kindPool(true).map((k) => k.id).join(','));
    ok(bub.kindPool(true).length === bub.kindPool(false).length - 1,
      'and the two pools differ by exactly that one kind',
      `${bub.kindPool(false).length} vs ${bub.kindPool(true).length}`);

    // …and swept, because a weighted table is exactly the kind of thing that
    // silently starts including one more entry than it meant to.
    let seed = 987654321;
    const rnd = () => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return seed / 0x7fffffff; };
    let swarmVideo = 0, fieldVideo = 0, swarmDraws = 0;
    for (let i = 0; i < 4000; i++) {
      const s = bub.pickKind(true, rnd);
      swarmDraws++;
      if (s === 'video') swarmVideo++;
      if (bub.pickKind(false, rnd) === 'video') fieldVideo++;
    }
    ok(swarmDraws === 4000 && swarmVideo === 0,
      '4000 swarm draws mint ZERO video bubbles', String(swarmVideo));
    ok(fieldVideo > 0, 'while our own field really does mint them', String(fieldVideo));
    ok(fieldVideo < 4000 * 0.10,
      'and rarely — well under a tenth of the field', (fieldVideo / 4000).toFixed(4));
    // the pure picker only ever answers with kinds that exist
    const ids = new Set(bub.KINDS.map((k) => k.id));
    let strays = 0;
    for (let i = 0; i < 500; i++) if (!ids.has(bub.pickKind(i % 2 === 0, rnd))) strays++;
    ok(strays === 0, 'and never invents a kind that has no sprite', String(strays));

    // ---- the executor-side dials this seam runs on
    ok(EX.VIDEO_BUBBLE_KIND === 'video', 'the executor forwards exactly the `video` kind', EX.VIDEO_BUBBLE_KIND);
    ok(EX.BUBBLE_WINDOW_MS > 0 && EX.BUBBLE_WINDOW_MS <= 60000,
      'an earned window floats for a payload-legal span', String(EX.BUBBLE_WINDOW_MS));
    ok(EX.BUBBLE_WINDOW_INTENSITY >= 0 && EX.BUBBLE_WINDOW_INTENSITY <= 1,
      'at a normal intensity', String(EX.BUBBLE_WINDOW_INTENSITY));
  }

  // ---------------------------- video bubbles: the fx.css contract of the sprite
  {
    const fs = await import('node:fs/promises');
    const url = await import('node:url');
    const raw = await fs.readFile(url.fileURLToPath(new URL('../exec/fx.css', import.meta.url)), 'utf8');
    const css = raw.replace(/\/\*[\s\S]*?\*\//g, '');
    const m = /\.gg-bubble--video\s*\{([^}]*)\}/.exec(css);
    ok(!!m, 'fx.css carries a .gg-bubble--video rule');
    const rule = m ? m[1] : '';
    // 2026-08-04, owner correction: it wore the DtRH prism for want of anything
    // better on disk, on the theory that an iridescent swirl reads as a screen.
    // It does not. It now wears goon's OWN sprite — a bubble with a big neon-red
    // play arrow — which says what popping it does.
    ok(/background-image:\s*url\('\.\.\/assets\/bubbles\/video\.png'\)/.test(rule),
      'the video bubble wears goon\'s own play-button sprite', rule.replace(/\s+/g, ' ').trim());
    ok(!/prism\.png/.test(rule), 'and the prism is gone from it for good', rule.replace(/\s+/g, ' ').trim());
    // THE PATH SHAPE IS LOAD-BEARING, and it is the opposite of its five
    // neighbours. DtRH art is absolute (`/dtrh/...`) because that prefix is
    // mounted identically everywhere; GOON'S OWN art must be RELATIVE, because
    // this page is served at /goon/ by the app and at the ROOT by every headless
    // harness and the solo dev server — an absolute `/goon/assets/...` 404s in
    // half of them, and a 404 sprite is an invisible bubble you cannot pop.
    ok(!/url\(['"]?\/goon\//.test(rule),
      'by a RELATIVE url — this page has no fixed prefix, so an absolute one would 404 in the harnesses',
      rule.replace(/\s+/g, ' ').trim());
    const f = ((/filter:\s*([^;]+)/.exec(rule) || [])[1] || '').trim();
    ok(f.indexOf('drop-shadow(') === 0 && f.lastIndexOf('drop-shadow(') === 0 && /\)$/.test(f),
      'its filter is a LONE drop-shadow — the recolour chains stay dead (see the kind-sprite banner)', f);
    const COLOUR_FNS = ['brightness(', 'sepia(', 'saturate(', 'hue-rotate(', 'contrast(', 'grayscale(', 'invert(', 'opacity('];
    ok(COLOUR_FNS.every((fn) => !f.includes(fn)),
      'no colour-space filter function anywhere in it', COLOUR_FNS.filter((fn) => f.includes(fn)).join(' '));
    ok(f === 'drop-shadow(0 0 11px rgba(255, 90, 130, 0.8))',
      'the halo is tuned to the art it now wears: neon red, like the arrow on it', f);
    ok(!/(^|[^-])animation:/.test(rule),
      'and the rule does not redeclare the animation shorthand (that would cancel the sway)', rule);
    // six kinds, six DIFFERENT halos — the halo is what names the kind
    const halos = ['flash', 'spiral', 'glitch', 'braindrain', 'pinkfilter', 'video'].map((k) => {
      const b = (new RegExp('\\.gg-bubble--' + k + '\\s*\\{([^}]*)\\}').exec(css) || [])[1] || '';
      return ((/filter:\s*([^;]+)/.exec(b) || [])[1] || '').trim();
    });
    ok(halos.every(Boolean) && new Set(halos).size === 6,
      'all six kinds are told apart by their own halo colour', String(new Set(halos).size));
    // …and the one sprite that is OURS is really on disk, at the path the
    // relative url resolves to from /exec/fx.css. A CSS url() that misses is a
    // silent nothing: the bubble still spawns, still pops, and shows no art.
    const art = url.fileURLToPath(new URL('../assets/bubbles/video.png', import.meta.url));
    let bytes = 0;
    try { bytes = (await fs.stat(art)).size; } catch (_e) { bytes = 0; }
    ok(bytes > 0, 'and ../assets/bubbles/video.png exists where fx.css points at it', `${bytes} bytes`);
  }

  /* ---------------- video bubbles: the live pop -> window path, end to end
   * Math.random is pinned at 0.99 for this block, which is the top of every
   * weighted draw and therefore ALWAYS the last (rarest) kind in the field pool:
   * `video`. The same 0.99 through the SWARM pool lands on pinkfilter, which is
   * the exclusion proving itself the hard way. Nothing else in the spawn path
   * cares what the number is (size/rise/sway/placement are all ranges). */
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);
    const bubHost = byId.get('gg-fx-bubbles');
    const winHost = byId.get('gg-fx-vwin');
    const stage = byId.get('gg-stage');
    const videos = ex.rendererFor(GoonElement.Videos);
    const pops = [];
    const onPop = (e) => pops.push(e.detail);
    document.addEventListener('gg-bubble-pop', onPop);
    const prisms = () => bubHost.findAll('gg-bubble')
      .filter((b) => b._cls.has('gg-bubble--video') && !b._cls.has('is-pop'));

    const realRandom = Math.random;
    Math.random = () => 0.99;
    try {
      m.emitStart({ element: GoonElement.Bubbles, intensity: 1, durationMs: 0, elapsedMs: 0 });
      await sleep(700);
      ok(prisms().length > 0, 'the field really mints video bubbles', String(prisms().length));
      ok(prisms()[0].getAttribute('data-gg-mint') === 'field',
        'and they are OURS — a swarm can never mint one', prisms()[0].getAttribute('data-gg-mint'));
      ok(videos.windowCount() === 0, 'no window before anybody pops one', String(videos.windowCount()));

      // ---- THE POP
      prisms()[0].fire('pointerdown');
      const last = pops[pops.length - 1];
      ok(pops.length === 1 && last.kind === 'video', 'popping it dispatches one gg-bubble-pop', JSON.stringify(last));
      ok(last.worth === 2.5, 'worth 2.5 — an effect bubble like any other', String(last.worth));
      ok(last.payload === false, 'and flagged as ours, so ui/drops.js pays for it');
      ok(videos.windowCount() === 1, 'and ONE floating window went up', String(videos.windowCount()));
      ok(m.receipts.length === 0,
        'with NO receipt: a local window has no payload id and earns no charge', JSON.stringify(m.receipts));
      ok(ex.activeCount() === 1,
        'and no registry entry either — activeCount still counts only the running element', String(ex.activeCount()));
      ok(stage.childNodes.length === 0,
        'nothing on #gg-stage (a husk there is a full-screen click shield)', String(stage.childNodes.length));

      // ---- and it is the FULL window, not a stripped-down cousin
      const w0 = liveWins(winHost)[0];
      const drift = w0.findAll('gg-vwin-drift')[0];
      const inner = w0.findAll('gg-vwin-inner')[0];
      const dot = w0.findAll('gg-vwin-dot')[0];
      const vid = w0.findAll('gg-vwin-vid')[0];
      ok(!!drift && !!inner && !!dot && !!vid,
        'it is the same wrapper > drift > inner > (video + dot) a payload builds');
      ok(/gg-vwin-in--[1-4]/.test(inner.className), 'with one of the four glitch-in variants', inner.className);
      ok(/px$/.test(w0.style.getPropertyValue('--gg-vwin-w')), 'sized through --gg-vwin-w',
        w0.style.getPropertyValue('--gg-vwin-w'));
      ok(parseFloat(drift.style.getPropertyValue('--gg-vwin-dy')) < 0, 'drifting up, away from mercy',
        drift.style.getPropertyValue('--gg-vwin-dy'));
      ok(String(vid.src).includes('ccp.assets'),
        'and the clip is drawn from the PLAYER OWN library through media.acquire', String(vid.src));
      ok(vid.muted === false, 'being the newest window, it speaks');
      const localNode = w0;

      // ---- RULE 3: fill the pool with THROWN windows, then pop another prism
      for (let i = 0; i < 3; i++) {
        m.emitPayload({
          payload: payloadOf({ id: 'pVB' + i, kind: GoonPayloadKind.Video, duration_ms: 40000 }),
          fireAtLocalMs: localMonotonicMs(),
        });
        await sleep(30);
      }
      await sleep(60);
      ok(videos.windowCount() === 4, 'the pool is full: 1 earned + 3 thrown', String(videos.windowCount()));
      ok(m.receipts.length === 0, 'and none of the thrown ones has finished', JSON.stringify(m.receipts));

      await sleep(900);                       // let the top-up mint more prisms
      const spare = prisms()[0];
      ok(!!spare, 'another prism to pop with the pool already full');
      const before = pops.length;
      spare.fire('pointerdown');
      ok(pops.length === before + 1, 'it still POPS', `${before} -> ${pops.length}`);
      ok(pops[pops.length - 1].worth === 2.5,
        'and still pays its 2.5 — a full pool costs the player nothing',
        String(pops[pops.length - 1].worth));
      ok(videos.windowCount() === 4,
        'but NO fifth window: a self-pop never displaces one', String(videos.windowCount()));
      ok(m.receipts.length === 0,
        'and above all it never receipted somebody else\'s window as survived', JSON.stringify(m.receipts));

      // ---- ONE POOL, and a PAYLOAD is the only thing that may evict from it —
      // including a local window, which just dies quietly (no id, no receipt).
      m.emitPayload({
        payload: payloadOf({ id: 'pVB3', kind: GoonPayloadKind.Video, duration_ms: 40000 }),
        fireAtLocalMs: localMonotonicMs(),
      });
      await sleep(120);
      ok(videos.windowCount() === 4, 'an arriving payload still evicts to stay at four',
        String(videos.windowCount()));
      ok(m.receipts.length === 0,
        'evicting the LOCAL window posts no receipt — it was never on the wire', JSON.stringify(m.receipts));
      await sleep(GHOST_WAIT);
      ok(!localNode.isConnected,
        'and it WAS the local one: the pool is one FIFO, oldest first, local or thrown');

      ex.stopAll();
      ok(videos.windowCount() === 0, 'stopAll sweeps earned and thrown windows alike',
        String(videos.windowCount()));
      ok(m.receipts.length === 4 && m.receipts.every((r) => r.endured === false),
        'the four THROWN ones receipt completed, and there is no fifth receipt for the earned one',
        JSON.stringify(m.receipts));

      // ---- the seam itself, driven by hand: kind gates it, `payload` blocks it
      const bang = (detail) => document.dispatchEvent(new CustomEvent('gg-bubble-pop', { detail, bubbles: true }));
      bang({ kind: 'flash', worth: 2.5, payload: false, size: 100, x: 0, y: 0 });
      ok(videos.windowCount() === 0, 'a flash pop opens no window — only `video` does',
        String(videos.windowCount()));
      bang({ kind: 'video', worth: 0, payload: true, size: 100, x: 0, y: 0 });
      ok(videos.windowCount() === 0,
        'and a swarm-minted video pop is refused at the seam too (the second lock on the mint rule)',
        String(videos.windowCount()));
      bang({ kind: 'video', worth: 2.5, payload: false, size: 100, x: 0, y: 0 });
      ok(videos.windowCount() === 1, 'ours opens exactly one', String(videos.windowCount()));
      ok(m.receipts.length === 4, 'and still not one receipt from the local path',
        String(m.receipts.length));

      // ---- an earned window is dismissed the same way a thrown one is
      const w2 = liveWins(winHost)[0];
      PID++;
      ptr(w2, 'pointerdown', 300, 300);
      ptr(w2, 'pointerup', 300, 300);
      ok(videos.windowCount() === 0, 'a click dismisses it, exactly like a thrown one',
        String(videos.windowCount()));
      ok(m.receipts.length === 4, 'silently — closing your own window is not a survival',
        String(m.receipts.length));

      // ---- and after detach nothing can conjure one onto the recap
      document.removeEventListener('gg-bubble-pop', onPop);
      ex.detach();
      bang({ kind: 'video', worth: 2.5, payload: false, size: 100, x: 0, y: 0 });
      ok(videos.windowCount() === 0, 'a pop after detach opens nothing at all',
        String(videos.windowCount()));
    } finally {
      Math.random = realRandom;
      document.removeEventListener('gg-bubble-pop', onPop);
    }
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
    ok(String(veils[0].style.getPropertyValue('--gg-drain-img')).includes('ccp.assets'),
      'the veil washes an image from the player own pool', veils[0].style.getPropertyValue('--gg-drain-img'));
    // …through the custom property ONLY: an inline background-image would drop
    // the dim + desaturate layers fx.css composes under the wash (the B&W fix).
    ok(!String(veils[0].style.getPropertyValue('background-image')).trim(),
      'and never by writing background-image over the layer stack',
      veils[0].style.getPropertyValue('background-image'));
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

  /* ------------- videos: the window COUNT leaves the renderer (2026-08-04)
   *
   * The opponent's monitor draws how many floating video windows you have up, which means the
   * pool is no longer private: every mutation has to publish wins.length upward, through
   * exec/executor.js, into the match's next tick as `vwin`.
   *
   * What is pinned here is the SEAM, not the wire (core/wire.js and the match's half live in
   * selftest-net / selftest-core):
   *   1. one edge per change, and never a stuck number — an eviction is a fall AND a rise;
   *   2. every door into the pool counts, including the self-popped window the opponent can
   *      never see any other way;
   *   3. stopAll ends at ZERO, including for local windows that no cancel fn reaches — the bug
   *      this seam would otherwise have shipped: a phantom stack on their screen for a match the
   *      player had already left;
   *   4. a listener is never load-bearing: no sink, or a throwing sink, and the windows behave
   *      exactly as they always did.
   */
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();

    const seen = [];
    const m = Object.assign(fakeMatch(), {
      setLocalWindowCount(count) { seen.push(count); return true; },
    });
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const videos = ex.rendererFor(GoonElement.Videos);
    const winHost = byId.get('gg-fx-vwin');
    const last = () => (seen.length ? seen[seen.length - 1] : null);
    const throwPayload = async (id) => {
      m.emitPayload({
        payload: payloadOf({ id, kind: GoonPayloadKind.Video, duration_ms: 40000 }),
        fireAtLocalMs: localMonotonicMs(),
      });
      await sleep(40);
    };

    ex.attach(m);
    ok(seen.length === 0, 'attaching publishes nothing — no windows, no news', JSON.stringify(seen));

    for (let i = 0; i < 4; i++) await throwPayload('vwc' + i);
    ok(videos.windowCount() === 4, 'four thrown windows are up', String(videos.windowCount()));
    ok(seen.join(',') === '1,2,3,4', 'one edge per window as the pool fills', seen.join(','));

    // The fifth evicts the oldest, and that is TWO changes: the pool cannot silently stay at 4
    // or a monitor driven off the edges would miss the swap entirely.
    await throwPayload('vwc4');
    ok(videos.windowCount() === 4, 'still four after the fifth arrives', String(videos.windowCount()));
    ok(seen.slice(4).join(',') === '3,4', 'an eviction publishes the fall AND the rise', seen.slice(4).join(','));

    // A click dismisses one — the player's own way out of a window. (The evicted window's node
    // hangs around as a GHOST while it plays out its exit, so skip the husks: clicking one would
    // be clicking a record that left the pool two paragraphs ago.)
    const w0 = liveWins(winHost)[0];
    PID++;
    ptr(w0, 'pointerdown', 300, 300);
    ptr(w0, 'pointerup', 300, 300);
    ok(videos.windowCount() === 3, 'the click took one down', String(videos.windowCount()));
    ok(last() === 3, 'and the count fell with it', String(last()));

    // …and the window the player EARNED counts exactly the same. This is the whole reason the
    // field exists: nothing about a self-pop reaches the opponent any other way.
    ok(videos.spawnLocal({ duration_ms: 30000, intensity: 0.5 }) === true, 'a self-popped window goes up');
    ok(last() === 4, 'and is counted like any other', String(last()));

    // A fizzled self-pop changes nothing, so it publishes nothing.
    const beforeFizzle = seen.length;
    ok(videos.spawnLocal({ duration_ms: 30000 }) === false, 'the pool is full, so the next pop fizzles');
    ok(seen.length === beforeFizzle, 'and a fizzle is not a change', String(seen.length - beforeFizzle));

    // THE SWEEP. Three of these are thrown (cancel fns reach them) and one is earned (nothing
    // does) — before stopWindows() that last record survived layers.stopAll() forever.
    ex.stopAll();
    ok(videos.windowCount() === 0, 'stopAll leaves no window standing, earned or thrown',
      String(videos.windowCount()));
    ok(last() === 0, 'and the count published is ZERO', String(last()));
    ok(winHost.childNodes.length === 0, 'with the layer emptied behind it', String(winHost.childNodes.length));
    // Five payloads were thrown and five receipts came back — one per ID and NOT ONE MORE. The
    // earned window is the sixth thing that lived in this pool and it is silent, exactly as it
    // was before the count seam existed: it has no id to close and earns no charge.
    ok(m.receipts.length === 5, 'five thrown windows, five receipts', JSON.stringify(m.receipts));
    ok(m.receipts.filter((r) => r.endured).length === 2,
      'the evicted one and the dismissed one were ENDURED', JSON.stringify(m.receipts));
    ok(m.receipts.slice(2).every((r) => r.endured === false),
      'and the three the sweep took receipt completed', JSON.stringify(m.receipts));

    const settled = seen.length;
    ex.stopAll();
    ok(seen.length === settled, 'a second stopAll publishes nothing — edges only', String(seen.length - settled));

    // Detach tears down through the same path, so a window can never outlive the match.
    await throwPayload('vwc5');
    ok(videos.windowCount() === 1 && last() === 1, 'one more window for the road', String(last()));
    ex.detach();
    ok(videos.windowCount() === 0 && last() === 0,
      'detach (stopAll first, while the match is still attached) ends at zero', String(last()));

    // A pop after detach reaches nobody, so nothing is published to a match that is gone.
    const afterDetach = seen.length;
    document.dispatchEvent(new CustomEvent('gg-bubble-pop', {
      detail: { kind: 'video', worth: 2.5, payload: false, size: 100, x: 0, y: 0 }, bubbles: true,
    }));
    ok(seen.length === afterDetach, 'and a pop after detach publishes nothing at all',
      String(seen.length - afterDetach));
  }

  /* ------------- …and the callback is never load-bearing --------------------- */
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const V = await import('../exec/videos.js');

    // No sink at all: the renderer is exactly what it was before the seam existed.
    const plain = V.createVideos({ layers, media: fakeMedia(), logger: quiet });
    plain.renderPayload({ id: 'vwp0', duration_ms: 40000, intensity: 0.5 }, () => {});
    ok(plain.windowCount() === 1, 'a renderer with no listener still opens windows', String(plain.windowCount()));
    ok(plain.stopWindows() === 1, 'stopWindows reports what it swept', String(plain.windowCount()));
    ok(plain.windowCount() === 0, 'and sweeps it');
    ok(plain.stopWindows() === 0, 'sweeping an empty pool is a no-op');

    // A sink that throws is the listener's problem and nobody else's.
    let calls = 0;
    let endured = null;
    const boom = V.createVideos({
      layers,
      media: fakeMedia(),
      logger: quiet,
      onWindowCountChanged() { calls++; throw new Error('a listener exploded'); },
    });
    boom.renderPayload({ id: 'vwp1', duration_ms: 40000, intensity: 0.5 }, (e) => { endured = e; });
    ok(boom.windowCount() === 1, 'a throwing listener does not stop the window going up', String(boom.windowCount()));
    ok(calls === 1, 'it was called exactly once', String(calls));
    boom.stopWindows();
    ok(boom.windowCount() === 0 && endured === false,
      'and the window still settles, and still receipts', `${boom.windowCount()} / ${endured}`);
    ok(calls === 2, 'both edges were offered to it', String(calls));
  }

  console.log(failures === 0 ? `PASS — ${n} checks` : `FAILED — ${failures}/${n} checks`);
  process.exit(failures === 0 ? 0 : 1);
}

main().catch((e) => { console.error(e); process.exit(1); });
