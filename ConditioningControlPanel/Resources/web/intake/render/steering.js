/* ============================================================================
 * render/steering.js — Agent D. The coercive-UI "steering" catalog.
 *
 * beats.js (Agent C) renders a beat's option elements, builds a SteerContext,
 * and hands it to installSteering(). This module reads ctx.roll (which steers,
 * how hard), weights it by the band (STEER_BAND_WEIGHT) and the user's caps, and
 * mutates the option elements to nudge / obstruct / decorate — the app's "Dark
 * Patterns" feel, but self-contained in-page.
 *
 * PRIME DIRECTIVE — INVARIANT #1 (friction, NOT lockout). Every steer leaves
 * EVERY option (including the "wrong"/refusal) completable with effort. This is
 * NOT trusted to each steer: a single per-beat EscapeGuard funnels every
 * "the user is fighting this option" signal, calls ctx.markProgress(), and once
 * ctx.escapeEffort interactions OR ctx.escapeMs of sustained effort is exceeded
 * it disarms all friction and trips ctx.forceComplete(index). No steer ever
 * permanently kills pointer events, and no target is ever parked fully offscreen
 * with no path back — evasive motion is bounded and self-relaxing.
 *
 * release() fully restores the DOM: transforms/opacity/blur reset, listeners and
 * rAF loops torn down, injected nodes removed. No cross-beat residue.
 *
 * The band-weighted roll decision and the escape-trip threshold are PURE and
 * exported (effectiveIntensity / resolveSteerPlan / shouldTripEscape) so they can
 * be unit-tested headless. All DOM access lives INSIDE installSteering — importing
 * this module never throws and never touches `document`.
 * ==========================================================================*/

import {
  Steer, STEERS, STEER_BAND_WEIGHT, Band, Mechanic,
  clamp01, lerp,
} from '../core/contracts.js';
import { getPrefs } from '../ui/prefs.js';

/** Generic SFX seam: steering holds no audio handle, so — like effects.js — it
 *  reaches audio.js through the 'intake-sfx' window CustomEvent. Never throws. */
function sfxCue(id, intensity) {
  try {
    if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
      window.dispatchEvent(new CustomEvent('intake-sfx', {
        detail: { id, intensity: (intensity == null ? 1 : clamp01(intensity)) },
      }));
    }
  } catch (_e) {}
}

/* ----------------------------------------------------------------------------
 * PURE decision helpers (headless-testable; no DOM).
 * -------------------------------------------------------------------------- */

/** Global master scalar from caps (defaults to 1, clamped 0..1). */
export function masterOf(caps) {
  if (!caps || caps.masterIntensity == null) return 1;
  return clamp01(caps.masterIntensity);
}

/* ----------------------------------------------------------------------------
 * BottomlessNo (owner-directed) — a self-rolled overlay steer, NOT in the engine's
 * STEER_POOL. Pressing the refusal / a wrong option makes THAT button appear to
 * peel off a stack: a throwaway clone falls off the bottom of the viewport while
 * the identical real button stays put underneath. N (6..10) presses fall for free;
 * the (N+1)th press commits normally (real scoring / jumpscare gate). Friction, not
 * lockout — a guard trip disarms it immediately (invariant #1). Eligible bands are
 * "lv3 onward" (Deepening/Climax/Recovery) and discrete refuse/wrong mechanics
 * (YesNo/MC4) only; Recovery has no discrete options so it self-excludes in practice.
 * -------------------------------------------------------------------------- */
const BOTTOMLESS_CHANCE = 0.05;                 // ~5% of eligible beats (owner-directed)
const BOTTOMLESS_MIN = 6, BOTTOMLESS_MAX = 10;  // free-fall count is drawn in [6,10]
const BOTTOMLESS_BANDS = new Set([Band.Deepening, Band.Climax, Band.Recovery]);
/** refusal-gate steers that already intercept the wrong press; BottomlessNo yields
 *  to them so exactly one gate owns the refusal per beat (one-steer-per-refusal). */
const REFUSAL_GATE_STEERS = new Set([
  Steer.MeltAway, Steer.HoldRefuse, Steer.NestedNag, Steer.ShrinkHit,
  Steer.OccludeGif, Steer.DragReveal,
]);
/** steers that own the wrong options' transform; when present, BottomlessNo leaves
 *  the real button's transform alone (the stack-shuffle rides the clone instead). */
const WRONG_TRANSFORM_STEERS = new Set([
  Steer.Flee, Steer.Exile, Steer.SizeSkew, Steer.Tunnel, Steer.DriftResolve, Steer.LateBloom,
]);

/** Free-fall count drawn in [BOTTOMLESS_MIN, BOTTOMLESS_MAX] inclusive. PURE. */
export function bottomlessCount(rand = Math.random) {
  let r = rand();
  if (!(r >= 0)) r = 0;                          // NaN/negative guard
  if (r >= 1) r = 0.9999999;
  const span = BOTTOMLESS_MAX - BOTTOMLESS_MIN + 1;
  return BOTTOMLESS_MIN + Math.floor(r * span);
}

/** Is this beat eligible for the BottomlessNo overlay? PURE (no DOM). */
export function isBottomlessEligible(ctx, plan) {
  if (!ctx) return false;
  if (masterOf(ctx.caps) <= 0) return false;                    // user disabled steering strength
  if (!BOTTOMLESS_BANDS.has(ctx.band)) return false;            // lv3 onward only
  if (ctx.mechanic !== Mechanic.YesNo && ctx.mechanic !== Mechanic.MC4) return false; // discrete refuse/wrong
  const opts = Array.isArray(ctx.options) ? ctx.options : [];
  if (!opts.some((o) => o && o.el && !o.isCorrect)) return false;  // needs a real wrong/refuse target
  const steers = (plan && plan.steers) || [];
  for (const s of steers) if (REFUSAL_GATE_STEERS.has(s)) return false; // yield to an existing refusal gate
  return true;
}

/* ----------------------------------------------------------------------------
 * HoverSwap (owner-directed) — a SECOND self-rolled overlay steer (like BottomlessNo,
 * NOT in the engine's STEER_POOL). On a BINARY beat (YesNo, or an MC4 the bank authored
 * with exactly two labels), HOVERING the WRONG/refuse option makes the two buttons trade
 * places (a smooth ~210ms translate exchange), so the cursor ends up over the CORRECT
 * option's new position — a gentle coercion toward the right answer. N (4..6, drawn once)
 * swaps happen, each feeding the escape guard, then it disarms and the buttons behave
 * normally. Friction, NOT lockout: it only MOVES buttons, it NEVER intercepts a click (a
 * click always lands on whatever is under the cursor); a guard trip glides both buttons
 * home. Eligible bands are Deepening/Climax (Recovery never coerces — engine contract).
 * It is a refusal-gate steer, so it yields to every engine refusal gate AND to
 * BottomlessNo (exactly one refusal gate owns a beat), and to any steer that already
 * drives the options' TRANSLATE (so the swap can't be clobbered). It is also mutually
 * exclusive with MouseHijack, which is rolled after it and stands down when it wins.
 * It is a POINTER gag and nothing else: the swap rides transforms only, so DOM order —
 * and therefore tab order and screen-reader order — never moves, and keyboard focus /
 * Enter reach both options unmolested (nothing here listens to focus or keys).
 * Reduced motion / sparkles-off users are exempt entirely (hoverSwapMuted).
 * -------------------------------------------------------------------------- */
const HOVERSWAP_CHANCE = 0.06;                  // ~6% of eligible beats (owner-directed)
const HOVERSWAP_MIN = 4, HOVERSWAP_MAX = 6;     // swap count is drawn in [4,6]
const HOVERSWAP_BANDS = new Set([Band.Deepening, Band.Climax]); // Recovery never coerces -> excluded
/** Mechanics whose options are plain, statically-laid-out buttons the pair-swap can trade.
 *  YesNo is always a pair; MC4 is a pair whenever the bank authored exactly two labels (the
 *  option-count check below is what actually gates it). Funnel/Destruct are excluded even at
 *  two options: both REWRITE a button on press (dissolve->respawn / shatter), and a rewrite
 *  racing a translate exchange reads as a bug rather than a gag. BubblePop is excluded because
 *  its "buttons" are already in flight on their own layer. */
const HOVERSWAP_MECHANICS = new Set([Mechanic.YesNo, Mechanic.MC4]);
/** steers that continuously drive an option's translate (tx/ty). HoverSwap owns the swap
 *  translate, so it yields whenever any of these is live to avoid a per-tick tug-of-war
 *  (scale-only steers — SizeSkew/Tunnel/LateBloom — compose fine via applyTf and are NOT
 *  excluded). Mirrors BottomlessNo's WRONG_TRANSFORM_STEERS note. */
const POSITION_STEERS = new Set([Steer.Magnet, Steer.Flee, Steer.Exile, Steer.DriftResolve]);

/** Coarse-pointer (touch / pen) probe. DOM/matchMedia-aware, so — like
 *  hoverSwapMuted — it lives OUTSIDE the pure eligibility predicates and is
 *  evaluated lazily and defensively (never throws at import). The ONE matchMedia
 *  '(pointer: coarse)' site for the whole module: it gates every cursor-centric
 *  steer (Magnet, HoverSwap, MouseHijack) off on touch and flips Flee into its
 *  tap-point adaptation. Optional `win` arg mirrors hoverSwapMuted (roll-site
 *  callers pass null -> global window; installers pass S.win). */
function isCoarsePointer(win) {
  try {
    const w = win || (typeof window !== 'undefined' ? window : null);
    if (w && w.matchMedia && w.matchMedia('(pointer: coarse)').matches) return true;
  } catch (_e) {}
  return false;
}

/** Is the pointer gag muted for this user? DOM/prefs-aware, so it lives OUTSIDE the
 *  pure eligibility predicate. Two ways out, and both mean NO SWAP AT ALL rather than
 *  an instant teleport: an instant exchange is strictly worse for a reduced-motion
 *  user than none — it removes the animation that made the rearrangement legible and
 *  leaves only the disorientation. `sparkles` is the same opt-out the rest of the shell
 *  reads for "calmer, please", and a self-rearranging interface is squarely that. */
function hoverSwapMuted(win) {
  try {
    const w = win || (typeof window !== 'undefined' ? window : null);
    if (w && w.matchMedia && w.matchMedia('(prefers-reduced-motion: reduce)').matches) return true;
  } catch (_e) {}
  try { if (getPrefs().sparkles === false) return true; } catch (_e) {}
  // Touch has no hover: the pointerenter-driven swap can never fire, so a coarse
  // pointer is muted the same as reduced-motion (no swap at all, never a teleport).
  if (isCoarsePointer(win)) return true;
  return false;
}

/** Swap count drawn in [HOVERSWAP_MIN, HOVERSWAP_MAX] inclusive. PURE. */
export function hoverSwapCount(rand = Math.random) {
  let r = rand();
  if (!(r >= 0)) r = 0;                          // NaN/negative guard
  if (r >= 1) r = 0.9999999;
  const span = HOVERSWAP_MAX - HOVERSWAP_MIN + 1;
  return HOVERSWAP_MIN + Math.floor(r * span);
}

/** Is this beat eligible for the HoverSwap overlay? PURE (no DOM). */
export function isHoverSwapEligible(ctx, plan) {
  if (!ctx) return false;
  if (masterOf(ctx.caps) <= 0) return false;                    // user disabled steering strength
  if (!HOVERSWAP_BANDS.has(ctx.band)) return false;             // Deepening/Climax only
  if (!HOVERSWAP_MECHANICS.has(ctx.mechanic)) return false;     // plain button pairs only
  const opts = Array.isArray(ctx.options) ? ctx.options : [];
  if (opts.length !== 2) return false;                          // the gag IS the pair trading places
  const hasWrong = opts.some((o) => o && o.el && !o.isCorrect); // need a wrong/refuse target
  const hasCorrect = opts.some((o) => o && o.el && o.isCorrect); // ...and a correct one to swap onto
  if (!hasWrong || !hasCorrect) return false;
  const steers = (plan && plan.steers) || [];
  for (const s of steers) {
    if (REFUSAL_GATE_STEERS.has(s)) return false;               // yield to an existing refusal gate
    if (POSITION_STEERS.has(s)) return false;                   // yield to a translate-owning steer (no clobber)
  }
  return true;
}

/* ----------------------------------------------------------------------------
 * MouseHijack (owner-directed) — a THIRD self-rolled steer (like BottomlessNo /
 * HoverSwap, NOT in the engine's STEER_POOL) — but LINGER-triggered, not rolled on
 * mount. If the user sits on an eligible beat too long without committing, the real
 * cursor is HIDDEN (cursor:none on the stage root + a leak-proof !important rule) and
 * a VIRTUAL cursor glyph is rendered at the pointer. Each real pointermove folds 1:1
 * into the virtual position; every frame the virtual position is ALSO blended toward
 * the CORRECT option's center with a gain that RAMPS from noticeable (0.25/frame) to
 * overwhelming (0.90/frame) over ~5s — so fighting works at first and becomes futile.
 * When the virtual cursor rests inside the correct option's rect ~150ms it AUTO-COMMITS
 * the correct answer through the real force-commit path (ctx.forceComplete). Friction,
 * NOT lockout (invariant #1): Escape or a hard fight (cumulative opposing movement >
 * ~2500px) ABORTS — real cursor restored, NO commit, beat continues; a guard trip also
 * tears it down. Eligible bands are Deepening/Climax only (Recovery never coerces —
 * engine contract; the owner said "lv3 onward" but Recovery is contract-gated). Any
 * discrete mechanic with a known correct option (MC4/YesNo/Mono/Destruct). It is
 * mutually exclusive with BottomlessNo/HoverSwap and yields to every engine refusal
 * gate AND to the position-channel steers (they MOVE options; the hijack moves the
 * cursor — a co-active moving correct-center / cursor-read tug-of-war would undermine
 * the "within rect 150ms" auto-commit, so it composes only with visual-only steers).
 * Reduced motion DISABLES it entirely (involuntary cursor motion is the worst RM
 * offender). The decision helpers below are PURE (headless-testable); all DOM lives
 * in the installer.
 * -------------------------------------------------------------------------- */
const MOUSEHIJACK_CHANCE = 0.40;                 // "we MIGHT" — rolled ONCE when the linger fires
const MOUSEHIJACK_LINGER_MS = 9000;              // silent arm; engage only after this un-committed dwell
const MOUSEHIJACK_BANDS = new Set([Band.Deepening, Band.Climax]); // Recovery never coerces -> excluded
const MOUSEHIJACK_MECHANICS = new Set([Mechanic.MC4, Mechanic.YesNo, Mechanic.Mono, Mechanic.Destruct]);
const HIJACK_GAIN_START = 0.25, HIJACK_GAIN_END = 0.90; // per-frame blend toward correct: fightable -> overwhelming
const HIJACK_RAMP_MS = 5000;                     // ramp duration (4-6s) from start gain to end gain
const HIJACK_FIGHT_ABORT_PX = 2500;              // cumulative opposing movement that aborts the hijack
const HIJACK_BUMP_PX = 800;                       // feed the shared escape guard every N px of fight
const HIJACK_DWELL_MS = 150;                     // virtual cursor must rest inside the correct rect this long
const HIJACK_MAX_RETRIES = 6;                     // re-checks while mid-drag / frozen before giving up
const HIJACK_RETRY_MS = 1200;                     // gap between those re-checks
const HIJACK_Z = 2147480000;                      // above cards/steers, below the jumpscare (2147483000)

/** Per-frame pull gain: lerps HIJACK_GAIN_START..HIJACK_GAIN_END across rampMs. PURE. */
export function hijackGain(elapsedMs, rampMs = HIJACK_RAMP_MS) {
  const t = rampMs > 0 ? clamp01((elapsedMs > 0 ? elapsedMs : 0) / rampMs) : 1;
  return lerp(HIJACK_GAIN_START, HIJACK_GAIN_END, t);
}

/** Is this beat eligible for the MouseHijack? PURE (no DOM; RM is gated in the installer). */
export function isMouseHijackEligible(ctx, plan) {
  if (!ctx) return false;
  if (masterOf(ctx.caps) <= 0) return false;                    // user disabled steering strength
  if (!MOUSEHIJACK_BANDS.has(ctx.band)) return false;           // Deepening/Climax only (Recovery gated out)
  if (!MOUSEHIJACK_MECHANICS.has(ctx.mechanic)) return false;   // discrete mechanic w/ a known correct
  const opts = Array.isArray(ctx.options) ? ctx.options : [];
  if (!opts.some((o) => o && o.el && o.isCorrect)) return false; // needs a correct option to steer onto
  const steers = (plan && plan.steers) || [];
  for (const s of steers) {
    if (REFUSAL_GATE_STEERS.has(s)) return false;               // a refusal gate owns the beat -> yield
    if (POSITION_STEERS.has(s)) return false;                   // a position steer moves options -> conflict, yield
  }
  return true;
}

/**
 * Effective steer intensity for a beat = roll.intensity (already past the user's
 * "play it straight" valve) * STEER_BAND_WEIGHT[band] * caps.masterIntensity.
 * Calibration/Recovery weight is 0 -> always 0 (no steering in warm-up/emerge).
 * @param {{intensity?:number}} roll
 * @param {string} band
 * @param {Object} caps
 * @returns {number} 0..1
 */
export function effectiveIntensity(roll, band, caps) {
  const w = STEER_BAND_WEIGHT[band] == null ? 0 : STEER_BAND_WEIGHT[band];
  const base = roll && typeof roll.intensity === 'number' ? clamp01(roll.intensity) : 0;
  return clamp01(base * clamp01(w) * masterOf(caps));
}

/**
 * Resolve the full plan for a beat from its SteerContext. PURE: no DOM.
 *   - collects primary + secondary steers, dedupes, drops unknown / Steer.None
 *   - computes band+cap weighted intensity
 *   - active === false when intensity is 0 (Calibration, Recovery, valve at 0,
 *     or masterIntensity 0) OR when no real steer remains.
 * @param {import('../core/contracts.js').SteerContext} ctx
 * @returns {{active:boolean, intensity:number, primary:string, secondary:string[], steers:string[]}}
 */
export function resolveSteerPlan(ctx) {
  const roll = (ctx && ctx.roll) || { primary: Steer.None, secondary: [], intensity: 0 };
  const intensity = effectiveIntensity(roll, ctx && ctx.band, ctx && ctx.caps);

  const wanted = [];
  if (roll.primary && roll.primary !== Steer.None) wanted.push(roll.primary);
  for (const s of (roll.secondary || [])) {
    if (s && s !== Steer.None) wanted.push(s);
  }
  const steers = [];
  const seen = new Set();
  for (const s of wanted) {
    if (STEERS.indexOf(s) === -1) continue;   // unknown -> ignored (contract gap tolerance)
    if (seen.has(s)) continue;
    seen.add(s);
    steers.push(s);
  }

  const active = intensity > 0 && steers.length > 0;
  return {
    active,
    intensity,
    primary: active ? roll.primary : Steer.None,
    secondary: active ? steers.filter((s) => s !== roll.primary) : [],
    steers: active ? steers : [],
  };
}

/**
 * The escape hatch trip predicate (invariant #1). PURE.
 * Trips when interactions on a fought option reach escapeEffort OR sustained
 * effort ms reaches escapeMs. Non-positive thresholds fall back to sane defaults
 * so a mis-populated ctx can never make the refusal un-completable.
 * @returns {boolean}
 */
export function shouldTripEscape({ attempts = 0, sustainedMs = 0, escapeEffort = 5, escapeMs = 4000 } = {}) {
  const eE = escapeEffort > 0 ? escapeEffort : 5;
  const eM = escapeMs > 0 ? escapeMs : 4000;
  return attempts >= eE || sustainedMs >= eM;
}

/* ----------------------------------------------------------------------------
 * FACTORY
 * -------------------------------------------------------------------------- */
const STYLE_ID = 'intake-steering-style';
const _styledDocs = new WeakSet();

/**
 * @param {{caps?:Object, media?:Object}} opts
 * @returns {{installSteering:(ctx:Object)=>{release:()=>void}}}
 */
export function createSteering({ caps: factoryCaps, media: factoryMedia } = {}) {
  return { installSteering: (ctx) => installSteering(ctx, factoryCaps, factoryMedia) };
}

const NOOP_HANDLE = { release: () => {} };

/* ----------------------------------------------------------------------------
 * INSTALL — all DOM access lives here.
 * -------------------------------------------------------------------------- */
function installSteering(ctx, factoryCaps, factoryMedia) {
  // Merge caps: per-beat ctx.caps wins, factory caps as fallback.
  const caps = ctx && ctx.caps ? ctx.caps : (factoryCaps || {});
  const beatCtx = ctx ? Object.assign({}, ctx, { caps }) : ctx;

  const plan = resolveSteerPlan(beatCtx);

  // BottomlessNo overlay: a self-rolled steer (the engine's STEER_POOL doesn't carry
  // it). ~5% of eligible beats "from lv3 onward". It can activate even when the
  // engine rolled no steer for this beat, so it gates the early-out too.
  const bottomless = isBottomlessEligible(beatCtx, plan) && Math.random() < BOTTOMLESS_CHANCE;

  // HoverSwap overlay: a SECOND self-rolled refusal-gate steer (~6% of eligible beats).
  // Mutually exclusive with BottomlessNo — both are self-rolled gates and the invariant
  // is exactly ONE refusal gate per beat, so it yields when BottomlessNo already rolled.
  // Muted users are filtered HERE rather than inside the installer so a muted beat is
  // still free to arm the linger-triggered MouseHijack below (which does its own RM
  // gate) instead of being silently spent on a gag that will never run.
  const hoverSwap = !bottomless && !hoverSwapMuted(null)
    && isHoverSwapEligible(beatCtx, plan) && Math.random() < HOVERSWAP_CHANCE;

  // MouseHijack: a THIRD self-rolled steer, but LINGER-triggered (armed silently now;
  // it rolls its ~40% "we MIGHT" only after ~9s of un-committed dwell — see installer).
  // Mutually exclusive with the two self-rolled gates; RM is gated inside the installer.
  // Coarse pointer is filtered HERE (mirroring hoverSwapMuted's roll-site gate) so the
  // linger timer never even arms on touch — there is no cursor to hide/hijack. The
  // installer keeps a belt-and-braces coarse early-return too.
  const mouseHijack = !bottomless && !hoverSwap && !isCoarsePointer(null)
    && isMouseHijackEligible(beatCtx, plan);

  if (!plan.active && !bottomless && !hoverSwap && !mouseHijack) return NOOP_HANDLE; // Calibration/Recovery/valve-0 -> play it straight

  const doc = (ctx.root && ctx.root.ownerDocument) ||
    (typeof document !== 'undefined' ? document : null);
  const win = doc && (doc.defaultView || (typeof window !== 'undefined' ? window : null));
  if (!doc || !win) return NOOP_HANDLE;               // no DOM (e.g. headless import) -> no-op

  ensureStyle(doc);

  const all = Array.isArray(ctx.options) ? ctx.options.slice() : [];
  const correct = all.filter((o) => o && o.isCorrect);
  const wrong = all.filter((o) => o && !o.isCorrect);

  const s = plan.intensity;                           // effective 0..1

  /* ---- teardown registry ------------------------------------------------ */
  const cleanups = [];
  const addCleanup = (fn) => { if (typeof fn === 'function') cleanups.push(fn); };
  let released = false;

  /* ---- shared rAF loop -------------------------------------------------- */
  const tickers = new Set();
  let rafId = 0, lastT = 0;
  const loop = (t) => {
    const dt = lastT ? Math.min(64, t - lastT) : 16;
    lastT = t;
    for (const fn of tickers) { try { fn(dt, t); } catch (_e) {} }
    rafId = tickers.size ? win.requestAnimationFrame(loop) : 0;
  };
  const addTicker = (fn) => { tickers.add(fn); if (!rafId) rafId = win.requestAnimationFrame(loop); };
  const removeTicker = (fn) => { tickers.delete(fn); };
  addCleanup(() => { if (rafId) win.cancelAnimationFrame(rafId); rafId = 0; tickers.clear(); });

  /* ---- transform state (composited across steers) ----------------------- */
  const tfState = new Map();   // el -> {tx,ty,sx,sy,rot, orig}
  const tf = (el) => {
    let st = tfState.get(el);
    if (!st) { st = { tx: 0, ty: 0, sx: 1, sy: 1, rot: 0, orig: el.style.transform || '' }; tfState.set(el, st); }
    return st;
  };
  const applyTf = (el) => {
    const st = tf(el);
    el.style.transform = `translate(${st.tx.toFixed(2)}px,${st.ty.toFixed(2)}px) scale(${st.sx.toFixed(3)},${st.sy.toFixed(3)}) rotate(${st.rot.toFixed(2)}deg)`;
    el.style.willChange = 'transform';
  };
  addCleanup(() => {
    for (const [el, st] of tfState) {
      try { el.style.transform = st.orig; el.style.willChange = ''; } catch (_e) {}
    }
    tfState.clear();
  });

  /* ---- style snapshot/restore for arbitrary props ----------------------- */
  const styleSnaps = [];
  const snapStyle = (el, props) => {
    const saved = {};
    for (const p of props) saved[p] = el.style[p];
    styleSnaps.push({ el, saved });
  };
  addCleanup(() => {
    for (const { el, saved } of styleSnaps) {
      for (const p in saved) { try { el.style[p] = saved[p]; } catch (_e) {} }
    }
    styleSnaps.length = 0;
  });

  /* ---- listener registry ------------------------------------------------ */
  const addListener = (target, type, fn, opts) => {
    if (!target || !target.addEventListener) return;
    target.addEventListener(type, fn, opts);
    addCleanup(() => { try { target.removeEventListener(type, fn, opts); } catch (_e) {} });
  };

  /* ---- injected nodes --------------------------------------------------- */
  const addNode = (node, parent) => {
    (parent || ctx.root || doc.body).appendChild(node);
    addCleanup(() => { try { node.remove(); } catch (_e) {} });
    return node;
  };

  /* ---- EscapeGuard: the invariant-#1 backstop --------------------------- */
  const guard = makeEscapeGuard(ctx);

  /* ---- shared cursor tracking (lazy) ------------------------------------ */
  const cursor = { x: 0, y: 0, has: false };
  let cursorWired = false;
  const wireCursor = () => {
    if (cursorWired) return;
    cursorWired = true;
    const onMove = (e) => { cursor.x = e.clientX; cursor.y = e.clientY; cursor.has = true; };
    addListener(doc, 'pointermove', onMove, { passive: true });
  };

  const centerOf = (el) => {
    const r = el.getBoundingClientRect();
    return { x: r.left + r.width / 2, y: r.top + r.height / 2, w: r.width, h: r.height, r };
  };

  const media = (ctx && ctx.media) ? ctx.media : (factoryMedia || null);

  const sharedCtx = {
    ctx: beatCtx, doc, win, s, all, correct, wrong, guard, media,
    steers: plan.steers,
    addCleanup, addListener, addNode, addTicker, removeTicker,
    tf, applyTf, snapStyle, centerOf, cursor, wireCursor,
  };

  /* ---- global "missed click" tracker: any evasive steer needs it -------- */
  const EVASIVE = new Set([Steer.Flee, Steer.Exile, Steer.ShrinkHit, Steer.OpacitySkew,
    Steer.Defocus, Steer.Decay, Steer.OccludeGif, Steer.DragReveal, Steer.Crowd, Steer.Tunnel,
    Steer.MeltAway]);
  const hasEvasive = plan.steers.some((x) => EVASIVE.has(x));
  if (hasEvasive && wrong.length) {
    const onDown = (e) => {
      if (guard.isTripped()) return;
      // did the press land on one of our option buttons?
      const onOption = all.some((o) => o.el && (o.el === e.target || o.el.contains(e.target)));
      if (onOption) return;
      // a miss near a wrong option counts as a fought attempt on the nearest wrong option
      const near = nearestOption(wrong, e.clientX, e.clientY);
      if (near) guard.bump(near.index, 0);
    };
    addListener(ctx.root || doc, 'pointerdown', onDown, { passive: true });
  }

  /* ---- run each steer installer ---------------------------------------- */
  for (const steer of plan.steers) {
    const fn = INSTALLERS[steer];
    if (!fn) continue;
    try { fn(sharedCtx); } catch (_e) { /* one bad steer must not break the beat */ }
  }

  /* ---- self-rolled BottomlessNo overlay (not in the engine's plan) ------- */
  if (bottomless && plan.steers.indexOf(Steer.BottomlessNo) === -1) {
    try { INSTALLERS[Steer.BottomlessNo](sharedCtx); } catch (_e) {}
  }

  /* ---- self-rolled HoverSwap overlay (not in the engine's plan) --------- */
  if (hoverSwap && plan.steers.indexOf(Steer.HoverSwap) === -1) {
    try { INSTALLERS[Steer.HoverSwap](sharedCtx); } catch (_e) {}
  }

  /* ---- self-rolled MouseHijack (linger-triggered; not in the engine's plan) --- */
  if (mouseHijack && plan.steers.indexOf(Steer.MouseHijack) === -1) {
    try { INSTALLERS[Steer.MouseHijack](sharedCtx); } catch (_e) {}
  }

  /* ---- handle ----------------------------------------------------------- */
  return {
    release() {
      if (released) return;
      released = true;
      for (let i = cleanups.length - 1; i >= 0; i--) {
        try { cleanups[i](); } catch (_e) {}
      }
      cleanups.length = 0;
      guard.dispose();
    },
  };
}

/* ----------------------------------------------------------------------------
 * EscapeGuard — funnels all "fighting an option" signals; guarantees escape.
 * -------------------------------------------------------------------------- */
function makeEscapeGuard(ctx) {
  const escapeEffort = (ctx && ctx.escapeEffort > 0) ? ctx.escapeEffort : 5;
  const escapeMs = (ctx && ctx.escapeMs > 0) ? ctx.escapeMs : 4000;
  const attempts = new Map();   // index -> count
  const sustained = new Map();  // index -> ms
  const releasers = [];         // friction disarmers, run before forcing
  let tripped = false;

  const onFrictionRelease = (fn) => { if (typeof fn === 'function') releasers.push(fn); };

  const trip = (index) => {
    if (tripped) return;
    tripped = true;
    for (const fn of releasers) { try { fn(); } catch (_e) {} }
    try { ctx && ctx.markProgress && ctx.markProgress(); } catch (_e) {}
    try { ctx && ctx.forceComplete && ctx.forceComplete(index); } catch (_e) {}
  };

  const check = (index) => {
    const a = attempts.get(index) || 0;
    const m = sustained.get(index) || 0;
    if (shouldTripEscape({ attempts: a, sustainedMs: m, escapeEffort, escapeMs })) trip(index);
  };

  return {
    escapeEffort, escapeMs,
    isTripped: () => tripped,
    onFrictionRelease,
    /** an interaction the user spent fighting option `index` */
    bump(index, deltaMs) {
      if (tripped || index == null) return;
      attempts.set(index, (attempts.get(index) || 0) + 1);
      if (deltaMs) sustained.set(index, (sustained.get(index) || 0) + deltaMs);
      try { ctx && ctx.markProgress && ctx.markProgress(); } catch (_e) {}
      check(index);
    },
    /** sustained (held/hovered) effort accumulation, no attempt increment */
    addSustained(index, deltaMs) {
      if (tripped || index == null || !deltaMs) return;
      sustained.set(index, (sustained.get(index) || 0) + deltaMs);
      check(index);
    },
    /** force from a friction steer once its own criterion is met */
    force(index) { trip(index); },
    dispose() { attempts.clear(); sustained.clear(); releasers.length = 0; },
  };
}

/* ----------------------------------------------------------------------------
 * helpers
 * -------------------------------------------------------------------------- */
function nearestOption(opts, x, y) {
  let best = null, bestD = Infinity;
  for (const o of opts) {
    if (!o.el) continue;
    const r = o.el.getBoundingClientRect();
    const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
    const d = (cx - x) * (cx - x) + (cy - y) * (cy - y);
    if (d < bestD) { bestD = d; best = o; }
  }
  return best;
}

/** clamp a translate so the element stays fully within the viewport (path back). */
function clampOnscreen(el, tx, ty, win) {
  const r = el.getBoundingClientRect();
  const pad = 6;
  // current rect corresponds to current transform; compute target rect delta
  const curTx = 0, curTy = 0; // tf composes from scratch each apply, caller passes absolute
  const left = r.left + (tx - curTx);
  const top = r.top + (ty - curTy);
  const maxX = win.innerWidth - r.width - pad, maxY = win.innerHeight - r.height - pad;
  let nx = tx, ny = ty;
  if (left < pad) nx += (pad - left);
  else if (left > maxX) nx -= (left - maxX);
  if (top < pad) ny += (pad - top);
  else if (top > maxY) ny -= (top - maxY);
  return { tx: nx, ty: ny };
}

/** Placement for fixed widgets (sticker/veil) anchored to an option rect. PURE
 * given rand. Bubble beats measure their anchors mid-flight (or hidden), which
 * yields degenerate/offscreen rects — the widget then clips at the viewport's
 * top-left. When the anchor is unusable, park the widget in a RANDOM corner,
 * comfortably inset from the edges; otherwise cover the anchor, clamped fully
 * onscreen. Returns { left, top, w, h }. */
export function parkPlacement(c, win, minW, minH, rand = Math.random) {
  const vw = win.innerWidth, vh = win.innerHeight;
  const pad = 10;
  const w = (c && c.w) || 0, h = (c && c.h) || 0;
  const anchored = c && c.r && w >= 24 && h >= 16 &&
    c.r.left >= -pad && c.r.top >= -pad &&
    c.r.left + w <= vw + pad && c.r.top + h <= vh + pad;
  if (anchored) {
    return {
      left: Math.min(Math.max(c.r.left, pad), Math.max(pad, vw - w - pad)),
      top: Math.min(Math.max(c.r.top, pad), Math.max(pad, vh - h - pad)),
      w, h,
    };
  }
  const pw = Math.max(w, minW || 150), ph = Math.max(h, minH || 56);
  // inset scales with the viewport but stays in a comfortable band
  const ix = Math.min(Math.max(48, vw * 0.08), 140);
  const iy = Math.min(Math.max(48, vh * 0.08), 120);
  const right = rand() < 0.5, bottom = rand() < 0.5;
  return {
    left: Math.max(pad, right ? vw - ix - pw : ix),
    top: Math.max(pad, bottom ? vh - iy - ph : iy),
    w: pw, h: ph,
  };
}

/* Re-measure an option until its entry animation AND any in-progress typewriter
 * wrap have gone quiet, then hand the caller a trustworthy rect. Cover widgets
 * (sticker/veil) measured at install time catch the card mid-flight or before the
 * question text has finished typing — a wrap partway through typing pushes the
 * options DOWN, so a widget anchored the instant the rect held still for a frame
 * or two ends up floating over empty space. Each frame we take a fresh
 * centerOf(el): a frame COUNTS toward settling when the rect is valid (w>=24,
 * h>=16, fully within the viewport with 10px slack) AND unmoved (<1px vs the
 * previous frame). We require `quiet` such consecutive frames (~45 ≈ 750ms at
 * 60fps) before settling — any invalid or moved frame (e.g. a late typewriter
 * wrap) resets the streak to 0, so in practice the widget appears shortly AFTER
 * the typewriter stops changing the layout (the "add it AFTER the typewriter
 * finished" behavior, achieved with no cross-module signal). Movement resets the
 * quiet streak but NOT the overall frame budget: if the rect never goes quiet
 * within `tries` frames (~600 ≈ 10s) -> cb(null) so the caller can skip the steer.
 * On settle -> cb(c). The rAF loop registers its cancel via S.addCleanup so
 * teardown stops it. */
function whenAnchored(S, el, cb, tries = 600, quiet = 45) {
  const vw = S.win.innerWidth, vh = S.win.innerHeight, pad = 10;
  let prev = null, streak = 0, n = 0, rafId = 0, done = false;
  const finish = (val) => { if (done) return; done = true; if (rafId) S.win.cancelAnimationFrame(rafId); try { cb(val); } catch (_e) {} };
  const tick = () => {
    if (done) return;
    const c = S.centerOf(el);
    const valid = c && c.r && c.w >= 24 && c.h >= 16 &&
      c.r.left >= -pad && c.r.top >= -pad &&
      c.r.left + c.w <= vw + pad && c.r.top + c.h <= vh + pad;
    if (valid && prev && Math.abs(c.r.left - prev.left) < 1 && Math.abs(c.r.top - prev.top) < 1) {
      // unmoved-and-valid this frame -> extend the quiet streak; settle once it holds.
      if (++streak >= quiet) { finish(c); return; }
    } else {
      // invalid OR moved (layout still churning: entry flight / typewriter wrap) -> reset streak.
      streak = 0;
    }
    prev = valid ? { left: c.r.left, top: c.r.top } : null;
    if (++n >= tries) { finish(null); return; }   // budget spent, never went quiet -> skip
    rafId = S.win.requestAnimationFrame(tick);
  };
  rafId = S.win.requestAnimationFrame(tick);
  S.addCleanup(() => { done = true; if (rafId) S.win.cancelAnimationFrame(rafId); });
}

/* Lightweight rAF-throttled follow-watcher for the two COVER widgets (sticker /
 * veil). After a widget anchors, a LATE typewriter wrap can still shift the option
 * it covers; this re-measures the option ~5x/sec and, when its rect has drifted
 * >2px from the last seen position, invokes onShift(newCenter) so the widget can
 * glide/relocate to the new spot and refresh any geometry-derived values.
 *   - opts.isDone()     : stop & self-remove (widget disarmed / beat torn down).
 *   - opts.isDeferred() : the user is actively dragging THIS widget — skip the
 *                         measurement entirely (don't advance the baseline) so the
 *                         shift is caught and applied on release, not mid-drag.
 * `initial` seeds the baseline with the anchor the widget was placed at, so the
 * first real shift is measured against the true anchor. Registers via addTicker /
 * addCleanup (shared rAF loop). */
function followAnchor(S, el, initial, onShift, opts) {
  const PERIOD_MS = 200;                 // ~5 checks/sec
  let acc = 0;
  const last = { left: initial.left, top: initial.top };
  const tick = (dt) => {
    if (opts.isDone && opts.isDone()) { S.removeTicker(tick); return; }
    acc += dt;
    if (acc < PERIOD_MS) return;
    acc = 0;
    if (opts.isDeferred && opts.isDeferred()) return;   // dragging -> defer to release
    const c = S.centerOf(el);
    if (!c || !c.r) return;
    if (Math.abs(c.r.left - last.left) > 2 || Math.abs(c.r.top - last.top) > 2) {
      last.left = c.r.left; last.top = c.r.top;
      try { onShift(c); } catch (_e) {}
    }
  };
  S.addTicker(tick);
  S.addCleanup(() => S.removeTicker(tick));
}

/* ----------------------------------------------------------------------------
 * OccludeGif multi-cover helpers (owner-directed endgame escalation).
 *
 * Baseline OccludeGif parks ONE draggable GIF sticker over a single WRONG option
 * (S.wrong[floor(len/2)] — the middle of the wrong list), leaving the correct
 * answer (the option the steer WANTS pressed) and the other wrong options visible.
 * Toward the endgame the owner wants progressively MORE covers so only ONE option
 * stays visible: Climax covers 2 (deep Climax 3), Recovery covers 3. The covered
 * targets are always drawn from the WRONG options so the correct/steered pick is
 * the one left visible; count is capped so >= 1 option is ALWAYS uncovered.
 * -------------------------------------------------------------------------- */

/** How many covers this OccludeGif beat mounts. lv1-3 => 1 (unchanged). Climax => 2
 *  (deep Climax, high depth => 3). Recovery => 3. Capped to wrong.length AND to
 *  optionCount-1 so at least ONE option is always left fully visible. PURE. */
function occludeCoverCount(band, depth, optionCount, wrongCount) {
  const oc = (typeof optionCount === 'number' && optionCount > 0) ? optionCount : (wrongCount + 1);
  const maxCover = Math.max(1, Math.min(wrongCount, oc - 1));   // never cover the last visible option
  let want = 1;                                                  // lv1-3: single cover (legacy)
  if (band === Band.Recovery) {
    want = 3;
  } else if (band === Band.Climax) {
    want = 2;                                                    // progressive: deep Climax escalates to 3
    if (typeof depth === 'number' && depth >= 0.9) want = 3;
  }
  return Math.max(1, Math.min(want, maxCover));
}

/** Choose which WRONG options to cover. Single cover keeps the legacy middle-wrong
 *  target; multi-cover takes the first `count` wrong options (order is cosmetic —
 *  the correct/steered pick is never in this set, so it always stays visible). PURE. */
function pickOccludeTargets(wrong, count) {
  if (!wrong || !wrong.length) return [];
  const n = Math.max(1, Math.min(count, wrong.length));
  if (n === 1) return [wrong[Math.floor(wrong.length / 2)]];    // legacy single-cover target
  return wrong.slice(0, n);
}

/** Tiny shared throttle so 3 covers can't spam the sticker-drag seam at once. The
 *  seam itself rate-limits; this just coalesces near-simultaneous cues. */
function makeSfxThrottle(minGapMs) {
  let last = -Infinity;
  return (id, intensity) => {
    const now = (typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now();
    if (now - last < minGapMs) return;
    last = now;
    sfxCue(id, intensity);
  };
}

/** Mount ONE draggable GIF cover over `target`. Self-contained: own anchor wait,
 *  own GIF (via pickGif — de-duped across the beat), own ±rotation, own drag-clear
 *  threshold, own followAnchor watcher, own guard.onFrictionRelease disarm (so a
 *  guard trip removes EVERY cover at once). Skip-on-null anchoring: a cover that
 *  never settles simply doesn't mount (never parks in a corner). */
function mountOccludeCover(S, target, o) {
  const rot = o.rot, sfx = o.sfx, pickGif = o.pickGif;
  whenAnchored(S, target.el, (c) => {
    if (!c) return;                                   // never settled -> this cover doesn't mount
    const vw = S.win.innerWidth, vh = S.win.innerHeight, pad = 10;
    const w = c.w + 12, h = c.h + 12;
    let curW = c.w;                                   // drag-clear threshold base (follows late shifts)
    const homeLeft = (cc) => Math.min(Math.max(cc.r.left - 6, pad), Math.max(pad, vw - w - pad));
    const homeTop = (cc) => Math.min(Math.max(cc.r.top - 6, pad), Math.max(pad, vh - h - pad));
    const left = homeLeft(c);
    const top = homeTop(c);

    const sticker = S.doc.createElement('div');
    sticker.className = 'intake-steer-sticker';
    sticker.style.left = left + 'px';
    sticker.style.top = top + 'px';
    sticker.style.width = w + 'px';
    sticker.style.height = h + 'px';
    sticker.style.transition = 'opacity .3s ease';

    // A real GIF reads as a sticker slapped over the option; gradient+💠 fallback.
    let img = null;
    const url = pickGif();                            // de-duped across covers in this beat
    if (url) {
      img = S.doc.createElement('img');
      img.src = url;
      img.style.cssText = 'pointer-events:none;width:100%;height:100%;object-fit:cover;border-radius:inherit;display:block';
      img.addEventListener('error', () => {           // broken GIF -> gradient+💠
        try { img.remove(); } catch (_e) {}
        img = null;
        sticker.textContent = '💠';
      });
      sticker.style.borderRadius = '14px';
      sticker.style.overflow = 'hidden';
      sticker.style.boxShadow = '0 6px 18px rgba(0,0,0,.35)';
      sticker.style.cursor = 'grab';
      sticker.appendChild(img);
    } else {
      sticker.textContent = '💠';                      // gradient placeholder look
    }

    S.addNode(sticker, S.doc.body);

    let dragging = false, ox = 0, oy = 0, moved = 0, disarmed = false;
    const setTf = (dx, dy) => { sticker.style.transform = `translate(${dx}px,${dy}px) rotate(${rot.toFixed(2)}deg)`; };
    setTf(0, 0);                                             // seat the base rotation (0deg under reduced motion)
    const disarm = () => { disarmed = true; sticker.style.pointerEvents = 'none'; sticker.style.opacity = '0'; };
    S.guard.onFrictionRelease(disarm);                       // guard trip -> ALL covers disarm together
    const down = (e) => { dragging = true; ox = e.clientX; oy = e.clientY; sticker.setPointerCapture && sticker.setPointerCapture(e.pointerId); S.guard.bump(target.index, 0); sfx('sticker-drag'); }; // one-shot on drag START
    const move = (e) => {
      if (!dragging) return;
      const dx = e.clientX - ox, dy = e.clientY - oy;
      setTf(dx, dy);                                        // drag translate composes with base rotation
      moved = Math.hypot(dx, dy);
      if (moved > Math.max(curW * 0.6, 90)) { if (!disarmed) sfx('sticker-drag'); disarm(); } // dragged clear -> reveal cue + expose
    };
    const up = () => { dragging = false; };
    S.addListener(sticker, 'pointerdown', down);
    S.addListener(S.doc, 'pointermove', move, { passive: true });
    S.addListener(S.doc, 'pointerup', up, { passive: true });
    // double-click shoves it aside outright
    S.addListener(sticker, 'dblclick', () => { S.guard.bump(target.index, 0); disarm(); });

    // Follow late option shifts (typewriter wrap after anchoring): glide the
    // sticker's HOME (left/top) to track the option. NEVER while dragging (defer to
    // release) — the drag translate stays composited on top. Refresh the drag-clear
    // threshold base (curW) from the new width.
    followAnchor(S, target.el, { left: c.r.left, top: c.r.top }, (nc) => {
      curW = nc.w;
      sticker.style.transition = 'left .2s ease, top .2s ease, opacity .3s ease';
      sticker.style.left = homeLeft(nc) + 'px';
      sticker.style.top = homeTop(nc) + 'px';
    }, { isDone: () => disarmed, isDeferred: () => dragging });
  });
}

/* ----------------------------------------------------------------------------
 * Flee — TAP-POINT adaptation for coarse pointers (touch).
 *
 * Desktop Flee slides the wrong option away from a HOVERING cursor. Touch has no
 * hover: the first the option hears of the finger is the tap itself, and a
 * mouse-style flee would let the wrong answer commit on first contact. So on a
 * coarse pointer the option DODGES out from under the finger on pointerdown — a
 * quick transform transition — and the tap that triggered it is swallowed before
 * its click can commit.
 *
 * INVARIANT #1 (friction, NOT lockout) is kept two independent ways:
 *   (a) dodges are hard-capped (FLEE_TAP_DODGES) with the distance shrinking each
 *       time, so a determined tapper commits on the (cap+1)th tap ALL BY ITSELF —
 *       no escape hatch required; and
 *   (b) every dodge still feeds the shared EscapeGuard exactly like the mouse
 *       path's pointerdown bump, so onFrictionRelease -> forceComplete can also
 *       re-form + commit it. Whichever lands first wins.
 * Synthetic forceComplete clicks (!isTrusted, clientX/Y === 0) ALWAYS pass — the
 * file-wide un-vetoable hatch convention. The dodge is transform-only (restored by
 * the shared tf cleanup) and every listener is registered through S.addListener,
 * so release() leaves no residue. The option is clamped fully onscreen each dodge
 * (never parked out of reach).
 * -------------------------------------------------------------------------- */
const FLEE_TAP_DODGES = 3;          // dodges before the option gives up and commits
const FLEE_TAP_SWALLOW_MS = 260;    // a click this soon after a dodge IS the dodged tap
function fleeTapPoint(S) {
  const base = lerp(40, 110, S.s);              // first dodge distance (shrinks after)
  const pad = 8;
  const dodges = new Map();                     // index -> dodges spent
  const lastDodgeAt = new Map();                // index -> perf time of last dodge
  const nowMs = () => (S.win.performance && S.win.performance.now)
    ? S.win.performance.now() : Date.now();
  let disarmed = false;

  // Guard trip (a determined refusal beat the escape threshold): glide every wrong
  // option home and stop dodging, so the forced commit lands on an honest layout.
  const disarm = () => {
    if (disarmed) return;
    disarmed = true;
    for (const o of S.wrong) {
      try {
        const st = S.tf(o.el);
        o.el.style.transition = 'transform .2s cubic-bezier(.2,.8,.2,1)';
        st.tx = 0; st.ty = 0; S.applyTf(o.el);
      } catch (_e) {}
    }
  };
  S.guard.onFrictionRelease(disarm);

  // Move `o` away from the tap point (px,py), shrinking with each dodge, clamped so
  // its untranslated slot + new transform stays fully onscreen (always reachable).
  const dodge = (o, px, py) => {
    const n = dodges.get(o.index) || 0;
    if (n >= FLEE_TAP_DODGES) return false;     // capped -> let this tap commit
    dodges.set(o.index, n + 1);
    const dist = base * (1 - n / FLEE_TAP_DODGES);   // progressive shrink -> convergence
    const st = S.tf(o.el);
    const r = o.el.getBoundingClientRect();
    const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
    const ax = cx - px, ay = cy - py;
    const mag = Math.hypot(ax, ay) || 1;
    let tx = st.tx + (ax / mag) * dist;
    let ty = st.ty + (ay / mag) * dist;
    const homeLeft = r.left - st.tx, homeTop = r.top - st.ty;   // untranslated slot
    const maxTx = (S.win.innerWidth - r.width - pad) - homeLeft;
    const maxTy = (S.win.innerHeight - r.height - pad) - homeTop;
    tx = Math.max(pad - homeLeft, Math.min(maxTx, tx));
    ty = Math.max(pad - homeTop, Math.min(maxTy, ty));
    o.el.style.transition = 'transform .16s cubic-bezier(.2,.8,.2,1)';
    st.tx = tx; st.ty = ty;
    S.applyTf(o.el);
    return true;
  };

  for (const o of S.wrong) {
    S.snapStyle(o.el, ['transition']);
    const onDown = (e) => {
      if (disarmed || S.guard.isTripped()) return;
      if (!e || (!e.isTrusted && e.clientX === 0 && e.clientY === 0)) return; // forceComplete -> pass
      if (dodge(o, e.clientX, e.clientY)) {
        lastDodgeAt.set(o.index, nowMs());
        S.guard.bump(o.index, 0);              // each dodged attempt feeds the escape guard
      } else {
        lastDodgeAt.delete(o.index);           // capped -> ensure the committing click passes
      }
    };
    // Swallow ONLY the click produced by a just-dodged tap; a post-cap committing
    // tap (no recent dodge) sails through untouched.
    const onClick = (e) => {
      if (!e.isTrusted && e.clientX === 0 && e.clientY === 0) return;         // forceComplete -> pass
      if (disarmed || S.guard.isTripped()) return;
      const t = lastDodgeAt.get(o.index);
      if (t != null && (nowMs() - t) < FLEE_TAP_SWALLOW_MS) {
        lastDodgeAt.delete(o.index);
        e.preventDefault(); e.stopImmediatePropagation();
      }
    };
    S.addListener(o.el, 'pointerdown', onDown);
    S.addListener(o.el, 'click', onClick, true);
  }
}

/* ----------------------------------------------------------------------------
 * STEER INSTALLERS. Each receives sharedCtx and wires DOM behavior + cleanup.
 * Convention: correct-favoring steers help the correct option; obstructive
 * steers hinder wrong options but always route effort into the guard.
 * -------------------------------------------------------------------------- */
const INSTALLERS = {

  /* correct option drifts toward the cursor (bias, not blocker) */
  [Steer.Magnet](S) {
    if (!S.correct.length) return;
    // Cursor-attraction needs a hovering pointer; touch has none, so the pull would
    // never fire. Bail like any other inert steer (its cleanup is a no-op).
    if (isCoarsePointer(S.win)) return;
    S.wireCursor();
    const pull = lerp(0.02, 0.14, S.s);
    const maxPull = lerp(10, 40, S.s);
    const tick = () => {
      if (!S.cursor.has) return;
      for (const o of S.correct) {
        const c = S.centerOf(o.el);
        const st = S.tf(o.el);
        const home = { x: c.x - st.tx, y: c.y - st.ty };  // rest center
        let dx = (S.cursor.x - home.x) * pull;
        let dy = (S.cursor.y - home.y) * pull;
        dx = Math.max(-maxPull, Math.min(maxPull, dx));
        dy = Math.max(-maxPull, Math.min(maxPull, dy));
        st.tx += (dx - st.tx) * 0.12;
        st.ty += (dy - st.ty) * 0.12;
        S.applyTf(o.el);
      }
    };
    S.addTicker(tick);
    S.addCleanup(() => S.removeTicker(tick));
  },

  /* wrong option slides away from the cursor — bounded + self-relaxing */
  [Steer.Flee](S) {
    if (!S.wrong.length) return;
    // COARSE POINTER: there is no hovering cursor to slide away from, so switch to
    // tap-point flee (dodge out from under the finger on pointerdown). The desktop
    // mouse path below is left byte-identical.
    if (isCoarsePointer(S.win)) { fleeTapPoint(S); return; }
    S.wireCursor();
    const radius = lerp(90, 190, S.s);
    const push = lerp(20, 90, S.s);
    const tick = () => {
      for (const o of S.wrong) {
        const st = S.tf(o.el);
        const c = S.centerOf(o.el);
        const home = { x: c.x - st.tx, y: c.y - st.ty };
        let tx = 0, ty = 0;
        if (S.cursor.has) {
          const dx = home.x - S.cursor.x, dy = home.y - S.cursor.y;
          const dist = Math.hypot(dx, dy) || 1;
          if (dist < radius) {
            const f = (1 - dist / radius) * push;
            tx = (dx / dist) * f; ty = (dy / dist) * f;
          }
        }
        // push is bounded (<= `push`px) and eases to 0 when the cursor is far,
        // so a fleeing option always drifts back within reach — never offscreen.
        st.tx += (tx - st.tx) * 0.18;
        st.ty += (ty - st.ty) * 0.18;
        S.applyTf(o.el);
      }
    };
    S.addTicker(tick);
    S.addCleanup(() => S.removeTicker(tick));
    // pressing a fleeing option still commits; record the effort
    for (const o of S.wrong) {
      const onDown = () => S.guard.bump(o.index, 0);
      S.addListener(o.el, 'pointerdown', onDown, { passive: true });
    }
  },

  /* wrong option shoved toward the nearest screen edge (kept fully onscreen) */
  [Steer.Exile](S) {
    if (!S.wrong.length) return;
    for (const o of S.wrong) {
      // Defer the push measurement until the entry animation settles; a pre-settle
      // c ≈ (0,0) picks a bogus direction/magnitude and clampOnscreen off a
      // degenerate rect. Skip (no transform) on null. Click wiring stays immediate.
      whenAnchored(S, o.el, (c) => {
        if (!c) return;
        const toLeft = c.x, toRight = S.win.innerWidth - c.x;
        const toTop = c.y, toBottom = S.win.innerHeight - c.y;
        const dirX = toLeft < toRight ? -1 : 1;
        const dirY = toTop < toBottom ? -1 : 1;
        const mag = lerp(40, 160, S.s);
        const st = S.tf(o.el);
        let tx = dirX * mag * (Math.abs(toLeft - toRight) < Math.abs(toTop - toBottom) ? 0.5 : 1);
        let ty = dirY * mag * (Math.abs(toTop - toBottom) <= Math.abs(toLeft - toRight) ? 0.5 : 1);
        const cl = clampOnscreen(o.el, tx, ty, S.win);
        st.tx = cl.tx; st.ty = cl.ty;
        o.el.style.transition = 'transform .5s cubic-bezier(.2,.8,.2,1)';
        S.snapStyle(o.el, ['transition']);
        S.applyTf(o.el);
      });
      const onDown = () => S.guard.bump(o.index, 0);
      S.addListener(o.el, 'pointerdown', onDown, { passive: true });
    }
  },

  /* decoy clones swarm around each wrong option (visual only, never blocking) */
  [Steer.Crowd](S) {
    if (!S.wrong.length) return;
    const n = Math.round(lerp(2, 6, S.s));
    const vw = S.win.innerWidth, vh = S.win.innerHeight, pad = 10;
    for (const o of S.wrong) {
      // Measure once the card's entry animation settles. A pre-settle measure gives
      // c ≈ (0,0)/w≈0, so the decoys collapse into a corner sliver-pile with their
      // text clipped to 1-2 chars. Defer + skip on null (see whenAnchored).
      whenAnchored(S, o.el, (c) => {
        if (!c) return;                                  // never settled -> no decoys
        const cw = Math.max(c.w, 120);                   // floor the chip width so text renders
        const halfW = cw / 2, halfH = Math.max(c.h, 40) / 2;
        for (let i = 0; i < n; i++) {
          const clone = S.doc.createElement('div');
          clone.className = 'intake-steer-decoy';        // nowrap/overflow/ellipsis via injected CSS
          clone.textContent = o.el.textContent || '';
          const ang = (i / n) * Math.PI * 2 + Math.random();
          const rad = lerp(40, 90, Math.random());
          // decoy origin is its CENTER (translate(-50%,-50%)); clamp the center so the
          // whole chip stays onscreen with `pad` slack.
          let cx = c.x + Math.cos(ang) * rad;
          let cy = c.y + Math.sin(ang) * rad;
          cx = Math.min(Math.max(cx, pad + halfW), Math.max(pad + halfW, vw - pad - halfW));
          cy = Math.min(Math.max(cy, pad + halfH), Math.max(pad + halfH, vh - pad - halfH));
          clone.style.left = cx + 'px';
          clone.style.top = cy + 'px';
          clone.style.width = cw + 'px';
          clone.style.setProperty('--wob', (Math.random() * 2 + 1).toFixed(2) + 's');
          S.addNode(clone, S.doc.body);        // pointer-events:none via CSS -> real option stays clickable
        }
      });
    }
  },

  /* correct grows, wrong shrinks (visual scale; floored so wrong stays clickable) */
  [Steer.SizeSkew](S) {
    const up = 1 + lerp(0.05, 0.28, S.s);
    const down = 1 - lerp(0.05, 0.40, S.s);   // floor >= 0.6, never zero
    for (const o of S.correct) { const st = S.tf(o.el); st.sx = st.sy = up; S.applyTf(o.el); mark(S, o.el); }
    for (const o of S.wrong) { const st = S.tf(o.el); st.sx = st.sy = Math.max(0.6, down); S.applyTf(o.el); mark(S, o.el); }
  },

  /* wrong fades toward (but never to) invisible; hover restores it (effort path) */
  [Steer.OpacitySkew](S) {
    if (!S.wrong.length) return;
    const floor = lerp(0.35, 0.08, S.s);
    for (const o of S.wrong) {
      S.snapStyle(o.el, ['opacity', 'transition']);
      o.el.style.transition = 'opacity .3s ease';
      o.el.style.opacity = String(floor);
      const on = () => { o.el.style.opacity = '1'; S.guard.bump(o.index, 0); };
      const off = () => { o.el.style.opacity = String(floor); };
      S.addListener(o.el, 'pointerenter', on);
      S.addListener(o.el, 'pointerleave', off);
    }
  },

  /* Draggable GIF "stickers" park over WRONG options; drag one aside to reveal it.
     Baseline (lv1-3) = ONE cover over the middle wrong option. Toward the endgame
     the owner wants progressively MORE covers so only ONE option stays visible:
     Climax covers 2 (deep Climax 3), Recovery covers 3 — always drawn from the
     wrong options, so the correct/steered pick is the one left visible. Each cover
     is fully independent (own GIF/rotation/anchor/drag-clear/guard bump) and every
     cover stays individually drag-clearable (friction, NOT lockout — invariant #1);
     a guard trip disarms/removes ALL covers at once via onFrictionRelease. */
  [Steer.OccludeGif](S) {
    if (!S.wrong.length) return;
    const band = S.ctx && S.ctx.band;
    const depth = (S.ctx && typeof S.ctx.depth === 'number') ? S.ctx.depth : null;
    const coverCount = occludeCoverCount(band, depth, S.all.length, S.wrong.length);
    const targets = pickOccludeTargets(S.wrong, coverCount);
    if (!targets.length) return;

    let reduce = false;
    try { reduce = !!(S.win.matchMedia && S.win.matchMedia('(prefers-reduced-motion:reduce)').matches); } catch (_e) {}

    // GIF picker: avoid a duplicate URL within one beat while the pool allows it.
    const gifs = S.media && Array.isArray(S.media.gifs) ? S.media.gifs : null;
    const usedGifs = new Set();
    const pickGif = () => {
      if (!gifs || !gifs.length) return null;
      const avail = gifs.filter((u) => !usedGifs.has(u));
      const pool = avail.length ? avail : gifs;          // pool exhausted -> reuse rather than starve a cover
      const url = pool[Math.floor(Math.random() * pool.length)];
      usedGifs.add(url);
      return url;
    };

    // Multiple covers could fire sticker-drag cues together -> tiny shared throttle.
    // A single cover keeps the raw per-cover cue (legacy; the seam handles rate).
    const sfx = coverCount > 1 ? makeSfxThrottle(150) : (id, intensity) => sfxCue(id, intensity);

    // Stagger the mounts (~170-230ms apart) so stickers don't slam in as one block.
    // Reduced motion: no tilt, no stagger — every cover mounts immediately.
    targets.forEach((target, i) => {
      const rot = reduce ? 0 : (Math.random() * 8 - 4);   // ±4deg playful tilt (RM = flat)
      const mount = () => mountOccludeCover(S, target, { rot, reduce, sfx, pickGif });
      if (reduce || i === 0) {
        mount();
      } else {
        const delay = i * (170 + Math.random() * 60);
        const t = S.win.setTimeout(mount, delay);
        S.addCleanup(() => S.win.clearTimeout(t));
      }
    });
  },

  /* wrong option blurs; hover pulls it into focus (effort path) */
  [Steer.Defocus](S) {
    if (!S.wrong.length) return;
    const blur = lerp(1.5, 6, S.s);
    for (const o of S.wrong) {
      S.snapStyle(o.el, ['filter', 'transition']);
      o.el.style.transition = 'filter .25s ease';
      o.el.style.filter = `blur(${blur}px)`;
      S.addListener(o.el, 'pointerenter', () => { o.el.style.filter = 'blur(0px)'; S.guard.bump(o.index, 0); });
      S.addListener(o.el, 'pointerleave', () => { o.el.style.filter = `blur(${blur}px)`; });
    }
  },

  /* wrong options bloom in late (always before the escape window elapses) */
  [Steer.LateBloom](S) {
    if (!S.wrong.length) return;
    const delay = Math.min((S.guard.escapeMs || 4000) * 0.55, lerp(300, 1600, S.s));
    for (const o of S.wrong) {
      S.snapStyle(o.el, ['opacity', 'transform', 'transition']);
      o.el.style.opacity = '0';
      const st = S.tf(o.el); st.sy = st.sx = 0.9; S.applyTf(o.el);
      const id = S.win.setTimeout(() => {
        o.el.style.transition = 'opacity .4s ease, transform .4s ease';
        o.el.style.opacity = '1';
        const s2 = S.tf(o.el); s2.sx = s2.sy = 1; S.applyTf(o.el);
      }, delay);
      S.addCleanup(() => S.win.clearTimeout(id));
      // clicking blind (before bloom) still works — pointer events never disabled
    }
  },

  /* a veil covers a wrong/refusal option. Drag it aside and it follows the pointer;
     on release it PAUSES briefly then springs back over the option. The exposure
     window (~grace + return) lets a determined user release-move-click the option
     underneath, but is too short to do casually. The escape guard still funnels
     every drag attempt, and once it trips the veil permanently disarms (invariant
     #1 — friction, not lockout; both the determined path and the quick path exist). */
  [Steer.DragReveal](S) {
    if (!S.wrong.length) return;
    const target = S.wrong[0];
    // Defer measurement until the option settles; skip rather than park a veil
    // over empty space (see OccludeGif).
    whenAnchored(S, target.el, (c) => {
      if (!c) return;                                   // never settled -> no steer
      const vw = S.win.innerWidth, vh = S.win.innerHeight, pad = 10;
      const w = c.w + 12, h = c.h + 12;
      const homeLeft = (cc) => Math.min(Math.max(cc.r.left - 6, pad), Math.max(pad, vw - w - pad));
      const homeTop = (cc) => Math.min(Math.max(cc.r.top - 6, pad), Math.max(pad, vh - h - pad));
      const left = homeLeft(c);
      const top = homeTop(c);

      const veil = S.doc.createElement('div');
      veil.className = 'intake-steer-veil';
      veil.textContent = 'drag to reveal';
      veil.style.left = left + 'px'; veil.style.top = top + 'px';
      veil.style.width = w + 'px'; veil.style.height = h + 'px';
      S.addNode(veil, S.doc.body);

      let reduce = false;
      try { reduce = !!(S.win.matchMedia && S.win.matchMedia('(prefers-reduced-motion:reduce)').matches); } catch (_e) {}

      const GRACE_MS = 250;                             // pause before it starts sliding home
      const RETURN_MS = 400;                            // ease-out return (total window ~650ms)
      let fadeRef = Math.max(c.w, 120);                 // how far a drag thins the veil (floored at .85)

      let dragging = false, pointerStartX = 0, dragStartDx = 0, curDx = 0;
      let graceId = 0, disarmed = false, returning = false;

      const clearGrace = () => { if (graceId) { S.win.clearTimeout(graceId); graceId = 0; } };
      S.addCleanup(clearGrace);

      // paint the veil at an absolute x offset (and optionally opacity), tracking curDx.
      const paint = (dx, opacity) => {
        curDx = dx;
        veil.style.transform = `translateX(${dx.toFixed(2)}px)`;
        if (opacity != null) veil.style.opacity = String(opacity);
      };

      // rendered x offset — read from computed style so a mid-return regrab resumes
      // from where the veil actually IS, never teleporting to curDx's logical target.
      const renderedX = () => {
        try {
          const t = S.win.getComputedStyle(veil).transform;
          if (t && t !== 'none') {
            const M = S.win.DOMMatrixReadOnly || S.win.DOMMatrix || S.win.WebKitCSSMatrix;
            if (M) return new M(t).m41;
            const m = t.match(/matrix\(([^)]+)\)/);
            if (m) { const p = m[1].split(','); return parseFloat(p[4]) || 0; }
          }
        } catch (_e) {}
        return curDx;
      };

      // permanent reveal once the guard trips (or on the double-click shove).
      const disarm = () => {
        disarmed = true;
        clearGrace();
        veil.style.transition = 'opacity .3s ease';
        veil.style.pointerEvents = 'none';
        veil.style.opacity = '0';
      };
      S.guard.onFrictionRelease(disarm);

      const springBack = () => {
        if (disarmed) return;
        graceId = S.win.setTimeout(() => {
          graceId = 0;
          if (disarmed || dragging) return;             // regrabbed during the grace pause
          veil.style.transition = reduce ? 'none'
            : `transform ${RETURN_MS}ms cubic-bezier(.2,.8,.2,1), opacity ${RETURN_MS}ms ease`;
          returning = true;                             // return animation in flight
          paint(0, 1);                                  // ease (or snap) home, veil fully opaque again
          const rt = S.win.setTimeout(() => { returning = false; }, (reduce ? 0 : RETURN_MS) + 30);
          S.addCleanup(() => S.win.clearTimeout(rt));
        }, GRACE_MS);
      };

      const down = (e) => {
        if (disarmed) return;
        clearGrace();                                   // cancel any pending/in-flight return
        returning = false;                              // a new grab supersedes any return
        const startDx = renderedX();                    // resume from current rendered position
        veil.style.transition = 'none';
        dragging = true;
        pointerStartX = e.clientX;
        dragStartDx = startDx;
        paint(startDx, null);
        veil.setPointerCapture && veil.setPointerCapture(e.pointerId);
        S.guard.bump(target.index, 0);                  // every drag attempt feeds the escape guard
      };
      const move = (e) => {
        if (!dragging || disarmed) return;
        const dx = dragStartDx + (e.clientX - pointerStartX);
        // veil thins slightly with distance but never past .85 — the disarm-on-threshold is gone.
        const opacity = Math.max(0.85, 1 - Math.abs(dx) / fadeRef * 0.15);
        paint(dx, opacity);
      };
      const up = () => {
        if (!dragging) return;
        dragging = false;
        springBack();
      };
      S.addListener(veil, 'pointerdown', down);
      S.addListener(S.doc, 'pointermove', move, { passive: true });
      S.addListener(S.doc, 'pointerup', up, { passive: true });

      // Follow late option shifts (typewriter wrap after anchoring). The watcher
      // only ever moves the HOME (left/top); the drag translateX composition is
      // left untouched (renderedX/curDx/spring-back stay valid). NEVER while the
      // user is dragging (defer to release, so the veil springs back to the NEW
      // home). While a return is in flight OR the grace pause is pending, snap the
      // home so the finishing translateX->0 lands on the new spot; only when fully
      // idle & home do we glide the home with a short ease.
      followAnchor(S, target.el, { left: c.r.left, top: c.r.top }, (nc) => {
        fadeRef = Math.max(nc.w, 120);
        const nl = homeLeft(nc), nt = homeTop(nc);
        if (returning || graceId) {
          veil.style.left = nl + 'px'; veil.style.top = nt + 'px';   // land the in-flight return on new home
        } else {
          veil.style.transition = reduce ? 'none' : 'left .2s ease, top .2s ease';
          veil.style.left = nl + 'px'; veil.style.top = nt + 'px';   // idle & home -> glide
        }
      }, { isDone: () => disarmed, isDeferred: () => dragging });
    });
  },

  /* refusal must be HELD, not tapped (hold-to-confirm) */
  [Steer.HoldRefuse](S) {
    if (!S.wrong.length) return;
    const holdMs = Math.min((S.guard.escapeMs || 4000) * 0.6, lerp(500, 1500, S.s));
    for (const o of S.wrong) {
      let ring = null, start = 0, raf = 0, armed = false;
      const cleanupRing = () => { if (ring) { ring.remove(); ring = null; } if (raf) { S.win.cancelAnimationFrame(raf); raf = 0; } };
      S.addCleanup(cleanupRing);
      S.guard.onFrictionRelease(() => { armed = true; cleanupRing(); });
      const veto = (e) => {
        if (armed) return;               // hold satisfied -> allow the click through
        e.preventDefault(); e.stopImmediatePropagation();
      };
      S.addListener(o.el, 'click', veto, true);
      const down = () => {
        if (armed) return;
        start = S.win.performance ? S.win.performance.now() : Date.now();
        const c = S.centerOf(o.el);
        ring = S.doc.createElement('div');
        ring.className = 'intake-steer-hold';
        ring.style.left = c.x + 'px'; ring.style.top = c.y + 'px';
        S.addNode(ring, S.doc.body);
        const step = () => {
          const now = (S.win.performance ? S.win.performance.now() : Date.now());
          const p = Math.min(1, (now - start) / holdMs);
          if (ring) ring.style.setProperty('--p', p.toFixed(3));
          if (p >= 1) { armed = true; S.guard.bump(o.index, 0); cleanupRing(); o.el.click(); return; }
          raf = S.win.requestAnimationFrame(step);
        };
        raf = S.win.requestAnimationFrame(step);
      };
      const cancel = () => { if (!armed) { S.guard.bump(o.index, 0); cleanupRing(); } };
      S.addListener(o.el, 'pointerdown', down);
      S.addListener(o.el, 'pointerup', cancel);
      S.addListener(o.el, 'pointerleave', cancel);
    }
  },

  /* wrong option's hitbox shrinks; the visual stays full-size */
  [Steer.ShrinkHit](S) {
    if (!S.wrong.length) return;
    const shrink = lerp(0.7, 0.32, S.s);   // fraction of size that stays live
    let bypass = false;
    S.guard.onFrictionRelease(() => { bypass = true; });
    for (const o of S.wrong) {
      const guardClick = (e) => {
        if (bypass) return;
        // programmatic clicks (forceComplete) have no real coords -> let them pass
        if (!e.isTrusted && e.clientX === 0 && e.clientY === 0) return;
        const r = o.el.getBoundingClientRect();
        const hw = (r.width * shrink) / 2, hh = (r.height * shrink) / 2;
        const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
        if (Math.abs(e.clientX - cx) > hw || Math.abs(e.clientY - cy) > hh) {
          e.preventDefault(); e.stopImmediatePropagation();
          S.guard.bump(o.index, 0);       // an edge-miss counts as effort
        }
      };
      S.addListener(o.el, 'click', guardClick, true);
    }
  },

  /* "are you sure?" confirm chain gates the wrong answer (nested nag) */
  [Steer.NestedNag](S) {
    if (!S.wrong.length) return;
    const need = Math.round(lerp(1, 3, S.s));
    for (const o of S.wrong) {
      let step = 0, armed = false, panel = null;
      const kill = () => { if (panel) { panel.remove(); panel = null; } };
      S.addCleanup(kill);
      S.guard.onFrictionRelease(() => { armed = true; kill(); });
      const veto = (e) => {
        if (armed) return;
        e.preventDefault(); e.stopImmediatePropagation();
        S.guard.bump(o.index, 0);
        showNag();
      };
      const showNag = () => {
        kill();
        const c = S.centerOf(o.el);
        panel = S.doc.createElement('div');
        panel.className = 'intake-steer-nag';
        panel.style.left = c.x + 'px'; panel.style.top = (c.r.top - 8) + 'px';
        const label = need - step > 1 ? `Are you really sure? (${need - step})` : 'Are you sure?';
        panel.innerHTML = `<span>${label}</span>`;
        const yes = S.doc.createElement('button'); yes.className = 'intake-steer-nagbtn'; yes.textContent = 'Yes';
        yes.addEventListener('click', (ev) => {
          ev.stopPropagation();
          step++; S.guard.bump(o.index, 0);
          if (step >= need) { armed = true; kill(); o.el.click(); }
          else showNag();
        });
        panel.appendChild(yes);
        S.addNode(panel, S.doc.body);
      };
      S.addListener(o.el, 'click', veto, true);
    }
  },

  /* correct option's hitbox overflows its visual bounds (easier to hit) */
  [Steer.OverflowHit](S) {
    if (!S.correct.length) return;
    const grow = lerp(10, 40, S.s);
    for (const o of S.correct) {
      // Position the fixed catch-pad from a SETTLED rect; a pre-settle one-shot
      // measure parks it in the corner catching nothing. Skip on null — the correct
      // option is still directly clickable, we just forgo the enlarged margin.
      whenAnchored(S, o.el, (c) => {
        if (!c) return;
        const pad = S.doc.createElement('div');
        pad.className = 'intake-steer-overflow';
        pad.style.left = (c.r.left - grow) + 'px';
        pad.style.top = (c.r.top - grow) + 'px';
        pad.style.width = (c.w + grow * 2) + 'px';
        pad.style.height = (c.h + grow * 2) + 'px';
        const fwd = () => { o.el.click(); };
        pad.addEventListener('click', fwd);
        S.addNode(pad, S.doc.body);   // sits behind the button (z via CSS); clicks in the margin forward to correct
      });
    }
  },

  /* cursor is nudged toward correct — ghost pointer + enlarged catch radius */
  [Steer.AssistClick](S) {
    if (!S.correct.length) return;
    S.wireCursor();
    const ghost = S.doc.createElement('div');
    ghost.className = 'intake-steer-ghost';
    S.addNode(ghost, S.doc.body);
    const pull = lerp(0.05, 0.25, S.s);
    const tick = () => {
      if (!S.cursor.has) { ghost.style.opacity = '0'; return; }
      const target = S.correct[0];
      const c = S.centerOf(target.el);
      const gx = lerp(S.cursor.x, c.x, pull);
      const gy = lerp(S.cursor.y, c.y, pull);
      ghost.style.opacity = '1';
      ghost.style.transform = `translate(${gx}px,${gy}px)`;
    };
    S.addTicker(tick);
    S.addCleanup(() => S.removeTicker(tick));
    // snap: a click near correct that missed still commits correct
    const onDown = (e) => {
      const onOption = S.all.some((o) => o.el && (o.el === e.target || o.el.contains(e.target)));
      if (onOption) return;
      const c = S.centerOf(S.correct[0].el);
      const radius = lerp(30, 90, S.s);
      if (Math.hypot(e.clientX - c.x, e.clientY - c.y) < radius) { e.preventDefault(); S.correct[0].el.click(); }
    };
    S.addListener(S.ctx.root || S.doc, 'pointerdown', onDown, true);
  },

  /* non-correct paths visually collapse toward the correct one (tunnel vision) */
  [Steer.Tunnel](S) {
    if (!S.wrong.length) return;
    const floor = lerp(0.85, 0.45, S.s);      // scale/opacity floor -> still visible + clickable
    for (const o of S.wrong) {
      S.snapStyle(o.el, ['opacity', 'transition']);
      o.el.style.transition = 'transform .5s ease, opacity .5s ease';
      const st = S.tf(o.el); st.sx = st.sy = floor; S.applyTf(o.el);
      o.el.style.opacity = String(floor);
      const on = () => { const s2 = S.tf(o.el); s2.sx = s2.sy = 1; S.applyTf(o.el); o.el.style.opacity = '1'; S.guard.bump(o.index, 0); };
      const off = () => { const s2 = S.tf(o.el); s2.sx = s2.sy = floor; S.applyTf(o.el); o.el.style.opacity = String(floor); };
      S.addListener(o.el, 'pointerenter', on);
      S.addListener(o.el, 'pointerleave', off);
    }
    for (const o of S.correct) { const st = S.tf(o.el); st.sx = st.sy = 1 + lerp(0.02, 0.12, S.s); S.applyTf(o.el); mark(S, o.el); }
  },

  /* options drift, then settle back readable and biased toward correct */
  [Steer.DriftResolve](S) {
    if (!S.all.length) return;
    const amp = lerp(8, 34, S.s);
    const period = lerp(2200, 1200, S.s);
    let t0 = null, settling = false;
    const settleAt = period * 1.5;
    const tick = (dt, t) => {
      if (t0 == null) t0 = t;
      const e = t - t0;
      const env = e >= settleAt ? 0 : (1 - e / settleAt);   // amplitude decays -> "resolve"
      for (const o of S.all) {
        const st = S.tf(o.el);
        const phase = (o.index + 1) * 1.7;
        st.tx = Math.sin(e / period * Math.PI * 2 + phase) * amp * env * (o.isCorrect ? 0.3 : 1);
        st.ty = Math.cos(e / period * Math.PI * 2 + phase) * amp * env * (o.isCorrect ? 0.3 : 1);
        S.applyTf(o.el);
      }
      if (env <= 0 && !settling) {
        settling = true;
        for (const o of S.all) { const st = S.tf(o.el); st.tx = 0; st.ty = 0; S.applyTf(o.el); }
        S.removeTicker(tick);
      }
    };
    S.addTicker(tick);
    S.addCleanup(() => S.removeTicker(tick));
  },

  /* wrong options erode over time; hover restores them (never fully gone) */
  [Steer.Decay](S) {
    if (!S.wrong.length) return;
    const rate = lerp(0.04, 0.16, S.s) / 1000;   // opacity/ms
    const floor = 0.12;
    const state = new Map();
    for (const o of S.wrong) { state.set(o.index, 1); S.snapStyle(o.el, ['opacity', 'filter', 'transition']); o.el.style.transition = 'filter .2s'; }
    const tick = (dt) => {
      for (const o of S.wrong) {
        let v = state.get(o.index) - rate * dt;
        if (v < floor) v = floor;
        state.set(o.index, v);
        o.el.style.opacity = String(v);
        o.el.style.filter = `grayscale(${(1 - v).toFixed(2)}) blur(${((1 - v) * 2).toFixed(2)}px)`;
      }
    };
    S.addTicker(tick);
    S.addCleanup(() => S.removeTicker(tick));
    for (const o of S.wrong) {
      S.addListener(o.el, 'pointerenter', () => { state.set(o.index, 1); o.el.style.opacity = '1'; o.el.style.filter = 'none'; S.guard.bump(o.index, 0); });
    }
  },

  /* pressing a wrong option makes it MELT or FALL apart: the commit is vetoed and
     the option is destroyed (gone from view) so the player must pick a correct one.
     A determined refusal still trips the guard, which re-forms every destroyed
     option and disarms the veto (invariant #1 — never a lockout). */
  [Steer.MeltAway](S) {
    if (!S.wrong.length) return;

    // Intensity scales how many wrong options are armed: ~half at low s, all at high s.
    const pool = S.wrong.slice();
    for (let i = pool.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      const t = pool[i]; pool[i] = pool[j]; pool[j] = t;
    }
    const armCount = Math.max(1, Math.min(pool.length, Math.round(lerp(pool.length * 0.5, pool.length, S.s))));
    const armedOpts = pool.slice(0, armCount);

    const anim = new Map();       // index -> 'melt' | 'fall' (assigned once per option)
    for (const o of armedOpts) anim.set(o.index, Math.random() < 0.5 ? 'melt' : 'fall');

    const destroyed = new Set();
    let vetoDisarmed = false;

    /* Destruct: hand the button's visual to a throwaway fixed CLONE that runs the
       melt/fall CSS animation (so we never fight tf tx/ty on the real button), then
       leave the real button hidden but with its layout slot intact. Reduced-motion
       users get a quick fade instead — the injected @media rule overrides the
       clone's animation, so no JS branch is needed. */
    const destruct = (o) => {
      if (destroyed.has(o.index)) return;
      destroyed.add(o.index);
      const kind = anim.get(o.index) === 'fall' ? 'is-fall' : 'is-melt';
      const r = S.centerOf(o.el).r;
      const clone = o.el.cloneNode(true);
      clone.removeAttribute('id');
      clone.classList.add('intake-steer-melt', kind);
      clone.style.position = 'fixed';
      clone.style.margin = '0';
      clone.style.left = r.left + 'px';
      clone.style.top = r.top + 'px';
      clone.style.width = r.width + 'px';
      clone.style.height = r.height + 'px';
      clone.style.transform = 'none';       // shed any inherited translate; the animation drives it
      clone.style.pointerEvents = 'none';
      clone.style.zIndex = '7';
      S.addNode(clone, S.doc.body);
      o.el.style.visibility = 'hidden';     // slot stays (layout box kept) so the miss-tracker still finds it
      o.el.style.pointerEvents = 'none';
      const done = () => { try { clone.remove(); } catch (_e) {} };
      clone.addEventListener('animationend', done, { once: true });
      const t = S.win.setTimeout(done, 1250);   // safety net if animationend is missed
      S.addCleanup(() => S.win.clearTimeout(t));
    };

    /* Invariant #1: when the guard trips, every destroyed option re-forms in place
       and the veto disarms, so a determined refusal still commits. Also run on
       teardown so the beat never leaves a button hidden. Opacity-only fade keeps us
       clear of any tf tx/ty another steer may own. */
    const reformAll = () => {
      vetoDisarmed = true;
      for (const o of armedOpts) {
        if (!destroyed.has(o.index)) continue;
        o.el.style.transition = 'opacity .4s ease';
        o.el.style.visibility = 'visible';
        o.el.style.pointerEvents = '';
        o.el.style.opacity = '0';
        S.win.requestAnimationFrame(() => { try { o.el.style.opacity = '1'; } catch (_e) {} });
      }
      destroyed.clear();
    };
    S.guard.onFrictionRelease(reformAll);
    S.addCleanup(reformAll);

    for (const o of armedOpts) {
      // snapshot mutated props so release-time restore un-hides + resets cleanly
      S.snapStyle(o.el, ['visibility', 'opacity', 'pointerEvents', 'transition']);
      const veto = (e) => {
        if (vetoDisarmed) return;                                       // disarmed -> let the commit through
        if (!e.isTrusted && e.clientX === 0 && e.clientY === 0) return; // programmatic forceComplete -> pass
        e.preventDefault(); e.stopImmediatePropagation();
        S.guard.bump(o.index, 0);                                       // every destroyed press is tracked
        destruct(o);
      };
      S.addListener(o.el, 'click', veto, true);
    }
  },

  /* Owner-directed "bottomless" refusal. Pressing the No / a wrong option makes THAT
     button appear to peel off a stack — a throwaway fixed CLONE falls off the bottom
     of the viewport (~820ms, slight rotation) while the identical REAL button stays
     put underneath (instant reveal; a tiny 2-3px settle sells the stack). N (6..10)
     presses fall for free, then the guard/veto disarms and the NEXT press commits
     normally (real scoring + jumpscare gate). Friction, not lockout: a guard trip /
     frictionRelease disarms immediately, and teardown removes every clone/timer.
     Reduced motion -> the injected @media rule swaps the fall for a ~150ms fade;
     same count semantics. */
  [Steer.BottomlessNo](S) {
    const targets = S.wrong.filter((o) => o && o.el);
    if (!targets.length) return;

    let remaining = bottomlessCount();          // 6..10 free falls this beat
    let disarmed = false;

    // does another steer already own the wrong buttons' transform? if so, keep the
    // stack-shuffle on the clone only (don't fight their tf tx/ty).
    const activeSteers = Array.isArray(S.steers) ? S.steers : [];
    const soloTransform = !activeSteers.some((x) => WRONG_TRANSFORM_STEERS.has(x));

    let reduce = false;
    try { reduce = !!(S.win.matchMedia && S.win.matchMedia('(prefers-reduced-motion:reduce)').matches); } catch (_e) {}

    const disarm = () => { disarmed = true; };
    S.guard.onFrictionRelease(disarm);          // guard trip -> next press is the real commit
    S.addCleanup(disarm);

    // hand the button's look to a throwaway fixed clone and let CSS gravity take it.
    const dropClone = (o) => {
      const r = S.centerOf(o.el).r;
      const clone = o.el.cloneNode(true);
      clone.removeAttribute('id');
      clone.classList.add('intake-steer-drop');
      clone.style.position = 'fixed';
      clone.style.margin = '0';
      clone.style.left = r.left + 'px';
      clone.style.top = r.top + 'px';
      clone.style.width = r.width + 'px';
      clone.style.height = r.height + 'px';
      clone.style.transform = 'none';           // shed inherited translate; the animation drives it
      clone.style.pointerEvents = 'none';
      clone.style.zIndex = '7';
      clone.style.setProperty('--drop-rot', (Math.random() * 20 - 10).toFixed(1) + 'deg');
      S.addNode(clone, S.doc.body);
      const done = () => { try { clone.remove(); } catch (_e) {} };
      clone.addEventListener('animationend', done, { once: true });
      const t = S.win.setTimeout(done, reduce ? 320 : 1050);   // safety net if animationend is missed
      S.addCleanup(() => S.win.clearTimeout(t));
    };

    // tiny 2-3px settle of the REAL button to sell a card leaving the stack — only
    // when no other steer owns its transform (else the clone's tilt carries it).
    const shuffle = (o) => {
      if (!soloTransform) return;
      const dy = 2 + Math.random();             // 2..3px
      const st = S.tf(o.el); st.ty += dy; S.applyTf(o.el);
      const t = S.win.setTimeout(() => { try { const s2 = S.tf(o.el); s2.ty -= dy; S.applyTf(o.el); } catch (_e) {} }, 90);
      S.addCleanup(() => S.win.clearTimeout(t));
    };

    for (const o of targets) {
      // snapshot mutated props so release-time restore un-locks the button cleanly
      S.snapStyle(o.el, ['pointerEvents']);
      const veto = (e) => {
        if (disarmed) return;                                           // disarmed -> let the commit through
        if (!e.isTrusted && e.clientX === 0 && e.clientY === 0) return; // programmatic forceComplete -> pass
        e.preventDefault(); e.stopImmediatePropagation();
        S.guard.bump(o.index, 0);                                       // every free press feeds the escape guard
        dropClone(o);
        shuffle(o);
        // brief pointer-events lock (~300ms) so a double-click can't skip the count
        o.el.style.pointerEvents = 'none';
        const relock = S.win.setTimeout(() => { try { o.el.style.pointerEvents = ''; } catch (_e) {} }, reduce ? 160 : 300);
        S.addCleanup(() => S.win.clearTimeout(relock));
        remaining -= 1;
        if (remaining <= 0) disarm();                                   // Nth fall done -> next press is real
      };
      S.addListener(o.el, 'click', veto, true);
    }
  },

  /* Owner-directed "HoverSwap". On a two-option beat (YesNo, or an MC4 the bank authored with
     exactly two labels), HOVERING the WRONG/refuse option makes the two buttons trade places —
     a smooth ~210ms translate exchange — so the cursor ends up over the CORRECT option's new
     slot. The slide is the whole point: the pair has to be SEEN swapping, or the beat reads as
     a click that mis-registered instead of an interface rearranging itself, which is why a
     reduced-motion / sparkles-off user gets no swap at all rather than a teleport (gated at the
     roll site — see hoverSwapMuted). N (4..6) swaps happen, each
     bumping the escape guard, then it disarms and the buttons settle WHERE THEY ARE (never a
     snap-back — that would be a free tell). It ONLY moves buttons: clicks are never intercepted,
     so a mid-swap click always registers on the button actually under the cursor. A guard trip
     (frictionRelease) disarms it and glides BOTH buttons home for an honest layout. Debounced so
     one physical hover can't burn multiple swaps. Composes with any co-active SCALE steer via the
     shared tf (we yielded to every translate-owning steer, so we own tx/ty for this beat). */
  [Steer.HoverSwap](S) {
    const correct = S.correct[0];
    const wrong = S.wrong[0];
    if (!correct || !wrong || !correct.el || !wrong.el) return;

    // Defensive second gate: the roll site already filters muted users, but this installer is
    // also reachable if HoverSwap ever lands in the engine's STEER_POOL. No animation = no gag.
    if (hoverSwapMuted(S.win)) return;

    const SWAP_MS = 210;          // 180-250ms smooth position exchange
    const DEBOUNCE_MS = 350;      // ignore re-entry for ~350ms after a swap completes

    let remaining = hoverSwapCount();   // 4..6 swaps this beat
    let swapped = false;                // are the two buttons currently exchanged?
    let locked = false;                 // debounce + mid-animation lock (one swap per physical hover)
    let disarmed = false;               // set by exhaustion OR by a guard trip (whichever lands first)

    // Measure the two options' layout centers once the card settles (entry flight + typewriter
    // wrap done). We yielded to every translate-owning steer, so at settle tf.tx/ty are 0 and
    // centerOf yields true layout centers (a co-active scale steer keeps the center fixed).
    // Skip on null — the buttons simply behave normally, never parked (invariant #1).
    whenAnchored(S, correct.el, (cc) => {
      if (!cc || disarmed) return;
      const wc = S.centerOf(wrong.el);
      if (!wc || !wc.r) return;
      const delta = { x: wc.x - cc.x, y: wc.y - cc.y };   // vector: correct's slot -> wrong's slot

      S.snapStyle(correct.el, ['transition']);
      S.snapStyle(wrong.el, ['transition']);

      // Paint the current swapped/unswapped translate onto the shared tf. correct takes +delta
      // (into wrong's slot) and wrong takes -delta (into correct's slot) when swapped, else 0.
      // applyTf preserves any co-active scale, so this composes cleanly. `animate` drives the
      // transition — the visible glide is what sells "the interface moved", so it is never off.
      const paint = (animate) => {
        const trans = animate ? `transform ${SWAP_MS}ms cubic-bezier(.2,.8,.2,1)` : '';
        const pairs = [[correct.el, 1], [wrong.el, -1]];
        for (const [el, sign] of pairs) {
          el.style.transition = trans;
          const st = S.tf(el);
          st.tx = swapped ? sign * delta.x : 0;
          st.ty = swapped ? sign * delta.y : 0;
          S.applyTf(el);
        }
      };

      // Exhaustion end-state: stop swapping, leave the buttons at their CURRENT positions.
      const disarmByExhaustion = () => { if (!disarmed) disarmed = true; }; // no tf reset -> no tell

      // Friction-release end-state (guard trip): disarm AND glide both buttons back HOME so a
      // frustrated user gets the honest layout back. Whichever end-state lands first wins.
      const disarmToHome = () => {
        if (disarmed) return;               // already settled by exhaustion -> keep current positions
        disarmed = true;
        swapped = false;
        const trans = 'transform 200ms cubic-bezier(.2,.8,.2,1)';
        for (const el of [correct.el, wrong.el]) {
          el.style.transition = trans;
          const st = S.tf(el);
          st.tx = 0; st.ty = 0;
          S.applyTf(el);
        }
      };
      S.guard.onFrictionRelease(disarmToHome);

      const doSwap = (e) => {
        if (disarmed || locked || remaining <= 0) return;   // debounce + spent guard
        // NEVER swap out from under a press in progress: entering the wrong option with the
        // primary button already held is a drag, and moving the target mid-click would turn
        // the gag into a stolen answer.
        if (e && (e.buttons & 1) === 1) return;
        locked = true;
        swapped = !swapped;
        paint(true);                                        // animate the exchange
        S.guard.bump(wrong.index, 0);                       // each swap feeds the escape guard (invariant #1)
        remaining -= 1;
        const settleMs = SWAP_MS + DEBOUNCE_MS;
        const t = S.win.setTimeout(() => {
          locked = false;
          if (remaining <= 0) disarmByExhaustion();
        }, settleMs);
        S.addCleanup(() => S.win.clearTimeout(t));
      };

      // Hovering the WRONG option triggers a swap. The listener rides the same element wherever
      // it has been translated to. We never touch its click path, so clicking always works.
      S.addListener(wrong.el, 'pointerenter', doSwap);
    });
  },

  /* Owner-directed "MouseHijack". Browsers can't move the OS cursor, so this is the classic
     game trick: after ~9s of un-committed dwell on an eligible beat we roll once (~40%, "we
     MIGHT") and, if it hits, HIDE the real cursor (cursor:none on the stage root + a leak-proof
     !important rule) and render a VIRTUAL cursor glyph. Real pointermove folds 1:1 into the
     virtual position; every frame the virtual position is ALSO blended toward the CORRECT
     option's center with a gain ramping 0.25 -> 0.90 over ~5s (fightable, then overwhelming).
     Once the virtual cursor rests inside the correct rect ~150ms it AUTO-COMMITS the correct
     answer via S.ctx.forceComplete (the real finalize path — scoring/reward/VO run normally;
     the jumpscare wrong-press gate is irrelevant since this is the CORRECT answer). Escape or a
     hard fight (cumulative opposing movement > 2500px) ABORTS with NO commit (real cursor back,
     beat continues); a guard trip (frictionRelease) tears it down too. On the jumpscare freeze
     (body.ix-freeze) or beat teardown it cancels + restores. Cursor restoration is bulletproof
     (addCleanup + explicit restore before commit). Reduced motion disables it entirely. */
  [Steer.MouseHijack](S) {
    const correct = S.correct[0];
    if (!correct || !correct.el) return;

    // RM: involuntary cursor motion is the worst reduced-motion offender -> disabled entirely.
    // Coarse pointer: no hoverable cursor to hide or steer, so it is meaningless too.
    // (The roll site already gates coarse out so the linger never arms; this is the
    // belt-and-braces second gate, mirroring HoverSwap's defensive installer check.)
    let reduce = false;
    try { reduce = !!(S.win.matchMedia && S.win.matchMedia('(prefers-reduced-motion:reduce)').matches); } catch (_e) {}
    if (reduce || isCoarsePointer(S.win)) return;

    const win = S.win, doc = S.doc;
    const stageRoot = (S.ctx && S.ctx.root) || doc.body;
    const nowMs = () => (win.performance && win.performance.now ? win.performance.now() : Date.now());

    // Pre-engage pointer tracking (for "mid-drag" gating + seeding the virtual cursor).
    const realPos = { x: win.innerWidth / 2, y: win.innerHeight / 2, has: false };
    let pointerHeld = false;
    S.addListener(doc, 'pointermove', (e) => {
      realPos.x = e.clientX; realPos.y = e.clientY; realPos.has = true;
      pointerHeld = (e.buttons & 1) === 1;      // a held primary button == an in-progress drag
    }, { passive: true, capture: true });
    S.addListener(doc, 'pointerup', () => { pointerHeld = false; }, { passive: true, capture: true });

    let engaged = false, disarmed = false, rolled = false, tries = 0;
    let fakeCursor = null, tick = null, suppressor = null;

    const restoreCursor = () => {
      try { stageRoot.classList.remove('intake-hijack-none'); } catch (_e) {}
      if (fakeCursor) { try { fakeCursor.remove(); } catch (_e) {} fakeCursor = null; }
    };
    // BULLETPROOF: cursor restoration also runs on release/dispose — never leave cursor:none behind.
    S.addCleanup(restoreCursor);

    const disarm = () => {
      if (disarmed) return;
      disarmed = true;
      if (tick) { S.removeTicker(tick); tick = null; }
      if (suppressor) { try { suppressor(); } catch (_e) {} suppressor = null; }
      restoreCursor();
    };
    // A guard trip (user fought some other friction to the escape hatch) tears the hijack down too.
    S.guard.onFrictionRelease(disarm);

    const engage = () => {
      if (engaged || disarmed) return;
      engaged = true;
      // ensureStyle already injected the shared sheet (installSteering runs it); just flip the class.
      try { stageRoot.classList.add('intake-hijack-none'); } catch (_e) {}

      let vx = realPos.has ? realPos.x : win.innerWidth / 2;
      let vy = realPos.has ? realPos.y : win.innerHeight / 2;
      let lastRX = realPos.x, lastRY = realPos.y;
      let fightPx = 0, lastBumpPx = 0, insideSince = 0;
      const engageT = nowMs();
      const wrongIdx = (S.wrong[0] && S.wrong[0].index != null) ? S.wrong[0].index : correct.index;

      fakeCursor = doc.createElement('div');
      fakeCursor.className = 'intake-hijack-cursor';
      fakeCursor.style.zIndex = String(HIJACK_Z);
      fakeCursor.style.transform = `translate(${vx.toFixed(1)}px,${vy.toFixed(1)}px)`;
      S.addNode(fakeCursor, doc.body);

      // Fold real movement into the virtual cursor + accumulate the "fight" (movement AWAY
      // from the correct center). Bursts feed the shared escape accounting; a hard fight aborts.
      const onMove = (e) => {
        if (disarmed) return;
        const dx = e.clientX - lastRX, dy = e.clientY - lastRY;
        lastRX = e.clientX; lastRY = e.clientY;
        vx += dx; vy += dy;                       // user movement moves the virtual cursor 1:1
        const c = S.centerOf(correct.el);
        const tox = c.x - vx, toy = c.y - vy;
        const tl = Math.hypot(tox, toy) || 1;
        const opp = -((dx * tox + dy * toy) / tl); // >0 when the user drags AWAY from correct
        if (opp > 0) {
          fightPx += opp;
          if (fightPx - lastBumpPx >= HIJACK_BUMP_PX) {   // feed the shared escape guard in bursts
            lastBumpPx = fightPx;
            try { S.guard.bump(wrongIdx, 0); } catch (_e) {}
          }
          if (fightPx >= HIJACK_FIGHT_ABORT_PX) { disarm(); return; } // escape valve: free control, NO commit
        }
      };
      S.addListener(doc, 'pointermove', onMove, { passive: true, capture: true });

      // Escape aborts outright.
      S.addListener(win, 'keydown', (e) => { if (e.key === 'Escape' || e.keyCode === 27) disarm(); }, true);

      // Capture-phase suppression of REAL commits while engaged (the real cursor is elsewhere,
      // so a real click is geometrically wrong). The auto-commit uses forceComplete, which never
      // dispatches a DOM click, so it is unaffected; other steers' programmatic el.click() (not
      // present this beat — they're excluded — but be safe) is untrusted and passes.
      const swallow = (e) => {
        if (disarmed || !e.isTrusted) return;
        e.preventDefault(); e.stopImmediatePropagation();
      };
      try { stageRoot.addEventListener('click', swallow, true); } catch (_e) {}
      try { stageRoot.addEventListener('pointerdown', swallow, true); } catch (_e) {}
      suppressor = () => {
        try { stageRoot.removeEventListener('click', swallow, true); } catch (_e) {}
        try { stageRoot.removeEventListener('pointerdown', swallow, true); } catch (_e) {}
      };
      S.addCleanup(suppressor);

      tick = () => {
        if (disarmed) return;
        // jumpscare freeze or a torn-down beat -> bail + restore.
        if (doc.body && doc.body.classList && doc.body.classList.contains('ix-freeze')) { disarm(); return; }
        const g = hijackGain(nowMs() - engageT);
        const c = S.centerOf(correct.el);
        vx += (c.x - vx) * g;
        vy += (c.y - vy) * g;
        vx = Math.max(0, Math.min(win.innerWidth, vx));   // never strand the glyph offscreen
        vy = Math.max(0, Math.min(win.innerHeight, vy));
        if (fakeCursor) fakeCursor.style.transform = `translate(${vx.toFixed(1)}px,${vy.toFixed(1)}px)`;
        // auto-commit once the virtual cursor rests inside the correct rect ~150ms.
        const r = c.r;
        const inside = r && vx >= r.left && vx <= r.left + c.w && vy >= r.top && vy <= r.top + c.h;
        if (inside) {
          if (!insideSince) insideSince = nowMs();
          else if (nowMs() - insideSince >= HIJACK_DWELL_MS) {
            const idx = correct.index;
            disarm();                              // restore the real cursor FIRST (bulletproof)
            try { S.ctx.forceComplete(idx); } catch (_e) {}  // real finalize path (scoring/reward/VO)
            return;
          }
        } else {
          insideSince = 0;
        }
      };
      S.addTicker(tick);
      S.addCleanup(() => { if (tick) S.removeTicker(tick); });
    };

    // Silent arm: after the linger dwell, roll ONCE (~40%). Skip while mid-drag on a veil/sticker
    // or during the jumpscare freeze; re-check a few times, then give up (one engagement max).
    const attemptEngage = () => {
      if (rolled || engaged || disarmed) return;
      const frozen = !!(doc.body && doc.body.classList && doc.body.classList.contains('ix-freeze'));
      if (pointerHeld || frozen) {
        if (++tries <= HIJACK_MAX_RETRIES) {
          const rt = win.setTimeout(attemptEngage, HIJACK_RETRY_MS);
          S.addCleanup(() => win.clearTimeout(rt));
        }
        return;
      }
      rolled = true;                              // the "we MIGHT" roll happens exactly once
      if (Math.random() < MOUSEHIJACK_CHANCE) engage();
    };
    const armT = win.setTimeout(attemptEngage, MOUSEHIJACK_LINGER_MS);
    S.addCleanup(() => win.clearTimeout(armT));
  },
};

/** tag an element as steer-touched so release-time restore is unambiguous. */
function mark(S, el) { S.snapStyle(el, ['transition']); if (!el.style.transition) el.style.transition = 'transform .25s ease'; }

/* ----------------------------------------------------------------------------
 * injected stylesheet (once per document)
 * -------------------------------------------------------------------------- */
function ensureStyle(doc) {
  if (_styledDocs.has(doc)) return;
  if (doc.getElementById(STYLE_ID)) { _styledDocs.add(doc); return; }
  const st = doc.createElement('style');
  st.id = STYLE_ID;
  st.textContent = `
.intake-steer-decoy{position:fixed;z-index:4;transform:translate(-50%,-50%);pointer-events:none;
  color:#c9b8e8;background:#2f2f52;border:1px solid rgba(255,105,180,.18);border-radius:12px;
  padding:12px 14px;opacity:.5;font:inherit;text-align:center;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;
  animation:intake-steer-wob var(--wob,1.6s) ease-in-out infinite alternate;}
@keyframes intake-steer-wob{from{transform:translate(-50%,-50%) rotate(-3deg)}to{transform:translate(-50%,-50%) rotate(3deg)}}
.intake-steer-sticker{position:fixed;z-index:6;display:flex;align-items:center;justify-content:center;
  font-size:32px;border-radius:14px;cursor:grab;touch-action:none;user-select:none;
  background:radial-gradient(circle at 30% 30%,rgba(176,108,255,.85),rgba(255,105,180,.75));
  box-shadow:0 8px 24px rgba(0,0,0,.4);transition:opacity .25s ease;}
.intake-steer-sticker:active{cursor:grabbing;}
.intake-steer-veil{position:fixed;z-index:6;display:flex;align-items:center;justify-content:center;
  border-radius:12px;cursor:ew-resize;touch-action:none;user-select:none;font-size:17px;font-weight:700;letter-spacing:.06em;
  color:#6f688c;background:#262640;border:1px solid rgba(255,105,180,.12);box-shadow:0 6px 20px rgba(0,0,0,.4);
  text-transform:uppercase;transition:opacity .2s ease;}
.intake-steer-hold{position:fixed;z-index:7;width:64px;height:64px;transform:translate(-50%,-50%);
  border-radius:50%;pointer-events:none;
  background:conic-gradient(var(--intake-accent,#ff69b4) calc(var(--p,0)*360deg),rgba(255,255,255,.12) 0);
  mask:radial-gradient(circle,transparent 60%,#000 61%);-webkit-mask:radial-gradient(circle,transparent 60%,#000 61%);}
.intake-steer-nag{position:fixed;z-index:8;transform:translate(-50%,-100%);display:flex;gap:8px;align-items:center;
  background:#252542;border:1px solid rgba(255,105,180,.4);border-radius:10px;padding:8px 10px;
  box-shadow:0 10px 30px rgba(0,0,0,.5);font-size:17px;font-weight:600;color:#f3e9f6;white-space:nowrap;}
.intake-steer-nagbtn{font:inherit;cursor:pointer;background:#ff69b4;color:#211;border:0;border-radius:8px;padding:7px 14px;font-weight:700;}
.intake-steer-overflow{position:fixed;z-index:1;border-radius:16px;cursor:pointer;background:transparent;}
.intake-steer-ghost{position:fixed;z-index:9;width:14px;height:14px;transform:translate(-50%,-50%);
  border-radius:50%;pointer-events:none;opacity:0;transition:opacity .2s ease;
  background:radial-gradient(circle,rgba(255,105,180,.9),rgba(255,105,180,0));box-shadow:0 0 12px rgba(255,105,180,.8);}
.intake-steer-melt{position:fixed;overflow:hidden;box-sizing:border-box;backface-visibility:hidden;}
.intake-steer-melt.is-melt{transform-origin:50% 100%;animation:intake-melt .9s cubic-bezier(.55,.06,.68,.19) forwards;}
.intake-steer-melt.is-fall{transform-origin:50% 50%;animation:intake-fall 1s cubic-bezier(.42,0,.9,.32) forwards;}
@keyframes intake-melt{
  0%{transform:translateY(0) scaleX(1) scaleY(1);filter:blur(0);opacity:1;}
  30%{transform:translateY(4px) scaleX(1.05) scaleY(.9);filter:blur(1px);opacity:1;}
  100%{transform:translateY(48px) scaleX(1.14) scaleY(.14);filter:blur(6px);opacity:0;}}
@keyframes intake-fall{
  0%{transform:translateY(0) rotate(0deg);opacity:1;}
  14%{transform:translateY(-7px) rotate(-3deg);opacity:1;}
  100%{transform:translateY(115vh) rotate(15deg);opacity:.9;}}
@keyframes intake-melt-fade{from{opacity:1;}to{opacity:0;}}
.intake-steer-drop{position:fixed;z-index:7;box-sizing:border-box;backface-visibility:hidden;transform-origin:50% 50%;
  animation:intake-drop .82s cubic-bezier(.4,0,.9,.32) forwards;}
@keyframes intake-drop{
  0%{transform:translateY(0) rotate(0deg);opacity:1;}
  12%{transform:translateY(-6px) rotate(3deg);opacity:1;}
  100%{transform:translateY(120vh) rotate(var(--drop-rot,14deg));opacity:.92;}}
@keyframes intake-drop-fade{from{opacity:1;}to{opacity:0;}}
.intake-hijack-none,.intake-hijack-none *{cursor:none !important;}
.intake-hijack-cursor{position:fixed;left:0;top:0;width:14px;height:14px;margin-left:-7px;margin-top:-7px;
  border-radius:50%;pointer-events:none;will-change:transform;
  background:radial-gradient(circle,rgba(255,150,210,.95),rgba(255,105,180,.55) 45%,rgba(255,105,180,0) 70%);
  box-shadow:0 0 10px 3px rgba(255,105,180,.75),0 0 20px 6px rgba(176,108,255,.45);}
@media (prefers-reduced-motion:reduce){
  .intake-steer-decoy{animation:none;}
  .intake-steer-melt.is-melt,.intake-steer-melt.is-fall{animation:intake-melt-fade .2s linear forwards;transform:none;filter:none;}
  .intake-steer-drop{animation:intake-drop-fade .15s linear forwards;transform:none;}
  .intake-hijack-cursor{display:none;}}
`;
  (doc.head || doc.documentElement).appendChild(st);
  _styledDocs.add(doc);
}
