/* ============================================================================
 * race/input.js - the wheel. Implements CONTRACT.md "race/run.js + raceBoot.js"
 * (PR 5, integration): the one place keyboard and gamepad are read.
 *
 * Keyboard: arrows / WASD steer + accel + brake, Shift drift, E item, Esc brake,
 * P cycles the pixel look.
 * Gamepad (navigator.getGamepads, standard map): left stick steer, RT accel,
 * LT brake, A drift, X item, Start brake.
 *
 *   read() -> { steer:-1..1, accel:0..1, brake:0..1, drift:bool }
 *   onAction(cb)   cb(action) for the ACTIONS table below ('item' | 'brake' | 'pixel' | 'mute'), edge-triggered, never repeats on hold
 *   dispose()
 *
 * Law II (input honesty): nothing here ever remaps an axis. accel defaults to 1
 * when nothing is pressed, so a player who only ever steers still cruises.
 * Digital steer is eased over ~80 ms so the cup leans on the first frame
 * (Law VIII) without a hard snap. The mirror item flips the picture, never
 * the hand: there is no flip/mirror API here and none may be added (the
 * owner's law: left stays left).
 * ==========================================================================*/

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
const PAD = { steer: 0, accelBtn: 7, brakeBtn: 6, drift: 0, item: 2, start: 9 };
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

export function createInput({ target = window } = {}) {
  const down = new Set();
  const acts = [];
  const out = { steer: 0, accel: 1, brake: 0, drift: false };
  let steerE = 0, lastT = 0, disposed = false;
  const padWas = { item: false, start: false };

  const fire = (name) => { for (const cb of acts) { try { cb(name); } catch (e) { /* a listener never breaks the wheel */ } } };

  function onKey(e) {
    if (disposed || e.altKey || e.metaKey || e.ctrlKey) return;
    const code = e.code || '';
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
  const onBlur = () => down.clear();

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
      const item = btn(g, PAD.item) > 0.5, start = btn(g, PAD.start) > 0.5;
      if (item && !padWas.item) fire('item');
      if (start && !padWas.start) fire('brake');
      padWas.item = item; padWas.start = start;
    }
    if (digital) steerE += (steer - steerE) * Math.min(1, dt * 14);
    else steerE = steer;
    if (Math.abs(steerE) < 0.002) steerE = 0;
    out.steer = clamp(steerE, -1, 1);
    out.accel = accel > 0 ? clamp(accel, 0, 1) : 1;          // nothing pressed = cruise
    out.brake = clamp(brake, 0, 1);
    out.drift = !!drift;
    return out;
  }

  function dispose() {
    disposed = true;
    target.removeEventListener('keydown', onKey);
    target.removeEventListener('keyup', onKey);
    target.removeEventListener('blur', onBlur);
    down.clear(); acts.length = 0;
  }

  return {
    read,
    onAction(cb) { if (typeof cb === 'function') acts.push(cb); return () => { const i = acts.indexOf(cb); if (i >= 0) acts.splice(i, 1); }; },
    dispose,
  };
}

// self-check: node --check is the bar (window/navigator are only touched inside createInput).
