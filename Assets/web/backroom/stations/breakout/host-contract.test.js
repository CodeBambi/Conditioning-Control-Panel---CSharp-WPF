import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read = f => readFileSync(new URL(f, import.meta.url), 'utf8');

// The host's close contract (room/main.js): answer `close` with exit-done, and finish an exit with it,
// or every panic and every Exit waits out the bridge's 800 ms forced close.
test('standalone page answers the host close and finishes its own exit', () => {
  const page = read('./play.html');
  assert.match(page, /bridge\.on\('close'/);
  assert.equal((page.match(/type: 'exit-done'/g) || []).length, 2);
});

test('a preview link never replaces a hosted player checkpoint', () => {
  assert.match(read('./station.js'), /previewRun = globalThis\.chrome\?\.webview \? null : previewCheckpoint\(q\)/);
});
