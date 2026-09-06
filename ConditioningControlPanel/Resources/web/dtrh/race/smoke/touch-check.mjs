/* ============================================================================
 * race/smoke/touch-check.mjs - node self-check for race/touch.js and the merge
 * race/input.js does with it.
 *
 *   node race/smoke/touch-check.mjs      (exits 0 on pass, 1 with a count of failures)
 *
 * jsdom-free on purpose: the DOM this needs is four methods wide, so it is stubbed
 * here (elements, classList, listeners, MutationObserver, a window with location /
 * navigator / matchMedia) and synthetic pointer events are dispatched straight at
 * the layer touch.js built. Date.now is driven by hand so a 180 ms hold costs no
 * wall clock. Nothing here proves the FEEL of the steering: that is the owner's,
 * on a real phone, with real thumbs.
 * ==========================================================================*/

import { createTouch, wantsTouch, steerFromDx, STEER_LOCK_PX, STEER_DEAD_PX, TAP_MS, TAP_PX } from '../touch.js';
import { createInput } from '../input.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const near = (a, b) => Math.abs(a - b) < 1e-9;

/* ---- the stub DOM ------------------------------------------------------- */
const VW = 800, VH = 400;

class Elem {
  constructor(tag) {
    this.tagName = String(tag || 'div').toUpperCase();
    this.children = []; this.parentNode = null; this.style = {}; this.textContent = '';
    this.attrs = {}; this._l = new Map(); this._cls = new Set(); this._obs = [];
  }
  get className() { return [...this._cls].join(' '); }
  set className(v) { this._cls = new Set(String(v).split(/\s+/).filter(Boolean)); }
  get classList() {
    const self = this;
    return {
      add: (...c) => { for (const x of c) self._cls.add(x); self._bump(); },
      remove: (...c) => { for (const x of c) self._cls.delete(x); self._bump(); },
      contains: (c) => self._cls.has(c),
      toggle: (c, on) => {
        const want = on === undefined ? !self._cls.has(c) : !!on;
        if (want) self._cls.add(c); else self._cls.delete(c);
        self._bump(); return want;
      },
    };
  }
  _bump() { for (const cb of this._obs.slice()) cb([{ type: 'attributes', attributeName: 'class' }]); }
  setAttribute(k, v) { this.attrs[k] = v; }
  appendChild(n) { n.parentNode = this; this.children.push(n); return n; }
  removeChild(n) { const i = this.children.indexOf(n); if (i >= 0) this.children.splice(i, 1); n.parentNode = null; return n; }
  addEventListener(t, fn) { if (!this._l.has(t)) this._l.set(t, []); this._l.get(t).push(fn); }
  removeEventListener(t, fn) { const a = this._l.get(t); if (a) { const i = a.indexOf(fn); if (i >= 0) a.splice(i, 1); } }
  getBoundingClientRect() { return { left: 0, top: 0, width: VW, height: VH }; }
  setPointerCapture() { } releasePointerCapture() { }
  closest(sel) { const c = sel.replace('.', ''); let n = this; while (n) { if (n._cls.has(c)) return n; n = n.parentNode; } return null; }
  querySelector(sel) {
    const c = sel.replace('.', '');
    const walk = (n) => { for (const k of n.children) { if (k._cls.has(c)) return k; const r = walk(k); if (r) return r; } return null; };
    return walk(this);
  }
  /** how many listeners are still hooked up (dispose has to leave none) */
  get listenerCount() { let n = 0; for (const a of this._l.values()) n += a.length; return n; }
  fire(type, props = {}) {
    const e = { type, target: this, cancelable: true, preventDefault() { }, stopPropagation() { }, ...props };
    for (const fn of (this._l.get(type) || []).slice()) fn(e);
    return e;
  }
}

class MO {
  constructor(cb) { this.cb = cb; this.t = null; }
  observe(el) { this.t = el; el._obs.push(this.cb); }
  disconnect() { if (this.t) this.t._obs = this.t._obs.filter((f) => f !== this.cb); }
}

const doc = { createElement: (t) => new Elem(t) };
const makeWin = ({ search = '', maxTouchPoints = 0, coarse = false } = {}) => ({
  location: { search }, navigator: { maxTouchPoints }, MutationObserver: MO, document: doc,
  matchMedia: (q) => ({ matches: !!coarse && /coarse/.test(String(q)) }),
  addEventListener() { }, removeEventListener() { },
});
/** a race root with the .race-hud host and the item slot hud.js owns */
const makeRoot = () => {
  const root = new Elem('div'); root.className = 'race-root';
  const hud = new Elem('div'); hud.className = 'race-hud'; root.appendChild(hud);
  const slot = new Elem('div'); slot.className = 'rh-item'; hud.appendChild(slot);
  return { root, hud, slot };
};

// the clock the hold test drives by hand
const realNow = Date.now;
let clock = 1600000000000;
Date.now = () => clock;
const tick = (ms) => { clock += ms; };
/** input.js eases DIGITAL steer against performance.now(), which is not stubbed, so the
 *  merge test spends real milliseconds to let that easing land. Nothing else waits. */
const settle = (input, ms = 800) => {
  const t0 = performance.now();
  let r = input.read();
  while (performance.now() - t0 < ms) r = input.read();
  return r;
};

/* ---- 1. the steering curve ---------------------------------------------- */
{
  ok(steerFromDx(0) === 0 && steerFromDx(STEER_DEAD_PX) === 0 && steerFromDx(-STEER_DEAD_PX) === 0, 'a thumb inside the dead zone is not a turn');
  ok(steerFromDx(STEER_LOCK_PX) === 1 && steerFromDx(-STEER_LOCK_PX) === -1, 'full lock at +-' + STEER_LOCK_PX + ' px');
  ok(steerFromDx(999) === 1 && steerFromDx(-999) === -1, 'and it never passes 1');
  ok(steerFromDx(-40) < 0 && steerFromDx(40) > 0, 'left is left and right is right (no flip, Law II)');
  const half = STEER_DEAD_PX + (STEER_LOCK_PX - STEER_DEAD_PX) / 2;
  ok(near(steerFromDx(half), 0.5), 'the ramp between the dead zone and the lock is linear');
}

/* ---- 2. who gets a layer at all ----------------------------------------- */
{
  ok(wantsTouch(makeWin({ maxTouchPoints: 5 })) === true, 'a page with touch points is touchable');
  ok(wantsTouch(makeWin({ coarse: true })) === true, 'so is a coarse pointer');
  ok(wantsTouch(makeWin({})) === false, 'a mouse desktop is not');
  ok(wantsTouch(makeWin({ search: '?touch=1' })) === true, '?touch=1 forces the layer on (the screenshot aid)');
  ok(wantsTouch(makeWin({ search: '?touch=0', maxTouchPoints: 5, coarse: true })) === false, '?touch=0 forces it off on a phone');
  const { root } = makeRoot();
  ok(createTouch({ root, win: makeWin({}), doc }) === null, 'and a mouse desktop builds not one node');
}

/* ---- 3. the layer it does build ----------------------------------------- */
const built = (() => {
  const { root, hud, slot } = makeRoot();
  const t = createTouch({ root, win: makeWin({ coarse: true }), doc });
  return { root, hud, slot, t };
})();
{
  const { hud, t } = built;
  ok(!!t && t.el && t.el.classList.contains('rh-touch'), 'a touchable page gets the .rh-touch layer');
  ok(hud.children.some((c) => c === t.el), 'built inside .race-hud, so it inherits the safe insets');
  ok(!!t.el.querySelector('.rt-pad') && !!t.el.querySelector('.rt-ring') && !!t.el.querySelector('.rt-dot'), 'with a thumb pad: a ring and a dot');
}

/* ---- 4. steering: the left 55% ------------------------------------------ */
{
  const { t } = built;
  const L = t.el;
  L.fire('pointerdown', { pointerId: 1, clientX: 100, clientY: 300 });
  ok(L.classList.contains('is-steering'), 'a thumb down on the left shows the pad');
  ok(t.read().steer === 0, 'and starts at zero, wherever it landed');
  L.fire('pointermove', { pointerId: 1, clientX: 100 + STEER_LOCK_PX, clientY: 300 });
  ok(t.read().steer === 1, 'dragging a full lock right reads +1');
  L.fire('pointermove', { pointerId: 1, clientX: 100 - 999, clientY: 300 });
  ok(t.read().steer === -1, 'and a long drag left is clamped to -1, never past it');
  L.fire('pointermove', { pointerId: 1, clientX: 100 + STEER_DEAD_PX - 1, clientY: 300 });
  ok(t.read().steer === 0, 'back inside the dead zone is straight again');
  L.fire('pointerup', { pointerId: 1, clientX: 160, clientY: 300 });
  ok(t.read().steer === 0 && !L.classList.contains('is-steering'), 'released is zero and the pad fades');
  ok(t.read().jump === false, 'a left-zone press is never a jump');
}

/* ---- 5. the right zone: tap jumps, hold drifts --------------------------- */
{
  const { t } = built;
  const L = t.el, X = 700;   // right of STEER_SIDE * 800
  L.fire('pointerdown', { pointerId: 2, clientX: X, clientY: 300 });
  ok(t.read().jump === false && t.read().drift === false, 'a fresh press is neither yet');
  tick(TAP_MS - 40);
  ok(t.read().drift === false, 'still inside the tap window, still not a drift');
  L.fire('pointerup', { pointerId: 2, clientX: X + 2, clientY: 301 });
  const r = t.read();
  ok(r.jump === true, 'a short still tap is one jump press');
  ok(t.read().jump === false, 'and exactly one: the next read is clean (the JUMP_KEY rule)');
  ok(r.steer === 0, 'a right-zone tap never steers');

  L.fire('pointerdown', { pointerId: 3, clientX: X, clientY: 300 });
  tick(TAP_MS);
  ok(t.read().drift === true, 'held past ' + TAP_MS + ' ms it becomes a drift, with no move event needed');
  L.fire('pointerup', { pointerId: 3, clientX: X, clientY: 300 });
  ok(t.read().drift === false && t.read().jump === false, 'letting a drift go is not a jump');

  L.fire('pointerdown', { pointerId: 4, clientX: X, clientY: 300 });
  L.fire('pointermove', { pointerId: 4, clientX: X + TAP_PX + 6, clientY: 300 });
  ok(t.read().drift === true, 'a press that travels is a drift at once, however short');
  L.fire('pointerup', { pointerId: 4, clientX: X + TAP_PX + 6, clientY: 300 });
  ok(t.read().jump === false, 'and never a jump');

  L.fire('pointerdown', { pointerId: 5, clientX: X, clientY: 300 });
  L.fire('pointercancel', { pointerId: 5, clientX: X, clientY: 300 });
  ok(t.read().jump === false, 'a cancelled press (a call, a system gesture) is no press at all');
}

/* ---- 6. both thumbs at once --------------------------------------------- */
{
  const { t } = built;
  const L = t.el;
  L.fire('pointerdown', { pointerId: 10, clientX: 120, clientY: 300 });
  L.fire('pointermove', { pointerId: 10, clientX: 120 - STEER_LOCK_PX, clientY: 300 });
  L.fire('pointerdown', { pointerId: 11, clientX: 700, clientY: 300 });
  tick(TAP_MS);
  const r = t.read();
  ok(r.steer === -1 && r.drift === true, 'one thumb steers while the other drifts: the two zones do not fight');
  t.flush();
  const f = t.read();
  ok(f.steer === 0 && f.drift === false && f.jump === false, 'flush() drops everything the thumbs were holding');
}

/* ---- 8. dispose leaves nothing behind ----------------------------------- */
{
  const { root, hud, slot } = makeRoot();
  const t = createTouch({ root, win: makeWin({ coarse: true }), doc });
  const layer = t.el;
  t.dispose();
  ok(layer.parentNode === null && !hud.children.includes(layer), 'dispose() takes the layer out of the page');
  ok(layer.listenerCount === 0, 'and unhooks every pointer listener');
  slot.classList.add('is-held');   // the observer is gone: this must not throw
  ok(true, 'and disconnects the slot observer');
}

/* ---- 9. THE MERGE: input.js reads all three sources ---------------------- */
{
  const win = makeWin({ coarse: true });
  const prevWindow = globalThis.window;
  globalThis.window = win;                       // createTouch's default win/doc
  const { root } = makeRoot();
  const target = new Elem('div');                // stands in for the key target
  const input = createInput({ target, root });
  const L = input.touchEl;
  ok(!!L, 'createInput({ root }) builds the touch layer on a touchable page');

  // the thumb alone
  L.fire('pointerdown', { pointerId: 1, clientX: 100, clientY: 300 });
  L.fire('pointermove', { pointerId: 1, clientX: 100 - STEER_LOCK_PX, clientY: 300 });
  let r = input.read();
  ok(r.steer === -1, 'a thumb at full lock reaches read().steer with no easing (it is analog)');
  ok(r.accel === 1 && r.brake === 0, 'and accel is still cruise: the phone never touches the pedals');

  // a key the other way loses to the bigger magnitude, and wins when it is bigger
  L.fire('pointermove', { pointerId: 1, clientX: 100 - 12, clientY: 300 });   // a barely-left thumb
  target.fire('keydown', { code: 'ArrowRight', type: 'keydown' });
  r = settle(input);
  ok(r.steer > 0.9, 'a full key beats a barely-held thumb: steer takes the larger magnitude, never a remap');
  // a digital key is magnitude 1, so a thumb can only take it back once the key is up
  target.fire('keyup', { code: 'ArrowRight', type: 'keyup' });
  L.fire('pointermove', { pointerId: 1, clientX: 100 - STEER_LOCK_PX, clientY: 300 });
  ok(input.read().steer === -1, 'and with the key up the thumb takes it straight back, unsmoothed');

  // drift is an OR
  L.fire('pointerup', { pointerId: 1, clientX: 88, clientY: 300 });
  L.fire('pointerdown', { pointerId: 2, clientX: 700, clientY: 300 });
  tick(TAP_MS);
  ok(input.read().drift === true, 'a held right thumb drifts through the merge');
  target.fire('keydown', { code: 'ShiftLeft', type: 'keydown' });
  ok(input.read().drift === true, 'and Shift on top of it is still one drift (an OR)');
  target.fire('keyup', { code: 'ShiftLeft', type: 'keyup' });
  L.fire('pointerup', { pointerId: 2, clientX: 700, clientY: 300 });

  // the jump edge survives the merge, once
  L.fire('pointerdown', { pointerId: 3, clientX: 700, clientY: 300 });
  tick(20);
  L.fire('pointerup', { pointerId: 3, clientX: 700, clientY: 300 });
  ok(input.read().jump === true, 'a tap raises read().jump');
  ok(input.read().jump === false, 'for exactly one frame, like Space');

  // flush() and dispose() reach the touch source too
  L.fire('pointerdown', { pointerId: 4, clientX: 100, clientY: 300 });
  L.fire('pointermove', { pointerId: 4, clientX: 300, clientY: 300 });
  input.flush();
  // the thumb is dropped at once; the eased value then falls to a hard zero the way a
  // released key's already does (flush has never reset the easing, for keys either)
  ok(settle(input).steer === 0, 'input.flush() clears a thumb still on the glass');
  input.dispose();
  ok(L.parentNode === null, 'input.dispose() takes the layer with it');

  globalThis.window = prevWindow;
}

/* ---- 10. and a mouse desktop is untouched -------------------------------- */
{
  const prevWindow = globalThis.window;
  globalThis.window = makeWin({});               // no touch points, no coarse pointer
  const { root, hud } = makeRoot();
  const input = createInput({ target: new Elem('div'), root });
  ok(input.touchEl === null, 'a mouse desktop gets no touch layer from createInput');
  ok(hud.children.length === 1, 'and not one extra node in .race-hud (the WebView2 build stays identical)');
  const r = input.read();
  ok(r.steer === 0 && r.accel === 1 && r.drift === false && r.jump === false, 'and read() is exactly what it was');
  input.dispose();
  globalThis.window = prevWindow;
}

Date.now = realNow;
console.log(fails ? '\n' + fails + ' FAILED' : '\nall touch checks pass');
process.exit(fails ? 1 : 0);
