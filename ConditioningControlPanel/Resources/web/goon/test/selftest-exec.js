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
const spiralGen = await import('../exec/spiralGen.js');
const spiralField = await import('../exec/spiralField.js');
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

/* THE ✕, AND THE OPTION THAT MAKES IT REAL (2026-08-04, owner-specced). A window
 * used to be dismissed with a click; a click MUTES one now, and the only way to
 * end one early is the ✕ — which exists only while the player has "skippable
 * videos" switched on. The flag reaches exec/ as a root attribute (ui/prefs.js
 * writes it; exec/ never imports ui/), so switching it here is switching it for
 * real, and every block that wants to close a window has to say so. */
const skipOn = (on) => documentElement.setAttribute('data-gg-vskip', on ? 'on' : 'off');
const closeBtnOf = (win) => (win ? win.findAll('gg-vwin-x')[0] : null);
const clickClose = (win) => { const b = closeBtnOf(win); if (b) b.fire('click'); return !!b; };

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

    // NOTHING BUNDLED. The pool of six DtRH sp*.gif files this bed used to draw
    // from is gone from goon (the FILES stay on disk — DtRH owns them and still
    // draws them). Nothing under exec/ may name that directory again.
    // COMMENTS ARE STRIPPED FIRST, the way the fx.css pins below do it: the
    // banners in both modules quote the retired path verbatim (that is the
    // history worth keeping), and a regression pin a comment can turn red is
    // not a pin.
    {
      const fs = await import('node:fs/promises');
      const url = await import('node:url');
      const read = async (rel) => (await fs.readFile(url.fileURLToPath(new URL(rel, import.meta.url)), 'utf8'))
        .replace(/\/\*[\s\S]*?\*\//g, '')
        .replace(/^\s*\/\/.*$/gm, '');
      for (const name of ['spiral.js', 'bubbles.js']) {
        const src = await read(`../exec/${name}`);
        ok(!/\/dtrh\/assets\/bubbles\/effects\/spiral/.test(src),
          `exec/${name} names no bundled spiral asset — every spiral is generated`,
          (/[^\n]*\/dtrh\/assets\/bubbles\/effects\/spiral[^\n]*/.exec(src) || [''])[0]);
      }
      const fxSrc = await read('../exec/fx.css');
      ok(!/\.gg-spiral\s*\{[^}]*url\(/.test(fxSrc),
        'fx.css .gg-spiral declares NO background image — the renderer writes a baked one',
        (/\.gg-spiral\s*\{[^}]*\}/.exec(fxSrc) || [''])[0]);
      // The spin rate/direction MUST ride custom properties: the woven path
      // cancels this keyframe with an inline `animation` shorthand and clears it
      // with '' again, and a shorthand clear eats every longhand it owns.
      ok(/animation:\s*ggSpiralSpin\s+var\(--gg-spiral-spin[^)]*\)[^;]*var\(--gg-spiral-dir/.test(fxSrc),
        '.gg-spiral takes its spin rate AND direction from custom properties',
        (/animation:\s*ggSpiralSpin[^;]*/.exec(fxSrc) || [''])[0]);
    }
  }

  /* ------------------------------------------------- the spiral generator
   * exec/spiralGen.js — the Loom's 🎲 roll, ported. Driven here through its
   * INJECTABLE rng, which is the only reason a random content surface is
   * testable at all: app code passes nothing and gets Math.random.
   * ------------------------------------------------------------------- */
  {
    /** A seeded rng (mulberry32) — same seed, same spiral, every run. */
    const seeded = (seed) => () => {
      seed = (seed + 0x6D2B79F5) | 0;
      let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };

    // --- deterministic under an injected rng
    const a = spiralGen.rollSpiral(seeded(1234));
    const b = spiralGen.rollSpiral(seeded(1234));
    ok(JSON.stringify(a.params) === JSON.stringify(b.params),
      'the same seed rolls the same spiral — the generator is rng-injectable, not clock-driven');
    ok(JSON.stringify(spiralGen.rollSpiral(seeded(99)).params) !== JSON.stringify(a.params),
      'and a different seed rolls a different one');

    // --- the whole space, swept. 4000 rolls off the real rng.
    let revOut = 0;
    let symBreak = 0;
    let dutyOut = 0;
    let offPalette = 0;
    let centrepiece = 0;
    let hue = 0;
    const stylesSeen = new Set();
    const armsSeen = new Set();
    let mostRecent = null;
    for (let i = 0; i < 4000; i++) {
      const roll = spiralGen.rollSpiral();
      const L = roll.params.layer;
      mostRecent = roll;
      stylesSeen.add(L.style);
      armsSeen.add(L.arms);
      if (roll.revSec < spiralGen.REV_SEC_MIN - 1e-6 || roll.revSec > spiralGen.REV_SEC_MAX + 1e-6) revOut++;
      // The symmetry rule: a multi-thread layer whose arm count is not a
      // multiple of its thread count has no sub-2PI symmetry and spins
      // arms/threads times too fast. See armChoices in exec/spiralGen.js.
      if (L.arms % L.colors.length !== 0) symBreak++;
      if (L.duty < 0.23 || L.duty > 0.37) dutyOut++;
      for (const c of L.colors) if (!spiralGen.SPIRAL_PALETTE.includes(c)) offPalette++;
      // THE OWNER'S EXPLICIT EXCLUSION: the Loom can stamp a dot/star/cross/x
      // over the middle at 25%. Goon must never. It is excluded by CONSTRUCTION
      // (the schema has no field) rather than filtered, so this check is a pin
      // on the schema, not on a branch.
      if ('centerpiece' in roll.params || 'centrepiece' in roll.params) centrepiece++;
      if (roll.params.hueCycles !== 0) hue++;
    }
    ok(revOut === 0,
      `every roll is solved into the ${spiralGen.REV_SEC_MIN}-${spiralGen.REV_SEC_MAX}s revolution band`,
      String(revOut));
    ok(symBreak === 0, 'no roll breaks the arms % threads === 0 symmetry rule', String(symBreak));
    ok(dutyOut === 0, 'duty stays in the BED band (~0.24-0.36), not the Loom‘s 0.3-0.7', String(dutyOut));
    ok(offPalette === 0, 'every thread is one of goon‘s five swatches', String(offPalette));
    ok(centrepiece === 0, 'NO roll can carry a centerpiece — the dot/cross overlays are excluded', String(centrepiece));
    ok(hue === 0, 'and hueCycles stays pinned off (a hue sweep on a screen blend is a strobe)', String(hue));
    ok(stylesSeen.size === 6, '4000 rolls reach all six weave styles', Array.from(stylesSeen).join(','));
    ok(armsSeen.size >= 5, 'and a spread of arm counts', Array.from(armsSeen).sort((x, y) => x - y).join(','));

    // --- the solved revolution really is the field's own number
    ok(Math.abs(spiralField.revolutionSecFor(mostRecent.params) - mostRecent.revSec) < 1e-9,
      'the revSec a roll reports is the FIELD‘s formula, not the generator‘s opinion',
      `${spiralField.revolutionSecFor(mostRecent.params)} vs ${mostRecent.revSec}`);

    // --- junk rng: a host that hands back garbage must not poison a roll
    const junk = spiralGen.rollSpiral(() => NaN);
    ok(junk.params.layer.arms >= 1 && junk.revSec > 0,
      'an rng that returns NaN still produces a valid spiral', JSON.stringify(junk.params.layer));

    // --- the 2D raster bake, on a recording fake context
    const calls = { fillRect: 0, arc: 0, fill: 0, gradients: 0 };
    const fakeCtx = {
      fillStyle: null,
      fillRect() { calls.fillRect++; },
      beginPath() {}, closePath() {}, lineTo() {},
      arc() { calls.arc++; },
      fill() { calls.fill++; },
      createRadialGradient() { calls.gradients++; return { addColorStop() {} }; },
    };
    const rolled = spiralGen.rollSpiral(seeded(7));
    ok(spiralGen.drawSpiralRaster(fakeCtx, 512, rolled.params, 0) === true,
      'drawSpiralRaster draws the still the no-WebGL bed shows');
    ok(calls.fillRect === 1, 'exactly one background fill', String(calls.fillRect));
    ok(calls.fill > 100 && calls.arc > 100, 'and the arms are annular wedge strips, not one path',
      `${calls.fill} fills / ${calls.arc} arcs`);
    ok(spiralGen.drawSpiralRaster({}, 512, rolled.params) === false,
      'a context that cannot draw is refused rather than thrown over');
    // The stub DOM has no canvas 2D context at all, which is exactly the host
    // that must get '' instead of a broken `url('')`.
    ok(spiralGen.bakeSpiralImage(rolled.params, 64) === '',
      'a host with no 2D canvas bakes to the empty string, not to junk',
      spiralGen.bakeSpiralImage(rolled.params, 64));

    // --- the session pool: pre-generated, and rotated rather than re-rolled
    const sess = spiralGen.createSpiralSession({ rng: seeded(42) });
    ok(sess.count === spiralGen.SESSION_SIZE, `a session pre-generates ${spiralGen.SESSION_SIZE} variants`,
      String(sess.count));
    ok(sess.all().length === spiralGen.SESSION_SIZE && sess.all() !== sess.all(),
      'all() hands back a copy, so a caller cannot edit the session‘s pool');
    const identities = new Set(sess.all().map((v) => JSON.stringify(v.params)));
    ok(identities.size >= 4, 'and the five it rolled are (near enough) all different',
      String(identities.size));
    const seen = new Map();
    let repeats = 0;
    let prev = null;
    for (let i = 0; i < 200; i++) {
      const v = sess.next();
      if (v === prev) repeats++;
      prev = v;
      seen.set(v, (seen.get(v) || 0) + 1);
    }
    ok(repeats === 0, 'next() never hands out the same variant twice running', String(repeats));
    ok(seen.size === spiralGen.SESSION_SIZE, '200 picks rotate through every variant', String(seen.size));
    const counts = Array.from(seen.values());
    ok(Math.max(...counts) - Math.min(...counts) <= 1,
      'and the rotation is even — a shuffled walk, not a re-roll', counts.join(','));
    ok(sess.all().every((v) => v.image() === '' && v.image() === v.image()),
      'image() caches (here: caches the empty string this host can only bake)');

    // --- the shared session, which is what makes a popped spiral bubble throw a
    //     spiral THIS MATCH has been showing rather than a seventh one
    const shared = spiralGen.beginSpiralSession({ rng: seeded(5) });
    ok(spiralGen.spiralSession() === shared, 'beginSpiralSession installs the shared pool');
    const fromShared = new Set(shared.all());
    let strays = 0;
    for (let i = 0; i < 50; i++) if (!fromShared.has(spiralGen.nextSpiral())) strays++;
    ok(strays === 0, 'nextSpiral() only ever draws from the shared session', String(strays));
    ok(spiralGen.pickSpiralImage() === '',
      'and pickSpiralImage() (exec/bubbles.js‘s flick) goes through the same pool');
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
    // NO background-image on this host: the stub has no 2D canvas, so the bake
    // returns '' and the renderer must leave the property alone rather than
    // write `url('')` and blank a bed that was working. What it DOES write, on
    // every host, is the roll's own spin — which is the pin that a variant
    // actually reached the pane.
    ok(!String(panes[0].style.getPropertyValue('background-image')).trim(),
      'a host that cannot bake a still gets no background-image, not url(\'\')',
      panes[0].style.getPropertyValue('background-image'));
    const spin = parseFloat(panes[0].style.getPropertyValue('--gg-spiral-spin'));
    ok(spin >= 10 && spin <= 18, 'the pane spins at the generated variant‘s own 10-18s revolution',
      panes[0].style.getPropertyValue('--gg-spiral-spin'));
    ok(/^(normal|reverse)$/.test(panes[0].style.getPropertyValue('--gg-spiral-dir')),
      'and turns the way that variant was rolled to turn',
      panes[0].style.getPropertyValue('--gg-spiral-dir'));
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
    // exactly one blur-behind (+ its -webkit- twin), and the count is pinned.
    // Counted by VALUE since the lite tier (2026-08-05): `backdrop-filter: none`
    // is the phone diet switching this very pane OFF, not a second pane, so the
    // guard splits the declarations into "the blur" and "the offs" and pins both
    // sides — anything that is neither is a new pane and still fails.
    const bdfAll = css.match(/backdrop-filter:[^;}]*/g) || [];
    const bdfBlur = bdfAll.filter((d) => /blur\(/.test(d)).length;
    const bdfNone = bdfAll.filter((d) => /:\s*none/.test(d)).length;
    ok(bdfBlur === 2, 'fx.css still declares ONE blur-behind (+ its -webkit- twin)', bdfAll.join(' | '));
    ok(bdfBlur + bdfNone === bdfAll.length,
      'and every other backdrop-filter is a `none` — the lite tier switching the same pane off, never a second pane',
      bdfAll.join(' | '));
    ok(/\.gg-drain\s*\{[^}]*backdrop-filter:\s*blur\(7px\)/.test(css),
      'and the one that exists is the drain blur', String(bdfBlur));
    ok(/html\[data-gg-perf="lite"\]\s*\.gg-drain\s*\{[^}]*backdrop-filter:\s*none/.test(css),
      'the lite tier drops the drain blur-behind outright — the most expensive declaration on the page does not run on phones');
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

  /* ---------------------------- flashes: PINCH, the mobile half of that wheel
   * Same gesture as the video window's, same helper, one difference that matters:
   * a flash is anchored on its CENTRE and sized in vmin, so the clamp has to do
   * its arithmetic in a unit the module does not think in. The cap bites here at
   * a plain 1280x720 (3x of 43.7vmin is taller than the viewport), which is the
   * case the video block cannot cover.
   * ------------------------------------------------------------------------ */
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const host = byId.get('gg-fx-flash');
    const P = await import('../exec/pinch.js');
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);
    const shotsOf = () => host.findAll('gg-flash').filter((el) => !el._cls.has('is-popped'));
    const sizeOf = (el) => parseFloat(el.style.getPropertyValue('--gg-flash-size')) || 43.7;
    const fin = (el, type, id, x, y) => ptr(el, type, x, y, { pointerId: id, pointerType: 'touch' });

    m.emitStart({ element: GoonElement.Flashes, intensity: 1, durationMs: 0, elapsedMs: 0 });
    await sleep(120);
    m.emitStop({ element: GoonElement.Flashes, intensity: 0, durationMs: 0, elapsedMs: 0 });
    await sleep(320);
    const f = shotsOf()[0];
    ok(!!f, 'a flash to pinch');
    const bornSize = sizeOf(f);
    // Its CENTRE in px: the vw anchor (which never moves) plus the drag offset.
    const anchorX = parseFloat(f.style.getPropertyValue('left')) / 100 * 1280;
    const centreX = () => anchorX + (dxOf(f) || 0);

    // Park the centre at a known x with ONE finger, exactly as the video block does.
    const A = ++PID, B = ++PID;
    fin(f, 'pointerdown', A, 400, 400);
    fin(f, 'pointermove', A, 700, 400);                  // past the slop: a plain grab
    ok(f._cls.has('gg-flash--grabbed'), 'one finger still just drags it', f.className);
    const ax = 400 + (400 - anchorX);
    fin(f, 'pointermove', A, ax, 400);
    ok(Math.abs(centreX() - 400) < 0.5, 'parked at a known place', String(centreX()));

    fin(f, 'pointerdown', B, ax + 100, 400);
    ok(Math.abs(sizeOf(f) - bornSize) < 0.06, 'a second finger landing resizes nothing on its own',
      String(sizeOf(f)));
    fin(f, 'pointermove', B, ax + 104, 400);             // inside PINCH_SLOP_PX
    fin(f, 'pointermove', B, ax + 100, 400);
    ok(Math.abs(sizeOf(f) - bornSize) < 0.06, 'and a wobble inside the slop is not a pinch', String(sizeOf(f)));

    // ---- the pair travelling together carries it, in vmin-land
    fin(f, 'pointermove', A, ax + 40, 400);
    fin(f, 'pointermove', B, ax + 140, 400);
    ok(Math.abs(sizeOf(f) - bornSize) < 0.06,
      'the pair back at its original spread is back at its original size', String(sizeOf(f)));
    ok(Math.abs(centreX() - 440) < 0.5, 'and the flash travelled with the midpoint, 40px', String(centreX()));

    // ---- spread -> --gg-flash-size, and NEVER a stacked transform scale
    fin(f, 'pointermove', B, ax + 240, 400);             // spread 200: exactly 2x
    ok(Math.abs(sizeOf(f) - bornSize * 2) < 0.12, 'doubling the gap doubles the flash',
      `${bornSize} -> ${sizeOf(f)}`);
    ok((f.style.getPropertyValue('transform').match(/scale\(/g) || []).length === 1
      && /scale\(1\.06\)/.test(f.style.getPropertyValue('transform')),
      'through the size var alone — the transform still carries the tilt and the hold lift, nothing else',
      f.style.getPropertyValue('transform'));

    // ---- the clamp, and here the SCREEN is what bites rather than the 3x
    fin(f, 'pointermove', B, ax + 40 + 2000, 400);
    const capVmin = P.pxToVmin(P.viewportCap(1280, 720, null, 1), 1280, 720);
    ok(capVmin < bornSize * P.PINCH_MAX_FACTOR,
      'on a 720-tall screen, 3x of a 43.7vmin flash does not fit — so the cap is the binding one',
      `${capVmin} < ${bornSize * P.PINCH_MAX_FACTOR}`);
    ok(Math.abs(sizeOf(f) - capVmin) < 0.2,
      'and a huge spread stops exactly there: never bigger than the screen', String(sizeOf(f)));
    fin(f, 'pointermove', B, ax + 60, 400);
    ok(Math.abs(sizeOf(f) - bornSize * P.PINCH_MIN_FACTOR) < 0.12,
      'pinching shut clamps at half its born size', String(sizeOf(f)));

    // ---- A PINCH IS NOT A THROW, and it is not a click either
    fin(f, 'pointermove', A, ax + 200, 400);
    fin(f, 'pointermove', A, ax + 320, 400);             // fast: this would be a fling
    const heldSize = sizeOf(f);                          // …and it spread the pair as it went
    fin(f, 'pointerup', A, ax + 320, 400);
    fin(f, 'pointerup', B, ax + 60, 400);
    ok(!f._cls.has('is-popped'), 'lifting two fingers never pops it — a pinch is not the hydra click',
      f.className);
    const rest = dxOf(f);
    await sleep(150);
    ok(Math.abs(dxOf(f) - rest) < 0.5,
      'and it does not sail off on the momentum of the last finger to leave', `${rest} -> ${dxOf(f)}`);
    ok(f.isConnected && Math.abs(sizeOf(f) - heldSize) < 0.12,
      'the size it was pinched to survives the release', String(sizeOf(f)));

    // ---- the hand is EMPTY: the next plain press is the hydra click it always was
    tap(f, 500, 400);
    ok(f._cls.has('is-popped'), 'the next press still pops, so no pinch state was left stuck', f.className);

    // ---- and two MICE on one flash resize nothing: desktop is untouched
    const g = shotsOf()[0];
    if (g) {
      const before = sizeOf(g);
      const M1 = ++PID, M2 = ++PID;
      ptr(g, 'pointerdown', 300, 300, { pointerId: M1 });
      ptr(g, 'pointermove', 400, 300, { pointerId: M1 });
      ptr(g, 'pointerdown', 500, 300, { pointerId: M2 });
      ptr(g, 'pointermove', 900, 300, { pointerId: M2 });
      ok(Math.abs(sizeOf(g) - before) < 0.06,
        'two mouse pointers are still one drag and one ignored press', `${before} -> ${sizeOf(g)}`);
      ptr(g, 'pointerup', 400, 300, { pointerId: M1 });
    }

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

  /* ---------------------------- PINCH: the arithmetic, with no fingers attached
   * exec/pinch.js is the whole gesture as numbers — both DOM callers (videos.js,
   * flashes.js) do nothing but read a record, call pinchStep and write a CSS var,
   * so everything that could be WRONG about a pinch is assertable right here.
   * ------------------------------------------------------------------------- */
  {
    const P = await import('../exec/pinch.js');
    ok(P.PINCH_MIN_FACTOR === 0.5 && P.PINCH_MAX_FACTOR === 3 && P.PINCH_SLOP_PX === 10
      && P.PINCH_KEEP_PX === 64,
      'the pinch dials are 0.5x..3x, 10px of slop, 64px kept on screen',
      `${P.PINCH_MIN_FACTOR}..${P.PINCH_MAX_FACTOR}x / ${P.PINCH_SLOP_PX}px / ${P.PINCH_KEEP_PX}px`);

    // ---- who may pinch: two DIFFERENT fingers, and a mouse is not a finger
    ok(P.isPinchPointer('touch') && P.isPinchPointer('pen') && !P.isPinchPointer('mouse')
      && !P.isPinchPointer(undefined),
      'touch and pen pinch; a mouse never does (desktop is untouched by all of this)');
    ok(P.pinchEligible('touch', 'touch', 1, 2) === true, 'two fingers on one surface is a pinch');
    ok(P.pinchEligible('touch', 'touch', 1, 1) === false, 'the SAME pointer twice is not');
    ok(P.pinchEligible('touch', 'mouse', 1, 2) === false, 'nor is a finger plus a mouse');
    ok(P.pinchEligible('touch', 'touch', null, 2) === false, 'nor a host that reports no pointerId');

    // ---- the two points -> spread and anchor
    ok(P.pinchDistance(0, 0, 3, 4) === 5, 'spread is the plain distance', String(P.pinchDistance(0, 0, 3, 4)));
    const mid = P.pinchMidpoint(100, 200, 300, 400);
    ok(mid.x === 200 && mid.y === 300, 'the anchor is the midpoint', JSON.stringify(mid));

    // ---- spread -> scale, guarded at both ends
    ok(P.pinchRatio(100, 200) === 2 && P.pinchRatio(200, 100) === 0.5, 'ratio is the spread ratio');
    ok(P.pinchRatio(0, 200) === 1 && P.pinchRatio(100, 0) === 1 && P.pinchRatio(NaN, 5) === 1,
      'a degenerate spread answers 1 — "leave it exactly as it is"');
    ok(P.pinchRatio(1, 100000) === P.PINCH_RATIO_MAX && P.pinchRatio(100000, 1) === P.PINCH_RATIO_MIN,
      'and an absurd one is clamped rather than allowed to explode the maths');

    // ---- two fingers resting is not a gesture
    ok(P.pinchStarted(100, 105) === false, 'a 5px wobble is not a pinch yet');
    ok(P.pinchStarted(100, 111) === true && P.pinchStarted(100, 89) === true,
      'past the slop it is — in either direction');

    // ---- the clamp: factors of the BORN size, and the viewport outranks both
    ok(P.clampSize(1000, 100, 0) === 300, 'growth stops at 3x the born size', String(P.clampSize(1000, 100, 0)));
    ok(P.clampSize(1, 100, 0) === 50, 'and shrink at 0.5x', String(P.clampSize(1, 100, 0)));
    ok(P.clampSize(150, 100, 0) === 150, 'in between it is left alone');
    ok(P.clampSize(1000, 100, 220) === 220,
      'a viewport ceiling below 3x WINS — that is the no-overflow promise', String(P.clampSize(1000, 100, 220)));
    ok(P.clampSize(1000, 100, 30) === 30 && P.clampSize(1, 100, 30) === 30,
      'a screen smaller than the FLOOR wins too: nothing is ever drawn bigger than the screen',
      `${P.clampSize(1000, 100, 30)} / ${P.clampSize(1, 100, 30)}`);
    ok(P.clampSize(120, 0, 0) === 120, 'no born size = nothing to clamp against, and no throw');

    // ---- the ceiling itself, in px, for a 16:9 window and a square flash
    const capWide = P.viewportCap(1280, 720, null, 9 / 16);
    ok(Math.abs(capWide - Math.min(1264, 704 / (9 / 16))) < 0.01,
      'a 16:9 window is capped by the SHORT side once it is wide enough', String(capWide));
    ok(Math.abs(P.viewportCap(1280, 720, null, 1) - 704) < 0.01,
      'a square one is capped by the height', String(P.viewportCap(1280, 720, null, 1)));
    ok(P.viewportCap(1280, 720, { left: 40, right: 40, top: 0, bottom: 0 }, 9 / 16) < capWide,
      'and the phone\'s safe-area insets come off it');
    ok(P.viewportCap(0, 0, null, 1) === 0, 'no viewport, no ceiling (and no NaN)');
    ok(Math.abs(P.pxToVmin(72, 1280, 720) - 10) < 1e-9 && P.pxToVmin(72, 0, 0) === 0,
      'px -> vmin for the one caller that sizes in vmin', String(P.pxToVmin(72, 1280, 720)));

    // ---- zoom about a fixed point: the pixel under the fingers stays there
    const z = P.zoomAbout(100, 100, 300, 300, 2);
    ok(z.x === -100 && z.y === -100, 'doubling about (300,300) pushes a corner at (100,100) to (-100,-100)',
      JSON.stringify(z));
    ok(P.zoomAbout(10, 20, 500, 500, 1).x === 10, 'a ratio of 1 moves nothing');
    const pd = P.panDelta(100, 100, 160, 90);
    ok(pd.x === 60 && pd.y === -10, 'and the midpoint\'s own travel is the pan', JSON.stringify(pd));

    // ---- KEEP IT ON SCREEN: contained when it fits, reachable when it cannot
    const r1 = P.clampRect(1200, 0, 200, 100, 1280, 720);
    ok(r1.x === 1080, 'a rect that fits is held WHOLLY inside — no right-edge overflow, ever', JSON.stringify(r1));
    ok(P.clampRect(-500, -500, 200, 100, 1280, 720).x === 0, '…and not off the left either');
    ok(P.clampRect(0, 0, 100, 100, 1280, 720, { left: 40, top: 30 }).x === 40,
      'containment starts at the safe-area inset, not at 0');
    const r2 = P.clampRect(-1000, 0, 2000, 100, 1280, 720, null, 64);
    ok(r2.x === -1000, 'a rect too big to contain is left where it is, so long as 64px stays reachable',
      JSON.stringify(r2));
    ok(P.clampRect(-3000, 0, 2000, 100, 1280, 720, null, 64).x === 64 - 2000,
      'and it is caught at exactly that 64px', String(P.clampRect(-3000, 0, 2000, 100, 1280, 720, null, 64).x));
    ok(P.clampRect(5, 5, 10, 10, 0, 0).x === 5, 'no viewport = no clamp (and no NaN)');

    // ---- ONE STEP, end to end: this is exactly what both DOM callers run
    const stepIn = {
      startDist: 100, startSize: 200, base: 200, size: 200,
      anchorX: 100, anchorY: 100, midX: 300, midY: 300,
    };
    const st = P.pinchStep(stepIn, { ax: 250, ay: 300, bx: 450, by: 300 },
      { vw: 1280, vh: 720, aspect: 1 });
    ok(st.size === 400, 'spreading 100px -> 200px doubles the surface', String(st.size));
    ok(st.ratio === 2 && st.midX === 350 && st.midY === 300, 'and reports the ratio and the new anchor',
      JSON.stringify(st));
    ok(st.x === 0 && st.y === 0,
      'the zoom pushed its corner off-screen and the clamp brought it back — nothing overflows',
      JSON.stringify(st));
    const twitch = P.pinchDistance(299, 300, 401, 300);
    ok(P.pinchStarted(100, twitch) === false,
      'a 2px twitch of the spread never gets past the slop gate both callers run first', String(twitch));
    const st2 = P.pinchStep(stepIn, { ax: 299, ay: 300, bx: 401, by: 300 }, { vw: 1280, vh: 720, aspect: 1 });
    ok(Math.abs(st2.size - 204) < 0.5,
      'pinchStep itself is unconditional by design — the gate is the caller\'s, so the ungated step is a plain 2%',
      String(st2.size));
    const st3 = P.pinchStep(stepIn, { ax: 100, ay: 300, bx: 1100, by: 300 },
      { vw: 1280, vh: 720, aspect: 1 });
    ok(st3.size === Math.min(200 * 3, P.viewportCap(1280, 720, null, 1)),
      'a 10x spread lands on whichever is smaller: 3x, or the screen', String(st3.size));
    const stC = P.pinchStep({ ...stepIn, anchorX: 640, anchorY: 360, midX: 640, midY: 360 },
      { ax: 540, ay: 360, bx: 740, by: 360 },
      { vw: 1280, vh: 720, aspect: 1, centred: true });
    ok(stC.x === 640 && stC.y === 360,
      'a CENTRED surface pinched about its own centre does not move an inch', JSON.stringify(stC));
    ok(P.pinchStep(null, null, null).size >= 0, 'and a step with nothing in it neither throws nor NaNs',
      JSON.stringify(P.pinchStep(null, null, null)));

    // ---- the one impure function answers ZEROES on a host that cannot say
    P.resetSafeInsets();
    const ins = P.safeInsets(1280, 720);
    ok(ins && ins.top === 0 && ins.right === 0 && ins.bottom === 0 && ins.left === 0,
      'safeInsets() on a host with no getComputedStyle is four zeroes, not a throw', JSON.stringify(ins));
    ok(P.safeInsets(1280, 720) === ins, 'and it is measured once per viewport size, not per pointermove');
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
    ok(vids()[0] && vids()[0].loop === true,
      'a PAYLOAD window LOOPS — the attack was bought for capMs, and a short clip '
      + 'ending early must not close it (2026-08-05 phone play-test)');

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

    // ---- a press that never moves is a CLICK, and a click MUTES (2026-08-04).
    // It used to DISMISS, which made the cheap gesture the destructive one: the
    // only thing a click could do to a window you merely wanted quiet was destroy
    // it. The window stays, un-receipted, wearing its muted glyph.
    skipOn(false);
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
    ok(!m.receipts.some((r) => r.id === 'pC1'),
      'a click NEVER dismisses a window — that is the ✕\'s job alone', JSON.stringify(m.receipts));
    ok(winsOf().length === 1 && clicked.isConnected && !clicked._cls.has('gg-vwin--out'),
      'the window is still up, and still floating', clicked.className);
    ok(clicked._cls.has('is-muted'), 'it is MUTED, and says so', clicked.className);
    ok(!!clicked.findAll('gg-vwin-mute')[0], 'through the glyph every window carries for the purpose');
    tap(clicked, 304, 303);
    ok(!clicked._cls.has('is-muted') && winsOf().length === 1,
      'and clicking again un-mutes it — a toggle, not a trapdoor', clicked.className);

    // ---- the ✕ is the ONE dismissal, and only when the option is on
    ok(!!closeBtnOf(clicked), 'every window builds its ✕ whether or not the option is on');
    ok(clickClose(clicked) && winsOf().length === 1 && !m.receipts.some((r) => r.id === 'pC1'),
      'and with the option OFF it refuses — a button that is merely invisible must still say no',
      String(winsOf().length));
    skipOn(true);
    clickClose(clicked);
    const c1 = m.receipts.find((r) => r.id === 'pC1');
    ok(!!c1 && c1.endured === true,
      'with it ON the ✕ closes the window, and choosing to close it is ENDURED', JSON.stringify(c1));
    ok(clicked._cls.has('gg-vwin--out') && !clicked._cls.has('is-on'),
      'the dismissed node glitches OUT rather than vanishing', clicked.className);
    await sleep(GHOST_WAIT);
    ok(winsOf().length === 0, 'the closed window is torn out', String(winsOf().length));
    ok(!clicked.isConnected, 'and the ghost with it — an exit that never removed the node is a leak');
    skipOn(false);

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
    // The hand is empty again — the very next press is a clean click, i.e. a mute.
    tap(dragged, 280, 250);
    ok(dragged._cls.has('is-muted'), 'and the next press is a clean click, so no drag state was left stuck',
      dragged.className);
    skipOn(true);
    clickClose(dragged);
    const c2 = m.receipts.find((r) => r.id === 'pC2');
    ok(!!c2 && c2.endured === true, 'and its ✕ still closes it after all that handling', JSON.stringify(c2));
    skipOn(false);
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

  /* -------------------- video windows: PINCH, the mobile half of the wheel
   * A phone has no wheel, so before this the opponent's clip arrived at whatever
   * size it was born at and there was nothing to be done about it. Two fingers
   * now drive the SAME --gg-vwin-w var — and everything below is a thing that
   * would be a bug: a pinch that mutes on release, a pinch a mouse can start, a
   * window that ends up hanging off the right edge of a phone, or a scale left
   * stuck on the next window.
   * ------------------------------------------------------------------------ */
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const host = byId.get('gg-fx-vwin');
    const V = await import('../exec/videos.js');
    const P = await import('../exec/pinch.js');
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);
    const winsOf = () => liveWins(host);
    const fire = (id) => m.emitPayload({
      payload: payloadOf({ id, kind: GoonPayloadKind.Video, duration_ms: 40000 }),
      fireAtLocalMs: localMonotonicMs(),
    });
    const sizeOf = (el) => parseFloat(el.style.getPropertyValue('--gg-vwin-w'));
    const leftOf = (el) => parseFloat(el.style.getPropertyValue('left')) + (dxOf(el) || 0);
    // A FINGER, as opposed to ptr()'s mouse: its own pointerId and a pointerType
    // the module will accept. Both are load-bearing — see the two refusals below.
    const fin = (el, type, id, x, y) => ptr(el, type, x, y, { pointerId: id, pointerType: 'touch' });
    const base = V.baseWindowWidth(1280);

    skipOn(false);
    fire('pZ1');
    await sleep(80);
    const w = winsOf()[0];
    ok(!!w && Math.abs(sizeOf(w) - base) < 1.5, 'a window to pinch, born at the base width', String(sizeOf(w)));

    // Park it at a known left with ONE finger first (the drag is unchanged), so
    // the arithmetic below does not depend on where it happened to spawn.
    const born = parseFloat(w.style.getPropertyValue('left'));
    const A = ++PID, B = ++PID;
    fin(w, 'pointerdown', A, 400, 400);
    fin(w, 'pointermove', A, 700, 400);                 // past the slop: a plain grab
    ok(w._cls.has('is-grabbed'), 'one finger still just drags it', w.className);
    const ax = 400 + (200 - born);
    fin(w, 'pointermove', A, ax, 400);                  // left := 200, exactly
    ok(Math.abs(leftOf(w) - 200) < 0.5, 'parked at a known place', String(leftOf(w)));

    // ---- the second finger. It resizes NOTHING until the spread actually moves
    fin(w, 'pointerdown', B, ax + 100, 400);            // spread 100
    ok(Math.abs(sizeOf(w) - base) < 1.5, 'a second finger landing resizes nothing on its own', String(sizeOf(w)));
    ok(w._cls.has('is-grabbed'), 'but it does take the window out of the drift, like any grab', w.className);
    fin(w, 'pointermove', B, ax + 104, 400);            // 4px: inside PINCH_SLOP_PX
    ok(Math.abs(sizeOf(w) - base) < 1.5, 'and a wobble inside the slop is still not a pinch', String(sizeOf(w)));
    fin(w, 'pointermove', B, ax + 100, 400);            // …and back, still inside it

    // ---- BOTH FINGERS TRAVELLING TOGETHER PAN IT. One event per pointer is how
    // a real touchscreen delivers this, so the spread genuinely skews mid-gesture
    // (and the size follows it) — and lands back on exactly the size and exactly
    // the +40 it should, because the size is absolute over the gesture rather
    // than accumulated, and the zoom is anchored on the midpoint.
    fin(w, 'pointermove', A, ax + 40, 400);             // spread 100 -> 60
    ok(sizeOf(w) < base, 'one finger of the pair moving is a resize, as it must be', String(sizeOf(w)));
    fin(w, 'pointermove', B, ax + 140, 400);            // spread 60 -> 100 again
    ok(Math.abs(sizeOf(w) - base) < 1.5,
      'the pair back at its original spread is back at its original size', String(sizeOf(w)));
    ok(Math.abs(leftOf(w) - 240) < 0.5, 'and the window travelled with the midpoint, 40px', String(leftOf(w)));

    // ---- spread -> size, through the var, never a stacked transform scale
    fin(w, 'pointermove', B, ax + 240, 400);            // spread 200: exactly 2x
    ok(Math.abs(sizeOf(w) - base * 2) < 2, 'doubling the gap doubles the window', `${base} -> ${sizeOf(w)}`);
    ok(!/scale\(/.test(w.style.getPropertyValue('transform')),
      'and it does it through --gg-vwin-w — the transform still carries the offset alone',
      w.style.getPropertyValue('transform'));

    // ---- the clamps: 3x, the screen, and half
    fin(w, 'pointermove', B, ax + 40 + 2000, 400);
    const cap = P.viewportCap(1280, 720, null, 9 / 16);
    ok(Math.abs(sizeOf(w) - Math.min(base * P.PINCH_MAX_FACTOR, cap)) < 2,
      'a huge spread lands on whichever is smaller: 3x its base, or the screen', String(sizeOf(w)));
    ok(leftOf(w) >= -0.5 && leftOf(w) + sizeOf(w) <= 1280.5,
      'and a window pinched wide open is STILL wholly on screen — no right-edge overflow',
      `${leftOf(w)} + ${sizeOf(w)}`);
    fin(w, 'pointermove', B, ax + 60, 400);             // spread 20: shut
    ok(Math.abs(sizeOf(w) - base * P.PINCH_MIN_FACTOR) < 2,
      'and pinching shut clamps at half its base', String(sizeOf(w)));

    // ---- panned off either edge, the edge holds
    fin(w, 'pointermove', A, ax + 40 - 3000, 400);
    fin(w, 'pointermove', B, ax + 60 - 3000, 400);
    ok(Math.abs(leftOf(w)) < 0.5, 'panned hard left, it stops AT the edge instead of leaving', String(leftOf(w)));
    fin(w, 'pointermove', A, ax + 40 + 4000, 400);
    fin(w, 'pointermove', B, ax + 60 + 4000, 400);
    ok(Math.abs(leftOf(w) + sizeOf(w) - 1280) < 1.5,
      'and panned hard right it stops at THAT edge — the overflow the mobile HUD rework was about',
      `${leftOf(w)} + ${sizeOf(w)}`);

    // ---- the release. A pinch is NOT a click, so it must not mute (nor dismiss)
    const kept = sizeOf(w);
    fin(w, 'pointerup', B, ax + 60 + 4000, 400);
    ok(!w._cls.has('is-muted'), 'lifting the second finger does not mute it', w.className);
    fin(w, 'pointerup', A, ax + 40 + 4000, 400);
    ok(!w._cls.has('is-muted'),
      'and neither does lifting the first — a pinch is never the click a one-finger press is', w.className);
    ok(w.isConnected && !m.receipts.some((r) => r.id === 'pZ1'), 'much less a dismissal');
    ok(Math.abs(sizeOf(w) - kept) < 0.5, 'the size it was pinched to survives the release', String(sizeOf(w)));

    // ---- and the hand is EMPTY: the next plain press is a plain click
    tap(w, 300, 300);
    ok(w._cls.has('is-muted'), 'the very next press is a clean click, so no pinch state was left stuck',
      w.className);
    // The ✕ is still a button, after all that handling (it never starts a gesture).
    skipOn(true);
    clickClose(w);
    const z1 = m.receipts.find((r) => r.id === 'pZ1');
    ok(!!z1 && z1.endured === true, 'and its ✕ still closes it, endured', JSON.stringify(z1));
    skipOn(false);
    await sleep(GHOST_WAIT);

    // ---- A MOUSE NEVER PINCHES. Desktop keeps exactly the gestures it had.
    fire('pZ2');
    await sleep(80);
    const w2 = winsOf()[0];
    ok(Math.abs(sizeOf(w2) - base) < 1.5,
      'the NEXT window is born at the base width — no scale leaked out of the last one', String(sizeOf(w2)));
    const M1 = ++PID, M2 = ++PID;
    ptr(w2, 'pointerdown', 300, 300, { pointerId: M1 });          // no pointerType: a mouse
    ptr(w2, 'pointermove', 400, 300, { pointerId: M1 });
    ptr(w2, 'pointerdown', 500, 300, { pointerId: M2 });          // a second mouse "finger"
    ptr(w2, 'pointermove', 900, 300, { pointerId: M2 });
    ok(Math.abs(sizeOf(w2) - base) < 1.5,
      'two MOUSE pointers resize nothing: the second is ignored exactly as it always was', String(sizeOf(w2)));
    ptr(w2, 'pointerup', 400, 300, { pointerId: M1 });

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
    skipOn(true);
    vids.renderPayload({ id: 'pA6', duration_ms: 40000, intensity: 0.5 }, () => {});
    const f = vidIn(0);
    await sleep(600);
    const node = liveWins(host)[0];
    clickClose(node);
    skipOn(false);
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
    skipOn(true);
    calmVids.renderPayload({ id: 'pCM', duration_ms: 40000, intensity: 0.5 }, () => {});
    const node = liveWins(host)[0];
    ok(!!node && !node.findAll('gg-vwin-drift')[0]._cls.has('gg-deco'),
      'a calm window is built without the decoration opt-in, as it always was');
    clickClose(node);
    skipOn(false);
    ok(calmVids.windowCount() === 0, 'its ✕ still closes it', String(calmVids.windowCount()));
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
      skipOn(true);
      clickClose(w2);
      skipOn(false);
      ok(videos.windowCount() === 0, 'its ✕ dismisses it, exactly like a thrown one\'s',
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

    // The ✕ dismisses one — the player's own way out of a window, when they have asked for one.
    // (The evicted window's node hangs around as a GHOST while it plays out its exit, so skip the
    // husks: closing one would be closing a record that left the pool two paragraphs ago.)
    const w0 = liveWins(winHost)[0];
    skipOn(true);
    clickClose(w0);
    skipOn(false);
    ok(videos.windowCount() === 3, 'the ✕ took one down', String(videos.windowCount()));
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

  /* ===================== SKIPPABLE WINDOWS, CLICK-TO-MUTE, PICKABLE AUDIO =====
   * (2026-08-04, owner-specced.) Three changes to the floating window, and each
   * one is a different answer to "what does the player get to decide":
   *
   *   1. A WINDOW CANNOT BE CLOSED EARLY unless the player asked for that, in
   *      Options, and the default is that they did not. Being thrown a window is
   *      something you sit through; a dismiss button on by default would quietly
   *      demote the payload to a notification. The flag reaches this tier as a
   *      root attribute because exec/ never imports ui/.
   *   2. A CLICK MUTES, and only mutes. The cheap gesture must not be the
   *      destructive one, and a window you merely want quiet must not have to be
   *      destroyed to get that.
   *   3. THE MIC IS PICKABLE: the last window TOUCHED (right-click, a completed
   *      drag, a wheel-resize) or SPAWNED is the one you hear. Muting the audible
   *      one leaves the room SILENT — nothing is promoted in its place, because
   *      promoting the next one is the room deciding you wanted a different
   *      soundtrack when what you said was "quiet".
   *
   * MERCY IS NEVER GATED BY ANY OF IT, which is the last block here: "unskippable"
   * is a property of the window, not of the way out.
   * ======================================================================== */

  // -------------------------- the focus resolver, pure (no DOM, no browser)
  {
    const V = await import('../exec/videos.js');
    ok(V.SKIP_ATTR === 'data-gg-vskip' && V.SKIP_ON === 'on',
      'the option travels as a root attribute — exec/ imports no ui/ module, ever',
      `${V.SKIP_ATTR}="${V.SKIP_ON}"`);
    ok(V.SKIP_BTN_CLASS === 'gg-vwin-x' && V.SKIP_BTN_MIN_PX === 22,
      'the ✕ has a class fx.css keys off and a pointer target it must meet',
      `${V.SKIP_BTN_CLASS} >= ${V.SKIP_BTN_MIN_PX}px`);
    ok(V.MUTED_CLASS === 'is-muted' && V.LIVE_CLASS === 'is-live',
      'and the two states have names both files agree on', `${V.MUTED_CLASS} / ${V.LIVE_CLASS}`);

    // A pool of n windows, wid 1..n, with the listed wids click-muted.
    const pool = (n, muted = []) => Array.from({ length: n }, (_x, i) => ({ wid: i + 1, muted: muted.includes(i + 1) }));

    ok(V.focusedIndexOf([], 1) === -1, 'nothing up, nobody speaks', String(V.focusedIndexOf([], 1)));
    ok(V.focusedIndexOf(pool(3)) === 2,
      'with no focus taken it is the NEWEST — the old invariant is the new default',
      String(V.focusedIndexOf(pool(3))));
    ok(V.focusedIndexOf(pool(3), null) === 2 && V.focusedIndexOf(pool(3), undefined) === 2,
      'and a null/undefined focus is the same answer, not a crash');
    ok(V.focusedIndexOf(pool(3), 1) === 0, 'a touched window takes the mic off the newest',
      String(V.focusedIndexOf(pool(3), 1)));
    ok(V.focusedIndexOf(pool(3), 2) === 1, 'wherever it is in the stack', String(V.focusedIndexOf(pool(3), 2)));
    ok(V.focusedIndexOf(pool(3), 99) === 2,
      'a focus id that has LEFT the pool (ended, evicted, ✕\'d) falls back to the newest remaining',
      String(V.focusedIndexOf(pool(3), 99)));
    ok(V.focusedIndexOf(pool(3, [2]), 2) === -1,
      'muting the window that had the mic is SILENCE — nothing is promoted in its place',
      String(V.focusedIndexOf(pool(3, [2]), 2)));
    ok(V.focusedIndexOf(pool(3, [3]), 1) === 0,
      'and somebody else being muted changes nothing about who speaks',
      String(V.focusedIndexOf(pool(3, [3]), 1)));
    ok(V.focusedIndexOf(pool(3, [3])) === -1,
      'a muted NEWEST is silence too — the fallback respects a mute, it does not overrule one',
      String(V.focusedIndexOf(pool(3, [3]))));

    // The two volume helpers took an index and kept their old answers.
    ok(JSON.stringify(V.unmutedFlags(4)) === '[false,false,false,true]',
      'unmutedFlags with no index is exactly what it always was', JSON.stringify(V.unmutedFlags(4)));
    ok(JSON.stringify(V.unmutedFlags(4, 1)) === '[false,true,false,false]',
      'and with one, it is the index that speaks', JSON.stringify(V.unmutedFlags(4, 1)));
    ok(JSON.stringify(V.unmutedFlags(4, -1)) === '[false,false,false,false]',
      'index -1 is a silent room, which is a legal thing to be', JSON.stringify(V.unmutedFlags(4, -1)));
    ok(JSON.stringify(V.audioTargets([0.7, 0.7, 0.7], 0)) === '[0.421,0,0]',
      'audioTargets aims the level at the FOCUSED window', JSON.stringify(V.audioTargets([0.7, 0.7, 0.7], 0)));
    ok(V.audioTargets([0.7, 0.7, 0.7], -1).every((x) => x === 0),
      'and at nobody when the room is silenced', JSON.stringify(V.audioTargets([0.7, 0.7, 0.7], -1)));
    ok(JSON.stringify(V.audioTargets([0.7, 0.7, 0.7])) === '[0,0,0.421]',
      'while the un-indexed call is untouched — every newest-speaks pin above still means what it said',
      JSON.stringify(V.audioTargets([0.7, 0.7, 0.7])));
    for (let k = 1; k <= 6; k++) {
      const t = V.audioTargets(new Array(k).fill(0.7), 0);
      ok(t.filter((x) => x > 0).length === 1 && t[0] > 0, `still exactly one speaker at n=${k}`, JSON.stringify(t));
    }
  }

  // ------------------- the ✕: only when asked for, never a drag, always endured
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const host = byId.get('gg-fx-vwin');
    const V = await import('../exec/videos.js');
    const vids = V.createVideos({ layers, media: fakeMedia(), logger: quiet });

    skipOn(false);
    ok(vids.skippable() === false,
      'DEFAULT OFF — a window runs its course, and nothing in the page had to say so',
      String(vids.skippable()));
    documentElement.removeAttribute(V.SKIP_ATTR);
    ok(vids.skippable() === false,
      'and a page that never wrote the attribute at all is off too, not undefined-on');

    let endured = null;
    vids.renderPayload({ id: 'pX1', duration_ms: 40000, intensity: 0.5 }, (e) => { endured = e; });
    const node = liveWins(host)[0];
    const btn = closeBtnOf(node);
    ok(!!btn, 'the ✕ is BUILT into every window regardless — fx.css decides whether it exists for the player');
    ok(btn.parentNode && btn.parentNode._cls.has('gg-vwin-inner'),
      'it lives inside the window chrome, next to the dot it sits opposite', String(btn.parentNode && btn.parentNode.className));
    ok(btn.getAttribute('type') === 'button' && !!btn.getAttribute('aria-label'),
      'a real button with a real label', `${btn.getAttribute('type')} / ${btn.getAttribute('aria-label')}`);

    // IT MUST NOT START A DRAG. The press stops at the button — and is NOT
    // preventDefault'ed, because that is how a browser is talked out of the click
    // that follows, and the click is the entire control.
    let stopped = false;
    const down = ptr(btn, 'pointerdown', 20, 20, { stopPropagation() { stopped = true; } });
    ok(stopped === true, 'a press on the ✕ stops there: it can never become a drag of the window');
    ok(down.defaulted === false,
      'and it does not preventDefault — a swallowed default is a swallowed click');
    ok(!node._cls.has('is-grabbed'), 'the window was never grabbed', node.className);

    btn.fire('click');
    ok(vids.windowCount() === 1 && endured === null,
      'with the option OFF the ✕ refuses — invisible is not the same as harmless', String(vids.windowCount()));

    skipOn(true);
    ok(vids.skippable() === true, 'the option is read FRESH every time, never cached at spawn');
    btn.fire('click');
    ok(vids.windowCount() === 0 && endured === true,
      'and now it closes the window, receipting ENDURED — the player chose to close it',
      `${vids.windowCount()} / ${endured}`);
    ok(node._cls.has(V.OUT_CLASS), 'through the ordinary exit, ghost and all', node.className);
    await sleep(GHOST_WAIT);

    // …and the button reaches windows that were ALREADY UP when the toggle
    // flipped, because nothing about it is decided at spawn time.
    skipOn(false);
    vids.renderPayload({ id: 'pX2', duration_ms: 40000, intensity: 0.5 }, () => {});
    const later = liveWins(host)[0];
    skipOn(true);
    ok(clickClose(later) && vids.windowCount() === 0,
      'a window born while the option was off still answers its ✕ once it is on', String(vids.windowCount()));
    skipOn(false);
    await sleep(GHOST_WAIT);
    vids.stopWindows();
  }

  /* --------------- the mic: pickable, mutable, and never auto-promoted
   * Everything here is a gesture the player made, and the assertions are about
   * WHO IS AUDIBLE afterwards. focusedIndex() is the renderer's own resolver
   * running over the real pool, so a green run here is the same function the
   * pure block above pinned, wired to real nodes.
   * ------------------------------------------------------------------------ */
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const host = byId.get('gg-fx-vwin');
    const V = await import('../exec/videos.js');
    const vids = V.createVideos({ layers, media: fakeMedia(), logger: quiet });
    const A = V.volumeFor(0.5);
    /* NODES ARE HELD BY NAME HERE, never by DOM index: grabbing a window
     * RE-APPENDS it (that is how the z-lift works — fx.css raises no z-index), so
     * after the first drag the layer's order and the POOL's order are different
     * things, and the pool's is the one focusedIndex() answers in. */
    const vidOf = (nd) => nd.findAll('gg-vwin-vid')[0];
    const spawn = (id) => {
      vids.renderPayload({ id, duration_ms: 40000, intensity: 0.5 }, () => {});
      const all = liveWins(host);
      return all[all.length - 1];        // a spawn appends, so the newest IS last
    };
    skipOn(false);

    const a = spawn('pF1'), b = spawn('pF2'), c = spawn('pF3');
    ok(vids.windowCount() === 3, 'three windows up', String(vids.windowCount()));
    ok(vids.focusedIndex() === 2, 'the newest SPAWN took the mic', String(vids.focusedIndex()));
    ok(c._cls.has('is-live') && !a._cls.has('is-live'),
      'and only that one wears .is-live, which is the pulsing dot', c.className);
    await sleep(560);
    ok(Math.abs(vidOf(c).volume - A) < 1e-9 && vidOf(a).volume === 0,
      'it is the one making a sound', `${vidOf(a).volume} / ${vidOf(c).volume}`);

    // ---- RIGHT CLICK is a touch, and it refuses the page's own menu
    const rc = ptr(a, 'contextmenu', 300, 300, { button: 2 });
    ok(rc.defaulted === true, 'a right-click preventDefaults — no browser menu over a duel');
    ok(vids.focusedIndex() === 0, 'and hands the mic to the window under it', String(vids.focusedIndex()));
    await sleep(120);
    ok(vidOf(a).volume > 0 && vidOf(c).volume > 0 && vidOf(c).volume < A,
      'the handover CROSSFADES, exactly like a spawn does',
      `${vidOf(a).volume} up / ${vidOf(c).volume} down`);
    await sleep(560);
    ok(Math.abs(vidOf(a).volume - A) < 1e-9 && vidOf(c).volume === 0 && vidOf(c).muted === true,
      'and lands: one speaker, the one you picked', `${vidOf(a).volume} / ${vidOf(c).volume}`);
    ok(a._cls.has('is-live') && !c._cls.has('is-live'),
      'the live light moved with it', `${a.className} | ${c.className}`);

    // ---- A COMPLETED DRAG is a touch. A press that never moves is not.
    PID++;
    ptr(b, 'pointerdown', 500, 500);
    ok(vids.focusedIndex() === 0, 'merely pressing a window takes nothing — the press is still deciding',
      String(vids.focusedIndex()));
    ptr(b, 'pointermove', 600, 600);
    ptr(b, 'pointerup', 600, 600);
    ok(!b._cls.has('is-grabbed'), 'released', b.className);
    ok(vids.focusedIndex() === 1, 'dragging one hands it the mic', String(vids.focusedIndex()));

    // ---- SO IS A WHEEL-RESIZE ("the last one we moved, resized, etc").
    ptr(c, 'wheel', 300, 300, { deltaY: -100 });
    ok(vids.focusedIndex() === 2, 'resizing one hands it the mic too', String(vids.focusedIndex()));
    for (let i = 0; i < 8; i++) ptr(c, 'wheel', 300, 300, { deltaY: -100 });
    ok(vids.focusedIndex() === 2,
      'and a long spin is ONE handover, not eight — re-focusing the focused window is a no-op all the way down',
      String(vids.focusedIndex()));
    await sleep(560);
    ok(Math.abs(vidOf(c).volume - A) < 1e-9 && vidOf(b).volume === 0,
      'the sound really followed the wheel', `${vidOf(b).volume} / ${vidOf(c).volume}`);

    // ---- CLICK-MUTING the audible window is SILENCE, and silence is a state
    tap(c, 300, 300);
    ok(JSON.stringify(vids.mutedFlags()) === '[false,false,true]',
      'a click mutes exactly the window it landed on', JSON.stringify(vids.mutedFlags()));
    ok(vids.focusedIndex() === -1,
      'and with the audible one muted the room goes SILENT — no window is promoted into the gap',
      String(vids.focusedIndex()));
    ok(vids.windowCount() === 3, 'nothing was dismissed by any of that', String(vids.windowCount()));
    ok(c._cls.has('is-muted') && !c._cls.has('is-live'),
      'the muted window says so, and its light is out', c.className);
    ok(liveWins(host).every((w) => !w._cls.has('is-live')),
      'and nothing else lit up in its place', liveWins(host).map((w) => w.className).join(' | '));
    await sleep(400);
    ok([a, b, c].every((nd) => vidOf(nd).volume === 0),
      'every window is at zero — silence is real, not merely un-drawn',
      [a, b, c].map((nd) => vidOf(nd).volume).join(','));

    // ---- a DRAG of a muted window moves it without asking it to speak
    PID++;
    ptr(c, 'pointerdown', 200, 200);
    ptr(c, 'pointermove', 300, 260);
    ptr(c, 'pointerup', 300, 260);
    ok(vids.focusedIndex() === -1 && vids.mutedFlags()[2] === true,
      'silencing a window and then moving it out of the way is ONE intention, not two',
      `${vids.focusedIndex()} / ${JSON.stringify(vids.mutedFlags())}`);

    // ---- RIGHT CLICK is the way back: an explicit "you, speak"
    ptr(c, 'contextmenu', 300, 300, { button: 2 });
    ok(vids.mutedFlags()[2] === false && vids.focusedIndex() === 2,
      'a right-click UNMUTES as well as focusing — it outranks a stale shush',
      `${JSON.stringify(vids.mutedFlags())} / ${vids.focusedIndex()}`);
    await sleep(560);
    ok(Math.abs(vidOf(c).volume - A) < 1e-9, 'and it is audible again', String(vidOf(c).volume));

    // ---- the focused window LEAVING falls back to the newest remaining
    ptr(a, 'contextmenu', 300, 300, { button: 2 });
    ok(vids.focusedIndex() === 0, 'the oldest window has the mic', String(vids.focusedIndex()));
    vidOf(a).onended();
    ok(vids.windowCount() === 2 && vids.focusedIndex() === 1,
      'its clip ends, and the mic falls back to the newest remaining — never to nobody',
      `${vids.windowCount()} up, focus ${vids.focusedIndex()}`);
    await sleep(GHOST_WAIT);

    // …and an EVICTION is the same fall-back, from the other direction.
    spawn('pF4'); spawn('pF5');
    ok(vids.windowCount() === 4 && vids.focusedIndex() === 3, 'four up, the newest speaking',
      `${vids.windowCount()} / ${vids.focusedIndex()}`);
    ptr(b, 'contextmenu', 300, 300, { button: 2 });   // b is the OLDEST left in the pool
    ok(vids.focusedIndex() === 0, 'the oldest takes the mic, one moment before it is displaced',
      String(vids.focusedIndex()));
    spawn('pF6');
    ok(vids.windowCount() === 4 && vids.focusedIndex() === 3,
      'the fifth window evicts the one holding the mic and takes it, as a spawn always does',
      `${vids.windowCount()} / ${vids.focusedIndex()}`);
    vids.stopWindows();
    ok(vids.focusedIndex() === -1, 'an empty pool has no speaker at all', String(vids.focusedIndex()));
    await sleep(GHOST_WAIT);
  }

  /* --------------- MERCY IS NEVER GATED. "Unskippable" is a property of the
   * WINDOW, not of the way out: the payload's cancel fn and stopWindows() are
   * what mercy, the recap and detach reach the pool through, and they take
   * everything on the spot whatever the option says, whatever is muted and
   * whoever has the mic. There is deliberately NO code path from the option to
   * teardown — this block is the pin that keeps it that way.
   * ------------------------------------------------------------------------ */
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const host = byId.get('gg-fx-vwin');
    const V = await import('../exec/videos.js');
    const vids = V.createVideos({ layers, media: fakeMedia(), logger: quiet });
    skipOn(false);                       // the UNSKIPPABLE default, on purpose

    const seen = [];
    const cancels = ['pM1', 'pM2', 'pM3'].map((id) =>
      vids.renderPayload({ id, duration_ms: 40000, intensity: 0.5 }, (e) => seen.push({ id, e })));
    ok(vids.windowCount() === 3 && vids.skippable() === false,
      'three windows the player may NOT close, which is the whole point of the default',
      `${vids.windowCount()} / ${vids.skippable()}`);
    // …and to be sure it is the option and not the pool: the ✕ is refusing.
    ok(!clickClose(liveWins(host)[0]) || vids.windowCount() === 3,
      'their ✕ does nothing at all', String(vids.windowCount()));
    tap(liveWins(host)[1], 300, 300);    // one muted
    ptr(liveWins(host)[0], 'contextmenu', 300, 300, { button: 2 });   // another holding the mic

    ok(vids.stopWindows() === 3, 'mercy sweeps all three anyway', String(vids.windowCount()));
    ok(vids.windowCount() === 0 && vids.ghostCount() === 0 && host.childNodes.length === 0,
      'in the same tick, node and all — the recap inherits an EMPTY layer, not three it may not close',
      `${vids.windowCount()} / ${vids.ghostCount()} / ${host.childNodes.length}`);
    ok(vids.focusedIndex() === -1, 'and nothing is left holding the mic', String(vids.focusedIndex()));

    // The other teardown door: the executor's own cancel fn, equally ungated.
    const c = vids.renderPayload({ id: 'pM4', duration_ms: 40000, intensity: 0.5 }, (e) => seen.push({ id: 'pM4', e }));
    tap(liveWins(host)[0], 300, 300);
    c();
    ok(vids.windowCount() === 0 && host.childNodes.length === 0,
      'a cancel fn takes a muted, unskippable window on the spot too', String(host.childNodes.length));
    ok(seen.length === 4 && seen.every((r) => r.e === false),
      'and every one of them receipts COMPLETED — swept, not endured, and never a charge',
      JSON.stringify(seen));
    ok(cancels.every((fn) => typeof fn === 'function'), 'the cancel fns are still the uniform renderer shape');
  }

  /* --------------- the fx.css half of the same three changes ---------------
   * The button's VISIBILITY is a stylesheet fact and nothing in JS can see it,
   * which is exactly why it is done there: flipping the option must reach every
   * window already on screen without the renderer hearing about it.
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
    const V = await import('../exec/videos.js');

    const x = ruleOf('.gg-vwin-x') || '';
    ok(!!x, 'fx.css carries the ✕ rule');
    ok(/display:\s*none/.test(x),
      'and it is HIDDEN by default — the option is off, so for almost everyone the button does not exist',
      x.replace(/\s+/g, ' ').trim());
    const on = new RegExp('html\\[' + V.SKIP_ATTR + '="' + V.SKIP_ON + '"\\]\\s*\\.' + V.SKIP_BTN_CLASS + '\\s*\\{([^}]*)\\}').exec(css);
    ok(!!on && /display:\s*(flex|block|grid)/.test(on[1]),
      'the root attribute is what turns it on, so a live toggle reaches windows already floating',
      String(on && on[1]));
    const size = /width:\s*(\d+)px/.exec(x);
    const hsize = /height:\s*(\d+)px/.exec(x);
    ok(!!size && Number(size[1]) >= V.SKIP_BTN_MIN_PX && !!hsize && Number(hsize[1]) >= V.SKIP_BTN_MIN_PX,
      `a real pointer target (>= ${V.SKIP_BTN_MIN_PX}px both ways)`, `${size && size[1]} x ${hsize && hsize[1]}`);
    ok(/top:/.test(x) && /left:/.test(x) && !/right:/.test(x),
      'pinned TOP-LEFT, opposite the live dot — the two controls never sit on each other',
      x.replace(/\s+/g, ' ').trim());
    ok(/pointer-events:\s*auto/.test(x), 'it opts into pointer events on a click-through layer');
    ok(!/(^|[;\s])animation:/.test(x),
      'and declares NO animation — the hover lift is a transition, because this element has a glitch-in playing over it',
      x.replace(/\s+/g, ' ').trim());
    ok(/transition:/.test(x), 'which is what the hover glow rides instead');
    // THE GHOST'S BUTTON. pointer-events:none on the wrapper does NOT protect a
    // child that opted back in — a husk with a live ✕ is the click shield this
    // whole layer is arranged to prevent, wearing a different hat.
    const ghostX = new RegExp('\\.gg-vwin\\.' + V.OUT_CLASS + '\\s+\\.' + V.SKIP_BTN_CLASS + '\\s*\\{([^}]*)\\}').exec(css);
    ok(!!ghostX && /display:\s*none/.test(ghostX[1]) && /pointer-events:\s*none/.test(ghostX[1]),
      'a GHOST has no ✕: the wrapper\'s pointer-events:none cannot protect a child that opts back in',
      String(ghostX && ghostX[1]));

    // THE MUTED GLYPH — state, not a control.
    const mute = ruleOf('.gg-vwin-mute') || '';
    ok(!!mute, 'fx.css carries the muted-speaker glyph');
    ok(/display:\s*none/.test(mute), 'hidden until the window is muted', mute.replace(/\s+/g, ' ').trim());
    ok(new RegExp('\\.gg-vwin\\.' + V.MUTED_CLASS + '\\s+\\.gg-vwin-mute\\s*\\{[^}]*display:\\s*(block|flex)').test(css),
      'and shown by the same class exec/videos.js paints on the wrapper');
    ok(/pointer-events:\s*none/.test(mute),
      'it never eats the click that would un-mute the window under it', mute.replace(/\s+/g, ' ').trim());
    ok(/mask:/.test(mute) && /--gg-pink-rgb/.test(mute),
      'a masked glyph tinted in the house pink, not a coloured-in image', mute.replace(/\s+/g, ' ').trim());
    ok(!/(^|[;\s])animation:/.test(mute), 'and it does not animate — it is a label');

    // THE LIVE LIGHT now means "this is the one you can hear". The pulse stays
    // declared on .gg-vwin-dot (its animation slot is the dot's, forever); the
    // OTHER windows have it switched off by a more specific rule.
    const dark = new RegExp('\\.gg-vwin:not\\(\\.' + V.LIVE_CLASS + '\\)\\s+\\.gg-vwin-dot\\s*\\{([^}]*)\\}').exec(css);
    ok(!!dark && /animation:\s*none/.test(dark[1]),
      'a window that is NOT the audible one has a dark, still light — one blinking dot means one thing',
      String(dark && dark[1]));
    ok(!!dark && /opacity:\s*0?\.\d+/.test(dark[1]),
      'dimmed rather than removed, so the window still reads as a little monitor', String(dark && dark[1]));
  }

  /* =========================================================================
   * THE SPIRAL PANE'S FREEZE DEFENCES (added 2026-08-04, after the incident).
   *
   * WHY THIS BLOCK EXISTS. A session froze VISUALLY while its JS kept running:
   * the app was healthy, the page's heartbeat never stopped, WebView2 wrote no
   * crash dump — a compositor/GPU stall. The shader spiral bed (exec/spiralField
   * .js) had gone in hours earlier and is the only GPU context the duel owns, so
   * it is now droppable four ways, and every one of them must land on the RASTER
   * bed with its fx.css treatments intact.
   *
   * THE STUB GROWS A GPU FOR THIS BLOCK ONLY. Everything above ran with no
   * canvas and no rAF — which is itself the "machine without WebGL" case, and is
   * why every check above sees the raster path. Here a fake canvas/context and a
   * MANUALLY PUMPED rAF are installed (frames only advance when this test says
   * so, which is what makes a 5-second stall testable in no time at all), and
   * both are removed again at the end so nothing downstream inherits them.
   *
   * WHAT CANNOT BE PINNED HERE: whether a real driver hang delivers
   * `webglcontextlost` at all. That is the whole reason the polled check and the
   * frame-gap watchdog exist next to the event listener rather than instead of it.
   * ======================================================================= */
  {
    const SP = await import('../exec/spiral.js');

    // --- the fake GPU ------------------------------------------------------
    let draws = 0;
    let drawsWhileDetached = 0;
    let contexts = 0;
    let lastGl = null;
    function fakeGl(el) {
      let lost = false;
      const g = {
        VERTEX_SHADER: 1, FRAGMENT_SHADER: 2, COMPILE_STATUS: 3, LINK_STATUS: 4,
        ARRAY_BUFFER: 5, STATIC_DRAW: 6, FLOAT: 7, TRIANGLES: 8,
        createShader: () => ({}), shaderSource() {}, compileShader() {}, deleteShader() {},
        getShaderParameter: () => true, getShaderInfoLog: () => '',
        createProgram: () => ({}), attachShader() {}, linkProgram() {}, useProgram() {},
        getProgramParameter: () => true, getProgramInfoLog: () => '',
        createBuffer: () => ({}), bindBuffer() {}, bufferData() {},
        getAttribLocation: () => 0, enableVertexAttribArray() {}, vertexAttribPointer() {},
        getUniformLocation: () => ({}),
        uniform1f() {}, uniform2f() {}, uniform3f() {}, uniform3fv() {},
        viewport() {},
        drawArrays: () => { draws++; if (!el.isConnected) drawsWhileDetached++; },
        deleteBuffer() {}, deleteProgram() {},
        getExtension: () => ({ loseContext() { lost = true; } }),
        isContextLost: () => lost,
        getError: () => (lost ? 0x9242 : 0),
        /** test-only: what a driver reset does behind the page's back */
        __die() { lost = true; },
      };
      return g;
    }
    const realCreate = document.createElement;
    document.createElement = (tag) => {
      const el = realCreate(tag);
      if (String(tag).toLowerCase() === 'canvas') {
        el.width = 0; el.height = 0;
        // THE FAKE IS A GPU, NOT A CANVAS. '2d' is answered with null on
        // purpose: exec/spiralGen.js's raster bake asks for a 2D context on its
        // own scratch canvas, and handing it this object would both count a
        // context this block never asked for and move `lastGl` off the weave
        // that the loss tests below are about to kill.
        el.getContext = (type) => {
          if (String(type) === '2d') return null;
          contexts++; lastGl = fakeGl(el); return lastGl;
        };
      }
      return el;
    };
    let rafQ = [];
    globalThis.requestAnimationFrame = (fn) => { rafQ.push(fn); return rafQ.length; };
    globalThis.cancelAnimationFrame = () => {};
    /** Run every queued frame callback at timestamp `ts`. */
    const pump = (ts) => { const q = rafQ; rafQ = []; for (const fn of q) fn(ts); };
    globalThis.devicePixelRatio = 2;
    globalThis.innerWidth = 3840;
    globalThis.innerHeight = 2160;

    const spiralLayer = byId.get('gg-fx-spiral');
    const paneOf = () => spiralLayer.findAll('gg-spiral')[0] || null;
    const canvasOf = () => {
      const p = paneOf();
      return p ? (p.childNodes.find((c) => c.tagName === 'CANVAS') || null) : null;
    };
    const inlineOf = (k) => { const p = paneOf(); return p ? p.style.getPropertyValue(k) : '(no pane)'; };
    /** A pane on a CLEAN layer — the previous one's node lingers 900ms by design. */
    const fresh = () => {
      spiralLayer.replaceChildren();
      rafQ = [];
      const s = SP.createSpiral({ layers, media: fakeMedia(), audio: { sfx() {} }, logger: quiet });
      s.start({ element: GoonElement.Spiral, intensity: 0.5, durationMs: 0, elapsedMs: 0 });
      return s;
    };

    // ---------------------------------------------- the kill switch, from cold
    documentElement.setAttribute(SP.SHADER_ATTR, SP.SHADER_OFF);
    {
      const sp = fresh();
      ok(!!paneOf(), 'shaders off: the spiral bed still mounts — the switch costs a renderer, not the effect');
      ok(!canvasOf(), 'shaders off: and it is the RASTER bed, with no canvas ever created',
        String(canvasOf() && 'canvas'));
      ok(inlineOf('filter') === '' && inlineOf('scale') === '' && inlineOf('animation') === '',
        'shaders off: no inline overrides, so fx.css paints its blur(1.1px) + overscan + spin',
        `filter="${inlineOf('filter')}" scale="${inlineOf('scale')}"`);
      // The variant reached the pane: its solved revolution and its rolled
      // direction are on it. (The baked PNG is not — this fake GPU refuses a 2D
      // context, which is exactly the "cannot bake" host, and the renderer must
      // leave background-image alone rather than write url('').)
      const spin = parseFloat(inlineOf('--gg-spiral-spin'));
      ok(spin >= 10 && spin <= 18,
        'shaders off: a GENERATED variant is on the pane, spinning at its own solved rate',
        inlineOf('--gg-spiral-spin'));
      ok(!String(inlineOf('background-image')).trim(),
        'shaders off: and a host that cannot bake a still writes no background-image at all',
        inlineOf('background-image'));
      sp.stop();
    }

    // ------------------------------------------------- the default is the good bed
    documentElement.removeAttribute(SP.SHADER_ATTR);
    {
      const sp = fresh();
      const cv = canvasOf();
      ok(!!cv, 'with the attribute ABSENT the pane weaves the shader — a page with no prefs store still gets the good path');
      ok(inlineOf('scale') === '1' && inlineOf('filter') === 'none' && inlineOf('animation') === 'none',
        'the woven pane cancels the three raster-only treatments inline',
        `scale="${inlineOf('scale')}" filter="${inlineOf('filter')}"`);
      ok(drawsWhileDetached >= 1,
        'and the FIRST frame is drawn BEFORE the canvas is appended — an alpha:false canvas is opaque black, and appending an undrawn one blinks the bed out',
        String(drawsWhileDetached));

      // --- the backing-store cap. 3840x2160 at devicePixelRatio 2 would be
      // 5760x3240 (18.7MP) after the ratio cap alone; the pixel budget pulls it
      // back to one 4K frame, aspect intact.
      ok(cv.width * cv.height <= SP.MAX_BACKING_PX,
        'the backing store stays inside the pixel budget', `${cv.width}x${cv.height}`);
      ok(cv.width === 3840 && cv.height === 2160,
        'a 4K window at DPR 2 allocates one 4K frame, not an 8K one', `${cv.width}x${cv.height}`);
      ok(Math.abs((cv.width / cv.height) - (3840 / 2160)) < 0.01,
        'and the budget preserves aspect — a squashed spiral is worse than a soft one', String(cv.width / cv.height));
      sp.stop();
    }

    // --- the ratio cap on its own, where the budget never bites
    globalThis.innerWidth = 1280; globalThis.innerHeight = 720; globalThis.devicePixelRatio = 3;
    {
      const sp = fresh();
      const cv = canvasOf();
      ok(!!cv && cv.width === Math.round(1280 * SP.MAX_DPR) && cv.height === Math.round(720 * SP.MAX_DPR),
        `devicePixelRatio 3 is capped at MAX_DPR ${SP.MAX_DPR} — the fill cost is per pixel per frame`,
        cv ? `${cv.width}x${cv.height}` : '(none)');
      sp.stop();
    }
    globalThis.devicePixelRatio = 1;

    // ------------------------------------------- webglcontextlost, at runtime
    {
      const sp = fresh();
      const cv = canvasOf();
      ok(!!cv, 'woven, ready to lose it');
      let prevented = false;
      cv._listeners.get('webglcontextlost')[0]({ type: 'webglcontextlost', preventDefault() { prevented = true; } });
      ok(prevented,
        'the loss event is preventDefault()ed — without it the browser never fires webglcontextrestored at all');
      ok(!canvasOf(), 'and the pane is on the raster bed THAT INSTANT, not after a restore that may never come');
      ok(inlineOf('scale') === '' && inlineOf('filter') === '' && inlineOf('animation') === '',
        'ALL THREE inline cancels are handed back — a leftover filter:none would be a magnified, UNBLURRED, still raster',
        `scale="${inlineOf('scale')}" filter="${inlineOf('filter')}" animation="${inlineOf('animation')}"`);
      // The still underneath is the SAME roll the shader was weaving, so the
      // swap is a change of renderer, not of picture. It cannot be asserted by
      // its pixels here (this fake GPU refuses a 2D context, so nothing baked);
      // what IS asserted is that the pane kept the variant's spin, which is
      // what the CSS keyframe now needs to turn that still at the right rate.
      const spin = parseFloat(inlineOf('--gg-spiral-spin'));
      ok(spin >= 10 && spin <= 18,
        'and the surviving pane keeps the variant‘s own revolution for the CSS spin to use',
        inlineOf('--gg-spiral-spin'));
      ok(cv.width === 1 && cv.height === 1,
        'the dead canvas is shrunk to 1x1: an antenna for the restore, not a retained backing store',
        `${cv.width}x${cv.height}`);
      const before = draws;
      pump(1000); pump(2000);
      ok(draws === before, 'and the render loop is over — no frame survives the swap', String(draws - before));

      // --- the lazy re-upgrade
      cv.fire('webglcontextrestored');
      const cv2 = canvasOf();
      ok(!!cv2, 'webglcontextrestored re-upgrades the pane');
      ok(cv2 !== cv, 'on a FRESH canvas — the 1x1 antenna is never drawn on again');
      ok((cv._listeners.get('webglcontextrestored') || []).length === 0,
        'and the antenna stops listening, so nothing keeps the dead node alive');

      // --- the budget. Two recoveries, then the pane stays on raster.
      cv2.fire('webglcontextlost');
      cv2.fire('webglcontextrestored');
      const cv3 = canvasOf();
      ok(!!cv3, 'a second loss recovers too');
      cv3.fire('webglcontextlost');
      ok(!canvasOf(), 'the third loss drops to raster');
      cv3.fire('webglcontextrestored');
      ok(!canvasOf(),
        `and STAYS there — MAX_CONTEXT_RECOVERIES (${SP.MAX_CONTEXT_RECOVERIES}) is a budget, so a driver resetting every few seconds cannot be handed the shader forever`);
      sp.stop();
    }

    // --- a restore that arrives after the bed is gone must not resurrect one
    {
      const sp = fresh();
      const cv = canvasOf();
      cv.fire('webglcontextlost');
      sp.stop();
      spiralLayer.replaceChildren();
      cv.fire('webglcontextrestored');
      ok(!paneOf() && !canvasOf(),
        'a restore that lands after the pane is gone weaves nothing — the bed does not come back from the dead');
    }

    // --- and a NEW pane gets a clean try: the defences are per-pane
    {
      const sp = fresh();
      ok(!!canvasOf(), 'the next spiral bed weaves again — one bad moment does not cost the session its good spiral');
      sp.stop();
    }

    // ------------------------------------------ the watchdog: rAF gap > 4s
    {
      const sp = fresh();
      ok(!!canvasOf(), 'woven, loop armed');
      pump(1000);            // first health check: no frame pair yet
      pump(2000);            // a healthy 1s gap
      ok(!!canvasOf(), 'a normal frame cadence is left alone', String(SP.HEALTH_CHECK_MS));
      pump(2000 + SP.RAF_STALL_MS + 500);
      ok(!canvasOf(),
        `frame callbacks more than RAF_STALL_MS (${SP.RAF_STALL_MS}) apart, while the pane believes it is drawing, drop it to raster`);
      ok(inlineOf('filter') === '', 'landing in the BLURRED raster branch, not a blurless hybrid', inlineOf('filter'));
      sp.stop();
    }

    // --- ...but a HIDDEN page is not a stalled one. rAF is suspended by design
    //     while hidden, so the first frame after a resume carries the whole gap.
    {
      const sp = fresh();
      document.visibilityState = 'hidden';
      pump(1000);
      pump(1000 + SP.RAF_STALL_MS + 5000);
      ok(!!canvasOf(),
        'a hidden page keeps its shader — not painting is CORRECT there, and a watchdog that fires on correct behaviour gets switched off by the people it protects');
      delete document.visibilityState;
      sp.stop();
    }

    // ------------------------------- the watchdog: a context lost WITHOUT the event
    {
      const sp = fresh();
      ok(!!canvasOf() && !!lastGl, 'woven, with a context to kill');
      const cv = canvasOf();
      lastGl.__die();        // a driver reset that never delivers an event
      pump(1000);
      pump(2000);
      ok(!canvasOf(),
        'a context that went away silently is caught by the once-a-second poll — an event needs an event loop, which is exactly what a stall is eating');
      ok(cv.width === 1 && cv.height === 1, 'and it takes the same antenna path as the event', `${cv.width}x${cv.height}`);
      cv.fire('webglcontextrestored');
      ok(!!canvasOf(), 'so a polled loss can still re-upgrade');
      sp.stop();
    }

    // --------------------------------------------------- IDLE DISCIPLINE
    // An always-running shader loop on an idle match is a perf tax AND a bigger
    // hang surface. The pane's own generation token is what guarantees this.
    {
      const sp = fresh();
      pump(1000);
      const before = draws;
      sp.stop();                       // teardown detaches the weave immediately
      pump(2000); pump(3000); pump(4000);
      ok(draws === before,
        'stopping the element ends the render loop on the spot — an idle match runs no shader frames at all',
        String(draws - before));
      ok(rafQ.length === 0, 'and re-arms nothing', String(rafQ.length));
    }

    // ------------------------------- the kill switch, flipped MID-BED and back
    {
      const sp = fresh();
      ok(!!canvasOf(), 'woven');
      documentElement.setAttribute(SP.SHADER_ATTR, SP.SHADER_OFF);
      pump(1000);
      pump(2000);
      ok(!canvasOf(),
        'switching shader spirals off reaches a bed that is ALREADY running, within one health check — that is what makes it an escape hatch during a freeze rather than a setting for next time');
      ok(inlineOf('filter') === '' && inlineOf('animation') === '',
        'and the raster bed it lands on is the full fx.css one', `filter="${inlineOf('filter')}"`);
      documentElement.removeAttribute(SP.SHADER_ATTR);
      const cancel = sp.renderPayload({ id: 'sp1', duration_ms: 4000, intensity: 0.6 }, () => {});
      ok(!!canvasOf(),
        'switching it back on re-weaves at the NEXT CUE, not mid-bed — a bed that snapped from raster to shader under the player would read as a glitch');
      cancel();
      sp.stop();
    }

    // ---------------------------------------------------------------- cleanup
    spiralLayer.replaceChildren();
    document.createElement = realCreate;
    delete globalThis.requestAnimationFrame;
    delete globalThis.cancelAnimationFrame;
    delete globalThis.devicePixelRatio;
    delete globalThis.innerWidth;
    delete globalThis.innerHeight;
    documentElement.removeAttribute(SP.SHADER_ATTR);
    ok(contexts > 0, 'the fake GPU was actually exercised', String(contexts));
  }

  /* --------- THE MANDATORY VIDEO OWNS THE ROOM (2026-08-04, owner-specced)
   * "when a mandatory video we trigger is playing we might wanna mute the other
   * videos." So while the ELEMENT bed is up — the shared ramp's fullscreen video
   * on #gg-stage — every floating window ducks to silence, and when it ends they
   * come back exactly as they were.
   *
   * THREE THINGS ARE BEING PINNED HERE, and they are all failure modes rather
   * than features:
   *   · the duck is a BED-level fact, not a clip-level one. The element chains
   *     clip after clip; a duck that lifted between them would flicker four
   *     soundtracks back in for the half-second it takes to load the next;
   *   · it is an OVERRIDE, not a state change. Mutes and the mic are the
   *     player's, and the room borrowing the volume must not spend them;
   *   · it CANNOT STRAND. A flag left up after a mercy, a recap or a bed that
   *     died on an empty library would silence the next match's windows with no
   *     symptom to trace it by, so every door out is pinned separately.
   * ------------------------------------------------------------------------ */
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const host = byId.get('gg-fx-vwin');
    const stageLayer = byId.get('gg-stage');
    const V = await import('../exec/videos.js');

    // ---- the resolver first, pure: a THIRD argument, and every old call intact
    const pool = (k, muted = []) => Array.from({ length: k }, (_x, i) => ({ wid: i + 1, muted: muted.includes(i + 1) }));
    ok(V.focusedIndexOf(pool(3), 2) === 1 && V.focusedIndexOf(pool(3)) === 2,
      'the two-argument resolver is bit-for-bit what it was — every focus pin above still means what it said',
      `${V.focusedIndexOf(pool(3), 2)} / ${V.focusedIndexOf(pool(3))}`);
    ok(V.focusedIndexOf(pool(3), 2, true) === -1,
      'ducked, NOBODY speaks — whoever holds the mic, however many are up',
      String(V.focusedIndexOf(pool(3), 2, true)));
    ok(V.focusedIndexOf(pool(3), 2, false) === 1 && V.focusedIndexOf(pool(3), 2, undefined) === 1,
      'and false/undefined are the OLD answer, not a new one: the argument is additive',
      `${V.focusedIndexOf(pool(3), 2, false)} / ${V.focusedIndexOf(pool(3), 2, undefined)}`);
    ok(V.focusedIndexOf(pool(3, [2]), 2, true) === -1 && V.focusedIndexOf(pool(3, [2]), 2) === -1,
      'a muted focus is silence either way — the duck overrules who speaks, never what the player set');
    ok(V.audioTargets([0.5, 0.5, 0.5], V.focusedIndexOf(pool(3), 2, true)).every((x) => x === 0),
      'so audioTargets aims the whole pool at zero, and the duck needs no fade code of its own',
      JSON.stringify(V.audioTargets([0.5, 0.5, 0.5], V.focusedIndexOf(pool(3), 2, true))));

    // ---- and now the wiring, on real nodes
    const vids = V.createVideos({ layers, media: fakeMedia(), logger: quiet });
    const A = V.volumeFor(0.5);
    const vidOf = (nd) => nd.findAll('gg-vwin-vid')[0];
    const spawn = (id) => {
      vids.renderPayload({ id, duration_ms: 40000, intensity: 0.5 }, () => {});
      const all = liveWins(host);
      return all[all.length - 1];
    };
    const bedVid = () => stageLayer.findAll('gg-vid')[0];
    const anyLive = () => liveWins(host).some((w) => w._cls.has('is-live'));
    skipOn(false);

    const a = spawn('pD1'), b = spawn('pD2');
    tap(a, 300, 300);                                  // one window the PLAYER silenced
    ok(vids.elementAudioActive() === false,
      'a thrown payload video is a WINDOW, not a bed: renderPayload never ducks the pool',
      String(vids.elementAudioActive()));
    ok(vids.focusedIndex() === 1 && JSON.stringify(vids.mutedFlags()) === '[true,false]',
      'the newest speaks, the muted one does not', `${vids.focusedIndex()} / ${JSON.stringify(vids.mutedFlags())}`);
    await sleep(560);
    ok(Math.abs(vidOf(b).volume - A) < 1e-9 && vidOf(a).volume === 0,
      'and the room is genuinely audible before the bed arrives', `${vidOf(a).volume} / ${vidOf(b).volume}`);

    // ---- THE BED GOES UP
    vids.start({ element: GoonElement.Videos, intensity: 0.6, durationMs: 0, elapsedMs: 0 });
    ok(!!bedVid() && vids.elementAudioActive() === true,
      'the ramp\'s mandatory video mounts on the stage and takes the room with it',
      `${!!bedVid()} / ${vids.elementAudioActive()}`);
    ok(vids.liveIndex() === -1, 'nothing in the pool is audible any more', String(vids.liveIndex()));
    ok(vids.focusedIndex() === 1 && JSON.stringify(vids.mutedFlags()) === '[true,false]',
      'while UNDERNEATH, the mic and the player\'s mute are untouched — the room borrowed the volume, it did not spend the state',
      `${vids.focusedIndex()} / ${JSON.stringify(vids.mutedFlags())}`);
    ok(!anyLive(),
      'and no window wears .is-live while it is ducked: the pulsing dot means AUDIBLE, so it must go out too',
      liveWins(host).map((w) => w.className).join(' | '));
    await sleep(120);
    ok(vidOf(b).volume > 0 && vidOf(b).volume < A,
      'it FADES down the existing 300ms ramp — mid-flight here, not cut', `${vidOf(b).volume} of ${A}`);
    await sleep(400);
    ok(liveWins(host).every((nd) => vidOf(nd).volume === 0 && vidOf(nd).muted === true),
      'and lands at zero, muted, every one of them',
      liveWins(host).map((nd) => vidOf(nd).volume).join(','));
    ok(bedVid().volume === V.volumeFor(0.6),
      'the mandatory video itself is untouched — it is the one thing in the room that IS meant to be heard',
      `${bedVid().volume} / ${V.volumeFor(0.6)}`);

    // ---- CHAINING A CLIP IS NOT A NEW BED. The element plays one clip after
    // another for as long as the ramp holds it; the duck must not flap with them.
    const before = bedVid();
    before.onended();
    ok(vids.elementAudioActive() === true && vids.liveIndex() === -1,
      'the bed chaining its next clip does NOT lift the duck for the gap between them',
      `${vids.elementAudioActive()} / ${vids.liveIndex()}`);
    await sleep(120);
    ok(liveWins(host).every((nd) => vidOf(nd).volume === 0),
      'nothing crept back in on the handover', liveWins(host).map((nd) => vidOf(nd).volume).join(','));

    // ---- A WINDOW THAT ARRIVES MID-BED is born into the duck, mic and all
    const c = spawn('pD3');
    ok(vids.focusedIndex() === 2,
      'it takes the mic exactly like any arrival — spawning is still the loudest touch there is',
      String(vids.focusedIndex()));
    ok(vids.liveIndex() === -1 && !anyLive(), 'but it holds a mic nobody can hear yet', String(vids.liveIndex()));
    await sleep(560);
    ok(vidOf(c).volume === 0 && vidOf(c).muted === true,
      'and it stays silent for as long as the bed does', `${vidOf(c).volume} muted=${vidOf(c).muted}`);
    ok(vids.windowCount() === 3, 'all three of them up, none of them audible', String(vids.windowCount()));

    // ---- THE BED COMES DOWN and the room is handed straight back
    vids.stop();
    ok(vids.elementAudioActive() === false, 'the duck lifts with the bed', String(vids.elementAudioActive()));
    ok(vids.liveIndex() === 2 && vids.liveIndex() === vids.focusedIndex(),
      'and the mic it was holding all along is the one that speaks — the window spawned MID-DUCK, as promised',
      `${vids.liveIndex()} / ${vids.focusedIndex()}`);
    ok(JSON.stringify(vids.mutedFlags()) === '[true,false,false]',
      'the player\'s click-mute survived the whole thing', JSON.stringify(vids.mutedFlags()));
    ok(c._cls.has('is-live') && !a._cls.has('is-live') && !b._cls.has('is-live'),
      'the live light comes back on exactly one window', c.className);
    await sleep(150);
    ok(vidOf(c).volume > 0 && vidOf(c).volume < A,
      'it RAMPS back in on the ordinary 500ms rise, it does not snap', `${vidOf(c).volume} of ${A}`);
    await sleep(450);
    ok(Math.abs(vidOf(c).volume - A) < 1e-9 && vidOf(a).volume === 0 && vidOf(b).volume === 0,
      'and lands at its own level, with the muted one still muted and the rest still quiet',
      `${vidOf(a).volume} / ${vidOf(b).volume} / ${vidOf(c).volume}`);

    // ---- TEARDOWN RESETS IT. A stranded duck is the one bug here with no
    // symptom you could trace: the NEXT match's windows would simply never speak.
    vids.start({ element: GoonElement.Videos, intensity: 0.6, durationMs: 0, elapsedMs: 0 });
    ok(vids.elementAudioActive() === true && vids.liveIndex() === -1, 'bed up again, room ducked again');
    ok(vids.stopWindows() === 3, 'mercy sweeps the pool', String(vids.windowCount()));
    ok(vids.elementAudioActive() === false && vids.windowCount() === 0,
      'and takes the BED with it: the recap inherits an empty layer and an un-ducked renderer',
      `${vids.elementAudioActive()} / ${vids.windowCount()}`);
    const d = spawn('pD4');
    await sleep(560);
    ok(Math.abs(vidOf(d).volume - A) < 1e-9 && d._cls.has('is-live') && vids.liveIndex() === 0,
      'so the very next window speaks — which is the whole reason teardown is pinned separately',
      `${vidOf(d).volume} / ${vids.liveIndex()}`);
    vids.stopWindows();
    await sleep(GHOST_WAIT);

    // ---- A BED THAT DIES ON AN EMPTY LIBRARY never stands the duck up and
    // walks away from it: begin() settles synchronously, inside start().
    const dead = V.createVideos({
      layers, logger: quiet,
      media: { drawKind: () => null, draw: () => null, acquire: () => null, hasMedia: () => false },
    });
    dead.start({ element: GoonElement.Videos, intensity: 0.6, durationMs: 0, elapsedMs: 0 });
    ok(dead.elementAudioActive() === false,
      'nothing playable, no bed, no duck — the flag is DERIVED from the live run, never set by hand',
      String(dead.elementAudioActive()));

    // ---- and the wire, not just the seam: the ramp's own element cue and the
    // executor's teardown are the two things that move this in a real match.
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const ex = createExecutor({ media: fakeMedia(), layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);
    const R = ex.rendererFor(GoonElement.Videos);
    R.renderPayload({ id: 'pD5', duration_ms: 40000, intensity: 0.5 }, () => {});
    m.emitStart({ element: GoonElement.Videos, intensity: 0.7, durationMs: 0, elapsedMs: 0 });
    ok(R.elementAudioActive() === true && R.liveIndex() === -1,
      'the shared ramp asking for the mandatory video is what ducks the pool in a real match',
      `${R.elementAudioActive()} / ${R.liveIndex()}`);
    m.emitStop({ element: GoonElement.Videos, intensity: 0, durationMs: 0, elapsedMs: 0 });
    ok(R.elementAudioActive() === false && R.liveIndex() === 0,
      'and the ramp letting it go hands the room back', `${R.elementAudioActive()} / ${R.liveIndex()}`);
    m.emitStart({ element: GoonElement.Videos, intensity: 0.7, durationMs: 0, elapsedMs: 0 });
    ok(R.elementAudioActive() === true, 'bed up once more, mid-match');
    ex.stopAll();
    ok(R.elementAudioActive() === false && R.windowCount() === 0,
      'MERCY takes the bed, the windows and the duck in one sweep — nothing survives into the recap',
      `${R.elementAudioActive()} / ${R.windowCount()}`);
    ex.detach();
    await sleep(GHOST_WAIT);
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
  }

  /* =========================================================================
   * P2P MEDIA TRANSFER — the RECEIVER side (spec §4.3).
   *
   * These run against the REAL exec/media.js pool rather than fakeMedia(),
   * because the whole property under test is the separation between the deck
   * (the player's own preset) and the received map (what a partner sent). A
   * fake that merged them would pass every check here and ship the bug.
   * ======================================================================= */

  /** 64 hex chars, deterministic per letter. Only a real sha is ever accepted. */
  const SHA = (c) => String(c).repeat(64).slice(0, 64);
  const SHA_IMG = SHA('a');
  const SHA_VID = SHA('b');
  const SHA_GONE = SHA('c');
  const SHA_BLOCKED = SHA('d');

  const ownManifest = () => ({
    images: Array.from({ length: 6 }, (_, i) => ({ name: 'own-img-' + i, url: 'https://ccp.assets/own/i' + i })),
    videos: Array.from({ length: 4 }, (_, i) => ({ name: 'own-vid-' + i, url: 'https://ccp.assets/own/v' + i })),
  });

  /** Run `fn` with a deterministic Math.random, so two decks are comparable. */
  function seeded(fn) {
    const orig = Math.random;
    let s = 987654321;
    Math.random = () => { s = (s * 1103515245 + 12345) & 0x7fffffff; return s / 0x7fffffff; };
    try { return fn(); } finally { Math.random = orig; }
  }

  // ------------------------------------------- drawFor: the resolution order
  {
    const { createGoonMediaPool } = await import('../exec/media.js');
    const pool = createGoonMediaPool();
    pool.setManifest(ownManifest());

    ok(pool.addReceived({ sha: SHA_IMG, kind: 'image', mime: 'image/webp', url: 'https://ccp.cache/recv/a.webp' }),
      'addReceived takes a well-formed artifact');
    ok(pool.addReceived({ sha: SHA_VID, kind: 'video', mime: 'video/mp4', url: 'https://ccp.cache/recv/b.mp4' }),
      '…of either kind');
    ok(pool.addReceived({ sha: 'not-a-sha', kind: 'image', url: 'x' }) === false,
      'and refuses anything whose id is not a sha — the id is the only thing that ever names a file');
    ok(pool.receivedCount() === 2, 'two artifacts held', String(pool.receivedCount()));

    // 1. tag + in the store -> the PEER's file, flagged as theirs.
    const hit = pool.drawFor('image', { tags: ['xfer:' + SHA_IMG] });
    ok(hit && hit.provenance === 'peer' && hit.url === 'https://ccp.cache/recv/a.webp',
      'a tag we hold resolves to the SENDER\'s artifact with provenance peer',
      hit ? `${hit.provenance} ${hit.url}` : 'null');
    ok(hit.acquire().provenance === 'peer', 'and the handle carries the provenance too, for the renderers');

    // 2. tag we never got -> our own library, exactly as today.
    const missed = pool.drawFor('image', { tags: ['xfer:' + SHA_GONE] });
    ok(missed && missed.provenance === 'local' && missed.url.startsWith('https://ccp.assets/'),
      'a tag that never landed falls back to our OWN library — the fallback IS the current code path',
      missed ? missed.url : 'null');

    // 3. kind mismatch -> SKIPPED, and the next tag still gets its turn.
    const skipped = pool.drawFor('image', { tags: ['xfer:' + SHA_VID, 'xfer:' + SHA_IMG] });
    ok(skipped && skipped.provenance === 'peer' && skipped.url.endsWith('a.webp'),
      'a video tag on an image draw is skipped, not stretched — the next tag is tried',
      skipped ? skipped.url : 'null');
    const onlyWrongKind = pool.drawFor('video', { tags: ['xfer:' + SHA_IMG] });
    ok(onlyWrongKind && onlyWrongKind.provenance === 'local',
      'and a tag list of nothing but the wrong kind lands on our own library');

    // 4. unknown namespaces are ignored, never guessed at.
    const foreign = pool.drawFor('image', { tags: ['lol:whatever', 42, null, 'xfer:' + SHA_IMG] });
    ok(foreign && foreign.provenance === 'peer', 'unrecognised tags are skipped without a throw');

    // 5. no tags at all -> today's line, unchanged.
    for (const p of [null, {}, { tags: null }, { tags: [] }]) {
      const own = pool.drawFor('image', p);
      ok(own && own.provenance === 'local', 'an untagged payload draws from our own library');
    }

    // 6. THE RENDER-TIME GATE. Blocked -> null -> the caller draws its own.
    pool.addReceived({ sha: SHA_BLOCKED, kind: 'image', mime: 'image/png', url: 'https://ccp.cache/recv/d.png' });
    let asked = 0;
    pool.attachBlocklist({
      knows: () => true,
      isBlocked: (sha) => { asked++; return sha === SHA_BLOCKED; },
    });
    ok(pool.acquireByTag('xfer:' + SHA_BLOCKED) === null,
      'acquireByTag returns null for a blocklisted sha — the gate that puts pixels on screen');
    ok(asked > 0, 'and it really asked the blocklist', String(asked));
    const blockedDraw = pool.drawFor('image', { tags: ['xfer:' + SHA_BLOCKED] });
    ok(blockedDraw && blockedDraw.provenance === 'local',
      'so a blocked tag renders the receiver\'s own library, as if the transfer had never landed');
    ok(pool.acquireByTag('xfer:' + SHA_IMG) !== null, 'an unblocked one still resolves');
    ok(pool.acquireByTag('nope:' + SHA_IMG) === null, 'and only the xfer: namespace is a tag at all');

    // 7. the deck can NEVER surface a peer artifact by itself.
    let peerFromDeck = 0;
    for (let i = 0; i < 200; i++) {
      const d = pool.draw();
      if (d && d.provenance !== 'local') peerFromDeck++;
    }
    ok(peerFromDeck === 0,
      'two hundred random draws never once surfaced the opponent\'s file — their media appears '
      + 'exactly where their payload asked for it and nowhere else', String(peerFromDeck));

    // 8. dropReceived is the blocklist sweep / user delete.
    ok(pool.dropReceived(SHA_IMG) === true && pool.hasReceived(SHA_IMG) === false,
      'dropReceived forgets it');
    ok(pool.drawFor('image', { tags: ['xfer:' + SHA_IMG] }).provenance === 'local',
      'and the tag falls back the moment it is gone');
  }

  /* --------------------------------------------- peekKind CONSUMES NOTHING
   * ui/throwPreview.js shows the player a thumbnail of what a payload is about
   * to play. If it got that thumbnail by DRAWING, it would spend the very draw
   * the effect was going to make and the preview would be a picture of a clip
   * that then never played — a bug the preview itself caused. So the peek is
   * asserted to leave the deck, the echo guard and the draw ORDER untouched. */
  {
    const { createGoonMediaPool } = await import('../exec/media.js');

    // Two identically seeded pools: one is peeked at hard, the other is not.
    const run = (peek) => seeded(() => {
      const p = createGoonMediaPool();
      p.setManifest(ownManifest());
      if (peek) for (let i = 0; i < 25; i++) { p.peekKind('video'); p.peekKind('image'); }
      const out = [];
      for (let i = 0; i < 10; i++) { const d = p.drawKind('video'); out.push(d ? d.name : '-'); }
      return out.join(',');
    });
    ok(run(true) === run(false),
      'peekKind leaves the deck EXACTLY as it found it — ten draws come out in the same order',
      run(true) + ' vs ' + run(false));

    const p = createGoonMediaPool();
    p.setManifest(ownManifest());
    const a = p.peekKind('video');
    const b = p.peekKind('video');
    ok(a && b && a.name === b.name && a.kind === 'video',
      'a peek is stable and of the kind asked for', a ? `${a.kind} ${a.name}` : 'null');
    // …and once the deck has actually been DEALT, the peek is the next draw.
    // (Before the first deal there is no deck to read, so the peek falls back to
    // pool order — representative, which is all it ever promises.)
    p.drawKind('video');
    ok(p.peekKind('video').name === p.drawKind('video').name,
      'on a dealt deck it really is what the next drawKind hands back, which is the point of previewing it');
    ok(p.peekKind('nonsense') === null && p.peekKind() === null, 'an unknown kind peeks at nothing');

    const empty = createGoonMediaPool();
    ok(empty.peekKind('image') === null, 'an empty pool peeks at nothing rather than throwing');
    empty.setLocalLibrary([{ kind: 'image', name: 'phone-pick', url: 'blob:x' }]);
    ok(empty.peekKind('image') && empty.peekKind('image').name === 'phone-pick',
      'a deck that has never been dealt still peeks — the standalone/phone path');
    ok(empty.peekKind('video') === null, 'and a kind the library has none of stays null');

    // A peer artifact must not leak out of this door either (rule: their media
    // appears where their payload asked for it and nowhere else).
    const peers = createGoonMediaPool();
    peers.setManifest(ownManifest());
    peers.addReceived({ sha: SHA_VID, kind: 'video', mime: 'video/mp4', url: 'https://ccp.cache/recv/b.mp4' });
    let leaked = 0;
    for (let i = 0; i < 50; i++) { const v = peers.peekKind('video'); if (v && v.provenance !== 'local') leaked++; }
    ok(leaked === 0, 'peekKind can never surface a received artifact', String(leaked));
  }

  /* ----------------------- GIF-ORIGIN CLIPS COME LAST IN THE VIDEO LANE
   *
   * A desktop gif is compressed into an mp4 to travel (the wire refuses a
   * kind/mime disagreement), so a Video payload could land a two-second loop
   * while real footage sat in the same received map — every layer working, and
   * the owner watching a gif. `drawReceived`/`peekReceived` prefer footage.
   *
   * IT IS A PREFERENCE, NOT A FILTER, and both halves are pinned below: with
   * footage present a gif is never drawn, and with ONLY gifs the lane still
   * draws one — because their gif still beats the receiver's own library, which
   * is the entire reason the transfer lane exists.
   */
  {
    const { createGoonMediaPool } = await import('../exec/media.js');
    const REAL_A = SHA('7');
    const REAL_B = SHA('8');
    const LOOP_A = SHA('9');
    const LOOP_B = SHA('0');

    const pool = createGoonMediaPool();
    pool.setManifest(ownManifest());
    pool.addReceived({ sha: LOOP_A, kind: 'video', mime: 'video/mp4', origin: 'gif', url: 'r/loopA.mp4' });
    pool.addReceived({ sha: LOOP_B, kind: 'video', mime: 'video/mp4', origin: 'gif', url: 'r/loopB.mp4' });
    pool.addReceived({ sha: REAL_A, kind: 'video', mime: 'video/mp4', url: 'r/realA.mp4' });
    pool.addReceived({ sha: REAL_B, kind: 'video', mime: 'video/mp4', origin: '', url: 'r/realB.mp4' });

    let loops = 0;
    for (let i = 0; i < 80; i++) {
      const v = pool.drawReceived('video');
      if (v && (v.sha === LOOP_A || v.sha === LOOP_B)) loops++;
    }
    ok(loops === 0, 'eighty video draws never hand back a gif-origin clip while real footage has landed',
      String(loops));

    let peeked = 0;
    for (let i = 0; i < 40; i++) {
      const v = pool.peekReceived('video');
      if (v && (v.sha === LOOP_A || v.sha === LOOP_B)) peeked++;
    }
    ok(peeked === 0,
      'and the throw PREVIEW draws from the same candidate set — or it would advertise a clip the '
      + 'render then refuses to play', String(peeked));

    // …and the demotion is only ever a demotion.
    const gifOnly = createGoonMediaPool();
    gifOnly.addReceived({ sha: LOOP_A, kind: 'video', mime: 'video/mp4', origin: 'gif', url: 'r/loopA.mp4' });
    gifOnly.addReceived({ sha: LOOP_B, kind: 'video', mime: 'video/mp4', origin: 'gif', url: 'r/loopB.mp4' });
    const fallback = gifOnly.drawReceived('video');
    ok(!!fallback && (fallback.sha === LOOP_A || fallback.sha === LOOP_B),
      'with ONLY gif-origin clips landed the lane still draws one — their gif beats our own library');
    ok(!!gifOnly.peekReceived('video'), 'the preview says the same thing');

    // An UNKNOWN origin (an old peer, a row primed from a previous session) is
    // footage: absence must never demote anything.
    const unknown = createGoonMediaPool();
    unknown.addReceived({ sha: REAL_A, kind: 'video', mime: 'video/mp4', url: 'r/realA.mp4' });
    unknown.addReceived({ sha: LOOP_A, kind: 'video', mime: 'video/mp4', origin: 'gif', url: 'r/loopA.mp4' });
    let unknownLoops = 0;
    for (let i = 0; i < 40; i++) if (unknown.drawReceived('video').sha === LOOP_A) unknownLoops++;
    ok(unknownLoops === 0, 'a row with NO origin field outranks a known gif — absent reads as footage',
      String(unknownLoops));

    // The IMAGE lane has no opinion: a gif is exactly what a gif should be there.
    const flashes = createGoonMediaPool();
    flashes.addReceived({ sha: SHA('a'), kind: 'image', mime: 'image/gif', origin: 'gif', url: 'r/a.gif' });
    flashes.addReceived({ sha: SHA('b'), kind: 'image', mime: 'image/png', url: 'r/b.png' });
    const seenImg = new Set();
    for (let i = 0; i < 60; i++) seenImg.add(flashes.drawReceived('image').sha);
    ok(seenImg.size === 2, 'the FlashBurst lane keeps drawing both — the preference is video-only',
      String(seenImg.size));

    // A tag is an EXACT instruction and outranks taste: the sender already
    // applied the same preference when it chose which tag to send.
    ok(pool.drawFor('video', { tags: ['xfer:' + LOOP_A] }).sha === LOOP_A,
      'an explicit xfer tag for a gif-origin clip still resolves to exactly that clip');
  }

  // -------------------------------- addReceived does not disturb the deck
  {
    const { createGoonMediaPool } = await import('../exec/media.js');
    const m = ownManifest();

    const clean = seeded(() => {
      const p = createGoonMediaPool();
      p.setManifest(m);
      return Array.from({ length: 14 }, () => p.draw().name);
    });
    const interrupted = seeded(() => {
      const p = createGoonMediaPool();
      p.setManifest(m);
      const out = [];
      for (let i = 0; i < 14; i++) {
        // Artifacts land MID-MATCH, between draws. That must be a Map write and
        // nothing else: no reshuffle, no disturbance of the 8-draw echo guard.
        if (i === 3 || i === 7) {
          p.addReceived({ sha: SHA(i === 3 ? 'e' : 'f'), kind: 'image', mime: 'image/png', url: 'u' + i });
        }
        out.push(p.draw().name);
      }
      return out;
    });
    ok(clean.join('|') === interrupted.join('|'),
      'the draw sequence is byte-identical either side of two addReceived calls',
      `${clean.join(',')} vs ${interrupted.join(',')}`);

    // …and setManifest's hard deck reset cannot destroy them.
    const p = createGoonMediaPool();
    p.setManifest(m);
    p.addReceived({ sha: SHA_VID, kind: 'video', mime: 'video/mp4', url: 'https://ccp.cache/recv/b.mp4' });
    const counts = p.setManifest({ images: [{ name: 'new', url: 'https://ccp.assets/new' }], videos: [] });
    ok(counts.images === 1 && counts.videos === 0, 'the deck really was re-dealt');
    ok(p.hasReceived(SHA_VID) === true,
      'setManifest re-deals the DECK and leaves the received map alone (trap register #5)');
    ok(p.drawFor('video', { tags: ['xfer:' + SHA_VID] }).provenance === 'peer',
      'the tag still resolves after a preset change, even though there is no local video at all');
  }

  /* -------------------------------------------------------------------------
   * THE LOCAL LIBRARY — standalone playback (the phone bug, 2026-08-04).
   *
   * In a browser there is no host: bridge.js synthesizes an EMPTY manifest, and
   * the files the player picks on the assets screen used to reach the SEND path
   * only. So the deck stayed empty and a practice session fired every effect
   * against nothing. setLocalLibrary is the other half of the deck, and these
   * checks pin BOTH directions of the independence that makes it safe.
   * ---------------------------------------------------------------------- */
  {
    const { createGoonMediaPool } = await import('../exec/media.js');

    // 1. The bug, reproduced: standalone boots on an empty manifest.
    const pool = createGoonMediaPool();
    pool.setManifest({ images: [], videos: [], skipped: 0, truncated: false });
    ok(pool.hasMedia() === false && pool.drawKind('image') === null,
      'standalone starts on the empty synthesized manifest — an empty deck (this WAS the bug)');

    // 2. The picks land, kind-first. Blob URLs carry NO extension, so the kind
    //    the store decided is the only thing that can classify them.
    const locals = [
      { kind: 'image', name: 'pic-0.jpg', url: 'blob:http://localhost/aaa' },
      { kind: 'image', name: 'pic-1.png', url: 'blob:http://localhost/bbb' },
      { kind: 'image', name: 'pic-2.gif', url: 'blob:http://localhost/ccc' },
      { kind: 'video', name: 'clip-0.mp4', url: 'blob:http://localhost/ddd' },
      { kind: 'video', name: 'clip-1.mp4', url: 'blob:http://localhost/eee' },
    ];
    const c = pool.setLocalLibrary(locals);
    ok(c.images === 3 && c.videos === 2, 'setLocalLibrary puts the picks in the deck',
      c.images + 'i/' + c.videos + 'v');
    ok(pool.hasMedia() === true && pool.localCount() === 5,
      'the pool has media now — a solo session draws from the player\'s own picks');

    const img = pool.drawKind('image');
    const vid = pool.drawKind('video');
    const vidUrls = new Set(['blob:http://localhost/ddd', 'blob:http://localhost/eee']);
    ok(img && img.url.startsWith('blob:') && img.kind === 'image' && img.provenance === 'local',
      'drawKind("image") hands back one of the picked stills, as local provenance',
      img ? img.url : 'null');
    ok(vid && vidUrls.has(vid.url), 'and drawKind("video") one of the picked clips', vid ? vid.url : 'null');
    ok(pool.acquire(img).url === img.url, 'acquire hands the blob URL straight through…');
    ok(typeof pool.acquire(img).release === 'function',
      '…with a no-op release: the assets store owns those object URLs, not the pool');

    // 3. Kinds never cross. A video pick must never render as a flash.
    let crossed = 0;
    for (let i = 0; i < 120; i++) {
      const d = pool.drawKind('image');
      if (!d || vidUrls.has(d.url)) crossed++;
    }
    ok(crossed === 0, 'one hundred and twenty image draws never surfaced a video pick', String(crossed));

    // 4. Junk is skipped, never pushed as an entry that can only fail to load.
    const c2 = pool.setLocalLibrary(locals.concat([
      null,
      { kind: 'image', name: 'no-url' },
      { kind: 'audio', name: 'wrong-kind.mp3', url: 'blob:http://localhost/fff' },
    ]));
    ok(c2.images === 3 && c2.videos === 2, 'a null, a URL-less row and an unknown kind are all dropped',
      c2.images + 'i/' + c2.videos + 'v');

    // 5. Removing every pick empties the local half — and only that half.
    ok(pool.setLocalLibrary([]).images === 0 && pool.localCount() === 0,
      'clearing the library clears the deck when there is no host manifest behind it');
  }

  /* -------------------------------------------------------------------------
   * …AND THE RENDERERS ACTUALLY PUT IT ON SCREEN (round 4, 2026-08-04).
   *
   * The block above proves the DECK. The owner's SECOND report — same phone,
   * same practice session, still "not triggering any asset" — is about the next
   * seam along: the element ramp starts Flashes at t=0 and the renderer draws off
   * that deck, so "the pool holds 3 images" and "the player sees an image" are
   * two different claims and only the first was ever pinned. This drives the real
   * renderer over the real pool and reads the URLs back off the layer.
   * ---------------------------------------------------------------------- */
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const { createGoonMediaPool } = await import('../exec/media.js');
    const pool = createGoonMediaPool();
    pool.setManifest({ images: [], videos: [], skipped: 0, truncated: false });   // standalone
    const picks = [
      { kind: 'image', name: 'a.png', url: 'blob:phone/a' },
      { kind: 'image', name: 'b.jpg', url: 'blob:phone/b' },
      { kind: 'video', name: 'c.mp4', url: 'blob:phone/c' },
    ];

    const ex = createExecutor({ media: pool, layers, logger: quiet });
    const m = fakeMatch();
    ex.attach(m);

    // The ramp's FIRST cue, fired against an EMPTY deck: nothing to draw, and
    // that is the state the phone was stuck in. It must not throw and must not
    // LATCH — the bed keeps beating, so the next beat picks the library up.
    m.emitStart({ element: GoonElement.Flashes, intensity: 0.9, durationMs: 60000, elapsedMs: 0 });
    await sleep(150);
    const flashHost = byId.get('gg-fx-flash');
    ok(flashHost.findAll('gg-flash').length === 0,
      'an empty deck renders nothing and throws nothing — the effect ran, the screen stayed blank (the report)');

    // The player's picks arrive mid-run: they came back from the assets screen.
    pool.setLocalLibrary(picks);
    await sleep(2600);
    const shown = flashHost.findAll('gg-flash').map((el) => el.src);
    ok(shown.length > 0, 'once the library lands the SAME running bed starts drawing — no restart needed',
      String(shown.length));
    ok(shown.length > 0 && shown.every((u) => u === 'blob:phone/a' || u === 'blob:phone/b'),
      'and every flash on the layer is one of the player\'s own picks', shown.slice(0, 4).join(','));
    ok(shown.indexOf('blob:phone/c') < 0,
      'never the clip: a video pick can never be rendered as a flash');
    m.emitStop({ element: GoonElement.Flashes });

    // The floating windows are the OTHER media-carrying renderer, asking for the
    // other kind. spawnLocal is the popped-bubble path, i.e. the one a practice
    // player reaches with no arsenal slot unlocked at all.
    const vids = ex.rendererFor(GoonElement.Videos);
    ok(vids.spawnLocal({ duration_ms: 4000, intensity: 0.5 }) === true,
      'and the video renderer spawns a local window off the same library');
    ok(byId.get('gg-fx-vwin').findAll('gg-vwin').length === 1, 'one window on the layer');

    ex.stopAll();
    ex.detach();
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
  }

  // ------------------- the two halves of the deck are INDEPENDENT (hosted safety)
  {
    const { createGoonMediaPool } = await import('../exec/media.js');
    const pool = createGoonMediaPool();
    const hosted = pool.setManifest(ownManifest());
    ok(hosted.images === 6 && hosted.videos === 4, 'the hosted manifest flow is untouched',
      hosted.images + 'i/' + hosted.videos + 'v');

    // Hosted, nobody ever picks a file — an empty local half changes nothing.
    const still = pool.setLocalLibrary([]);
    ok(still.images === 6 && still.videos === 4,
      'an empty local library leaves the host\'s preset exactly as it was',
      still.images + 'i/' + still.videos + 'v');
    let offPreset = 0;
    for (let i = 0; i < 60; i++) {
      const d = pool.draw();
      if (!d || !d.url.startsWith('https://ccp.assets/')) offPreset++;
    }
    ok(offPreset === 0, 'and sixty draws all come off the host manifest', String(offPreset));

    // Defensive: if both ever coexist, they ADD — neither setter clobbers the other.
    const both = pool.setLocalLibrary([{ kind: 'image', name: 'p.jpg', url: 'blob:x/1' }]);
    ok(both.images === 7 && both.videos === 4, 'a pick alongside a host manifest adds to the deck',
      both.images + 'i/' + both.videos + 'v');
    const relisted = pool.setManifest({ images: [{ name: 'only', url: 'https://ccp.assets/only' }], videos: [] });
    ok(relisted.images === 2 && relisted.videos === 0,
      'a NEW manifest re-deals the host half and KEEPS the player\'s picks (2 = 1 host + 1 local)',
      relisted.images + 'i/' + relisted.videos + 'v');
    ok(pool.localCount() === 1, 'the local half really survived the manifest reset', String(pool.localCount()));

    // …and the received map is none of setLocalLibrary's business either.
    pool.addReceived({ sha: SHA_VID, kind: 'video', mime: 'video/mp4', url: 'https://ccp.cache/recv/b.mp4' });
    pool.setLocalLibrary([{ kind: 'image', name: 'q.jpg', url: 'blob:x/2' }]);
    ok(pool.hasReceived(SHA_VID) === true,
      'setLocalLibrary re-deals the DECK and leaves the received map alone, exactly like setManifest');
    let peerFromDeck = 0;
    for (let i = 0; i < 120; i++) {
      const d = pool.draw();
      if (d && d.provenance !== 'local') peerFromDeck++;
    }
    ok(peerFromDeck === 0, 'and a local pick still cannot make the deck surface a peer artifact', String(peerFromDeck));
  }

  // ------------------------------------ videos: the payload path, and only it
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const host = byId.get('gg-fx-vwin');
    const { createGoonMediaPool } = await import('../exec/media.js');
    const pool = createGoonMediaPool();
    pool.setManifest(ownManifest());
    pool.addReceived({ sha: SHA_VID, kind: 'video', mime: 'video/mp4', url: 'https://ccp.cache/recv/b.mp4' });

    const ex = createExecutor({ media: pool, layers, logger: quiet, toyBridge: null });
    const m = fakeMatch();
    ex.attach(m);
    const R = ex.rendererFor(GoonElement.Videos);

    R.renderPayload({ id: 'pT1', duration_ms: 40000, intensity: 0.5, tags: ['xfer:' + SHA_VID] }, () => {});
    const win = liveWins(host)[0];
    const vid = win && win.findAll('gg-vwin-vid')[0];
    ok(!!vid && vid.src === 'https://ccp.cache/recv/b.mp4',
      'a Video payload carrying an xfer tag opens the window on the SENDER\'s clip',
      vid ? vid.src : 'no video');

    // A dud peer file must not loop: reSource redraws from OUR OWN library.
    vid.onerror();
    ok(vid.src.startsWith('https://ccp.assets/'),
      'and a peer clip that fails to decode re-sources from the receiver\'s own library — '
      + 'never from the same tag, or a dud file would loop until MAX_SOURCE_TRIES',
      vid.src);

    R.stop();
    ex.stopAll();
    await sleep(GHOST_WAIT);
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();

    // An untagged payload is still PEER-FIRST (2026-08-05: "the attacks are a
    // transfer, every time"): any clip the opponent already transferred this
    // match beats the receiver's own library. Only an empty received store
    // reads as the old behaviour — covered by the drawFor suite above.
    R.renderPayload({ id: 'pT2', duration_ms: 40000, intensity: 0.5 }, () => {});
    const plainVid = liveWins(host)[0].findAll('gg-vwin-vid')[0];
    ok(plainVid.src === 'https://ccp.cache/recv/b.mp4',
      'an untagged Video payload still prefers a clip the opponent transferred this match', plainVid.src);
    ex.stopAll();
    ex.detach();
    await sleep(GHOST_WAIT);
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
  }

  // ------------- flashes: exact tags first, then the received store, own last
  {
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const host = byId.get('gg-fx-flash');
    const F = await import('../exec/flashes.js');
    const { createGoonMediaPool } = await import('../exec/media.js');
    const pool = createGoonMediaPool();
    pool.setManifest(ownManifest());
    const tagShas = ['1', '2', '3', '4'].map((c) => SHA(c));
    tagShas.forEach((sha, i) => pool.addReceived({
      sha, kind: 'image', mime: 'image/png', url: 'https://ccp.cache/recv/t' + i + '.png',
    }));

    ok(F.XFER_TAGS_MAX === 3,
      'exec/flashes.js mirrors the protocol\'s XFER_TAGS_MAX locally (exec/ never imports net/)',
      String(F.XFER_TAGS_MAX));

    const ex = createExecutor({ media: pool, layers, logger: quiet, toyBridge: null });
    ex.attach(fakeMatch());
    const R = ex.rendererFor(GoonElement.Flashes);

    // FOUR tags offered; the cap is three EXACT spends, and past them the burst
    // keeps drawing the sender's files from the received store — the receiver's
    // own library is the last resort, not the rest of the squall (2026-08-05
    // play-test: "one desktop gif then local-only" read as a broken transfer).
    const cancel = R.renderPayload({
      id: 'pF1', duration_ms: 4000, intensity: 0.8,
      tags: tagShas.map((s) => 'xfer:' + s),
    }, () => {});
    await sleep(900);
    const srcs = host.findAll('gg-flash').map((el) => el.src || '');
    const peer = srcs.filter((s) => s.startsWith('https://ccp.cache/recv/'));
    const own = srcs.filter((s) => s.startsWith('https://ccp.assets/'));
    ok(peer.length > 3,
      'the burst stays on the SENDER\'s images past the three exact tag spends — '
      + 'the received store feeds the rest of the squall',
      `${peer.length} peer / ${own.length} own`);
    for (const i of [0, 1, 2]) {
      ok(peer.includes('https://ccp.cache/recv/t' + i + '.png'),
        `exact tag ${i} was spent on its own flash before the random received draws`, peer.join(','));
    }
    ok(own.length === 0,
      'the receiver\'s own library never shows while received artifacts exist', String(own.length));
    cancel();
    ex.stopAll();
    ex.detach();
    await sleep(60);
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
  }

  /* ------------------------------------------------- the received store, hosted
   *
   * The disk backend talks to TransferInboxStore through five verbs that ALL reply
   * with the same `goon-recv-result` shape carrying the same job id — so the
   * correlation is a per-id FIFO, not a map lookup. This fake host is the shipped
   * one's contract: replies in request order, seq-gapless, and a commit that can
   * refuse with the DtrhLoomStore vocabulary.
   */
  {
    const { createReceivedStore, CHUNK_BYTES } = await import('../exec/receivedStore.js');

    function fakeHost({ commitError = null, beginError = null } = {}) {
      const sent = [];
      let onResult = null;
      const bridge = {
        isHosted: true,
        on(type, fn) { if (type === 'goon-recv-result') onResult = fn; },
        send(msg) {
          sent.push(msg);
          const reply = (o) => Promise.resolve().then(() => onResult && onResult(Object.assign({ id: msg.id }, o)));
          if (msg.type === 'goon-recv-begin') reply({ ok: !beginError, error: beginError });
          else if (msg.type === 'goon-recv-chunk') reply({ ok: true });
          else if (msg.type === 'goon-recv-commit') {
            reply(commitError
              ? { ok: false, error: commitError }
              : { ok: true, url: 'https://ccp.cache/recv/' + msg.sha256 + '.mp4', bytes: 999 });
          } else reply({ ok: true });
        },
      };
      return { bridge, sent };
    }

    const sha = SHA('9');
    const payloadBytes = Math.round(CHUNK_BYTES * 2.5);
    const body = new Uint8Array(payloadBytes);
    for (let i = 0; i < body.length; i++) body[i] = i & 0xff;

    {
      const host = fakeHost();
      const store = createReceivedStore({ bridge: host.bridge, hosted: true, logger: quiet });
      ok(store.backend === 'host', 'hosted means the disk backend', store.backend);
      ok(store.has(sha) === false && store.partialLength(sha) === 0, 'nothing held yet');
      ok(store.begin(sha, 'video/mp4', payloadBytes) === true, 'begin opens the job');
      ok(store.begin(sha, 'video/mp4', payloadBytes) === true,
        'and begin is IDEMPOTENT — a resumed transfer calls it again and must keep the partial');
      ok(store.begin(sha, 'application/pdf', 10) === false,
        'the mime allowlist is checked BEFORE the resume shortcut, so a live job cannot be '
        + 'reopened as something we do not render');
      ok(store.partialLength(sha) === 0, 'and the refusal left the partial exactly where it was');
      ok(store.begin(SHA('8'), 'application/pdf', 10) === false, 'a new job with a bad mime is refused too');
      ok(store.begin(sha, 'video/mp4', -1) === false, 'as is a nonsense length');

      // Feed it in protocol-sized pieces; the store re-chunks to CHUNK_BYTES itself.
      const STEP = 16376;
      let at = 0;
      let allWrote = true;
      while (at < payloadBytes) {
        const len = Math.min(STEP, payloadBytes - at);
        if (!store.write(sha, at, body.buffer.slice(at, at + len))) allWrote = false;
        at += len;
      }
      ok(allWrote === true, 'every in-order write was taken');
      ok(store.write(sha, 0, body.buffer.slice(0, 8)) === false,
        'and a write at the wrong offset is refused — write appends at exactly `offset`, never seeks');
      ok(store.partialLength(sha) === payloadBytes, 'partialLength is the resume offset',
        String(store.partialLength(sha)));

      const res = await store.commit(sha);
      ok(res.ok === true && res.url.includes(sha), 'commit lands with the host\'s virtual-host URL', res.url);
      ok(store.has(sha) === true, 'and the artifact is now held');
      ok(store.thisMatch().length === 1, 'and listed as landed THIS session (the report card\'s shortlist)');

      const chunks = host.sent.filter((m) => m.type === 'goon-recv-chunk');
      ok(chunks.length === 3, 'the bytes went out as three chunks (2.5 x CHUNK_BYTES)', String(chunks.length));
      ok(chunks.every((c, i) => c.seq === i),
        'with GAPLESS sequence numbers from 0 — TransferInboxStore refuses anything else',
        chunks.map((c) => c.seq).join(','));
      ok(chunks.every((c) => c.b64.length <= 350000),
        'and every base64 body inside the host\'s MaxChunkB64Chars',
        String(Math.max(...chunks.map((c) => c.b64.length))));
      ok(chunks[0].b64 === Buffer.from(body.subarray(0, CHUNK_BYTES)).toString('base64'),
        'the encoding is the bytes, not a mangled copy');
      const order = host.sent.map((m) => m.type);
      ok(order[0] === 'goon-recv-begin' && order[order.length - 1] === 'goon-recv-commit',
        'begin first, commit last, chunks in between — which is what makes FIFO correlation exact',
        order.join(' '));

      store.drop(sha);
      ok(store.has(sha) === false, 'drop forgets it');
      ok(host.sent.some((m) => m.type === 'goon-recv-drop' && m.sha256 === sha),
        'and tells the host to delete the file');
      store.dispose();
    }

    // The error vocabulary the protocol maps onto xfer_fail.
    {
      const host = fakeHost({ commitError: 'hash-mismatch' });
      const store = createReceivedStore({ bridge: host.bridge, hosted: true, logger: quiet });
      store.begin(sha, 'video/mp4', 32);
      store.write(sha, 0, new Uint8Array(32).buffer);
      const res = await store.commit(sha);
      ok(res.ok === false && res.error === 'hash-mismatch',
        'a host-side hash mismatch comes back verbatim — the page\'s sha was only ever a claim', res.error);
      ok(store.has(sha) === false, 'and nothing was indexed');
      store.dispose();
    }
    {
      // A refused begin is async, so it surfaces where the protocol can act on it: commit.
      const host = fakeHost({ beginError: 'cap-reached' });
      const store = createReceivedStore({ bridge: host.bridge, hosted: true, logger: quiet });
      ok(store.begin(sha, 'image/png', 16) === true, 'begin is synchronous by contract and answers optimistically');
      store.write(sha, 0, new Uint8Array(16).buffer);
      const res = await store.commit(sha);
      ok(res.ok === false && res.error === 'cap-reached',
        'and a full inbox surfaces at commit instead of being swallowed', res.error);
      store.dispose();
    }

    // The manifest prime — a returning session's whole point.
    {
      const host = fakeHost();
      const store = createReceivedStore({ bridge: host.bridge, hosted: true, logger: quiet });
      const shas = store.primeReceived([
        { sha: SHA('7'), ext: 'mp4', mime: 'video/mp4', bytes: 1234 },
        { sha: 'garbage', ext: 'mp4', mime: 'video/mp4', bytes: 1 },
        { sha: SHA('6'), ext: 'exe', mime: 'application/x-msdownload', bytes: 1 },
      ]);
      ok(shas.length === 1 && shas[0] === SHA('7'),
        'primeReceived takes the good row and drops the bad name and the mime we do not render',
        shas.join(','));
      ok(store.has(SHA('7')) === true,
        'so the very first offer of a match can answer decline:have instead of re-transferring');
      const v = store.view(SHA('7'));
      ok(v && v.url === 'https://ccp.cache/recv/' + SHA('7') + '.mp4',
        'and the view is the virtual-host URL, which streams like every other asset', v && v.url);
      ok(typeof v.release === 'function', 'release() exists and is the no-op the disk backend wants');
      store.dispose();
    }
  }

  /* =========================================================================
   * THE SPIRAL BED ON A HOST THAT CAN ACTUALLY BAKE.
   *
   * Every spiral check above ran on the bare stub, which has no 2D context —
   * the "cannot bake" host, where the renderer must write no background-image
   * at all. This block gives it one and pins the other half: a real pane gets
   * a GENERATED still, as a data URL, and it lands ONE TICK LATE on purpose
   * (~1600 path fills plus a PNG encode is not something to do inside the frame
   * a cue lands on).
   *
   * IT RUNS LAST, AND THAT IS DELIBERATE. exec/spiralGen.js caches one scratch
   * canvas per size for the life of the page; handing it a working 2D context
   * earlier would leave every later block able to bake, and the freeze-defence
   * block above depends on being the machine that cannot.
   * ======================================================================= */
  {
    const SP = await import('../exec/spiral.js');
    const realCreate = document.createElement;
    let encodes = 0;
    document.createElement = (tag) => {
      const el = realCreate(tag);
      if (String(tag).toLowerCase() === 'canvas') {
        el.width = 0; el.height = 0;
        el.getContext = (type) => (String(type) === '2d' ? {
          fillStyle: null,
          fillRect() {}, beginPath() {}, closePath() {}, lineTo() {}, arc() {}, fill() {},
          createRadialGradient: () => ({ addColorStop() {} }),
        } : null);
        el.toDataURL = () => { encodes++; return 'data:image/png;base64,SPIRAL'; };
      }
      return el;
    };

    const spiralLayer = byId.get('gg-fx-spiral');
    spiralLayer.replaceChildren();
    const sp = SP.createSpiral({ layers, media: fakeMedia(), audio: { sfx() {} }, logger: quiet });
    sp.start({ element: GoonElement.Spiral, intensity: 0.5, durationMs: 0, elapsedMs: 0 });
    const pane = spiralLayer.findAll('gg-spiral')[0] || null;
    ok(!!pane, 'the bed mounts on a bakeable host too');
    const bgAtOnce = pane ? pane.style.getPropertyValue('background-image') : '(no pane)';
    ok(!String(bgAtOnce).trim(),
      'the bake does NOT happen inside the cue‘s own frame — the pane fades in over 450ms, the encode can wait a tick',
      bgAtOnce);
    await sleep(30);
    const bg = pane ? pane.style.getPropertyValue('background-image') : '(no pane)';
    ok(/^url\("data:image\/png;base64,/.test(String(bg)),
      'a tick later the pane is wearing a GENERATED still — no file, no network, no bundled asset', String(bg));
    ok(encodes >= 1, 'and it came from a real canvas encode', String(encodes));

    // Rotation: a second cue must swap in a DIFFERENT variant, and the pane's
    // spin must follow it (each roll carries its own solved revolution).
    const firstSpin = pane.style.getPropertyValue('--gg-spiral-spin');
    let spins = new Set([firstSpin]);
    for (let i = 0; i < 8; i++) {
      sp.stop();
      sp.start({ element: GoonElement.Spiral, intensity: 0.5, durationMs: 0, elapsedMs: 0 });
      const p = spiralLayer.findAll('gg-spiral').slice(-1)[0];
      if (p) spins.add(p.style.getPropertyValue('--gg-spiral-spin'));
    }
    ok(spins.size > 1, 'consecutive cues rotate through the session‘s variants, they do not re-show one',
      Array.from(spins).join(' '));
    // One check, not one per spin: the set's size is a roll of the dice and a
    // suite whose CHECK COUNT moves between runs is a suite nobody can diff.
    const outOfBand = Array.from(spins).filter((s) => !(parseFloat(s) >= 10 && parseFloat(s) <= 18));
    ok(outOfBand.length === 0, 'and every one of them spins inside the 10-18s band', outOfBand.join(' '));
    sp.stop();
    document.createElement = realCreate;
  }

  /* --------- THE DEVICE PERFORMANCE TIER (exec/perfTier.js, 2026-08-05)
   * The owner's iPhone dropped frames in a live duel, so the renderers grew a
   * second, smaller set of caps behind <html data-gg-perf="lite">. What is
   * pinned here is the DECISION TABLE and the polarity, because both are
   * contracts other files lean on:
   *   · a FINE pointer can never resolve lite — "desktop byte-identical" is a
   *     requirement, not a tendency, and no memory claim may override it;
   *   · ABSENT MEANS FULL — every check in this suite ran without the
   *     attribute, and that is exactly the guarantee that keeps them honest;
   *   · the explicit pref values beat detection, junk falls through to it.
   * ------------------------------------------------------------------------ */
  {
    const PT = await import('../exec/perfTier.js');
    const F = await import('../exec/flashes.js');
    const B = await import('../exec/bubbles.js');
    const V = await import('../exec/videos.js');
    const SP = await import('../exec/spiral.js');

    ok(F.MAX_LIVE_LITE === 10 && B.MAX_LIVE_LITE === 10 && V.MAX_WINDOWS_LITE === 3,
      'the lite caps are the shipped dials (flashes 10, bubbles 10, windows 3)',
      `${F.MAX_LIVE_LITE}/${B.MAX_LIVE_LITE}/${V.MAX_WINDOWS_LITE}`);
    ok(F.MAX_LIVE_LITE < F.MAX_LIVE && B.MAX_LIVE_LITE < B.MAX_LIVE && V.MAX_WINDOWS_LITE < V.MAX_WINDOWS,
      'and every one of them is strictly under its full-tier twin — a "lite" cap above full would be a typo, not a tier');
    ok(SP.LITE_MAX_DPR < SP.MAX_DPR && SP.LITE_FRAME_MS >= 30,
      'the spiral halves both of its dials: CSS-pixel backing and a ~30fps draw cadence',
      `${SP.LITE_MAX_DPR}/${SP.LITE_FRAME_MS}`);

    // ---- the decision table, pure
    ok(PT.detectPerfTier({ coarse: false, viewportMinPx: 400, deviceMemoryGb: 2 }) === PT.PERF_FULL,
      'a fine pointer is FULL whatever else the device claims — the desktop can never detect its way to lite');
    ok(PT.detectPerfTier({ coarse: true, viewportMinPx: 428 }) === PT.PERF_LITE,
      'coarse + phone-sized viewport is lite (the iPhone 13 Pro Max is 428)');
    ok(PT.detectPerfTier({ coarse: true, viewportMinPx: 1024 }) === PT.PERF_FULL,
      'coarse + a big tablet viewport stays full — an iPad Pro renders the full stack fine');
    ok(PT.detectPerfTier({ coarse: true, viewportMinPx: 1024, deviceMemoryGb: 4 }) === PT.PERF_LITE,
      'unless a low deviceMemory casts the second vote');
    ok(PT.detectPerfTier() === PT.PERF_FULL && PT.detectPerfTier({}) === PT.PERF_FULL,
      'and an empty/unreadable env is FULL: a missing signal must never degrade somebody’s picture');

    // ---- the pref resolution: explicit beats detection, junk falls through
    const phone = { coarse: true, viewportMinPx: 428 };
    ok(PT.resolvePerfTier('full', phone) === PT.PERF_FULL && PT.resolvePerfTier('lite', {}) === PT.PERF_LITE,
      'the explicit pref values overrule detection in BOTH directions');
    ok(PT.resolvePerfTier('auto', phone) === PT.PERF_LITE && PT.resolvePerfTier(undefined, phone) === PT.PERF_LITE
      && PT.resolvePerfTier(42, phone) === PT.PERF_LITE,
      'auto, undefined and junk all fall through to detection — a corrupt store can only land on "what this device deserves"');

    // ---- the attribute contract: absent means full, only the exact value bites
    ok(PT.perfLite() === false, 'no stamp = full tier — which is the tier this whole suite just ran under');
    documentElement.setAttribute(PT.PERF_ATTR, PT.PERF_LITE);
    ok(PT.perfLite() === true, 'the stamped lite value is read live off <html>');
    documentElement.setAttribute(PT.PERF_ATTR, 'sideways');
    ok(PT.perfLite() === false, 'anything that is not exactly "lite" is full — same polarity contract as data-gg-shader');
    documentElement.removeAttribute(PT.PERF_ATTR);
  }

  /* --------- SUSTAINED MOBILE LOAD, PASS 2 (2026-08-05)
   * The tier's caps counted NODES; the second play-test showed the real cost is
   * what a node DOES per frame — an animated GIF is a decode loop where a PNG
   * is a texture, and a flash burst of peer GIFs buried the phone the caps had
   * just saved. What is pinned here: the ANIMATION BUDGET (ANIM_LIVE_LITE and
   * the sniff it trusts), the still-preferred draw the washes lean on, the
   * lite burst's tempo-not-count dials, and the cross-effect load governor
   * (lite-gated, deadline-expiring, self-healing).
   * ------------------------------------------------------------------------ */
  {
    const PT = await import('../exec/perfTier.js');
    const F = await import('../exec/flashes.js');
    const SP = await import('../exec/spiral.js');
    const M = await import('../exec/media.js');
    const G = await import('../exec/loadGovernor.js');

    // ---- the new dials, pinned
    ok(F.ANIM_LIVE_LITE === 3 && F.ANIM_LIVE_LITE < F.MAX_LIVE_LITE,
      'the animation budget is 3 concurrent PLAYING gifs, strictly inside the lite node cap',
      `${F.ANIM_LIVE_LITE}/${F.MAX_LIVE_LITE}`);
    ok(F.BURST_GAP_SCALE === 0.5 && F.BURST_GAP_SCALE_LITE === 0.72,
      'the burst dials are the shipped pair (full halves the gap, lite tightens it to 0.72)',
      `${F.BURST_GAP_SCALE}/${F.BURST_GAP_SCALE_LITE}`);
    ok(F.BURST_GAP_SCALE_LITE > F.BURST_GAP_SCALE,
      'and the lite burst is strictly SLOWER-spawning than the full one — density by tempo, not node count');
    ok(SP.LITE_BURST_FRAME_MS > SP.LITE_FRAME_MS,
      'the spiral steps down further while a burst is on than it does at lite idle',
      `${SP.LITE_BURST_FRAME_MS} vs ${SP.LITE_FRAME_MS}`);
    ok(G.GOVERNOR_HOLD_MAX_MS >= 8000,
      'the governor cap covers the longest burst (8s) — a hold can never expire under its own payload',
      String(G.GOVERNOR_HOLD_MAX_MS));

    // ---- the animated-media sniff, pure. Generous by design: ambiguity votes
    // ANIMATED, because freezing a still is invisible and playing a GIF is not.
    ok(M.isAnimatedMedia({ mime: 'image/gif' }) === true, 'gif by mime is animated (peer artifacts always carry one)');
    ok(M.isAnimatedMedia({ name: 'loop.WEBP', url: 'blob:x' }) === true,
      'webp by NAME extension is animated — a local pick keeps its filename when its blob: URL has none');
    ok(M.isAnimatedMedia({ url: 'https://ccp.assets/images/a.gif?v=1' }) === true,
      'gif by URL extension is animated, query string and all (the host manifest path)');
    ok(M.isAnimatedMedia({ mime: 'image/png', name: 'still.png', url: 'https://ccp.assets/images/still.png' }) === false,
      'png/jpeg stay still — the budget must never charge a texture');
    ok(M.isAnimatedMedia(null) === false && M.isAnimatedMedia({}) === false,
      'null and fieldless entries read still: an unreadable signal must never spend the budget');

    // ---- drawStillImage: a preference, never a filter
    {
      const deal = (seq) => {
        let at = 0;
        return { drawKind: () => ({ kind: 'image', name: seq[Math.min(at, seq.length - 1)], url: 'blob:' + (at++) }) };
      };
      const mixed = deal(['a.gif', 'b.gif', 'c.png', 'd.gif']);
      const still = M.drawStillImage(mixed);
      ok(still && still.name === 'c.png',
        'drawStillImage redraws past the loops and settles on the still', still && still.name);
      const loops = deal(['a.gif', 'b.gif', 'c.gif', 'd.gif', 'e.gif']);
      const gif = M.drawStillImage(loops);
      ok(!!gif && /\.gif$/.test(gif.name),
        'an all-GIF library still gets its GIF — the preference is not allowed to become a missing wash',
        gif && gif.name);
      ok(M.drawStillImage(null) === null && M.drawStillImage({}) === null,
        'and a poolless caller gets null, exactly like drawKind would');
    }

    // ---- the load governor: lite-gated at the READ, expiring, self-healing
    ok(G.governorBusy() === false, 'the governor is idle until somebody holds it');
    const rel = G.governorHold(5000);
    ok(G.governorBusy() === false,
      'a hold on the FULL tier is inert — the desktop renders everything at once on purpose');
    documentElement.setAttribute(PT.PERF_ATTR, PT.PERF_LITE);
    ok(G.governorBusy() === true, 'the same hold reads busy the moment the lite tier is in force');
    rel();
    ok(G.governorBusy() === false, 'released = idle again');
    rel();
    ok(G.governorBusy() === false, 'and the release is idempotent (settle + cancel double-call is normal)');
    const rel2 = G.governorHold(30);
    ok(G.governorBusy() === true, 'a short hold is live…');
    await sleep(60);
    ok(G.governorBusy() === false,
      '…and reads idle past its own deadline even if never released — the degradation cannot stick');
    rel2();

    // ---- BEHAVIOUR: an all-animated pool on lite caps at the budget. The stub
    // has no Image and its canvases have no 2d context, so the freeze path
    // reports "cannot freeze" and over-budget spawns are SKIPPED — the
    // budget-respecting fallback, and the one this harness can observe.
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const flashHost = byId.get('gg-fx-flash');
    const gifMedia = {
      drawKind: (kind) => ({ kind, name: `${kind}-1.gif`, url: `https://ccp.assets/${kind}/1.gif` }),
      acquire: (e) => (e ? { url: e.url, release() {} } : null),
      hasMedia: () => true,
    };
    const liveShots = () => flashHost.findAll('gg-flash').filter((el) => !el._cls.has('is-popped'));
    const fl = F.createFlashes({ layers, media: gifMedia, logger: quiet });
    fl.start({ intensity: 1 });
    await sleep(1400);                       // two beats' worth of spawn attempts at i=1
    ok(liveShots().length > 0 && liveShots().length <= F.ANIM_LIVE_LITE,
      'a lite bed drawing only GIFs never exceeds the animation budget', String(liveShots().length));
    fl.stop();
    // The hydra spends the same budget: hammer the splits and it still holds.
    for (let round = 0; round < 4; round++) {
      for (const el of liveShots()) tap(el);
      await sleep(280);
    }
    ok(liveShots().length <= F.ANIM_LIVE_LITE,
      'and the hydra cannot split past it either', String(liveShots().length));

    // ---- while a STILL pool on lite still fills to the full lite node cap —
    // the budget bites animated media only, never the field.
    for (const id of LAYER_IDS) byId.get(id).replaceChildren();
    const fl2 = F.createFlashes({ layers, media: fakeMedia(), logger: quiet });
    fl2.start({ intensity: 1 });
    await sleep(320);
    fl2.stop();
    for (let round = 0; round < 6 && liveShots().length < F.MAX_LIVE_LITE; round++) {
      for (const el of liveShots()) tap(el);
      await sleep(280);
    }
    await sleep(300);
    ok(liveShots().length === F.MAX_LIVE_LITE,
      'a still-image pool on lite fills to MAX_LIVE_LITE — the animation budget charged nothing',
      String(liveShots().length));
    documentElement.removeAttribute(PT.PERF_ATTR);

    // ---- the lite CSS half of pass 2 (fx.css)
    {
      const fs = await import('node:fs/promises');
      const url = await import('node:url');
      const raw = await fs.readFile(url.fileURLToPath(new URL('../exec/fx.css', import.meta.url)), 'utf8');
      const css = raw.replace(/\/\*[\s\S]*?\*\//g, '');
      ok(/html\[data-gg-perf="lite"\]\s*\.gg-drain\.is-glitching\s*\{\s*animation-name:\s*ggDrainGlitchLite/.test(css),
        'lite swaps the glitch shudder onto its transform-only twin');
      const liteBody = /@keyframes\s+ggDrainGlitchLite([\s\S]*?)(?=@keyframes|html\[|$)/.exec(css);
      ok(!!liteBody && !/filter/.test(liteBody[1]),
        'and the twin animates NO filter — the fullscreen recolour raster is the whole cost being shed',
        liteBody ? liteBody[1].trim().slice(0, 60) : '(missing)');
      ok(/html\[data-gg-perf="lite"\]\s*\.gg-bounce-word\.is-hit\s*\{\s*animation:\s*none/.test(css),
        'lite drops the bounce-word hit flash (a filter pass per wall hit, for a garnish)');
    }
  }

  console.log(failures === 0 ? `PASS — ${n} checks` : `FAILED — ${failures}/${n} checks`);
  process.exit(failures === 0 ? 0 : 1);
}

main().catch((e) => { console.error(e); process.exit(1); });
