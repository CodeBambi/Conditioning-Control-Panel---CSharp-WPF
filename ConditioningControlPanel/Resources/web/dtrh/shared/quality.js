/* ============================================================================
 * quality.js — the one shared quality-knob block for the 3D engine.
 *
 * Defaults are the desktop values (exactly what shipped before tiers existed).
 * scene.start() calls setQuality(tier) FIRST, before any geometry/material is
 * built, so every module that imports Q sees the resolved tier. Phones get a
 * lighter scene: no MSAA/bloom, lower DPR, fewer fog particles / wall panels /
 * tube segments — the tunnel look survives, the fill-rate bill doesn't.
 * ==========================================================================*/

export const Q = {
  tier: 'desktop',
  antialias: true,
  maxDpr: 2,             // devicePixelRatio cap
  bloom: true,           // UnrealBloom composer (full-screen mip pyramid)
  tubeSegMult: 2.5,      // tunnel length-segments per depth unit
  tubeRadial: 28,        // tunnel radial segments
  fogAmbientPerDepth: 28,// ambient fog particles per depth unit
  fogPerCard: 9,         // fog particles hugging each feature card
  fogPerTitle: 60,       // fog halo particles per sector title
  fogOctaves: 2,         // simplex-noise octave pairs in the fog vertex shader
  wallRows: 7,           // gallery "TV wall" grid
  wallCols: 7,
  galleryPool: 14,       // gallery images sampled per wall
  swirlCount: 90,        // orbiting particles per feature card
  veinRadial: 18,        // branch-vein tube radial segments
  deeperRadial: 20,      // Deeper flagship tube radial segments
};

export function setQuality(tier) {
  Q.tier = tier === 'mobile' ? 'mobile' : 'desktop';
  if (Q.tier !== 'mobile') return;
  Object.assign(Q, {
    antialias: false,    // bloom-less additive glow hides edges well enough
    maxDpr: 1.5,
    bloom: false,
    tubeSegMult: 1.4,
    tubeRadial: 20,
    fogAmbientPerDepth: 6,
    fogPerCard: 4,
    fogPerTitle: 24,
    fogOctaves: 1,
    wallRows: 4,
    wallCols: 4,
    galleryPool: 8,
    swirlCount: 36,
    veinRadial: 12,
    deeperRadial: 14,
  });
}
