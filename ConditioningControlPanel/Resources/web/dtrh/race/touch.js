/* ============================================================================
 * race/touch.js - the phone's hands. A THIRD input source for race/input.js.
 *
 * Law II (input honesty) says nothing may remap an axis and no mirror/flip API
 * may exist. This file adds neither: it is a new source that produces the same
 * steer / drift / jump a key or a stick produces, and race/input.js merges it
 * with the other two (max of magnitudes for steer, OR for drift, one press per
 * tap for jump). Left stays left.
 *
 *   createTouch({ root, fire }) -> null on a mouse desktop, else
 *     { el, read(), flush(), dispose() }
 *   read() -> { steer:-1..1, drift:bool, jump:bool }   (jump is one frame, one tap)
 *
 * Nothing is built unless the page is actually touchable: `(pointer: coarse)`
 * or `navigator.maxTouchPoints > 0`. `?touch=1` forces the layer on (the
 * headless screenshot aid), `?touch=0` forces it off. A mouse desktop and the
 * WebView2 desktop build therefore get not one node of this.
 *
 * The layer rides at z12 inside `.race-hud`: above the chrome (z3) and the
 * payload layers (z4 / z9), BELOW the Brake and End screens (z20), the
 * countdown (z21), the menu (z25), the cards (z26) and the boot splash (z30),
 * so every card and screen still takes the tap first and needs no cooperation
 * from here.
 *
 * The hands:
 *   LEFT 55%   steering. Drag horizontally from wherever the thumb lands: full
 *              lock at +-STEER_LOCK_PX, nothing inside STEER_DEAD_PX, zero the
 *              moment the thumb lifts. A ring marks the touch-down point and a
 *              dot rides the drag, so the player can see the wheel they are on.
 *   RIGHT      a TAP (under TAP_MS, under TAP_PX of travel) is one jump press,
 *              exactly like Space. Anything longer or further is DRIFT, held
 *              for as long as the thumb stays down. Accel is untouched: nothing
 *              pressed is cruise, and a phone never needs the brake pedal.
 *   BUTTONS    pause (fires 'brake', which opens the Brake screen and its own
 *              tappable buttons), mute, and an item button that only appears
 *              while the slot is `is-held`. 48 px minimum, anchored on the
 *              existing --rh-in-* safe-area insets.
 *
 * Pointer Events only, never TouchEvent, and the pointer is captured so a drag
 * that wanders off the zone still belongs to the finger that started it.
 * ==========================================================================*/

/** Full steering lock, css px of horizontal drag from the touch-down point. */
export const STEER_LOCK_PX = 72;
/** Dead zone: a thumb resting on the glass is not a turn. */
export const STEER_DEAD_PX = 6;
/** A press shorter than this, and stiller than TAP_PX, is a jump; longer is a drift. */
export const TAP_MS = 180;
export const TAP_PX = 12;
/** The left fraction of the width that steers. The rest jumps and drifts. */
export const STEER_SIDE = 0.55;

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

/** The steering curve, pulled out so race/smoke/touch-check.mjs can walk it. */
export function steerFromDx(dx, lock = STEER_LOCK_PX, dead = STEER_DEAD_PX) {
  const m = Math.abs(dx);
  if (!(m > dead)) return 0;
  const v = Math.min(1, (m - dead) / Math.max(1, lock - dead));
  return dx < 0 ? -v : v;
}

/** Should this page build the layer at all? `?touch=1` / `?touch=0` override the device. */
export function wantsTouch(win) {
  const w = win || (typeof window !== 'undefined' ? window : null);
  if (!w) return false;
  try {
    const q = new URLSearchParams(w.location.search).get('touch');
    if (q === '1' || q === 'on') return true;
    if (q === '0' || q === 'off') return false;
  } catch (e) { /* no location, or a page that hides it: fall through to the device test */ }
  const nav = w.navigator;
  if (nav && typeof nav.maxTouchPoints === 'number' && nav.maxTouchPoints > 0) return true;
  try { return !!(typeof w.matchMedia === 'function' && w.matchMedia('(pointer: coarse)').matches); }
  catch (e) { return false; }
}

export function createTouch({ root = null, fire = () => {}, win = null, doc = null } = {}) {
  const w = win || (typeof window !== 'undefined' ? window : null);
  const d = doc || (w && w.document) || null;
  if (!root || !d || !wantsTouch(w)) return null;

  const host = root.querySelector('.race-hud') || root;
  const mk = (cls, parent, text) => {
    const n = d.createElement('div');
    n.className = cls;
    if (text != null) n.textContent = text;
    parent.appendChild(n);
    return n;
  };
  const layer = mk('rh-touch', host);
  const pad = mk('rt-pad', layer);
  mk('rt-ring', pad);
  const dot = mk('rt-dot', pad);

  const button = (cls, glyph, label, action) => {
    const b = d.createElement('button');
    b.type = 'button';
    b.className = 'rt-btn ' + cls;
    b.textContent = glyph;
    b.setAttribute('aria-label', label);
    // pointerdown, not click: on glass the press IS the answer, and waiting for a
    // synthesised click reads as a dropped tap.
    b.addEventListener('pointerdown', (e) => { if (e.cancelable) e.preventDefault(); e.stopPropagation(); fire(action); });
    layer.appendChild(b);
    return b;
  };
  button('rt-pause', 'II', 'brake', 'brake');
  button('rt-mute', 'sound', 'mute', 'mute');
  const itemBtn = button('rt-item', 'use', 'use item', 'item');

  // the item button only exists while there is something to spend: the slot already
  // carries `is-held` (race/hud.js), so watch that rather than invent a second truth
  const slot = root.querySelector('.rh-item');
  const syncItem = () => itemBtn.classList.toggle('is-on', !!(slot && slot.classList.contains('is-held')));
  let mo = null;
  if (slot && w && typeof w.MutationObserver === 'function') {
    mo = new w.MutationObserver(syncItem);
    mo.observe(slot, { attributes: true, attributeFilter: ['class'] });
  }
  syncItem();

  const out = { steer: 0, drift: false, jump: false };
  let steerId = -1, steerX0 = 0, steer = 0;
  let actId = -1, actT0 = 0, actX0 = 0, actY0 = 0, actMoved = false;
  let jumpQ = false, disposed = false;

  const at = (e) => {
    const r = layer.getBoundingClientRect();
    return { x: e.clientX - r.left, y: e.clientY - r.top, w: r.width || 1 };
  };
  const showPad = (x, y) => {
    pad.style.left = x + 'px'; pad.style.top = y + 'px';
    dot.style.transform = 'translate(0px, 0px)';
    layer.classList.add('is-steering');
  };

  function onDown(e) {
    if (disposed) return;
    const t = e.target;
    if (t && typeof t.closest === 'function' && t.closest('.rt-btn')) return;   // the buttons speak for themselves
    const p = at(e);
    if (p.x < p.w * STEER_SIDE) {
      if (steerId >= 0) return;
      steerId = e.pointerId; steerX0 = e.clientX; steer = 0;
      showPad(p.x, p.y);
    } else {
      if (actId >= 0) return;
      actId = e.pointerId; actT0 = Date.now(); actX0 = e.clientX; actY0 = e.clientY; actMoved = false;
    }
    try { layer.setPointerCapture(e.pointerId); } catch (err) { /* no capture: the listeners still fire */ }
    if (e.cancelable) e.preventDefault();
  }

  function onMove(e) {
    if (disposed) return;
    if (e.pointerId === steerId) {
      const dx = e.clientX - steerX0;
      steer = steerFromDx(dx);
      dot.style.transform = 'translate(' + clamp(dx, -STEER_LOCK_PX, STEER_LOCK_PX) + 'px, 0px)';
    } else if (e.pointerId === actId) {
      if (Math.abs(e.clientX - actX0) > TAP_PX || Math.abs(e.clientY - actY0) > TAP_PX) actMoved = true;
    } else return;
    if (e.cancelable) e.preventDefault();
  }

  function onUp(e) {
    if (disposed) return;
    if (e.pointerId === steerId) {
      steerId = -1; steer = 0;
      layer.classList.remove('is-steering');
    } else if (e.pointerId === actId) {
      // a tap is one jump press; a cancel (a call, a system gesture) is no press at all
      if (e.type === 'pointerup' && !actMoved && Date.now() - actT0 < TAP_MS) jumpQ = true;
      actId = -1;
    } else return;
    try { layer.releasePointerCapture(e.pointerId); } catch (err) { /* already gone */ }
  }

  const noMenu = (e) => { if (e.cancelable) e.preventDefault(); };   // no long-press menu over the road
  layer.addEventListener('pointerdown', onDown);
  layer.addEventListener('pointermove', onMove);
  layer.addEventListener('pointerup', onUp);
  layer.addEventListener('pointercancel', onUp);
  layer.addEventListener('contextmenu', noMenu);

  /** Drift is decided here, not in a handler: a thumb held perfectly still sends no
   *  move event, and the hold still has to become a drift on its own. */
  function read() {
    out.steer = clamp(steer, -1, 1);
    out.drift = actId >= 0 && (actMoved || Date.now() - actT0 >= TAP_MS);
    out.jump = jumpQ; jumpQ = false;
    return out;
  }

  function flush() {
    steerId = -1; actId = -1; steer = 0; jumpQ = false; actMoved = false;
    layer.classList.remove('is-steering');
  }

  function dispose() {
    disposed = true;
    layer.removeEventListener('pointerdown', onDown);
    layer.removeEventListener('pointermove', onMove);
    layer.removeEventListener('pointerup', onUp);
    layer.removeEventListener('pointercancel', onUp);
    layer.removeEventListener('contextmenu', noMenu);
    if (mo) { try { mo.disconnect(); } catch (err) { /* observer already dead */ } mo = null; }
    if (layer.parentNode) layer.parentNode.removeChild(layer);
    flush();
  }

  return { el: layer, read, flush, dispose };
}

// self-check: node --check is the bar (window/navigator/document are only touched inside
// wantsTouch and createTouch, so race/smoke/touch-check.mjs can import this file in node).
