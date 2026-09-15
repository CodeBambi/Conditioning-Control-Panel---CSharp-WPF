/* ============================================================================
 * shared/hypno/callout.js - the win callout and the glyph highlight timings,
 * shared by every Back Room game (owner direction, 2026-09-15).
 *
 * CONTRACT (stable; the look and the motion land in the flow lane):
 *   createCallout({ mount, lex })      one per station; mount is the station's root element
 *   .show(key, fallback, { tier })     a diegetic name, e.g. ('br_callout_double_spin', 'Double Spin', { tier: 'small' })
 *                                      tier: 'small' | 'big' | 'hero'. Returns { done: Promise } resolving when the
 *                                      text has dissolved (CALLOUT_MS). A show while one is up replaces it.
 *   .cancel()                          Law VI: suspend or leave drops the text at once
 *   .dispose()
 *   .debug()                           { shown: [{ key, tier, at }] } for the smokes
 *
 * The flow every game follows on the frame the result SHOWS (Law I):
 *   0 ms                 the landing thud
 *   0..HIGHLIGHT_MS      the winning glyphs glow, HIGHLIGHT_GAP_MS apart in landing order (DOM: class GLYPH_HIT;
 *                        three.js: the station's own rim light, same timings)
 *   FX_DELAY_MS          the callout and the host effect start together
 *   FX_DELAY_MS + CALLOUT_MS   the next press unlocks (host overlays never block it; the jackpot hero holds itself)
 * ==========================================================================*/
export const HIGHLIGHT_MS = 400;
export const HIGHLIGHT_GAP_MS = 80;
export const FX_DELAY_MS = 400;
export const CALLOUT_MS = 1600;
export const GLYPH_HIT = 'br-glyph-hit';
export const TIERS = Object.freeze(['small', 'big', 'hero']);

/** Placeholder until the flow lane lands the motion: the API is final, the text is not drawn yet. */
export function createCallout({ mount = null, lex = (_, f) => f } = {}) {
  const shown = [];
  let disposed = false;
  return {
    show(key, fallback = '', { tier = 'small' } = {}) {
      if (disposed) return { done: Promise.resolve() };
      const text = lex(key, fallback) || fallback;
      shown.push({ key, text, tier: TIERS.includes(tier) ? tier : 'small', at: typeof performance !== 'undefined' ? performance.now() : Date.now() });
      return { done: new Promise((r) => setTimeout(r, CALLOUT_MS)) };
    },
    cancel() {},
    dispose() { disposed = true; },
    debug() { return { shown: shown.slice(), mount: !!mount }; },
  };
}
