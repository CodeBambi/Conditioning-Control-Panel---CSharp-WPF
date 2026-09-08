/* ============================================================================
 * race/smoke/gifrain-row-check.mjs - the gif rain, on the road blocks.
 *
 *   node race/smoke/gifrain-row-check.mjs        (from Resources/web/dtrh; 0 on pass)
 *   RACE_SHOTS=1 node race/smoke/gifrain-row-check.mjs   (also writes shots/ png)
 *
 * Two halves. The pure one holds the table: the `gif-rain` preset wears the
 * gifrain bubble, it has a plate class of its own in race.css, the catalogue set
 * that asks for it is a real one, and the theme table and the catalogue are still
 * level with each other.
 *
 * The browser one drives a real worded run, because the thing that can go wrong
 * here is not in the table: a row that resolved to rain has to pour exactly ONE
 * cascade. Five stacked would be the whole screen. So the run reads two counters
 * back - `race.fxStats()`, every payload the mixer has poured, counted at the one
 * door they all go through, and `race.wordFaces().placed`, every bubble the field
 * has put on the road - and holds the cascades against the rain bubbles that were
 * there to pour them.
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
import { KIND_BY_ID } from '../bubbleKinds.js';
import { THEME_BY_PRESET, PRESETS_IN_USE, kindForPreset } from '../triggerTheme.js';
import { GIFRAIN_KIND, GIFRAIN_ROW_CHANCE } from '../cues.js';
import { TRIGGER_SETS } from '../../chart/editor/triggerSets.js';
import { wordedRoad, PEAKS_PER_SEC } from '../cloudChart.js';

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
 * 1. the table: a preset, a bubble, a plate
 * ==========================================================================*/
const rain = THEME_BY_PRESET['gif-rain'];
ok(!!rain, 'the theme table has a gif-rain row');
eq(kindForPreset('gif-rain'), GIFRAIN_KIND, 'and it wears the gifrain bubble');
ok(KIND_BY_ID[GIFRAIN_KIND] && KIND_BY_ID[GIFRAIN_KIND].spawn !== false, 'which is a bubble that still spawns');
eq(KIND_BY_ID[GIFRAIN_KIND].payload, 'gifCascade', 'and whose payload is the cascade itself');
const themes = Object.values(THEME_BY_PRESET).map((r) => r.theme);
eq(new Set(themes).size, themes.length, 'every preset in the table still has a plate class of its own');
const css = readFileSync(resolve(RACE, 'race.css'), 'utf8');
const missing = [...new Set(themes)].filter((t) => !css.includes('.rc-plate--' + t));
eq(missing.length, 0, 'and race.css carries one for every theme in it' + (missing.length ? ' (missing ' + missing.join(', ') + ')' : ''));
ok(/\.rc-plate--rain\b/.test(css), 'the rain plate is written down');
ok(/@keyframes rcRain\b/.test(css), 'with an animation of its own');
ok(/prefers-reduced-motion[\s\S]*rc-plate--rain/.test(css), 'and it holds still under reduced motion (Law VI)');

const sets = TRIGGER_SETS.filter((s) => s.preset === 'gif-rain');
ok(sets.length >= 1, sets.length + ' trigger set(s) ask for it: ' + sets.map((s) => s.id).join(', '));
ok(PRESETS_IN_USE.every((p) => THEME_BY_PRESET[p]), 'every preset the catalogue uses has a row in the theme table');
ok(TRIGGER_SETS.every((s) => KIND_BY_ID[kindForPreset(s.preset)] && KIND_BY_ID[kindForPreset(s.preset)].spawn !== false),
  'and not one set in the catalogue points at a darkened bubble');
ok(GIFRAIN_ROW_CHANCE > 0 && GIFRAIN_ROW_CHANCE < 0.25, 'a word row wears the rain about one time in six, not most times');

/* ============================================================================
 * the browser: the web folder plus the fixture chart
 * ==========================================================================*/
/** The fixture road: the opening transcript, driven LOUD and with rows in it.
 *
 * Two liberties, both because of what the real file is. Its energy is pinned flat at the top,
 * because the rain has an intensity floor of its own (bubbleKinds.js) and a headless run should
 * not have to wait out a swell to reach it. And it says its first trigger phrase 106 seconds in,
 * one every 55 seconds after that, so the fixture gets a plain word row every few seconds from the
 * start - the same shape the transcript's own `mark` rows have. The rows are what is under test;
 * how long the voice takes to say one is not. */
const ROW_EVERY = 4;
function hot(durationSec, perSec = PEAKS_PER_SEC) {
  const n = Math.ceil(durationSec * perSec);
  const peaks = new Float32Array(n * 2);
  for (let i = 0; i < n; i++) { peaks[i * 2] = -0.9; peaks[i * 2 + 1] = 0.9; }
  return peaks;
}
const road = wordedRoad({ peaks: hot(WORDS.durationSec), durationSec: WORDS.durationSec, name: ROW.title, hash: ROW.hash, words: WORDS });
const rows = [];
for (let t = 10, i = 0; t < 240; t += ROW_EVERY, i++) {
  rows.push({ kind: 'trigger', t, dur: 0.5, label: 'blank', conf: 0.99, weight: 1, setId: 'w-blank', cue: 'mark', id: 'rr' + i });
}
road.events = [...road.events, ...rows].sort((x, y) => x.t - y.t);
const FIXTURE = JSON.stringify(road);

const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 8872, DEBUG_PORT = 9342;
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

const prof = mkdtempSync(join(tmpdir(), 'race-gifrain-'));
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
 * 2. a rain row pours ONE cascade
 *
 * The pump takes the whole road at once, so every rain bubble that was laid gets
 * popped, which is the worst case for stacking: if a row could pour more than one
 * cascade, this is the run where it would.
 * ==========================================================================*/
let placed = {}, fx = {}, peak = 0, shots = 0;
for (let i = 0; i < 260; i++) {
  await ev(`window.__race.race.debugPickup('the_pump')`);
  await sleep(400);
  const live = await ev(`document.querySelectorAll('.sf-pfx-cascade').length`);
  if (live > peak) peak = live;
  if (SHOTS && live >= 3 && shots < 1) {
    const png = (await cdp('Page.captureScreenshot', { format: 'png' })).result?.data;
    if (png) {
      await mkdir(SHOTS, { recursive: true });
      const at = join(SHOTS, 'gifrain-row.png');
      await writeFile(at, Buffer.from(png, 'base64'));
      console.log('  --  shot: ' + at);
      shots++;
    }
  }
  fx = await json(`window.__race.race.fxStats()`);
  placed = await json(`window.__race.race.wordFaces().placed`);
  if ((fx.gifCascade || 0) >= 2 && (SHOTS ? shots : 1)) break;
}
const rainBubbles = placed[GIFRAIN_KIND] || 0, cascades = fx.gifCascade || 0;
console.log(`  --  the road laid ${rainBubbles} rain bubbles; the mixer poured ${cascades} cascades, ${peak} pictures on the glass at once`);
ok(rainBubbles > 0, 'the road laid the rain on it');
ok(cascades > 0, 'and popping one poured a cascade');
ok(cascades <= rainBubbles, 'never more cascades than there were rain bubbles to pour them: one row, one cascade');
ok(peak > 0 && peak <= 14, `the pictures never stacked past the cascade's own cap (${peak} at once, cap 14)`);
ok(!placed.flash && !placed.video, 'and the darkened kinds are still off the road');
ok(errs.length === 0, 'not one console error in the whole run' + (errs.length ? ':\n    ' + errs.slice(0, 5).join('\n    ') : ''));

await done(fails ? 1 : 0);

async function done(code) {
  try { ws && ws.close(); } catch (e) { /* never opened */ }
  try { chrome.kill(); } catch (e) { /* already gone */ }
  await new Promise((r) => server.close(r));
  await sleep(300);
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* chrome still has a lock */ }
  if (code) console.error(`\n${fails} failure${fails === 1 ? '' : 's'}`);
  else console.log('\ngifrain-row-check: all good');
  process.exit(code);
}
