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
 *   - a spawn PROFILE (Phase 4): the chamber's play-feel. `density` scales the
 *     field's concurrent-bubble cap (and, gently, its cadence); the behavioral
 *     multipliers bias which "menagerie" mechanics show up so each chamber plays
 *     like somewhere, not just a re-tinted sky:
 *       echo      - the bubble that MULTIPLIES if you don't hold it (overgrowth)
 *       chaperone - a live bubble with a little escort you must clear first (pairs)
 *       bound     - two bubbles, both defused quickly or they enrage (mirrors)
 *       tease     - the DON'T-touch bubble (denial)
 *     These multiply the base spawn-chance ON TOP of the rank gates in
 *     trySpawnBehavioral - they bias the mix within what rank already allows,
 *     they never un-gate a mechanic early. A run that isn't in region mode uses
 *     PROFILE_NEUTRAL (all 1.0), so nothing changes for legacy/scripted runs.
 *
 * This maps a hypnotic deepening arc onto an Alice-in-Wonderland motif:
 *   I  Curiosity  -> II Confusion -> III Fixation -> IV Surrender
 * ==========================================================================*/

export const REGIONS = [
  {
    id: 'longfall', numeral: 'I', name: 'The Long Fall', subtitle: 'curiosity',
    weatherId: null,               // open sky - the fall itself is the identity
    band: { start: 0.10, peak: 0.42 },
    // Sparse and open - mostly plain treats drifting past. Few tricks; the fall
    // itself is the beat. Curiosity, not pressure.
    profile: { density: 0.72, behavioral: 0.40, echo: 0.5, chaperone: 0.6, bound: 0.5, tease: 0.5 },
  },
  {
    id: 'doors', numeral: 'II', name: 'The Hall of Doors', subtitle: 'confusion',
    weatherId: 'static',           // stray current, disorientation
    band: { start: 0.28, peak: 0.64 },
    // Everything comes in PAIRS - escorts and bound twins. Which door, which
    // twin, which first? Confusion by way of doubling.
    profile: { density: 0.92, behavioral: 1.00, echo: 0.6, chaperone: 2.2, bound: 1.9, tease: 0.8 },
  },
  {
    id: 'garden', numeral: 'III', name: 'The Mad Garden', subtitle: 'fixation',
    weatherId: 'perfume',          // her sweet fog, lust climbs
    band: { start: 0.46, peak: 0.84 },
    // Overgrown. The Echo multiplies if you let it, so the field BLOOMS - a
    // dense, clustered tangle you have to keep cutting back. Fixation.
    profile: { density: 1.22, behavioral: 1.10, echo: 2.4, chaperone: 0.8, bound: 0.7, tease: 1.0 },
  },
  {
    id: 'court', numeral: 'IV', name: 'The Court of Hearts', subtitle: 'surrender',
    weatherId: 'overstim',         // too bright, too fast - the deepest place
    band: { start: 0.60, peak: 1.00 },
    // Crescendo. The fullest field and the most Teases - deny, deny, deny under
    // the brightest sky. The deepest place the run goes. Surrender.
    profile: { density: 1.35, behavioral: 1.25, echo: 1.3, chaperone: 1.1, bound: 1.2, tease: 2.0 },
  },
];

export const REGION_COUNT = REGIONS.length;

/** Neutral profile: no bias. Non-region runs (legacy/scripted) resolve to this
 * so their spawn feel is byte-for-byte what it was before Four Chambers. */
export const PROFILE_NEUTRAL = Object.freeze({
  density: 1.0, behavioral: 1.0, echo: 1.0, chaperone: 1.0, bound: 1.0, tease: 1.0,
});

/** The spawn profile for a given 1-based region index. Falls back to neutral if
 * a region ever lacks one (defensive - every REGION above defines profile). */
export function profileForWave(waveIndex) {
  return regionForWave(waveIndex).profile || PROFILE_NEUTRAL;
}

/** Resolve a 1-based region index (== the run's waveIndex) to its region.
 * Relapse can push waveIndex past IV; those bonus loops reuse the Court. */
export function regionForWave(waveIndex) {
  const i = Math.min(Math.max(1, (waveIndex | 0)), REGIONS.length);
  return REGIONS[i - 1];
}
