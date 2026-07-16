/* ============================================================================
 * craftedEffects.js - Part 2's effect tables for THE BOUDOIR's crafted items.
 *
 * DELIBERATELY SEPARATE from boonPassives.js: crafted items must never enter
 * PASSIVE_APPLY or LIFETIME_BOONS - the grab/draft pools are built from
 * LIFETIME_BOONS (engine/powerupDrops.js) and anything added there would leak
 * into tube grabs and door drafts. Crafted permanents apply once at run start
 * (applyCraftedKit in chaosRun.js), keyed by ownership in cfg.craftedItems.
 * ==========================================================================*/

/** Run-start appliers for crafted permanents: id -> (st, cfg, count). Called
 *  once per owned id from applyCraftedKit (never on the scripted first run). */
export const CRAFTED_PERMANENT_APPLY = {
  // a chrome key: doors stop being suggestions
  skeleton_key: (st) => { st.rerollsLeft += 1; },
  // the collar, but it locks now - one extra save on the fatal pop
  locked_collar: (st) => { st.collarSaves += 1; },
  // lace crossed tight: the start_resistance triple-write (boonPassives.js)
  the_corset: (st) => {
    st.shields += 1;
    st.startShields += 1;      // regen cap
    st.startingShields += 1;   // HUD hollow-heart row
  },
  // a pill in a chrome case: the descent ticks a touch slower (10% longer)
  the_timepiece: (st, cfg) => {
    st.runDurationSec = Math.round(cfg.durationSec * 1.10);
  },
};

/** Dock cards for the three crafted consumables - they have no boon-catalog
 *  def, so the HUD toy dock needs its own glyph/name/desc rows. */
export const CRAFTED_TOY_DEFS = {
  slippery_slope: {
    glyph: '💨', name: 'Slippery Slope',
    desc: '10s: the fall runs ×1.5 faster and heat builds twice as fast.',
  },
  rubber_up: {
    glyph: '🛡', name: 'Rubber Up',
    desc: '8s: nothing can touch you.',
  },
  sugar_cube: {
    glyph: '🍬', name: 'Sugar Cube',
    desc: 'the White Rabbit comes immediately.',
  },
};
