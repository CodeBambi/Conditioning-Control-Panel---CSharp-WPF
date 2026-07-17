/* ============================================================================
 * loomSpirals.js - the player's saved Loom spirals PLUS the bundled spiral
 * pool, and the one picker every 'spiral' effect shares.
 *
 * boot.js fills the Loom list from the host's loom-list message (urls point at
 * the ccp.spirals virtual host). pickSpiralUrl() mixes those ~50/50 with the
 * bundled spirals, and BOTH spiral effect paths call it - the classic overlay
 * (bubbles.fireEffect) and the in-run payload fx (payloadFx.showSpiral) - so a
 * woven spiral shows up everywhere from one source of truth. Deliberately dumb:
 * all file authority/validation is C#-side (DtrhLoomStore).
 * ==========================================================================*/

let spirals = [];   // [{ slug, url, params }]

// the shipped spiral overlays (assets/bubbles/effects/spirals/); the Loom's
// saved spirals join this pool at runtime via pickSpiralUrl(). All animate
// natively in an <img>/background (gif/webp), so no webm decoder cold-start.
const BUNDLED_BASE = '/dtrh/assets/bubbles/effects/spirals/';
export const BUNDLED_SPIRALS = ['sp1.gif', 'sp2.webp', 'sp3.gif', 'sp4.webp', 'sp5.gif', 'sp6.gif', 'sp7.gif', 'sp8.gif']
  .map((f) => BUNDLED_BASE + f);

export function setLoomSpirals(list) {
  spirals = Array.isArray(list) ? list.filter((s) => s && s.url) : [];
}

export function getLoomSpirals() { return spirals; }

/** One spiral overlay url. The player's Loom spirals join the bundled pool
 *  ~50/50 (they only appear once at least one has been woven). */
export function pickSpiralUrl() {
  if (spirals.length && Math.random() < 0.5) {
    return spirals[Math.floor(Math.random() * spirals.length)].url;
  }
  return BUNDLED_SPIRALS[Math.floor(Math.random() * BUNDLED_SPIRALS.length)];
}
