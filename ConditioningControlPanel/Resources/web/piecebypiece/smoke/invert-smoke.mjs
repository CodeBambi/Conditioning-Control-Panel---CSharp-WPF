/* ============================================================================
 * smoke/invert-smoke.mjs - Invert left/right and up/down for the camera drag
 * (ccp-bugs #1329).
 *
 * board/camera.js orbitBy runs every orbit drag through invertDrag from
 * game/preferences.js. Both flags start off, save, read back, and flip only
 * their own axis.
 *
 *   node smoke/invert-smoke.mjs        (no server, no browser)
 * ==========================================================================*/

import { readFileSync } from 'node:fs';

let failed = 0;
let passed = 0;
function expect(cond, name) {
  if (cond) { passed++; console.log('  ok   ' + name); }
  else { failed++; console.error('  FAIL ' + name); }
}

const store = new Map();
globalThis.localStorage = { getItem: (k) => store.get(k) ?? null, setItem: (k, v) => store.set(k, String(v)) };
globalThis.window = { chrome: { webview: { addEventListener() {}, postMessage() {} } } };
const { presentation, setPresentation, invertDrag } = await import('../game/preferences.js');

expect(presentation().invertX === false && presentation().invertY === false, 'both start off');
expect(invertDrag(3, -4).join() === '3,-4', 'off leaves the drag alone');

setPresentation({ invertX: true });
expect(presentation().invertX === true && presentation().invertY === false, 'left/right on alone');
expect(invertDrag(3, -4).join() === '-3,-4', 'left/right flips only dx');

setPresentation({ invertX: false, invertY: true });
expect(invertDrag(3, -4).join() === '3,4', 'up/down flips only dy');

setPresentation({ invertX: true });
expect(invertDrag(3, -4).join() === '-3,4', 'both flip both');
const saved = JSON.parse(store.get('pbp-presentation-v1'));
expect(saved.invertX === true && saved.invertY === true, 'both are saved');

setPresentation({ invertX: 'yes', invertY: 1 });
expect(presentation().invertX === true && presentation().invertY === true, 'a non-boolean patch changes nothing');

const camera = readFileSync(new URL('../board/camera.js', import.meta.url), 'utf8');
expect(/function orbitBy\(dx, dy, dt\) \{[\s\S]{0,300}invertDrag\(dx, dy\)/.test(camera), 'orbitBy flips through invertDrag');
const html = readFileSync(new URL('../index.html', import.meta.url), 'utf8');
expect(html.includes('id="game-invert-x"') && html.includes('id="game-invert-y"'), 'Options has both boxes');

console.log(`\n${passed} passed, ${failed} failed`);
process.exit(failed ? 1 : 0);
