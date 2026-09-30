/* ============================================================================
 * smoke/motion-smoke.mjs - a saved "Reduce motion" holds after every launch.
 *
 * game/preferences.js is imported (through director.js, hud.js and the rest)
 * before boot.js has made window.PBP, so its first write lands on nothing. And
 * the host's `pbp:settings` frame carries its own reducedMotion, which boot's
 * copy wrote straight over the player's choice. Everything that reads
 * PBP.settings.reducedMotion (replays, follow cam, shake, jiggle, drag, the
 * promotion picker, the HUD) then moved while Options showed the box ticked.
 *
 *   A  preferences.js, loaded the way the page loads it: no PBP at import, the
 *      board made after, then the host frame. The saved choice wins; the host
 *      and the OS can only add reduction.
 *   B  boot.js seeds PBP.settings from presentation() and never copies the
 *      host's reducedMotion over it.
 *
 *   node smoke/motion-smoke.mjs        (no server, no browser)
 * ==========================================================================*/

import { readFileSync } from 'node:fs';

let failed = 0;
let passed = 0;
function expect(cond, name) {
  if (cond) { passed++; console.log('  ok   ' + name); }
  else { failed++; console.error('  FAIL ' + name); }
}

/* ---- A: preferences.js, run the way the page loads it ------------------------ */
{
  const store = new Map([['pbp-presentation-v1', JSON.stringify({ experience: 'classic', reducedMotion: true })]]);
  globalThis.localStorage = { getItem: (k) => store.get(k) ?? null, setItem: (k, v) => store.set(k, String(v)) };
  let hostSays = null;
  globalThis.window = { chrome: { webview: { addEventListener: (t, fn) => { if (t === 'message') hostSays = fn; }, postMessage() {} } } };
  const { presentation, setPresentation } = await import('../game/preferences.js');
  expect(presentation().reducedMotion === true, 'the saved choice reads back at load');
  // boot.js makes the board after every import has run, then the host sends its settings
  window.PBP = { settings: { videoHoldSec: 15, reducedMotion: presentation().reducedMotion } };
  hostSays({ data: { type: 'pbp:settings', videoHoldSec: 15, reducedMotion: false, whispers: [] } });
  expect(window.PBP.settings.reducedMotion === true, 'a host frame that says false leaves the saved choice on');
  setPresentation({ reducedMotion: false });
  expect(window.PBP.settings.reducedMotion === false, 'unticking the box turns it off');
  hostSays({ data: { type: 'pbp:settings', reducedMotion: true } });
  expect(window.PBP.settings.reducedMotion === true, 'the host can still add reduction');
}

/* ---- B: boot.js wiring --------------------------------------------------------- */
{
  const boot = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  expect(/import \{[^}]*\bpresentation\b[^}]*\} from '\.\/game\/preferences\.js'/.test(boot), 'boot reads the player\'s presentation');
  expect(/settings: \{[^}]*reducedMotion: presentation\(\)\.reducedMotion/.test(boot), 'PBP.settings starts from the saved choice, not a hard false');
  const handler = boot.match(/m\.type !== 'pbp:settings'\) return;([\s\S]*?)Object\.assign\(window\.PBP\.settings, values\)/);
  const copy = handler && handler[1].match(/const \{([^}]*)\} = m;/);
  expect(!!copy && /\breducedMotion\b/.test(copy[1]), 'the pbp:settings copy leaves reducedMotion to preferences.js');
}

console.log(`\nmotion smoke: ${passed} passed, ${failed} failed`);
if (failed) process.exit(1);
