/* ============================================================================
 * regions.js - THE FOUR CHAMBERS: DtRH's fixed descent identity.
 *
 * The descent is four regions, ALWAYS in this order (I -> II -> III -> IV). It
 * is a guided journey: it always completes, and each chamber ends in a boon
 * draft (its "Landing"). Every region owns:
 *   - a sky, reusing the Wave-2 weather zone plumbing (weather.js). Region I is
 *     open sky (null) - the fall itself is the identity.
 *   - a numeral + name + subtitle for its arrival banner.
 *   - an intensity band [start, peak]. The run brain's intensity() sweeps this
 *     band across the region so each chamber BREATHES: a calm arrival, a climb
 *     to a local peak (its Landing), then the next chamber resets to a calmer -
 *     but higher-than-before - arrival. The bands rise region to region, so the
 *     Court (IV) is objectively the deepest place the run goes.
 *
 * This maps a hypnotic deepening arc onto an Alice-in-Wonderland motif:
 *   I  Curiosity  -> II Confusion -> III Fixation -> IV Surrender
 * ==========================================================================*/

export const REGIONS = [
  {
    id: 'longfall', numeral: 'I', name: 'The Long Fall', subtitle: 'curiosity',
    weatherId: null,               // open sky - the fall itself is the identity
    band: { start: 0.10, peak: 0.42 },
  },
  {
    id: 'doors', numeral: 'II', name: 'The Hall of Doors', subtitle: 'confusion',
    weatherId: 'static',           // stray current, disorientation
    band: { start: 0.28, peak: 0.64 },
  },
  {
    id: 'garden', numeral: 'III', name: 'The Mad Garden', subtitle: 'fixation',
    weatherId: 'perfume',          // her sweet fog, lust climbs
    band: { start: 0.46, peak: 0.84 },
  },
  {
    id: 'court', numeral: 'IV', name: 'The Court of Hearts', subtitle: 'surrender',
    weatherId: 'overstim',         // too bright, too fast - the deepest place
    band: { start: 0.60, peak: 1.00 },
  },
];

export const REGION_COUNT = REGIONS.length;

/** Resolve a 1-based region index (== the run's waveIndex) to its region.
 * Relapse can push waveIndex past IV; those bonus loops reuse the Court. */
export function regionForWave(waveIndex) {
  const i = Math.min(Math.max(1, (waveIndex | 0)), REGIONS.length);
  return REGIONS[i - 1];
}
