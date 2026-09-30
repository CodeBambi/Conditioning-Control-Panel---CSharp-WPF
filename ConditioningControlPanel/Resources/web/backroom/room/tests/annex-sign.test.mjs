// The Annex header sign is a blade sign seen from both sides of the doorway. A DoubleSide plane
// shows its back face as a mirror image, so the sign has to be two one-sided faces back to back.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, writeFileSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';

const here = (p) => new URL(p, import.meta.url);
const threeUrl = pathToFileURL(fileURLToPath(here('../../../vendor/three/three.module.min.js'))).href;
const walkUrl = pathToFileURL(fileURLToPath(here('../walk.js'))).href;
const source = readFileSync(here('../annex.js'), 'utf8');

async function load() {
  const code = source
    .replace(/from 'three'/, `from '${threeUrl}'`)
    .replace(/from '\.\/walk\.js'/, `from '${walkUrl}'`);
  const dir = mkdtempSync(join(tmpdir(), 'annex-sign-'));
  const file = join(dir, 'annex.mjs');
  writeFileSync(file, code);
  const ctx = new Proxy({}, { get: () => () => {}, set: () => true });
  globalThis.document ??= { createElement: () => ({ width: 0, height: 0, getContext: () => ctx }) };
  const annex = await import(pathToFileURL(file).href);
  const T = await import(threeUrl);
  return { annex, T };
}

test('a two-faced sign reads the right way round from both sides', async () => {
  const { annex, T } = await load();
  const sign = annex.makeSign('The Annex', { width: 1.4, height: 0.38, twoFaced: true });
  assert.equal(sign.material.side, T.FrontSide, 'the front face is one-sided');
  assert.equal(sign.children.length, 1, 'one back face');
  const back = sign.children[0];
  assert.equal(back.material, sign.material, 'the back shares the material');
  assert.equal(back.geometry, sign.geometry, 'and the geometry');
  assert.ok(Math.abs(back.rotation.y - Math.PI) < 1e-9, 'turned half round, so its text is not mirrored');

  const plaque = annex.makeSign('Coming soon');
  assert.equal(plaque.material.side, T.DoubleSide, 'a door plaque keeps its one readable side');
  assert.equal(plaque.children.length, 0);
});

test('the header sign beside the doorway is the two-faced one', () => {
  assert.match(source, /makeSign\(lex\('br_annex_sign', 'The Annex'\), \{[^}]*twoFaced: true/);
});
