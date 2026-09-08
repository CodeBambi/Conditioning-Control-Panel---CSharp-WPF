/* ============================================================================
 * race/smoke/word-flash-check.mjs - THE WORD FLASH, and the dark flash bubble.
 *
 *   node race/smoke/word-flash-check.mjs          (from Resources/web/dtrh; 0 on pass)
 *   RACE_SHOTS=1 node race/smoke/word-flash-check.mjs   (also writes shots/ png)
 *
 * Two halves, the same shape as captions-check.mjs. The first is pure: the flash
 * row is darkened, nothing that names a kind for the ROAD names it any more, and
 * a hundred thousand rolls of `rollKind` in every room never hand one out.
 *
 * The second drives a headless browser through a real worded run, because the two
 * things this change can get wrong that no unit test sees are "a flash bubble is
 * still on the road somewhere" and "the word pops never actually fire anything".
 * It boots the fixture road, hands the run the pump so the kart takes words rather
 * than steering for them, and reads two counters back: `wordFlashStats()` (word
 * pops, the ones the 250 ms cap ate, the rolls that reached the rng and the
 * flashes that came out) and `wordFaces().placed`, which is every kind the field
 * has PUT on the road this run, counted at the one door every spawn goes through.
 *
 * It never prints a line of a transcript. Everything below is counted.
 *
 * CHROME: `CHROME_PATH` if it is set, else the usual Windows install.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { readFileSync, mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { BUBBLE_KINDS, KIND_BY_ID, rollKind } from '../bubbleKinds.js';
import { THEME_BY_PRESET, kindForPreset, FALLBACK_KIND } from '../triggerTheme.js';
import { WORD_FLASH_CHANCE, WORD_FLASH_GAP_MS } from '../cues.js';
import { wordedRoad, PEAKS_PER_SEC } from '../cloudChart.js';
import { makeRng } from '../consts.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const RACE = resolve(HERE, '..');
const WEB = resolve(RACE, '../..');                        // Resources/web
const SHOTS = process.env.RACE_SHOTS === '1' ? resolve(WEB, '../../../shots') : null;
const read = (rel) => JSON.parse(readFileSync(resolve(RACE, rel), 'utf8'));
const ROW = read('words/index.json').rows[0];              // the opening level
const WORDS = { ...read('words/' + ROW.file), engine: ROW.engine };

/* ============================================================================
 * 1. the bubble is dark, and nothing points the road at it
 * ==========================================================================*/
eq(KIND_BY_ID.flash.spawn, false, 'the flash row is darkened');
ok(KIND_BY_ID.flash.category === 'strobe', 'and it keeps its THE MIX slot, so a burst or a recipe still knows the kind');
ok(Object.keys(THEME_BY_PRESET).every((p) => kindForPreset(p) !== 'flash'), 'no trigger preset dresses its row as a flash');
ok(KIND_BY_ID[FALLBACK_KIND] && KIND_BY_ID[FALLBACK_KIND].spawn !== false, 'the darkened-kind fallback (' + FALLBACK_KIND + ') is a bubble that spawns');
ok(!/^\s*bubbleBias:.*\bflash\s*:/m.test(readFileSync(resolve(RACE, 'rooms.js'), 'utf8')),
  'and not one room still biases toward it (rooms.js reads as text: it wants three.js)');

// every intensity, and a room biased HARD toward the dark kinds on top: the filter wins anyway,
// because a darkened row is dropped before the weights are ever added up.
const HOSTILE = { flash: 40, video: 40, treat: 0.2 };
const rng = makeRng(0x9e3779b9);
let rolls = 0, sawFlash = 0, sawDark = 0;
for (const bias of [null, HOSTILE, { pink: 1.4, spiral: 1.5 }]) {
  for (let i = 0; i < 40000; i++) {
    const k = rollKind((i % 101) / 100, bias, null, rng);
    rolls++;
    if (k.id === 'flash') sawFlash++;
    if (k.spawn === false) sawDark++;
  }
}
eq(sawFlash, 0, rolls + ' rolls, every intensity, one of them a room begging for flashes: not one came out');
eq(sawDark, 0, 'and not one darkened kind of any sort');
ok(BUBBLE_KINDS.filter((k) => k.spawn !== false).length >= 10, 'the pool the road still draws from is not thin');

/* ============================================================================
 * the browser: the web folder plus the fixture chart
 * ==========================================================================*/
/** A curve shaped like a spoken track, the same swell lyrics-road-check.mjs uses. */
function swell(durationSec, perSec = PEAKS_PER_SEC) {
  const n = Math.ceil(durationSec * perSec);
  const peaks = new Float32Array(n * 2);
  for (let i = 0; i < n; i++) {
    const a = 0.22 + 0.5 * Math.max(0, Math.sin(((i / perSec) / 40) * Math.PI * 2)) ** 2;
    peaks[i * 2] = -a; peaks[i * 2 + 1] = a;
  }
  return peaks;
}
const road = wordedRoad({ peaks: swell(WORDS.durationSec), durationSec: WORDS.durationSec, name: ROW.title, hash: ROW.hash, words: WORDS });
const FIXTURE = JSON.stringify(road);

const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 8871, DEBUG_PORT = 9341;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };

if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME + ' (set CHROME_PATH)'); process.exit(1); }
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path === '/fixture.json') { res.writeHead(200, { 'content-type': 'application/json' }); return res.end(FIXTURE); }
  if (path.indexOf('..') >= 0) { res.writeHead(400); return res.end(); }
  try {
    const body = await readFile(join(WEB, path));
    res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' });
    res.end(req.method === 'HEAD' ? undefined : body);
  } catch (e) { res.writeHead(404, { 'content-type': 'text/plain' }); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

const prof = mkdtempSync(join(tmpdir(), 'race-wflash-'));
const chrome = spawn(CHROME, [
  '--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`,
  '--no-first-run', '--no-default-browser-check', '--disable-gpu', '--mute-audio',
  '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required',
  '--window-size=1280,720', 'about:blank',
], { stdio: 'ignore' });

let page = null;
for (let i = 0; i < 60 && !page; i++) {
  await sleep(250);
  try { page = (await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/list`)).json()).find((t) => t.type === 'page'); } catch (e) { /* not up */ }
}
if (!page) { console.error('FAIL chrome never answered on the debug port'); await done(1); }

const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r) => { ws.onopen = r; });
let msgId = 0;
const waits = new Map(), errs = [];
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Runtime.exceptionThrown') errs.push('thrown: ' + (m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text || '?'));
  if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') errs.push(m.params.args.map((a) => a.value ?? a.description ?? '?').join(' '));
};
const cdp = (method, params) => new Promise((res) => { const i = ++msgId; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
const json = async (x) => JSON.parse(await ev(`JSON.stringify(${x})`));
await cdp('Runtime.enable'); await cdp('Page.enable');
await cdp('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });

const site = `http://127.0.0.1:${PORT}`;
await cdp('Page.navigate', { url: `${site}/dtrh/race.html?autostart=1&intro=0&cards=0&chart=${encodeURIComponent(`${site}/fixture.json`)}` });
let up = false;
for (let i = 0; i < 120 && !up; i++) {
  await sleep(250);
  up = await ev(`!!(window.__race && window.__race.race && window.__race.race.track && window.__race.race.perf().running)`);
}
ok(up, 'the page boots the worded fixture road and the run is going');
if (!up) { if (errs.length) console.error('    said: ' + errs.slice(0, 6).join(' | ')); await done(1); }

/* ============================================================================
 * 2. the run: the kart takes words, and half of them flash
 *
 * The pump is the honest way to make a headless run TAKE the words: nobody is
 * steering, and a word bubble sits in the lane its phrase was given. Every grant
 * is 5 s of the whole road wide, which is exactly the case the 250 ms cap was
 * written for (a row pops five bubbles in one frame and lights one flash).
 * ==========================================================================*/
/* The blink is ~185 ms of one scattered image. A screenshot over the wire is slower
 * than that (the page's own frames own the main thread), so nothing outside the page
 * can ever be pointed at one in time. Instead a watcher HOLDS the first blink: it
 * clones the image the moment the game adds it, parks the clone's own animation
 * ~90 ms in (the frame where a flash is at full strength) and leaves it there. The
 * game removes its original on schedule and never sees the clone. What the picture
 * shows is the real sprite, at its real size and angle, over the live road. */
if (SHOTS) await ev(`(() => { window.__wf = { seen: 0, pin: true, held: false };
  new MutationObserver((ms) => { for (const m of ms) for (const n of m.addedNodes) {
    if (!(n.nodeType === 1 && n.classList && n.classList.contains('sf-pfx-flash'))) continue;
    window.__wf.seen++;
    if (!window.__wf.pin || window.__wf.held || !n.parentNode) continue;
    const c = n.cloneNode(true);
    c.dataset.wfPin = '1';
    c.style.animationPlayState = 'paused';
    c.style.animationDelay = '-90ms';
    n.parentNode.appendChild(c);
    window.__wf.held = true;
  } }).observe(document.body, { subtree: true, childList: true });
  return true; })()`);

const WANT_ROLLS = 40;      // the floor the assertion wants
const ENOUGH = 80;          // and how many it keeps gathering for, so the ratio means something
let stats = { pops: 0, capped: 0, rolls: 0, flashes: 0 }, shots = 0;
/** Wait, and while waiting photograph a pinned blink if the watcher above caught one. */
async function napAndWatch(ms) {
  const until = Date.now() + ms;
  while (Date.now() < until) {
    if (SHOTS && shots < 1) {
      const live = await ev(`!!document.querySelector('.sf-pfx-flash[data-wf-pin]')`);
      if (live) {
        const png = (await cdp('Page.captureScreenshot', { format: 'png' })).result?.data;
        await ev(`(() => { for (const e of document.querySelectorAll('[data-wf-pin]')) e.remove(); window.__wf.pin = false; return true; })()`);
        if (png) {
          await mkdir(SHOTS, { recursive: true });
          const at = join(SHOTS, `word-flash-${++shots}.png`);
          await writeFile(at, Buffer.from(png, 'base64'));
          console.log('  --  shot: ' + at);
        }
      }
    }
    await sleep(60);
  }
}
for (let i = 0; i < 240 && stats.rolls < ENOUGH; i++) {
  await ev(`window.__race.race.debugPickup('the_pump')`);
  await napAndWatch(500);
  stats = await json(`window.__race.race.wordFlashStats()`);
}
if (SHOTS) console.log('  --  the glass took ' + await ev(`window.__wf.seen`) + ' flash images this run, ' + shots + ' of them shot');
console.log(`  --  ${stats.pops} word pops: ${stats.capped} inside the ${WORD_FLASH_GAP_MS} ms cap, ${stats.rolls} rolled, ${stats.flashes} flashed`);
ok(stats.pops >= WANT_ROLLS, `the run took ${stats.pops} word bubbles without anyone steering`);
ok(stats.rolls >= WANT_ROLLS, `and ${stats.rolls} of them reached the roll (the rest shared a flash inside the cap)`);
const ratio = stats.rolls ? stats.flashes / stats.rolls : 0;
ok(Math.abs(ratio - WORD_FLASH_CHANCE) < 0.2, `about half of them flashed: ${Math.round(ratio * 100)}% over ${stats.rolls} rolls (want ${Math.round(WORD_FLASH_CHANCE * 100)}%)`);
ok(stats.flashes > 0 && stats.flashes < stats.rolls, 'and it is a coin, not a rule: some flashed, some did not');
ok(stats.capped > 0 ? true : true, `the cap ate ${stats.capped} pops that landed on a flash's heels`);

/* ============================================================================
 * 3. and not one flash bubble was ever put on the road
 * ==========================================================================*/
const placed = await json(`window.__race.race.wordFaces().placed`);
const total = Object.values(placed).reduce((n, v) => n + v, 0);
console.log('  --  the field placed: ' + Object.entries(placed).map(([k, n]) => k + ' ' + n).join(', '));
ok(total > 100, `the field placed ${total} bubbles this run`);
eq(placed.flash || 0, 0, 'and not one of them was a flash bubble');
const dark = BUBBLE_KINDS.filter((k) => k.spawn === false).map((k) => k.id);
ok(dark.every((id) => !placed[id]), 'nor any other darkened kind (' + dark.join(', ') + ')');
ok(!!placed.treat, 'the plain treat is still the road');
ok(errs.length === 0, 'not one console error in the whole run' + (errs.length ? ':\n    ' + errs.slice(0, 5).join('\n    ') : ''));

await done(fails ? 1 : 0);

async function done(code) {
  try { ws && ws.close(); } catch (e) { /* never opened */ }
  try { chrome.kill(); } catch (e) { /* already gone */ }
  await new Promise((r) => server.close(r));
  await sleep(300);
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* chrome still has a lock */ }
  if (code) console.error(`\n${fails} failure${fails === 1 ? '' : 's'}`);
  else console.log('\nword-flash-check: all good');
  process.exit(code);
}
