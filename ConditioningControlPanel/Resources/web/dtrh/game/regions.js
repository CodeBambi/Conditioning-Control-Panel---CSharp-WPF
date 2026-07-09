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
 *   - a visual STYLE (Four Chambers identity pass): the chamber OWNS the tube's
 *     look during a region run. `palette` re-seeds fx.js's six base colors
 *     (engine-facing, NEVER persisted - the hub reverts to the user's theme);
 *     `knobs` drive the tunnel shader's pattern knobs, lerping enter->peak
 *     across the chamber's intensity band; `density` re-spaces the ring/spiral/
 *     arm cadence ONCE at the chamber commit (snapped under the transition
 *     flash - eased count changes read as a visible respace); `bloom`,
 *     `sparkleBoost`/`ribbonBoost` and the `field` accents (fieldFx 2D glow)
 *     finish the dress. All knob values 0 (or absent) = the classic look.
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
    // Warm dusk amber/rose on soft dark: an invitation, not a demand. Sparse
    // wide rings that BREATHE (slow width/brightness swell), three lazy arms.
    style: {
      palette: { colLine: 0xe8a06b, colSpiral: 0xd98a8f, colFog: 0x1c1210,
                 colBg: 0x241611, colRibbon: 0xd9925f, colSparkle: 0xffe4c8 },
      knobs: { enter: { breath: 0.65 },
               peak:  { breath: 0.45, throb: 0.10 } }, // breath settles; a first faint pulse near the Landing
      density: { rings: 110, spiralTurns: 39, arms: 3 },
      bloom: 0.9,
      field: null,
    },
  },
  {
    id: 'doors', numeral: 'II', name: 'The Hall of Doors', subtitle: 'confusion',
    weatherId: 'static',           // stray current, disorientation
    band: { start: 0.28, peak: 0.64 },
    // Everything comes in PAIRS - escorts and bound twins. Which door, which
    // twin, which first? Confusion by way of doubling.
    profile: { density: 0.92, behavioral: 1.00, echo: 0.6, chaperone: 2.2, bound: 1.9, tease: 0.8 },
    // Cold violet/indigo, geometry that lies: rings dashed into doorframe
    // posts, six arms of doorpost verticality, and a spiral whose phase
    // wanders - it stalls and runs BACKWARDS while you fall forwards.
    style: {
      palette: { colLine: 0x8f7bff, colSpiral: 0x5e6cff, colFog: 0x10102b,
                 colBg: 0x141033, colRibbon: 0x7a5cff, colSparkle: 0xcfd0ff },
      knobs: { enter: { ringDash: 0.45, lineWave: 0.08, spiralDrift: 0.50 },
               peak:  { ringDash: 0.70, lineWave: 0.14, spiralDrift: 1.00 } },
      density: { rings: 125, spiralTurns: 45, arms: 6 },
      bloom: 1.0,
      field: { bolt: [150, 120, 255], boltCore: [220, 214, 255],
               residue: [150, 140, 255], sparkleHue: 255 },
    },
  },
  {
    id: 'garden', numeral: 'III', name: 'The Mad Garden', subtitle: 'fixation',
    weatherId: 'perfume',          // her sweet fog, lust climbs
    band: { start: 0.46, peak: 0.84 },
    // Overgrown. The Echo multiplies if you let it, so the field BLOOMS - a
    // dense, clustered tangle you have to keep cutting back. Fixation.
    profile: { density: 1.22, behavioral: 1.10, echo: 2.4, chaperone: 0.8, bound: 0.7, tease: 1.0 },
    // Hot fuchsia perfume, organic: the lines undulate like vines, the glow
    // throbs on a heartbeat, sparkles bloom. Denser winding - overgrowth.
    style: {
      palette: { colLine: 0xff4fd8, colSpiral: 0xff8ac2, colFog: 0x3d0f33,
                 colBg: 0x33102b, colRibbon: 0xff6ad4, colSparkle: 0xffc8ec },
      knobs: { enter: { lineWave: 0.30, throb: 0.35, breath: 0.20 },
               peak:  { lineWave: 0.55, throb: 0.75, breath: 0.20 } },
      density: { rings: 130, spiralTurns: 51, arms: 4 },
      bloom: 1.12, sparkleBoost: 0.25,
      field: { residue: [255, 140, 220], sparkleHue: 315 },
    },
  },
  {
    id: 'court', numeral: 'IV', name: 'The Court of Hearts', subtitle: 'surrender',
    weatherId: 'overstim',         // too bright, too fast - the deepest place
    band: { start: 0.60, peak: 1.00 },
    // Crescendo. The fullest field and the most Teases - deny, deny, deny under
    // the brightest sky. The deepest place the run goes. Surrender.
    profile: { density: 1.35, behavioral: 1.25, echo: 1.3, chaperone: 1.1, bound: 1.2, tease: 2.0 },
    // Neon crimson/electric pink on near-black: crisp twin rings, a hard
    // metronomic tick (~1.4Hz, capped - signage, never a strobe light), max
    // glow and bloom, the tightest ring cadence. The verdict room.
    style: {
      palette: { colLine: 0xff1744, colSpiral: 0xff2fb0, colFog: 0x0d0208,
                 colBg: 0x120309, colRibbon: 0xff2f6b, colSparkle: 0xffd0dc },
      knobs: { enter: { ringDouble: 0.80, strobe: 0.30 },
               peak:  { ringDouble: 1.00, strobe: 0.65, throb: 0.25 } },
      density: { rings: 140, spiralTurns: 45, arms: 4 },
      bloom: 1.3, ribbonBoost: 0.3,
      field: { bolt: [255, 60, 110], boltCore: [255, 220, 230],
               residue: [255, 90, 140], sparkleHue: 345 },
    },
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
