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
  leanLights: false,     // race: 7 lights -> 3 (ambient + the dresser's hemisphere + EMI's cupLight stay)
  bubbleCap: 160,        // race: live bubble sprites (each visible one is a draw call on the crisp layer)
  bubbleShards: 64,      // race: pop shard sprites
  bubbleViewAhead: 110,  // race: metres ahead beyond which a live sprite is hidden (fog has eaten it by ~80 m)
  leanSpirals: false,    // race: draw spiral pops from the two lightest bundled gifs, prefetched before the run
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
    leanLights: true,    // 7 lights -> 3: every lit material's fragment loop shrinks with the light count
    bubbleCap: 100,      // the run holds 46-93 live on desktop's 160; the pool is 100 sprite materials smaller
    bubbleShards: 32,
    bubbleViewAhead: 76, // what the fog has already folded away is not drawn (a quarter fewer sprite draw calls)
    leanSpirals: true,   // sp6 + sp7 (0.8 MB together) instead of a 2.2-5.3 MB gif fetched mid-lap
  });
}
