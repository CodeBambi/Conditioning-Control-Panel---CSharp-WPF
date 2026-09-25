/* ============================================================================
 * ui/duel/noisePick.js - the words and colours of the Sort duel's VS reveal and
 * noise pick. PURE: no DOM, safe under node.
 *
 * A side's NICHE is what its hello named in `caps.niches` (the Scrolller niches
 * picked for the match; the owner allowed the names across on 2026-09-24). When
 * those niches belong to one of the five flavours the reveal says the flavour,
 * in its own tint; otherwise the first niche, "r/" and all, plus a count.
 * ==========================================================================*/

import { FLAVOURS } from '../flavours.js';
import { NOISE_SETS, noiseSet } from '../../core/noiseSets.js';
import { DUEL_COPY } from './copy.js';

const lower = (s) => String(s).toLowerCase();
const DEFAULT_TINT = '#ff69b4';

/**
 * The flavour these niches came from, or null. The one sharing the most names wins; a single
 * shared name is enough (a player may switch most of a flavour off and add their own).
 */
export function flavourOfNiches(niches) {
  const mine = new Set((Array.isArray(niches) ? niches : []).map(lower));
  if (!mine.size) return null;
  let best = null;
  let bestN = 0;
  for (const f of FLAVOURS) {
    const n = [...f.subs, ...(f.extras || [])].filter((s) => mine.has(lower(s))).length;
    if (n > bestN) { best = f; bestN = n; }
  }
  return best;
}

/**
 * One side of the VS card.
 * @param {string[]} niches  cleaned niche names
 * @param {'player'|'bot'} [kind]
 * @returns {{name:string, tint:string, subs:string[], more:string}}
 */
export function nicheLabel(niches, kind = 'player') {
  const subs = (Array.isArray(niches) ? niches : []).filter((s) => typeof s === 'string' && s).slice(0, 8);
  if (kind === 'bot') return { name: DUEL_COPY.botNiche, tint: '#b99cff', subs: [], more: '' };
  if (!subs.length) return { name: DUEL_COPY.houseMix, tint: '#c9b8e6', subs: [], more: '' };
  const f = flavourOfNiches(subs);
  if (f) return { name: f.name, tint: f.tint || DEFAULT_TINT, subs, more: '' };
  return { name: 'r/' + subs[0], tint: DEFAULT_TINT, subs, more: subs.length > 1 ? DUEL_COPY.nicheMore(subs.length - 1) : '' };
}

/** A board's name on screen. */
export function noiseName(id) {
  const s = noiseSet(id);
  if (!s) return '';
  const key = 'noise' + s.id.charAt(0).toUpperCase() + s.id.slice(1);
  const v = DUEL_COPY[key];
  return typeof v === 'string' && v ? v : s.id;
}

/** Every tile the pick shows, in table order: {id, name, glyph, tint}. */
export function noiseTiles() {
  return NOISE_SETS.map((s) => ({ id: s.id, name: noiseName(s.id), glyph: s.glyph, tint: s.tint }));
}

export default nicheLabel;
