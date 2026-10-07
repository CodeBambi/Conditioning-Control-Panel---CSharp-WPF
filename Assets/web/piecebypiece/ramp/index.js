/* ============================================================================
 * ramp/index.js - the conditioning ramp for Piece by Piece.
 *
 * The chess rules never change. What changes is the player: as a side's clock
 * drains and as pieces come off the board, that side's screen gets busier,
 * softer and harder to read. Taking a piece ramps you MORE than losing one, so
 * the player who is ahead on material is also the one squinting.
 *
 * What the player sees is the THINK (owner, 2026-10-01): while the player sits
 * on their own move the screen climbs toward full, and the wash rises over the
 * board on a long think. The match pressure (clock + pieces) makes that climb
 * faster. THE BREATH (owner, 2026-10-02, there is no floor any more): the
 * moment a move is made everything for the player stops (pictures fade out,
 * the wash and the haze drop, the heartbeat and whispers go quiet) and stays
 * stopped until their own turn card; then the climb starts from nothing. A
 * local loss is THE FALL: everything comes up at once, holds, and drains away
 * under the end card.
 *
 * The player's dials (Options, game/preferences.js): Amount (how many pictures
 * pop), Strength (how see-through every wash and picture is) and Ramp (how long
 * the climb takes).
 *
 * attachRamp({ bus, root, stage, media, board }) -> { dispose, setEnabled, debug }
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

import { createMeter, createThinkClock, surgeLevel, RAMP_TUNING, clamp01, thinkRate, thinkFullMs, amountRate } from './meter.js';
import { createSchedule, videoHoldMs, sustainedFor } from './schedule.js';
import { createLayerStack } from './layers/index.js';
import { createFixtureMedia } from './media.js';
import { presentation, onPresentation } from '../game/preferences.js';

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
  if (root && root.appendChild) {
    // The page supplies the element (#fx); the ramp supplies the class. Without
    // it ramp.css's click-through guarantee never reaches the layers the ramp
    // hangs inside it, and the page's own rule is the only thing holding them
    // off the board. Position and z-index still come from the page's #fx rule,
    // which outranks .pbp-fx on specificity.
    try { root.classList.add('pbp-fx'); } catch { /* not an element we can class */ }
    return root;
  }
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
  // the player's dials; refreshed by onPresentation below
  let dials = { amount: 'normal', strength: 1, rampSpeed: 'normal' };
  const think = createThinkClock({ tuning, fullMs: () => thinkFullMs(dials.rampSpeed, tuning) });
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

  const stack = createLayerStack({ root, front, stage, media, tuning, rng: schedule.rng, board: opts.board });

  // declared before the subscription below, which can fire the moment it is made
  let hostHoldSec = null;     // pbp:settings.videoHoldSec, when the host sent one
  let reducedMotion = presentation().reducedMotion;  // pbp:settings.reducedMotion: the moving layers stop

  // A host-backed pool (media.js createHostMedia) forwards the host's own
  // settings frame. A fixture pool has no onSettings and this is simply skipped.
  let unsubSettings = null;
  if (media && typeof media.onSettings === 'function') {
    try {
      unsubSettings = media.onSettings((s) => {
        hostHoldSec = Number.isFinite(s && s.videoHoldSec) ? s.videoHoldSec : null;
        const wasReduced = reducedMotion;
        reducedMotion = presentation().reducedMotion || !!(s && s.reducedMotion);
        if (reducedMotion && !wasReduced) stack.clear();   // drop what is mid-flight
      });
    } catch { warn('host settings could not be subscribed'); }
  }

  let enabled = presentation().experience === 'distraction';
  let localSides = ['w', 'b'];
  let disposed = false;
  let rafId = 0;
  let lastTick = 0;
  let lastBoardPush = 0;
  let ticks = 0;   // scheduling beats served, so a harness can prove the loop runs
  let overrideMeter = null;   // dev harness: pin the meter regardless of the game
  let seenTurn = false;       // the board's seeding `turn` is not a played move
  let lastTurn = '';          // side:ply of the last turn, so a resync is not a move
  let solo = null;            // dev harness: show ONE sustained layer, for a screenshot
  let lastBeat = 0;           // the previous scheduling beat, for the think clock's step
  let nextCardAt = 0;         // the earliest the next think wash may rise
  let cardWasLive = false;
  let snapTimer = 0;          // clears the fast-transition class after a snap
  let surge = null;           // { at } while the loss surge runs
  let resting = false;        // THE BREATH: a move was made, nothing for us until our turn card
  let restWake = 0;           // ms of our own live turn spent resting, for when no card comes
  // OFF is the "this layer is currently off" key; UNSET is "we have never
  // written it". They must differ, or a forced re-apply of an off layer reads
  // as no change and is skipped, which is how a solo call once left the layers
  // it was meant to hide still showing.
  const OFF = -1;
  const UNSET = Number.NaN;
  const applied = { melt: UNSET, blur: UNSET, spiral: UNSET, overlay: UNSET };

  const now = () => (typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now());

  /** The side the ramp is acting on: whoever is on the move. */
  const actingSide = () => localSides.length === 1 ? localSides[0] : meter.active;
  /** What the layers ride: the surge at a loss, nothing during the breath, else the think. */
  function liveMeter(t = now()) {
    if (overrideMeter != null) return clamp01(overrideMeter);
    if (surge) return surgeLevel(t - surge.at, tuning);
    if (resting) return 0;
    return think.lift();
  }
  /** The meter plus what is left of the last capture kick (one-shots ride this). */
  function liveHeat(t) {
    if (overrideMeter != null || surge) return liveMeter(t);
    if (resting) return 0;
    return clamp01(liveMeter(t) + meter.kickFor(actingSide(), t));
  }
  /** The sustained stack at this meter, with the player's Strength on it. */
  const susFor = (m) => sustainedFor(m, tuning, { cardLive: stack.cardLive, strength: dials.strength });

  /**
   * Is the acting side sitting on its own move with a live board in front of it?
   * Only then does the think clock run: not before a game is dealt, not behind
   * the menu or the end card (the referee idles at the start with white to
   * move), not on the opponent's move, not under the pause card, not while a
   * full-screen replay owns the view. The referee's turn() leads the clocks
   * online (an optimistic move), so it is asked first.
   */
  function thinking() {
    if (!seenTurn) return false;
    const side = actingSide();
    if (!localSides.includes(side)) return false;
    try {
      const pbp = globalThis.window?.PBP;
      if (pbp?.door?.isUp?.()) return false;
      const game = pbp?.game;
      if (game && typeof game.turn === 'function') {
        if (game.turn() !== side || (typeof game.isOver === 'function' && game.isOver())) return false;
      } else if (meter.active !== side) return false;
      if (pbp?.isPaused?.()) return false;
      const board = opts.board || pbp?.board;
      if (board?.director?.holding?.()) return false;
    } catch { return false; }
    return true;
  }

  /** The fast-transition class on both planes and the stage, for one snap. */
  function setSnapClass(on) {
    for (const el of [root, front, stage]) {
      try { if (el) el.classList.toggle('is-snap', on); } catch { /* gone */ }
    }
  }

  /** The move is made: the wash leaves in a blink, the haze and the sound drop now. */
  function snap() {
    stack.clearCard({ fast: true });
    if (!enabled) return;
    setSnapClass(true);
    if (snapTimer) clearTimeout(snapTimer);
    snapTimer = setTimeout(() => { snapTimer = 0; setSnapClass(false); }, tuning.think.snapMs);
    const t = now();
    const m = liveMeter(t);
    applySustained(susFor(m));
    lastBoardPush = t;
    pushToBoard(m);
  }

  /** THE BREATH: a move was made. Everything for us lets go until our turn card. */
  function rest() {
    think.reset();
    resting = true;
    restWake = 0;
    stack.rest(tuning.think.snapMs / 2);
    snap();
  }

  /** Our turn card is up (or never came): the climb starts again from nothing. */
  function wake() {
    if (!resting) return;
    resting = false;
    restWake = 0;
    think.reset();
    lastBeat = now();
  }

  /** On a long think the wash rises, and the next one follows the last. */
  function thinkCard(t, m, counting) {
    const live = stack.cardLive;
    if (cardWasLive && !live) nextCardAt = t + tuning.think.cardGapMs;   // a breath between washes
    cardWasLive = live;
    if (live || !counting || reducedMotion || t < nextCardAt || think.ms < think.fullMs * tuning.think.cardAt) return;
    stack.videoCard({ holdMs: videoHoldMs(m, tuning, hostHoldSec), side: actingSide() });
    // a pool with every picture already up declines: try again after a breath, not every beat
    nextCardAt = t + tuning.think.cardGapMs;
    cardWasLive = stack.cardLive;
  }

  /** THE FALL: a local loss brings everything up at once; the tick drains it and switches off. */
  function startSurge() {
    const t = now();
    surge = { at: t };
    resting = false;
    think.reset();
    stack.clearCard({ fast: true });
    stack.burst(schedule.burstFor('taker', 1));
    applySustained(sustainedFor(1, tuning, { cardLive: false, strength: dials.strength }));
    lastBoardPush = t;
    pushToBoard(1);
    schedulePump();
  }

  /** Hand the meter to A's board if it grew those knobs. Always guarded. */
  function pushToBoard(m) {
    try {
      // the board the caller handed us wins; window.PBP is the fallback for a
      // harness that never passed one
      const board = opts.board
        || (typeof window !== 'undefined' && window.PBP && window.PBP.board) || null;
      if (!board) return;
      if (typeof board.setWobble === 'function') board.setWobble(clamp01(m * tuning.wobbleScale));
      if (typeof board.setCameraSway === 'function') board.setCameraSway(clamp01(m * tuning.swayScale));
      if (typeof board.setMeter === 'function') board.setMeter(clamp01(m));
    } catch { /* A's board is optional and may change under us */ }
  }

  // The spiral PULSES: it shows for a hold and then hides itself, and the next
  // pass only starts on the next set() it is handed. Deduping it the way the
  // steady layers are deduped means a steady meter gets exactly one pass and
  // then a dead veil forever, so it is always forwarded and gates itself on its
  // own gapMs.
  const PULSING = new Set(['spiral']);

  function applySustained(sus) {
    for (const name of ['melt', 'blur', 'spiral', 'overlay']) {
      const spec = solo && solo !== name ? { on: false } : sus[name];
      const key = spec.on ? (spec.alpha != null ? spec.alpha : spec.px) : OFF;
      // retune only on a real change, so we are not writing style every 90ms
      if (!PULSING.has(name) && Math.abs(key - applied[name]) < 0.005) continue;   // NaN fails this, as it should
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
      // the fall has drained: the game is over, go quiet
      if (surge && t - surge.at >= tuning.surge.holdMs + tuning.surge.drainMs) { surge = null; setEnabled(false); return; }
      const dt = lastBeat ? t - lastBeat : 0;
      lastBeat = t;
      const live = !surge && thinking();
      // no turn card is coming (clock under 10 s, a resumed game): the breath ends on its own
      if (resting && live) { restWake += Math.min(dt, tuning.think.maxStepMs); if (restWake >= tuning.think.wakeAfterMs) wake(); }
      const counting = live && !resting;
      think.advance(dt, counting, thinkRate(meter.meterFor(actingSide()), tuning));
      const m = liveMeter(t);
      // pictures only pop while we are on our own live move (or in the fall)
      const out = schedule.tick(t, liveHeat(t), m, {
        cardLive: stack.cardLive, quiet: !surge && !counting,
        amount: amountRate(dials.amount), strength: dials.strength,
      });
      // reduced motion keeps the still layers (the melt, the blur, the veils)
      // and drops everything that pops, falls or rushes at the player
      if (!reducedMotion) for (const kind of out.fire) stack.oneshot(kind, { heat: out.heat });
      applySustained(out.sustained);
      stack.boardMask();
      thinkCard(t, m, counting);
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
    newgame() { resetMatch(); },
    local(p) {
      localSides = Array.isArray(p?.sides) ? p.sides.filter(s => s === 'w' || s === 'b') : ['w', 'b'];
      resetMatch();
    },
    // An online seat the lobby could only guess, corrected by the server's first
    // word (net/match.js): the climb belongs to the player's real side.
    seat(p) {
      const side = p && (p.color === 'w' || p.color === 'b') ? p.color : null;
      if (!side || localSides.length !== 1 || localSides[0] === side) return;
      localSides = [side];
      think.reset();
    },
    // The turn card (ui/turn-handoff.js): ours ends the breath.
    'turn-card'(p) {
      const side = p && (p.side === 'w' || p.side === 'b') ? p.side : null;
      if (side && localSides.includes(side)) wake();
    },
    clock(p) { meter.setClock(p); },
    turn(p) {
      const seeding = !seenTurn;
      seenTurn = true;
      meter.setTurn(p);
      // An online resync repeats the turn it already had (net/match.js
      // applyState): nobody moved, so the think goes on and nothing snaps.
      const key = p ? p.side + ':' + p.ply : '';
      if (!seeding && key === lastTurn) return;
      lastTurn = key;
      // A move was made, by anyone: THE BREATH (owner, 2026-10-02). Everything
      // for the player lets go now and stays quiet until their own turn card.
      // The board's seeding `turn` at start() is not a move, so it never rests.
      lastBeat = now();
      if (seeding) { think.reset(); return; }
      if (surge || meter.over) return;   // the fall owns the screen now
      rest();
    },
    // A capture still counts (it makes the next climb faster) but no longer
    // bursts: it lands on a move, and a move is the breath. The capture replay
    // owns that moment. A check no longer flashes for the same reason.
    capture(p) { meter.noteCapture(p, now()); },
    grab(p) { if (enabled) stack.grab(p); },
    dragmove(p) { if (enabled) stack.dragmove(p); },
    drop(p) { if (enabled) stack.drop(p); },
    gameover(p) {
      meter.over = true;
      // THE FALL (owner, 2026-10-01): a local loss is the climax, not a
      // failure. Hotseat counts too, since the side that lost sits at this
      // screen. A win or a draw goes quiet as before.
      const winner = p && (p.winner === 'w' || p.winner === 'b') ? p.winner : null;
      if (enabled && !reducedMotion && winner && localSides.includes(otherSide(winner))) { startSurge(); return; }
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
    enabled = !!on && !meter.over && presentation().experience === 'distraction';
    if (!enabled) {
      surge = null;
      stack.clear();
      for (const k of Object.keys(applied)) applied[k] = UNSET;
      pushToBoard(0);
    } else { lastBeat = 0; schedulePump(); }   // a pause never banks think time
    return enabled;
  }

  function resetMatch() {
    setEnabled(false);
    meter.reset();
    think.reset();
    nextCardAt = 0;
    cardWasLive = false;
    schedule.reset(now());
    // Continuing a saved game restores its accumulated pressure without replaying bursts.
    const game = globalThis.window?.PBP?.game;
    const history = game?.rules?.chess?.history({ verbose: true }) || [];
    for (const move of history) if (move.captured) meter.noteCapture({ by: move.color, victimSide: otherSide(move.color) }, now() - 60000);

    seenTurn = false;
    lastTurn = '';
    resting = false;
    restWake = 0;
    overrideMeter = null;
    lastTick = lastBoardPush = 0;
    setEnabled(true);
  }

  /** The player's Strength reaches the pictures as a CSS multiplier (ramp.css). */
  function paintStrength() {
    for (const el of [root, front]) {
      try { if (el) el.style.setProperty('--pbp-strength', String(dials.strength)); } catch { /* gone */ }
    }
  }

  const unsubPresentation = onPresentation(p => {
    const before = dials;
    dials = { amount: p.amount || 'normal', strength: Number.isFinite(p.strength) ? p.strength : 1, rampSpeed: p.rampSpeed || 'normal' };
    paintStrength();
    if (before.strength !== dials.strength) {
      for (const k of Object.keys(applied)) applied[k] = UNSET;
      if (enabled) applySustained(susFor(liveMeter()));
    }
    const wasReduced = reducedMotion;
    reducedMotion = p.reducedMotion;
    if (reducedMotion && !wasReduced) {
      stack.clear();
      for (const k of Object.keys(applied)) applied[k] = UNSET;
    }
    setEnabled(p.experience === 'distraction');
  });

  function dispose() {
    if (disposed) return;
    disposed = true;
    unsubPresentation();
    if (snapTimer) { clearTimeout(snapTimer); snapTimer = 0; setSnapClass(false); }
    if (rafId) { try { cancelAnimationFrame(rafId); } catch { clearTimeout(rafId); } rafId = 0; }
    if (unsubSettings) { try { unsubSettings(); } catch { /* gone */ } unsubSettings = null; }
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
      meter: liveMeter(t),
      heat: liveHeat(t),
      think: { ms: think.ms, lift: think.lift(), fullMs: think.fullMs, counting: thinking() && !resting },
      resting,
      dials: { ...dials },
      surge: surge ? { ms: t - surge.at } : null,
      override: overrideMeter,
      hostHoldSec,
      reducedMotion,
      model: meter.snapshot(t),
      layers: stack.debug ? stack.debug() : null,
      media: media.stats ? media.stats() : null,
      tuning,
      /** Dev harness only: pin the meter (null gives the game back control).
       *  Sustained layers are applied at once so a screenshot does not have to
       *  wait for the next rAF beat (headless barely gets any). */
      setMeter(v) {
        overrideMeter = v == null ? null : clamp01(v);
        applySustained(susFor(liveMeter()));
        stack.boardMask();
        return overrideMeter;
      },
      /** Dev harness only: fire one layer by name right now. */
      fire(kind, o) {
        if (kind === 'videoCard') stack.videoCard(o || { holdMs: videoHoldMs(liveMeter(), tuning, hostHoldSec) });
        else stack.oneshot(kind, o || { heat: liveHeat(now()) });
      },
      /** Dev harness only: isolate ONE sustained layer (null shows them all). */
      only(name) {
        solo = name || null;
        for (const k of Object.keys(applied)) applied[k] = UNSET;
        applySustained(susFor(liveMeter()));
        return solo;
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
