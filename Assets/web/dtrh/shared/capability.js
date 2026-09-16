/* ============================================================================
 * capability.js — decide 3D vs 2D BEFORE any heavy three.js work.
 *
 * Returns { mode: '2d' | '3d', reason: string, canTry3d, tier, hardBlock, reduced }.
 *   - ?mode=2d / ?mode=3d query param forces a mode (testing / opt-in).
 *   - No WebGL                       -> 2d
 *   - prefers-reduced-motion: reduce -> 2d, UNLESS the caller passes
 *     detectMode({ reducedIs3d: true }): a page that owns a complete
 *     reduced-motion path (Racing Thoughts) reads `reduced` and turns its own
 *     motion down inside the 3D scene instead of being refused the engine.
 *   - few cores AND low memory       -> 2d (genuinely weak hardware)
 * Phones/tablets (coarse pointer / small viewport) DO dive — they get
 * tier 'mobile', which scene.start feeds to quality.js for a lighter scene.
 * The 2D path is always a complete, accessible baseline, so degrading is safe.
 * ==========================================================================*/

function hasWebGL() {
  try {
    const c = document.createElement('canvas');
    return !!(
      window.WebGLRenderingContext &&
      (c.getContext('webgl2') || c.getContext('webgl') || c.getContext('experimental-webgl'))
    );
  } catch (_) {
    return false;
  }
}

function supportsImportMap() {
  return HTMLScriptElement.supports && HTMLScriptElement.supports('importmap');
}

export function detectMode(opts) {
  // reducedIs3d: the caller owns a complete reduced-motion path of its own, so
  // "reduce" is a setting to honour inside the 3D scene rather than a reason to
  // refuse it. Off by default, so every other page keeps the old rule.
  const reducedIs3d = !!(opts && opts.reducedIs3d);
  const params = new URLSearchParams(location.search);
  const forced = params.get('mode');
  const webgl = hasWebGL();
  const importmap = supportsImportMap();
  const reduced = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  // 3D can be offered as an opt-in whenever it's technically possible and the
  // user hasn't asked for reduced motion (or the caller handles it itself).
  const canTry3d = webgl && importmap && (reducedIs3d || !reduced);

  const coarse = window.matchMedia && window.matchMedia('(pointer: coarse)').matches;
  const smallViewport = Math.min(window.innerWidth, window.innerHeight) < 720;
  const fewCores = (navigator.hardwareConcurrency || 8) <= 4;
  const lowMemory = (navigator.deviceMemory || 8) <= 4;
  const tier = (coarse || smallViewport) ? 'mobile' : 'desktop';

  // A genuine hard wall: the browser can't load or run the 3D engine at all.
  // reduced-motion and low-end hardware only downgrade to '2d' for comfort/perf
  // reasons - a consumer with no 2D fallback (the Fall) can still offer 3D as an
  // opt-in in those cases, but never when hardBlock is true.
  const hardBlock = !webgl || !importmap;

  if (forced === '2d') return { mode: '2d', reason: 'forced via ?mode=2d', canTry3d, tier, hardBlock, reduced };
  if (forced === '3d') return { mode: '3d', reason: 'forced via ?mode=3d', canTry3d, tier, hardBlock, reduced };

  if (!webgl) return { mode: '2d', reason: 'no WebGL', canTry3d: false, tier, hardBlock, reduced };
  if (!importmap) return { mode: '2d', reason: 'no importmap support', canTry3d: false, tier, hardBlock, reduced };
  if (reduced && !reducedIs3d) return { mode: '2d', reason: 'prefers-reduced-motion', canTry3d: false, tier, hardBlock, reduced };

  // Both signals together = genuinely weak hardware; either alone (e.g. a
  // 4-core flagship phone) still handles the mobile-tier scene fine.
  if (fewCores && lowMemory) {
    return { mode: '2d', reason: 'low-end hardware', canTry3d, tier, hardBlock, reduced };
  }

  const capable = tier === 'mobile' ? 'capable (mobile tier)' : 'capable';
  return { mode: '3d', reason: reduced ? capable + ', reduced motion' : capable, canTry3d, tier, hardBlock, reduced };
}
