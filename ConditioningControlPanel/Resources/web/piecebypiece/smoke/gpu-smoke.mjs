/* ============================================================================
 * smoke/gpu-smoke.mjs - a GPU that drops the board gets the lite one, once.
 *
 *   A  gfxLite: off by default, on for a week after a loss, on with ?gfx=lite,
 *      off when storage throws.
 *   B  watchContextLoss: full board -> remember + reload; lite board or no
 *      storage -> the give-up card, never a reload loop.
 *   C  boot.js retries a board that cannot start once in lite, and the lite
 *      board loads neither the glow nor the reflected room.
 *
 *   node smoke/gpu-smoke.mjs        (no server, no browser)
 * ==========================================================================*/

import { readFileSync } from 'node:fs';

let failed = 0;
let passed = 0;
function expect(cond, name) {
  if (cond) { passed++; console.log('  ok   ' + name); }
  else { failed++; console.error('  FAIL ' + name); }
}

function memoryStore() {
  const m = new Map();
  return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), map: m };
}
function setWindow({ search = '', storage = memoryStore() } = {}) {
  globalThis.window = {
    location: { search },
    get localStorage() { if (storage === 'throws') throw new Error('denied'); return storage; },
  };
  return storage;
}
function fakeCanvas() {
  const listeners = new Map();
  return {
    addEventListener: (t, f) => listeners.set(t, f),
    removeEventListener: (t, f) => { if (listeners.get(t) === f) listeners.delete(t); },
    lose() { const f = listeners.get('webglcontextlost'); let prevented = false; if (f) f({ preventDefault: () => { prevented = true; } }); return prevented; },
    has: (t) => listeners.has(t),
  };
}

setWindow();
const { GPU, gfxLite, rememberLite, watchContextLoss } = await import('../board/gpu.js');

/* ---- A ----------------------------------------------------------------------- */
{
  setWindow();
  expect(gfxLite() === false, 'A full board by default');
  const now = 1_000_000_000_000;
  rememberLite(now);
  expect(gfxLite(now + 1000) === true, 'A lite right after a loss');
  expect(gfxLite(now + GPU.holdMs - 1) === true, 'A lite for the week');
  expect(gfxLite(now + GPU.holdMs + 1) === false, 'A full again after the week');
  setWindow({ search: '?gfx=lite' });
  expect(gfxLite() === true, 'A ?gfx=lite forces it');
  setWindow({ storage: 'throws' });
  expect(gfxLite() === false, 'A storage that throws reads as full');
}

/* ---- B ----------------------------------------------------------------------- */
{
  setWindow();
  let reloads = 0; let gaveUp = 0;
  const c = fakeCanvas();
  watchContextLoss(c, { lite: false, onGiveUp: () => gaveUp++, reload: () => reloads++ });
  const prevented = c.lose();
  expect(prevented, 'B the loss is prevented (no default teardown)');
  expect(reloads === 1 && gaveUp === 0, 'B full board: one reload');
  expect(gfxLite() === true, 'B the reload comes back lite');
  expect(!c.has('webglcontextlost'), 'B listener drops itself after one loss');

  reloads = 0; gaveUp = 0;
  const c2 = fakeCanvas();
  watchContextLoss(c2, { lite: true, onGiveUp: () => gaveUp++, reload: () => reloads++ });
  c2.lose();
  expect(reloads === 0 && gaveUp === 1, 'B lite board: the card, no reload');

  setWindow({ storage: 'throws' });
  reloads = 0; gaveUp = 0;
  const c3 = fakeCanvas();
  watchContextLoss(c3, { lite: false, onGiveUp: () => gaveUp++, reload: () => reloads++ });
  c3.lose();
  expect(reloads === 0 && gaveUp === 1, 'B no storage: the card, never a reload loop');
}

/* ---- C ----------------------------------------------------------------------- */
{
  const boot = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  expect(/createScene\(\{ canvas: dom\.canvas, lite \}\)[\s\S]*lite = true;[\s\S]*createScene\(\{ canvas: dom\.canvas, lite \}\)/.test(boot), 'C a board that cannot start is retried lite');
  expect(/if \(!lite\) import\('\.\/board\/bloom\.js'\)/.test(boot), 'C no glow on the lite board');
  expect(/if \(!lite\) import\('\.\/board\/env\.js'\)/.test(boot), 'C no reflected room on the lite board');
  expect(/watchContextLoss\(dom\.canvas/.test(boot), 'C the canvas is watched for a lost context');
  const scene = readFileSync(new URL('../board/scene.js', import.meta.url), 'utf8');
  expect(/antialias: !lite/.test(scene) && /shadowMap\.enabled = !lite/.test(scene), 'C lite drops MSAA and the shadow map');
}

console.log(`\n${passed} passed, ${failed} failed`);
if (failed) process.exit(1);
