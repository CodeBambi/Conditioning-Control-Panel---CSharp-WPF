/* ============================================================================
 * stations/breakout/twists/index.js - the twist registry, id -> module.
 * One module per lane; every hook is optional. The shape is frozen in
 * twists/CONTRACT.md, which wins over any other description of it.
 * ==========================================================================*/
import CRUMBLE from './crumble.js';
import MIRROR from './mirror.js';
import KEYS from './keys.js';
import NODE from './node.js';
import JUSTONE from './justone.js';

import CRUMBLE_RENDER from './crumble-render.js';
import MIRROR_RENDER from './mirror-render.js';
import KEYS_RENDER from './keys-render.js';
import NODE_RENDER from './node-render.js';
import JUSTONE_RENDER from './justone-render.js';

export const TWISTS = {
  crumble: CRUMBLE, mirror: MIRROR, keys: KEYS, node: NODE, justone: JUSTONE,
};
export const TWIST_RENDER = {
  crumble: CRUMBLE_RENDER, mirror: MIRROR_RENDER, keys: KEYS_RENDER, node: NODE_RENDER, justone: JUSTONE_RENDER,
};

export const twistFor = id => (id && TWISTS[id]) || null;
export const twistRenderFor = id => (id && TWIST_RENDER[id]) || null;

export default TWISTS;
