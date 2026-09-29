/* ============================================================================
 * smoke/bridge-smoke.mjs - a frame the host sends once, at `ready`, still
 * reaches a module that attaches late.
 *
 * The host posts pbp:settings and pbp:media-state once per boot, the moment the
 * page says ready. boot.js says ready before the HUD (and with it Options >
 * Pictures) and the ramp's media pool have landed from their async imports,
 * and the bridge only buffers while NO listener exists at all. Both missed
 * their one frame: the picker never appeared, and the host's videoHoldSec
 * never reached the video card (bug hunt 2026-09-29, CHESS-9).
 *
 *   A  bridge.js: a listener that joins after the frames went by still gets
 *      the last of each, once; other frames are not replayed.
 *   B  ramp/media.js: a host pool made after the settings frame starts from it.
 *
 *   node smoke/bridge-smoke.mjs        (no server, no browser)
 * ==========================================================================*/

let failed = 0;
let passed = 0;
function expect(cond, name) {
  if (cond) { passed++; console.log('  ok   ' + name); }
  else { failed++; console.error('  FAIL ' + name); }
}

// A host the way WebView2 is: every page-side 'message' listener hears every frame.
const hostListeners = [];
globalThis.window = {
  chrome: { webview: { addEventListener: (t, fn) => { if (t === 'message') hostListeners.push(fn); }, removeEventListener() {}, postMessage() {} } },
};
const send = (m) => { for (const fn of [...hostListeners]) fn({ data: m }); };

const bridge = await import('../bridge.js');
const { createHostMedia } = await import('../ramp/media.js');

/* ---- A: the bridge ------------------------------------------------------------ */
const early = [];
bridge.onHostMessage((m) => early.push(m.type));   // preferences.js and boot.js listen from the start
send({ type: 'pbp:settings', videoHoldSec: 40, reducedMotion: false, whispers: [] });
send({ type: 'pbp:media-state', flavour: 'trance', last: 'trance', custom: {}, online: true, appWide: false });
send({ type: 'stake', op: 'limits', ok: true });
expect(early.join() === 'pbp:settings,pbp:media-state,stake', 'an early listener hears every frame once');

const late = [];
bridge.onHostMessage((m) => late.push(m));        // the HUD's picture picker, 0.4 to 2.6 s after ready
expect(late.map((m) => m.type).sort().join() === 'pbp:media-state,pbp:settings', 'a late listener still gets the two frames the host sends once');
expect(late.find((m) => m.type === 'pbp:settings')?.videoHoldSec === 40, 'the replayed frame is the host\'s own');
expect(!late.some((m) => m.type === 'stake'), 'nothing else is replayed');

send({ type: 'pbp:media-state', flavour: 'pink', last: 'pink', custom: {}, online: true, appWide: false });
expect(late.length === 3 && late[2].flavour === 'pink', 'after joining it hears new frames once, the normal way');
expect(early.length === 4, 'the early listener is not handed the replay');

const later = [];
bridge.onHostMessage((m) => later.push(m));
expect(later.find((m) => m.type === 'pbp:media-state')?.flavour === 'pink', 'the replay is the latest frame of each kind');

/* ---- B: the ramp's media pool --------------------------------------------------- */
const pool = createHostMedia();                     // attaches after the settings frame went by
let hold = null;
pool.onSettings((s) => { hold = s.videoHoldSec; });
expect(hold === 40, 'a media pool made after the settings frame starts from the host\'s videoHoldSec');
pool.dispose?.();

console.log(`\nbridge smoke: ${passed} passed, ${failed} failed`);
if (failed) process.exit(1);
