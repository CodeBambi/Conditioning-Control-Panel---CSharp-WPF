/* ============================================================================
 * ui/pause.js — the IN-GAME PAUSE MENU for "Graded Intake".
 *
 * Two halves:
 *
 *   1. THE SHELL. A kawaii / Doki-Doki-storefront panel (ui/kawaii.css) floated
 *      over a TRANSLUCENT scrim, so the frozen run stays visible underneath and
 *      keeps staring back at you. Resume · Options · Quit run.
 *
 *   2. THE FREEZE. The hard part. beats.js runs its own countdowns (the 20s
 *      freeze-gate ceremony, melt timers, bubble-split chains, scramble
 *      intervals, auto-commit backstops) across ~50 independent timer sites, and
 *      a pause that only stops boot.js's beat loop while a countdown keeps
 *      draining is a bug the player hits within one session.
 *
 *      Rather than thread an `if (paused)` through fifty call sites in a file
 *      three other agents are also editing, this module installs a TIMING SHIM
 *      at the window level, ONCE, before the beat loop starts:
 *
 *        setTimeout / setInterval  -> tracked; on pause the pending native timer
 *                                     is cancelled and its REMAINING time banked,
 *                                     then re-armed for exactly that remainder on
 *                                     resume. Callers keep their handle (we hand
 *                                     out virtual ids and map them).
 *        requestAnimationFrame     -> deferred while paused, flushed on resume.
 *        performance.now / Date.now-> frozen at the pause instant, and offset by
 *                                     the total paused time afterwards, so every
 *                                     `(now() - t0) / DURATION` progress ramp in
 *                                     beats.js (that IS how the freeze gate, the
 *                                     melt and the glide are timed) simply does
 *                                     not advance.
 *
 *      Everything created AFTER installPause() therefore freezes for free, with
 *      zero edits to beats.js / effects.js / steering.js.
 *
 *      HONEST LIMITS — what keeps running while paused (see the report notes):
 *        · WebAudio events already SCHEDULED on the audio clock still fire. The
 *          binaural bed is suspended (audio.suspendBed) and <video>/<audio>
 *          elements are paused, but a one-shot VO line already in flight plays
 *          out its last moment.
 *        · Animated GIFs cannot be paused by the web platform.
 *        · CSS *transitions* already in flight complete (CSS *animations* are
 *          held via `animation-play-state: paused`).
 *        · Timers created BEFORE installPause() are not tracked. In practice the
 *          only one is web-shim's heartbeat rAF loop, which we deliberately
 *          replace with a native-timer keepalive while paused so the C# host
 *          watchdog (20s of heartbeat silence -> relaunch the window) can never
 *          mistake a paused run for a wedged one.
 *
 * The timing shim is intentionally NOT uninstalled on dispose(): callers hold
 * virtual timer handles, and pulling the shim out from under them would strand
 * those handles. Unpaused, the shim is a thin pass-through.
 * ========================================================================== */

import * as shim from '../web-shim.js';
import { getPrefs, subscribe } from './prefs.js';
import { startMenuMusic, stopMenuMusic } from './menuMusic.js';
import { setBandDepth, resetCorruption, isDeepBand } from './corruption.js';
import * as fullscreen from './fullscreen.js';

/**
 * THE QUIT BUTTON FALLS OFF. From band 3 onward (Deepening / Climax / Recovery
 * — the same set ui/corruption.js rots the palette over), a CONFIRMED quit has
 * a 1-in-10 chance of the button simply coming off in your hand instead of
 * working. The player is never trapped: the host window's own close still
 * works, Resume and Options are untouched, and a later pause gets a fresh
 * button. There is deliberately no apology copy — an apology would explain the
 * joke and turn a prank into a system message.
 */
const QUIT_FALL_CHANCE = 0.10;

/**
 * THE QUIT BUTTON IS GENERATING. The confirming press does not quit — it queues
 * your quit button for manufacture, and you are invited to wait for it.
 *
 * The countdown is honest for the first five seconds and then quietly stops
 * being honest: from 10 downward each second costs 16% more real time than the
 * one before, so "10 seconds left" is really ~21, and the number keeps dropping
 * at a plausible-looking rate the whole way. Nobody catches the ramp in the
 * first few ticks, which is the point — it has to feel like an ordinary
 * progress bar right up until it feels like a joke.
 *
 * Then, with three seconds left, a 1-in-3 roll fails the build and re-queues it
 * at 8 or 9. The roll happens EVERY time the countdown passes 3, so it can fire
 * more than once; at p=1/3 the expected number of extra loops is 0.5 and the
 * ceremony terminates with probability 1.
 *
 * The player is never trapped. Resume, Options, Escape and the scrim all still
 * work, the host window's own close still works, and leaving the pause menu
 * cancels the whole thing — a later pause starts over from an unarmed Quit.
 */
const QUIT_GEN_START      = 15;    // seconds on the clock at queue time
const QUIT_GEN_HONEST_TO  = 10;    // ticks above this are a real second each
const QUIT_GEN_DRAG       = 1.16;  // each tick at or below that costs 16% more
const QUIT_GEN_GLITCH_AT  = 3;     // "a few seconds left"
const QUIT_GEN_GLITCH_P   = 0.33;
const QUIT_GEN_REQUEUE    = [8, 9];
const QUIT_GEN_GLITCH_MS  = 900;   // how long the failure reads before re-queuing

/* ----------------------------------------------------------------------------
 * MODULE STATE — one pause per page. installPause() disposes any predecessor.
 * -------------------------------------------------------------------------- */
let NATIVE = null;          // captured real timer/clock functions
let shimInstalled = false;
let active = null;          // the live handle (also mirrored on window for boot's fatal path)

let _paused = false;
let _offset = 0;            // total ms spent paused (subtracted from both clocks)
let _freezePerf = 0;
let _freezeDate = 0;

let nextId = 0x40000000;    // virtual handle space, far above real timer ids
const timers = new Map();   // vid -> { kind:'t'|'i', fn, args, period, nextAt, nativeId, leadId }
const rafLive = new Map();  // vid -> { nativeId, cb }
const rafHeld = new Map();  // vid -> cb   (deferred while paused)

/** Is the run currently paused? Safe to call from any module. */
export function isPaused() { return _paused; }

/**
 * The PAUSE CLOCK: monotonic milliseconds that stop advancing while paused.
 * Exported so a future beats.js timer can consult it directly instead of
 * relying on the (already frozen) global `performance.now`.
 */
export function pauseClockNow() {
  if (!NATIVE) return (typeof performance !== 'undefined' ? performance.now() : Date.now());
  return (_paused ? _freezePerf : NATIVE.perfNow()) - _offset;
}

/** Fatal-path helper for boot.js: tear down whatever pause is installed. */
export function disposeActivePause() {
  try { if (active && typeof active.dispose === 'function') active.dispose(); } catch (_e) { /* never fatal */ }
}

/* ----------------------------------------------------------------------------
 * THE TIMING SHIM
 * -------------------------------------------------------------------------- */
function captureNative(win) {
  if (NATIVE) return NATIVE;
  const hasPerf = (typeof performance !== 'undefined') && typeof performance.now === 'function';
  NATIVE = {
    setTimeout:    win.setTimeout.bind(win),
    clearTimeout:  win.clearTimeout.bind(win),
    setInterval:   win.setInterval.bind(win),
    clearInterval: win.clearInterval.bind(win),
    raf: (typeof win.requestAnimationFrame === 'function') ? win.requestAnimationFrame.bind(win) : null,
    caf: (typeof win.cancelAnimationFrame === 'function') ? win.cancelAnimationFrame.bind(win) : null,
    perfNow: hasPerf ? performance.now.bind(performance) : Date.now.bind(Date),
    dateNow: Date.now.bind(Date),
    perfDesc: hasPerf ? performance.now : null,
  };
  return NATIVE;
}

function armTimer(vid, rec, delay) {
  const N = NATIVE;
  rec.nextAt = N.perfNow() + delay;
  if (rec.kind === 't') {
    rec.nativeId = N.setTimeout(() => {
      timers.delete(vid);
      rec.fn.apply(null, rec.args);     // no try/catch: preserve native error semantics
    }, delay);
  } else if (delay === rec.period) {
    rec.nativeId = N.setInterval(() => {
      rec.nextAt = N.perfNow() + rec.period;
      rec.fn.apply(null, rec.args);
    }, rec.period);
  } else {
    // Resuming mid-period: one lead timeout for the remainder, then the interval.
    rec.leadId = N.setTimeout(() => {
      rec.leadId = 0;
      if (!timers.has(vid)) return;
      rec.nextAt = N.perfNow() + rec.period;
      rec.nativeId = N.setInterval(() => {
        rec.nextAt = N.perfNow() + rec.period;
        rec.fn.apply(null, rec.args);
      }, rec.period);
      rec.fn.apply(null, rec.args);
    }, delay);
  }
}

function disarmTimer(rec) {
  const N = NATIVE;
  if (rec.nativeId) {
    if (rec.kind === 't') N.clearTimeout(rec.nativeId); else N.clearInterval(rec.nativeId);
    rec.nativeId = 0;
  }
  if (rec.leadId) { N.clearTimeout(rec.leadId); rec.leadId = 0; }
}

function installTimingShim(win) {
  if (shimInstalled) return;
  const N = captureNative(win);
  shimInstalled = true;

  win.setTimeout = function (fn, delay) {
    if (typeof fn !== 'function') return N.setTimeout.apply(null, arguments);
    const d = Math.max(0, Number(delay) || 0);
    const vid = nextId++;
    const rec = { kind: 't', fn, args: Array.prototype.slice.call(arguments, 2), period: d, nextAt: 0, nativeId: 0, leadId: 0 };
    timers.set(vid, rec);
    if (_paused) rec.nextAt = _freezePerf + d;   // banked; armed on resume
    else armTimer(vid, rec, d);
    return vid;
  };

  win.setInterval = function (fn, delay) {
    if (typeof fn !== 'function') return N.setInterval.apply(null, arguments);
    const d = Math.max(1, Number(delay) || 1);
    const vid = nextId++;
    const rec = { kind: 'i', fn, args: Array.prototype.slice.call(arguments, 2), period: d, nextAt: 0, nativeId: 0, leadId: 0 };
    timers.set(vid, rec);
    if (_paused) rec.nextAt = _freezePerf + d;
    else armTimer(vid, rec, d);
    return vid;
  };

  const clear = (kind) => function (h) {
    const rec = timers.get(h);
    if (rec) { disarmTimer(rec); timers.delete(h); return; }
    if (kind === 't') N.clearTimeout(h); else N.clearInterval(h);
  };
  win.clearTimeout = clear('t');
  win.clearInterval = clear('i');

  if (N.raf) {
    win.requestAnimationFrame = function (cb) {
      if (typeof cb !== 'function') return N.raf(cb);
      const vid = nextId++;
      if (_paused) { rafHeld.set(vid, cb); return vid; }
      scheduleRaf(vid, cb);
      return vid;
    };
    win.cancelAnimationFrame = function (h) {
      const live = rafLive.get(h);
      if (live) { if (N.caf) N.caf(live.nativeId); rafLive.delete(h); return; }
      if (rafHeld.has(h)) { rafHeld.delete(h); return; }
      if (N.caf) N.caf(h);
    };
  }

  // Frozen clocks. Every `(now() - t0) / DURATION` ramp in beats.js reads one of
  // these two, which is what makes the freeze-gate ceremony genuinely hold.
  try {
    if (N.perfDesc) performance.now = function () { return (_paused ? _freezePerf : N.perfNow()) - _offset; };
  } catch (_e) { /* locked-down host: clocks stay real, timers still freeze */ }
  try {
    Date.now = function () { return (_paused ? _freezeDate : N.dateNow()) - _offset; };
  } catch (_e) { /* ditto */ }
}

function scheduleRaf(vid, cb) {
  const N = NATIVE;
  const nativeId = N.raf(function (ts) {
    rafLive.delete(vid);
    cb(ts - _offset);   // hand callbacks the PAUSE clock, not the wall clock
  });
  rafLive.set(vid, { nativeId, cb });
}

function freezeTimers() {
  const N = NATIVE;
  const now = N.perfNow();
  for (const rec of timers.values()) {
    rec.remaining = Math.max(0, (rec.nextAt || now) - now);
    disarmTimer(rec);
  }
  for (const [vid, live] of rafLive) {
    if (N.caf) { try { N.caf(live.nativeId); } catch (_e) {} }
    rafHeld.set(vid, live.cb);
  }
  rafLive.clear();
}

function thawTimers() {
  for (const [vid, rec] of timers) {
    const remaining = (rec.remaining != null) ? rec.remaining : rec.period;
    rec.remaining = null;
    armTimer(vid, rec, remaining);
  }
  const held = Array.from(rafHeld);
  rafHeld.clear();
  for (const [vid, cb] of held) scheduleRaf(vid, cb);
}

/* ----------------------------------------------------------------------------
 * installPause
 * -------------------------------------------------------------------------- */
/**
 * Install the in-game pause menu.
 *
 * @param {{audio?:object, background?:object, effects?:object, caps?:object,
 *          config?:object}} [ctx]
 * @returns {{dispose:()=>void, isPaused:()=>boolean, open:()=>void,
 *            close:()=>void, gate:()=>Promise<void>,
 *            setBand:(band:string, depth:number)=>void, button:HTMLElement|null}}
 */
export function installPause(ctx = {}) {
  const doc = (typeof document !== 'undefined') ? document : null;
  const win = (typeof window !== 'undefined') ? window : null;
  if (!doc || !win || !doc.body) return inertHandle();

  disposeActivePause();          // only ever one
  installTimingShim(win);
  const N = NATIVE;

  const audio = ctx.audio || null;
  const background = ctx.background || null;
  const effects = ctx.effects || null;

  let destroyed = false;
  let open_ = false;
  let quitting = false;
  let optionsBusy = false;
  let bgWasEnabled = false;      // only used on the setEnabled fallback path
  let heldMedia = [];
  let hbKeepalive = 0;
  let lastFocus = null;
  let confirmQuit = false;
  const gateWaiters = [];

  /* ---- band plumbing -----------------------------------------------------
   * boot.js pumps every beat's band + depth in through the handle's setBand().
   * It feeds ui/corruption.js (palette + shell-music rot) and gates the Quit
   * prank below. ONE channel: nothing here reaches into the engine itself. */
  let deepBand = false;          // Deepening / Climax / Recovery
  let quitFallen = false;        // the button has come off THIS pause session
  let fallNode = null;           // the tumbling clone
  let fallRaf = 0;
  let fallT = 0;

  /* ---- the generating-quit-button ceremony (see QUIT_GEN_* above) --------- */
  let genNode = null;            // the whole "generating…" block, inside the panel
  let genLine = null;            // its status line
  let genFill = null;            // its progress bar fill
  let genBtn = null;             // the generated Quit, revealed at zero
  let genT = 0;                  // native timeout for the next tick
  let genScramble = 0;           // native interval that garbles the number mid-glitch
  let genLeft = 0;               // seconds still on the clock
  let genSpan = QUIT_GEN_START;  // seconds this cycle started from (drives the bar)
  let quitReleased = false;      // the generated button exists and actually quits

  // A fresh run must never inherit the last one's rot (same page, second boot).
  resetCorruption();

  ensureStylesheet(doc);

  /* ---- the pause button (BODY-parented, so it survives stage wipes) -------
   * beats.js does `stage.innerHTML = ''` at the top of every render, so any
   * node parked inside #intake-stage is destroyed once per beat. #intake-hud is
   * a body-level sibling of the stage, but boot.js's buildHud() also clears it
   * (`dom.hud.innerHTML = ''`) and it is `pointer-events: none`. So the button
   * hangs off <body> directly: nothing in the run's render path ever touches
   * body's own children except the nodes it created itself. */
  const btn = doc.createElement('button');
  btn.type = 'button';
  btn.className = 'kw-root kw-pause-btn';
  btn.setAttribute('aria-label', 'Pause');
  btn.title = 'Pause (Esc)';
  btn.innerHTML = '<span class="kw-pause-btn__bars" aria-hidden="true"></span>';
  btn.addEventListener('click', (e) => { e.preventDefault(); e.stopPropagation(); open(); });
  doc.body.appendChild(btn);

  /* ---- the menu (built once, mounted/unmounted on open/close) ------------- */
  const root = buildRoot(doc);
  const unsubPrefs = subscribe(() => applyDecorPref(root));
  applyDecorPref(root);

  root.addEventListener('click', (e) => {
    const t = e.target && e.target.closest ? e.target.closest('[data-act]') : null;
    if (!t) return;
    const act = t.getAttribute('data-act');
    if (act === 'resume') close();
    else if (act === 'options') doOptions();
    else if (act === 'fullscreen') fullscreen.toggle();
    else if (act === 'quit') doQuit(t);
  });

  /* ---- window mode -------------------------------------------------------
   * The button is a live mirror of the actual window, never of an intent: when
   * hosted, `fullscreen.isActive()` only flips once C# has echoed the window it
   * really built, so the label can never claim a mode we are not in. Repainted
   * on every confirmed change (this button, the Options row, F11) and on open. */
  const fsBtn = root.querySelector('[data-act="fullscreen"]');
  if (fsBtn && !fullscreen.supported) fsBtn.hidden = true;
  const syncFsBtn = () => {
    if (!fsBtn || !fullscreen.supported) return;
    fsBtn.textContent = fullscreen.actionLabel();
  };
  const unsubFs = fullscreen.subscribe(syncFsBtn);
  syncFsBtn();
  // Clicking the scrim resumes — the same forgiving gesture the menu uses.
  const scrim = root.querySelector('.kw-pause-scrim');
  if (scrim) scrim.addEventListener('click', () => close());

  /* ---- ESCAPE, WITHOUT STEALING STEERING'S ESCAPE ------------------------
   * render/steering.js's MouseHijack registers a WINDOW/CAPTURE keydown that
   * disarms the hijack on Escape — invariant #1, "the player can always get
   * out". That must keep winning.
   *
   * We use two listeners and the DOM's own ordering:
   *   probe  — window/capture, registered HERE at install time, therefore ahead
   *            of every per-beat steering listener. It only RECORDS whether a
   *            hijack is currently armed; it never consumes the key.
   *   opener — document/capture. Capture runs window -> document -> ... , so
   *            this fires strictly AFTER every window-capture listener, i.e.
   *            after steering has already had (and possibly used) the key.
   *            If the probe saw an armed hijack, the Escape belonged to
   *            steering and we do not open, plus we hold a short suppression
   *            window so the same press cannot double-fire.
   * Result: while a steer is armed, Escape is steering's and only steering's. */
  let hijackWasArmed = false;
  let suppressUntil = 0;

  const isHijackArmed = () => {
    try {
      if (doc.querySelector('.intake-hijack-cursor')) return true;
      const stage = doc.getElementById('intake-stage');
      if (stage && stage.classList.contains('intake-hijack-none')) return true;
    } catch (_e) {}
    return false;
  };

  const probe = (e) => {
    if (!isEsc(e)) return;
    hijackWasArmed = isHijackArmed();
  };

  const opener = (e) => {
    if (!isEsc(e)) return;
    if (open_) return;                       // the blocker below already closed us
    const t = N.perfNow();
    if (hijackWasArmed || t < suppressUntil) { suppressUntil = t + 700; return; }
    // The jumpscare ceremony owns the screen; never pause on top of it.
    try { if (doc.body.classList.contains('ix-freeze')) return; } catch (_e) {}
    e.preventDefault();
    open();
  };

  win.addEventListener('keydown', probe, true);
  doc.addEventListener('keydown', opener, true);

  /* ---- input blocking while paused ---------------------------------------
   * Capture-phase on window (ahead of steering's own doc/window captures), so
   * nothing in the run ever sees a pointer or key while the menu is up. */
  const inRoot = (e) => {
    const t = e.target;
    return !!(t && t.nodeType === 1 && root.contains(t));
  };

  const keyBlocker = (e) => {
    if (!open_) return;
    // F11 is never the run's to swallow. ui/fullscreen.js registers its own
    // window/capture handler at module load — i.e. before this one — so this is
    // strictly belt-and-braces, but a key whose entire job is "get me my title
    // bar back" must not depend on listener ordering to keep working.
    if (e.key === 'F11') return;
    // The Options panel is a modal child of this root and owns its own keyboard,
    // Escape included (it closes ITSELF and stops propagation). Never pop two
    // layers on one keypress.
    if (optionsBusy) {
      if (inRoot(e) || isEsc(e)) return;
      e.preventDefault();
      e.stopImmediatePropagation();
      return;
    }
    if (isEsc(e)) {
      // Escape closes the menu. We do NOT stopImmediatePropagation here: a stale
      // steering disarm receiving this key is harmless, and letting it through
      // keeps steering's guarantee unconditional. We DO arm the suppression
      // window, so `opener` (which runs later, on document/capture, and will by
      // then see open_ === false) cannot re-open on the very same keypress.
      if (e.type === 'keydown') {
        suppressUntil = N.perfNow() + 700;
        e.preventDefault();
        close();
      }
      return;
    }
    if (inRoot(e)) {
      if (e.key === 'Tab' && e.type === 'keydown') trapTab(e, root);
      return;                                   // menu keyboard use passes
    }
    e.preventDefault();
    e.stopImmediatePropagation();
  };

  const ptrBlocker = (e) => {
    if (!open_ || inRoot(e)) return;
    e.stopImmediatePropagation();
    if (e.cancelable && e.type !== 'pointermove' && e.type !== 'mousemove') e.preventDefault();
  };

  const KEY_EVENTS = ['keydown', 'keyup', 'keypress'];
  const PTR_EVENTS = ['pointerdown', 'pointerup', 'pointermove', 'mousedown', 'mouseup',
                      'mousemove', 'click', 'dblclick', 'contextmenu', 'wheel',
                      'touchstart', 'touchend', 'touchmove'];
  for (const t of KEY_EVENTS) win.addEventListener(t, keyBlocker, true);
  for (const t of PTR_EVENTS) win.addEventListener(t, ptrBlocker, true);

  /* ---- open / close ------------------------------------------------------ */
  function open() {
    if (open_ || destroyed || quitting) return;
    try { if (doc.body.classList.contains('ix-freeze')) return; } catch (_e) {}

    // 1. STOP THE CLOCKS. Order matters: freeze the clocks, THEN bank the timers,
    //    so nothing re-arms itself against a moving `now`.
    _freezePerf = N.perfNow();
    _freezeDate = N.dateNow();
    _paused = true;
    freezeTimers();

    // 2. Keep the C# host's heartbeat watchdog fed (web-shim's rAF beat is now
    //    held; 20s of silence would make IntakeHostService relaunch the window).
    hbKeepalive = N.setInterval(() => {
      try { shim.send({ type: 'heartbeat', t: N.perfNow() }); } catch (_e) {}
    }, 2000);
    try { shim.send({ type: 'heartbeat', t: N.perfNow() }); } catch (_e) {}

    // 3. Freeze the run's own layers.
    try { doc.body.classList.add('ix-paused'); } catch (_e) {}
    if (background && typeof background.setFrozen === 'function') {
      safe(() => background.setFrozen(true));
    } else if (background && typeof background.setEnabled === 'function') {
      bgWasEnabled = true;
      safe(() => background.setEnabled(false));
    }
    safe(() => effects && typeof effects.setFrozen === 'function' && effects.setFrozen(true));
    safe(() => audio && typeof audio.suspendBed === 'function' && audio.suspendBed());
    heldMedia = pauseMedia(doc, root);
    // The shell song comes back while a menu is up — pause IS a menu screen.
    // It resumes mid-track (menuMusic keeps the playhead), and it is immune to
    // pauseMedia above because the element is never appended to the document.
    // Honours its own mute pref, so a player who silenced it stays silenced.
    safe(() => startMenuMusic());

    // 4. Show the menu. A new pause session gets a whole new Quit button — the
    //    fall never respawns WITHIN a session, but reopening later is fine.
    confirmQuit = false;
    quitFallen = false;
    removeQuitFall();
    cancelQuitGeneration();
    resetQuitButton(root);
    syncFsBtn();
    open_ = true;
    lastFocus = doc.activeElement;
    if (!root.isConnected) doc.body.appendChild(root);
    root.hidden = false;
    btn.classList.add('is-hidden');
    const first = root.querySelector('[data-act="resume"]');
    if (first) { try { first.focus(); } catch (_e) {} }
    try { shim.log('pause: opened'); } catch (_e) {}
  }

  function close() {
    if (!open_ || destroyed) return;
    open_ = false;
    root.hidden = true;
    try { if (root.parentNode) root.parentNode.removeChild(root); } catch (_e) {}
    btn.classList.remove('is-hidden');
    removeQuitFall();   // a clone still in the air does not follow us into the run
    // Resuming abandons the order. Nothing keeps ticking behind the run, and the
    // next pause starts over from an unarmed Quit — which is the escape hatch
    // that makes the whole gag safe to ship.
    cancelQuitGeneration();

    // Unfreeze the layers first, then the clocks, so the first thawed frame
    // already has a live background/audio to draw against.
    try { doc.body.classList.remove('ix-paused'); } catch (_e) {}
    if (background && typeof background.setFrozen === 'function') {
      safe(() => background.setFrozen(false));
    } else if (bgWasEnabled && background && typeof background.setEnabled === 'function') {
      bgWasEnabled = false;
      safe(() => background.setEnabled(true));
    }
    safe(() => effects && typeof effects.setFrozen === 'function' && effects.setFrozen(false));
    safe(() => audio && typeof audio.resumeBed === 'function' && audio.resumeBed());
    resumeMedia(heldMedia); heldMedia = [];
    // ...and back out of the way of the run. Fade, not cut — the binaural bed
    // is resuming on the same beat and a hard stop would read as a glitch.
    safe(() => stopMenuMusic());

    if (hbKeepalive) { N.clearInterval(hbKeepalive); hbKeepalive = 0; }

    _offset += N.perfNow() - _freezePerf;
    _paused = false;
    thawTimers();

    if (lastFocus && typeof lastFocus.focus === 'function' && doc.contains(lastFocus)) {
      try { lastFocus.focus(); } catch (_e) {}
    }
    lastFocus = null;

    const waiters = gateWaiters.splice(0, gateWaiters.length);
    for (const r of waiters) { try { r(); } catch (_e) {} }
    try { shim.log('pause: resumed'); } catch (_e) {}
  }

  /* ---- Options (owned by another module; degrade if it is not there) ------ */
  async function doOptions() {
    if (optionsBusy) return;
    optionsBusy = true;
    const optBtn = root.querySelector('[data-act="options"]');
    try {
      const mod = await import('./options.js');
      if (!mod || typeof mod.openOptions !== 'function') throw new Error('no openOptions export');
      await mod.openOptions({ parent: root, audio });
    } catch (e) {
      try { shim.log('pause: options unavailable (' + ((e && e.message) || e) + ')'); } catch (_e) {}
      if (optBtn) {
        optBtn.textContent = 'Options unavailable';
        optBtn.disabled = true;
      }
    } finally {
      optionsBusy = false;
      const first = root.querySelector('[data-act="resume"]');
      if (open_ && first) { try { first.focus(); } catch (_e) {} }
    }
  }

  /* ---- Quit run — two-press confirm, then the host's own exit path -------- */
  function doQuit(el) {
    if (quitting || quitFallen) return;

    // The generated button is the only one that actually leaves. It carries the
    // same data-act, so it arrives here — and it skips every ceremony, because
    // the player has already served the whole sentence to get it.
    if (quitReleased) { finishQuit(el); return; }
    if (genNode) return;            // still generating; the spent original is inert

    /* THE PRANK ROLLS HERE — on the FIRST press, before the confirm is armed, so
     * the button comes off in the player's hand instead of ever asking "really?".
     *
     * It used to roll on the CONFIRMING press, on the theory that a refusal is
     * funnier once the game has asked and been answered. That reasoning was fine
     * and the placement was still wrong, because of reachability: a confirm press
     * that does NOT fall immediately quits the run. So the roll was reachable at
     * most once per run, at the exact moment the player was leaving — ~30 real
     * quits to see it once at 10%. Pressing Quit thirty times in one pause rolls
     * nothing at all, because presses after the first only re-enter an armed
     * confirm. An easter egg nobody can reach is not an easter egg.
     *
     * On the first press it rolls once per pause session (quitFallen latches, and
     * open() clears it), so every pause in a deep band is a fresh 1-in-10 — which
     * is what "1 in 10 chances that the quit falls" actually means. */
    if (deepBand && !confirmQuit && Math.random() < QUIT_FALL_CHANCE) { knockQuitOff(el); return; }

    if (!confirmQuit) {
      confirmQuit = true;
      if (el) { el.textContent = 'Really quit? No score is saved.'; el.classList.add('kw-pause-quit--armed'); }
      return;
    }

    // The confirming press does not quit. It orders you a quit button.
    startQuitGeneration(el);
  }

  /** The actual wind-down. Only reachable from the GENERATED button. */
  function finishQuit(el) {
    quitting = true;
    if (el) { el.textContent = 'Closing…'; el.disabled = true; }
    // Abort: no QuizRunResult is emitted, so no session is drafted.
    //   hosted     -> `exit`, the host's documented page-initiated wind-down
    //                 (IntakeHostService: _exiting = true; ArmExitWatchdog()).
    //   standalone -> web-shim's closeHost() (window.close, then about:blank).
    try { shim.log('pause: quit run'); } catch (_e) {}
    if (shim.isHosted) { try { shim.send({ type: 'exit' }); } catch (_e) {} }
    else { try { shim.closeHost(); } catch (_e) {} }
  }

  /* ---- "Your quit button is generating" ----------------------------------
   * EVERY TIMER IN HERE IS NATIVE. The menu is open, which means the shimmed
   * setTimeout has banked its pending callbacks and both clocks are frozen —
   * a countdown driven by the global timers would sit at 15 forever. Same
   * reasoning as startQuitFall() below. */

  /** Real milliseconds the tick that leaves `n` on the clock is made to last. */
  function genTickMs(n) {
    if (n > QUIT_GEN_HONEST_TO) return 1000;
    return Math.round(1000 * Math.pow(QUIT_GEN_DRAG, QUIT_GEN_HONEST_TO - n));
  }

  function startQuitGeneration(el) {
    if (genNode) return;                       // already queued; the press was a double
    try { shim.log('pause: quit button queued for generation'); } catch (_e) {}

    // The armed original stays in place, spent: it keeps the actions column the
    // same height and it reads as the thing that DID something.
    if (el) {
      try {
        el.textContent = 'Quit request received.';
        el.classList.remove('kw-pause-quit--armed');
        el.classList.add('kw-quit-spent');
        el.disabled = true;
      } catch (_e) {}
    }
    // A disabled control must not keep the keyboard.
    const res = root.querySelector('[data-act="resume"]');
    if (res) { try { res.focus(); } catch (_e) {} }

    genNode = buildQuitGen(doc);
    genLine = genNode.querySelector('.kw-quit-gen__line');
    genFill = genNode.querySelector('.kw-quit-gen__fill');
    genBtn  = genNode.querySelector('.kw-quit-gen__btn');
    try {
      const acts = root.querySelector('.kw-pause-actions');
      if (acts) acts.appendChild(genNode); else return cancelQuitGeneration();
    } catch (_e) { return cancelQuitGeneration(); }

    genLeft = QUIT_GEN_START;
    genSpan = QUIT_GEN_START;
    renderQuitGen();
    stepQuitGen();
  }

  function renderQuitGen() {
    if (genLine) {
      genLine.textContent =
        'Your quit button is generating, wait ' + genLeft + ' second' + (genLeft === 1 ? '' : 's');
    }
    if (genFill) {
      const done = genSpan > 0 ? (genSpan - genLeft) / genSpan : 1;
      try { genFill.style.width = (Math.max(0, Math.min(1, done)) * 100).toFixed(1) + '%'; } catch (_e) {}
    }
  }

  function stepQuitGen() {
    genT = N.setTimeout(() => {
      genT = 0;
      if (destroyed || !genNode) return;
      genLeft -= 1;
      if (genLeft <= 0) { releaseQuitButton(); return; }
      renderQuitGen();
      if (genLeft === QUIT_GEN_GLITCH_AT && Math.random() < QUIT_GEN_GLITCH_P) { glitchRequeue(); return; }
      stepQuitGen();
    }, genTickMs(genLeft));
  }

  /** The build fails on the home straight and goes back in the queue at 8 or 9. */
  function glitchRequeue() {
    try { shim.log('pause: quit generation failed, re-queued'); } catch (_e) {}
    const animates = fallAnimates();
    if (genNode && animates) { try { genNode.classList.add('is-glitching'); } catch (_e) {} }
    if (genLine) genLine.textContent = 'Generation failed. Re-queuing your quit button…';
    // The number garbles while the failure reads — but only if this player has
    // motion turned on, since it is pure decoration.
    if (animates) {
      genScramble = N.setInterval(() => {
        if (!genFill) return;
        try { genFill.style.width = (10 + Math.random() * 85).toFixed(1) + '%'; } catch (_e) {}
      }, 70);
    }
    genT = N.setTimeout(() => {
      genT = 0;
      if (genScramble) { N.clearInterval(genScramble); genScramble = 0; }
      if (destroyed || !genNode) return;
      try { genNode.classList.remove('is-glitching'); } catch (_e) {}
      genLeft = QUIT_GEN_REQUEUE[Math.floor(Math.random() * QUIT_GEN_REQUEUE.length)];
      genSpan = genLeft;
      renderQuitGen();
      stepQuitGen();
    }, animates ? QUIT_GEN_GLITCH_MS : 420);
  }

  /** Zero at last. The generated button appears and it is a real one. */
  function releaseQuitButton() {
    quitReleased = true;
    if (genFill) { try { genFill.style.width = '100%'; } catch (_e) {} }
    if (genLine) genLine.textContent = 'Your quit button is ready. Thank you for waiting ♥';
    if (genNode) { try { genNode.classList.add('is-ready'); } catch (_e) {} }
    if (genBtn) {
      try {
        genBtn.hidden = false;
        genBtn.disabled = false;
        genBtn.focus();
      } catch (_e) {}
    }
    try { shim.log('pause: quit button generated'); } catch (_e) {}
  }

  /** Tear the ceremony down. Idempotent; safe from close/open/dispose. */
  function cancelQuitGeneration() {
    if (genT) { try { N.clearTimeout(genT); } catch (_e) {} genT = 0; }
    if (genScramble) { try { N.clearInterval(genScramble); } catch (_e) {} genScramble = 0; }
    if (genNode) { try { if (genNode.parentNode) genNode.parentNode.removeChild(genNode); } catch (_e) {} }
    genNode = genLine = genFill = genBtn = null;
    quitReleased = false;
  }

  /**
   * Detach the Quit button: the real one keeps its box and goes invisible, a
   * body-parented clone tumbles out of frame. Mirrors boot.js §5b's falling HUD
   * counter (knockCounterOff / startCounterFall) beat for beat, including the
   * reflow trap — `visibility: hidden` rather than `display: none`, so the
   * actions column keeps the gap and Resume/Options do not jump upward under
   * the cursor the instant the button leaves.
   */
  function knockQuitOff(el) {
    quitFallen = true;
    if (!el) return;
    try { shim.log('pause: quit button fell off'); } catch (_e) {}
    let r = null;
    try { r = el.getBoundingClientRect(); } catch (_e) {}
    try {
      el.classList.add('kw-quit-gone');
      el.disabled = true;
    } catch (_e) {}
    // The dead button may hold focus; hand it to Resume so the keyboard is
    // never stranded on a disabled control.
    const res = root.querySelector('[data-act="resume"]');
    if (res) { try { res.focus(); } catch (_e) {} }
    if (!r) return;

    let clone = null;
    try {
      clone = el.cloneNode(true);
      clone.classList.remove('kw-quit-gone');
      clone.classList.add('kw-quit-fall');
      clone.disabled = true;
      clone.setAttribute('aria-hidden', 'true');
      clone.setAttribute('tabindex', '-1');
      clone.style.left = r.left + 'px';
      clone.style.top = r.top + 'px';
      clone.style.width = r.width + 'px';
      clone.style.height = r.height + 'px';
      // <body>, not the menu: the clone has to outlive a close() that unmounts
      // the root, exactly like the counter clone outlives its card.
      doc.body.appendChild(clone);
    } catch (_e) { return; }
    fallNode = clone;

    if (!fallAnimates() || !N.raf) {
      // Reduced motion (or no rAF at all): it still refuses to quit, it just
      // goes, without a tumble.
      try { clone.classList.add('kw-quit-fall--plain'); } catch (_e) {}
      N.setTimeout(() => {
        try { clone.style.opacity = '0'; clone.style.transform = 'translateY(46px)'; } catch (_e) {}
      }, 20);
      fallT = N.setTimeout(removeQuitFall, 520);
      return;
    }
    startQuitFall(clone, r);
  }

  /**
   * Gravity + tumble + drift, on the NATIVE clock.
   *
   * The pause menu is open while this runs, which means the shimmed
   * requestAnimationFrame is DEFERRING every callback and both `performance.now`
   * and `Date.now` are frozen — a fall driven by any of the three would never
   * advance a pixel. `N` was captured by captureNative() before the shim was
   * installed, so N.raf / N.perfNow / N.setTimeout are the real ones. (The prior
   * art in boot.js uses Date.now, which is correct THERE and would be broken
   * here for exactly this reason.)
   */
  function startQuitFall(node, rect) {
    const G = 0.0024;                                   // px per ms^2
    let x = 0, y = 0, rot = 0;
    let vy = -0.06 - Math.random() * 0.06;              // small hop as it detaches
    const vx = (Math.random() < 0.5 ? -1 : 1) * (0.02 + Math.random() * 0.06);
    const spin = (Math.random() < 0.5 ? -1 : 1) * (0.05 + Math.random() * 0.12); // deg/ms
    const floor = (win.innerHeight || 800) + 200;
    let last = N.perfNow();
    const tick = () => {
      const t = N.perfNow(); const dt = Math.min(48, t - last); last = t;
      vy += G * dt; y += vy * dt; x += vx * dt; rot += spin * dt;
      try {
        node.style.transform =
          'translate(' + x.toFixed(1) + 'px, ' + y.toFixed(1) + 'px) rotate(' + rot.toFixed(1) + 'deg)';
      } catch (_e) {}
      if (rect.top + y > floor) { removeQuitFall(); return; }
      fallRaf = N.raf(tick);
    };
    fallRaf = N.raf(tick);
  }

  /** Drop the falling clone + its clock. Idempotent. */
  function removeQuitFall() {
    if (fallRaf) { if (N.caf) { try { N.caf(fallRaf); } catch (_e) {} } fallRaf = 0; }
    if (fallT) { try { N.clearTimeout(fallT); } catch (_e) {} fallT = 0; }
    if (fallNode) { try { if (fallNode.parentNode) fallNode.parentNode.removeChild(fallNode); } catch (_e) {} fallNode = null; }
  }

  function fallAnimates() {
    try {
      if (getPrefs().sparkles === false) return false;
      return !(win.matchMedia && win.matchMedia('(prefers-reduced-motion: reduce)').matches);
    } catch (_e) { return true; }
  }

  /**
   * BAND FEED — called once per beat from boot.js's loop. The single channel by
   * which the shell learns how deep the run has got: it drives the palette /
   * music corruption and arms the Quit prank from band 3 onward.
   */
  function setBand(band, depth) {
    if (destroyed) return;
    deepBand = isDeepBand(band);
    try { setBandDepth(band, depth); } catch (_e) { /* cosmetic; never fatal */ }
  }

  function resetQuitButton(r) {
    const q = r.querySelector('[data-act="quit"]');
    if (!q || quitting) return;
    q.textContent = 'Quit run';
    q.classList.remove('kw-pause-quit--armed', 'kw-quit-gone', 'kw-quit-spent');
    q.disabled = false;
  }

  /**
   * A promise that resolves as soon as the run is not paused. boot.js awaits
   * this at the card-swap seam so a resume never lands mid-mount.
   */
  function gate() {
    if (!open_ || destroyed) return Promise.resolve();
    return new Promise((res) => gateWaiters.push(res));
  }

  function dispose() {
    if (destroyed) return;
    if (open_) close();
    destroyed = true;
    removeQuitFall();
    cancelQuitGeneration();
    // The run is over: the shell goes back to being innocent. boot.js disposes
    // the pause menu before the outro ceremony, so this is also what guarantees
    // a SECOND run in the same page starts from a clean palette and a clean
    // song (installPause() resets again on the way in, belt and braces).
    try { resetCorruption(); } catch (_e) {}
    win.removeEventListener('keydown', probe, true);
    doc.removeEventListener('keydown', opener, true);
    for (const t of KEY_EVENTS) win.removeEventListener(t, keyBlocker, true);
    for (const t of PTR_EVENTS) win.removeEventListener(t, ptrBlocker, true);
    try { unsubPrefs(); } catch (_e) {}
    try { unsubFs(); } catch (_e) {}
    try { if (btn.parentNode) btn.parentNode.removeChild(btn); } catch (_e) {}
    try { if (root.parentNode) root.parentNode.removeChild(root); } catch (_e) {}
    const waiters = gateWaiters.splice(0, gateWaiters.length);
    for (const r of waiters) { try { r(); } catch (_e) {} }
    if (active === handle) { active = null; try { delete win.__intakePauseHandle; } catch (_e) { win.__intakePauseHandle = null; } }
  }

  const handle = { dispose, isPaused: () => open_, open, close, gate, setBand, button: btn };
  active = handle;
  try { win.__intakePauseHandle = handle; } catch (_e) {}
  return handle;
}

/* ----------------------------------------------------------------------------
 * DOM construction
 * -------------------------------------------------------------------------- */
function buildRoot(doc) {
  const root = doc.createElement('div');
  root.className = 'kw-root kw-pause-root';
  root.setAttribute('role', 'dialog');
  root.setAttribute('aria-modal', 'true');
  root.setAttribute('aria-label', 'Paused');
  root.hidden = true;

  const scrim = doc.createElement('div');
  scrim.className = 'kw-scrim kw-pause-scrim';
  root.appendChild(scrim);

  const decor = doc.createElement('div');
  decor.className = 'kw-decor-layer kw-pause-decor';
  for (let i = 0; i < 12; i++) {
    const s = doc.createElement('span');
    s.className = 'kw-sparkle';
    s.style.left = (4 + Math.random() * 92).toFixed(1) + '%';
    s.style.top = (6 + Math.random() * 88).toFixed(1) + '%';
    s.style.animationDelay = (Math.random() * 2.6).toFixed(2) + 's';
    decor.appendChild(s);
  }
  for (let i = 0; i < 5; i++) {
    const h = doc.createElement('span');
    h.className = 'kw-heart';
    h.textContent = i % 2 ? '♥' : '✧';
    h.style.left = (8 + Math.random() * 84).toFixed(1) + '%';
    h.style.bottom = '-4%';
    h.style.animationDelay = (Math.random() * 6).toFixed(2) + 's';
    decor.appendChild(h);
  }
  root.appendChild(decor);

  const panel = doc.createElement('div');
  panel.className = 'kw-panel kw-pause-panel kw-pop-in';

  const ruleTop = doc.createElement('hr');
  ruleTop.className = 'kw-rainbow-rule';
  panel.appendChild(ruleTop);

  const title = doc.createElement('h2');
  title.className = 'kw-pause-title kw-bounce-line';
  const word = 'paused!';
  for (let i = 0; i < word.length; i++) {
    const g = doc.createElement('span');
    g.className = 'kw-bl';
    g.style.setProperty('--kw-i', String(i));
    g.textContent = word[i];
    title.appendChild(g);
  }
  panel.appendChild(title);

  const sub = doc.createElement('p');
  sub.className = 'kw-pause-sub';
  sub.textContent = 'take a little break, sweetie ♥';
  panel.appendChild(sub);

  const acts = doc.createElement('div');
  acts.className = 'kw-pause-actions';
  acts.appendChild(mkBtn(doc, 'resume', 'Resume ♥', 'kw-btn'));
  acts.appendChild(mkBtn(doc, 'options', 'Options', 'kw-btn kw-btn--soft'));
  // THE WAY OUT OF FULLSCREEN. Deliberately a first-class button and not a row
  // inside Options: the whole safety argument for a fullscreen mode is that
  // leaving it is never a hunt, and this menu is one Escape away from anywhere
  // in the run. It is also the one button in this column no prank ever touches.
  acts.appendChild(mkBtn(doc, 'fullscreen', 'Fullscreen', 'kw-btn kw-btn--soft kw-pause-fs'));
  acts.appendChild(mkBtn(doc, 'quit', 'Quit run', 'kw-btn kw-btn--soft kw-pause-quit'));
  panel.appendChild(acts);

  const ruleBot = doc.createElement('hr');
  ruleBot.className = 'kw-rainbow-rule';
  panel.appendChild(ruleBot);

  const disc = doc.createElement('p');
  disc.className = 'kw-disclaimer kw-pause-disclaimer';
  disc.textContent = 'SESSION SUSPENDED · OBSERVATION CONTINUES · DO NOT LEAVE THE ROOM';
  panel.appendChild(disc);

  root.appendChild(panel);
  return root;
}

/**
 * The "your quit button is generating" block: a status line, a progress bar
 * that is telling the truth about a lie, and the generated Quit button itself,
 * built hidden and revealed at zero.
 *
 * aria-live is deliberately OFF. A screen reader announcing a new number every
 * second for half a minute is not a joke, it is an assault; the block announces
 * itself once when the button appears and takes focus instead.
 */
function buildQuitGen(doc) {
  const wrap = doc.createElement('div');
  wrap.className = 'kw-quit-gen';
  wrap.setAttribute('role', 'status');
  wrap.setAttribute('aria-live', 'off');

  const line = doc.createElement('p');
  line.className = 'kw-quit-gen__line';
  wrap.appendChild(line);

  const bar = doc.createElement('div');
  bar.className = 'kw-quit-gen__bar';
  const fill = doc.createElement('span');
  fill.className = 'kw-quit-gen__fill';
  bar.appendChild(fill);
  wrap.appendChild(bar);

  const b = doc.createElement('button');
  b.type = 'button';
  b.className = 'kw-btn kw-btn--soft kw-pause-quit kw-quit-gen__btn';
  b.setAttribute('data-act', 'quit');
  b.textContent = 'Quit run ♥';
  b.disabled = true;
  b.hidden = true;
  wrap.appendChild(b);

  return wrap;
}

function mkBtn(doc, act, label, cls) {
  const b = doc.createElement('button');
  b.type = 'button';
  b.className = cls;
  b.setAttribute('data-act', act);
  b.textContent = label;
  return b;
}

/** Honour the `sparkles` pref (and let Options toggle it live). */
function applyDecorPref(root) {
  try {
    const on = getPrefs().sparkles !== false;
    root.classList.toggle('kw-no-decor', !on);
  } catch (_e) { /* prefs are best-effort */ }
}

/**
 * kawaii.css is not linked from index.html (that page is the RUN's, and the kit
 * is shell-only), so mount it on demand. Idempotent across menu/options/pause.
 */
function ensureStylesheet(doc) {
  let href;
  try { href = new URL('./kawaii.css', import.meta.url).href; }
  catch (_e) { href = './ui/kawaii.css'; }
  try {
    // Keyed by the same data attribute menu.js uses. Href equality alone is not
    // enough: whichever module mounts first wins, and menu.js only recognises
    // the attribute — so a pause-first mount (menu import failed, or a harness
    // route) would otherwise leave menu.js adding a second identical sheet.
    if (doc.querySelector('link[data-kw-kit="1"]')) return;
    const link = doc.createElement('link');
    link.rel = 'stylesheet';
    link.href = href;
    link.setAttribute('data-kw-kit', '1');
    (doc.head || doc.documentElement).appendChild(link);
  } catch (_e) { /* a missing sheet degrades to unstyled, never fatal */ }
}

/* ----------------------------------------------------------------------------
 * small helpers
 * -------------------------------------------------------------------------- */
function isEsc(e) { return !!e && (e.key === 'Escape' || e.key === 'Esc' || e.keyCode === 27); }

function safe(fn) { try { return fn(); } catch (_e) { return undefined; } }

/** Pause every playing <video>/<audio> outside the menu; remember what we touched. */
function pauseMedia(doc, root) {
  const held = [];
  try {
    const media = doc.querySelectorAll('video, audio');
    for (const m of media) {
      if (root.contains(m) || m.paused) continue;
      try { m.pause(); held.push(m); } catch (_e) {}
    }
  } catch (_e) {}
  return held;
}

function resumeMedia(held) {
  for (const m of held) {
    try { const p = m.play(); if (p && typeof p.catch === 'function') p.catch(() => {}); } catch (_e) {}
  }
}

/** Keep Tab inside the dialog. */
function trapTab(e, root) {
  const items = Array.prototype.filter.call(
    root.querySelectorAll('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'),
    (n) => !n.disabled && n.offsetParent !== null);
  if (!items.length) return;
  const first = items[0], last = items[items.length - 1];
  const cur = document.activeElement;
  if (e.shiftKey && (cur === first || !root.contains(cur))) { e.preventDefault(); try { last.focus(); } catch (_e) {} }
  else if (!e.shiftKey && cur === last) { e.preventDefault(); try { first.focus(); } catch (_e) {} }
}

/** No DOM (node / import-safety): a handle that does nothing but is safe to call. */
function inertHandle() {
  return {
    dispose() {}, isPaused() { return false; }, open() {}, close() {},
    gate() { return Promise.resolve(); }, setBand() {}, button: null,
  };
}
