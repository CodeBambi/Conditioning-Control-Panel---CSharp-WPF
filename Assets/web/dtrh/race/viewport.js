/* ============================================================================
 * race/viewport.js - the aspect rule and the resize plumbing every race camera
 * shares. Implements CONTRACT.md "Camera aspect rule".
 *
 *   vFovForAspect(baseVFov, aspect, maxVFov) -> vertical fov in degrees
 *   hFovFor(vFov, aspect)                    -> the horizontal fov that pair gives
 *   bindViewportResize(fn)                   -> dispose()
 *
 * THE RULE. A THREE.PerspectiveCamera fov is VERTICAL, so a fixed number keeps
 * the same amount of sky at every shape of window and throws the width away.
 * On a 390x844 phone the run's 72 vertical collapses to about 37 horizontal:
 * the road is a slit and both side lanes sit off-screen. So the run does not own
 * a vertical fov at all, it owns a HORIZONTAL one, measured once at 16:9 from
 * the number the desktop look was built on, and every other aspect solves back
 * for the vertical that keeps that width:
 *
 *   hFov = 2 * atan(tan(base / 2) * 16 / 9)        (the constant we protect)
 *   vFov = clamp(2 * atan(tan(hFov / 2) / aspect), MIN, max)
 *
 * At exactly 16:9 the two cancel and vFov === base, so nothing on a desktop
 * moves by a pixel. Narrower than 16:9 the vertical opens up until the clamp;
 * wider than 16:9 it closes down, which is what keeps an ultrawide from being
 * a fisheye. The clamp is the whole safety net: without it a 9:19.5 phone asks
 * for about 141 vertical and the road turns into a lens flare.
 *
 * Nothing here touches three or the DOM at import time, so race/smoke/fov-check.mjs
 * can run it under bare node.
 * ==========================================================================*/

const DEG = Math.PI / 180;
/** The shape the desktop look was authored at. Every base fov in the game is read as "at 16:9". */
export const REF_ASPECT = 16 / 9;
/** Past this the road stops reading as a road and starts reading as a fisheye tunnel (picked on a 390x844 shot). */
export const MAX_VFOV = 102;
/** A floor so an absurd ultrawide cannot squeeze the camera down to a telescope. */
export const MIN_VFOV = 28;

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

/** The horizontal fov (degrees) a vertical fov covers at `aspect` (w / h). */
export function hFovFor(vFovDeg, aspect) {
  return 2 * Math.atan(Math.tan(vFovDeg * 0.5 * DEG) * Math.max(0.01, aspect)) / DEG;
}

/**
 * The vertical fov (degrees) that holds `baseVFovDeg`'s 16:9 WIDTH at `aspect`.
 * Returns `baseVFovDeg` unchanged at 16:9. Clamped to [MIN_VFOV, maxVFovDeg].
 */
export function vFovForAspect(baseVFovDeg, aspect, maxVFovDeg = MAX_VFOV) {
  const a = Math.max(0.01, +aspect || REF_ASPECT);
  const halfW = Math.tan(clamp(+baseVFovDeg || 1, 1, 175) * 0.5 * DEG) * REF_ASPECT;
  const v = 2 * Math.atan(halfW / a) / DEG;
  return clamp(v, MIN_VFOV, Math.max(MIN_VFOV, maxVFovDeg));
}

/**
 * Every event that can change the shape of the viewport, in one place.
 * `resize` alone misses an iOS orientation flip (it fires with the OLD size) and
 * misses the URL bar sliding away on mobile Safari, which only moves
 * `visualViewport`. So: listen to all three, and re-run one frame after an
 * orientation flip because the first report is stale there.
 * Returns a dispose() that takes every listener back off.
 */
export function bindViewportResize(fn) {
  if (typeof window === 'undefined' || typeof fn !== 'function') return () => {};
  let pending = 0;
  const later = () => {
    if (pending) cancelAnimationFrame(pending);
    pending = requestAnimationFrame(() => { pending = 0; fn(); });
  };
  const onFlip = () => { fn(); later(); };
  const vv = window.visualViewport || null;
  window.addEventListener('resize', fn);
  window.addEventListener('orientationchange', onFlip);
  if (vv && vv.addEventListener) vv.addEventListener('resize', fn);
  return () => {
    if (pending) cancelAnimationFrame(pending);
    pending = 0;
    window.removeEventListener('resize', fn);
    window.removeEventListener('orientationchange', onFlip);
    if (vv && vv.removeEventListener) vv.removeEventListener('resize', fn);
  };
}
