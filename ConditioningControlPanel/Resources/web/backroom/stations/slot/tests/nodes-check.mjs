// Re-run on every new slot.glb drop:
//   node ConditioningControlPanel/Resources/web/backroom/stations/slot/tests/nodes-check.mjs [path/to/slot.glb]
// Exit 1 when a REQUIRED node is missing; optional ones only warn (the page degrades the same way).
// Also runs under `node --test` (nodes.test.mjs imports readGlbNodes).
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { checkNodes, FACE_MATERIAL } from '../nodes.js';

export function readGlbNodes(path) {
  const bytes = readFileSync(path);
  if (bytes.readUInt32LE(0) !== 0x46546c67) throw new Error(`${path} is not a glb`);
  const json = JSON.parse(bytes.subarray(20, 20 + bytes.readUInt32LE(12)).toString('utf8'));
  return {
    nodes: (json.nodes || []).map(n => n.name).filter(Boolean),
    materials: (json.materials || []).map(m => m.name).filter(Boolean),
  };
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const path = process.argv[2] || fileURLToPath(new URL('../assets/slot.glb', import.meta.url));
  const { nodes, materials } = readGlbNodes(path);
  const { missingRequired, missingOptional } = checkNodes(nodes);
  console.log(`${path}: ${nodes.length} nodes, ${materials.length} materials`);
  for (const n of missingOptional) console.warn(`  missing (optional, page degrades) ${n}`);
  if (!materials.includes(FACE_MATERIAL)) console.warn(`  missing material ${FACE_MATERIAL} (EMI face stays idle)`);
  for (const n of missingRequired) console.error(`  MISSING (required) ${n}`);
  if (missingRequired.length) { console.error(`FAIL: ${missingRequired.length} required node(s) missing`); process.exit(1); }
  console.log('OK: every required node is present');
}
