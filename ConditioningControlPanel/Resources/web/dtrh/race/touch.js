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
 *   ANYWHERE   a DOUBLE TAP is a jump. Two taps inside DOUBLE_TAP_MS, either
 *              half, whatever the thumbs are already doing. This is the rule
 *              the how-to panel teaches, because it is the one that survives a
 *              thumb already mid-corner.
 *   RIGHT      a single TAP (under TAP_MS, under TAP_PX of travel) is one jump
 *              press too, exactly like Space. Anything longer or further is
 *              DRIFT, held for as long as the thumb stays down. Accel is
 *              untouched: nothing pressed is cruise, and a phone never needs
 *              the brake pedal.
 *   BUTTONS    pause (fires 'brake', which opens the Brake screen and its own
 *              tappable buttons), mute, and an item button that only appears
 *              while the slot is `is-held`. 48 px minimum, anchored on the
 *              existing --rh-in-* safe-area insets. A press that lands on a
 *              button is never a tap, so double tapping `use` spends the item
 *              twice and never jumps.
 *
 * WHY THE TAP DIED ON AN IPHONE. A jump used to need one exact thing to happen:
 * a `pointerup`, on the right half, inside 180 ms, on a captured pointer. Every
 * one of those four is something WebKit is allowed to take away.
 *   - `touch-action: none` in the stylesheet does not, on its own, stop Safari
 *     arming its own recognisers over a layer whose `touchstart` default was
 *     never prevented. The moment it decides the gesture is a zoom, a pan or a
 *     long-press callout it sends `pointercancel` instead of `pointerup`, and a
 *     cancel is deliberately no press at all.
 *   - `setPointerCapture` is the API it hands back as that same cancel.
 *   - 180 ms is under a deliberate thumb press on glass.
 *   - and the right half is the half a right thumb is already busy holding.
 * So none of the four is load bearing any more:
 *   - the layer prevents the default of `touchstart` itself (non-passive, and
 *     skipped over the buttons so they keep their own press),
 *   - only the WHEEL pointer is captured; the jump hand is not, because the
 *     layer is inset: 0 and had nowhere to lose it to anyway,
 *   - a TouchEvent floor sits under the LIFT: a `touchend` that looks like a tap
 *     raises the same tap the pointer path would have, and two taps closer
 *     together than TAP_ECHO_MS are one lift reported twice,
 *   - the fingers left on the glass at a `touchend` end any held id they do not
 *     account for, so a swallowed `pointerup` cannot strand a drift that eats
 *     every jump after it,
 *   - `pointerup` / `pointercancel` are heard on the window as well as on the
 *     layer, so a lift that lands elsewhere still ends the gesture,
 *   - the tap window is 220 ms, not 180,
 *   - and a DOUBLE TAP jumps from either half, so the hand that is free is
 *     always the right hand.
 * None of this is proven on a real iPhone from here. The node smoke walks the
 * logic; the phone is the owner's.
 * ==========================================================================*/

/** Full steering lock, css px of horizontal drag from the touch-down point. */
export const STEER_LOCK_PX = 72;
/** Dead zone: a thumb resting on the glass is not a turn. */
export const STEER_DEAD_PX = 6;
/** A press shorter than this, and stiller than TAP_PX, is a tap; longer is a drift. */
export const TAP_MS = 220;
export const TAP_PX = 14;
/** Two taps this close together, on EITHER half, are a jump. The owner's rule. */
export const DOUBLE_TAP_MS = 320;
/** One physical lift can be reported twice (a pointerup, then the touchend behind it).
 *  Taps closer than this are that echo, counted once. Well under a real double tap. */
export const TAP_ECHO_MS = 60;
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
  let steerId = -1, steerX0 = 0, steerY0 = 0, steerT0 = 0, steerMoved = false, steer = 0;
  let actId = -1, actT0 = 0, actX0 = 0, actY0 = 0, actMoved = false;
  let jumpQ = false, disposed = false;
  /** the double tap: when the last counted tap landed, and when the last lift was counted */
  let lastTapT = 0, tapEchoT = 0;
  /** the TouchEvent floor's own book: identifier -> where and when that finger went down */
  const touchDown = new Map();

  const onBtn = (t) => !!(t && typeof t.closest === 'function' && t.closest('.rt-btn'));

  /** One completed tap, from the pointer path or the touch floor under it. A tap on the
   *  right half is a jump on its own; two taps on EITHER half inside DOUBLE_TAP_MS are a
   *  jump too, which is the rule that still answers with a thumb already mid-corner. */
  function tapped(now, rightSide) {
    if (tapEchoT && now - tapEchoT < TAP_ECHO_MS) return;      // the same lift, reported twice
    tapEchoT = now;
    const dbl = lastTapT > 0 && now - lastTapT <= DOUBLE_TAP_MS;
    lastTapT = dbl ? 0 : now;                                  // a pair is spent: three taps are one jump and a fresh first
    if (rightSide || dbl) jumpQ = true;
  }

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
    if (onBtn(e.target)) return;                                               // the buttons speak for themselves
    const p = at(e);
    if (p.x < p.w * STEER_SIDE) {
      if (steerId >= 0) return;
      steerId = e.pointerId; steerX0 = e.clientX; steerY0 = e.clientY;
      steerT0 = Date.now(); steerMoved = false; steer = 0;
      showPad(p.x, p.y);
      // capture only the WHEEL: a steering thumb wanders and has to keep its pointer
      try { layer.setPointerCapture(e.pointerId); } catch (err) { /* no capture: the listeners still fire */ }
    } else {
      if (actId >= 0) return;
      actId = e.pointerId; actT0 = Date.now(); actX0 = e.clientX; actY0 = e.clientY; actMoved = false;
      // and NOT the jump hand. The layer is inset: 0, so there is nowhere that finger can go
      // that this element is not already under, and capture is the thing WebKit hands back as
      // a `pointercancel` when it changes its mind about who owns the gesture. One less way
      // to lose a tap, for a capture that was never buying anything.
    }
    if (e.cancelable) e.preventDefault();
  }

  function onMove(e) {
    if (disposed) return;
    if (e.pointerId === steerId) {
      const dx = e.clientX - steerX0;
      if (Math.abs(dx) > TAP_PX || Math.abs(e.clientY - steerY0) > TAP_PX) steerMoved = true;   // a drag is not a tap
      steer = steerFromDx(dx);
      dot.style.transform = 'translate(' + clamp(dx, -STEER_LOCK_PX, STEER_LOCK_PX) + 'px, 0px)';
    } else if (e.pointerId === actId) {
      if (Math.abs(e.clientX - actX0) > TAP_PX || Math.abs(e.clientY - actY0) > TAP_PX) actMoved = true;
    } else return;
    if (e.cancelable) e.preventDefault();
  }

  function onUp(e) {
    if (disposed) return;
    const now = Date.now();
    if (e.pointerId === steerId) {
      // a still, short press on the wheel side turns nothing, but it still counts toward
      // the double tap: the rule is anywhere, not one half
      if (e.type === 'pointerup' && !steerMoved && now - steerT0 < TAP_MS) tapped(now, false);
      steerId = -1; steer = 0; steerMoved = false;
      layer.classList.remove('is-steering');
    } else if (e.pointerId === actId) {
      // a tap is one jump press; a cancel (a call, a system gesture) is no press at all
      if (e.type === 'pointerup' && !actMoved && now - actT0 < TAP_MS) tapped(now, true);
      actId = -1;
    } else return;
    try { layer.releasePointerCapture(e.pointerId); } catch (err) { /* already gone */ }
  }

  /* ---- the TouchEvent floor under the pointer path -------------------------
   * Not a second input scheme. `touchstart` only takes the default away from
   * WebKit's gesture recognisers, and `touchend` only reports a LIFT the pointer
   * path may never have been told about: the tap it raises, and the held id it
   * ends. Where a wheel goes and how far is still the pointer path's alone. */
  /** Which half of the layer a raw Touch is on, or null if it is over a button (a button
   *  press is the button's, never a tap and never a wheel). */
  function sideOf(c) {
    if (!c || onBtn(c.target)) return null;
    const r = layer.getBoundingClientRect();
    return (c.clientX - r.left) >= (r.width || 1) * STEER_SIDE ? 'right' : 'left';
  }

  function onTouchStart(e) {
    if (disposed) return;
    // this preventDefault IS the fix: without it Safari keeps its own zoom / pan / callout
    // recognisers armed over the road and cancels our pointers to go and run one instead.
    // It is skipped over the buttons so they keep their own press.
    if (!onBtn(e.target) && e.cancelable) e.preventDefault();
    const list = e.changedTouches || [];
    for (let i = 0; i < list.length; i++) {
      const c = list[i];
      // per touch, not per event: two fingers can land in one touchstart and only e.target
      // belongs to the first of them, so the button test has to be asked of each one
      const side = sideOf(c);
      if (!side) continue;
      touchDown.set(c.identifier, { t0: Date.now(), x0: c.clientX, y0: c.clientY, right: side === 'right' });
    }
  }
  function onTouchEnd(e) {
    if (disposed) return;
    const now = Date.now();
    const list = e.changedTouches || [];
    for (let i = 0; i < list.length; i++) {
      const c = list[i];
      const s = touchDown.get(c.identifier);
      touchDown.delete(c.identifier);
      if (!s || e.type !== 'touchend') continue;                // touchcancel is no press, same as pointercancel
      if (now - s.t0 < TAP_MS && Math.abs(c.clientX - s.x0) <= TAP_PX && Math.abs(c.clientY - s.y0) <= TAP_PX) tapped(now, s.right);
    }
    // A `pointerup` Safari swallowed would otherwise strand a held id forever: the drift never
    // ends and `onDown` refuses every press after it, which is a jump that never comes back.
    // The fingers still on the glass are the truth, so ask them, per half.
    const rest = e.touches || [];
    let onLeft = false, onRight = false;
    for (let i = 0; i < rest.length; i++) {
      const side = sideOf(rest[i]);
      if (side === 'right') onRight = true; else if (side === 'left') onLeft = true;
    }
    if (!onLeft && steerId >= 0) { steerId = -1; steer = 0; steerMoved = false; layer.classList.remove('is-steering'); }
    if (!onRight) actId = -1;
    if (!onLeft && !onRight) touchDown.clear();
  }

  const noMenu = (e) => { if (e.cancelable) e.preventDefault(); };   // no long-press menu over the road
  layer.addEventListener('pointerdown', onDown);
  layer.addEventListener('pointermove', onMove);
  layer.addEventListener('pointerup', onUp);
  layer.addEventListener('pointercancel', onUp);
  layer.addEventListener('contextmenu', noMenu);
  layer.addEventListener('touchstart', onTouchStart, { passive: false });
  layer.addEventListener('touchend', onTouchEnd, { passive: false });
  layer.addEventListener('touchcancel', onTouchEnd, { passive: false });
  // a lift that lands anywhere else still ends the gesture; onUp ignores every id it does not hold
  if (w && typeof w.addEventListener === 'function') {
    w.addEventListener('pointerup', onUp, true);
    w.addEventListener('pointercancel', onUp, true);
  }

  /** Drift is decided here, not in a handler: a thumb held perfectly still sends no
   *  move event, and the hold still has to become a drift on its own. */
  function read() {
    out.steer = clamp(steer, -1, 1);
    out.drift = actId >= 0 && (actMoved || Date.now() - actT0 >= TAP_MS);
    out.jump = jumpQ; jumpQ = false;
    return out;
  }

  function flush() {
    steerId = -1; actId = -1; steer = 0; jumpQ = false; actMoved = false; steerMoved = false;
    lastTapT = 0; tapEchoT = 0; touchDown.clear();
    layer.classList.remove('is-steering');
  }

  function dispose() {
    disposed = true;
    layer.removeEventListener('pointerdown', onDown);
    layer.removeEventListener('pointermove', onMove);
    layer.removeEventListener('pointerup', onUp);
    layer.removeEventListener('pointercancel', onUp);
    layer.removeEventListener('contextmenu', noMenu);
    layer.removeEventListener('touchstart', onTouchStart);
    layer.removeEventListener('touchend', onTouchEnd);
    layer.removeEventListener('touchcancel', onTouchEnd);
    if (w && typeof w.removeEventListener === 'function') {
      w.removeEventListener('pointerup', onUp, true);
      w.removeEventListener('pointercancel', onUp, true);
    }
    if (mo) { try { mo.disconnect(); } catch (err) { /* observer already dead */ } mo = null; }
    if (layer.parentNode) layer.parentNode.removeChild(layer);
    flush();
  }

  return { el: layer, read, flush, dispose };
}

// self-check: node --check is the bar (window/navigator/document are only touched inside
// wantsTouch and createTouch, so race/smoke/touch-check.mjs can import this file in node).
