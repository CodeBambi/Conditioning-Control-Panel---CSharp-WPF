import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { checkNodes } from '../../../room/nodes-roulette.js';
test('optimized roulette retains every authored landing anchor', () => {
  const bytes=readFileSync(new URL('../../../room/assets/roulette.glb',import.meta.url));
  const gltf=JSON.parse(bytes.subarray(20,20+bytes.readUInt32LE(12)).toString());
  assert.deepEqual(checkNodes(gltf.nodes.map(n=>n.name)).missingRequired,[]);
});
