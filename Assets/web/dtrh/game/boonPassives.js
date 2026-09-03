/* ============================================================================
 * boonPassives.js - grab-in-the-tube rework (2026-07).
 *
 * When a floating accessory/charm card is grabbed mid-fall, its numeric effect
 * must apply to the LIVE run-state right then - there is no pre-run C# loadout
 * snapshot anymore. So each passive's effect is ported here as a one-shot
 * apply(st, level, ctx), mirroring game/boons.js `{apply}` for drafted mantras.
 *
 * SOURCE OF TRUTH: Services/Chaos/ChaosLifetimeBoons.cs `Apply` lambdas. The
 * numbers come from catalog.js `levelValues` (identical to the C# LevelValues),
 * so a level's `v` here == `ValueAt(level)` there. KEEP THESE IN LOCKSTEP - any
 * balance change to a levelValues array or a derived formula (blindfold opacity,
 * golden baseline, GoldenPayRange, DripFeedCap, ripple radius/life) must land in
 * BOTH files.
 *
 * ctx = { syncPhys, wireStickyFingers } - the caller runs syncPhys() after every
 * grab (so physics passives take effect), and applyGrabbedPassive stamps
 * st.maxedBoons for any item grabbed at its max level (arms capstones).
 * ==========================================================================*/

import { boonDefById } from './catalog.js';

// ---- ports of the C# derived-number helpers (ChaosLifetimeBoons.cs) ----

// GoldenPayRange(level): the gold a lucky golden bubble pays at a Rabbit's Foot level.
export function goldenPayRange(level) {
  if (level <= 0) return [10, 20];
  if (level === 1) return [12, 24];
  if (level === 2) return [14, 28];
  if (level === 3) return [16, 32];
  return [20, 40];   // level 4: the gold doubles
}

// DripFeedCap(dropPerPop): the per-descent trickle ceiling (60/90/120/150 by level).
export function dripFeedCap(dropPerPop) {
  return 30 + 30 * Math.max(1, Math.min(4, dropPerPop));
}

// ChaosTuning ripple constants (mirror Services/Chaos/ChaosTuning.cs).
const RIPPLE_RADIUS_PX = 260, RIPPLE_RADIUS_PER_LVL_PX = 45;
const RIPPLE_LIFE_MS = 520, RIPPLE_LIFE_PER_LVL_MS = 110;
const RIPPLE_FOCUS_COST = 30;   // base per-cast focus cost (mirror chaosRun.js)

const r = Math.round;

/** Blindfold dim strength for a pay-mult level value. Exported so biome
 * mechanics that borrow the dim plumbing (Keyhole/Searchlight) can re-assert
 * the relic's own dim on chamber exit instead of a stale pre-grab snapshot. */
export const blindfoldOpacityFor = (v) => (v >= 2.0 ? 0.25 : v >= 1.75 ? 0.32 : 0.40);

/* Each entry mutates st in place. `level` is 1-indexed; `v` is the level value.
 * Physics passives (cursorPull/spanker/chainReach/blindfold) are read by
 * syncPhys(), which the caller runs right after - they just set st fields here. */
export const PASSIVE_APPLY = {
  // ---- Accessories ----
  sticky_fingers: (st, level, _v, ctx) => {
    st.stickyFingersLevel = level;           // held-card paddle pay tier
    if (ctx && ctx.wireStickyFingers) ctx.wireStickyFingers();   // capstone throw-on-release
  },
  surrender: (st, _level, v) => { st.sinExtraMult = v; },
  chain_reaction: (st, _level, v) => { st.chainReactionReach = v; },   // Poppers (syncPhys)
  blindfold: (st, _level, v) => {
    st.blindfoldPayMult = v;
    st.blindfoldActive = true;
    st.blindfoldOpacity = blindfoldOpacityFor(v);
  },
  last_breath: (st, _level, v) => {
    st.lastBreathPayMult = v;
    st.lastBreathWindowSec = v >= 20 ? 0.8 : v >= 10 ? 0.6 : 0.4;
  },
  taking_chances: (st, _level, v) => {
    st.rerollsLeft += r(v);
    st.chanceDoubleOdds = 0.50 + 0.05 * (Math.max(1, Math.min(3, v)) - 1);
  },
  the_pull: (st, _level, v) => { st.cursorPullStrength = v; },          // syncPhys
  the_spanker: (st, _level, v) => { st.spankerActive = true; st.spankGrowFactor = v; },  // syncPhys
  intrusive_thoughts: (st, _level, v) => { st.intrusiveThoughtsSec = v; },

  // ---- Charms (utility) ----
  rabbits_foot: (st, level, v) => {
    st.goldenChance = v;
    st.goldenChanceBase = v;   // weather re-derives goldenChance from this
    st.goldenPay = goldenPayRange(level);
  },
  drip_feed: (st, _level, v) => {
    st.dropPerPop = r(v);
    st.dripFeedCap = dripFeedCap(st.dropPerPop);
  },
  blank_eyes: (st) => { st.showPopScores = true; },
  breast_enlargement: (st, _level, v) => { st.bubbleScale = 1.0 + v / 100.0; },
  slow_recovery: (st, _level, v) => { st.shieldRegenPops = r(v); },
  start_resistance: (st, _level, v) => {
    const n = r(v);
    st.shields += n;
    st.startShields += n;      // regen cap grows with what you descended-into wearing
    st.startingShields += n;   // HUD hollow-heart row
  },
  collar: (st, _level, v) => { st.collarSaves += r(v); },
  golden_touch: (st, _level, v) => {
    st.baseMult = v;   // totalMult() reads st.baseMult live
    st.benignBaseline = v >= 1.45 ? 0.60 : v >= 1.3 ? 0.55 : v >= 1.2 ? 0.50 : 0.45;
  },
  slowburner: (st, _level, v) => {
    // MULTIPLICATIVE - the takenPassiveIds guard in applyGrabbedPassive prevents a
    // double-grab from stacking it (each id grabs at most once per run anyway).
    const f = 1.0 + v / 100.0;
    st.fuseTimeMult *= f;
    st.fuseTimeMultBase *= f;   // weather re-derives fuseTimeMult from base
  },
  mood_ring: (st, _level, v) => { st.moodRingLevel = r(v); },
  pocket_watch: (st) => { st.showWaveTimer = true; },
  skipping_stone: (st, level, v) => {
    st.rippleRechargeSec = v;
    st.rippleFocusCost = Math.max(15, Math.min(RIPPLE_FOCUS_COST, r(v * 2)));
    st.rippleRadiusPx = RIPPLE_RADIUS_PX + level * RIPPLE_RADIUS_PER_LVL_PX;
    st.rippleLifeMs = RIPPLE_LIFE_MS + level * RIPPLE_LIFE_PER_LVL_MS;
  },
};

/** True if this id is a grabbable passive (has an apply here). */
export const isGrabbablePassive = (id) => Object.prototype.hasOwnProperty.call(PASSIVE_APPLY, id);
