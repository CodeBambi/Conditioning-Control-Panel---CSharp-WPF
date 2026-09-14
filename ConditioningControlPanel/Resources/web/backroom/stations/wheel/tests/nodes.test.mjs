import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { readGlbNodes } from './nodes-check.mjs';
import { checkNodes, REQUIRED, FACE_MATERIAL } from '../nodes.js';

const GLB = fileURLToPath(new URL('../assets/wheel.glb', import.meta.url));

test('the shipped wheel.glb carries every node the page drives', () => {
  const { nodes, materials } = readGlbNodes(GLB);
  const { missingRequired, missingOptional } = checkNodes(nodes);
  assert.deepEqual(missingRequired, []);
  assert.deepEqual(missingOptional, []);
  assert.ok(materials.includes(FACE_MATERIAL));
  assert.equal(nodes.filter(n => /^bulb_\d+$/.test(n)).length, 32);
});

test('a glb without the rotor fails the check', () => {
  const { missingRequired } = checkNodes(['wheel_station', 'pointer']);
  assert.deepEqual(missingRequired, ['wheel_rotor']);
  assert.ok(REQUIRED.includes('wheel_rotor'));
});
