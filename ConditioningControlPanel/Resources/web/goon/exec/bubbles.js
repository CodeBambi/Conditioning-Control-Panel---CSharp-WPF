/* ============================================================================
 * exec/bubbles.js — GoonElement.Bubbles (3) + GoonPayloadKind.BubbleSwarm (2).
 *
 * A passive field of rising, swaying bubbles on #gg-fx-bubbles. Rise + sway are
 * CSS keyframes (ggBubbleRise / ggBubbleSway in fx.css, re-namespaced from the
 * Fall's rhBubbleRise) so the whole field lives on the compositor and costs the
 * main thread nothing but its spawn timer.
 *
 * NON-INTERACTIVE BY CONTRACT. #gg-fx is pointer-events:none and nothing here
 * opts back in: the poppable bubbles are the Bubble Race sudden-death round,
 * which is a different module owned by a different tier. If a bubble here ever
 * becomes clickable, a player mid-duel will try to pop it and lose the round.
 *
 * Uniform renderer shape — see the banner in exec/flashes.js.
 * ==========================================================================*/

const MAX_LIVE = 40;      // hard ceiling on bubble nodes, swarm included
const POOL_MAX = 24;      // retired wrappers kept for reuse

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

/** Cue intensity -> field density. */
export function bubbleTuning(intensity, calm) {
  const i = clamp01(intensity);
  return {
    gapMs: Math.round(lerp(1100, 180, i) * (calm ? 1.8 : 1)),
    riseMs: Math.round(lerp(15000, 8000, i) * (calm ? 1.4 : 1)),
    size: Math.round(lerp(28, 64, i)),
    opacity: +lerp(0.35, 0.75, i).toFixed(3),
  };
}

export function createBubbles({ layers, media, audio, logger } = {}) {
  const log = logger || null;
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:bubbles] ${m}`); };
  const calm = reducedMotion();

  const free = [];
  let live = 0;
  let sustained = null;

  const layer = () => (layers && typeof layers.get === 'function' ? layers.get('bubbles') : null);

  function takeWrap() {
    const n = free.pop();
    if (n) return n;
    if (typeof document === 'undefined') return null;
    const wrap = document.createElement('div');
    wrap.className = 'gg-bubble-wrap';
    const ball = document.createElement('div');
    ball.className = 'gg-bubble';
    wrap.appendChild(ball);
    return wrap;
  }

  function spawn(tune) {
    const host = layer();
    if (!host || live >= MAX_LIVE) return;
    const wrap = takeWrap();
    if (!wrap) return;
    live++;

    const size = Math.round(tune.size * rand(0.7, 1.35));
    const riseMs = Math.round(tune.riseMs * rand(0.8, 1.25));
    wrap.style.left = `${rand(-2, 98).toFixed(1)}%`;
    wrap.style.setProperty('--gg-rise', `${riseMs}ms`);
    const ball = wrap.firstChild;
    if (ball && ball.style) {
      ball.style.setProperty('--gg-bub-size', `${size}px`);
      ball.style.setProperty('--gg-bub-op', String(tune.opacity * rand(0.75, 1)));
      ball.style.setProperty('--gg-sway', `${Math.round(rand(14, 48))}px`);
      ball.style.setProperty('--gg-sway-dur', `${(rand(2.4, 5.2)).toFixed(2)}s`);
    }
    host.appendChild(wrap);

    // Retire on the timer, not on animationend: a throttled/hidden tab may never
    // deliver the event, and a leaked node is a leak for the rest of the match.
    soon(() => {
      live = Math.max(0, live - 1);
      try { wrap.remove(); } catch (_e) { /* ignore */ }
      if (free.length < POOL_MAX) free.push(wrap);
    }, riseMs + 120);
  }

  function loop(run) {
    if (!run.alive) return;
    const tune = bubbleTuning(run.intensity, calm);
    spawn(tune);
    run.timer = soon(() => loop(run), rand(tune.gapMs * 0.6, tune.gapMs * 1.4));
  }

  return {
    name: 'bubbles',

    start(cue) {
      const intensity = clamp01(cue && cue.intensity);
      if (sustained) { sustained.intensity = intensity; return; }
      sustained = { alive: true, intensity, timer: 0 };
      // Seed a few so the field does not fade in one bubble at a time.
      const tune = bubbleTuning(intensity, calm);
      for (let i = 0; i < 3; i++) soon(() => { if (sustained && sustained.alive) spawn(tune); }, i * 260);
      loop(sustained);
    },

    setIntensity(v) { if (sustained) sustained.intensity = clamp01(v); },

    stop() {
      if (!sustained) return;
      sustained.alive = false;
      try { clearTimeout(sustained.timer); } catch (_e) { /* ignore */ }
      sustained = null;
      // Airborne bubbles are left to finish their rise — yanking 30 nodes
      // mid-flight reads as a glitch, and each one retires on its own timer.
    },

    /** BubbleSwarm: a wave. Denser and bigger than any bed the ramp asks for. */
    renderPayload(payload, done) {
      const p = payload || {};
      const runMs = Math.max(1500, (p.duration_ms | 0) || 10000);
      const run = {
        alive: true,
        intensity: Math.max(0.65, clamp01(p.intensity !== undefined ? p.intensity : 0.75)),
        timer: 0,
        endTimer: 0,
      };

      let finished = false;
      const settle = (endured) => {
        if (finished) return;
        finished = true;
        run.alive = false;
        try { clearTimeout(run.timer); } catch (_e) { /* ignore */ }
        try { clearTimeout(run.endTimer); } catch (_e) { /* ignore */ }
        if (typeof done === 'function') { try { done(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
      };

      const waveLoop = () => {
        if (!run.alive) return;
        const tune = bubbleTuning(run.intensity, calm);
        tune.gapMs = Math.round(tune.gapMs * 0.35);
        spawn(tune);
        spawn(tune);
        run.timer = soon(waveLoop, rand(tune.gapMs * 0.6, tune.gapMs * 1.4));
      };
      waveLoop();
      run.endTimer = soon(() => settle(true), runMs);
      return () => settle(false);
    },
  };
}

export default createBubbles;
