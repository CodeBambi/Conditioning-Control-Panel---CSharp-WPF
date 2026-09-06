/* ============================================================================
 * race/input.js - the wheel. Implements CONTRACT.md "race/run.js + raceBoot.js"
 * (PR 5, integration): the one place keyboard, gamepad and touch are read.
 *
 * Keyboard: arrows / WASD steer + accel + brake, Shift drift, Space jump, E item,
 * Esc brake, P cycles the pixel look.
 * Gamepad (navigator.getGamepads, standard map): left stick steer, RT accel,
 * LT brake, A drift, B jump, X item, Start brake.
 * Touch (race/touch.js, only on a touchable page): the left half drags the wheel,
 * the right half taps to jump and holds to drift, plus pause / mute / use buttons.
 * `createInput({ root })` needs the race root or the touch layer is never built.
 *
 *   read() -> { steer:-1..1, accel:0..1, brake:0..1, drift:bool, jump:bool }
 *   onAction(cb)   cb(action) for the ACTIONS table below ('item' | 'brake' | 'pixel' | 'mute'), edge-triggered, never repeats on hold
 *   flush()        drop everything held or queued (run.js calls it as the run starts)
 *   dispose()
 *
 * `jump` is true for exactly the frame of a fresh press and never on hold, so one press is one
 * jump (race/kart.js reads it straight off read()). Space is also the introduction cards' "next",
 * so run.js flushes the wheel at start(): the press that closed the last card never also jumps.
 * A focused button keeps its own space; everywhere else the press is swallowed so the page
 * cannot scroll under the canvas.
 *
 * Law II (input honesty): nothing here ever remaps an axis. accel defaults to 1
 * when nothing is pressed, so a player who only ever steers still cruises.
 * Digital steer is eased over ~80 ms so the cup leans on the first frame
 * (Law VIII) without a hard snap. The mirror item flips the picture, never
 * the hand: there is no flip/mirror API here and none may be added (the
 * owner's law: left stays left).
 *
 * THE MERGE: keys, pad and touch are three ADDITIVE sources, never a remap of
 * one another. steer takes whichever source has the larger magnitude (an analog
 * source, stick or thumb, also turns the easing off because it is already
 * continuous); accel and brake take the max; drift is an OR; jump and the
 * ACTIONS are edges, so any source may raise one and each is one press.
 * ==========================================================================*/

import { createTouch } from './touch.js';

const KEYS = {
  ArrowLeft: 'left', KeyA: 'left', ArrowRight: 'right', KeyD: 'right',
  ArrowUp: 'accel', KeyW: 'accel', ArrowDown: 'brake', KeyS: 'brake',
  ShiftLeft: 'drift', ShiftRight: 'drift',
};
/** Edge-triggered actions by key code; append a line here to add one (fired once per press, never on hold). */
const ACTIONS = {
  KeyE: 'item',
  Escape: 'brake',
  KeyP: 'pixel',
  KeyM: 'mute',   // race/audio.js listens
};
const DEADZONE = 0.16;
const PAD = { steer: 0, accelBtn: 7, brakeBtn: 6, drift: 0, jump: 1, item: 2, start: 9 };
/** Space is the jump: not a held KEY and not an ACTION, the kart reads one press per frame. */
const JUMP_KEY = 'Space';
/** A space that belongs to a focused control (menu buttons) is left alone. */
const CONTROLS = /^(BUTTON|INPUT|SELECT|TEXTAREA|A)$/;
/** `?jump=<ms>` fires one synthetic jump press about that far into the run: a screenshot aid.
 *  It counts frames at 60 Hz instead of reading the clock, so a headless page whose virtual time
 *  stands still still gets its press. Nothing else about the wheel changes. */
const AID_JUMP = (() => {
  try { const v = +new URLSearchParams(location.search).get('jump') || 0; return v > 0 ? Math.max(1, Math.round(v / 16.7)) : 0; }
  catch (e) { return 0; }                   // node, or a page with no location: the aid is simply off
})();
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

export function createInput({ target = window, root = null } = {}) {
  const down = new Set();
  const acts = [];
  const out = { steer: 0, accel: 1, brake: 0, drift: false, jump: false };
  let steerE = 0, lastT = 0, disposed = false;
  let jumpQ = false, jumpHeld = false, aidLeft = AID_JUMP;
  const padWas = { item: false, start: false, jump: false };

  const fire = (name) => { for (const cb of acts) { try { cb(name); } catch (e) { /* a listener never breaks the wheel */ } } };

  /** The third source. null on a mouse desktop: not one node is built there. */
  const touch = createTouch({ root, fire: (name) => fire(name) });

  function onKey(e) {
    if (disposed || e.altKey || e.metaKey || e.ctrlKey) return;
    const code = e.code || '';
    if (code === JUMP_KEY) {                                  // space: the jump, one per press
      const t = e.target;
      if (t && t.tagName && CONTROLS.test(t.tagName)) return; // a focused button keeps its space
      e.preventDefault();                                     // and the page never scrolls
      if (e.type !== 'keydown') { jumpHeld = false; return; }
      if (e.repeat || jumpHeld) return;                       // holding it is not a second jump
      jumpHeld = true; jumpQ = true;
      return;
    }
    if (e.type === 'keydown') {
      if (e.repeat) { if (KEYS[code]) e.preventDefault(); return; }
      if (ACTIONS[code]) { fire(ACTIONS[code]); return; }
      if (!KEYS[code]) return;
      down.add(KEYS[code]);
      e.preventDefault();
    } else if (KEYS[code]) {
      down.delete(KEYS[code]);
    }
  }
  const onBlur = () => { down.clear(); jumpHeld = false; jumpQ = false; };

  target.addEventListener('keydown', onKey);
  target.addEventListener('keyup', onKey);
  target.addEventListener('blur', onBlur);

  function pad() {
    const list = (typeof navigator !== 'undefined' && navigator.getGamepads) ? navigator.getGamepads() : null;
    if (!list) return null;
    for (let i = 0; i < list.length; i++) { const g = list[i]; if (g && g.connected) return g; }
    return null;
  }
  const btn = (g, i) => { const b = g.buttons && g.buttons[i]; return b ? (typeof b.value === 'number' ? b.value : (b.pressed ? 1 : 0)) : 0; };

  function read() {
    const now = performance.now();
    const dt = lastT ? clamp((now - lastT) / 1000, 0, 0.1) : 0.016;
    lastT = now;
    let steer = (down.has('right') ? 1 : 0) - (down.has('left') ? 1 : 0);
    let accel = down.has('accel') ? 1 : 0;
    let brake = down.has('brake') ? 1 : 0;
    let drift = down.has('drift');
    let jump = jumpQ; jumpQ = false;                           // one frame, one press
    if (aidLeft > 0 && --aidLeft === 0) jump = true;            // `?jump=<ms>`, see the header
    let digital = true;
    const g = pad();
    if (g) {
      const ax = g.axes && g.axes.length > PAD.steer ? g.axes[PAD.steer] : 0;
      if (Math.abs(ax) > DEADZONE && steer === 0) {
        steer = Math.sign(ax) * (Math.abs(ax) - DEADZONE) / (1 - DEADZONE);
        digital = false;
      }
      accel = Math.max(accel, btn(g, PAD.accelBtn));
      brake = Math.max(brake, btn(g, PAD.brakeBtn));
      drift = drift || btn(g, PAD.drift) > 0.5;
      const item = btn(g, PAD.item) > 0.5, start = btn(g, PAD.start) > 0.5, jmp = btn(g, PAD.jump) > 0.5;
      if (item && !padWas.item) fire('item');
      if (start && !padWas.start) fire('brake');
      if (jmp && !padWas.jump) jump = true;                    // pad B, edge-triggered like the key
      padWas.item = item; padWas.start = start; padWas.jump = jmp;
    }
    if (touch) {
      const t = touch.read();
      if (Math.abs(t.steer) > Math.abs(steer)) { steer = t.steer; digital = false; }   // the thumb is analog too
      drift = drift || t.drift;
      if (t.jump) jump = true;                                 // one tap, one press
    }
    if (digital) steerE += (steer - steerE) * Math.min(1, dt * 14);
    else steerE = steer;
    if (Math.abs(steerE) < 0.002) steerE = 0;
    out.steer = clamp(steerE, -1, 1);
    out.accel = accel > 0 ? clamp(accel, 0, 1) : 1;          // nothing pressed = cruise
    out.brake = clamp(brake, 0, 1);
    out.drift = !!drift;
    out.jump = !!jump;
    return out;
  }

  /** Drop everything held or queued. `jumpHeld` stays: a key still physically down only jumps
   *  again once it has come up, so a space that closed the last card cannot jump twice either. */
  function flush() {
    down.clear(); jumpQ = false;
    padWas.item = false; padWas.start = false; padWas.jump = false;
    if (touch) touch.flush();   // and a thumb still on the glass starts the run neutral
  }

  function dispose() {
    disposed = true;
    target.removeEventListener('keydown', onKey);
    target.removeEventListener('keyup', onKey);
    target.removeEventListener('blur', onBlur);
    if (touch) touch.dispose();
    down.clear(); acts.length = 0;
    jumpQ = false; jumpHeld = false;
  }

  return {
    read,
    flush,
    /** The touch layer element, or null where none was built. Test aid only. */
    get touchEl() { return touch ? touch.el : null; },
    onAction(cb) { if (typeof cb === 'function') acts.push(cb); return () => { const i = acts.indexOf(cb); if (i >= 0) acts.splice(i, 1); }; },
    dispose,
  };
}

// self-check: node --check is the bar (window/navigator are only touched inside createInput).
