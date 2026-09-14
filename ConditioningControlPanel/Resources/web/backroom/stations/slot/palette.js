/* palette.js - recolour the cabinet for a room variant (CONTRACT 7, ctx.variant).
 * The room's three slots are one station in three colours: violet and mint pass a palette of
 * material name -> 'rrggbb' (candy_rose, candy_violet, candy_plum, wand_pink), rose passes null.
 * PURE: no three import, so the node tests can drive it with plain objects. */

const HEX = /^[0-9a-f]{6}$/i;

/**
 * Give every mesh whose material name is in `palette` ONE shared clone per name with the new colour.
 * Returns the clones so the caller can dispose them. A null or empty palette changes nothing.
 */
export function applyPalette(root, palette) {
  const made = new Map();
  if (!root || !palette || typeof palette !== 'object') return [];
  root.traverse(n => {
    if (!n.isMesh || !n.material || Array.isArray(n.material)) return;
    const name = n.material.name, hex = palette[name];
    if (typeof hex !== 'string' || !HEX.test(hex)) return;
    if (!made.has(name)) {
      const m = n.material.clone();
      m.color.set('#' + hex);
      made.set(name, m);
    }
    n.material = made.get(name);
  });
  return [...made.values()];
}
