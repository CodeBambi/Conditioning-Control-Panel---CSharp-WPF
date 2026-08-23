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

/* ---------------------- dials (designer-tunable) ------------------------- */
export const DIALS = Object.freeze({
  W_DEFAULT: 150,          // px, EMI's width; the aspect comes from the PNG
  W_MIN: 110,
  W_MAX: 220,
  ASPECT_W: 859,           // mascot-body-blank.png
  ASPECT_H: 869,
  MARGIN: 4,               // px kept between EMI and the viewport edge
  DRAG_PX: 6,              // move this far and it is a DRAG, not a pet
  DRAG_MS: 250,            // ...or hold this long
  HOLD_MS: 450,            // press-and-hold arms the x affordance
  FLING_SPEED: 1.5,        // px/ms
  FLING_MS: 120,           // ...sustained this long = >.< and a THUD landing
  SETTLE_MS: 600,          // ^_^ after a release, then idle
  RAW_HOLD_MS: 1400,       // a raw face string is held this long
  BLINK_EVERY_MS: 5200,
  BLINK_HOLD_MS: 110,
  PET_WINDOW_MS: 4000,     // three pets inside this = the love cycle
  PET_TARGET: 3,
  PET_COOLDOWN_MS: 6000,   // ...then only a wink for this long
  SAVE_DEBOUNCE_MS: 600,
});

const IDLE_FACE = '0_0';
const BLINK_FACE = '-_-';
const DRAG_FACE = '@_@';
const FLING_FACE = '>.<';
const SETTLE_FACE = '^_^';
const BODY_CLASSES = ['breath', 'nod', 'shiver', 'bounce', 'thud', 'droop'];
/* How long each body move runs before EMI settles back into BREATH. Agent A's
 * keyframe lengths, rounded up: a move that never returns leaves `droop`'s
 * `forwards` fill (or a dead `bounce`) welded onto the root. */
const BODY_MS = { bounce: 800, thud: 800, shiver: 900, nod: 1900, droop: 800 };
/* The bubble hangs OFF EMI's right edge (emi.css: right:-5%, max-width:46%), so
 * she needs roughly this multiple of her own width to the right of her left
 * edge or the line runs off the viewport - at which point it flips left. */
const BUBBLE_REACH = 1.55;

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
  };
}

function readStats(raw) {
  const s = blankStats();
  if (!raw || typeof raw !== 'object') return s;
  for (const k of Object.keys(s)) {
    const v = raw[k];
    if (k === 'firstSeenAt' || k === 'lastSeenAt') {
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
 * @param {Function=} o.log
 */
export function createWidget({ root, face, chains, fx, store, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  if (!root || typeof document === 'undefined' || !document.createElement) return null;

  /* ---------------------- persisted state ------------------------------- */
  let saved = {};
  try {
    const raw = store && typeof store.get === 'function' ? store.get(STORE_KEY) : null;
    if (raw && typeof raw === 'object') saved = raw;
  } catch (e) { say('emi: store read failed - ' + ((e && e.message) || e)); }

  const stats = readStats(saved.stats);
  let width = clamp(num(saved.w) || DIALS.W_DEFAULT, DIALS.W_MIN, DIALS.W_MAX);
  // Anchor = the TOP-LEFT corner as a fraction of the viewport, so a resize
  // moves EMI proportionally and then re-clamps instead of drifting off-screen.
  let fx0 = num(saved.x);
  let fy0 = num(saved.y);
  let hidden = saved.hidden === true;
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
    const out = Object.assign({}, stats, { msVisible: visibleMs() });
    return { x: fx0, y: fy0, hidden, w: width, stats: out };
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
  body.src = './art/emi/body.png';
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

  /* ---------------------- the renderer, injected ------------------------ */
  /* A CANVAS THE PLATFORM CANNOT PAINT IS NOT AN ERROR. The node DOM double has
   * no getContext, so the widget runs faceless there instead of throwing on
   * boot - which is what keeps the shell suite green. */
  let painter = null;
  let CHAINS = null;
  let playChain = null;
  let makeSay = null;
  let showFx = null;

  function attach(mods) {
    const f = mods && mods.face;
    const c = mods && mods.chains;
    const x = mods && mods.fx;
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
      // painting before it would show one frame in the system monospace.
      if (painter.ready && typeof painter.ready.then === 'function') {
        painter.ready.then(() => { if (!hidden && enabled && !busy()) idle(); }).catch(() => {});
      }
      idle();
    }
  }
  attach({ face, chains, fx });

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
    faceBubble(left, s.w, vp.w);
    return { left, top, s, vp };
  }
  /** THE BUBBLE HANGS OFF HER RIGHT EAR and would run off the right edge of the
   *  window there, so it flips to the left ear instead (`.bubble-left`, styled in
   *  widget.css). Decided from the position, so a drag and a resize both fix it. */
  function faceBubble(left, w, vw) {
    const overRight = left + w * BUBBLE_REACH > vw;
    const room = left - w * (BUBBLE_REACH - 1) > 0;
    // `classList.toggle` is not in every DOM double; add/remove is.
    if (overRight && room) el.classList.add('bubble-left');
    else el.classList.remove('bubble-left');
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

  function drawFace(text, o) {
    if (!painter || typeof painter.draw !== 'function') return;
    try { painter.draw(String(text == null ? '' : text), o || {}); }
    catch (e) { say('emi: draw threw - ' + ((e && e.message) || e)); }
  }

  function setBubble(text) {
    if (text == null || text === '') {
      bubble.hidden = true;
      bubble.textContent = '';
      bubble.classList.remove('dots', 'pop');
      return;
    }
    const s = String(text);
    const dots = /^\.{1,3}$/.test(s);
    bubble.textContent = s;
    bubble.hidden = false;
    bubble.classList.remove('dots', 'pop');
    if (dots) bubble.classList.add('dots');
    else { bubble.classList.add('pop'); stats.bubblesSeen += 1; }
  }

  /* BODY MOVES GO ON THE ROOT. Agent A's keyframes animate `transform` on `.emi`
   * itself, which is also why widget.js positions her with left/top and never
   * with a transform of its own - the two would fight every frame. */
  let bodyTimer = null;
  function clearBody() {
    if (bodyTimer !== null) { clearTimeout(bodyTimer); bodyTimer = null; }
    for (const c of BODY_CLASSES) el.classList.remove(c);
  }
  function setBody(cls) {
    clearBody();
    if (!cls) return;
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
    }, BODY_MS[cls] || 800);
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
    clearBody();
    const handle = playChain(chain, {
      draw: (text, fo) => drawFace(text, fo),
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
    if (o.clearBubble !== false) setBubble(null);
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
    setBubble(null);
    clearBody();
    if (!painter || hidden || !enabled) return;
    drawFace(IDLE_FACE, {});
    if (!reducedMotion()) el.classList.add('breath');
    blinkTimer = setInterval(() => {
      if (busy() || hidden || !enabled || dragging) return;
      drawFace(BLINK_FACE, {});
      later(() => { if (!busy() && !dragging) drawFace(IDLE_FACE, {}); }, DIALS.BLINK_HOLD_MS);
    }, DIALS.BLINK_EVERY_MS);
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
  let armTimer = null;
  let petTimes = [];
  let petCooldownUntil = 0;

  function disarmHold() {
    if (armTimer !== null) { clearTimeout(armTimer); armTimer = null; }
  }

  function inX(ev) {
    const tgt = ev && ev.target;
    if (!tgt) return false;
    if (tgt === xBtn) return true;
    return !!(tgt.closest && tgt.closest('.emi-x'));
  }

  function onDown(ev) {
    if (!enabled || hidden) return;
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
    if (!saying()) { cancelChain(); killTimers(); stopBlink(); clearBody(); drawFace(DRAG_FACE, {}); }
  }

  function onMove(ev) {
    if (!pressing || ev.pointerId !== pressId) return;
    const dx = ev.clientX - lastX;
    const dy = ev.clientY - lastY;
    const t = nowMs();
    const dt = Math.max(1, t - lastT);
    const moved = Math.hypot(ev.clientX - lastX, ev.clientY - lastY);

    if (!dragging) {
      // The threshold is measured from where the PRESS began, never from the
      // previous move - a slow drag never crosses a per-move delta.
      const total = Math.hypot(ev.clientX - startX, ev.clientY - startY);
      if (total > DIALS.DRAG_PX || (t - pressAt) > DIALS.DRAG_MS) beginDrag();
    }
    if (dragging) {
      const vp = viewport();
      const s = sizePx();
      const left = clamp(ev.clientX - grabX, DIALS.MARGIN, Math.max(DIALS.MARGIN, vp.w - s.w - DIALS.MARGIN));
      const top = clamp(ev.clientY - grabY, DIALS.MARGIN, Math.max(DIALS.MARGIN, vp.h - s.h - DIALS.MARGIN));
      el.style.left = Math.round(left) + 'px';
      el.style.top = Math.round(top) + 'px';
      faceBubble(left, s.w, vp.w);

      // FLUNG? speed over the dial, sustained. `>.<` while it lasts.
      const speed = Math.hypot(dx, dy) / dt;
      if (speed > DIALS.FLING_SPEED) {
        if (!fastSince) fastSince = t;
        else if (t - fastSince > DIALS.FLING_MS && !saying()) { wasFling = true; drawFace(FLING_FACE, {}); }
      } else if (fastSince) {
        fastSince = 0;
        if (!saying()) drawFace(DRAG_FACE, {});
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
      el.classList.remove('dragging');
      const r = el.getBoundingClientRect ? el.getBoundingClientRect() : { left: 0, top: 0 };
      commit(r.left, r.top);
      if (wasFling) stats.flings += 1;
      // SETTLE: ^_^ for a beat, then back to resting. The landing move is the
      // playbook's BOUNCE, or a THUD when she was thrown.
      if (!saying()) {
        cancelChain(); killTimers(); stopBlink();
        drawFace(SETTLE_FACE, {});
        if (!reducedMotion()) setBody(wasFling ? 'thud' : 'bounce');
        later(() => idle(), DIALS.SETTLE_MS);
      }
      wasFling = false;
      // ONE write per interaction, on the END of it (never per pointermove).
      save();
      return;
    }
    // NOT A DRAG. A short press is a PET; a long hold was the x-arming gesture
    // and is deliberately not a pet (the player was reaching for the dismiss).
    el.classList.remove('dragging');
    if (held < DIALS.HOLD_MS) pet();
  }

  function onCancel() {
    if (!pressing) return;
    pressing = false;
    disarmHold();
    if (dragging) {
      dragging = false;
      el.classList.remove('dragging');
      const r = el.getBoundingClientRect ? el.getBoundingClientRect() : { left: 0, top: 0 };
      commit(r.left, r.top);
      save();
      if (!saying()) idle();
    }
    pressId = null;
  }

  /** A pet: positive cycle, three inside the window buys the love chain. */
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
      if (CHAINS && CHAINS.wink) play(CHAINS.wink);
      else raw('^_~', { hold: 500 });
      save();
      return;
    }
    petTimes = petTimes.filter((p) => t - p < DIALS.PET_WINDOW_MS);
    petTimes.push(t);
    if (petTimes.length >= DIALS.PET_TARGET) {
      petTimes = [];
      petCooldownUntil = t + DIALS.PET_COOLDOWN_MS;
      stats.petStreaks3 += 1;
      if (CHAINS && CHAINS.love) play(CHAINS.love);
      else raw('(≧◡≦)', { hold: 1400, fx: 'hearts', body: 'bounce' });
    } else {
      raw(SETTLE_FACE, { hold: 900, fx: 'hearts', body: reducedMotion() ? null : 'bounce' });
    }
    save();
  }

  /* ---------------------- hide / dock ----------------------------------- */
  function hide(opts) {
    if (hidden) return;
    hidden = true;
    accrueVisible();
    if (!(opts && opts.silent)) { stats.hides += 1; touchSeen(); }
    cancelChain(); killTimers(); stopBlink(); clearBody(); setBubble(null);
    el.classList.remove('armed', 'dragging');
    el.hidden = true;
    dock.hidden = false;
    save(true);
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
  }

  xBtn.addEventListener('click', (ev) => {
    // Inside `.emi`: stopping this one is legal and necessary (the pet handler
    // must not also fire). Nothing outside the widget is touched.
    if (ev && typeof ev.stopPropagation === 'function') ev.stopPropagation();
    hide();
  });
  // The x owns its own pointer stream so a press on it can never start a drag.
  xBtn.addEventListener('pointerdown', (ev) => { if (ev && ev.stopPropagation) ev.stopPropagation(); });
  dock.addEventListener('click', () => show());

  el.addEventListener('pointerdown', onDown);
  el.addEventListener('pointermove', onMove);
  el.addEventListener('pointerup', onUp);
  el.addEventListener('pointercancel', onCancel);
  el.addEventListener('pointerleave', () => { if (!pressing) el.classList.remove('armed'); });

  /* ---------------------- viewport ------------------------------------- */
  function onResize() { if (!hidden && enabled) place(); }
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

  /* ---------------------- first paint ----------------------------------- */
  place();
  if (hidden) { el.hidden = true; dock.hidden = false; }
  else { el.hidden = false; dock.hidden = true; beginVisible(); if (painter) idle(); }

  /* ---------------------- the handle ------------------------------------ */
  return {
    el, dock, canvas, fxHost, bubble,
    attach,
    play, raw, idle,
    makeSayFn() { return makeSay; },
    chainsTable() { return CHAINS; },
    hasFace() { return !!painter; },
    saying, busy,
    setBubble,
    hide, show,
    get hidden() { return hidden; },
    setEnabled(on) {
      const next = !!on;
      if (next === enabled) return;
      enabled = next;
      if (!enabled) {
        accrueVisible();
        cancelChain(); killTimers(); stopBlink();
        root.hidden = true;
        save(true);
      } else {
        root.hidden = false;
        if (!hidden) { place(); beginVisible(); idle(); }
      }
    },
    get enabled() { return enabled; },
    /** Read-only lifetime telemetry (a copy - nothing outside may mutate it). */
    stats() { return Object.assign({}, stats, { msVisible: visibleMs() }); },
    /** Test/host seam: force the debounced write out now. */
    flush,
    destroy() {
      accrueVisible();
      save(true);
      cancelChain(); killTimers(); stopBlink(); disarmHold();
      if (saveTimer !== null) { clearTimeout(saveTimer); saveTimer = null; }
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
