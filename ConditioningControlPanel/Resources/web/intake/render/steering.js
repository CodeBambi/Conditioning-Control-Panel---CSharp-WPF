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
  Steer, STEERS, STEER_BAND_WEIGHT, Band,
  clamp01, lerp,
} from '../core/contracts.js';

/* ----------------------------------------------------------------------------
 * PURE decision helpers (headless-testable; no DOM).
 * -------------------------------------------------------------------------- */

/** Global master scalar from caps (defaults to 1, clamped 0..1). */
export function masterOf(caps) {
  if (!caps || caps.masterIntensity == null) return 1;
  return clamp01(caps.masterIntensity);
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
 * @param {{caps?:Object}} opts
 * @returns {{installSteering:(ctx:Object)=>{release:()=>void}}}
 */
export function createSteering({ caps: factoryCaps } = {}) {
  return { installSteering: (ctx) => installSteering(ctx, factoryCaps) };
}

const NOOP_HANDLE = { release: () => {} };

/* ----------------------------------------------------------------------------
 * INSTALL — all DOM access lives here.
 * -------------------------------------------------------------------------- */
function installSteering(ctx, factoryCaps) {
  // Merge caps: per-beat ctx.caps wins, factory caps as fallback.
  const caps = ctx && ctx.caps ? ctx.caps : (factoryCaps || {});
  const beatCtx = ctx ? Object.assign({}, ctx, { caps }) : ctx;

  const plan = resolveSteerPlan(beatCtx);
  if (!plan.active) return NOOP_HANDLE;               // Calibration/Recovery/valve-0 -> play it straight

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

  const sharedCtx = {
    ctx: beatCtx, doc, win, s, all, correct, wrong, guard,
    addCleanup, addListener, addNode, addTicker, removeTicker,
    tf, applyTf, snapStyle, centerOf, cursor, wireCursor,
  };

  /* ---- global "missed click" tracker: any evasive steer needs it -------- */
  const EVASIVE = new Set([Steer.Flee, Steer.Exile, Steer.ShrinkHit, Steer.OpacitySkew,
    Steer.Defocus, Steer.Decay, Steer.OccludeGif, Steer.DragReveal, Steer.Crowd, Steer.Tunnel]);
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

/* ----------------------------------------------------------------------------
 * STEER INSTALLERS. Each receives sharedCtx and wires DOM behavior + cleanup.
 * Convention: correct-favoring steers help the correct option; obstructive
 * steers hinder wrong options but always route effort into the guard.
 * -------------------------------------------------------------------------- */
const INSTALLERS = {

  /* correct option drifts toward the cursor (bias, not blocker) */
  [Steer.Magnet](S) {
    if (!S.correct.length) return;
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
      const c = S.centerOf(o.el);
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
      const onDown = () => S.guard.bump(o.index, 0);
      S.addListener(o.el, 'pointerdown', onDown, { passive: true });
    }
  },

  /* decoy clones swarm around each wrong option (visual only, never blocking) */
  [Steer.Crowd](S) {
    if (!S.wrong.length) return;
    const n = Math.round(lerp(2, 6, S.s));
    for (const o of S.wrong) {
      const c = S.centerOf(o.el);
      for (let i = 0; i < n; i++) {
        const clone = S.doc.createElement('div');
        clone.className = 'intake-steer-decoy';
        clone.textContent = o.el.textContent || '';
        const ang = (i / n) * Math.PI * 2 + Math.random();
        const rad = lerp(40, 90, Math.random());
        clone.style.left = (c.x + Math.cos(ang) * rad) + 'px';
        clone.style.top = (c.y + Math.sin(ang) * rad) + 'px';
        clone.style.width = c.w + 'px';
        clone.style.setProperty('--wob', (Math.random() * 2 + 1).toFixed(2) + 's');
        S.addNode(clone, S.doc.body);          // pointer-events:none via CSS -> real option stays clickable
      }
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

  /* draggable "sticker" parks over a wrong option; drag it aside to reveal it */
  [Steer.OccludeGif](S) {
    if (!S.wrong.length) return;
    const target = S.wrong[Math.floor(S.wrong.length / 2)];
    const c = S.centerOf(target.el);
    const sticker = S.doc.createElement('div');
    sticker.className = 'intake-steer-sticker';
    sticker.textContent = '💠';
    sticker.style.left = (c.r.left) + 'px';
    sticker.style.top = (c.r.top) + 'px';
    sticker.style.width = c.w + 'px';
    sticker.style.height = c.h + 'px';
    S.addNode(sticker, S.doc.body);
    let dragging = false, ox = 0, oy = 0, moved = 0;
    const disarm = () => { sticker.style.pointerEvents = 'none'; sticker.style.opacity = '0'; };
    S.guard.onFrictionRelease(disarm);
    const down = (e) => { dragging = true; ox = e.clientX; oy = e.clientY; sticker.setPointerCapture && sticker.setPointerCapture(e.pointerId); S.guard.bump(target.index, 0); };
    const move = (e) => {
      if (!dragging) return;
      const dx = e.clientX - ox, dy = e.clientY - oy;
      sticker.style.transform = `translate(${dx}px,${dy}px)`;
      moved = Math.hypot(dx, dy);
      if (moved > Math.max(c.w, 80) * 0.9) disarm();   // dragged clear -> option exposed
    };
    const up = () => { dragging = false; };
    S.addListener(sticker, 'pointerdown', down);
    S.addListener(S.doc, 'pointermove', move, { passive: true });
    S.addListener(S.doc, 'pointerup', up, { passive: true });
    // double-click shoves it aside outright
    S.addListener(sticker, 'dblclick', () => { S.guard.bump(target.index, 0); disarm(); });
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

  /* a veil covers a wrong/refusal option; swipe it away to uncover (drag-reveal) */
  [Steer.DragReveal](S) {
    if (!S.wrong.length) return;
    const target = S.wrong[0];
    const c = S.centerOf(target.el);
    const veil = S.doc.createElement('div');
    veil.className = 'intake-steer-veil';
    veil.textContent = 'drag to reveal';
    veil.style.left = c.r.left + 'px'; veil.style.top = c.r.top + 'px';
    veil.style.width = c.w + 'px'; veil.style.height = c.h + 'px';
    S.addNode(veil, S.doc.body);
    const disarm = () => { veil.style.pointerEvents = 'none'; veil.style.opacity = '0'; };
    S.guard.onFrictionRelease(disarm);
    let dragging = false, sx = 0, need = Math.max(c.w * 0.8, 70);
    const down = (e) => { dragging = true; sx = e.clientX; veil.setPointerCapture && veil.setPointerCapture(e.pointerId); S.guard.bump(target.index, 0); };
    const move = (e) => {
      if (!dragging) return;
      const dx = e.clientX - sx;
      veil.style.transform = `translateX(${dx}px)`;
      veil.style.opacity = String(Math.max(0, 1 - Math.abs(dx) / need));
      if (Math.abs(dx) >= need) disarm();
    };
    const up = () => { dragging = false; };
    S.addListener(veil, 'pointerdown', down);
    S.addListener(S.doc, 'pointermove', move, { passive: true });
    S.addListener(S.doc, 'pointerup', up, { passive: true });
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
      const c = S.centerOf(o.el);
      const pad = S.doc.createElement('div');
      pad.className = 'intake-steer-overflow';
      pad.style.left = (c.r.left - grow) + 'px';
      pad.style.top = (c.r.top - grow) + 'px';
      pad.style.width = (c.w + grow * 2) + 'px';
      pad.style.height = (c.h + grow * 2) + 'px';
      const fwd = () => { o.el.click(); };
      pad.addEventListener('click', fwd);
      S.addNode(pad, S.doc.body);   // sits behind the button (z via CSS); clicks in the margin forward to correct
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
  border-radius:12px;cursor:ew-resize;touch-action:none;user-select:none;font-size:12px;letter-spacing:.06em;
  color:#f3e9f6;background:linear-gradient(120deg,#3a2a5c,#5a2a52);box-shadow:0 6px 20px rgba(0,0,0,.4);
  text-transform:uppercase;transition:opacity .2s ease;}
.intake-steer-hold{position:fixed;z-index:7;width:64px;height:64px;transform:translate(-50%,-50%);
  border-radius:50%;pointer-events:none;
  background:conic-gradient(var(--intake-accent,#ff69b4) calc(var(--p,0)*360deg),rgba(255,255,255,.12) 0);
  mask:radial-gradient(circle,transparent 60%,#000 61%);-webkit-mask:radial-gradient(circle,transparent 60%,#000 61%);}
.intake-steer-nag{position:fixed;z-index:8;transform:translate(-50%,-100%);display:flex;gap:8px;align-items:center;
  background:#252542;border:1px solid rgba(255,105,180,.4);border-radius:10px;padding:8px 10px;
  box-shadow:0 10px 30px rgba(0,0,0,.5);font-size:13px;color:#f3e9f6;white-space:nowrap;}
.intake-steer-nagbtn{font:inherit;cursor:pointer;background:#ff69b4;color:#211;border:0;border-radius:8px;padding:5px 12px;font-weight:600;}
.intake-steer-overflow{position:fixed;z-index:1;border-radius:16px;cursor:pointer;background:transparent;}
.intake-steer-ghost{position:fixed;z-index:9;width:14px;height:14px;transform:translate(-50%,-50%);
  border-radius:50%;pointer-events:none;opacity:0;transition:opacity .2s ease;
  background:radial-gradient(circle,rgba(255,105,180,.9),rgba(255,105,180,0));box-shadow:0 0 12px rgba(255,105,180,.8);}
@media (prefers-reduced-motion:reduce){.intake-steer-decoy{animation:none;}}
`;
  (doc.head || doc.documentElement).appendChild(st);
  _styledDocs.add(doc);
}
