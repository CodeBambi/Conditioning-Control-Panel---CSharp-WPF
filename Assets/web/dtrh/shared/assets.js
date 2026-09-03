/* ============================================================================
 * assets.js — texture cache for feature node art.
 * Returns a THREE.Texture immediately (populates async) and caches by URL so
 * the same screenshot reused as node + panel hero is only fetched once.
 * ==========================================================================*/

import * as THREE from 'three';

const loader = new THREE.TextureLoader();
const cache = new Map();

// onReady(texture) fires once the image is available (immediately if cached).
export function loadTexture(url, onReady) {
  const cached = cache.get(url);
  if (cached) {
    if (onReady) {
      if (cached.image && cached.image.width) onReady(cached);
      else (cached.userData._cbs ||= []).push(onReady);
    }
    return cached;
  }
  const tex = loader.load(
    url,
    (t) => { for (const cb of (t.userData._cbs || [])) cb(t); t.userData._cbs = []; },
    undefined,
    () => console.warn('[rabbit-hole] texture failed to load:', url),
  );
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.generateMipmaps = false;
  tex.minFilter = THREE.LinearFilter;
  tex.magFilter = THREE.LinearFilter;
  tex.userData._cbs = onReady ? [onReady] : [];
  cache.set(url, tex);
  return tex;
}

export function disposeTextures() {
  for (const t of cache.values()) t.dispose();
  cache.clear();
}
