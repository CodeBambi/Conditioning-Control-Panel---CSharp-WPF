/* ============================================================================
 * exec/perfTier.js — THE DEVICE TIER. One question, answered in one place:
 * is this machine allowed the full effect stack, or the lite one?
 *
 * WHY THIS EXISTS (2026-08-05). The owner play-tested a real 1v1 on an iPhone
 * 13 Pro Max — no power-save, nothing else running — and got lag and skipped
 * frames during live matches. The busy screen is the same one the desktop
 * renders effortlessly: ~20 drop-shadowed flash <img>s, a 26-bubble field, a
 * fullscreen two-layer WebGL spiral under a screen blend, up to four decoding
 * <video> windows, and a fullscreen backdrop-filter drain pane. A phone GPU
 * does not get to vote on any of that unless something tells the renderers to
 * ask for less — this module is that something.
 *
 * TWO TIERS, NOT A DIAL. 'full' is the page exactly as it has always been;
 * 'lite' lowers the concurrent-node caps, halves the spiral's frame rate and
 * resolution, and swaps the iOS-catastrophic CSS (backdrop-filter, the big
 * per-node drop-shadows) for flat equivalents. A dial would invite per-device
 * tuning nobody can reproduce; a tier is a fact a bug report can name.
 *
 * HOW THE ANSWER TRAVELS — the same road every exec-visible preference already
 * takes (data-gg-vskip, data-gg-shader, data-gg-fx): a ROOT ATTRIBUTE,
 * `data-gg-perf`, stamped by boot.js from applyPerfTier() and re-stamped when
 * the pref changes. exec/ modules read it lazily through perfLite(); the lite
 * CSS keys off `html[data-gg-perf="lite"]`. exec/ never imports ui/ and ui/
 * never imports exec/, and this module keeps both promises: the DETECTION
 * lives here (exec/, importable by the renderers and by boot), the PREF lives
 * in ui/prefs.js (`perfMode`), and boot.js — which imports both tiers by
 * charter — is the one place they meet.
 *
 * ABSENT MEANS FULL. The attribute only exists once boot has stamped it, and a
 * page that never did (the node self-tests, the import sweep, a bare harness)
 * must behave exactly as it did before this module existed. Only the exact
 * 'lite' value switches anything down — same polarity contract as
 * data-gg-shader, and for the same reason.
 *
 * THE DETECTION IS DELIBERATELY COARSE, and desktop-safe by construction: a
 * FINE-pointer machine can never resolve lite, whatever its memory claims,
 * because the requirement is "desktop byte-identical" and a touchscreen
 * laptop's deviceMemory is not a reason to change a desktop's picture. On a
 * coarse-pointer device (a phone, a small tablet) either signal — a small
 * viewport or a low navigator.deviceMemory (Chrome-only; Safari never exposes
 * it, which is why it is a second vote and not the first) — lands on lite.
 * The player can overrule either answer from the options drawer.
 *
 * Import-safe under node: nothing here touches the DOM at import, and every
 * runtime read is guarded.
 * ==========================================================================*/

/** The root attribute, on <html>. Written by applyPerfTier (boot.js calls it). */
export const PERF_ATTR = 'data-gg-perf';
export const PERF_FULL = 'full';
export const PERF_LITE = 'lite';

/**
 * A coarse-pointer viewport at or under this (its SMALLER dimension, so
 * rotating a phone cannot flip the tier) is a phone or a small tablet. 820
 * clears every iPhone (428 for the 13 Pro Max) and the 768 iPads while leaving
 * the 1024 iPad Pros — which render the full stack fine — on full.
 */
export const LITE_VIEWPORT_MAX_PX = 820;

/** navigator.deviceMemory at or under this GB is the second vote for lite.
 *  Chrome-only and clamped to 8 by spec; Safari simply never casts it. */
export const LITE_DEVICE_MEMORY_GB = 4;

/**
 * The tier, from a plain env bag — PURE, so the self-test can sweep the whole
 * decision table without a window. Missing fields vote for full: an unreadable
 * signal must never be the thing that degrades somebody's picture.
 *
 * @param {{coarse?:boolean, viewportMinPx?:number, deviceMemoryGb?:number}} [env]
 * @returns {'full'|'lite'}
 */
export function detectPerfTier(env) {
  const e = env && typeof env === 'object' ? env : {};
  if (e.coarse !== true) return PERF_FULL;   // fine pointer = desktop = full, always
  const minPx = (typeof e.viewportMinPx === 'number' && e.viewportMinPx > 0) ? e.viewportMinPx : Infinity;
  if (minPx <= LITE_VIEWPORT_MAX_PX) return PERF_LITE;
  const mem = (typeof e.deviceMemoryGb === 'number' && e.deviceMemoryGb > 0) ? e.deviceMemoryGb : Infinity;
  if (mem <= LITE_DEVICE_MEMORY_GB) return PERF_LITE;
  return PERF_FULL;
}

/** What this host actually is, read fresh. Every probe is guarded — a stub
 *  without matchMedia or a window simply reads as a fine-pointer desktop. */
export function readPerfEnv() {
  const env = { coarse: false, viewportMinPx: 0, deviceMemoryGb: 0 };
  try { env.coarse = typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches === true; }
  catch (_e) { env.coarse = false; }
  try {
    if (typeof window !== 'undefined' && window && window.innerWidth > 0 && window.innerHeight > 0) {
      env.viewportMinPx = Math.min(window.innerWidth, window.innerHeight);
    }
  } catch (_e) { /* stays 0 = unknown */ }
  try {
    const m = (typeof navigator !== 'undefined' && navigator) ? navigator.deviceMemory : undefined;
    if (typeof m === 'number' && m > 0) env.deviceMemoryGb = m;
  } catch (_e) { /* stays 0 = unknown */ }
  return env;
}

/**
 * The pref -> the tier. 'full' and 'lite' are the player's explicit answers and
 * win outright; EVERYTHING else — 'auto', a corrupt store, an older client's
 * boolean, undefined — falls through to detection, so a junk pref can only ever
 * land on "what this device deserves", never on a frozen wrong answer.
 * Pure and total, same contract as ui/prefs.js resolveArsenalOpen.
 *
 * @param {string} mode the stored `perfMode` pref
 * @param {{coarse?:boolean, viewportMinPx?:number, deviceMemoryGb?:number}} [env]
 *        injectable for tests; app code omits it and gets the real host
 * @returns {'full'|'lite'}
 */
export function resolvePerfTier(mode, env) {
  if (mode === PERF_FULL) return PERF_FULL;
  if (mode === PERF_LITE) return PERF_LITE;
  return detectPerfTier(env || readPerfEnv());
}

/**
 * Resolve and STAMP. boot.js calls this once at startup (beside the
 * data-gg-motion write) and again whenever the `perfMode` pref changes, so a
 * toggle flipped mid-match reaches renderers that were built at startup — the
 * caps, the spiral throttle and the lite CSS all read the attribute lazily.
 * @param {string} mode the stored `perfMode` pref
 * @returns {'full'|'lite'} what was stamped (or would have been, headless)
 */
export function applyPerfTier(mode) {
  const tier = resolvePerfTier(mode);
  try {
    if (typeof document !== 'undefined' && document && document.documentElement) {
      document.documentElement.setAttribute(PERF_ATTR, tier);
    }
  } catch (_e) { /* a host without a DOM has nothing to degrade */ }
  return tier;
}

/**
 * Is the LITE tier in force RIGHT NOW? The renderers' one entry point — read
 * lazily at every spawn/refresh (never cached at build, or the options toggle
 * would be a dead switch), and absent means full: a page nobody stamped is the
 * page exactly as it was.
 */
export function perfLite() {
  try {
    if (typeof document === 'undefined' || !document || !document.documentElement) return false;
    return document.documentElement.getAttribute(PERF_ATTR) === PERF_LITE;
  } catch (_e) { return false; }
}

export default perfLite;
