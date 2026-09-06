/* ============================================================================
 * race/frameFx.js - the full-frame beats a hand chart drops on the run.
 * Implements CHART.md section `Hand-authored charts`: the `fx` half of a hand cue.
 *
 *   createFrameFx({ root, reducedMotion, sfx }) -> { play, tick, dispose, FX_KINDS }
 *
 * The auto charter only ever spends an event on the world: a bubble, a jump, a pour. An author
 * wants the other thing, the beat that happens TO the player - the lids closing on a trigger, the
 * frame going black under a mantra, one white flash on a finger snap. Those live here because they
 * are the only race effects that are not the world: three lightweight divs over the whole viewport,
 * driven by tick(dt) off the run's own frame loop so a paused run holds them exactly where they are.
 *
 * ONE AT A TIME, ALWAYS. play() cancels whatever was running first. Two lids and a veil stacking on
 * each other is not a second effect, it is a blackout the author did not write, and on a hypno track
 * a blackout nobody asked for is the difference between a beat and a scare.
 *
 * `shake`, `melt` and `flash` are in FX_KINDS but are NOT drawn here: run.js already owns a screen
 * shake, THE MIX and the engine flash, so it maps those three itself and only the frame-owning
 * three ever reach play().
 * ==========================================================================*/

import { FX_IDS } from './chart.js';

export { FX_IDS };

/** The table the editor palette reads and run.js dispatches on. `dur` is the default in seconds. */
export const FX_KINDS = {
  blink:    { id: 'blink',    label: 'blink',    dur: 0.45, frame: true,  what: 'two lids close over the whole frame and open again' },
  blackout: { id: 'blackout', label: 'blackout', dur: 1.2,  frame: true,  what: 'the frame fades to black and back' },
  snap:     { id: 'snap',     label: 'snap',     dur: 0.12, frame: true,  what: 'one white frame, then a short freeze of the world' },
  shake:    { id: 'shake',    label: 'shake',    dur: 0.4,  frame: false, what: 'a jolt of the world (run.js: game/screenShake.js)' },
  melt:     { id: 'melt',     label: 'melt',     dur: 0,    frame: false, what: 'the braindrain overlay through THE MIX (run.js)' },
  flash:    { id: 'flash',    label: 'flash',    dur: 0.3,  frame: false, what: 'the engine pulse flash (run.js)' },
};

/** How long the world holds still after a snap. CHART.md: 120 ms. */
export const SNAP_FREEZE_SEC = 0.12;
/** A fully shut lid is half the frame from the top and half from the bottom. */
const LID_MAX_PCT = 50;
/** Of `dur`: the lids close over the first slice, hold shut, then open over the rest. */
const BLINK_CLOSE = 0.4, BLINK_HOLD_SEC = 0.08;
/** The darkest a blackout may ever get, and the cap again under reduced motion. */
const BLACKOUT_MAX = 0.85, BLACKOUT_REDUCED = 0.5;
/** Under reduced motion a lid beat becomes a plain pulse at this share of its strength. */
const REDUCED_FLASH = 0.5;
/** race/audio.js HOST_SFX is a closed set and a name outside it is silent on the host. There is no
 *  `snap` in that catalogue, so the beat borrows the click, which is the fallback CHART.md names. */
const SNAP_SFX = 'ui_click';

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const ease = (p) => (p < 0.5 ? 2 * p * p : 1 - Math.pow(-2 * p + 2, 2) / 2);

export function createFrameFx({ root, reducedMotion = false, sfx = null } = {}) {
  let disposed = false;
  let cur = null;   // { id, strength, dur, t }

  // WHERE THIS SITS IN THE LADDER. race.html stacks #race-root as: the canvas (the world and the
  // media on the tube walls), the race chrome at z3, #sf-hud at z10 carrying payloadFx's wash (z4)
  // and its front card (z9), and the boot splash at z30. z8 puts the lids over the whole world and
  // everything painted into it while leaving #sf-hud on top, so a blackout never swallows the video
  // card, the pause menu or the score readouts the player needs to find their way back out.
  const el = document.createElement('div');
  el.className = 'race-framefx';
  el.setAttribute('aria-hidden', 'true');
  el.style.cssText = 'position:fixed;inset:0;z-index:8;pointer-events:none;overflow:hidden;';

  const mk = (css) => { const d = document.createElement('div'); d.style.cssText = css; el.appendChild(d); return d; };
  const lidTop = mk('position:absolute;left:0;right:0;top:0;height:0;background:#000;');
  const lidBot = mk('position:absolute;left:0;right:0;bottom:0;height:0;background:#000;');
  const veil = mk('position:absolute;inset:0;background:#000;opacity:0;');
  if (root) root.appendChild(el);

  function rest() {
    cur = null;
    lidTop.style.height = '0';
    lidBot.style.height = '0';
    veil.style.opacity = '0';
    veil.style.background = '#000';
  }

  function lids(pct) {
    const v = clamp(pct, 0, LID_MAX_PCT).toFixed(2) + '%';
    lidTop.style.height = v;
    lidBot.style.height = v;
  }

  /**
   * Run one beat. Returns null, or what the run has to do about it:
   *   { freezeSec } hold the world this long (a snap), { flash } pulse instead (reduced motion).
   */
  function play(id, strength = 1, dur = null) {
    if (disposed) return null;
    const kind = FX_KINDS[id];
    if (!kind || !kind.frame) return null;
    const s = clamp(isFinite(Number(strength)) ? Number(strength) : 1, 0, 1);
    const secs = (dur != null && isFinite(Number(dur)) && Number(dur) > 0) ? Number(dur) : kind.dur;
    rest();

    if (reducedMotion && (id === 'blink' || id === 'snap')) return { flash: s * REDUCED_FLASH };

    if (id === 'snap') {
      // one white frame: painted now, cleared on the next tick, and the world stops dead behind it
      veil.style.background = '#fff';
      veil.style.opacity = String(s);
      cur = { id, strength: s, dur: secs, t: 0 };
      if (sfx) { try { sfx(SNAP_SFX, 0.9); } catch (e) { /* audio gone */ } }
      return { freezeSec: SNAP_FREEZE_SEC };
    }
    cur = { id, strength: s, dur: Math.max(0.05, secs), t: 0 };
    return null;
  }

  /** Walk the running beat on. Safe to call every frame with nothing running. */
  function tick(dt) {
    if (disposed || !cur) return;
    const d = Number(dt) || 0;
    cur.t += d;
    if (cur.id === 'snap') { if (cur.t > 0) rest(); return; }   // one frame of white, then gone
    if (cur.id === 'blink') {
      const close = cur.dur * BLINK_CLOSE, open = cur.dur - close, shut = cur.strength * LID_MAX_PCT;
      if (cur.t < close) lids(shut * ease(cur.t / close));
      else if (cur.t < close + BLINK_HOLD_SEC) lids(shut);
      else if (cur.t < close + BLINK_HOLD_SEC + open) lids(shut * (1 - ease((cur.t - close - BLINK_HOLD_SEC) / open)));
      else rest();
      return;
    }
    if (cur.id === 'blackout') {
      const cap = Math.min(cur.strength, reducedMotion ? BLACKOUT_REDUCED : BLACKOUT_MAX);
      const half = cur.dur / 2;
      if (cur.t < half) veil.style.opacity = String(cap * ease(cur.t / half));
      else if (cur.t < cur.dur) veil.style.opacity = String(cap * (1 - ease((cur.t - half) / half)));
      else rest();
    }
  }

  function dispose() {
    if (disposed) return;
    disposed = true;
    rest();
    if (el.parentNode) el.parentNode.removeChild(el);
  }

  return { play, tick, dispose, stop: rest, FX_KINDS, get live() { return !!cur; } };
}

export default createFrameFx;
