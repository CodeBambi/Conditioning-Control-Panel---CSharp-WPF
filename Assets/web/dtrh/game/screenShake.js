/* ============================================================================
 * screenShake.js - the run-config `screenShake` / `shakeIntensity` dials, rendered.
 *
 * The host has always sent both knobs down with run-config (the WPF game feeds
 * them to App.ScreenShake); the page had no consumer, so the setting was a dead
 * wire in DtRH. This is that consumer: a translate-only jolt on the 3D canvas.
 *
 * WHY THE CANVAS, NOT THE HUD: the tube is the world, and #sf-hud carries the
 * score/dock chrome AND every DOM overlay (bubble field, payloadFx). Shaking the
 * hud would jitter unreadable text and double-shake anything already animating.
 * The canvas is a single fixed, GPU-composited element that nothing else
 * transforms, so the world can rock while the readouts hold still.
 *
 * Modest by design: amplitude tops out at MAX_PX * shakeIntensity and decays to
 * nothing inside MAX_MS. Overlapping triggers EXTEND the window and take the
 * louder amplitude - they never stack into a seizure. Hard off when the user's
 * dial is off or the OS asks for reduced motion.
 * ==========================================================================*/

const MAX_PX = 8;     // peak offset at amount 1.0 x intensity 1.0
const MAX_MS = 400;   // longest a single jolt may ring for

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));

export function createScreenShake({ el } = {}) {
  const reduced = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  let enabled = true;
  let intensity = 0.8;   // matches ChaosRunConfig.ShakeIntensity's default
  let peak = 0;          // current amplitude in px
  let spanMs = MAX_MS;   // the window the decay is measured against
  let endAt = 0;
  let raf = 0;
  let disposed = false;

  const live = () => !disposed && !!el && enabled && !reduced;

  function rest() {
    if (raf) { cancelAnimationFrame(raf); raf = 0; }
    peak = 0; endAt = 0;
    if (el) el.style.transform = '';
  }

  function step() {
    raf = 0;
    if (disposed || !el) return;
    const left = endAt - performance.now();
    if (left <= 0 || peak <= 0) { rest(); return; }
    const decay = clamp(left / spanMs, 0, 1);
    const a = peak * decay * decay;   // quadratic ring-down: hits hard, settles fast
    const x = (Math.random() * 2 - 1) * a;
    const y = (Math.random() * 2 - 1) * a;
    el.style.transform = `translate3d(${x.toFixed(2)}px, ${y.toFixed(2)}px, 0)`;
    raf = requestAnimationFrame(step);
  }

  return {
    /** Jolt the world. amount 0..1 = how heavy the moment was; ms is clamped to MAX_MS. */
    shake(amount = 1, ms = 260) {
      if (!live()) return;
      const a = clamp(amount, 0, 1) * clamp(intensity, 0, 1) * MAX_PX;
      if (a < 0.1) return;
      const now = performance.now();
      const dur = clamp(ms, 60, MAX_MS);
      // extend-not-stack: keep whichever amplitude is louder RIGHT NOW and push
      // the deadline out, so a burst of triggers reads as one longer rumble.
      const left = Math.max(0, endAt - now);
      const decay = spanMs > 0 ? clamp(left / spanMs, 0, 1) : 0;
      peak = Math.max(a, peak * decay);
      endAt = Math.max(endAt, now + dur);
      spanMs = Math.max(dur, endAt - now);
      if (!raf) raf = requestAnimationFrame(step);
    },
    /** The user's `screenShake` dial (run-config). Off stands the effect fully down. */
    setEnabled(on) {
      enabled = !!on;
      if (!enabled) rest();
    },
    /** The user's `shakeIntensity` dial, 0..1 (0 = silent, same as off). */
    setIntensity(v) {
      intensity = clamp(Number.isFinite(v) ? v : 0.8, 0, 1);
    },
    /** Drop any jolt in flight (run end / teardown) - the canvas returns to centre. */
    stop: rest,
    dispose() {
      rest();
      disposed = true;
      el = null;
    },
  };
}

export default createScreenShake;
