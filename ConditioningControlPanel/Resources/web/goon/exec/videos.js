/* ============================================================================
 * exec/videos.js — GoonElement.Videos (1) + GoonPayloadKind.Video (3).
 *
 * TWO SHAPES, ONE POOL. Both draw clips from the PLAYER'S OWN library
 * (exec/media.js — the opponent sends a tag reference, never a file), but they
 * are different objects on screen and always have been meant to be:
 *
 *   ELEMENT (the shared ramp's mandatory video) — FULLSCREEN on #gg-stage (z20,
 *   under the HUD, under mercy, over the screens). One clip chained after
 *   another for as long as the ramp keeps it up. Unchanged.
 *
 *   PAYLOAD (the opponent throws the VHS) — FLOATING MINI WINDOWS on
 *   #gg-fx-vwin (z30). Small, glitch-in, drifting, DRAGGABLE, wheel-resizable,
 *   and dismissed with a click. NEVER on #gg-stage: the stage is full-bleed and
 *   only click-through while :empty (ui/screens.css), so a husk left on it is a
 *   full-screen click shield — the exact bug clearForRecap exists to sweep up.
 *   A window on an fx layer cannot do that, and layers.stopAll() takes it with
 *   the rest of the tier.
 *
 * THE WINDOW, in the owner's words: "when they start they should have a random
 * glitch animation btw, also add some glowing borders with a red dot on the top
 * right (kinda like the live red light)". So: one of GLITCH_VARIANTS CRT-style
 * spawn-ins picked per window, a pink/violet glow ring, and a recording dot that
 * pulses while it plays.
 *
 * THREE ELEMENTS, THREE ANIMATIONS — and that is not styling, it is the
 * ANIMATION SHORTHAND TRAP: `animation` is a shorthand, so a one-shot and a loop
 * declared on ONE element cancel each other. The wrapper takes the drag offset
 * (inline transform, NO animation of its own WHILE IT LIVES, so the inline write
 * is authoritative); the drift loop rides .gg-vwin-drift; the one-shot glitch-in
 * rides .gg-vwin-inner; the pulse rides .gg-vwin-dot; the glow breathes on
 * .gg-vwin-inner::after. Nobody shares.
 *
 * AND IT LEAVES THE WAY IT ARRIVED. A window used to simply vanish; it now
 * glitches OUT (ggVwinOut, fx.css) on the way, which is where the wrapper's free
 * animation slot finally gets used — see THE GHOST below for why using it is
 * safe there and only there.
 *
 * PHYSICAL INTERACTION is exec/flashes.js's, ported rather than reinvented — it
 * already solved the three things that make this kind of node feel broken:
 *   · 6px of slop decides CLICK vs DRAG, and it is decided on pointerUP, so one
 *     press can still become either;
 *   · a grabbed window leaves the keyframe world (.is-grabbed kills the drift in
 *     fx.css) because animations outrank inline styles — a live drift keyframe
 *     would drag the node back to its anchor every frame. The drift offset is
 *     FOLDED into the drag offset first, so killing it moves nothing;
 *   · the z-lift is a re-append (fx.css rule 1: nothing here raises z-index),
 *     and a re-append RELEASES pointer capture — so we re-take it and swallow
 *     exactly one stale lostpointercapture.
 *
 * AUDIO: only the NEWEST window speaks, at volumeFor(intensity). When it goes,
 * the next-newest takes over. Four soundtracks at once is mush. Chromium's
 * autoplay policy can still refuse a play() with sound before the page has had a
 * gesture; that is HANDLED, not dropped — we retry muted, so the picture never
 * silently fails to appear. The one thing we never do is give up on the visual.
 *
 * AND IT FADES, both ways. The handover used to be a hard cut — a window was
 * unmuted at full volume the frame it appeared and silenced the frame it left —
 * which in a stack of four reads as an audio glitch rather than as a handover.
 * Now every change of the audible window is a RAMP on el.volume (500ms in,
 * 300ms out), and the four rules that make ramps behave are all in rampVolume():
 *   · .muted MUST NOT BE THE INSTRUMENT. A mute flip is a step function, so a
 *     rise unmutes FIRST and then ramps the volume up from wherever it was, and
 *     a fall ramps the volume to zero and only then flips .muted on. Ramping a
 *     muted element is ramping nothing.
 *   · ONE RAMP PER ELEMENT. Starting a new one cancels the old, so a window
 *     whose target changes mid-flight retargets from its CURRENT volume instead
 *     of two lerps fighting over one property.
 *   · HANDOVERS CROSSFADE. Both ramps are started in the same tick from
 *     applyAudio(), so the newcomer rises while the leaver falls; nothing waits
 *     its turn.
 *   · AN END YOU CAN SEE COMING IS FADED BEFORE IT ARRIVES. A clip that simply
 *     ENDS cannot be faded afterwards — there is nothing left to fade — so the
 *     ramp-out is armed ~300ms before the cap, and again (tighter) once
 *     loadedmetadata tells us the clip's real duration. An end nobody saw coming
 *     (a click, an eviction) fades on the way out instead, on the GHOST.
 *
 * WHO SPEAKS IS A CHOICE NOW, not a timestamp (2026-08-04, owner-specced). The
 * mic still starts on the newest window, but the player can move it and can shut
 * one up, and all three gestures are deliberately different:
 *
 *   LEFT CLICK (sub-slop) = MUTE TOGGLE for that window. It used to DISMISS,
 *   which meant the only thing a click could do to a window you merely wanted
 *   quiet was destroy it. Muting the audible window leaves the room SILENT: no
 *   auto-promotion, because promoting the next one is the room deciding you
 *   wanted a different soundtrack when what you said was "quiet".
 *
 *   HANDLING ONE IS A TOUCH — a right-click, a completed drag, or a wheel-resize
 *   — and the last window TOUCHED (or SPAWNED) is the one that speaks. The
 *   owner's rule, verbatim: "the last touched video is the active one for the
 *   sound (in general, the last one we moved, resized, etc)". A right-click on a
 *   click-muted window also UNMUTES it: an explicit "you, speak" beats a stale
 *   "shush". Moving or resizing does not — silencing a window and then dragging
 *   it out of the way is one intention, not two.
 *
 *   THE ✕, and only the ✕, dismisses — and only when the player asked for it (see
 *   SKIPPABLE below).
 *
 * The resolver is pure and lives in focusedIndexOf(): given the pool and the
 * focused id it answers WHICH INDEX speaks, or -1 for silence. audioTargets()
 * merely takes that index instead of assuming the last one, which is why the
 * newest-speaks pins still hold — newest IS the default answer.
 *
 * SKIPPABLE VIDEOS, DEFAULT OFF. A floating window runs its clip/60s course and
 * cannot be closed early; that is the point of being thrown one. A player who
 * wants an out turns "skippable videos" on in the options drawer and every window
 * grows a ✕ (top-LEFT, opposite the live dot). exec/ never imports ui/, so the
 * flag arrives the way heat and motion already do: as a ROOT ATTRIBUTE
 * (data-gg-vskip, written by ui/prefs.js), read lazily at the moment of the
 * click. fx.css hides the button outright when the attribute is off, so flipping
 * the toggle changes every window already on screen with no plumbing at all, and
 * the JS re-checks anyway — a button that is merely invisible must still refuse.
 *
 * MERCY IS NEVER GATED BY ANY OF THIS. "Unskippable" is a property of the WINDOW,
 * not of the way out: the payload's cancel fn and stopWindows() (mercy, recap,
 * detach) take everything down on the spot whatever the option says, whatever is
 * muted, whatever has focus. There is no code path from the option to teardown,
 * and that is deliberate.
 *
 * THE GHOST — the node outlives the record, and ONLY the node. When a window
 * settles, the record leaves `wins` FIRST and unconditionally: the slot is free,
 * the count has already fallen, the receipt is already posted, MAX_WINDOWS
 * arithmetic already agrees. What lingers for the ~300ms exit animation is an
 * orphan node with pointer-events off, owned by nobody, holding no slot. That
 * ordering is the whole design — a husk that still counted would be a phantom
 * window on the opponent's monitor, and a husk that still took clicks would be
 * the click shield this layer exists to prevent. It removes itself on
 * animationend, with a ~600ms timer behind it because animationend is not a
 * promise: a parked animation (heat "hot"), reduced motion, or a re-parented
 * node can all swallow it, and a leaked node must be impossible, not unlikely.
 * TEARDOWN NEVER GHOSTS: the payload's cancel fn and stopWindows() (mercy,
 * recap, detach) remove the node and kill its audio on the spot, because the
 * recap must inherit an empty layer, not four fading windows.
 *
 * RECEIPTS, mirrored from what the fullscreen payload did before:
 *   video ended · 60s cap · player ✕'d it · displaced by a 5th window
 *                                              -> done(true)  "survived", +1 charge
 *   executor cancel (mercy / stopAll) · nothing playable in the library
 *                                              -> done(false) "completed", no charge
 *
 * AND A THIRD DOOR INTO THE SAME POOL, spawnLocal(): a `video` bubble popped
 * (exec/bubbles.js), exec/executor.js heard it on the POP_EVENT seam, and the
 * player earned a window off their own field. It is the SAME object in every
 * visible way — but it has no wire behind it, so it settles SILENTLY (no
 * receipt, no done()) and it NEVER EVICTS: at MAX_WINDOWS it fizzles rather
 * than close a window the opponent made you sit through. Eviction remains a
 * PAYLOAD's privilege alone (a payload does evict a local window — the pool is
 * one pool). See spawnLocal at the bottom of this file.
 *
 * THE POOL IS ALSO A REPORT (2026-08-04). Both doors change one number — how many windows are
 * floating — and the opponent's monitor draws it, so every mutation publishes wins.length through
 * the optional onWindowCountChanged callback. exec/ never talks to core/ or ui/ directly: the
 * number goes UP to exec/executor.js, which hands it to core/match.js for the tick's `vwin`.
 * Nobody listening = nothing changes.
 *
 * Uniform renderer shape — see the banner in exec/flashes.js.
 * ==========================================================================*/

const MAX_SOURCE_TRIES = 3;   // a broken entry costs one redraw, not the run

/* --- the window dials. Exported because the self-test asserts against these
   numbers rather than re-typing them. ------------------------------------- */
export const MAX_WINDOWS = 4;          // live floating windows; a 5th evicts the oldest
export const WINDOW_CAP_MS = 60000;    // nothing floats longer than this, ever
export const DRAG_SLOP_PX = 6;         // <= this much travel and the press was a CLICK
export const WHEEL_STEP = 1.08;        // size multiplier per wheel notch
export const SIZE_MIN_FACTOR = 0.5;    // wheel clamp, relative to the window's base width
export const SIZE_MAX_FACTOR = 2.5;
export const GLITCH_VARIANTS = 4;      // .gg-vwin-in--1 .. --4 in fx.css
/* The bottom gutter MERCY owns. Same 96px ui/options.js clips off the drawer
   (MERCY_CLEARANCE_PX) — the guaranteed way out is never covered, by anything. */
export const MERCY_KEEPOUT_PX = 96;

/* --- the fades. Audio ramps are asymmetric on purpose: a window arriving may
   take its time, a window leaving must be out of the way of the next one. ---- */
export const AUDIO_FADE_IN_MS = 500;    // silence -> volumeFor(intensity)
export const AUDIO_FADE_OUT_MS = 300;   // volumeFor(intensity) -> silence, then .muted
export const AUDIO_RAMP_TICK_MS = 25;   // ~40 steps/s: inaudible stepping, no rAF needed

/* --- SKIPPABLE VIDEOS. The pref is ui/prefs.js's (`skippableVideos`, default
   false); it reaches this tier as a ROOT ATTRIBUTE, because exec/ does not import
   ui/ and a renderer built once at startup must still see a toggle flipped
   mid-match. fx.css keys the ✕'s visibility off the same attribute, so the two
   halves cannot disagree about what "on" looks like. ------------------------- */
export const SKIP_ATTR = 'data-gg-vskip';   // on <html>, written by ui/prefs.js
export const SKIP_ON = 'on';                // …anything else, or absent, is OFF
export const SKIP_BTN_CLASS = 'gg-vwin-x';
export const SKIP_BTN_MIN_PX = 22;          // the pointer target fx.css must meet
export const MUTED_CLASS = 'is-muted';      // this window was click-silenced
export const LIVE_CLASS = 'is-live';        // …and this one is the one speaking

/* --- the exit. The class fx.css hangs ggVwinOut off, how long that animation
   runs, and the timer that guarantees the node dies even if it never plays. --- */
export const OUT_CLASS = 'gg-vwin--out';
export const OUT_ANIM_MS = 300;         // must match ggVwinOut in fx.css
export const GHOST_REMOVE_MS = 600;     // the "animationend never came" backstop

const HOLD_WATCHDOG_MS = 30000;  // nothing may be "held" longer than this
const SETTLE_FADE_MS = 240;      // the opacity transition on .gg-vwin (fx.css)
const SPAWN_TRIES = 60;          // placement attempts before the safe-corner fallback

const clamp01 = (n) => (typeof n === 'number' && n === n ? (n < 0 ? 0 : n > 1 ? 1 : n) : 0);
const lerp = (a, b, t) => a + (b - a) * clamp01(t);
const num = (v, dflt) => (typeof v === 'number' && v === v ? v : dflt);
const soon = (fn, ms) => {
  const t = setTimeout(fn, Math.max(0, ms | 0));
  if (t && typeof t.unref === 'function') t.unref();
  return t;
};
/** setInterval's twin of soon(): unref'd, so a ramp can never hold a loop open. */
const every = (fn, ms) => {
  if (typeof setInterval !== 'function') return 0;
  const t = setInterval(fn, Math.max(1, ms | 0));
  if (t && typeof t.unref === 'function') t.unref();
  return t;
};
const nowMs = () => {
  try { if (typeof performance === 'object' && performance && typeof performance.now === 'function') return performance.now(); }
  catch (_e) { /* fall through */ }
  return Date.now();
};
const reducedMotion = () => {
  try { return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch (_e) { return false; }
};
const vpW = () => ((typeof window !== 'undefined' && window && window.innerWidth > 0) ? window.innerWidth : 1280);
const vpH = () => ((typeof window !== 'undefined' && window && window.innerHeight > 0) ? window.innerHeight : 720);

/** Cue intensity -> playback volume. Modest on purpose: the HUD must stay audible. */
export function volumeFor(intensity) {
  return +lerp(0.12, 0.55, clamp01(intensity)).toFixed(3);
}

/**
 * WHO SPEAKS, as a pure function over the pool — the resolver the whole audio
 * side now hangs off. Given the windows in spawn order and the id of the last one
 * TOUCHED or SPAWNED, it answers the index that may make a sound, or -1 for
 * silence. Four rules, and every one of them is a thing the player said:
 *
 *   · nothing up, nobody speaks;
 *   · the FOCUSED window speaks — that is what focus means;
 *   · a focus id that is no longer in the pool (it ended, it was evicted, it was
 *     ✕'d) falls back to the NEWEST remaining, which is exactly what this module
 *     did before focus existed and is why the newest-speaks pins still hold;
 *   · a window the player CLICK-MUTED does not speak even when it is the focused
 *     one, and nothing is promoted in its place. Muting the audible window means
 *     silence, not "play me the next one".
 *
 * @param {Array<{wid?:*, muted?:boolean}>} pool windows, oldest first
 * @param {*} [focusedId] the wid of the last window touched/spawned
 * @returns {number} index into pool, or -1
 */
export function focusedIndexOf(pool, focusedId) {
  const arr = Array.isArray(pool) ? pool : [];
  if (!arr.length) return -1;
  let idx = -1;
  if (focusedId !== undefined && focusedId !== null) {
    for (let i = 0; i < arr.length; i++) {
      if (arr[i] && arr[i].wid === focusedId) { idx = i; break; }
    }
  }
  if (idx < 0) idx = arr.length - 1;              // the newest, as it always was
  return (arr[idx] && arr[idx].muted) ? -1 : idx;
}

/**
 * THE AUDIO INVARIANT, as a pure function: given `count` windows in spawn order,
 * exactly ONE is unmuted — by default the newest (last) one, and only when there
 * is one at all. Kept separate from the DOM so it can be asserted without a
 * browser.
 * @param {number} count
 * @param {number} [focusIndex] who speaks; defaults to the newest, -1 = silence
 */
export function unmutedFlags(count, focusIndex) {
  const n = Math.max(0, count | 0);
  const f = (focusIndex === undefined || focusIndex === null) ? n - 1 : (focusIndex | 0);
  const out = new Array(n);
  for (let i = 0; i < n; i++) out[i] = (i === f);
  return out;
}

/**
 * THE SAME INVARIANT AS A VOLUME, which is what the ramps actually chase: given
 * the pool's intensities in spawn order, the volume every window should be
 * HEADING TOWARD. At most one non-zero, and it is volumeFor(intensity) of the
 * window that has the mic — never above it, so the ceiling stays wherever
 * volumeFor put it. A focusIndex of -1 is a room the player silenced: all zeroes.
 * @param {number[]} intensities
 * @param {number} [focusIndex] defaults to the newest window
 * @returns {number[]}
 */
export function audioTargets(intensities, focusIndex) {
  const arr = Array.isArray(intensities) ? intensities : [];
  const flags = unmutedFlags(arr.length, focusIndex);
  const out = new Array(arr.length);
  for (let i = 0; i < arr.length; i++) out[i] = flags[i] ? volumeFor(arr[i]) : 0;
  return out;
}

/**
 * How long a ramp between two volumes takes. Rising is the generous one (the
 * window is arriving and has nothing to get out of the way of); falling is the
 * short one (something else is usually rising into the gap). Going nowhere takes
 * no time at all, which is what keeps a re-applied target from restarting a fade.
 */
export function fadeMsFor(from, to) {
  const a = clamp01(from), b = clamp01(to);
  if (Math.abs(b - a) < 1e-6) return 0;
  return b > a ? AUDIO_FADE_IN_MS : AUDIO_FADE_OUT_MS;
}

/**
 * ONE STEP OF THE LERP, pure, so the fade curve is assertable without an audio
 * element (or a browser). Clamped at both ends: a ramp that overruns its
 * duration lands exactly on its target and never past it.
 */
export function rampAt(from, to, elapsedMs, durMs) {
  const a = clamp01(from), b = clamp01(to);
  const d = num(durMs, 0);
  if (!(d > 0)) return b;
  const t = clamp01(num(elapsedMs, 0) / d);
  return +(a + (b - a) * t).toFixed(4);
}

/**
 * How long ONE window may float: never past WINDOW_CAP_MS, never past what the
 * payload asked for, never under a second. The clip ending beats all of it.
 */
export function windowCapMs(durationMs) {
  return Math.min(WINDOW_CAP_MS, Math.max(1000, (durationMs | 0) || 30000));
}

/** A window's born width in px — phone-screen-ish, and never more than a slab. */
export function baseWindowWidth(viewportW) {
  const w = num(viewportW, 0) > 0 ? viewportW : 1280;
  return Math.round(Math.min(400, Math.max(220, w * 0.22)));
}

/**
 * The two rectangles a window may not be BORN on top of (it may drift, slowly,
 * anywhere — the drift is biased upward and away from the gutter, see
 * driftVars()):
 *   mercy    the bottom-centre gutter. Never covered, at any heat, in any phase.
 *   monitor  the opponent's CRT column, top-right (ui/hud.css .gg-rightcol is
 *            --gg-mon-w = clamp(210px, 22vw, 280px) wide).
 */
export function keepOutRects(viewportW, viewportH) {
  const W = num(viewportW, 0) > 0 ? viewportW : 1280;
  const H = num(viewportH, 0) > 0 ? viewportH : 720;
  const monW = Math.min(280, Math.max(210, W * 0.22)) + 48;   // + the column's gap/margin
  return {
    mercy: { x0: W * 0.28, y0: Math.max(0, H - MERCY_KEEPOUT_PX), x1: W * 0.72, y1: H },
    monitor: { x0: Math.max(0, W - monW), y0: 0, x1: W, y1: H * 0.72 },
  };
}

const hits = (x, y, w, h, r) => !(x + w <= r.x0 || x >= r.x1 || y + h <= r.y0 || y >= r.y1);

/**
 * Where a new window lands: random, but off the mercy gutter and off the
 * opponent's monitor. Rejection sampling with a deterministic fallback (the
 * top-left corner clears both keep-outs by construction) — a window that could
 * not be placed still has to appear.
 * @param {(n?:number)=>number} [rnd] injectable RNG so the self-test can sweep it
 */
export function placeWindow(viewportW, viewportH, w, h, rnd) {
  const R = typeof rnd === 'function' ? rnd : Math.random;
  const W = num(viewportW, 0) > 0 ? viewportW : 1280;
  const H = num(viewportH, 0) > 0 ? viewportH : 720;
  const ww = Math.min(num(w, 320), W * 0.9);
  const hh = Math.min(num(h, 180), H * 0.9);
  const pad = 8;
  const spanX = Math.max(0, W - ww - pad * 2);
  const spanY = Math.max(0, H - hh - pad * 2);
  const zones = keepOutRects(W, H);
  for (let i = 0; i < SPAWN_TRIES; i++) {
    const x = pad + R() * spanX;
    const y = pad + R() * spanY;
    if (hits(x, y, ww, hh, zones.mercy) || hits(x, y, ww, hh, zones.monitor)) continue;
    return { x: Math.round(x), y: Math.round(y) };
  }
  return { x: pad, y: pad };
}

/** Drift dials for one window. dy is biased UP: the gutter is downstairs. */
function driftVars(R) {
  const rnd = typeof R === 'function' ? R : Math.random;
  const sign = rnd() < 0.5 ? -1 : 1;
  return {
    dx: Math.round(sign * (18 + rnd() * 28)),
    dy: -Math.round(14 + rnd() * 26),
    durS: +(18 + rnd() * 16).toFixed(1),
  };
}

/** Which spawn-in plays. 1..GLITCH_VARIANTS so four spawns do not look cloned. */
export function glitchVariant(rnd) {
  const R = typeof rnd === 'function' ? rnd : Math.random;
  return 1 + Math.min(GLITCH_VARIANTS - 1, Math.floor(R() * GLITCH_VARIANTS));
}

/* ===========================================================================
 * THE VOLUME RAMP. Module-level, and keyed on the ELEMENT rather than on the
 * pool record, because a settling window hands its <video> to a ghost that has
 * no record any more and still owes the room a 300ms fade-out.
 * ========================================================================= */
const RAMP = '__ggVolRamp';

/** Stop whatever this element was doing to its own volume. Idempotent. */
function cancelRamp(v) {
  if (!v || !v[RAMP]) return;
  try { clearInterval(v[RAMP].timer); } catch (_e) { /* ignore */ }
  v[RAMP] = null;
}

const setVol = (v, x) => { try { v.volume = clamp01(x); } catch (_e) { /* ignore */ } };

/** Is this element already on its way to `to`? Re-targeting the same place must
 *  be a no-op, or every applyAudio() would restart the fade it is watching. */
const rampingTo = (v, to) => !!(v && v[RAMP] && Math.abs(v[RAMP].to - to) < 1e-6);

/**
 * Ramp one media element's volume to `to`. See the AUDIO banner for the four
 * rules; this is where all four live.
 * @param {object} v the media element
 * @param {number} to target volume, 0..1
 * @param {{ms?:number, onEnd?:()=>void}} [opts] ms overrides fadeMsFor()
 */
function rampVolume(v, to, opts) {
  if (!v) return;
  const o = opts || {};
  const target = clamp01(to);
  const from = clamp01(num(v.volume, 0));
  cancelRamp(v);                                    // one ramp per element, ever
  const ms = Math.max(0, num(o.ms, fadeMsFor(from, target)));
  // A rise unmutes FIRST: ramping the volume of a muted element ramps nothing.
  // (If the autoplay policy re-mutes us a tick later, that is play()'s business
  // and the picture still survives — see play().)
  if (target > 0) { try { v.muted = false; } catch (_e) { /* ignore */ } }
  const land = () => {
    setVol(v, target);
    // …and a fall flips .muted only once there is nothing left to hear.
    if (target <= 0) { try { v.muted = true; } catch (_e) { /* ignore */ } }
    v[RAMP] = null;
    if (typeof o.onEnd === 'function') { try { o.onEnd(); } catch (_e) { /* ignore */ } }
  };
  if (!(ms > 0)) { land(); return; }
  const t0 = nowMs();
  const state = { to: target, from, ms, timer: 0 };
  state.timer = every(() => {
    const el = nowMs() - t0;
    if (el >= ms) { try { clearInterval(state.timer); } catch (_e) { /* ignore */ } land(); return; }
    setVol(v, rampAt(from, target, el, ms));
  }, AUDIO_RAMP_TICK_MS);
  if (!state.timer) { land(); return; }              // a host without setInterval
  v[RAMP] = state;
  setVol(v, rampAt(from, target, 0, ms));            // step 0 now: no first-frame jump
}

/** translate() out of a computed matrix, so folding the drift needs no library. */
function matrixXY(str) {
  const s = String(str || '');
  if (!s || s === 'none') return null;
  const m = /matrix\(\s*([-\d.eE]+)\s*,\s*([-\d.eE]+)\s*,\s*([-\d.eE]+)\s*,\s*([-\d.eE]+)\s*,\s*([-\d.eE]+)\s*,\s*([-\d.eE]+)\s*\)/.exec(s);
  if (m) return { x: parseFloat(m[5]) || 0, y: parseFloat(m[6]) || 0 };
  const m3 = /matrix3d\(([^)]+)\)/.exec(s);
  if (m3) {
    const p = m3[1].split(',').map((v) => parseFloat(v) || 0);
    if (p.length >= 14) return { x: p[12], y: p[13] };
  }
  return null;
}

/**
 * @param {object} [o]
 * @param {(n:number)=>void} [o.onWindowCountChanged] fired with wins.length whenever it CHANGES —
 *        a window opened, settled, was evicted or was swept. exec/executor.js forwards it to
 *        core/match.js, which puts it on the state tick as `vwin` so the opponent's monitor can
 *        draw the stack. Renderers do not import one another and know nothing about the wire; this
 *        is a number handed upward, and nothing here changes if nobody listens.
 */
export function createVideos({ layers, media, audio, logger, onWindowCountChanged } = {}) {
  const sfx = (id) => { try { if (audio && typeof audio.sfx === 'function') audio.sfx(id); } catch (_e) { /* stub */ } };
  const log = logger || null;
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:videos] ${m}`); };
  const info = (m) => { if (log && log.info) log.info(`[gg:videos] ${m}`); };
  const calm = reducedMotion();

  let sustained = null;               // the chained element run (fullscreen, stage)
  const runs = new Set();             // every live element run
  const wins = [];                    // floating windows, OLDEST FIRST
  let drag = null;                    // the ONE live press, if any
  let focusedId = null;               // wid of the last window TOUCHED or SPAWNED
  let widSeq = 0;                     // window ids: unique, thrown or earned

  const countSink = typeof onWindowCountChanged === 'function' ? onWindowCountChanged : null;
  let lastCount = 0;                  // what the sink was last told; edges only

  /**
   * Publish wins.length upward, once per actual change. Called after EVERY mutation of the pool —
   * the eviction inside openWindow is a settle followed by a push, so the sink sees 4 -> 3 -> 4
   * rather than a stuck number, and a listener that only redraws on change costs nothing.
   * A throwing listener is a listener's problem: it must never take a window down with it.
   */
  function publishCount() {
    if (!countSink) return;
    const n = wins.length;
    if (n === lastCount) return;
    lastCount = n;
    try { countSink(n); } catch (e) { warn(`onWindowCountChanged threw: ${e && e.message}`); }
  }

  const stage = () => (layers && typeof layers.get === 'function' ? layers.get('stage') : null);
  const winLayer = () => (layers && typeof layers.get === 'function' ? layers.get('vwin') : null);

  /**
   * Is "skippable videos" on RIGHT NOW? Read lazily, every time, off the root
   * attribute ui/prefs.js maintains — never cached, because the whole point is
   * that a player who turns it on mid-match can close the window already floating
   * in front of them. A page with no such attribute (a bare test host, a
   * pref store that never loaded) is OFF, which is the default anyway.
   */
  function skippable() {
    try {
      if (typeof document === 'undefined' || !document || !document.documentElement) return false;
      return document.documentElement.getAttribute(SKIP_ATTR) === SKIP_ON;
    } catch (_e) { return false; }
  }

  /* =========================================================================
   * THE ELEMENT RUN — fullscreen on #gg-stage. This is the ramp's mandatory
   * video and it is deliberately the boring one: mount, chain, tear down.
   * ======================================================================= */
  function createRun({ intensity, onDone }) {
    const run = {
      alive: true, intensity: clamp01(intensity),
      wrap: null, video: null, handle: null, tries: 0, finished: false,
    };
    runs.add(run);

    const settle = (endured) => {
      if (run.finished) return;
      run.finished = true;
      run.alive = false;
      teardown();
      runs.delete(run);
      if (typeof onDone === 'function') { try { onDone(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
    };

    function releaseSource() {
      try { if (run.handle && run.handle.release) run.handle.release(); } catch (_e) { /* ignore */ }
      run.handle = null;
    }

    function teardown() {
      const v = run.video;
      if (v) {
        try { v.pause(); } catch (_e) { /* ignore */ }
        try { v.removeAttribute('src'); v.load(); } catch (_e) { /* ignore */ }
        v.onended = null; v.onerror = null;
      }
      releaseSource();
      const w = run.wrap;
      if (w) {
        w.classList.remove('is-on');
        soon(() => { try { w.remove(); } catch (_e) { /* ignore */ } }, 280);
      }
      run.wrap = null;
      run.video = null;
    }

    function mount() {
      if (typeof document === 'undefined') return false;
      const host = stage();
      if (!host) return false;
      const wrap = document.createElement('div');
      wrap.className = 'gg-vid-wrap';
      const video = document.createElement('video');
      video.className = 'gg-vid';
      video.playsInline = true;
      video.autoplay = true;
      video.controls = false;
      video.preload = 'auto';
      video.volume = volumeFor(run.intensity);
      wrap.appendChild(video);
      host.appendChild(wrap);
      soon(() => wrap.classList.add('is-on'), 16);
      run.wrap = wrap;
      run.video = video;
      return true;
    }

    /** Draw the next clip. Returns false when the pool has nothing playable. */
    function next() {
      if (!run.alive) return false;
      if (!run.video && !mount()) return false;
      if (!media || typeof media.drawKind !== 'function') return false;

      const entry = media.drawKind('video');
      if (!entry) return false;
      const handle = (typeof media.acquire === 'function') ? media.acquire(entry) : null;
      if (!handle || !handle.url) return false;

      releaseSource();
      run.handle = handle;

      const v = run.video;
      v.loop = false;               // the element CHAINS: one clip after another
      v.onerror = () => {
        run.tries++;
        if (run.tries >= MAX_SOURCE_TRIES) { settle(false); return; }
        warn(`source failed (${run.tries}/${MAX_SOURCE_TRIES}), redrawing`);
        if (!next()) settle(false);
      };
      v.onended = () => { if (run.alive && !next()) settle(false); };
      try { v.src = handle.url; } catch (_e) { return false; }
      play(v);
      return true;
    }

    run.settle = settle;
    /**
     * The pool has no playable video. That is the RECEIVER'S library gap, not a
     * failure of the sender's payload, so the render is a silent no-op.
     */
    run.begin = () => {
      if (!next()) { settle(false); return false; }
      return true;
    };
    run.retune = (v) => {
      run.intensity = clamp01(v);
      if (run.video) { try { run.video.volume = volumeFor(run.intensity); } catch (_e) { /* ignore */ } }
    };
    return run;
  }

  /** play(), with the autoplay-policy fallback: keep the PICTURE, drop the sound. */
  function play(v) {
    if (!v || typeof v.play !== 'function') return;
    let p = null;
    try { p = v.play(); } catch (_e) { return; }
    if (p && typeof p.catch === 'function') {
      p.catch(() => {
        try { v.muted = true; const q = v.play(); if (q && q.catch) q.catch(() => { /* give up quietly */ }); }
        catch (_e) { /* ignore */ }
        info('autoplay refused with sound — playing muted');
      });
    }
  }

  /* =========================================================================
   * THE FLOATING WINDOWS — the payload. Everything below this line lives on
   * #gg-fx-vwin and never touches #gg-stage.
   * ======================================================================= */

  /**
   * ONE window speaks — the focused one, by default the newest — but it RAMPS
   * there. Re-applied on every spawn, every settle, every touch and every mute,
   * and it is the crossfade: the leaver's fall and the newcomer's rise are both
   * started here, in one tick, so neither waits. Re-targeting a ramp that is
   * already heading to the same place is skipped, or a fade would restart every
   * time the pool so much as twitched.
   *
   * It is also where the two STATE CLASSES are painted, for the same reason they
   * are one function: what the player hears and what the player can see about
   * what they are hearing must never be computed twice.
   */
  function applyAudio() {
    const idx = focusedIndexOf(wins, focusedId);
    const targets = audioTargets(wins.map((w) => w.intensity), idx);
    for (let i = 0; i < wins.length; i++) {
      const rec = wins[i];
      mark(rec, LIVE_CLASS, i === idx);
      mark(rec, MUTED_CLASS, !!rec.muted);
      const v = rec.video;
      if (!v || rampingTo(v, targets[i])) continue;
      rampVolume(v, targets[i]);
    }
  }

  const mark = (rec, cls, on) => {
    try { if (rec && rec.node) rec.node.classList.toggle(cls, !!on); } catch (_e) { /* ignore */ }
  };

  /**
   * A TOUCH: this window now holds the mic. Right-click, a completed drag and a
   * wheel-resize are the three touches — anything the player did ON PURPOSE to
   * one window. The ✕ is not one (that window is leaving), and neither is a
   * pointer merely passing over.
   *
   * `unmute` is the right-click's extra: an explicit "you, speak" outranks a
   * stale click-mute, which is the only way back out of a muted focus. A drag
   * does NOT carry it — moving a window you silenced is not asking it to talk.
   */
  function focusWindow(rec, unmute) {
    if (!rec || rec.finished) return;
    if (unmute) rec.muted = false;
    focusedId = rec.wid;
    applyAudio();
  }

  /**
   * A sub-slop LEFT CLICK: quiet, or not quiet. It used to dismiss, which is now
   * the ✕'s job alone — a click is the cheap gesture and must not be the
   * destructive one. Muting the window that happens to be speaking leaves the
   * room silent on purpose: see focusedIndexOf.
   */
  function toggleMute(rec) {
    if (!rec || rec.finished) return;
    rec.muted = !rec.muted;
    sfx('ui-select');
    applyAudio();
  }

  /**
   * Arm the ramp-out for an end we can SEE COMING (the cap; the clip's own
   * duration once loadedmetadata reports it). Replaces any previously armed one,
   * because the second answer is always the tighter one.
   */
  function armFade(rec, inMs) {
    if (!rec || rec.finished) return;
    try { clearTimeout(rec.fadeTimer); } catch (_e) { /* ignore */ }
    rec.fadeTimer = soon(() => preFade(rec), Math.max(0, inMs));
  }

  /** The armed fade itself. A window that was not the one speaking has nothing
   *  to fade, and must not be hauled out of its own silence to prove it. */
  function preFade(rec) {
    if (!rec || rec.finished || !rec.video) return;
    const v = rec.video;
    if (v.muted || !(num(v.volume, 0) > 0)) return;
    rampVolume(v, 0, { ms: AUDIO_FADE_OUT_MS });
  }

  /** The one place a window's transform is written: drag offset in screen px. */
  function paint(rec) {
    if (!rec || !rec.node) return;
    try { rec.node.style.setProperty('transform', `translate(${rec.dx.toFixed(1)}px, ${rec.dy.toFixed(1)}px)`); }
    catch (_e) { /* ignore */ }
  }

  function drop(rec) {
    const i = wins.indexOf(rec);
    if (i >= 0) wins.splice(i, 1);
  }

  /* ------------------------------------------------------------- the ghosts
   * Nodes that have already left the pool and are playing out their exit. They
   * are tracked only so a teardown can be sure of them: nothing else in this
   * module ever reads the set, because a ghost is by definition nobody's.
   * ---------------------------------------------------------------------- */
  const ghosts = new Set();

  /** Kill one ghost NOW: its audio, its source, its handle, its node. Idempotent. */
  function closeGhost(g) {
    if (!g || g.closed) return;
    g.closed = true;
    ghosts.delete(g);
    try { clearTimeout(g.timer); } catch (_e) { /* ignore */ }
    const v = g.video;
    if (v) {
      cancelRamp(v);
      try { v.pause(); } catch (_e) { /* ignore */ }
      try { v.removeAttribute('src'); v.load(); } catch (_e) { /* ignore */ }
      v.onended = null; v.onerror = null; v.onloadedmetadata = null;
    }
    try { if (g.handle && g.handle.release) g.handle.release(); } catch (_e) { /* ignore */ }
    g.video = null;
    g.handle = null;
    const node = g.node;
    g.node = null;
    if (node) { try { node.remove(); } catch (_e) { /* ignore */ } }
  }

  /**
   * Let one settled window play out its exit. THE RECORD IS ALREADY GONE from
   * `wins` by the time we are called — see settleWindow — so everything here is
   * cosmetic and nothing here can hold a slot, a count or a click.
   *
   * The removal is belt AND braces: animationend when the exit really plays, and
   * a timer that does not care whether it did. Under reduced motion there IS no
   * exit animation (fx.css switches it off), so the calm path is the plain
   * opacity fade the base rule already transitions, on the shorter clock.
   */
  function lingerGhost(g) {
    const node = g.node;
    if (!node) { closeGhost(g); return; }
    ghosts.add(g);
    // The fade-out an unforeseeable end (a click, an eviction) never got to arm.
    if (g.video) rampVolume(g.video, 0, { ms: AUDIO_FADE_OUT_MS });
    try { node.classList.remove('is-on'); } catch (_e) { /* ignore */ }
    // fx.css does this too; inline so it is true even before the class lands.
    try { node.style.setProperty('pointer-events', 'none'); } catch (_e) { /* ignore */ }
    if (!calm) {
      try {
        node.classList.add(OUT_CLASS);
        node.addEventListener('animationend', (e) => {
          // The glitch-IN on .gg-vwin-inner bubbles up through here as well —
          // a window dismissed inside its own first 700ms would otherwise take
          // its spawn-in's animationend as the exit's.
          if (e && e.target && e.target !== node) return;
          closeGhost(g);
        });
      } catch (_e) { /* no listener: the timer below is the whole safety net */ }
    }
    g.timer = soon(() => closeGhost(g),
      calm ? Math.max(SETTLE_FADE_MS, AUDIO_FADE_OUT_MS) : GHOST_REMOVE_MS);
  }

  /** Every ghost, gone, now. Teardown only — the recap inherits an empty layer. */
  function purgeGhosts() {
    for (const g of Array.from(ghosts)) closeGhost(g);
  }

  /**
   * ONE window leaves.
   *
   * THE ORDER IS THE CONTRACT. The record is out of `wins` before anything
   * visual happens, so the freed slot, the published count, the MAX_WINDOWS
   * arithmetic and the receipt are all exactly as immediate as they were when a
   * window simply vanished. Only the NODE lingers, and it lingers as a ghost:
   * detached from the pool, pointer-events off, owned by nobody.
   *
   * @param {boolean} endured the receipt
   * @param {boolean} [immediate] TEARDOWN (the payload's cancel fn, stopWindows):
   *        no ghost, no exit, no fade — the node and its audio go on the spot.
   */
  function settleWindow(rec, endured, immediate) {
    if (!rec || rec.finished) return;
    rec.finished = true;
    if (!immediate) sfx('video-window-out');   // teardown (mercy/recap) settles silently
    try { clearTimeout(rec.endTimer); } catch (_e) { /* ignore */ }
    try { clearTimeout(rec.fadeTimer); } catch (_e) { /* ignore */ }
    if (drag && drag.rec === rec) forgetDrag();
    drop(rec);                       // ← THE POOL DEPARTURE, and it is first

    const g = { node: rec.node, video: rec.video, handle: rec.handle, timer: 0, closed: false };
    rec.node = null;
    rec.video = null;
    rec.handle = null;
    if (immediate) closeGhost(g); else lingerGhost(g);

    applyAudio();
    publishCount();
    if (typeof rec.onDone === 'function') {
      try { rec.onDone(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); }
    }
  }

  /* ------------------------------------------------------------ the press */

  const ptX = (e) => num(e && e.clientX, 0);
  const ptY = (e) => num(e && e.clientY, 0);
  const idMatch = (d, e) => !(d.pointerId != null && e && e.pointerId != null && e.pointerId !== d.pointerId);

  function listen(d, on) {
    const n = d.rec && d.rec.node;
    if (!n || typeof n.addEventListener !== 'function') return;
    const io = on ? 'addEventListener' : 'removeEventListener';
    try {
      n[io]('pointermove', onPointerMove);
      n[io]('pointerup', onPointerUp);
      n[io]('pointercancel', onPointerCancel);
      n[io]('lostpointercapture', onPointerCancel);
    } catch (_e) { /* ignore */ }
  }

  /** Tear the bookkeeping down without touching the window. */
  function forgetDrag() {
    if (!drag) return;
    listen(drag, false);
    try { clearTimeout(drag.watchdog); } catch (_e) { /* ignore */ }
    drag = null;
  }

  function onPointerDown(rec, e) {
    // THE BUTTON TEST COMES FIRST, and that ordering is load-bearing: a right
    // press is the "you, speak" gesture (onContextMenu below), and swallowing its
    // default here is how a browser gets talked out of raising contextmenu at
    // all. A right press on a window is therefore left completely alone.
    if (e && e.button != null && e.button !== 0) return;
    if (e && typeof e.preventDefault === 'function') e.preventDefault();
    if (e && typeof e.stopPropagation === 'function') e.stopPropagation();
    if (rec.finished || !rec.node || !rec.node.isConnected) return;

    if (drag) {
      // A second finger is IGNORED — but a drag whose node was torn out from
      // under us must never block the field forever.
      if (drag.rec && drag.rec.node && drag.rec.node.isConnected) return;
      forgetDrag();
    }

    const x = ptX(e), y = ptY(e);
    drag = {
      rec,
      pointerId: (e && e.pointerId != null) ? e.pointerId : null,
      x0: x, y0: y, baseDx: rec.dx, baseDy: rec.dy,
      grabbed: false, watchdog: 0, staleCapture: false,
    };
    listen(drag, true);
    drag.watchdog = soon(() => { if (drag) endDrag('watchdog'); }, HOLD_WATCHDOG_MS);
    try {
      if (typeof rec.node.setPointerCapture === 'function' && e && e.pointerId != null) {
        rec.node.setPointerCapture(e.pointerId);
      }
    } catch (_e) { /* capture is a nicety; the node listeners still work without it */ }
  }

  /**
   * Past the slop: the window LEAVES the keyframe world. The live drift offset
   * is folded into the drag offset FIRST, so `.is-grabbed`'s `animation: none`
   * (fx.css) freezes it exactly where it was instead of snapping it home.
   */
  function beginGrab(d) {
    const rec = d.rec;
    d.grabbed = true;
    rec.held = true;
    const fold = readDrift(rec);
    if (fold) {
      rec.dx += fold.x; rec.dy += fold.y;
      d.baseDx += fold.x; d.baseDy += fold.y;
    }
    try { rec.node.classList.add('is-grabbed'); } catch (_e) { /* ignore */ }
    lift(d);
    paint(rec);
  }

  /** The drift element's CURRENT translate, or null on a host without layout. */
  function readDrift(rec) {
    if (!rec || !rec.drift) return null;
    try {
      if (typeof getComputedStyle !== 'function') return null;
      return matrixXY(getComputedStyle(rec.drift).transform);
    } catch (_e) { return null; }
  }

  /**
   * Float the grabbed window over its siblings by DOM ORDER — fx.css rule 1
   * forbids raising z-index in this tier, and last-painted wins inside one layer.
   * THE CATCH (flashes.js's, verbatim): re-inserting a node counts as a removal,
   * and a removal implicitly RELEASES pointer capture. So we re-take it in the
   * same breath and swallow exactly ONE stale release notice.
   */
  function lift(d) {
    const rec = d.rec;
    try {
      const host = rec.node.parentNode;
      const kids = host && host.childNodes;
      if (!host || typeof host.appendChild !== 'function') return;
      if (kids && kids.length && kids[kids.length - 1] === rec.node) return;
      host.appendChild(rec.node);
      if (d.pointerId != null && typeof rec.node.setPointerCapture === 'function') {
        d.staleCapture = true;
        rec.node.setPointerCapture(d.pointerId);
      }
    } catch (_e) { /* ignore */ }
  }

  function onPointerMove(e) {
    const d = drag;
    if (!d || !idMatch(d, e)) return;
    // A stale release notice is dispatched BEFORE the next pointer event, so a
    // move means lift()'s re-capture went through and the licence has expired.
    d.staleCapture = false;
    const x = ptX(e), y = ptY(e);
    if (!d.grabbed) {
      if (Math.hypot(x - d.x0, y - d.y0) <= DRAG_SLOP_PX) return;   // still a click
      beginGrab(d);
    }
    if (e && typeof e.preventDefault === 'function') e.preventDefault();
    d.rec.dx = d.baseDx + (x - d.x0);
    d.rec.dy = d.baseDy + (y - d.y0);
    paint(d.rec);
  }

  /**
   * THE ONE EXIT. `why` is 'up' (released), 'cancel', 'stop' or 'watchdog'.
   * Only 'up' is a gesture the player completed; every other reason is a gentle
   * drop that changes nothing, which is what "no stuck held windows" means.
   */
  function endDrag(why) {
    const d = drag;
    if (!d) return;
    drag = null;
    listen(d, false);
    try { clearTimeout(d.watchdog); } catch (_e) { /* ignore */ }

    const rec = d.rec;
    if (!rec || !rec.node) return;

    if (!d.grabbed) {
      // Never passed the slop: this press was a CLICK, and a click MUTES. It
      // used to dismiss — a window is closed with its ✕ now, and only when the
      // player asked for one (see the SKIPPABLE banner).
      if (why === 'up' && rec.node.isConnected) toggleMute(rec);
      return;
    }

    rec.held = false;
    try { rec.node.classList.remove('is-grabbed'); } catch (_e) { /* ignore */ }
    // The drift restarts from wherever it was dropped (its offset is already in
    // rec.dx/dy), so letting go moves nothing either.
    paint(rec);
    // A completed drag is a TOUCH: you handled this window, so it is the one you
    // are listening to. It does not un-mute — see focusWindow.
    if (why === 'up') focusWindow(rec, false);
  }

  /**
   * RIGHT CLICK: "you, speak." The page's own menu is refused (a context menu
   * over a duel is nobody's idea of a gesture) and the window takes the mic,
   * un-muting itself if the player had clicked it quiet earlier.
   */
  function onContextMenu(rec, e) {
    if (e && typeof e.preventDefault === 'function') e.preventDefault();
    if (e && typeof e.stopPropagation === 'function') e.stopPropagation();
    if (!rec || rec.finished || !rec.node) return;
    focusWindow(rec, true);
  }

  function onPointerUp(e) {
    const d = drag;
    if (!d || !idMatch(d, e)) return;
    if (e && typeof e.preventDefault === 'function') e.preventDefault();
    endDrag('up');
  }

  /** pointercancel / lostpointercapture: the hand is empty and the window stays
   *  exactly where it was, un-dismissed. The one exception is the release lift()
   *  causes on purpose — paperwork, swallowed once. */
  function onPointerCancel(e) {
    const d = drag;
    if (!d || !idMatch(d, e)) return;
    if (d.staleCapture && e && e.type === 'lostpointercapture') {
      d.staleCapture = false;
      const n = d.rec && d.rec.node;
      try { if (n && typeof n.hasPointerCapture === 'function' && n.hasPointerCapture(d.pointerId)) return; }
      catch (_e) { /* fall through and try to re-take it */ }
      try { if (n && d.pointerId != null && typeof n.setPointerCapture === 'function') { n.setPointerCapture(d.pointerId); return; } }
      catch (_e) { /* genuinely gone: drop it */ }
    }
    endDrag('cancel');
  }

  /**
   * Wheel over a window resizes it through --gg-vwin-w — a WIDTH var, never a
   * stacked transform scale, which would fight the drag offset. No press needed:
   * the pointer being over the window is the whole gesture.
   *
   * AND RESIZING IS HANDLING IT, so it takes the mic like a drag does. The
   * promotion happens on the NOTCH, before the size clamps: a player winding a
   * window that is already as big as it goes has still told you which one they
   * are looking at. Re-focusing the window that already has focus is a no-op all
   * the way down (applyAudio skips a ramp already heading where it is going), so
   * a thirty-notch spin is one handover, not thirty.
   */
  function onWheel(rec, e) {
    if (!rec || rec.finished || !rec.node) return;
    if (e && typeof e.preventDefault === 'function') e.preventDefault();   // the page never scrolls
    if (e && typeof e.stopPropagation === 'function') e.stopPropagation();
    const dy = num(e && e.deltaY, 0);
    if (!dy) return;
    focusWindow(rec, false);
    const notches = Math.max(-3, Math.min(3, Math.round(dy / 100) || (dy > 0 ? 1 : -1)));
    const lo = rec.baseW * SIZE_MIN_FACTOR;
    const hi = rec.baseW * SIZE_MAX_FACTOR;
    const want = rec.widthPx * Math.pow(WHEEL_STEP, -notches);   // wheel UP (deltaY<0) grows
    const next = want < lo ? lo : (want > hi ? hi : want);
    if (Math.abs(next - rec.widthPx) < 0.01) return;
    rec.widthPx = next;
    try { rec.node.style.setProperty('--gg-vwin-w', `${next.toFixed(0)}px`); } catch (_e) { /* ignore */ }
  }

  /* ------------------------------------------------------------ the window */

  /**
   * Build + mount one window. Returns the record, or null if it cannot exist.
   * @param {boolean} [mayEvict=true] false for a LOCAL spawn: a self-popped
   *        window may never displace a payload window (see spawnLocal).
   */
  function openWindow(intensity, capMs, onDone, mayEvict) {
    if (typeof document === 'undefined') return null;
    const host = winLayer();
    if (!host) return null;
    if (mayEvict === false && wins.length >= MAX_WINDOWS) return null;
    if (!media || typeof media.drawKind !== 'function') return null;

    const entry = media.drawKind('video');
    if (!entry) return null;
    const handle = (typeof media.acquire === 'function') ? media.acquire(entry) : null;
    if (!handle || !handle.url) return null;
    sfx('video-window-in');

    // At the cap the OLDEST settles first — it has been watched the longest, so
    // it is endured, not interrupted. A LOCAL spawn never reaches this line.
    while (wins.length >= MAX_WINDOWS) settleWindow(wins[0], true);

    const baseW = baseWindowWidth(vpW());
    const pos = placeWindow(vpW(), vpH(), baseW, baseW * 9 / 16);
    const drift = driftVars();

    const node = document.createElement('div');
    node.className = 'gg-vwin';
    node.style.setProperty('left', `${pos.x}px`);
    node.style.setProperty('top', `${pos.y}px`);
    node.style.setProperty('--gg-vwin-w', `${baseW}px`);

    const driftEl = document.createElement('div');
    driftEl.className = calm ? 'gg-vwin-drift' : 'gg-vwin-drift gg-deco';
    driftEl.style.setProperty('--gg-vwin-dx', `${drift.dx}px`);
    driftEl.style.setProperty('--gg-vwin-dy', `${drift.dy}px`);
    driftEl.style.setProperty('--gg-vwin-drift-dur', `${drift.durS}s`);

    const inner = document.createElement('div');
    // ONE one-shot glitch-in, on its OWN element: the drift loop and the dot
    // pulse are shorthand `animation` declarations too, and two of those on one
    // element cancel each other (see the banner).
    inner.className = `gg-vwin-inner gg-vwin-in--${glitchVariant()}`;

    const video = document.createElement('video');
    video.className = 'gg-vwin-vid';
    video.playsInline = true;
    video.autoplay = true;
    video.controls = false;
    video.preload = 'auto';
    video.loop = false;               // it ends when the CLIP ends
    video.muted = true;               // applyAudio() hands the mic to the newest
    video.volume = 0;                 // …and it RAMPS up to volumeFor(intensity)

    const dot = document.createElement('span');
    dot.className = calm ? 'gg-vwin-dot' : 'gg-vwin-dot gg-deco';

    // The muted-speaker glyph: state, not a control. Purely CSS (a masked SVG in
    // fx.css), pointer-events off, shown only while .is-muted is on the wrapper.
    const mute = document.createElement('span');
    mute.className = 'gg-vwin-mute';
    mute.setAttribute('aria-hidden', 'true');

    // THE ✕ — the ONLY way to end a window early, and only when the player asked
    // for one. It is built unconditionally and hidden by fx.css while the option
    // is off, so flipping the toggle reaches windows that are ALREADY UP without
    // this module hearing about it; the click handler re-checks anyway, because
    // an invisible button that still works is a button.
    const skipBtn = document.createElement('button');
    skipBtn.className = SKIP_BTN_CLASS;
    skipBtn.setAttribute('type', 'button');
    skipBtn.setAttribute('aria-label', 'close this video');
    skipBtn.textContent = '✕';

    inner.appendChild(video);
    inner.appendChild(dot);
    inner.appendChild(mute);
    inner.appendChild(skipBtn);
    driftEl.appendChild(inner);
    node.appendChild(driftEl);

    const rec = {
      node, drift: driftEl, video, handle, onDone,
      intensity: clamp01(intensity),
      wid: ++widSeq, muted: false,
      dx: 0, dy: 0, held: false, finished: false,
      baseW, widthPx: baseW, tries: 0, endTimer: 0, fadeTimer: 0, bornAt: nowMs(),
    };

    // The button must never start a drag: the press stops here. It is NOT
    // preventDefault'ed — that is how a browser is talked out of the click that
    // follows, and the click is the whole point of the control.
    skipBtn.addEventListener('pointerdown', (e) => {
      if (e && typeof e.stopPropagation === 'function') e.stopPropagation();
    });
    skipBtn.addEventListener('click', (e) => {
      if (e && typeof e.stopPropagation === 'function') e.stopPropagation();
      if (e && typeof e.preventDefault === 'function') e.preventDefault();
      if (!skippable()) return;                 // hidden, and refusing regardless
      if (rec.finished || !rec.node) return;
      // The player chose to close it, which is the same receipt clicking it used
      // to post: they sat with it until they were done. ENDURED.
      settleWindow(rec, true);
    });

    video.onerror = () => {
      rec.tries++;
      if (rec.tries >= MAX_SOURCE_TRIES) { settleWindow(rec, false); return; }
      warn(`window source failed (${rec.tries}/${MAX_SOURCE_TRIES}), redrawing`);
      if (!reSource(rec)) settleWindow(rec, false);
    };
    video.onended = () => { if (!rec.finished) settleWindow(rec, true); };
    // The window wears the CLIP's own shape once the browser knows it — and the
    // same event is the only place the clip's real END becomes knowable, which
    // is the only chance to fade a natural ending at all (see the AUDIO banner).
    video.onloadedmetadata = () => {
      const w = num(video.videoWidth, 0), h = num(video.videoHeight, 0);
      if (w > 0 && h > 0) { try { node.style.setProperty('--gg-vwin-ar', `${w} / ${h}`); } catch (_e) { /* ignore */ } }
      const secs = num(video.duration, 0);
      if (secs > 0 && isFinite(secs)) {
        const endsIn = (secs - num(video.currentTime, 0)) * 1000;
        if (endsIn > 0 && endsIn < capMs) armFade(rec, endsIn - AUDIO_FADE_OUT_MS);
      }
    };
    node.addEventListener('pointerdown', (e) => onPointerDown(rec, e));
    node.addEventListener('contextmenu', (e) => onContextMenu(rec, e));
    try { node.addEventListener('wheel', (e) => onWheel(rec, e), { passive: false }); }
    catch (_e) { try { node.addEventListener('wheel', (e) => onWheel(rec, e)); } catch (_e2) { /* ignore */ } }

    try { video.src = handle.url; } catch (_e) { /* onerror will redraw */ }
    host.appendChild(node);
    wins.push(rec);
    // A fresh window takes the mic — arriving is the loudest touch there is.
    focusedId = rec.wid;
    publishCount();
    soon(() => { if (!rec.finished && rec.node) rec.node.classList.add('is-on'); }, 16);
    applyAudio();
    play(video);
    rec.endTimer = soon(() => settleWindow(rec, true), capMs);
    // The cap is an end we can see coming, so its fade is armed with it. A clip
    // that ends first re-arms this tighter from onloadedmetadata.
    armFade(rec, capMs - AUDIO_FADE_OUT_MS);
    return rec;
  }

  /** Redraw a source into a window whose entry turned out to be a dud. */
  function reSource(rec) {
    if (!rec || rec.finished || !rec.video) return false;
    if (!media || typeof media.drawKind !== 'function') return false;
    const entry = media.drawKind('video');
    if (!entry) return false;
    const handle = (typeof media.acquire === 'function') ? media.acquire(entry) : null;
    if (!handle || !handle.url) return false;
    try { if (rec.handle && rec.handle.release) rec.handle.release(); } catch (_e) { /* ignore */ }
    rec.handle = handle;
    try { rec.video.src = handle.url; } catch (_e) { return false; }
    play(rec.video);
    return true;
  }

  return {
    name: 'videos',

    start(cue) {
      const intensity = clamp01(cue && cue.intensity);
      if (sustained && sustained.alive) { sustained.retune(intensity); return; }
      sustained = createRun({ intensity, onDone: null });
      sustained.begin();
    },

    setIntensity(v) { if (sustained && sustained.alive) sustained.retune(v); },

    stop() {
      // The element run only. Payload windows are independent (uniform shape) —
      // executor.stopAll() cancels those through their own cancel fn.
      if (!sustained) return;
      sustained.settle(false);   // no receipt path on the element run (onDone is null)
      sustained = null;
    },

    /**
     * ONE floating window per payload. It dies on whichever comes first: the
     * clip ending, the duration cap (never more than WINDOW_CAP_MS), the player's
     * ✕ (only when "skippable videos" is on), or a fifth window displacing it —
     * all of those are ENDURED. Only the cancel fn (mercy / stopAll) and an empty
     * library are not.
     */
    renderPayload(payload, done) {
      const p = payload || {};
      const capMs = windowCapMs(p.duration_ms);
      const intensity = p.intensity !== undefined ? p.intensity : 0.6;
      const rec = openWindow(intensity, capMs, done, true);
      if (!rec) {
        // Nothing playable in the receiver's library: a silent no-op, receipted
        // completed (endured=false — nothing was endured, so no charge).
        if (typeof done === 'function') { try { done(false); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
        return () => {};
      }
      // TEARDOWN, not a dismissal: the executor calls this from stopAll (mercy,
      // recap, detach), and a fading ghost on the recap is exactly the husk this
      // renderer moved off #gg-stage to avoid. It goes on the spot.
      return () => settleWindow(rec, false, true);
    },

    /**
     * A window the player EARNED — exec/bubbles.js's `video` kind popped, and
     * exec/executor.js routed the pop here over the POP_EVENT seam. Everything
     * about it is a payload window: the same `wins` pool, the same chrome, the
     * same glitch-in, the same drag/wheel/click/✕, the same 60s-or-clip-end
     * lifetime, the same focus-follows-touch mic, and layers.stopAll() takes it
     * with the rest of the tier.
     *
     * THE TWO DIFFERENCES ARE BOTH ABOUT THE WIRE, AND BOTH ARE THE POINT:
     *
     *   NO RECEIPT. onDone is null, so nothing settles, nothing is notified and
     *   the opponent never hears about it. There is no payload id to close and
     *   no charge to earn: this window came out of the player's own bubble
     *   field, and paying a survival charge for closing your own window would
     *   be a charge printer. The pop already paid, once, through the drop
     *   economy (POP_WORTH_EFFECT).
     *
     *   IT NEVER EVICTS. At MAX_WINDOWS it FIZZLES and returns false instead of
     *   settling the oldest. An opponent's thrown VHS is something you are being
     *   asked to sit through and close; a self-pop must never be able to close
     *   it for you — least of all by receipting it `survived` on your way past.
     *   Eviction stays exactly where it was: an ARRIVING PAYLOAD evicts the
     *   oldest window, local or not, and a local one just dies quietly when it
     *   is the one displaced.
     *
     * The pop is still a pop either way. bubbles.js has already burst it, paid
     * its drop and dispatched its event before we are called, so a fizzle costs
     * the player nothing but the window.
     *
     * @param {{duration_ms?:number, intensity?:number}} [opts]
     * @returns {boolean} true if a window went up
     */
    spawnLocal(opts) {
      const o = opts || {};
      if (wins.length >= MAX_WINDOWS) {
        info(`local window fizzled — ${wins.length}/${MAX_WINDOWS} up, and a self-pop never displaces one`);
        return false;
      }
      const capMs = windowCapMs(o.duration_ms);
      const intensity = o.intensity !== undefined ? o.intensity : 0.6;
      const rec = openWindow(intensity, capMs, null, false);
      if (!rec) { info('local window fizzled — nothing playable in the library'); return false; }
      return true;
    },

    /**
     * Take down EVERY floating window, thrown or earned. exec/executor.js calls this from
     * stopAll() (mercy, recap, detach), just before layers.stopAll() wipes the tier's DOM.
     *
     * It exists because those two are not the same sweep. A THROWN window is reached through its
     * payload's cancel fn, so stopAll already had it; a window the player EARNED off their own
     * bubble has no payload, no id and no cancel — layers.stopAll() would delete its node and
     * leave the record in `wins` forever. Nothing was visible after that, so it never mattered
     * until the pool became something we REPORT (`vwin`): a phantom count would have kept a stack
     * of windows on the opponent's monitor for a match the player had already left.
     *
     * Each one settles endured=false — interrupted, not endured — which is the same receipt the
     * cancel fn posts, so calling this after the cancels is idempotent rather than a second
     * receipt (settleWindow latches on rec.finished).
     *
     * @returns {number} how many were still up
     */
    stopWindows() {
      const n = wins.length;
      while (wins.length) settleWindow(wins[0], false, true);
      // …and anything already mid-exit goes with them. A ghost is harmless while
      // the match is up (no slot, no count, no clicks) but it is still a node
      // with a live clip in it, and the recap must inherit an EMPTY layer.
      purgeGhosts();
      return n;
    },

    /** Test/diagnostic seam: how many windows are floating right now. This is
     *  the POOL, so a node still playing out its exit is NOT in it — that is the
     *  whole point of the ghost (see settleWindow). */
    windowCount: () => wins.length,

    /** Test/diagnostic seam: nodes that have left the pool and not yet the DOM. */
    ghostCount: () => ghosts.size,

    /** Test/diagnostic seam: which pooled window has the mic, -1 for silence. */
    focusedIndex: () => focusedIndexOf(wins, focusedId),

    /** Test/diagnostic seam: who the player has click-muted, oldest first. */
    mutedFlags: () => wins.map((w) => !!w.muted),

    /** Test/diagnostic seam: is the ✕ live? (the root attribute, read fresh). */
    skippable,
  };
}

export default createVideos;
