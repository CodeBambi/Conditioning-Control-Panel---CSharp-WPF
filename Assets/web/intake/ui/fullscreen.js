/* ============================================================================
 * ui/fullscreen.js — the intake's window mode, in ONE place.
 *
 * WHY C# OWNS THE WINDOW WHEN WE ARE HOSTED. The browser Fullscreen API eats the
 * first Escape to leave fullscreen, and that behaviour is mandated — a page
 * cannot preventDefault it. This page's Escape is already spoken for four rungs
 * deep (steering's hijack disarm, then the pause menu opening, then the Options
 * panel closing, then the pause menu closing), so letting Chromium silently
 * swallow the first press would put a random hole in that ladder. So hosted we
 * never touch the browser API: enter/leave post `fullscreen-set` and C#
 * borderless-toggles its own WPF window (ChaosWebViewHost.SetFullscreen), which
 * is exactly what DtRH does (see dtrh/engine/scene.js's FS shim). Escape never
 * leaves our grip, and the host is free to remember the mode across runs.
 *
 * ESCAPE IS NOT AN EXIT FROM FULLSCREEN HERE, deliberately — unlike DtRH, where
 * Escape walks a ladder, this page's Escape CLOSES whatever it opened, so a
 * fullscreen rung would make the same key sometimes close the pause menu and
 * sometimes drop the window mode. The exits are F11 (installed below, before
 * every other keyboard consumer) and the labelled button in the pause menu,
 * which is always one Escape away and never subject to the Quit prank.
 *
 * STANDALONE (harness / plain browser) keeps the element Fullscreen API, because
 * there is no C# to ask. There the browser DOES eat the first Escape, so the
 * exit edge is reconciled into a pause — the same trick DtRH plays.
 *
 * NO TIMERS. Everything here is edge-driven; the one piece of state that would
 * normally want a self-healing timeout (was this exit deliberate?) is a
 * timestamp comparison instead, so this module has nothing that could keep
 * ticking behind a paused run.
 * ========================================================================== */

import * as shim from '../web-shim.js';

const doc = (typeof document !== 'undefined') ? document : null;
const win = (typeof window !== 'undefined') ? window : null;

const listeners = new Set();

function emit() {
  for (const fn of Array.from(listeners)) {
    try { fn(isActive()); } catch (_e) { /* a bad listener never breaks a toggle */ }
  }
}

/* ----------------------------------------------------------------------------
 * TRANSPORT — hosted asks C#; standalone drives the element API.
 * -------------------------------------------------------------------------- */
let hostedOn = false;          // last state C# echoed back
let lastDeliberateLeave = 0;   // Date.now() of a leave WE asked for (standalone)

const root = doc ? doc.documentElement : null;
const request = root && (root.requestFullscreen || root.webkitRequestFullscreen);
const exit = doc && (doc.exitFullscreen || doc.webkitExitFullscreen);
const enabled = doc && (doc.fullscreenEnabled || doc.webkitFullscreenEnabled);

/** True when this build can change window mode at all (false on iOS Safari). */
export const supported = shim.isHosted ? true : !!(root && request && exit && enabled);

if (shim.isHosted) {
  // C# echoes the ACTUAL window state after every toggle, and posts it once on
  // init — so a run launched straight into a remembered fullscreen paints its
  // buttons correctly without ever having asked for the change itself.
  shim.on('fullscreen', (m) => {
    const next = !!(m && m.on);
    if (next === hostedOn) return;
    hostedOn = next;
    emit();
  });
} else if (doc) {
  const evt = ('onwebkitfullscreenchange' in doc) ? 'webkitfullscreenchange' : 'fullscreenchange';
  doc.addEventListener(evt, () => {
    emit();
    if (isActive()) return;                                  // only the EXIT edge
    // A deliberate leave (our own button / F11) already did what it meant to.
    // Anything else on this edge is the browser having eaten an Escape to get
    // out of fullscreen, which means the player pressed Escape and got nothing
    // they asked for — turn it into the pause they were reaching for.
    if (Date.now() - lastDeliberateLeave < 500) return;
    try {
      const h = win && win.__intakePauseHandle;
      if (h && typeof h.open === 'function') h.open();
    } catch (_e) { /* no pause installed yet (menu / briefing) */ }
  });
}

/** Is the window fullscreen right now? */
export function isActive() {
  if (shim.isHosted) return hostedOn;
  if (!doc) return false;
  return !!(doc.fullscreenElement || doc.webkitFullscreenElement);
}

/**
 * Ask for a window mode. Hosted this is a REQUEST — `isActive()` does not change
 * until C# echoes, which is what keeps the page from ever disagreeing with the
 * window it is painted in.
 */
export function setFullscreen(on) {
  const want = !!on;
  if (!supported || want === isActive()) return;
  if (shim.isHosted) {
    try { shim.send({ type: 'fullscreen-set', on: want }); } catch (_e) { /* host gone */ }
    return;
  }
  if (want) {
    try { const r = request.call(root); if (r && r.catch) r.catch(() => {}); } catch (_e) {}
  } else {
    lastDeliberateLeave = Date.now();
    try { const r = exit.call(doc); if (r && r.catch) r.catch(() => {}); } catch (_e) {}
  }
}

/** Flip it. Every affordance in the UI funnels through here. */
export function toggle() { setFullscreen(!isActive()); }

/** Fires on every confirmed change. Returns an unsubscribe function. */
export function subscribe(fn) {
  if (typeof fn !== 'function') return () => {};
  listeners.add(fn);
  return () => listeners.delete(fn);
}

/** The label every affordance shows, so they can never word it differently. */
export function actionLabel() { return isActive() ? 'Windowed' : 'Fullscreen'; }

/* ----------------------------------------------------------------------------
 * F11 — the always-available exit.
 *
 * Registered at MODULE LOAD, on window/capture. Both consumers (ui/options.js,
 * ui/pause.js) import this module statically, so this listener is in place
 * before installPause() adds its own capture-phase key blocker — and the pause
 * blocker additionally lets F11 through by name, so the ordering is belt AND
 * braces. A player who has somehow lost every on-screen affordance still has
 * one key that gets the title bar back.
 * -------------------------------------------------------------------------- */
if (win && supported) {
  win.addEventListener('keydown', (e) => {
    if (!e || e.key !== 'F11' || e.repeat) return;
    e.preventDefault();
    toggle();
  }, true);
}
