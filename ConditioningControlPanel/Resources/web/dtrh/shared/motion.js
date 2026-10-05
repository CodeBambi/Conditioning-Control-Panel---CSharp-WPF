/* ============================================================================
 * motion.js - the one motion level for the descent page.
 *
 * Three levels, matching the desktop app's Motion setting:
 *   'full'    - everything plays.
 *   'reduced' - half speed and half amplitude; no shake, no hit-stop, no flash
 *               brighter than a soft pulse.
 *   'off'     - still. Opacity fades only.
 *
 * Until the host says otherwise the level follows the OS reduced-motion
 * setting. boot.js calls setMotionLevel() once the host's init arrives.
 * Every module that adds movement reads this, never matchMedia on its own.
 * ==========================================================================*/

const LEVELS = ['full', 'reduced', 'off'];
const listeners = new Set();

function osLevel() {
  try {
    return window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'reduced' : 'full';
  } catch (_) { return 'full'; }
}

let level = typeof window === 'undefined' ? 'full' : osLevel();

export function motionLevel() { return level; }
export function motionFull() { return level === 'full'; }
export function motionOff() { return level === 'off'; }

/* Scale for amplitudes and speeds: 1 full, 0.5 reduced, 0 off. */
export function motionScale() { return level === 'full' ? 1 : level === 'reduced' ? 0.5 : 0; }

export function setMotionLevel(next) {
  const v = LEVELS.includes(next) ? next : osLevel();
  if (v === level) return;
  level = v;
  try { document.documentElement.dataset.motion = level; } catch (_) {}
  for (const fn of listeners) { try { fn(level); } catch (_) {} }
}

export function onMotionLevel(fn) { listeners.add(fn); return () => listeners.delete(fn); }

try { document.documentElement.dataset.motion = level; } catch (_) {}
