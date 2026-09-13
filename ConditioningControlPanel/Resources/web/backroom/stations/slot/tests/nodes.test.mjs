import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { readGlbNodes } from './nodes-check.mjs';
import { checkNodes, REQUIRED } from '../nodes.js';

test('the shipped slot.glb snapshot carries every required node', () => {
  const { nodes, materials } = readGlbNodes(fileURLToPath(new URL('../assets/slot.glb', import.meta.url)));
  assert.deepEqual(checkNodes(nodes).missingRequired, []);
  assert.ok(materials.includes('emi_face'));
});

test('a missing node is sorted into required or optional', () => {
  const r = checkNodes(REQUIRED.filter(n => n !== 'lever'));
  assert.deepEqual(r.missingRequired, ['lever']);
  assert.ok(r.missingOptional.includes('payout_spawn'));
});
