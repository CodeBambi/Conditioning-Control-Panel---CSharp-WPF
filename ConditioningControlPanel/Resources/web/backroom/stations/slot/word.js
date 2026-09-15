/* ============================================================================
 * word.js - the slot's lone word and dead-spin settle (shared/hypno/callout.js).
 *
 * The station calls outcome(o, fireFx) on the frame the result shows (Law I),
 * after the server reply, in place of firing every fx id itself:
 *   fx.sub_single            the PAGE shows the dealt word (callout.word) and the host is NOT sent it
 *   fx.sub_pair / sub_cascade the page shows the two / three dealt words back to back, and the host is
 *                            still sent the fx with args { wordsShown: true } (it skips its own words and
 *                            plays the pair's spiral / the cascade's fullscreen GIF)
 *   line none, no fx at all  callout.settle(): the sweep, the settle cue, EMI's bark
 * Words come from the deal (media.word(n)), else the symbol's fallback label (symbols.js WORDS). The
 * subliminal gate off (ctx.gates.subliminal === false) leaves every fx to the host as before, so the host's
 * own toggle stays the only judge. The reversal easter egg is seeded from the outcome (index + stops), so a
 * replayed outcome shows the same thing; it never touches the payout.
 * ==========================================================================*/

import { createCallout } from '../../shared/hypno/callout.js';
import { fxSymbols } from './media.js';
import { kindOf, WORDS } from './symbols.js';

const SUB_FX = new Set(['fx.sub_single', 'fx.sub_pair', 'fx.sub_cascade']);

/** The dealt texts for an outcome's subliminals, in reel order. */
export function subWords(o, media) {
  const row = Array.isArray(o.symbols) ? o.symbols : [];
  const subs = Array.isArray(o.subs) && o.subs.length ? o.subs : row.filter(s => kindOf(s).kind === 'sub');
  return subs.map(s => {
    const { kind, n } = kindOf(s);
    if (kind !== 'sub') return null;
    const dealt = media && typeof media.word === 'function' ? media.word(n) : null;
    return dealt || WORDS[n % WORDS.length];
  }).filter(Boolean).slice(0, 3);
}

/** A stable seed for one outcome: the tape index and the stops. */
export function outcomeSeed(o) {
  let h = 2166136261 ^ ((Number(o && o.i) || 0) + 1);
  for (const s of Array.isArray(o && o.stops) ? o.stops : []) { h ^= (Number(s) || 0) + 1; h = Math.imul(h, 16777619); }
  return h >>> 0;
}

export function createSlotWords({ ctx, mount, media, emi, lex }) {
  const callout = createCallout({ mount, lex: lex || ctx.lex, ctx, emi });
  return {
    callout,
    /** Fires the outcome's fx through fireFx(fxId, keys, args), showing the words here first. */
    outcome(o, fireFx) {
      const fx = Array.isArray(o.fx) ? o.fx : [];
      const dead = fx.length === 0 && (o.line === 'none' || !o.line) && !(o.pay > 0);
      if (dead) { callout.settle(); return { words: [], settled: true }; }
      const gateOn = !(ctx && ctx.gates && ctx.gates.subliminal === false);
      const texts = gateOn && fx.some(f => SUB_FX.has(f)) ? subWords(o, media) : [];
      if (texts.length) callout.word(texts[0], { chain: texts.slice(1), seed: outcomeSeed(o) });
      for (const f of fx) {
        if (f === 'fx.sub_single' && texts.length) continue;
        const args = texts.length && (f === 'fx.sub_pair' || f === 'fx.sub_cascade') ? { wordsShown: true } : undefined;
        fireFx(f, fxSymbols(f, o, media), args);
      }
      return { words: texts, settled: false };
    },
    cancel() { callout.cancel(); },
    dispose() { callout.dispose(); },
    debug() { return callout.debug(); },
  };
}
