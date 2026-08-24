/* ============================================================================
 * emi/widget.js - THE FLOATING ELEMENT (agent B).
 *
 * EMI is a free-floating widget inside the Arcademy window: the user drags her
 * anywhere, pets her, and can dismiss her into a little semi-transparent dock
 * button on the bottom-right edge. Position and the dismissed flag persist.
 *
 * This module owns the DOM, the pointer verbs and the persistence. The LOOK is
 * agent A's (`emi.css`), the FACE is agent A's (`face.js` / `chains.js` /
 * `fx.js`) and it is INJECTED - this file never imports them, so a broken
 * renderer degrades EMI to a body PNG rather than taking the shell's boot with
 * it (the same "optional module" discipline `shell.js loadOptional` uses).
 *
 * THREE LAWS THIS FILE EXISTS TO KEEP
 * 1. INPUT TRUST (CLAUDE.md §top): the layer is `pointer-events:none` and only
 *    `.emi` and `.emi-dock` re-enable it. Nothing here calls `preventDefault`
 *    on anything outside `.emi`, so a board click over EMI's hidden footprint
 *    still lands on the board.
 * 2. ESC IS NOT OURS. EMI adds no rung to the Esc ladder and binds no key
 *    listener at all (trap 29 / shell.js escapeStep own that ladder).
 * 3. A SAY IS NEVER CUT MID-LINE. Petting and dragging queue nothing and cancel
 *    nothing while a speech bubble is typing; they are ignored (a reaction that
 *    eats a sentence reads as a bug, not as a mascot).
 * ==========================================================================*/

/* ---------------------- dials (designer-tunable) -------------------------
 * EVERY tunable EMI has is in this one frozen object, so the owner can retune
 * her feel by hand without reading the code around it. One line each. */
export const DIALS = Object.freeze({
  /* --- size ------------------------------------------------------------ */
  W_DEFAULT: 150,          // px wide on a roomy window (>= W_NARROW_VW); aspect comes from the PNG
  W_NARROW: 116,           // ...and this much on a narrow one, so she does not own the board
  W_NARROW_VW: 900,        // px of viewport width below which W_NARROW is the default
  W_MIN: 110,              // clamp floor for a stored/explicit width
  W_MAX: 220,              // clamp ceiling for the same
  ASPECT_W: 859,           // mascot-body-blank.png, natural size
  ASPECT_H: 869,
  MARGIN: 4,               // px kept between EMI and the viewport edge

  /* --- pointer verbs --------------------------------------------------- */
  DRAG_PX: 6,              // move this far from the press point and it is a DRAG, not a pet
  DRAG_MS: 250,            // ...or hold the button this long while moving at all
  HOLD_MS: 450,            // press-and-hold this long arms the x affordance (touch has no hover)
  FLING_SPEED: 1.5,        // px/ms; above this the drag counts as FAST
  FLING_MS: 120,           // ...sustained this long = >.< in the air and a THUD landing
  SETTLE_MS: 600,          // ^_^ held this long after a release, then back to idle

  /* --- petting --------------------------------------------------------- */
  PET_WINDOW_MS: 4000,     // PET_TARGET pets inside this window buys the glee cycle
  PET_TARGET: 3,           // ...how many that is
  PET_COOLDOWN_MS: 6000,   // ...after which she only winks, so spam cannot loop the show

  /* --- idling ---------------------------------------------------------- */
  RAW_HOLD_MS: 1400,       // a raw face string (no chain) is held this long
  BLINK_EVERY_MS: 5200,    // resting blink cadence
  BLINK_HOLD_MS: 110,      // ...and how long the eyes stay shut

  /* --- perception (the wave of 2026-08-24) ------------------------------ */
  GAZE_MAX_PX: 3,          // the face may lean at most this many px toward the cursor
  GAZE_DIV: 60,            // px of cursor distance per px of lean (bigger = subtler)
  GAZE_EASE: 0.15,         // per-frame easing toward the target (0..1)
  APPROACH_PX: 120,        // cursor within this many px of her EDGE = she notices
  APPROACH_COOLDOWN_MS: 30000, // one noticing per this window, so she is not a doorbell
  GLANCE_SPEED: 1.2,       // px/ms; entering the radius faster than this earns the glance chain
  LINGER_MS: 2000,         // hover this long without clicking = expectant
  LINGER_AWAY_MS: 4000,    // ...and this much longer with no pet = the look-away
  DANGLE_MAX_DEG: 6,       // carried tilt is clamped here (physics reads as care, not slapstick)
  DANGLE_K: 4,             // deg of tilt per px/ms of horizontal drag speed
  DANGLE_SETTLE_MS: 260,   // the spring back to upright on release

  /* --- the idle sway (five body frames; antenna + hand tips only) ------- */
  SWAY_STEP_MS: 200,       // one step of the ping-pong walk
  SWAY_CENTER_MIN_MS: 600, // ...and the pause when it passes back through centre
  SWAY_CENTER_MAX_MS: 900, // (a range, so two idle beats never land in lockstep)

  /* --- the speech bubble ------------------------------------------------ */
  BUBBLE_SHIFT_X: 15,      // px further off her ear (owner, 2026-08-24). Mirrored
                           // by `.bubble-left` and paid for in faceBubble's reach.
  SAY_HOLD_MIN_MS: 3000,   // a landed line NEVER holds less than this...
  SAY_HOLD_BASE_MS: 1400,  // ...and a long one grows: BASE + PER_CHAR x length
  SAY_HOLD_PER_CHAR_MS: 45,

  /* --- the field trip (wave W2a) --------------------------------------- */
  CRT_MS: 200,             // ONE power-off: squish to a line (~140) + flash dot (~60).
                           // MIRRORS widget.css `emi-crt-off`/`emi-crt-on`, the way
                           // BODY_MS mirrors emi.css. Change both or she moves in the light.
  TRIP_GAP_PX: 14,         // px she stands off the fixture she came to look at
  TRIP_BEAT_MS: 320,       // the pause after the line, before the tube goes dark again

  /* --- persistence ----------------------------------------------------- */
  SAVE_DEBOUNCE_MS: 600,   // one write per interaction, never per pointermove
});

/** The one-shot line the FIRST dismiss ever earns. Shown through the shell's toast. */
export const HINT_LINE = 'EMI is in the corner.';

const IDLE_FACE = '0_0';
const BLINK_FACE = '-_-';
const DRAG_FACE = '@_@';
const FLING_FACE = '>.<';
const SETTLE_FACE = '^_^';
const BODY_CLASSES = ['breath', 'nod', 'shiver', 'bounce', 'thud', 'droop'];
/* How long each body move runs before EMI settles back into BREATH. Agent A's
 * keyframe lengths, rounded up: a move that never returns leaves `droop`'s
 * `forwards` fill (or a dead `bounce`) welded onto the root. */
const BODY_MS = { bounce: 800, thud: 800, shiver: 900, nod: 1900, droop: 800, shake: 420 };
/* SHAKE is the exit flinch's move and it is deliberately NOT a new keyframe:
 * emi.css owns the keyframes and this file owns how long a move runs, so a
 * shake is SHIVER cut short. It inherits the reduced-motion refusal with it. */
const BODY_ALIAS = { shake: 'shiver' };
/* ============================================================================
 * THE BODY FRAMES
 * ==========================================================================*/
/* EMI's body used to be ONE png - the arms-up ceremony pose - and she wore it
 * at rest, which is why she always looked like she had just won something. The
 * locked pose set gives her six, and `body.png` keeps its name so nothing else
 * in the bundle moves: it is now CEREMONY ONLY. Every frame is the same
 * 859x869 canvas with the same screen rect, so the face canvas lands in exactly
 * the same place whichever one is up. */
export const BODY_FRAME_SRC = Object.freeze({
  celebration: './art/emi/body.png',      // arms up: stamps, wins, reveals, LV UP
  idle: './art/emi/body-idle.png',        // arms down: THE RESTING POSE + sway centre
  sad: './art/emi/body-sad.png',          // cry, K.O., a broken streak, the exit flinch
  shock: './art/emi/body-shock.png',      // shock, wake, rage, glitch, a rare drop
  smug: './art/emi/body-smug.png',        // smug, suspicious, the dork-canon lines
  pet: './art/emi/body-pet.png',          // pets, love, the pet streak
  // ...and the four off-centre steps of the idle sway. Only her antenna and her
  // hand tips differ from `idle`; nothing else in the frame moves.
  sway1: './art/emi/body-sway1.png',
  sway2: './art/emi/body-sway2.png',
  sway3: './art/emi/body-sway3.png',
  sway4: './art/emi/body-sway4.png',
});

/* THE POSE WALK. Out to one side and back through the centre, then out to the
 * other - `sway1` and `sway4` are the two extremes. The centre is HELD (see
 * DIALS.SWAY_CENTER_*); every other step is one SWAY_STEP_MS. */
const SWAY_CYCLE = ['idle', 'sway2', 'sway1', 'sway2', 'idle', 'sway3', 'sway4', 'sway3'];

/* THE DEFAULT MAP: a face family -> a pose, for everything that is NOT a chain
 * with its own `bodyFrame` (raw holds, and every line `makeSay` builds - which
 * is resolved frame by frame, so the typing dots stay at `idle` and the pose
 * lands with the reaction face). Anything absent here is `idle`, deliberately:
 * a face nobody paired is a face she has no strong feeling about. */
export const FACE_BODY_FRAME = Object.freeze({
  // celebration
  '^_^': 'celebration', '^___^': 'celebration', '^_~': 'celebration',
  '\\o/': 'celebration', 'GG': 'celebration', 'LV UP': 'celebration',
  '★★★': 'celebration', ':D': 'celebration', 'XD': 'celebration',
  // sad
  ';_;': 'sad', 'T_T': 'sad', 'x_x': 'sad', '(ಥ_ಥ)': 'sad',
  '(✖╭╮✖)': 'sad', ":'(": 'sad',
  // shock
  'o_o': 'shock', '(◉_◉)': 'shock', '(⊙_⊙)': 'shock',
  '>_<': 'shock', '>.<': 'shock', '!!!': 'shock', '???': 'shock',
  '#ERR': 'shock', '404': 'shock', ':O': 'shock',
  // smug
  '¬_¬': 'smug', '(¬‿¬)': 'smug', '(ಠ‿ಠ)': 'smug',
  '(⌐■_■)': 'smug', '( ͡° ͜ʖ ͡°)': 'smug',
  '(◔_◔)': 'smug', 'B)': 'smug', '>:)': 'smug',
  // pet
  '(｡♥‿♥｡)': 'pet', '(✿◡‿◡)': 'pet',
  '(≧◡≦)': 'pet', '(◠‿◠)': 'pet', '(◕‿◕)': 'pet',
  '*_*': 'pet', '♥♥♥': 'pet', '<3': 'pet',
});

/** A frame key, or null when it is not one of ours (never throws on junk). */
function frameKey(k) {
  return (typeof k === 'string' && Object.prototype.hasOwnProperty.call(BODY_FRAME_SRC, k)) ? k : null;
}
/** The pose a raw face string wears. Unpaired faces rest at `idle`. */
export function frameForFace(text) {
  const t = typeof text === 'string' ? text : '';
  return Object.prototype.hasOwnProperty.call(FACE_BODY_FRAME, t) ? FACE_BODY_FRAME[t] : 'idle';
}

/**
 * HOW LONG A LANDED LINE STAYS UP (owner ruling 2026-08-24: "3 sec, or more
 * according to length"). The typing cadence is UNCHANGED - `. .. ...` still
 * runs 420/420/520 and the clear frame is still 200 - only the hold grows.
 * An explicit ask from a caller wins when it is LONGER; it can never pull a
 * line back under the floor.
 */
export function sayHoldMs(line, explicitMs) {
  const n = typeof line === 'string' ? line.length : 0;
  const grown = DIALS.SAY_HOLD_BASE_MS + n * DIALS.SAY_HOLD_PER_CHAR_MS;
  const asked = (typeof explicitMs === 'number' && isFinite(explicitMs)) ? Math.round(explicitMs) : 0;
  return Math.max(DIALS.SAY_HOLD_MIN_MS, grown, asked);
}
/** Everything in a locked SAY that is NOT the hold: . / .. / ... plus the clear. */
export const SAY_LEAD_MS = 420 + 420 + 520 + 200;
/* The bubble hangs UP OFF EMI's right ear (emi.css: left:58%, bottom:96%,
 * max-width:104px), so she needs roughly this multiple of her own width to the
 * right of her left edge or the line runs off the viewport - at which point it
 * flips to the left ear (.bubble-left). */
const BUBBLE_REACH = 1.55;
/* ...and it RISES, so parked within this many px of the top edge there is no
 * sky for it: it drops below the chin instead (.bubble-low). Sized for the
 * worst wrapped bark (~5 lines) plus the tail. */
const BUBBLE_RISE = 96;

/** The persisted blob's key in the C# meta store (core/store.js top-level). */
export const STORE_KEY = 'emi';

function clamp(v, lo, hi) { return v < lo ? lo : (v > hi ? hi : v); }
function num(v) { return typeof v === 'number' && isFinite(v) ? v : null; }
function nowMs() { return Date.now(); }
function isoDay() { try { return new Date().toISOString().slice(0, 10); } catch (e) { return null; } }

/** A zeroed telemetry record. Lifetime counters; no UI reads them yet. */
function blankStats() {
  return {
    pets: 0, petStreaks3: 0, drags: 0, flings: 0, hides: 0, dockRestores: 0,
    bubblesSeen: 0, firstSeenAt: null, lastSeenAt: null, msVisible: 0,
    /* WHERE SHE GETS PUT DOWN: drop counts per ninth of the viewport (z0..z8,
     * row-major). The favourite-spot beats read the count off the dropAt
     * payload; nothing else looks in here. */
    zones: {},
  };
}

function readStats(raw) {
  const s = blankStats();
  if (!raw || typeof raw !== 'object') return s;
  for (const k of Object.keys(s)) {
    const v = raw[k];
    if (k === 'zones') {
      if (v && typeof v === 'object') {
        for (const zk of Object.keys(v)) {
          const n = v[zk];
          if (/^z[0-8]$/.test(zk) && typeof n === 'number' && isFinite(n) && n >= 0) {
            s.zones[zk] = Math.round(n);
          }
        }
      }
    } else if (k === 'firstSeenAt' || k === 'lastSeenAt') {
      if (typeof v === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(v)) s[k] = v;
    } else if (typeof v === 'number' && isFinite(v) && v >= 0) {
      s[k] = Math.round(v);
    }
  }
  return s;
}

/* ============================================================================
 * createWidget
 * ==========================================================================*/
/**
 * @param {Object} o
 * @param {Element} o.root            the `.arc-emi` layer (fixed, inset:0, pe:none)
 * @param {Object=} o.face            face.js module | createFace fn | a live face
 * @param {Object=} o.chains          chains.js module (FACES/CHAINS/playChain/makeSay)
 * @param {Object=} o.fx              fx.js module | showFx fn
 * @param {Object=} o.store           core/store.js (get/set) - the persistence seam
 * @param {Function=} o.toast         the SHELL's toast (boot.js -> createShell's `shout`)
 * @param {Function=} o.log
 */
export function createWidget({ root, face, chains, fx, vox: vox0, store, toast, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  /* THE SHELL'S TOAST, NOT A SECOND ONE. `#arc-toast` already exists, already
   * stacks above EMI (z 60 vs 50) and already expires its own nodes; minting a
   * rival would put two notice systems on one page. */
  const shout = typeof toast === 'function' ? toast : () => {};
  if (!root || typeof document === 'undefined' || !document.createElement) return null;

  /* ---------------------- persisted state ------------------------------- */
  let saved = {};
  try {
    const raw = store && typeof store.get === 'function' ? store.get(STORE_KEY) : null;
    if (raw && typeof raw === 'object') saved = raw;
  } catch (e) { say('emi: store read failed - ' + ((e && e.message) || e)); }

  const stats = readStats(saved.stats);
  /* WIDTH IS A DEFAULT UNTIL SOMEBODY SETS IT. A stored `w` only exists once a
   * resize verb has run (`setWidth`; there is no resize UI yet), so out of the
   * box she follows the window: full size on a roomy one, smaller on a narrow
   * one. Persisting the auto width would freeze the first window she ever saw. */
  let userSized = num(saved.w) !== null;
  let width = userSized ? clamp(num(saved.w), DIALS.W_MIN, DIALS.W_MAX) : autoWidth();
  // Anchor = the TOP-LEFT corner as a fraction of the viewport, so a resize
  // moves EMI proportionally and then re-clamps instead of drifting off-screen.
  let fx0 = num(saved.x);
  let fy0 = num(saved.y);
  let hidden = saved.hidden === true;
  let hintShown = saved.hintShown === true;
  let enabled = true;

  let saveTimer = null;
  let visibleSince = null;

  function accrueVisible() {
    if (visibleSince == null) return;
    stats.msVisible += Math.max(0, nowMs() - visibleSince);
    visibleSince = null;
  }
  function beginVisible() { if (visibleSince == null) visibleSince = nowMs(); }

  /** msVisible INCLUDING the stretch still running, rounded to whole seconds -
   *  the counter is a story beat's number, not a metric, and rounding keeps a
   *  mid-session write from churning the blob. */
  function visibleMs() {
    const live = visibleSince == null ? 0 : Math.max(0, nowMs() - visibleSince);
    return Math.round((stats.msVisible + live) / 1000) * 1000;
  }

  function blob() {
    // `zones` is the one nested object: cloned so the store never holds a live
    // reference into the counters.
    const out = Object.assign({}, stats, { msVisible: visibleMs(), zones: Object.assign({}, stats.zones) });
    const b = { x: fx0, y: fy0, hidden, stats: out };
    // `w` is written ONLY when it was chosen, never when it was derived from the
    // window - see `userSized`. An auto width in the blob would out-vote the
    // viewport rule for ever after the first drag.
    if (userSized) b.w = width;
    if (hintShown) b.hintShown = true;
    return b;
  }

  /** Write-through to the C# meta store, DEBOUNCED. Never per pointermove. */
  function save(immediate) {
    if (!store || typeof store.set !== 'function') return;
    if (saveTimer !== null) { clearTimeout(saveTimer); saveTimer = null; }
    const doIt = () => {
      saveTimer = null;
      try { store.set(STORE_KEY, blob()); }
      catch (e) { say('emi: store write failed - ' + ((e && e.message) || e)); }
    };
    if (immediate) doIt();
    else saveTimer = setTimeout(doIt, DIALS.SAVE_DEBOUNCE_MS);
  }

  /** Called on every real interaction: keeps the two date stamps honest. */
  function touchSeen() {
    const d = isoDay();
    if (!d) return;
    if (!stats.firstSeenAt) stats.firstSeenAt = d;
    stats.lastSeenAt = d;
  }

  /* ---------------------- DOM ------------------------------------------- */
  const el = document.createElement('div');
  el.className = 'emi';
  el.setAttribute('role', 'img');
  el.setAttribute('aria-label', 'EMI');

  const body = document.createElement('img');
  body.className = 'emi-body';
  /* THE RESTING POSE IS `idle`, NOT the ceremony one. body.png (arms up) is what
   * a stamp, a win or a reveal puts on her; at rest her arms are down. */
  body.src = BODY_FRAME_SRC.idle;
  body.alt = '';
  body.draggable = false;
  if (body.setAttribute) body.setAttribute('draggable', 'false');

  const canvas = document.createElement('canvas');
  canvas.className = 'emi-screen';

  const fxHost = document.createElement('div');
  fxHost.className = 'emi-fx';

  const bubble = document.createElement('div');
  bubble.className = 'emi-bubble';
  bubble.hidden = true;

  const xBtn = document.createElement('button');
  xBtn.className = 'emi-x';
  xBtn.type = 'button';
  xBtn.textContent = '×';
  xBtn.title = 'Hide EMI';
  if (xBtn.setAttribute) xBtn.setAttribute('aria-label', 'Hide EMI');

  /* THE BUBBLE'S OFFSET IS A DIAL, and the sheets read it as a custom property
   * so widget.css keeps one half of it and this file's reach test keeps the
   * other, both off the same number. */
  try {
    if (el.style && typeof el.style.setProperty === 'function') {
      el.style.setProperty('--emi-bubble-dx', DIALS.BUBBLE_SHIFT_X + 'px');
    }
  } catch (e) { /* noop */ }

  el.appendChild(body);
  el.appendChild(canvas);
  el.appendChild(fxHost);
  el.appendChild(bubble);
  el.appendChild(xBtn);

  const dock = document.createElement('button');
  dock.className = 'emi-dock';
  dock.type = 'button';
  dock.textContent = '0_0';
  dock.title = 'Show EMI';
  if (dock.setAttribute) dock.setAttribute('aria-label', 'Show EMI');
  dock.hidden = true;

  root.appendChild(el);
  root.appendChild(dock);

  /* ---------------------- the body frames ------------------------------- */
  /* A FRAME THAT WILL NOT LOAD IS NOT AN ERROR. EMI floats over every screen in
   * the school and she may never break one, so a missing or broken png falls
   * back to `body.png` (the one frame that has shipped since day one) silently
   * and once per key. */
  const frameFailed = Object.create(null);
  let bodyFrame = 'idle';

  function frameUrl(key) {
    return frameFailed[key] ? BODY_FRAME_SRC.celebration : BODY_FRAME_SRC[key];
  }

  /** Swap the body png. A no-op when it is already up (this runs per sway step). */
  function setBodyFrame(key) {
    const k = frameKey(key) || 'idle';
    if (k === bodyFrame) return k;
    bodyFrame = k;
    try { body.src = frameUrl(k); } catch (e) { /* noop */ }
    try { if (body.setAttribute) body.setAttribute('data-frame', k); } catch (e) { /* noop */ }
    return k;
  }

  if (body.addEventListener) {
    body.addEventListener('error', () => {
      const k = bodyFrame;
      if (!k || k === 'celebration' || frameFailed[k]) return;
      frameFailed[k] = true;
      say('emi: body frame "' + k + '" failed to load - falling back to body.png');
      try { body.src = BODY_FRAME_SRC.celebration; } catch (e) { /* noop */ }
    });
  }

  /* PRELOAD, ONCE. The bundle is offline so every frame is a local file, but a
   * first decode on the beat a stamp lands would still flash an empty body.
   * `Image` does not exist in the node DOM double - no preload, no problem. */
  const preloaded = [];
  (function preloadFrames() {
    if (typeof Image !== 'function') return;
    for (const k of Object.keys(BODY_FRAME_SRC)) {
      try {
        const im = new Image();
        im.onerror = () => {
          if (frameFailed[k]) return;
          frameFailed[k] = true;
          say('emi: body frame "' + k + '" is missing - body.png stands in');
          if (bodyFrame === k) { try { body.src = BODY_FRAME_SRC.celebration; } catch (e) { /* noop */ } }
        };
        im.src = BODY_FRAME_SRC[k];
        preloaded.push(im);
      } catch (e) { /* noop */ }
    }
  }());

  /* ---------------------- the renderer, injected ------------------------ */
  /* A CANVAS THE PLATFORM CANNOT PAINT IS NOT AN ERROR. The node DOM double has
   * no getContext, so the widget runs faceless there instead of throwing on
   * boot - which is what keeps the shell suite green. */
  let painter = null;
  let CHAINS = null;
  let playChain = null;
  let makeSay = null;
  let showFx = null;
  /* HER VOICE (emi/vox.js), injected the same way and just as optional. It owns
   * no audio node - it only asks shell/audio.js for blips - so from here it is
   * three calls hanging off setBubble(), the one place that already knows the
   * difference between typing, a landed line and a cleared bubble. */
  let vox = null;
  /* CONSTRUCTION-TIME ATTACH IS NOT A REPAINT. `attach({face, chains, fx})` runs
   * once from inside createWidget - a caller may hand the renderer straight to
   * the constructor instead of injecting it a tick later - and at that point the
   * chain runner's own state (`current`, `timers`, `blinkTimer`) is still in its
   * temporal dead zone below. The first-paint block at the BOTTOM of this
   * function already calls idle(), so the sync paint here is only for the LATE
   * attach. Without this flag the constructor threw
   * `ReferenceError: Cannot access 'current' before initialization`. */
  let built = false;

  function attach(mods) {
    const f = mods && mods.face;
    const c = mods && mods.chains;
    const x = mods && mods.fx;
    const v = mods && mods.vox;
    if (v && typeof v.speak === 'function') vox = v;
    if (!painter && f && canvas && typeof canvas.getContext === 'function') {
      const mk = typeof f === 'function' ? f : (f && typeof f.createFace === 'function' ? f.createFace : null);
      try {
        if (mk) painter = mk(canvas, {});
        else if (f && typeof f.draw === 'function') painter = f;
      } catch (e) { say('emi: createFace threw - ' + ((e && e.message) || e)); painter = null; }
    }
    if (c) {
      if (c.CHAINS && typeof c.CHAINS === 'object') CHAINS = c.CHAINS;
      if (typeof c.playChain === 'function') playChain = c.playChain;
      if (typeof c.makeSay === 'function') makeSay = c.makeSay;
    }
    if (x) showFx = typeof x === 'function' ? x : (typeof x.showFx === 'function' ? x.showFx : null);
    if (painter && !hidden && enabled) {
      // THE BUNDLED FACE FIRST. face.ready settles once Noto Sans Mono is in;
      // painting before it would show one frame in the system monospace. This
      // one is safe from the constructor: it resolves on a later tick.
      if (painter.ready && typeof painter.ready.then === 'function') {
        painter.ready.then(() => { if (!hidden && enabled && !busy()) idle(); }).catch(() => {});
      }
      if (built) idle();
    }
  }
  attach({ face, chains, fx, vox: vox0 });

  /* ---------------------- motion preference ----------------------------- */
  function reducedMotion() {
    try {
      if (document.documentElement && document.documentElement.classList
        && document.documentElement.classList.contains('arc-reduced')) return true;
      if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
        return !!window.matchMedia('(prefers-reduced-motion: reduce)').matches;
      }
    } catch (e) { /* noop */ }
    return false;
  }

  /* ---------------------- geometry -------------------------------------- */
  function viewport() {
    const w = (typeof window !== 'undefined' && window.innerWidth) || 1280;
    const h = (typeof window !== 'undefined' && window.innerHeight) || 800;
    return { w, h };
  }
  function sizePx() {
    return { w: width, h: Math.round(width * DIALS.ASPECT_H / DIALS.ASPECT_W) };
  }
  /** The default width for THIS window. Owner's rule: 150 on >= 900px, 116 below. */
  function autoWidth() {
    return viewport().w >= DIALS.W_NARROW_VW ? DIALS.W_DEFAULT : DIALS.W_NARROW;
  }
  /** Re-derive the width after a resize. No-op once the user has sized her. */
  function refitWidth() {
    if (userSized) return false;
    const next = autoWidth();
    if (next === width) return false;
    width = next;
    return true;
  }
  /** Place EMI from the stored fractions, clamped inside the viewport. */
  function place() {
    const vp = viewport();
    const s = sizePx();
    if (fx0 == null || fy0 == null) {
      // First run: park her bottom-right, clear of the dock's corner.
      fx0 = (vp.w - s.w - 24) / vp.w;
      fy0 = (vp.h - s.h - 56) / vp.h;
    }
    const left = clamp(fx0 * vp.w, DIALS.MARGIN, Math.max(DIALS.MARGIN, vp.w - s.w - DIALS.MARGIN));
    const top = clamp(fy0 * vp.h, DIALS.MARGIN, Math.max(DIALS.MARGIN, vp.h - s.h - DIALS.MARGIN));
    el.style.width = s.w + 'px';
    el.style.left = Math.round(left) + 'px';
    el.style.top = Math.round(top) + 'px';
    faceBubble(left, top, s.w, vp.w);
    return { left, top, s, vp };
  }
  /** THE BUBBLE HANGS OFF HER RIGHT EAR and would run off the right edge of the
   *  window there, so it flips to the left ear instead (`.bubble-left`, styled in
   *  widget.css). Decided from the position, so a drag and a resize both fix it. */
  function faceBubble(left, top, w, vw) {
    // The +15px shift is part of the reach on BOTH sides: the box is mirrored
    // with the flip, so the flip has to know about the offset or it would
    // re-create the clipped line it exists to prevent (owner tweak 2026-08-24).
    const dx = DIALS.BUBBLE_SHIFT_X;
    const overRight = left + w * BUBBLE_REACH + dx > vw;
    const room = left - w * (BUBBLE_REACH - 1) - dx > 0;
    // `classList.toggle` is not in every DOM double; add/remove is.
    if (overRight && room) el.classList.add('bubble-left');
    else el.classList.remove('bubble-left');
    if (top < BUBBLE_RISE) el.classList.add('bubble-low');
    else el.classList.remove('bubble-low');
  }

  /** Record the CURRENT pixel position back into the fractions. */
  function commit(left, top) {
    const vp = viewport();
    fx0 = left / vp.w;
    fy0 = top / vp.h;
  }

  /* ---------------------- the chain player ------------------------------ */
  /* ONE runner, so there is exactly one thing to cancel and exactly one place
   * that knows whether a speech bubble is mid-line. */
  let current = null;              // {handle, protect}
  const timers = new Set();
  function later(fn, ms) {
    const id = setTimeout(() => { timers.delete(id); fn(); }, ms);
    timers.add(id);
    return id;
  }
  function killTimers() { for (const id of timers) clearTimeout(id); timers.clear(); }

  let blinkTimer = null;
  function stopBlink() { if (blinkTimer !== null) { clearInterval(blinkTimer); blinkTimer = null; } }

  /* ---------------------- the idle sway ---------------------------------
   * Five frames of ONE pose walked out and back through the centre. It is the
   * only loop the body png ever runs, it exists only at rest, and REDUCED
   * MOTION REFUSES IT OUTRIGHT - the design lock allows no breath/shiver loops
   * there and a sprite sway is the same law, not a loophole. Anything that
   * takes the glass (a chain, a say, a drag, a hide) stops it; `idle()` is the
   * only thing that starts it. */
  let swayTimer = null;
  let swayAt = 0;
  function stopSway() { if (swayTimer !== null) { clearTimeout(swayTimer); swayTimer = null; } }
  function swayHold(key) {
    if (key !== 'idle') return DIALS.SWAY_STEP_MS;
    const lo = DIALS.SWAY_CENTER_MIN_MS;
    const hi = Math.max(lo, DIALS.SWAY_CENTER_MAX_MS);
    return lo + Math.floor(Math.random() * (hi - lo + 1));
  }
  function startSway() {
    stopSway();
    if (reducedMotion() || hidden || !enabled) return;
    swayAt = 0;
    const step = () => {
      swayTimer = null;
      if (hidden || !enabled || busy() || dragging || reducedMotion()) return;
      const key = SWAY_CYCLE[swayAt % SWAY_CYCLE.length];
      swayAt += 1;
      setBodyFrame(key);
      swayTimer = setTimeout(step, swayHold(key));
    };
    // She is already on the centre frame when idle() calls this, so the first
    // beat is the centre pause, not a jump.
    swayTimer = setTimeout(step, swayHold('idle'));
  }

  function drawFace(text, o) {
    if (!painter || typeof painter.draw !== 'function') return;
    try { painter.draw(String(text == null ? '' : text), o || {}); }
    catch (e) { say('emi: draw threw - ' + ((e && e.message) || e)); }
  }

  /* THE BUBBLE IS ALSO WHERE SHE IS AUDIBLE. This function already knows the
   * only three states her voice cares about - typing, landed, gone - and EVERY
   * cancel path in this file funnels through the `null` branch, so hanging the
   * voice here (and nowhere else) is what makes "dismiss mid-babble cuts her
   * instantly" true by construction instead of by discipline. See trap 70. */
  function setBubble(text) {
    if (text == null || text === '') {
      bubble.hidden = true;
      bubble.textContent = '';
      bubble.classList.remove('dots', 'pop');
      if (vox) { try { vox.stop(); } catch (e) { /* a voice may never break a bubble */ } }
      return;
    }
    const s = String(text);
    const dots = /^\.{1,3}$/.test(s);
    bubble.textContent = s;
    bubble.hidden = false;
    bubble.classList.remove('dots', 'pop');
    if (dots) {
      bubble.classList.add('dots');
      if (vox) { try { vox.tick(); } catch (e) { /* noop */ } }
    } else {
      bubble.classList.add('pop'); stats.bubblesSeen += 1;
      /* THE MOOD IS DRAWN ONE LINE LATER. playChain hands us the bubble BEFORE
       * it hands the frame to `draw`, which is what resolves `bodyFrame` for a
       * makeSay line - so we ask on the microtask and read the pose she is
       * actually wearing. Same tick, same frame, chains.js untouched. */
      if (vox) {
        const speak = () => { try { vox.speak(s, { face: bodyFrame }); } catch (e) { /* noop */ } };
        if (typeof queueMicrotask === 'function') queueMicrotask(speak); else speak();
      }
    }
  }

  /* BODY MOVES GO ON THE ROOT. Agent A's keyframes animate `transform` on `.emi`
   * itself, which is also why widget.js positions her with left/top and never
   * with a transform of its own - the two would fight every frame. */
  let bodyTimer = null;
  function clearBody() {
    if (bodyTimer !== null) { clearTimeout(bodyTimer); bodyTimer = null; }
    for (const c of BODY_CLASSES) el.classList.remove(c);
  }
  function setBody(name) {
    clearBody();
    if (!name) return;
    const cls = BODY_ALIAS[name] || name;
    if (reducedMotion() && (cls === 'breath' || cls === 'shiver')) return;
    // Force a reflow so re-adding the same class re-runs the keyframe (trap 4's
    // lesson, one line instead of a whole reveal). `droop` fills FORWARDS, so a
    // move that is never taken off welds itself to the root.
    void el.offsetWidth;
    el.classList.add(cls);
    if (cls === 'breath') return;
    bodyTimer = setTimeout(() => {
      bodyTimer = null;
      el.classList.remove(cls);
      // Back to breathing, unless the player asked for stillness.
      if (!reducedMotion() && !hidden && enabled) { void el.offsetWidth; el.classList.add('breath'); }
    }, BODY_MS[name] || BODY_MS[cls] || 800);
  }

  function burst(kind) {
    if (!showFx || !kind) return;
    try { showFx(fxHost, kind); } catch (e) { say('emi: fx threw - ' + ((e && e.message) || e)); }
  }

  /** True while a SAY chain is typing or holding its line. */
  function saying() { return !!(current && current.protect); }
  /** True while any chain is running. */
  function busy() { return !!current; }

  function cancelChain() {
    if (current && current.handle && typeof current.handle.cancel === 'function') {
      try { current.handle.cancel(); } catch (e) { /* noop */ }
    }
    current = null;
  }

  /**
   * Run one chain object. A protected chain (a SAY) refuses to be replaced by an
   * unprotected one - law 3 in the header.
   * @returns {boolean} true when it started
   */
  function play(chain, opts) {
    const o = opts || {};
    if (!chain || typeof playChain !== 'function' || !painter) return false;
    if (saying() && !o.protect && !o.force) return false;
    cancelChain();
    killTimers();
    stopBlink();
    stopSway();
    clearBody();
    restGaze();      // a performance owns the whole glass; the lean eases home
    /* THE POSE. A chain that declares `bodyFrame` (chains.js owns that table)
     * HOLDS it for the whole run - a pose that flickered per 90ms glitch frame
     * would read as a broken sprite, not as a mood. A chain that declares none
     * - every line `makeSay` builds - is resolved FRAME BY FRAME off the face
     * instead, which is what keeps the typing dots at `idle` and lands the pose
     * with the reaction face. `opts.bodyFrame` overrides both (the pet streak
     * borrows the GLEE chain but wants the pet pose, not the ceremony one). */
    const chainFrame = frameKey(o.bodyFrame) || frameKey(chain.bodyFrame);
    if (chainFrame) setBodyFrame(chainFrame);
    const handle = playChain(chain, {
      draw: (text, fo) => { if (!chainFrame) setBodyFrame(frameForFace(text)); drawFace(text, fo); },
      bubble: (b) => setBubble(b),
      body: (cls) => setBody(cls),
      fx: (kind) => burst(kind),
      done: () => {
        current = null;
        if (typeof o.onDone === 'function') { try { o.onDone(); } catch (e) { /* noop */ } }
        idle();
      },
    });
    current = { handle, protect: !!o.protect };
    return true;
  }

  /** Draw one raw face string and hold it, then fall back to idle. */
  function raw(text, opts) {
    const o = opts || {};
    if (!painter) return false;
    if (saying() && !o.force) return false;
    cancelChain();
    killTimers();
    stopBlink();
    stopSway();
    restGaze();
    if (o.clearBubble !== false) setBubble(null);
    setBodyFrame(frameKey(o.bodyFrame) || frameForFace(text));
    drawFace(text, o.frameOpts || {});
    if (o.body) setBody(o.body);
    if (o.fx) burst(o.fx);
    const hold = typeof o.hold === 'number' ? o.hold : DIALS.RAW_HOLD_MS;
    if (hold > 0) later(() => idle(), hold);
    return true;
  }

  /** 0_0 + blink + breath. The resting state; only runs when nothing else does. */
  function idle() {
    cancelChain();
    killTimers();
    stopBlink();
    stopSway();
    setBubble(null);
    clearBody();
    // ARMS DOWN AT REST. This runs even faceless: the body png is the half of
    // EMI that never needed a 2d context.
    setBodyFrame('idle');
    if (!painter || hidden || !enabled) return;
    drawFace(IDLE_FACE, {});
    if (!reducedMotion()) el.classList.add('breath');
    blinkTimer = setInterval(() => {
      if (busy() || hidden || !enabled || dragging) return;
      drawFace(BLINK_FACE, {});
      later(() => { if (!busy() && !dragging) drawFace(IDLE_FACE, {}); }, DIALS.BLINK_HOLD_MS);
      emitGesture('blinkIdle');
    }, DIALS.BLINK_EVERY_MS);
    startSway();
  }

  /* ---------------------- gestures (the voice's ear) --------------------
   * A READ-ONLY TAP on the pointer verbs. EMI's own reaction runs first and is
   * completely unchanged; this only lets emi/voice.js hear that a pet, a fling
   * or an idle blink happened and decide whether the moment also earns a line.
   * A subscriber that throws is swallowed here, because a listener must never
   * be able to reach into a pointer handler. */
  const gestureSubs = new Set();
  function onGesture(cb) {
    if (typeof cb !== 'function') return () => {};
    gestureSubs.add(cb);
    return () => gestureSubs.delete(cb);
  }
  function emitGesture(kind, detail) {
    if (!gestureSubs.size) return;
    for (const cb of gestureSubs) {
      try { cb(kind, detail || {}); } catch (e) { /* noop */ }
    }
  }

  /* ---------------------- pointer: drag / pet / hide -------------------- */
  let dragging = false;
  let pressing = false;
  let pressId = null;
  let pressAt = 0;
  let grabX = 0, grabY = 0;         // pointer offset inside EMI
  let startX = 0, startY = 0;       // where the press began (the 6px threshold)
  let lastX = 0, lastY = 0, lastT = 0;
  let fastSince = 0;
  let wasFling = false;
  /* WHICH FACE THE DRAG IS CURRENTLY WEARING. `onMove` runs on every pointermove
   * and drawing is a full canvas repaint (measure + stroke + fill), so it must
   * only fire when the expression actually CHANGES, not for every frame she
   * happens to be travelling fast. */
  let dragFace = null;
  /* THE TWO THINGS A GESTURE HAS TO PROVE (field bug, 2026-08-24). `beginDrag`
   * also fires on the TIME threshold, so a slow click that never moved - the
   * click that focuses the window, say, landing on EMI's corner - used to end
   * as a "drag" that committed her existing position and emitted a dropAt with
   * nobody touching anything. A gesture now needs a pointer that TRAVELLED at
   * least DRAG_PX, and an event the platform did not mark synthetic. EMI's own
   * reactions are unchanged: this gates only what the VOICE is told. */
  let dragMax = 0;            // furthest the pointer got from the press point
  let dragTold = false;       // 'drag' is announced once, when it becomes real
  let trusted = true;         // ev.isTrusted, when the platform reports one
  let armTimer = null;
  let petTimes = [];
  let petCooldownUntil = 0;
  /* THE DANGLE. Carried, she tilts a few degrees against the direction of
   * travel and springs upright on release - an inline `rotate` on the root,
   * which is safe exactly while dragging: no body-move keyframe runs then, and
   * the ones that run at release (bounce/thud) out-rank an inline transform for
   * as long as they play. Smoothed so a jittery hand does not read as a shiver. */
  let dangleV = 0;            // smoothed horizontal speed, px/ms
  let dangleDeg = 0;          // the tilt currently applied

  function clearDangle(immediate) {
    dangleV = 0;
    dangleDeg = 0;
    if (!el.style) return;
    if (immediate || reducedMotion()) {
      el.style.transition = '';
      el.style.transform = '';
      return;
    }
    el.style.transition = 'transform ' + DIALS.DANGLE_SETTLE_MS + 'ms cubic-bezier(.2,1.5,.4,1)';
    el.style.transform = '';
    // Untracked on purpose: killTimers() must not be able to strand the
    // transition on the root (it would slow every later dangle, nothing more).
    setTimeout(() => { try { el.style.transition = ''; } catch (e) { /* noop */ } },
      DIALS.DANGLE_SETTLE_MS + 50);
  }

  /** Which ninth of the viewport a point is in: z0..z8 row-major, plus the row band. */
  function zoneOf(x, y, vp) {
    const col = clamp(Math.floor(x * 3 / Math.max(1, vp.w)), 0, 2);
    const row = clamp(Math.floor(y * 3 / Math.max(1, vp.h)), 0, 2);
    return { z: row * 3 + col, row: row === 0 ? 'top' : (row === 1 ? 'mid' : 'bottom') };
  }

  function disarmHold() {
    if (armTimer !== null) { clearTimeout(armTimer); armTimer = null; }
  }

  /** Paint a drag reaction, but only when it is not already on the glass. */
  function setDragFace(text) {
    if (dragFace === text) return;
    dragFace = text;
    // The pose follows the face the way a raw hold's does: `@_@` is unpaired, so
    // a carry rests at `idle`; `>.<` is paired, so a FLING wears `shock`.
    setBodyFrame(frameForFace(text));
    drawFace(text, {});
  }

  /* A PRESS THAT NEVER GETS ITS pointerup. `hide()` and `setEnabled(false)` can
   * both take EMI out from under a live finger (the API is callable from any
   * moment), and a latched `pressing`/`dragging` would then let the NEXT
   * pointerup commit a position read from a hidden element - rect 0,0, i.e. she
   * teleports to the top-left corner the next time she is restored. */
  /** Did this press actually become a DRAG a human would recognise? */
  function realDrag() { return trusted && dragMax >= DIALS.DRAG_PX; }

  function endPress() {
    if (pressId !== null) {
      try { if (el.releasePointerCapture) el.releasePointerCapture(pressId); } catch (e) { /* noop */ }
    }
    pressing = false;
    dragging = false;
    pressId = null;
    fastSince = 0;
    wasFling = false;
    dragFace = null;
    dragMax = 0;
    dragTold = false;
    disarmHold();
    clearDangle(true);
    poiForget();                     // W2a: the drag's fixture snapshot dies with the drag
    el.classList.remove('dragging', 'armed');
  }

  function inX(ev) {
    const tgt = ev && ev.target;
    if (!tgt) return false;
    if (tgt === xBtn) return true;
    return !!(tgt.closest && tgt.closest('.emi-x'));
  }

  function onDown(ev) {
    if (!enabled || hidden) return;
    /* TOUCH ALWAYS WINS (W2a, trap 75). A finger on her at ANY point of a field
     * trip ends it on the spot - she stays where the trip had put her, upright,
     * and the press below carries on into a perfectly ordinary drag. */
    cancelTrip({ stay: true });
    // The x is a real button: let it have its own click, and never start a drag
    // from it (a dismiss that could turn into a fling is a trap, not a feature).
    if (inX(ev)) return;
    pressing = true;
    dragging = false;
    wasFling = false;
    pressId = ev.pointerId;
    pressAt = nowMs();
    const r = el.getBoundingClientRect ? el.getBoundingClientRect() : { left: 0, top: 0 };
    grabX = ev.clientX - r.left;
    grabY = ev.clientY - r.top;
    startX = ev.clientX; startY = ev.clientY;
    lastX = ev.clientX; lastY = ev.clientY; lastT = pressAt;
    fastSince = 0;
    dragMax = 0;
    dragTold = false;
    trusted = ev.isTrusted !== false;
    try { if (el.setPointerCapture) el.setPointerCapture(ev.pointerId); } catch (e) { /* noop */ }
    // PRESS-AND-HOLD ARMS THE x. Touch has no hover, so this is the only way to
    // reach the dismiss affordance with a finger.
    disarmHold();
    armTimer = setTimeout(() => { armTimer = null; el.classList.add('armed'); }, DIALS.HOLD_MS);
    // Inside `.emi` only - this is the one place a preventDefault is legal here,
    // and it exists to stop the browser's native image-drag ghost.
    if (ev.cancelable && typeof ev.preventDefault === 'function') ev.preventDefault();
  }

  function beginDrag() {
    dragging = true;
    stats.drags += 1;
    touchSeen();
    el.classList.add('dragging');
    // The reaction is a raw face, not a chain: a chain would fight the next
    // frame of movement. A live SAY keeps the glass (law 3).
    poiSnapshot();                   // W2a: measure the fixtures ONCE, not per move
    if (!saying()) { cancelChain(); killTimers(); stopBlink(); stopSway(); clearBody(); setDragFace(carryFace()); }
  }

  function onMove(ev) {
    if (!pressing || ev.pointerId !== pressId) return;
    const dx = ev.clientX - lastX;
    const dy = ev.clientY - lastY;
    const t = nowMs();
    const dt = Math.max(1, t - lastT);
    const moved = Math.hypot(ev.clientX - lastX, ev.clientY - lastY);

    // The threshold is measured from where the PRESS began, never from the
    // previous move - a slow drag never crosses a per-move delta.
    const total = Math.hypot(ev.clientX - startX, ev.clientY - startY);
    if (total > dragMax) dragMax = total;
    if (!dragging) {
      if (total > DIALS.DRAG_PX || (t - pressAt) > DIALS.DRAG_MS) beginDrag();
    }
    // ...and the voice only hears about it once it is a journey, not a jiggle.
    if (dragging && !dragTold && realDrag()) { dragTold = true; emitGesture('drag'); }
    if (dragging) {
      const vp = viewport();
      const s = sizePx();
      const left = clamp(ev.clientX - grabX, DIALS.MARGIN, Math.max(DIALS.MARGIN, vp.w - s.w - DIALS.MARGIN));
      const top = clamp(ev.clientY - grabY, DIALS.MARGIN, Math.max(DIALS.MARGIN, vp.h - s.h - DIALS.MARGIN));
      el.style.left = Math.round(left) + 'px';
      el.style.top = Math.round(top) + 'px';
      faceBubble(left, top, s.w, vp.w);

      // FLUNG? speed over the dial, sustained. `>.<` while it lasts. setDragFace
      // swallows the repeats, so a sustained fling repaints the canvas ONCE
      // instead of once per pointermove.
      const speed = Math.hypot(dx, dy) / dt;
      if (speed > DIALS.FLING_SPEED) {
        if (!fastSince) fastSince = t;
        else if (t - fastSince > DIALS.FLING_MS && !saying()) { wasFling = true; setDragFace(FLING_FACE); }
      } else if (fastSince) {
        fastSince = 0;
        if (!saying()) setDragFace(carryFace());
      }

      /* FIELD-TRIP ANTICIPATION (W2a). Carried over a fixture she has a line
       * about, she lights up. ENTER/LEAVE ONLY: the rects were measured once at
       * beginDrag and the repaint rides setDragFace's dedupe, so a pointermove
       * over open ground costs a handful of number compares and nothing else. */
      if (poiCache) {
        const onPoi = overPoi(left, top);
        if (onPoi !== poiOver) {
          poiOver = onPoi;
          if (!saying() && dragFace !== FLING_FACE) setDragFace(carryFace());
        }
      }

      // THE DANGLE: lean against the travel, clamped and smoothed. Style is
      // only written when the tilt moved a visible amount.
      if (!reducedMotion()) {
        dangleV = dangleV * 0.8 + (dx / dt) * 0.2;
        const deg = clamp(-dangleV * DIALS.DANGLE_K, -DIALS.DANGLE_MAX_DEG, DIALS.DANGLE_MAX_DEG);
        if (Math.abs(deg - dangleDeg) > 0.3) {
          dangleDeg = deg;
          try { el.style.transform = 'rotate(' + deg.toFixed(1) + 'deg)'; } catch (e) { /* noop */ }
        }
      }
    }
    if (moved > 0) { lastX = ev.clientX; lastY = ev.clientY; lastT = t; }
  }

  function onUp(ev) {
    if (!pressing || (pressId !== null && ev.pointerId !== pressId)) return;
    pressing = false;
    disarmHold();
    try { if (el.releasePointerCapture) el.releasePointerCapture(ev.pointerId); } catch (e) { /* noop */ }
    const held = nowMs() - pressAt;
    pressId = null;

    if (dragging) {
      dragging = false;
      dragFace = null;
      el.classList.remove('dragging');
      clearDangle(false);
      const r = el.getBoundingClientRect ? el.getBoundingClientRect() : { left: 0, top: 0 };
      commit(r.left, r.top);
      if (wasFling) stats.flings += 1;
      // SETTLE: ^_^ for a beat, then back to resting. The landing move is the
      // playbook's BOUNCE, or a THUD when she was thrown.
      if (!saying()) {
        cancelChain(); killTimers(); stopBlink(); stopSway();
        setBodyFrame(frameForFace(SETTLE_FACE));
        drawFace(SETTLE_FACE, {});
        if (!reducedMotion()) setBody(wasFling ? 'thud' : 'bounce');
        later(() => idle(), DIALS.SETTLE_MS);
      }
      const flung = wasFling;
      const travelled = realDrag();
      wasFling = false;
      if (travelled) {
        // WHERE SHE LANDED, in viewport coordinates: her CENTRE, which is the
        // point a hit-test should ask about. voice.js does the asking. A press
        // that never travelled is NOT a drop - she is exactly where she was.
        const sz = sizePx();
        const px = Math.round(r.left + sz.w / 2);
        const py = Math.round(r.top + sz.h / 2);
        // ...and the SPOT MEMORY: which ninth of the window she was put down
        // in, counted for life. The favourite-spot beats read the count off
        // this payload, so the voice never has to open the widget's blob.
        const zi = zoneOf(px, py, viewport());
        const zk = 'z' + zi.z;
        stats.zones[zk] = (stats.zones[zk] || 0) + 1;
        // ONE write per interaction, on the END of it (never per pointermove).
        save();
        if (flung) emitGesture('fling');
        emitGesture('dropAt', { x: px, y: py, zone: zi.z, zoneRow: zi.row, zoneCount: stats.zones[zk] });
      } else {
        save();
      }
      return;
    }
    // NOT A DRAG. A short press is a PET; a long hold was the x-arming gesture
    // and is deliberately not a pet (the player was reaching for the dismiss).
    el.classList.remove('dragging');
    if (held < DIALS.HOLD_MS && ev.isTrusted !== false) pet();
  }

  function onCancel() {
    if (!pressing) return;
    const wasDragging = dragging;
    // The rect has to be read BEFORE endPress clears the classes - a cancel that
    // banked nothing would lose the whole drag.
    const r = wasDragging && el.getBoundingClientRect ? el.getBoundingClientRect() : null;
    endPress();
    if (wasDragging) {
      commit(r ? r.left : 0, r ? r.top : 0);
      save();
      if (!saying()) idle();
    }
  }

  /** A pet: positive cycle; PET_TARGET inside the window buys the glee chain. */
  function pet() {
    if (!enabled || hidden) return;
    // LAW 3: a line in flight is never cut for a head-pat. Ignore, do not queue -
    // a reaction that lands four seconds late reads as a glitch.
    if (saying()) return;
    stats.pets += 1;
    touchSeen();
    const t = nowMs();
    if (t < petCooldownUntil) {
      // Spam guard: she still notices you, she just does not do the whole show.
      if (CHAINS && CHAINS.wink) play(CHAINS.wink, { bodyFrame: 'pet' });
      else raw('^_~', { hold: 500, bodyFrame: 'pet' });
      save();
      emitGesture('pet', { cooled: true });
      return;
    }
    petTimes = petTimes.filter((p) => t - p < DIALS.PET_WINDOW_MS);
    petTimes.push(t);
    if (petTimes.length >= DIALS.PET_TARGET) {
      petTimes = [];
      petCooldownUntil = t + DIALS.PET_COOLDOWN_MS;
      stats.petStreaks3 += 1;
      // THE LOCK SAYS THE THIRD PET LANDS ON (≧◡≦) - that is `glee`, not
      // `love` (which ends on the lovestruck kaomoji and is a different beat).
      /* THE POSE IS THE PET ONE, not the ceremony one. `glee` is shared: it is
       * also the streak-STAMP beat, where the arms-up frame is the right answer
       * - so the override rides the CALL SITE, never the chain. */
      const cycle = CHAINS && (CHAINS.glee || CHAINS.love);
      if (cycle) play(cycle, { bodyFrame: 'pet' });
      else raw('(≧◡≦)', { hold: 1400, fx: 'hearts', body: 'bounce', bodyFrame: 'pet' });
      save();
      emitGesture('pet');
      emitGesture('petStreak3');
      return;
    }
    raw(SETTLE_FACE, { hold: 900, fx: 'hearts', body: reducedMotion() ? null : 'bounce', bodyFrame: 'pet' });
    save();
    emitGesture('pet');
  }

  /* ---------------------- hide / dock ----------------------------------- */
  function hide(opts) {
    if (hidden) return;
    const o = opts || {};
    hidden = true;
    accrueVisible();
    if (!o.silent) { stats.hides += 1; touchSeen(); }
    // A live press has to be let go BEFORE she goes: a latched drag would let the
    // next pointerup commit a position read off a display:none element.
    endPress();
    cancelTrip();                    // W2a: dismissed mid-trip = home first, then the dock
    cancelChain(); killTimers(); stopBlink(); stopSway(); clearBody(); setBubble(null);
    clearApproachTimers();
    restGaze();
    try { canvas.style.transform = ''; } catch (e) { /* noop */ }
    el.hidden = true;
    dock.hidden = false;
    /* THE ONE-SHOT HINT. The dock is 28px and .35 opacity in a corner, so the
     * very first dismissal says where she went - once, ever, and only for the x
     * (an API hide is the shell's doing, not a thing the player needs told). */
    if (o.fromX && !hintShown) {
      hintShown = true;
      try { shout(HINT_LINE); } catch (e) { /* a toast may never hold a dismiss */ }
    }
    save(true);
    if (!o.silent) emitGesture('hide', { fromX: !!o.fromX });
  }

  function show(opts) {
    if (!hidden && el.hidden === false) return;
    hidden = false;
    if (!(opts && opts.silent)) { stats.dockRestores += 1; touchSeen(); }
    el.hidden = false;
    dock.hidden = true;
    place();
    beginVisible();
    idle();
    save(true);
    if (!(opts && opts.silent)) emitGesture('restore');
  }

  xBtn.addEventListener('click', (ev) => {
    // Inside `.emi`: stopping this one is legal and necessary (the pet handler
    // must not also fire). Nothing outside the widget is touched.
    if (ev && typeof ev.stopPropagation === 'function') ev.stopPropagation();
    hide({ fromX: true });
  });
  // The x owns its own pointer stream so a press on it can never start a drag.
  xBtn.addEventListener('pointerdown', (ev) => { if (ev && ev.stopPropagation) ev.stopPropagation(); });
  dock.addEventListener('click', () => show());

  el.addEventListener('pointerdown', onDown);
  el.addEventListener('pointermove', onMove);
  el.addEventListener('pointerup', onUp);
  el.addEventListener('pointercancel', onCancel);
  el.addEventListener('pointerleave', () => { if (!pressing) el.classList.remove('armed'); });

  /* ---------------------- perception ------------------------------------
   * She notices you BEFORE you touch her. Three pieces, all read off one
   * document-level pointermove (rAF-shaped, ~16 samples/s):
   *
   *   GAZE      the face leans a few px toward the cursor. NOT a canvas
   *             repaint - the glyph is expensive to draw (widget header), so
   *             the lean is a CSS transform on the canvas element, which is
   *             free. Idle-only: a chain, a say or a drag eases it home.
   *   APPROACH  cursor near her edge = a perk (o_o); arriving FAST = the
   *             glance chain. One per APPROACH_COOLDOWN_MS.
   *   LINGER    hovering without committing = expectant, then one look-away
   *             if the pet never comes. The episode resets when you leave.
   *
   * All of it is a spectator: it plays through raw()/play() like every other
   * reaction, so law 3 (a SAY is never cut) holds without a special case, and
   * the voice hears `approach`/`hoverLinger` through the same gesture tap as
   * every pointer verb (no pools yet - wave 2b's writing slot). */
  let gazeTX = 0, gazeTY = 0, gazeX = 0, gazeY = 0, gazeRaf = null;
  let apInside = false, apCoolUntil = 0, apPets0 = 0;
  let apLingerTimer = null, apAwayTimer = null;
  let apLastX = 0, apLastY = 0, apLastT = 0;

  function gazeActive() {
    return !!painter && !hidden && enabled && !dragging && !busy() && !reducedMotion();
  }
  function gazeStep() {
    gazeRaf = null;
    const ax = gazeActive() ? gazeTX : 0;
    const ay = gazeActive() ? gazeTY : 0;
    gazeX += (ax - gazeX) * DIALS.GAZE_EASE;
    gazeY += (ay - gazeY) * DIALS.GAZE_EASE;
    // SETTLED = STOP. The loop only runs while the lean is travelling; a
    // pointermove (or restGaze) nudges it back to life. A resting rAF that
    // rewrote the same transform every frame would be the repaint-per-move
    // mistake in a nicer hat.
    if (Math.abs(gazeX - ax) + Math.abs(gazeY - ay) < 0.05) {
      gazeX = ax; gazeY = ay;
      try { canvas.style.transform = ax === 0 && ay === 0 ? '' : 'translate(' + ax.toFixed(2) + 'px,' + ay.toFixed(2) + 'px)'; } catch (e) { /* noop */ }
      return;
    }
    try { canvas.style.transform = 'translate(' + gazeX.toFixed(2) + 'px,' + gazeY.toFixed(2) + 'px)'; } catch (e) { /* noop */ }
    if (typeof requestAnimationFrame === 'function') gazeRaf = requestAnimationFrame(gazeStep);
  }
  function nudgeGaze() {
    if (gazeRaf == null && typeof requestAnimationFrame === 'function') {
      gazeRaf = requestAnimationFrame(gazeStep);
    }
  }
  /** Ease the lean home NOW (a hide, a disable, a drag taking over). */
  function restGaze() {
    gazeTX = 0; gazeTY = 0;
    nudgeGaze();
  }

  function clearApproachTimers() {
    if (apLingerTimer !== null) { clearTimeout(apLingerTimer); apLingerTimer = null; }
    if (apAwayTimer !== null) { clearTimeout(apAwayTimer); apAwayTimer = null; }
  }
  /** May a perception face go up right now? Never over a chain, a say, a press. */
  function canPerk() {
    return !!painter && !hidden && enabled && !pressing && !dragging && !busy() && !saying();
  }

  function onDocMove(ev) {
    if (!enabled || hidden || !ev) return;
    const t = nowMs();
    if (t - apLastT < 60) return;                       // ~16 samples/s is plenty
    const px = apLastX, py = apLastY, pt = apLastT;
    apLastX = ev.clientX; apLastY = ev.clientY; apLastT = t;

    let r = null;
    try { r = el.getBoundingClientRect ? el.getBoundingClientRect() : null; } catch (e) { /* noop */ }
    if (!r || !r.width) return;
    const cx = r.left + r.width / 2;
    const cy = r.top + r.height / 2;
    const dx = ev.clientX - cx;
    const dy = ev.clientY - cy;
    const d = Math.hypot(dx, dy);

    // GAZE: the lean is proportional and capped, and the loop eases it home
    // on its own the moment gazeActive() goes false.
    gazeTX = clamp(dx / DIALS.GAZE_DIV, -DIALS.GAZE_MAX_PX, DIALS.GAZE_MAX_PX);
    gazeTY = clamp(dy / DIALS.GAZE_DIV, -DIALS.GAZE_MAX_PX, DIALS.GAZE_MAX_PX);
    nudgeGaze();

    // APPROACH: measured from her EDGE, so a bigger EMI is not a bigger doorbell.
    const inside = d < r.width / 2 + DIALS.APPROACH_PX;
    if (inside === apInside) return;
    apInside = inside;
    if (!inside) { clearApproachTimers(); return; }

    if (t < apCoolUntil) return;
    apCoolUntil = t + DIALS.APPROACH_COOLDOWN_MS;
    apPets0 = stats.pets;
    // Arriving fast earns the GLANCE (she tracks the fly-by); walking up earns
    // the quiet perk. Both are idle-frame on purpose - noticing is not a beat.
    const speed = pt > 0 ? Math.hypot(ev.clientX - px, ev.clientY - py) / Math.max(1, t - pt) : 0;
    if (canPerk()) {
      if (speed > DIALS.GLANCE_SPEED && CHAINS && CHAINS.glance) play(CHAINS.glance, { bodyFrame: 'idle' });
      else raw('o_o', { hold: 900, bodyFrame: 'idle' });
    }
    emitGesture('approach', { fast: speed > DIALS.GLANCE_SPEED });

    clearApproachTimers();
    apLingerTimer = setTimeout(() => {
      apLingerTimer = null;
      if (!apInside || !canPerk()) return;
      raw('^_^', { hold: 1100, bodyFrame: 'idle' });
      emitGesture('hoverLinger');
      apAwayTimer = setTimeout(() => {
        apAwayTimer = null;
        // Still hovering, still no pet: one small look-away. She is not hurt,
        // she is making a point.
        if (!apInside || !canPerk() || stats.pets > apPets0) return;
        raw('¬_¬', { hold: 1000, bodyFrame: 'idle' });
      }, DIALS.LINGER_AWAY_MS);
    }, DIALS.LINGER_MS);
  }
  if (typeof document !== 'undefined' && document.addEventListener) {
    document.addEventListener('pointermove', onDocMove, { passive: true });
  }

  /* ---------------------- viewport ------------------------------------- */
  /* A resize re-derives the default width AND re-clamps the anchor, in that
   * order - clamping against the old size would park her by a stale edge. */
  // Seeded from the boot-time window: a page that OPENS narrow never "squished".
  let wasNarrowVp = viewport().w < DIALS.W_NARROW_VW;
  function onResize() {
    /* A RESIZE MOVED THE FIXTURE (W2a, trap 73). The anchor she is standing at
     * was solved against the old viewport, so the trip is over: she goes home
     * and the scheduler may offer another one later. */
    cancelTrip();
    refitWidth();
    if (!hidden && enabled) place();
    // THE SQUISH: crossing into the narrow-window regime, once per crossing.
    // The voice's one-shot beat makes "once ever" out of it; later crossings
    // reach a beat already seen and land as silence.
    const narrow = viewport().w < DIALS.W_NARROW_VW;
    if (narrow !== wasNarrowVp) {
      wasNarrowVp = narrow;
      if (narrow && !hidden && enabled) emitGesture('windowSquish');
    }
  }
  if (typeof window !== 'undefined' && window.addEventListener) {
    window.addEventListener('resize', onResize);
  }

  /* THE LAST FLUSH. msVisible is only true if it is banked before the page goes,
   * and a debounced write in flight would be lost with it. */
  function flush() { accrueVisible(); beginVisible(); save(true); }
  const onPageHide = () => { accrueVisible(); save(true); };
  if (typeof window !== 'undefined' && window.addEventListener) {
    window.addEventListener('pagehide', onPageHide);
  }


  /* ========================================================================
   * THE FIELD TRIP (wave W2a, 2026-08-24) - EMI'S ONE AUTONOMOUS VERB
   *
   * Everything this wave adds to the widget lives between these two rules,
   * deliberately: `feat/emi-off-channels` is editing this file at the same
   * time. The only code outside the block is eight one-line hooks, each marked
   * `W2a` - endPress, onDown, beginDrag, onMove, hide, onResize, setEnabled,
   * destroy - plus three members on the handle.
   *
   * WHAT A TRIP IS. She powers her tube off where she stands, reappears beside
   * a campus fixture, says one line about it, powers off again and comes back
   * to the player's saved spot. It is a scripted rare delight and NOT wandering:
   * WHEN it may happen is `emi/fieldtrips.js`'s problem, and it is the only
   * caller. This file owns HOW, and the how is four laws:
   *
   *   1. THE RECT IS RESOLVED AT FIRE TIME, NEVER AT SCHEDULE TIME. Screens
   *      resize, the campus repaints, the plan is `xMidYMid slice` - a rect
   *      captured when the trip was queued is a rect that has moved by the
   *      time she gets there. `apparate` therefore takes a GETTER and calls it
   *      inside the dark, one frame before she lands. See trap 73.
   *   2. TOUCH ALWAYS WINS. A pointerdown on her at any point of the ladder
   *      ends the trip on the spot: she stays where she is, upright, with no
   *      stranded animation class, and the press carries on into an ordinary
   *      drag. Trap 75.
   *   3. SHE NEVER STARTS ONE OVER HERSELF. Mid-say, mid-chain, mid-press,
   *      dismissed, disabled or already travelling, `apparate` refuses and
   *      answers null. The caller does not need a guard.
   *   4. THE SAVED SPOT IS NEVER WRITTEN. The trip moves `el.style.left/top`
   *      and never `fx0`/`fy0`, so "come home" is just `place()` and a crash,
   *      a reload or a cancel can never lose where the player put her. The one
   *      exception is the touch cancel, which commits the spot she was ACTUALLY
   *      standing on - from the trip's own bookkeeping, never from
   *      `getBoundingClientRect`, which mid-squish reports a 1px line.
   * ======================================================================*/

  /** The carried face over a registered fixture: pure delight. `*_*` is already
   *  paired to the `pet` pose in FACE_BODY_FRAME, so the body follows for free. */
  const POI_FACE = '*_*';

  /** Normalise anything rect-shaped (a DOMRect, a plain object, a getter's
   *  answer) into our own numbers, or null. Never throws on junk. */
  function rectNow(src) {
    let r = null;
    try { r = typeof src === 'function' ? src() : src; } catch (e) { return null; }
    if (!r || typeof r !== 'object') return null;
    const left = num(r.left);
    const top = num(r.top);
    if (left === null || top === null) return null;
    let w = num(r.width);
    let h = num(r.height);
    if (w === null) { const rt = num(r.right); w = rt === null ? null : rt - left; }
    if (h === null) { const bt = num(r.bottom); h = bt === null ? null : bt - top; }
    if (w === null || h === null || !(w > 0) || !(h > 0)) return null;
    return { left, top, width: w, height: h, right: left + w, bottom: top + h };
  }

  /** How much of her box would sit on top of the fixture, in square px. */
  function overlapArea(left, top, s, rect) {
    const ox = Math.min(left + s.w, rect.right) - Math.max(left, rect.left);
    const oy = Math.min(top + s.h, rect.bottom) - Math.max(top, rect.top);
    return (ox > 0 && oy > 0) ? ox * oy : 0;
  }

  /**
   * WHERE SHE STANDS TO LOOK AT SOMETHING. Four candidates - right, left, under,
   * over - each clamped into the viewport and then scored. The clamp runs BEFORE
   * the overlap test on purpose: against a fixture in the corner the clamp is
   * what drags a candidate back over the very thing she came to see, and a test
   * on the raw candidate would never notice.
   */
  function anchorFor(rect) {
    const vp = viewport();
    const s = sizePx();
    const g = DIALS.TRIP_GAP_PX;
    const lo = DIALS.MARGIN;
    const maxX = Math.max(lo, vp.w - s.w - lo);
    const maxY = Math.max(lo, vp.h - s.h - lo);
    const midY = rect.top + rect.height / 2 - s.h / 2;
    const midX = rect.left + rect.width / 2 - s.w / 2;
    const cands = [
      { left: rect.right + g, top: midY },
      { left: rect.left - g - s.w, top: midY },
      { left: midX, top: rect.bottom + g },
      { left: midX, top: rect.top - g - s.h },
    ];
    let best = null;
    for (const c of cands) {
      const left = Math.round(clamp(c.left, lo, maxX));
      const top = Math.round(clamp(c.top, lo, maxY));
      const over = overlapArea(left, top, s, rect);
      const pushed = Math.abs(left - c.left) + Math.abs(top - c.top);
      // Clean beats compromised; among compromises, least overlap then least
      // clamping. There is ALWAYS an answer - a mascot with nowhere to stand
      // would be a trip that hangs half way through.
      if (!best || over < best.over || (over === best.over && pushed < best.pushed)) {
        best = { left, top, over, pushed };
      }
      if (over === 0 && pushed < 1) break;
    }
    return { left: best.left, top: best.top };
  }

  /* ---- the drag's fixture snapshot ------------------------------------ */
  /* MEASURED ONCE PER DRAG, NEVER PER MOVE. A getBoundingClientRect for every
   * registered fixture on every pointermove is the same mistake the drag-face
   * dedupe exists to prevent, multiplied by the size of the registry - and a
   * drag cannot resize the window, so one snapshot is also the correct answer. */
  let poiRectsFn = null;
  let poiCache = null;
  let poiOver = false;

  function poiForget() { poiCache = null; poiOver = false; }

  function poiSnapshot() {
    poiForget();
    if (!poiRectsFn) return;
    let list = null;
    try { list = poiRectsFn(); } catch (e) { return; }
    if (!Array.isArray(list) || !list.length) return;
    const out = [];
    for (const r of list) { const n = rectNow(r); if (n) out.push(n); }
    if (out.length) poiCache = out;
  }

  /** Is her CENTRE inside one of the snapshotted fixtures? */
  function overPoi(left, top) {
    if (!poiCache) return false;
    const s = sizePx();
    const cx = left + s.w / 2;
    const cy = top + s.h / 2;
    for (const r of poiCache) {
      if (cx >= r.left && cx <= r.right && cy >= r.top && cy <= r.bottom) return true;
    }
    return false;
  }

  /** The face a CARRY currently wears, fixture or not. The fling face outranks
   *  both while it lasts; this is only what she falls back to. */
  function carryFace() { return poiOver ? POI_FACE : DRAG_FACE; }

  /* ---- the tube ------------------------------------------------------- */
  /* THE POWER-OFF IS A KEYFRAME AND THAT IS THE WHOLE ARGUMENT (trap 71). The
   * `.emi` root's INLINE transform belongs to the dangle; a CSS animation
   * out-ranks an inline style for as long as it runs, which is exactly why the
   * squish is allowed to own the root and the carry tilt is not disturbed.
   * The reflow is trap 4's lesson: re-adding the same class without one is a
   * keyframe the browser coalesces away. */
  function crtClear() {
    try {
      el.classList.remove('crt-off');
      el.classList.remove('crt-on');
      el.classList.remove('crt-blank');
    } catch (e) { /* noop */ }
  }
  function crt(cls) {
    crtClear();
    if (!cls) return;
    try { void el.offsetWidth; el.classList.add(cls); } catch (e) { /* noop */ }
  }
  /** 0 under reduced motion: the CSS drops the squish, so waiting for it would
   *  only be a pause in an empty room. */
  function crtMs() { return reducedMotion() ? 0 : DIALS.CRT_MS; }

  /* ---- the trip ------------------------------------------------------- */
  /* {timer, left, top, onDone} - `left`/`top` are the trip's OWN bookkeeping of
   * where it last put her, because a rect read mid-squish is a 1px line. */
  let trip = null;

  function tripping() { return !!trip; }

  /** One step at a time, on a timer this trip owns. NOT `later()`: the say in
   *  the middle goes through `play()`, and `play()` calls `killTimers()`. */
  function tripStep(ms, fn) {
    if (!trip) return;
    if (trip.timer !== null) clearTimeout(trip.timer);
    const mine = trip;
    trip.timer = setTimeout(() => {
      if (trip !== mine) return;
      trip.timer = null;
      try { fn(); }
      catch (e) { say('emi: trip step threw - ' + ((e && e.message) || e)); cancelTrip(); }
    }, Math.max(0, ms));
  }

  /** Move her for the TRIP only: style plus the bubble flip, never the fractions. */
  function tripPlaceAt(left, top) {
    const vp = viewport();
    const s = sizePx();
    if (trip) { trip.left = left; trip.top = top; }
    try {
      el.style.left = Math.round(left) + 'px';
      el.style.top = Math.round(top) + 'px';
    } catch (e) { /* noop */ }
    faceBubble(left, top, s.w, vp.w);
  }

  /**
   * END A TRIP. Two shapes and they are genuinely different:
   *   cancelTrip()            -> she comes HOME (a dismiss, a resize, a disable,
   *                              a destroy, the caller's own cancel)
   *   cancelTrip({stay:true}) -> she stops WHERE SHE IS and that spot becomes
   *                              hers (a finger landed on her: trap 75)
   * Both clear every animation class and every protected say, so whatever
   * happens next starts from a mascot in a known state.
   */
  function cancelTrip(opts) {
    if (!trip) return false;
    const t = trip;
    trip = null;
    if (t.timer !== null) { clearTimeout(t.timer); t.timer = null; }
    /* THE FILL IS FORWARDS, SO TAKING THE CLASS OFF IS NOT OPTIONAL (trap 74).
     * A welded `scaleY(1)` would out-rank the dangle's inline rotate for ever. */
    crtClear();
    cancelChain();
    killTimers();
    setBubble(null);
    const stay = !!(opts && opts.stay);
    if (stay) { commit(t.left, t.top); save(); }
    else place();
    if (typeof t.onDone === 'function') {
      try { t.onDone({ cancelled: true, stay }); } catch (e) { /* noop */ }
    }
    idle();
    return true;
  }

  /**
   * APPARATE: the whole trip, as one call.
   *
   * @param {Function|Object} getRect  a RECT-GETTER, e.g.
   *        `() => node.getBoundingClientRect()`. A bare rect is accepted and is
   *        a bug waiting to happen - see law 1 and trap 73.
   * @param {{line?:string, face?:string, onDone?:Function}=} opts
   * @returns {Function|null} a cancel function, or null when she refused.
   */
  function apparate(getRect, opts) {
    const o = opts || {};
    if (!getRect) return null;
    // LAW 3: she never starts one over herself.
    if (!enabled || hidden || trip || pressing || dragging || busy() || saying()) return null;
    if (!el || !el.classList) return null;
    // Nothing to travel TO is not a failure, it is a fixture that is not on
    // screen. Answer null and let the scheduler try again another night.
    if (!rectNow(getRect)) return null;

    const line = typeof o.line === 'string' && o.line.trim() ? o.line : null;
    const face = typeof o.face === 'string' && o.face ? o.face : '^_^';
    trip = { timer: null, left: 0, top: 0, onDone: typeof o.onDone === 'function' ? o.onDone : null };

    // Where she is standing right now, in pixels, so a cancel during the very
    // first squish still knows the spot it is being asked to keep.
    const start = place();
    trip.left = start.left;
    trip.top = start.top;

    stopBlink();
    stopSway();
    clearBody();
    restGaze();

    /* 1. THE TUBE GOES OFF. */
    crt('crt-off');
    tripStep(crtMs(), () => {
      /* 2. THE DARK. The rect is resolved HERE - law 1 - and a fixture that has
       *    gone since the trip was scheduled sends her straight home. */
      crt('crt-blank');
      const rect = rectNow(getRect);
      if (!rect) { cancelTrip(); return; }
      const spot = anchorFor(rect);
      tripPlaceAt(spot.left, spot.top);
      crt('crt-on');
      tripStep(crtMs(), () => {
        /* 3. THE LINE. Through the ordinary say path, so the bubble, the pose,
         *    the hold and the Blipese babble are all the ones she already has
         *    (trap 70: the voice hangs off setBubble and nowhere else). */
        crtClear();
        let wait = DIALS.TRIP_BEAT_MS;
        if (line) {
          wait = SAY_LEAD_MS + sayHoldMs(line) + DIALS.TRIP_BEAT_MS;
          let spoke = false;
          if (makeSay && painter) {
            try { spoke = play(makeSay(line, face, sayHoldMs(line)), { protect: true, force: true }); }
            catch (e) { spoke = false; }
          }
          // FACELESS STILL TALKS. The body png and the bubble are the half of
          // EMI that never needed a 2d context, and the trip is worth more than
          // the reaction frame on top of it.
          if (!spoke) { setBubble(line); wait = sayHoldMs(line) + DIALS.TRIP_BEAT_MS; }
        }
        tripStep(wait, () => {
          /* 4. HOME. The same two beats, backwards. */
          cancelChain();
          killTimers();
          setBubble(null);
          crt('crt-off');
          tripStep(crtMs(), () => {
            crt('crt-blank');
            // `place()` reads the fractions the trip never touched, which IS the
            // player's saved spot (law 4). No arithmetic, so no drift.
            const home = place();
            if (trip) { trip.left = home.left; trip.top = home.top; }
            crt('crt-on');
            tripStep(crtMs(), () => {
              const t = trip;
              trip = null;
              crtClear();
              idle();
              if (t && typeof t.onDone === 'function') {
                try { t.onDone({ cancelled: false, stay: false }); } catch (e) { /* noop */ }
              }
            });
          });
        });
      });
    });

    return () => cancelTrip();
  }

  /** THE REGISTRY SEAM. `emi/fieldtrips.js` hands over a function answering the
   *  live rects of every fixture EMI has a line about; the widget uses them for
   *  exactly ONE thing, the carried `*_*`. Null clears it. */
  function setPoiRects(fn) {
    poiRectsFn = typeof fn === 'function' ? fn : null;
    if (!poiRectsFn) poiForget();
  }
  /* ===================== end of the field trip ========================== */

  /* ---------------------- first paint ----------------------------------- */
  built = true;
  place();
  if (hidden) { el.hidden = true; dock.hidden = false; }
  else { el.hidden = false; dock.hidden = true; beginVisible(); if (painter) idle(); }

  /* ---------------------- the handle ------------------------------------ */
  return {
    el, dock, canvas, fxHost, bubble,
    attach,
    /** Subscribe to the pointer verbs (emi/voice.js). Returns an unsubscribe. */
    onGesture,
    play, raw, idle,
    /** Swap the body png by frame key (BODY_FRAME_SRC). Test/host seam. */
    setBodyFrame,
    /** Which pose is up right now. */
    get bodyFrame() { return bodyFrame; },
    /** True while EMI is walking the idle sway (reduced motion never is). */
    swaying() { return swayTimer !== null; },
    makeSayFn() { return makeSay; },
    chainsTable() { return CHAINS; },
    hasFace() { return !!painter; },
    saying, busy,
    setBubble,
    /* W2a - the field trip. `apparate` takes a RECT-GETTER (trap 73), refuses
     * over any live verb, and answers a cancel function or null. */
    apparate, setPoiRects, tripping,
    hide, show,
    get hidden() { return hidden; },
    /** True once the first-dismiss hint has been spent (persisted). */
    get hintShown() { return hintShown; },
    /**
     * THE RESIZE SEAM. There is no UI for it yet; the moment there is, calling
     * this is what turns the width from "the window's default" into "the
     * player's choice", and only then does `w` start being persisted.
     */
    setWidth(px) {
      const n = num(px);
      if (n === null) return width;
      const next = clamp(Math.round(n), DIALS.W_MIN, DIALS.W_MAX);
      userSized = true;
      if (next !== width) { width = next; if (!hidden && enabled) place(); }
      save();
      return width;
    },
    get width() { return width; },
    setEnabled(on) {
      const next = !!on;
      if (next === enabled) return;
      enabled = next;
      if (!enabled) {
        accrueVisible();
        endPress();
        cancelTrip();                // W2a: switched off mid-trip = home first
        cancelChain(); killTimers(); stopBlink(); stopSway(); clearBody();
        root.hidden = true;
        save(true);
      } else {
        root.hidden = false;
        if (!hidden) { place(); beginVisible(); idle(); }
      }
    },
    get enabled() { return enabled; },
    /** Read-only lifetime telemetry (a copy - nothing outside may mutate it). */
    stats() {
      return Object.assign({}, stats, { msVisible: visibleMs(), zones: Object.assign({}, stats.zones) });
    },
    /** Test/host seam: force the debounced write out now. */
    flush,
    destroy() {
      accrueVisible();
      cancelTrip();                  // W2a: never leave a trip timer behind
      save(true);
      // clearBody() is the easy one to miss: `bodyTimer` outlives everything else
      // and its callback re-adds `.breath` to a node that is no longer in the page.
      cancelChain(); killTimers(); stopBlink(); stopSway(); disarmHold(); clearBody();
      clearApproachTimers();
      if (gazeRaf != null && typeof cancelAnimationFrame === 'function') {
        cancelAnimationFrame(gazeRaf); gazeRaf = null;
      }
      // Her voice is a setTimeout ladder of its own and nothing above clears it.
      if (vox) { try { vox.stop(); } catch (e) { /* noop */ } }
      if (saveTimer !== null) { clearTimeout(saveTimer); saveTimer = null; }
      if (typeof document !== 'undefined' && document.removeEventListener) {
        document.removeEventListener('pointermove', onDocMove);
      }
      if (typeof window !== 'undefined' && window.removeEventListener) {
        window.removeEventListener('resize', onResize);
        window.removeEventListener('pagehide', onPageHide);
      }
      try { el.remove(); } catch (e) { /* noop */ }
      try { dock.remove(); } catch (e) { /* noop */ }
    },
  };
}

export default createWidget;
