/* ============================================================================
 * quality.js — one shared quality-knob block for the endless tunnel.
 *
 * Trimmed from the marketing-site explore engine. main.js resolves the tier
 * BEFORE any geometry/material is built, so every module that imports Q sees
 * the resolved values. The host (ChaosTunnelService) can request a tier via a
 * `quality` message; otherwise we auto-pick from hardware hints.
 *
 * This background runs ON TOP of the already GPU-heavy Chaos game, so even the
 * "desktop" tier here is deliberately lighter than the marketing page.
 * ==========================================================================*/

export const Q = {
  tier: 'desktop',
  antialias: true,
  maxDpr: 1.75,          // devicePixelRatio cap (page sits behind a busy game)
  bloom: true,           // UnrealBloom composer (lazy-loaded, degrades gracefully)
  tubeRadial: 26,        // tunnel radial segments
  chunkStations: 22,     // control points per tube chunk
  aheadUnits: 380,       // how far ahead (world units) we keep geometry generated
  behindUnits: 70,       // how far behind the camera we keep geometry before recycling
  fogCount: 900,         // rolling ambient fog particles
};

export function setQuality(tier) {
  Q.tier = tier === 'low' ? 'low' : 'desktop';
  if (Q.tier !== 'low') return;
  Object.assign(Q, {
    antialias: false,
    maxDpr: 1.25,
    bloom: false,
    tubeRadial: 18,
    chunkStations: 18,
    aheadUnits: 300,
    behindUnits: 55,
    fogCount: 340,
  });
}

/** Best-effort auto tier from hardware hints when the host doesn't dictate one. */
export function autoTier() {
  try {
    const cores = navigator.hardwareConcurrency || 8;
    const mem = navigator.deviceMemory || 8;
    if (cores <= 4 || mem <= 4) return 'low';
  } catch (_) {}
  return 'desktop';
}
