/* ============================================================================
 * exec/bouncingText.js — GoonElement.BouncingText (7).
 *
 * 1-3 DVD-idler phrases drifting across #gg-fx-bounce, bouncing off the window
 * edges. Same word pool as the subliminal bed (exec/subliminals.js owns the
 * resolution order) so the two elements never read as different games.
 *
 * The travel is a rAF loop writing translate3d — NOT a CSS keyframe — because a
 * keyframe cannot honour a live intensity change or a window resize mid-flight,
 * and this element runs for minutes at a time. Under node (no rAF) it falls back
 * to an interval, so the module stays import-safe and testable headless.
 *
 * There is no BouncingText PAYLOAD kind in the v1 protocol; renderPayload() is
 * implemented anyway so the executor can treat every renderer identically, and
 * so a future kind needs no new plumbing here.
 *
 * Uniform renderer shape — see the banner in exec/flashes.js.
 * ==========================================================================*/

import { resolvePhrases } from './subliminals.js';

const MAX_WORDS = 3;
const FALLBACK_TICK_MS = 33;   // headless / no-rAF cadence

const clamp01 = (n) => (typeof n === 'number' && n === n ? (n < 0 ? 0 : n > 1 ? 1 : n) : 0);
const lerp = (a, b, t) => a + (b - a) * clamp01(t);
const rand = (a, b) => a + Math.random() * (b - a);
const soon = (fn, ms) => {
  const t = setTimeout(fn, Math.max(0, ms | 0));
  if (t && typeof t.unref === 'function') t.unref();
  return t;
};
const reducedMotion = () => {
  try { return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch (_e) { return false; }
};
const hasRaf = () => typeof requestAnimationFrame === 'function' && typeof cancelAnimationFrame === 'function';

/** Cue intensity -> how many phrases, how fast, how present. */
export function bounceTuning(intensity, calm) {
  const i = clamp01(intensity);
  return {
    count: Math.min(MAX_WORDS, 1 + Math.floor(i * 2.99)),
    speed: lerp(45, 210, i) * (calm ? 0.45 : 1),   // px/sec
    opacity: +lerp(0.30, 0.85, i).toFixed(3),
  };
}

export function createBouncingText({ layers, media, audio, logger, phrases } = {}) {
  const log = logger || null;
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:bounce] ${m}`); };
  const calm = reducedMotion();

  const words = resolvePhrases(phrases);
  const movers = [];        // {node, x, y, vx, vy, w, h}
  let intensity = 0;
  let running = false;
  let raf = 0;
  let interval = 0;
  let lastTs = 0;
  let extra = 0;            // payload-driven bonus phrases

  const layer = () => (layers && typeof layers.get === 'function' ? layers.get('bounce') : null);
  const viewport = () => ({
    w: (typeof window !== 'undefined' && window.innerWidth) || 1280,
    h: (typeof window !== 'undefined' && window.innerHeight) || 720,
  });

  function makeMover() {
    const host = layer();
    if (!host || typeof document === 'undefined') return null;
    const node = document.createElement('div');
    node.className = 'gg-bounce-word';
    node.textContent = words[(Math.random() * words.length) | 0] || 'hold';
    host.appendChild(node);

    const vp = viewport();
    const tune = bounceTuning(intensity, calm);
    const angle = rand(0.35, 1.2) * (Math.random() < 0.5 ? 1 : -1);
    const m = {
      node,
      w: node.offsetWidth || 220,
      h: node.offsetHeight || 34,
      x: rand(0.1, 0.7) * vp.w,
      y: rand(0.1, 0.8) * vp.h,
      vx: Math.cos(angle) * tune.speed * (Math.random() < 0.5 ? 1 : -1),
      vy: Math.sin(angle) * tune.speed,
    };
    node.style.setProperty('--gg-bounce-op', String(tune.opacity));
    place(m);
    return m;
  }

  function place(m) {
    if (!m.node || !m.node.style) return;
    m.node.style.transform = `translate3d(${Math.round(m.x)}px, ${Math.round(m.y)}px, 0)`;
  }

  function hit(m) {
    if (!m.node || !m.node.classList) return;
    m.node.classList.remove('is-hit');
    // reflow so the animation restarts; offsetWidth read is the cheapest force
    void (m.node.offsetWidth);
    m.node.classList.add('is-hit');
  }

  function syncCount() {
    const want = Math.min(MAX_WORDS, bounceTuning(intensity, calm).count + extra);
    while (movers.length > want) {
      const m = movers.pop();
      try { m.node.remove(); } catch (_e) { /* ignore */ }
    }
    while (movers.length < want) {
      const m = makeMover();
      if (!m) break;
      movers.push(m);
    }
    const tune = bounceTuning(intensity, calm);
    for (const m of movers) {
      // Re-tune speed without losing direction, and refresh presence.
      const mag = Math.hypot(m.vx, m.vy) || 1;
      m.vx = (m.vx / mag) * tune.speed;
      m.vy = (m.vy / mag) * tune.speed;
      if (m.node && m.node.style) m.node.style.setProperty('--gg-bounce-op', String(tune.opacity));
    }
  }

  function step(nowMs) {
    if (!running) return;
    const now = (typeof nowMs === 'number' ? nowMs : Date.now());
    const dt = lastTs ? Math.min(0.1, (now - lastTs) / 1000) : 0.016;
    lastTs = now;
    const vp = viewport();

    for (const m of movers) {
      if (!m.w || !m.h) { m.w = (m.node && m.node.offsetWidth) || 220; m.h = (m.node && m.node.offsetHeight) || 34; }
      m.x += m.vx * dt;
      m.y += m.vy * dt;
      let bounced = false;
      if (m.x <= 0) { m.x = 0; m.vx = Math.abs(m.vx); bounced = true; }
      else if (m.x + m.w >= vp.w) { m.x = Math.max(0, vp.w - m.w); m.vx = -Math.abs(m.vx); bounced = true; }
      if (m.y <= 0) { m.y = 0; m.vy = Math.abs(m.vy); bounced = true; }
      else if (m.y + m.h >= vp.h) { m.y = Math.max(0, vp.h - m.h); m.vy = -Math.abs(m.vy); bounced = true; }
      place(m);
      if (bounced && !calm) hit(m);
    }
    schedule();
  }

  function schedule() {
    if (!running) return;
    if (hasRaf()) { raf = requestAnimationFrame(step); return; }
    if (!interval) {
      interval = setInterval(() => step(Date.now()), FALLBACK_TICK_MS);
      if (interval && typeof interval.unref === 'function') interval.unref();
    }
  }

  function halt() {
    running = false;
    if (raf && hasRaf()) { try { cancelAnimationFrame(raf); } catch (_e) { /* ignore */ } }
    raf = 0;
    if (interval) { clearInterval(interval); interval = 0; }
    lastTs = 0;
    for (const m of movers) { try { m.node.remove(); } catch (_e) { /* ignore */ } }
    movers.length = 0;
  }

  return {
    name: 'bouncingText',

    start(cue) {
      intensity = clamp01(cue && cue.intensity);
      if (running) { syncCount(); return; }
      running = true;
      syncCount();
      schedule();
    },

    setIntensity(v) {
      intensity = clamp01(v);
      if (running) syncCount();
    },

    stop() {
      extra = 0;
      halt();
    },

    /**
     * No BouncingText payload kind exists in v1 — this is the uniform-shape
     * stub: it thickens the field for duration_ms and receipts like any other.
     */
    renderPayload(payload, done) {
      const p = payload || {};
      const runMs = Math.max(1500, (p.duration_ms | 0) || 8000);
      const wasRunning = running;
      extra = 1;
      if (!running) { intensity = Math.max(intensity, clamp01(p.intensity !== undefined ? p.intensity : 0.6)); this.start({ intensity }); }
      else syncCount();

      let finished = false;
      let endTimer = 0;
      const settle = (endured) => {
        if (finished) return;
        finished = true;
        try { clearTimeout(endTimer); } catch (_e) { /* ignore */ }
        extra = 0;
        if (!wasRunning) halt(); else syncCount();
        if (typeof done === 'function') { try { done(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
      };
      endTimer = soon(() => settle(true), runMs);
      return () => settle(false);
    },
  };
}

export default createBouncingText;
