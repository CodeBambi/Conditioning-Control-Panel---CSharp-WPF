/* ============================================================================
 * ramp/index.js - the conditioning ramp for Piece by Piece.
 *
 * The chess rules never change. What changes is the player: as a side's clock
 * drains and as pieces come off the board, that side's screen gets busier,
 * softer and harder to read. Taking a piece ramps you MORE than losing one, so
 * the player who is ahead on material is also the one squinting.
 *
 * attachRamp({ bus, root, stage, media }) -> { dispose, setEnabled, debug }
 *
 * CONTRACT (the board owns these, this module only listens):
 *   turn     {side,ply,clocks:{w,b},total}   capture {by,piece,square,victimSide}
 *   check    {side}                          grab    {square,piece,screen:{x,y}}
 *   dragmove {screen:{x,y}}                  drop    {ok}
 *   gameover {result,winner}                 clock   {w,b,total,active}  every 250ms
 *   local    {sides:[...]}                   once at boot
 *
 * NOTHING here may throw at import or at attach time. A missing bus, a missing
 * #fx, a missing #stage, a missing media pool and a missing board API all
 * degrade to a console.warn and a quieter ramp.
 * ==========================================================================*/

import { createMeter, RAMP_TUNING, clamp01 } from './meter.js';
import { createSchedule, videoHoldMs, sustainedFor } from './schedule.js';
import { createLayerStack } from './layers/index.js';
import { createFixtureMedia } from './media.js';

const TICK_MS = 90;            // scheduling cadence; layers animate on their own
const BOARD_PUSH_MS = 200;     // how often we hand the meter to A's board
const CSS_ID = 'pbp-ramp-css';

const warn = (msg) => { try { console.warn('[pbp/ramp] ' + msg); } catch { /* no console */ } };
const otherSide = (s) => (s === 'w' ? 'b' : 'w');

/** Inject ramp.css once, resolved next to this module (works from any page). */
function ensureCss() {
  try {
    if (typeof document === 'undefined' || document.getElementById(CSS_ID)) return;
    const link = document.createElement('link');
    link.id = CSS_ID;
    link.rel = 'stylesheet';
    link.href = new URL('./ramp.css', import.meta.url).href;
    (document.head || document.documentElement).appendChild(link);
  } catch { warn('could not inject ramp.css'); }
}

/** A root to hang layers on. Falls back to a self-made one rather than dying. */
function resolveRoot(root) {
  if (root && root.appendChild) return root;
  try {
    if (typeof document === 'undefined') return null;
    warn('no #fx root supplied; creating a fallback layer host');
    const el = document.createElement('div');
    el.className = 'pbp-fx pbp-fx-fallback';
    document.body.appendChild(el);
    return el;
  } catch { return null; }
}

export function attachRamp(opts = {}) {
  const bus = opts.bus && typeof opts.bus.on === 'function' ? opts.bus : null;
  if (!bus) warn('no bus supplied; the ramp will sit idle');

  ensureCss();
  const root = resolveRoot(opts.root);
  const stage = opts.stage && opts.stage.style ? opts.stage : null;
  if (!stage) warn('no #stage supplied; CSS filter layers (melt tint, blur) are off');

  const tuning = opts.tuning || RAMP_TUNING;
  const media = opts.media || createFixtureMedia([]);
  const meter = createMeter({ tuning });
  const schedule = createSchedule({ tuning, seed: opts.seed || 'pbp-ramp' });

  // The front plane carries the video card: in front of the POV, still
  // click-through, so the player can hover the board they cannot see.
  let front = null;
  if (root) {
    try {
      front = document.createElement('div');
      front.className = 'pbp-fx-front';
      (root.parentNode || root).appendChild(front);
    } catch { front = null; }
  }

  const stack = createLayerStack({ root, front, stage, media, tuning, rng: schedule.rng });

  let enabled = true;
  let disposed = false;
  let rafId = 0;
  let lastTick = 0;
  let lastBoardPush = 0;
  let ticks = 0;   // scheduling beats served, so a harness can prove the loop runs
  let overrideMeter = null;   // dev harness: pin the meter regardless of the game
  const applied = { melt: -1, blur: -1, spiral: -1, overlay: -1 };

  const now = () => (typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now());

  /** The side the ramp is acting on: whoever is on the move. */
  const actingSide = () => meter.active;
  const liveMeter = () => (overrideMeter == null ? meter.meterFor(actingSide()) : clamp01(overrideMeter));
  const liveHeat = (t) => (overrideMeter == null ? meter.heatFor(actingSide(), t) : clamp01(overrideMeter));

  /** Hand the meter to A's board if it grew those knobs. Always guarded. */
  function pushToBoard(m) {
    try {
      const board = (typeof window !== 'undefined' && window.PBP && window.PBP.board) || null;
      if (!board) return;
      if (typeof board.setWobble === 'function') board.setWobble(clamp01(m * tuning.wobbleScale));
      if (typeof board.setCameraSway === 'function') board.setCameraSway(clamp01(m * tuning.swayScale));
    } catch { /* A's board is optional and may change under us */ }
  }

  function applySustained(sus) {
    for (const name of ['melt', 'blur', 'spiral', 'overlay']) {
      const spec = sus[name];
      const key = spec.on ? (spec.alpha != null ? spec.alpha : spec.px) : -1;
      // retune only on a real change, so we are not writing style every 90ms
      if (Math.abs(key - applied[name]) < 0.005) continue;
      applied[name] = key;
      stack.setSustained(name, spec);
    }
  }

  function tick() {
    rafId = 0;
    if (disposed) return;
    schedulePump();
    if (!enabled) return;
    const t = now();
    if (t - lastTick >= TICK_MS) {
      lastTick = t;
      ticks += 1;
      const m = liveMeter();
      const out = schedule.tick(t, liveHeat(t), m);
      for (const kind of out.fire) stack.oneshot(kind, { heat: out.heat });
      applySustained(out.sustained);
      if (t - lastBoardPush >= BOARD_PUSH_MS) { lastBoardPush = t; pushToBoard(m); }
    }
  }

  function schedulePump() {
    if (disposed || rafId) return;
    try {
      rafId = requestAnimationFrame(tick);
    } catch {
      rafId = setTimeout(tick, TICK_MS);   // no rAF (a test shim): still tick
    }
  }

  /* ---- bus wiring --------------------------------------------------------- */
  const handlers = {
    local() { /* both sides are on this screen: nothing to gate, kept for parity */ },
    clock(p) { meter.setClock(p); },
    turn(p) {
      meter.setTurn(p);
      // The card belongs to the side that just MOVED and is now waiting: it
      // rides over the board while the opponent thinks. In hotseat both sides
      // are local, so this is simply every turn.
      const waiting = otherSide(p && p.side === 'b' ? 'b' : 'w');
      const holdMs = videoHoldMs(overrideMeter == null ? meter.meterFor(waiting) : clamp01(overrideMeter), tuning);
      stack.videoCard({ holdMs, side: waiting });
    },
    capture(p) {
      const t = now();
      meter.noteCapture(p, t);
      const taker = p && p.by === 'b' ? 'b' : 'w';
      // both sides feel it; the one who took the piece feels it harder
      stack.burst(schedule.burstFor('taker', meter.heatFor(taker, t)));
      stack.burst(schedule.burstFor('victim', meter.heatFor(otherSide(taker), t)));
    },
    check() { stack.oneshot('flash', { heat: liveHeat(now()) }); },
    grab(p) { stack.grab(p); },
    dragmove(p) { stack.dragmove(p); },
    drop(p) { stack.drop(p); },
    gameover() {
      meter.over = true;
      setEnabled(false);
    },
  };
  const bound = [];
  if (bus) {
    for (const [type, fn] of Object.entries(handlers)) {
      const safe = (payload) => { try { fn(payload); } catch (e) { warn(type + ' handler: ' + (e && e.message)); } };
      try { bus.on(type, safe); bound.push([type, safe]); } catch { warn('could not subscribe to ' + type); }
    }
  }

  function setEnabled(on) {
    enabled = !!on;
    if (!enabled) {
      stack.clear();
      for (const k of Object.keys(applied)) applied[k] = -1;
      pushToBoard(0);
    } else { schedulePump(); }
    return enabled;
  }

  function dispose() {
    if (disposed) return;
    disposed = true;
    if (rafId) { try { cancelAnimationFrame(rafId); } catch { clearTimeout(rafId); } rafId = 0; }
    if (bus) for (const [type, fn] of bound) { try { bus.off(type, fn); } catch { /* gone already */ } }
    bound.length = 0;
    try { stack.dispose(); } catch { /* best effort */ }
    if (front && front.remove) front.remove();
    pushToBoard(0);
  }

  function debug() {
    const t = now();
    return {
      enabled,
      ticks,
      side: actingSide(),
      meter: liveMeter(),
      heat: liveHeat(t),
      override: overrideMeter,
      model: meter.snapshot(t),
      layers: stack.debug ? stack.debug() : null,
      media: media.stats ? media.stats() : null,
      tuning,
      /** Dev harness only: pin the meter (null gives the game back control).
       *  Sustained layers are applied at once so a screenshot does not have to
       *  wait for the next rAF beat (headless barely gets any). */
      setMeter(v) {
        overrideMeter = v == null ? null : clamp01(v);
        applySustained(sustainedFor(liveMeter(), tuning));
        return overrideMeter;
      },
      /** Dev harness only: fire one layer by name right now. */
      fire(kind, o) {
        if (kind === 'videoCard') stack.videoCard(o || { holdMs: videoHoldMs(liveMeter(), tuning) });
        else stack.oneshot(kind, o || { heat: liveHeat(now()) });
      },
      /** Dev harness only: fill the screen for a screenshot without waiting. */
      prime(n = 5) {
        const heat = liveHeat(now());
        for (let i = 0; i < n; i++) { stack.oneshot('flash', { heat }); stack.oneshot('gifRain', { heat }); }
      },
      stack,
    };
  }

  schedulePump();
  return { dispose, setEnabled, debug };
}

export default attachRamp;
