/* ============================================================================
 * effects.js — in-page effect layer for "Graded Intake" (Agent E).
 *
 * Self-contained DOM effects driven by ONE depth scalar + RewardEvents. NO
 * coupling to WPF App.Flash/App.Bubbles — everything is a DOM overlay mounted
 * inside the beat stage (`root`). Four visual channels ride the depth curve:
 *   flashes · subliminals · ambient bubbles · reward payloads (flash/bubble/drop/praise).
 *
 * CONTRACT (contracts.js §"EFFECTS + AUDIO (Agent E)"):
 *   createEffects({ root, caps }) -> {
 *     setDepth(depth),                 // depthToChannels -> clampToCaps -> cadence/opacity
 *     play(rewardEvent, depth),        // clampIntensity -> render by RewardKind
 *     recover(depth),                  // invariant #3: un-ramp; depth 0 = all off/removed
 *   }
 *
 * INVARIANTS honored here:
 *   #2 — every level flows from depthToChannels(depth) x clampToCaps(...,caps) or
 *        clampIntensity(intensity,caps). We NEVER hardcode an absolute strength;
 *        the constants below are ONLY 0..1 -> real-unit (ms / px / alpha) glue.
 *   #3 — recover() walks the stack down; recover(<=0) tears everything out.
 *
 * IMPORTS ARE SIDE-EFFECT FREE: no document/DOM access at module load. All DOM
 * work is guarded inside the factory + its methods, so importing this never
 * throws (a throw-at-import = silent infinite loader spin — see dtrh gotchas).
 *
 * The pure mappings (channel vector -> render numbers) are exported for headless
 * tests; the live path calls them too so the tested math IS the shipped math.
 * ==========================================================================*/

import { depthToChannels, clampToCaps, clampIntensity, RewardKind, lerp, clamp01 } from '../core/contracts.js';

/* ----------------------------------------------------------------------------
 * PURE MAPPING — clamped channel vector -> concrete render numbers.
 * Only 0..1 -> real-unit translation lives here (spawn intervals in ms, peak
 * alphas). The CURVE + CAPS already happened upstream (depthToChannels x caps);
 * this never re-derives either. Exported + unit-tested headless.
 * -------------------------------------------------------------------------- */

/** Interval range endpoints (ms). A channel of 0 => Infinity (silent/off). */
const FLASH_MS  = { slow: 1600, fast: 110 };
const SUB_MS    = { slow: 2600, fast: 360 };
const BUBBLE_MS = { slow: 2200, fast: 260 };

/** Comfort ceiling on the full-screen flash wash so cap=1 is bright, not blinding.
 *  This is a safety clamp on the OUTPUT alpha, not a re-derivation of the curve. */
const FLASH_ALPHA_CEIL = 0.85;

const _finiteInterval = (active, rate, slow, fast) =>
  active ? lerp(slow, fast, clamp01(rate)) : Infinity;

/**
 * @param {import('../core/contracts.js').Channels} ch  ALREADY clamped to caps.
 * @returns {{
 *   flashOn:boolean, flashMs:number, flashAlpha:number,
 *   subOn:boolean, subMs:number, subAlpha:number,
 *   bubbleOn:boolean, bubbleMs:number, bubbleAlpha:number
 * }}
 */
export function channelsToVisual(ch) {
  const flashRate    = clamp01(ch && ch.flashRate);
  const flashOpacity = clamp01(ch && ch.flashOpacity);
  const subDensity   = clamp01(ch && ch.subDensity);
  const bubbleRate   = clamp01(ch && ch.bubbleRate);

  const flashOn  = flashRate  > 0.001;
  const subOn    = subDensity > 0.001;
  const bubbleOn = bubbleRate > 0.001;

  return {
    flashOn,
    flashMs:    _finiteInterval(flashOn, flashRate, FLASH_MS.slow, FLASH_MS.fast),
    // flashOpacity is the peak; cap already applied, comfort ceiling on top.
    flashAlpha: flashOpacity * FLASH_ALPHA_CEIL,

    subOn,
    subMs:    _finiteInterval(subOn, subDensity, SUB_MS.slow, SUB_MS.fast),
    // subliminals are faint by design: 0.05 floor + up to +0.35.
    subAlpha: subOn ? (0.05 + subDensity * 0.35) : 0,

    bubbleOn,
    bubbleMs:    _finiteInterval(bubbleOn, bubbleRate, BUBBLE_MS.slow, BUBBLE_MS.fast),
    bubbleAlpha: bubbleOn ? (0.10 + bubbleRate * 0.40) : 0,
  };
}

/** Reward intensity (already clampIntensity'd) -> flash-pulse numbers. Pure. */
export function rewardFlashSpec(intensity) {
  const i = clamp01(intensity);
  return { alpha: (0.25 + i * 0.60) * FLASH_ALPHA_CEIL, durMs: 220 + i * 260 };
}
/** Reward intensity -> particle-burst numbers (drop / bubble reward). Pure. */
export function rewardBurstSpec(intensity) {
  const i = clamp01(intensity);
  return { count: Math.round(4 + i * 12), spreadPx: 60 + i * 160, durMs: 620 + i * 640 };
}

/* Faint, niche-agnostic subliminal pool. Niche-specific words come from prompts/
 * AI upstream (Agents A/H) — the effect layer stays persona-neutral on purpose. */
const SUBLIMINAL_WORDS = [
  'focus', 'deeper', 'relax', 'soft', 'yes', 'sink',
  'listen', 'good', 'let go', 'drift', 'easy', 'obey',
];
const PRAISE_WORDS = ['good', 'perfect', 'so good', 'yes', 'well done', 'gooood'];

/* ----------------------------------------------------------------------------
 * SCOPED STYLES — injected once, guarded. Class prefix `ixfx-`.
 * -------------------------------------------------------------------------- */
const STYLE_ID = 'ixfx-styles';
const CSS = `
.ixfx-root{position:fixed;inset:0;z-index:6;pointer-events:none;overflow:hidden;
  contain:strict;}
.ixfx-flash{position:absolute;inset:0;background:radial-gradient(120% 120% at 50% 45%,
  #fff 0%,#ffd9f2 55%,#ffb3e6 100%);opacity:0;will-change:opacity;}
.ixfx-sub{position:absolute;color:#fff;font-weight:700;letter-spacing:.06em;
  text-transform:lowercase;white-space:nowrap;opacity:0;will-change:opacity,transform;
  text-shadow:0 0 12px rgba(255,105,180,.5);mix-blend-mode:screen;}
.ixfx-bubble{position:absolute;border-radius:50%;
  background:radial-gradient(circle at 35% 30%,rgba(255,255,255,.9),
  rgba(255,105,180,.35) 55%,rgba(176,108,255,.15) 100%);
  box-shadow:0 0 14px rgba(255,105,180,.35);opacity:0;will-change:transform,opacity;}
.ixfx-particle{position:absolute;width:10px;height:10px;border-radius:50%;
  background:radial-gradient(circle at 40% 35%,#fff,#ff69b4 60%,#b06cff 100%);
  opacity:0;will-change:transform,opacity;}
.ixfx-praise{position:absolute;left:50%;top:46%;transform:translate(-50%,-50%);
  color:#fff;font-weight:800;font-size:clamp(28px,7vw,68px);letter-spacing:.02em;
  text-transform:lowercase;opacity:0;will-change:opacity,transform;
  text-shadow:0 0 24px rgba(255,105,180,.8),0 0 60px rgba(176,108,255,.5);}
@media (prefers-reduced-motion: reduce){
  .ixfx-sub,.ixfx-bubble,.ixfx-particle{transition:none;}
}`;

function ensureStyles() {
  if (typeof document === 'undefined') return;
  if (document.getElementById(STYLE_ID)) return;
  try {
    const s = document.createElement('style');
    s.id = STYLE_ID;
    s.textContent = CSS;
    (document.head || document.documentElement).appendChild(s);
  } catch (_e) { /* non-fatal */ }
}

/* ----------------------------------------------------------------------------
 * FACTORY
 * -------------------------------------------------------------------------- */
export function createEffects({ root, caps } = {}) {
  const hasDOM = typeof document !== 'undefined' && !!root;
  const supportsAnim = hasDOM && typeof Element !== 'undefined' &&
    typeof Element.prototype.animate === 'function';

  let layer = null;         // .ixfx-root container
  let flashEl = null;       // the full-screen wash
  let mounted = false;

  // current visual params (from the last setDepth)
  let vis = channelsToVisual(clampToCaps(depthToChannels(0), capsOf()));
  let depthNow = 0;

  // rAF spawn driver
  let rafId = 0;
  let running = false;
  let lastFlash = 0, lastSub = 0, lastBubble = 0;

  // live spawned nodes (for teardown on recover(0))
  const live = new Set();

  function capsOf() { return caps || undefined; }

  function mount() {
    if (mounted || !hasDOM) return;
    ensureStyles();
    try {
      layer = document.createElement('div');
      layer.className = 'ixfx-root';
      layer.setAttribute('aria-hidden', 'true');
      flashEl = document.createElement('div');
      flashEl.className = 'ixfx-flash';
      layer.appendChild(flashEl);
      root.appendChild(layer);
      mounted = true;
    } catch (_e) { mounted = false; }
  }

  function track(el, removeAfterMs) {
    live.add(el);
    if (removeAfterMs != null) {
      setTimeout(() => { removeNode(el); }, removeAfterMs);
    }
  }
  function removeNode(el) {
    live.delete(el);
    try { if (el && el.parentNode) el.parentNode.removeChild(el); } catch (_e) {}
  }

  /* ----- rAF loop: ambient flashes / subliminals / bubbles by cadence ------- */
  function loop(now) {
    if (!running) return;
    if (vis.flashOn && now - lastFlash >= vis.flashMs) { lastFlash = now; ambientFlash(); }
    if (vis.subOn && now - lastSub >= vis.subMs) { lastSub = now; spawnSubliminal(); }
    if (vis.bubbleOn && now - lastBubble >= vis.bubbleMs) { lastBubble = now; spawnBubble(); }
    rafId = requestAnimationFrame(loop);
  }
  function startLoop() {
    if (running || !mounted) return;
    running = true;
    const t = (typeof performance !== 'undefined' ? performance.now() : Date.now());
    lastFlash = lastSub = lastBubble = t;
    rafId = requestAnimationFrame(loop);
  }
  function stopLoop() {
    running = false;
    if (rafId) { try { cancelAnimationFrame(rafId); } catch (_e) {} rafId = 0; }
  }

  /* ----- primitive spawns --------------------------------------------------- */
  function pulseFlash(alpha, durMs) {
    if (!flashEl) return;
    if (supportsAnim) {
      try {
        flashEl.animate(
          [{ opacity: 0 }, { opacity: alpha, offset: 0.35 }, { opacity: 0 }],
          { duration: durMs, easing: 'ease-out' });
        return;
      } catch (_e) { /* fall through */ }
    }
    // Fallback: opacity blip via timeout.
    flashEl.style.opacity = String(alpha);
    setTimeout(() => { if (flashEl) flashEl.style.opacity = '0'; }, Math.max(40, durMs * 0.4));
  }
  function ambientFlash() { pulseFlash(vis.flashAlpha, 180); }

  function spawnSubliminal() {
    if (!layer) return;
    try {
      const el = document.createElement('div');
      el.className = 'ixfx-sub';
      el.textContent = SUBLIMINAL_WORDS[(Math.random() * SUBLIMINAL_WORDS.length) | 0];
      el.style.left = (8 + Math.random() * 74) + '%';
      el.style.top = (12 + Math.random() * 66) + '%';
      el.style.fontSize = (16 + Math.random() * 34) + 'px';
      layer.appendChild(el);
      const peak = vis.subAlpha;
      const dur = 900 + Math.random() * 900;
      if (supportsAnim) {
        const a = el.animate(
          [{ opacity: 0, transform: 'scale(.9)' },
           { opacity: peak, offset: 0.4 },
           { opacity: 0, transform: 'scale(1.08)' }],
          { duration: dur, easing: 'ease-in-out' });
        a.onfinish = () => removeNode(el);
        track(el, dur + 200);
      } else {
        el.style.opacity = String(peak);
        track(el, dur);
      }
    } catch (_e) {}
  }

  function spawnBubble() {
    if (!layer) return;
    try {
      const el = document.createElement('div');
      el.className = 'ixfx-bubble';
      const size = 16 + Math.random() * 42;
      el.style.width = el.style.height = size + 'px';
      el.style.left = (Math.random() * 92) + '%';
      el.style.bottom = '-8%';
      layer.appendChild(el);
      const peak = vis.bubbleAlpha;
      const dur = 3200 + Math.random() * 2600;
      const drift = (Math.random() * 60 - 30);
      if (supportsAnim) {
        const a = el.animate(
          [{ opacity: 0, transform: 'translate(0,0) scale(.6)' },
           { opacity: peak, offset: 0.2 },
           { opacity: peak, offset: 0.8 },
           { opacity: 0, transform: `translate(${drift}px,-115vh) scale(1)` }],
          { duration: dur, easing: 'ease-in' });
        a.onfinish = () => removeNode(el);
        track(el, dur + 200);
      } else {
        el.style.opacity = String(peak);
        track(el, dur);
      }
    } catch (_e) {}
  }

  function burst(spec, hue) {
    if (!layer) return;
    try {
      const cx = 40 + Math.random() * 20, cy = 38 + Math.random() * 24;
      for (let i = 0; i < spec.count; i++) {
        const el = document.createElement('div');
        el.className = 'ixfx-particle';
        el.style.left = cx + '%';
        el.style.top = cy + '%';
        if (hue != null) el.style.filter = `hue-rotate(${hue}deg)`;
        layer.appendChild(el);
        const ang = Math.random() * Math.PI * 2;
        const dist = spec.spreadPx * (0.4 + Math.random() * 0.6);
        const dx = Math.cos(ang) * dist, dy = Math.sin(ang) * dist - dist * 0.3;
        if (supportsAnim) {
          const a = el.animate(
            [{ opacity: 1, transform: 'translate(-50%,-50%) scale(1)' },
             { opacity: 0, transform: `translate(calc(-50% + ${dx}px),calc(-50% + ${dy}px)) scale(.3)` }],
            { duration: spec.durMs, easing: 'cubic-bezier(.2,.8,.3,1)' });
          a.onfinish = () => removeNode(el);
          track(el, spec.durMs + 120);
        } else {
          track(el, spec.durMs);
        }
      }
    } catch (_e) {}
  }

  function praise(intensity) {
    if (!layer) return;
    try {
      const el = document.createElement('div');
      el.className = 'ixfx-praise';
      el.textContent = PRAISE_WORDS[(Math.random() * PRAISE_WORDS.length) | 0];
      layer.appendChild(el);
      const peak = 0.35 + clamp01(intensity) * 0.6;
      const dur = 900 + clamp01(intensity) * 700;
      if (supportsAnim) {
        const a = el.animate(
          [{ opacity: 0, transform: 'translate(-50%,-40%) scale(.8)' },
           { opacity: peak, offset: 0.35, transform: 'translate(-50%,-52%) scale(1.05)' },
           { opacity: 0, transform: 'translate(-50%,-64%) scale(1.15)' }],
          { duration: dur, easing: 'ease-out' });
        a.onfinish = () => removeNode(el);
        track(el, dur + 150);
      } else {
        el.style.opacity = String(peak);
        track(el, dur);
      }
    } catch (_e) {}
  }

  /* ----- public API --------------------------------------------------------- */

  /** Drive the ambient stack from one depth scalar (invariant #2: via caps). */
  function setDepth(depth) {
    depthNow = clamp01(depth);
    const ch = clampToCaps(depthToChannels(depthNow), capsOf());
    vis = channelsToVisual(ch);
    if (!hasDOM) return;
    if (depthNow <= 0.0005) { // nothing to show
      stopLoop();
      // leave existing spawned nodes to finish; teardown is recover(0)'s job.
      return;
    }
    mount();
    startLoop();
  }

  /** Render a resolved reward by kind, scaled by clamped intensity (invariant #2). */
  function play(rewardEvent, depth) {
    if (!rewardEvent || !rewardEvent.fire) return;
    if (!hasDOM) return;
    mount();
    const intensity = clampIntensity(rewardEvent.intensity, capsOf());
    if (intensity <= 0.0005) return;
    const kind = rewardEvent.kind;
    switch (kind) {
      case RewardKind.Flash: {
        const s = rewardFlashSpec(intensity); pulseFlash(s.alpha, s.durMs); break;
      }
      case RewardKind.Bubble: {
        // a bright reward bubble burst + one ambient bubble
        burst(rewardBurstSpec(intensity)); spawnBubble(); break;
      }
      case RewardKind.Drop: {
        // gif-burst stand-in: a shower of particles (no asset dependency here).
        burst(rewardBurstSpec(intensity), 40); break;
      }
      case RewardKind.Praise: {
        praise(intensity); break;
      }
      case RewardKind.Chime: {
        // audio owns the sound; give it a small sparkle so it reads on-screen.
        burst({ count: Math.round(3 + intensity * 5), spreadPx: 40 + intensity * 70, durMs: 480 }); break;
      }
      case RewardKind.None:
      default: break;
    }
  }

  /** Invariant #3: un-ramp toward 0. depth<=0 tears the whole layer out. */
  function recover(depth) {
    const d = clamp01(depth);
    if (d > 0.0005) { setDepth(d); return; }
    // full surfacing: stop spawning, drop every live node, hide + remove layer.
    depthNow = 0;
    vis = channelsToVisual(clampToCaps(depthToChannels(0), capsOf()));
    stopLoop();
    if (flashEl) flashEl.style.opacity = '0';
    for (const el of Array.from(live)) removeNode(el);
    live.clear();
    if (layer) { try { if (layer.parentNode) layer.parentNode.removeChild(layer); } catch (_e) {} }
    layer = null; flashEl = null; mounted = false;
  }

  return { setDepth, play, recover };
}
