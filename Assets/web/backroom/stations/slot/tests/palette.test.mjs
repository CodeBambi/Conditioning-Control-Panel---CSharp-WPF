import test from 'node:test';
import assert from 'node:assert/strict';
import { applyPalette } from '../palette.js';

function material(name) {
  return { name, hex: null, clones: 0, color: { set(v) { this.value = v; } },
    clone() { this.clones++; const c = material(name); c.from = this; return c; } };
}
function rig(names) {
  const shared = new Map();
  const meshes = names.map(n => {
    if (!shared.has(n)) shared.set(n, material(n));
    return { isMesh: true, material: shared.get(n) };
  });
  return { meshes, shared, traverse(fn) { meshes.forEach(fn); } };
}

const VIOLET = { candy_rose: 'ac83ed', candy_violet: '7255ad', candy_plum: '372557', wand_pink: 'cab0ff' };

test('applyPalette: violet recolours the four candy materials, one clone per name', () => {
  const r = rig(['candy_rose', 'candy_rose', 'candy_violet', 'candy_plum', 'wand_pink', 'brushed_brass']);
  const made = applyPalette(r, VIOLET);
  assert.equal(made.length, 4);
  assert.equal(r.meshes[0].material, r.meshes[1].material, 'two rose meshes share the one clone');
  assert.equal(r.meshes[0].material.color.value, '#ac83ed');
  assert.equal(r.meshes[3].material.color.value, '#372557');
  assert.equal(r.meshes[5].material, r.shared.get('brushed_brass'), 'brass is untouched');
  assert.equal(r.shared.get('candy_rose').clones, 1);
  assert.equal(r.shared.get('candy_rose').color.value, undefined, 'the glb material itself is never edited');
});

test('applyPalette: rose (null), junk hex and arrays change nothing', () => {
  const r = rig(['candy_rose', 'candy_violet']);
  assert.deepEqual(applyPalette(r, null), []);
  assert.deepEqual(applyPalette(r, { candy_rose: 'not-a-colour', candy_violet: 12 }), []);
  assert.equal(r.meshes[0].material, r.shared.get('candy_rose'));
  const multi = { traverse(fn) { fn({ isMesh: true, material: [material('candy_rose')] }); } };
  assert.deepEqual(applyPalette(multi, VIOLET), []);
});
