// Every module the page loads must PARSE. station.js and render.js need a DOM, so no unit test imports them, and a
// syntax error in one shipped to the live preview on 2026-09-21: a regex lost its backslashes in a patch script,
// "//dev.html$/" became a comment, and the game would not load at all while 326 tests stayed green.
// node --check on a plain .js path does not catch it (it exits 0); the same bytes as .mjs do.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const modules = (dir) => fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => {
  const p = path.join(dir, e.name);
  if (e.isDirectory()) return e.name === 'assets' ? [] : modules(p);
  return e.name.endsWith('.js') && !e.name.endsWith('.test.js') ? [p] : [];
});

test('every station module parses as an ES module', () => {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'bo-syntax-')), files = modules(here);
  assert.ok(files.length > 30, 'found the station: ' + files.length);
  try {
    for (const file of files) {
      const copy = path.join(tmp, 'check.mjs'); fs.copyFileSync(file, copy);
      const run = spawnSync(process.execPath, ['--check', copy], { encoding: 'utf8' });
      assert.equal(run.status, 0, path.relative(here, file) + ' does not parse:\n' + String(run.stderr).split('\n').slice(0, 6).join('\n'));
    }
  } finally { fs.rmSync(tmp, { recursive: true, force: true }); }
});
