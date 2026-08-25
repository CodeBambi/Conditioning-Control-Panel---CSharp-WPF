/* ============================================================================
 * engine/sustained.js — the sustain()/stop() primitives.
 *
 *   row_drift · bubble_field · wash · gif_rain · ambient_field · crt
 *
 * Laws enforced here:
 *   - ONE reused DOM element per WASH KIND (spiral / pink / braindrain / sublim),
 *     refreshing its fade deadline — overlapping triggers never pile DOM
 *     (DTRH holdOn, verbatim posture);
 *   - node budgets: gif_rain 14 (6 coarse/low-motion), bubbles 18 (8),
 *     ambient 24 (10) - the lite twins ride ctx.lite(), flashBurstLite's seam;
 *   - cadences come from the clamped channel vector (bubbleRate 2200->260ms,
 *     rain gap from flashRate, drift speed/amplitude from bgIntensity) and
 *     RETUNE LIVE when setHeat changes them;
 *   - stop(kind) FADES, never snaps (the engine only snaps on suspend/dispose);
 *   - reduced motion / motionLevel 0: drift becomes slow opacity breathing, the
 *     spiral wash freezes to a static veil, rain and crt roll are skipped;
 *   - clickable bubbles are a blocking effect -> escape guard.
 *
 * ADDITIVE (2026-08-23, Instant Recall's variety pass): the wash handle answers
 * `url` - the url that was actually written to the element's backgroundImage
 * (`opts.url`, else `ctx.spiralUrl()` for a spiral), or `null` when the wash kept
 * its CSS gradient. Nothing else moved: this is a READ of what already happened,
 * so every existing caller is byte-identical and simply ignores a new field.
 * It exists because a class may only record what the engine DID (Law I), and
 * "which spiral did you just see" is unanswerable off the request alone.
 *
 * ADDITIVE (2026-08-25, the Loom directive): the spiral source may now be a
 * PARAMS WRAPPER instead of a url string - {loom:true, id:'loom:xxxxxxxx',
 * params:<loomField v2>, href:<bundled gif fallback url>} - from `opts.url` or
 * from the shell's spiralUrl() provider. That routes through loomWash.js: ONE
 * <canvas> child inside the SAME wash element (the one-element-per-kind law is
 * untouched; the parent's opacity stays the intensity channel), drawn live by
 * the vendored Loom shader. The handle's `url` then answers the wrapper's
 * STABLE id ('loom:'+hash of the normalized params) - and on the WebGL floor
 * (no context, or a context lost) the bundled `href` gif is painted through the
 * plain path and the handle answers THAT url, because the ledger records what
 * is actually on screen (Law I). A plain string source is byte-identical to
 * before.
 * ==========================================================================*/

import { clamp01 } from '../core/caps.js';
import { NODE_CAPS, washSpec, gifRainSpec, ambientSpec, driftSpec, bubbleSpec } from './curves.js';
import { rand, hasDom, mediaEl, budgetedKind } from './util.js';
import { createEscapeGuard } from './escape.js';
import { createLoomWash } from './loomWash.js';

/** ambient_field kinds ported from DTRH fieldFx AMBIENT_KINDS (DOM subset). */
const AMBIENT = {
  motes:    { shape: 'dot',   size: [2, 5],  dy: [-40, -90],  dx: [-14, 14], col: 'var(--ae-lav)',  dur: [12, 22] },
  confetti: { shape: 'fleck', size: [3, 7],  dy: [70, 150],   dx: [-24, 24], col: 'var(--ae-pink)', dur: [5, 9] },
  petals:   { shape: 'dot',   size: [4, 9],  dy: [50, 110],   dx: [-30, 30], col: 'var(--ae-pink)', dur: [7, 13] },
  embers:   { shape: 'fleck', size: [2, 5],  dy: [-90, -170], dx: [-16, 16], col: 'var(--ae-gold)', dur: [4, 8] },
  specks:   { shape: 'dot',   size: [1, 3],  dy: [-18, 18],   dx: [-18, 18], col: 'var(--ae-ink)',  dur: [3, 6] },
  glints:   { shape: 'star',  size: [5, 11], dy: [-12, 12],   dx: [-12, 12], col: 'var(--ae-gold)', dur: [3, 7] },
  ash:      { shape: 'fleck', size: [2, 4],  dy: [40, 90],    dx: [-22, 8],  col: 'var(--ae-ink)',  dur: [8, 15] },
  bubbles:  { shape: 'dot',   size: [3, 8],  dy: [-60, -130], dx: [-10, 10], col: 'var(--ae-lav)',  dur: [6, 12] },
  goldleaf: { shape: 'fleck', size: [3, 6],  dy: [-30, -70],  dx: [-18, 18], col: 'var(--ae-gold)', dur: [9, 16] },
  coins:    { shape: 'star',  size: [4, 8],  dy: [60, 120],   dx: [-12, 12], col: 'var(--ae-gold)', dur: [5, 9] },
};
export const AMBIENT_KINDS = Object.freeze(Object.keys(AMBIENT));

export function createSustained(ctx) {
  /** kind -> handle. sustain() on a live kind RETUNES it instead of stacking. */
  const active = new Map();
  const washHolds = new Map();   // washKind -> { el, hideTimer } — the one-element law
  const live = { rain: 0, bubbles: 0 };
  /* W3 (trap 111): a sustain is cued PER WAVE, never per node, so the three
   * things a re-entrant starter has to remember live up here.
   *   lastWashKind  the wash that last took the air, so a refresh of the SAME
   *                 wash is silent and a change of kind is not (P0-25);
   *   lastHumAt     the Loom hum's own floor - the spiral is re-triggered by
   *                 the pressure decks far faster than the 1.4s recipe, and a
   *                 stack of hums is a drone nobody asked for (P0-26). */
  let lastWashKind = null;
  let lastHumAt = 0;
  const SPIRAL_HUM_GAP_MS = 1400;

  /* ---- wash -------------------------------------------------------------- */
  function ensureWash(washKind) {
    if (!hasDom()) return null;
    let h = washHolds.get(washKind);
    if (!h) {
      const node = document.createElement('div');
      node.className = 'ae-wash ae-wash-' + washKind + (ctx.reduced() ? ' ae-wash-static' : '');
      ctx.layers.back.appendChild(node);
      ctx.timers.own(node);
      h = { el: node, hideTimer: 0, forever: false, heldAlpha: 0 };
      washHolds.set(washKind, h);
    }
    return h;
  }

  /* ---- the live Loom inside a wash hold (see the 2026-08-25 header note) --- */
  /** Is this spiral source the Loom params wrapper rather than a url string? */
  function isLoomWrap(v) {
    return !!(v && typeof v === 'object' && v.loom === true && v.params);
  }
  /** Mount/retune the loom canvas on this hold. Returns the LEDGER answer:
   *  the wrapper's id while the canvas is live, the fallback gif url when the
   *  WebGL floor caught it, or null (the CSS conic kept the element). */
  function loomMount(h, wrap) {
    try {
      if (!h.loom) h.loom = createLoomWash({ log: ctx.log });
      if (h.loom.supported()) {
        const ok = h.loom.mount(h.el, wrap.params, {
          still: ctx.reduced(),
          /* context lost MID-HOLD: the canvas is gone, so the gif takes the
           * element at once - the screen never goes bare. (The one answer
           * already handed out for this hold can go stale for its remainder;
           * every NEXT trigger answers the gif url, honestly.) */
          onLost: () => {
            try {
              if (wrap.href) h.el.style.backgroundImage = 'url("' + wrap.href + '")';
            } catch (e) { /* ignore */ }
          },
        });
        if (ok) {
          h.el.style.backgroundImage = 'none';
          return String(wrap.id || 'loom:0');
        }
      }
    } catch (e) { ctx.log('loom wash refused (' + ((e && e.message) || e) + ') - gif fallback'); }
    // THE FLOOR: no WebGL -> the bundled gif IS what is shown, so it IS the answer.
    loomDrop(h);
    if (wrap.href && typeof wrap.href === 'string') {
      h.el.style.backgroundImage = 'url("' + wrap.href + '")';
      return String(wrap.href);
    }
    return null;
  }
  /** Tear the loom canvas off a hold entirely (a url string took the element,
   *  or the hold is being released). Safe on a hold that never had one. */
  function loomDrop(h) {
    if (h && h.loom) { try { h.loom.dispose(); } catch (e) { /* ignore */ } h.loom = null; }
  }
  /** The hold's opacity was driven to 0 (or back up): idle the loom's rAF. */
  function loomActive(h, on) {
    if (h && h.loom) { try { h.loom.setActive(!!on); } catch (e) { /* ignore */ } }
  }

  /**
   * Pre-warm the spiral wash's live-loom path (2026-08-26, the deep-end merge
   * choreography). The first jackpot garnish / spiral sustain used to pay
   * WebGL context creation + two shader compiles + a draw INLINE on a reward
   * frame. This mounts the loom canvas on the spiral hold NOW - the element
   * sits at its CSS opacity 0, one frame renders, the rAF loop is idled - so
   * the first real trigger reuses the same canvas and compiled program.
   * No rng, no fx event, nothing visible; a gif-path class (no loom wrapper
   * from the provider) is a no-op, and a hold already live is left alone.
   * @returns {boolean} true when a canvas was warmed.
   */
  function warmSpiral() {
    if (!hasDom() || typeof ctx.spiralUrl !== 'function') return false;
    let src = null;
    try { src = ctx.spiralUrl(); } catch (e) { src = null; }
    if (!isLoomWrap(src)) return false;
    const h = ensureWash('spiral');
    if (!h || h.loom || h.forever) return false;   // live or held: already paid for
    const took = loomMount(h, src);
    loomActive(h, false);
    return took != null;
  }

  /**
   * wash: snap ON at alpha, then fade after holdMs. A fresh trigger REFRESHES the
   * deadline on the same element (never a second element for the same kind).
   */
  function startWash(opts = {}) {
    const chans = ctx.channels();
    const variant = ctx.variant('wash', chans.bgIntensity, opts.variant || opts.washKind);
    const washKind = variant.name || 'pink';
    const h = ensureWash(washKind);
    if (!h) return null;
    const strength = ctx.ceiling('bgIntensity', opts.strength);
    if (strength <= 0.001) return null;       // bgIntensity capped off -> no wash
    const spec = washSpec(washKind === 'drain' || washKind === 'braindrain' ? 'braindrain' : washKind,
      ctx.magnitude(strength), opts.durationMult || 1);
    const alpha = Math.min(spec.alpha, clamp01(opts.alpha == null ? spec.alpha : ctx.pct(opts.alpha))) * (ctx.reduced() ? 0.7 : 1);
    const holdMs = opts.holdMs == null ? spec.holdMs : Math.max(120, opts.holdMs | 0);

    /* THE URL THE WASH ACTUALLY WORE. `opts.url` is a REQUEST; what lands on the
     * element is this, and a class that quizzes the player on "which spiral did
     * you just see" must read the ANSWER off the handle, never off the request
     * (Law I: the ledger records what the engine did). `null` when nothing was
     * written - a pink/drain wash with no url keeps its CSS gradient. */
    let wroteUrl = null;
    let loomTook = false;
    if (opts.url || (washKind === 'spiral' && ctx.spiralUrl)) {
      const url = opts.url || ctx.spiralUrl();
      if (isLoomWrap(url)) {
        // THE LOOM PATH (2026-08-25): a params wrapper - live canvas, stable id.
        wroteUrl = loomMount(h, url);
        loomTook = typeof wroteUrl === 'string' && wroteUrl.indexOf('loom:') === 0;
      } else if (url) {
        loomDrop(h);   // a url string takes the element back; no-op when no canvas
        h.el.style.backgroundImage = 'url("' + url + '")'; wroteUrl = String(url);
      }
    }
    /* Was this wash already on screen? Read BEFORE the opacity is written: it
     * is the difference between a wash arriving and a wash being refreshed,
     * which is the whole of P0-25's per CHANGE, not per re-trigger. */
    const wasUp = parseFloat(h.el.style.opacity || '0') > 0.001;
    if (h.hideTimer) { ctx.timers.cancel(h.hideTimer); h.hideTimer = 0; }
    h.el.style.opacity = String(alpha);
    /* THE HELD WASH. sustainForever HOLDS the element at alpha until a later
       trigger says otherwise. A later trigger that is NOT forever is one of
       two things: a FLARE (a higher alpha - a jackpot's forced garnish, a
       ceremony's accent) that must fall back to the HELD alpha when its hold
       ends, or a STEP-DOWN (a lower alpha - the decks' whisper-out idiom) that
       ends the hold and fades to 0. Before this, a jackpot over a held spiral
       fell back to 0 and silently killed the class's wheel. */
    if (opts.sustainForever === true) {
      h.forever = true; h.heldAlpha = alpha;
    } else if (h.forever && alpha > (h.heldAlpha || 0)) {
      const back = h.heldAlpha;
      h.hideTimer = ctx.timers.after(holdMs, () => { h.el.style.opacity = String(back); h.hideTimer = 0; });
    } else {
      h.forever = false; h.heldAlpha = 0;
      h.hideTimer = ctx.timers.after(holdMs, () => { h.el.style.opacity = '0'; h.hideTimer = 0; loomActive(h, false); });
    }
    ctx.fx('wash', washKind);
    /* W3 P0-25: THE AIR. `if (opts.sfx)` was never true - no caller in the
     * school ever set it - so the heaviest thing the engine draws arrived in
     * silence. A wash now takes the air by default and `sfx:false` is the way
     * out; a string still names the cue. It fires on a CHANGE only: the wash
     * arriving, or a different kind taking the element. startWash is re-entrant
     * by design (a deck refreshes the deadline several times a second at the
     * top of the ladder) and one cue per refresh would be a hiss. */
    const washChanged = !wasUp || washKind !== lastWashKind;
    if (opts.sfx !== false && washChanged) {
      ctx.sfx(typeof opts.sfx === 'string' ? opts.sfx : 'wash', 0.2 + 0.3 * strength, { duck: 'voice' });
    }
    lastWashKind = washKind;
    /* W3 P0-26: the Loom is the biggest thing on the screen and had no sound at
     * all. The hum is struck on the mount and on each re-trigger that takes the
     * canvas - never per frame (loomWash draws at 30-60fps and owns no cue) -
     * and never on warmSpiral, which mounts at opacity 0 and does not come
     * through here. The recipe's own 1.4s length is the floor between strikes. */
    if (loomTook) {
      const now = Date.now();
      if (now - lastHumAt >= SPIRAL_HUM_GAP_MS) { lastHumAt = now; ctx.sfx('spiral_hum', 0.22); }
    }
    return {
      kind: 'wash', variant: washKind, alpha, holdMs, url: wroteUrl,
      retune() { /* alpha follows the next trigger; nothing to animate */ },
      stop() { if (h.hideTimer) { ctx.timers.cancel(h.hideTimer); h.hideTimer = 0; } h.forever = false; h.heldAlpha = 0; h.el.style.opacity = '0'; loomActive(h, false); },
    };
  }

  /* ---- row_drift --------------------------------------------------------- */
  function startRowDrift(opts = {}) {
    const targets = ctx.nodes(opts.targets);
    if (!targets.length) { ctx.log('row_drift needs opts.targets (the rows to slide) - nothing to drift'); return null; }
    const axis = opts.axis === 'y' ? 'y' : 'x';
    // drift rides bgIntensity; opts.strength SPENDS that ceiling, never raises it
    let bg = ctx.ceiling('bgIntensity', opts.strength);
    if (bg <= 0.001) return null;
    let drift = driftSpec(bg);
    const variant = ctx.variant('row_drift', bg, opts.variant);
    const phase = targets.map((_, i) => (opts.stagger === false ? 0 : i * 0.7 + ctx.rng() * 0.3));

    if (ctx.reduced() || ctx.motion() <= 0) {
      // degrade: slow opacity breathing, no transforms at all
      for (const t of targets) { try { t.classList.add('ae-drift', 'ae-drift-breathe'); } catch { /* ignore */ } }
      ctx.fx('row_drift', 'breathe');
      return {
        kind: 'row_drift', variant: 'breathe', retune() {},
        stop() { for (const t of targets) { try { t.classList.remove('ae-drift', 'ae-drift-breathe'); } catch { /* ignore */ } } },
      };
    }

    let t0 = 0;
    let amp = Math.round(drift.px * (opts.amplitudeMult == null ? 1 : opts.amplitudeMult)) * ctx.motion();
    let hz = drift.hz * (opts.speedMult == null ? 1 : opts.speedMult);
    for (const t of targets) { try { t.classList.add('ae-drift'); } catch { /* ignore */ } }
    const loop = ctx.timers.loop((dt) => {
      t0 += dt;
      for (let i = 0; i < targets.length; i++) {
        const off = Math.sin((t0 * hz * Math.PI * 2) + phase[i]) * amp;
        const shear = variant.name === 'shear' ? Math.cos(t0 * hz * Math.PI * 2 + phase[i]) * amp * 0.25 : 0;
        try {
          targets[i].style.transform = axis === 'x'
            ? 'translate3d(' + off.toFixed(2) + 'px,' + shear.toFixed(2) + 'px,0)'
            : 'translate3d(' + shear.toFixed(2) + 'px,' + off.toFixed(2) + 'px,0)';
        } catch { /* ignore */ }
      }
      return true;
    });
    ctx.fx('row_drift', variant.name);
    return {
      kind: 'row_drift', variant: variant.name,
      retune() {
        bg = ctx.ceiling('bgIntensity', opts.strength);
        drift = driftSpec(bg);
        amp = Math.round(drift.px * (opts.amplitudeMult == null ? 1 : opts.amplitudeMult)) * ctx.motion();
        hz = drift.hz * (opts.speedMult == null ? 1 : opts.speedMult);
      },
      stop() {
        loop.cancel();
        for (const t of targets) { try { t.style.transform = ''; t.classList.remove('ae-drift'); } catch { /* ignore */ } }
      },
    };
  }

  /* ---- bubble_field ------------------------------------------------------ */
  function startBubbleField(opts = {}) {
    if (!hasDom()) return null;
    let rate = ctx.ceiling('bubbleRate', opts.strength);
    if (rate <= 0.001) { ctx.log('bubble_field: bubbleRate is 0 at this heat/cap - no field'); return null; }
    let spec = bubbleSpec(rate);
    const variant = ctx.variant('bubble_field', spec.alpha, opts.variant);
    // the lite twin rides flashBurstLite's own seam: ctx.lite() at spend time
    const bubbleCap = ctx.lite() ? NODE_CAPS.bubblesLite : NODE_CAPS.bubbles;
    const max = Math.min(bubbleCap, opts.max == null ? bubbleCap : opts.max | 0);
    const clickSafe = !!opts.clickSafe;
    const clickable = !clickSafe && opts.clickable !== false && typeof opts.onPop === 'function';
    let guard = null;
    if (clickable) {
      guard = createEscapeGuard({
        timers: ctx.timers,
        sfx: ctx.sfx,                      // W3 P0-24: the trip is a release
        onComplete: (why) => { ctx.log('bubble_field escape (' + why + ')'); if (typeof opts.onForceComplete === 'function') { try { opts.onForceComplete(why); } catch { /* ignore */ } } },
      });
      guard.arm();
    }
    /* W3 P1-17: the field's own air, ONE cue for the wave (trap 111). The
     * clickSafe field - decoration, nobody pops it - was silent from entrance
     * to exit, which is the whole reason the row exists. The exit is the
     * teardown's coalesced wash, not a second cue here. */
    ctx.sfx('bubble_bed', 0.24);
    let popN = 0;

    function spawn() {
      if (live.bubbles >= max) return;
      const size = Math.round(rand(ctx.rng, 44, 96) * (opts.sizeMult == null ? 1 : opts.sizeMult));
      const node = document.createElement('div');
      node.className = 'ae-bubble' + (clickable ? ' ae-bubble-clickable' : '');
      node.style.setProperty('--ae-x', Math.round(rand(ctx.rng, 6, 94)) + '%');
      node.style.setProperty('--ae-size', size + 'px');
      node.style.setProperty('--ae-alpha', String(Math.min(spec.alpha, clamp01(opts.alpha == null ? spec.alpha : ctx.pct(opts.alpha)))));
      node.style.setProperty('--ae-sway', Math.round(rand(ctx.rng, -60, 60)) + 'px');
      const durMs = Math.round(rand(ctx.rng, 5200, 9000) / Math.max(0.35, ctx.motion() || 0.35));
      node.style.setProperty('--ae-dur', durMs + 'ms');
      if (opts.url || (opts.media && ctx.assetUrlSync)) {
        const url = opts.url || ctx.assetUrlSync('still');
        if (url) node.style.backgroundImage = 'url("' + url + '")';
      }
      if (clickable) {
        node.addEventListener('click', (ev) => {
          try { ev.stopPropagation(); } catch { /* ignore */ }
          if (guard) guard.note();
          node.classList.add('ae-bubble-pop');
          // W3 P1-17: the pop ladder climbs a semitone a bubble, capped at 8.
          popN = Math.min(8, popN + 1);
          ctx.sfx('bubble_pop', 0.3 + 0.3 * clamp01(spec.alpha), { pitch: 1 + 0.06 * popN });
          try { opts.onPop(node); } catch { /* game's problem */ }
          ctx.timers.after(280, () => kill(node));
        }, { once: true });
      }
      ctx.layers.back.appendChild(node);
      ctx.timers.own(node);
      live.bubbles += 1;
      ctx.timers.after(durMs + 400, () => kill(node));
    }
    function kill(node) {
      if (!node || !node.parentNode) return;
      ctx.timers.release(node);
      live.bubbles = Math.max(0, live.bubbles - 1);
    }

    let timer = 0;
    function schedule() {
      const ms = Number.isFinite(opts.cadenceMs) ? opts.cadenceMs
        : (Number.isFinite(spec.cadenceMs) ? spec.cadenceMs : 2200);
      if (!Number.isFinite(ms)) return;                    // rate 0 -> silent
      timer = ctx.timers.after(Math.max(120, ms * (0.75 + ctx.rng() * 0.5)), () => { spawn(); schedule(); });
    }
    if (spec.on || Number.isFinite(opts.cadenceMs)) schedule();
    ctx.fx('bubble_field', variant.name);
    return {
      kind: 'bubble_field', variant: variant.name, escape: guard,
      get live() { return live.bubbles; },
      retune() { rate = ctx.ceiling('bubbleRate', opts.strength); spec = bubbleSpec(rate); },
      stop() {
        if (timer) ctx.timers.cancel(timer);
        timer = 0;
        popN = 0;
        if (guard) guard.cancel();
        // fade, never snap: existing bubbles finish their rise
      },
    };
  }

  /* ---- gif_rain ---------------------------------------------------------- */
  function startGifRain(opts = {}) {
    if (!hasDom()) return null;
    if (ctx.reduced() || ctx.motion() <= 0) { ctx.log('gif_rain skipped (reduced motion)'); return null; }
    const chans = ctx.channels();
    const rate = ctx.ceiling('flashRate', opts.strength);
    if (rate <= 0.001) return null;           // flashRate capped off -> no rain
    const variant = ctx.variant('gif_rain', rate, opts.variant);
    const spec = gifRainSpec(ctx.magnitude(rate), opts.durationMult || 1);
    const clickSafe = opts.clickSafe !== false;      // rain is decoration by default
    const endAt = (opts.durationMs == null ? spec.durationMs : opts.durationMs) + Date.now();
    // gifRainSpec is PURE (spec.max is the desktop cap); the lite twin is
    // composed here, on flashBurstLite's own ctx.lite() seam, min = protective.
    const rainCap = ctx.lite() ? Math.min(spec.max, NODE_CAPS.gifRainLite) : spec.max;
    let timer = 0;

    function spawn() {
      if (live.rain >= rainCap) return;
      /* THE DECODER BUDGET (util.js): past it a 'loop' request is served a
       * still, so the rain keeps its node count and drops its decode cost. */
      const url = ctx.assetUrlSync(budgetedKind(opts.assetKind || 'loop'));
      const node = (url && mediaEl(url)) || document.createElement('div');   // <video> for a webm/mp4 loop
      node.className = 'ae-rain';
      node.style.setProperty('--ae-x', Math.round(rand(ctx.rng, 2, 88)) + '%');
      node.style.setProperty('--ae-size', Math.round(rand(ctx.rng, 90, 190)) + 'px');
      node.style.setProperty('--ae-rot', Math.round(rand(ctx.rng, -14, 14)) + 'deg');
      const rainAlphaCeil = 0.55 + 0.35 * clamp01(chans.flashOpacity);
      node.style.setProperty('--ae-alpha', String(Math.min(rainAlphaCeil, clamp01(opts.alpha == null ? rainAlphaCeil : ctx.pct(opts.alpha)))));
      const fall = Math.round(rand(ctx.rng, spec.fallMsMin, spec.fallMsMax));
      node.style.setProperty('--ae-fall', fall + 'ms');
      if (!clickSafe && typeof opts.onPop === 'function') {
        node.style.pointerEvents = 'auto';
        node.addEventListener('click', () => { try { opts.onPop(node); } catch { /* ignore */ } ctx.timers.release(node); live.rain = Math.max(0, live.rain - 1); }, { once: true });
      }
      ctx.layers.back.appendChild(node);
      ctx.timers.own(node);
      live.rain += 1;
      ctx.timers.after(fall + 500, () => { ctx.timers.release(node); live.rain = Math.max(0, live.rain - 1); });
    }
    function schedule() {
      if (Date.now() >= endAt) { active.delete('gif_rain'); return; }
      timer = ctx.timers.after(Math.max(120, spec.gapMs * (0.8 + ctx.rng() * 0.4)), () => { spawn(); schedule(); });
    }
    spawn();
    schedule();
    ctx.fx('gif_rain', variant.name);
    return {
      kind: 'gif_rain', variant: variant.name,
      get live() { return live.rain; },
      retune() {},
      stop() { if (timer) ctx.timers.cancel(timer); timer = 0; },
    };
  }

  /* ---- ambient_field ----------------------------------------------------- */
  function startAmbient(opts = {}) {
    if (!hasDom()) return null;
    const chans = ctx.channels();
    const variant = ctx.variant('ambient_field', chans.bgIntensity, opts.kind || opts.variant);
    const kind = AMBIENT[variant.name] ? variant.name : 'motes';
    const def = AMBIENT[kind];
    const spec = ambientSpec(ctx.magnitude(ctx.ceiling('bgIntensity', opts.density)), opts.count || 16);
    if (!spec.on) return null;
    const nodes = [];
    // ambientSpec is PURE (spec.count <= NODE_CAPS.ambient); the lite twin is
    // composed here on ctx.lite(), exactly the flashBurstLite pick. Up to 24
    // forever-animating motes is the phone cost this trims to 10.
    const ambientCap = ctx.lite() ? NODE_CAPS.ambientLite : NODE_CAPS.ambient;
    const count = Math.max(1, Math.min(ambientCap, Math.round(spec.count * (ctx.motion() <= 0 ? 0.4 : 1))));
    for (let i = 0; i < count; i++) {
      const node = document.createElement('div');
      node.className = 'ae-mote' + (def.shape === 'dot' ? '' : ' ae-mote-' + def.shape);
      node.style.setProperty('--ae-x', Math.round(rand(ctx.rng, 2, 98)) + '%');
      node.style.setProperty('--ae-y', Math.round(rand(ctx.rng, 2, 98)) + '%');
      node.style.setProperty('--ae-size', Math.round(rand(ctx.rng, def.size[0], def.size[1])) + 'px');
      node.style.setProperty('--ae-dx', Math.round(rand(ctx.rng, def.dx[0], def.dx[1])) + 'px');
      node.style.setProperty('--ae-dy', Math.round(rand(ctx.rng, def.dy[0], def.dy[1])) + 'px');
      node.style.setProperty('--ae-dur', rand(ctx.rng, def.dur[0], def.dur[1]).toFixed(1) + 's');
      node.style.setProperty('--ae-delay', (-rand(ctx.rng, 0, def.dur[1])).toFixed(1) + 's');
      node.style.setProperty('--ae-alpha', String(Math.min(spec.alpha, clamp01(opts.alpha == null ? spec.alpha : ctx.pct(opts.alpha)))));
      node.style.setProperty('--ae-col', opts.color || def.col);
      if (ctx.reduced() || ctx.motion() <= 0) node.style.animation = 'none';
      ctx.layers.back.appendChild(node);
      ctx.timers.own(node);
      nodes.push(node);
    }
    ctx.fx('ambient_field', kind);
    return {
      kind: 'ambient_field', variant: kind,
      retune() { /* density changes are applied on the next sustain() call */ },
      stop() { for (const n of nodes) ctx.timers.release(n); nodes.length = 0; },
    };
  }

  /* ---- crt / scanline / chroma ------------------------------------------- */
  function startCrt(opts = {}) {
    if (!hasDom()) return null;
    const ceiling = ctx.ceiling('bgIntensity', opts.level);
    if (ceiling <= 0.001) return null;        // bgIntensity capped off -> no dressing
    const variant = ctx.variant('crt', ceiling, opts.variant);
    const level = ctx.strobe(ceiling);
    const node = document.createElement('div');
    node.className = 'ae-crt ae-crt-' + variant.name + ((ctx.reduced() || ctx.motion() <= 0) ? '' : ' ae-crt-live');
    ctx.layers.mid.appendChild(node);
    ctx.timers.own(node);
    // one frame later so the opacity transition actually runs
    ctx.timers.after(16, () => { node.style.opacity = String(clamp01(0.10 + level * 0.45)); });
    /* W3 P1-17: a CRT coming up is a room getting a machine in it. The seep's
     * own hum, quieter than the tell that owns it, once for the wave. */
    ctx.sfx('seep_hum', 0.12);
    ctx.fx('crt', variant.name);
    return {
      kind: 'crt', variant: variant.name,
      retune() {
        const lv = ctx.strobe(ctx.ceiling('bgIntensity', opts.level));
        node.style.opacity = String(clamp01(0.10 + lv * 0.45));
      },
      stop() { node.style.opacity = '0'; ctx.timers.after(600, () => ctx.timers.release(node)); },
    };
  }

  const STARTERS = {
    row_drift: startRowDrift,
    bubble_field: startBubbleField,
    wash: startWash,
    gif_rain: startGifRain,
    ambient_field: startAmbient,
    crt: startCrt,
  };

  function sustain(kind, opts) {
    const starter = STARTERS[kind];
    if (!starter) return null;
    // wash is special: a re-trigger refreshes the SAME element per wash kind
    if (kind !== 'wash' && active.has(kind)) {
      const h = active.get(kind);
      if (opts && opts.restart) { stop(kind, true); }
      else { try { h.retune(); } catch { /* ignore */ } return h; }
    }
    const handle = starter(opts || {});
    if (handle && kind !== 'wash') active.set(kind, handle);
    return handle;
  }

  function stop(kind, immediate) {
    if (kind === 'wash') {
      for (const [, h] of washHolds) {
        if (h.hideTimer) { ctx.timers.cancel(h.hideTimer); h.hideTimer = 0; }
        h.forever = false; h.heldAlpha = 0;
        h.el.style.opacity = '0';
        loomActive(h, false);
        if (immediate) { loomDrop(h); ctx.timers.release(h.el); }
      }
      if (immediate) washHolds.clear();
      return true;
    }
    const h = active.get(kind);
    if (!h) return false;
    active.delete(kind);
    try { h.stop(); } catch { /* ignore */ }
    return true;
  }

  /**
   * stopAll(immediate, quiet)
   * W3 P1-17: the teardown tears six things down on one frame, so it gets ONE
   * cue and not six (trap 111) - the wash recipe pitched down, quiet, the exit
   * half of every entrance above. Two guards on it. It only speaks when
   * something was actually up (a stopAll over an empty stage is not a beat),
   * and `quiet` silences it for the SUSPEND path: a panic, a mandatory video
   * or an audio-only flip is the one teardown that must make no sound at all.
   */
  function stopAll(immediate, quiet) {
    const hadAnything = !quiet && (active.size > 0 || washHolds.size > 0);
    for (const kind of [...active.keys()]) stop(kind, immediate);
    stop('wash', immediate);
    lastWashKind = null;
    live.rain = 0;
    live.bubbles = 0;
    if (hadAnything) ctx.sfx('wash', 0.12, { pitch: 0.8 });
  }

  function retuneAll() {
    for (const [, h] of active) { try { h.retune(); } catch { /* ignore */ } }
  }

  return { sustain, stop, stopAll, retuneAll, warmSpiral, live, active, AMBIENT_KINDS };
}

export default createSustained;
