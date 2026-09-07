/* ============================================================================
 * race/smoke/label-px-check.mjs - the word on the road is big enough to read.
 *
 *   node race/smoke/label-px-check.mjs      (from Resources/web/dtrh; 0 on pass, 1 with a count)
 *   RACE_SHOTS=1 node race/smoke/label-px-check.mjs      (also writes shots/, untracked)
 *
 * THE THING THIS FILE EXISTS FOR. A bubble is 1.15 m across and the phone's own field of
 * view (race/viewport.js: FOV_BASE is a HORIZONTAL 72 wearing a vertical number) draws
 * that at about 20 px at 20 m. Four letters inside 20 px is not a word, it is a texture.
 * race/wordTags.js therefore gives the word its own sprite with its own screen-size floor,
 * and a floor is exactly the kind of promise that holds on the machine it was written on
 * and quietly breaks on a phone. So it is measured here, on a phone (390x844 at device
 * scale 2, the narrowest window the web build supports and the worst case for every number
 * below), on a real transcript, in a run that is driving itself.
 *
 * `?chart=<url>` hands race.html a whole road built here off the Bubble Induction
 * transcript, the same door race/smoke/captions-check.mjs uses. No audio is fetched or
 * played and no third party is touched.
 *
 * What it holds, over every frame it samples:
 *   1. the floor is in the file (a source guard: wordTags.js cannot be imported in node)
 *   2. a worded road actually puts tags on the glass, in LINES of four and more
 *   3. every visible tag's measured cap height is at least MIN_CAP_PX, from the far end
 *      of the fade all the way down to the pop point
 *   4. no two tags on one frame overlap, which is the whole reason the lift exists
 *   5. no tag is drawn off the glass
 *   6. a trigger row wears ONE tag, not five
 *   7. nothing on the console
 *
 * It never prints a line of a transcript: everything below is counted.
 *
 * CHROME: `CHROME_PATH` if it is set, else the usual Windows install.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { readFileSync, writeFileSync, mkdirSync, mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { wordedRoad, PEAKS_PER_SEC } from '../cloudChart.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const RACE = resolve(HERE, '..');
const WEB = resolve(RACE, '../..');                        // Resources/web
const SHOTS = resolve(WEB, '../../../shots');           // C:/wt-race-words/shots, untracked
const read = (rel) => JSON.parse(readFileSync(resolve(RACE, rel), 'utf8'));

/* ============================================================================
 * 1. the floor is written down (wordTags.js imports three and the DOM: source guard)
 * ==========================================================================*/
const src = readFileSync(resolve(RACE, 'wordTags.js'), 'utf8');
const constOf = (name) => Number((src.match(new RegExp(name + ' = ([0-9.]+)')) || [])[1]);
const MIN_CAP_PX = constOf('MIN_CAP_PX');
eq(MIN_CAP_PX, 18, 'the cap height floor is 18 px');
eq(constOf('REF_W_PX'), 390, 'and it is written for a 390 px wide phone');
eq(constOf('POOL'), 16, 'the tag pool is 16 sprites');
eq(constOf('TAG_WEAR'), 10, 'and the nearest 10 bubbles wear one');
eq(constOf('TEX_W'), 256, 'each on a 256 px canvas');
eq(constOf('TEX_H'), 64, 'by 64');
ok(/CRISP_LAYER/.test(src), 'the tags draw on the crisp full-res layer, not the blocky pass');
ok(/--rh-font/.test(src) && /800 /.test(src), 'and are set in the HUD sans at weight 800');
ok(!/serif/.test(src.replace(/sans-serif/g, 'sans')), 'with no serif anywhere near them');

/* ============================================================================
 * the road: the Bubble Induction transcript, charted here, served to ?chart=
 * ==========================================================================*/
const rows = read('words/index.json').rows;
const ROW = rows.find((r) => /bubble induction/i.test(String(r.title || ''))) || rows[0];
const WORDS = read('words/' + ROW.file);
/** A curve shaped like a spoken track, the same swell the other road checks use. */
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
const wordEvents = road.events.filter((e) => e.kind === 'word' && e.w);
const rowEvents = road.events.filter((e) => e.kind === 'trigger');
console.log(`the road: ${wordEvents.length} word bubbles, ${rowEvents.length} trigger rows, ${Math.round(road.source.durationSec)}s`);
ok(wordEvents.length > 200, 'the fixture road carries the whole script');

/* ============================================================================
 * the browser
 * ==========================================================================*/
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 8873, DEBUG_PORT = 9343;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };
const FIXTURE = JSON.stringify(road);

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

const prof = mkdtempSync(join(tmpdir(), 'race-tags-'));
const chrome = spawn(CHROME, [
  '--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`,
  '--no-first-run', '--no-default-browser-check', '--disable-gpu', '--mute-audio',
  '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required',
  '--window-size=390,844', 'about:blank',
], { stdio: 'ignore' });

async function done(code) {
  try { chrome.kill(); } catch (e) { /* already gone */ }
  await new Promise((r) => server.close(r));
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* windows holds the profile */ }
  process.exit(code);
}

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
await cdp('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });

const site = `http://127.0.0.1:${PORT}`;
await cdp('Page.navigate', { url: `${site}/dtrh/race.html?autostart=1&intro=0&cards=0&chart=${encodeURIComponent(`${site}/fixture.json`)}` });
let up = false;
for (let i = 0; i < 120 && !up; i++) {
  await sleep(250);
  up = await ev(`!!(window.__race && window.__race.race && window.__race.race.track && window.__race.race.wordTags())`);
}
ok(up, 'the page boots the worded road and builds the tag layer');
if (!up) {
  console.error('    boot: ' + await ev(`JSON.stringify({ race: !!window.__race, run: !!(window.__race&&window.__race.race), track: !!(window.__race&&window.__race.race&&window.__race.race.track), tags: !!(window.__race&&window.__race.race&&window.__race.race.wordTags) })`));
  if (errs.length) console.error('    said: ' + errs.slice(0, 6).join(' | '));
  await done(1);
}

/* ---- the sampling ---------------------------------------------------------
 * Every sample is one frame of the live run. race/track.js starts a chart PAUSED (the
 * host's 250 ms tick is the only thing that ever sets the second), so the clock is started
 * once, at the file's first word, and then left to walk on its own off the wall: what is
 * measured below is a road being driven, not a road being posed. */
const START = Math.max(0, (wordEvents[0] ? wordEvents[0].t : 0) - 3);
await ev(`window.__race.race.trackClock(${START.toFixed(2)}, true)`);
await sleep(400);
ok(await ev(`window.__race.race.track.playing === true`), `the clock is running, from ${START.toFixed(1)}s (the file's first word)`);
const SAMPLES = 150, EVERY_MS = 120;
const samples = [];
for (let i = 0; i < SAMPLES; i++) {
  const s = await json(`(()=>{ const r=window.__race.race, m=r.wordTags();
    return { t: r.track ? r.track.t : 0, vw: m.vw, vh: m.vh, floorPx: m.floorPx, tags: m.tags }; })()`);
  if (s) samples.push(s);
  await sleep(EVERY_MS);
}
ok(samples.length > 100, `sampled ${samples.length} frames of the run`);

let seen = 0, withTags = 0, lines4 = 0, lines6 = 0, thin = 0, overlaps = 0, offGlass = 0, rowTags = 0, bigTags = 0;
let minCap = Infinity, maxCap = 0, minDist = Infinity, maxDist = 0, mostTags = 0, best = null, bestRow = null;
const cover = (a, b) => !(a.right <= b.left || a.left >= b.right || a.bottom <= b.top || a.top >= b.bottom);
for (const s of samples) {
  const tags = s.tags || [];
  seen += tags.length;
  if (tags.length) withTags++;
  if (tags.length >= 4) lines4++;
  if (tags.length >= 6) lines6++;
  if (tags.length > mostTags) { mostTags = tags.length; best = s; }
  for (let i = 0; i < tags.length; i++) {
    const a = tags[i];
    if (a.capPx < minCap) minCap = a.capPx;
    if (a.capPx > maxCap) maxCap = a.capPx;
    if (a.dist < minDist) minDist = a.dist;
    if (a.dist > maxDist) maxDist = a.dist;
    if (a.capPx < MIN_CAP_PX - 0.01) thin++;
    if (a.top < 0 || a.bottom > s.vh || a.left < 0 || a.right > s.vw) offGlass++;
    if (a.big) bigTags++;
    if (a.rowN > 1) { rowTags++; if (!bestRow || tags.length > (bestRow.tags || []).length) bestRow = s; }
    for (let j = i + 1; j < tags.length; j++) if (cover(a, tags[j])) overlaps++;
  }
}
eq(samples.filter((s) => s.vw === 390 && s.vh === 844).length, samples.length, 'every sample was taken on a 390x844 phone');
ok(withTags > samples.length * 0.5, `the road wears its script: ${withTags} of ${samples.length} frames had a tag on the glass, ${seen} tags in all`);
ok(lines4 > 0, `and ${lines4} of them carried a LINE of four or more (the most at once was ${mostTags})`);
eq(thin, 0, `not one tag was drawn under the ${MIN_CAP_PX} px floor (smallest ${minCap === Infinity ? 0 : minCap.toFixed(1)} px, largest ${maxCap.toFixed(1)} px)`);
eq(overlaps, 0, 'and no two tags on one frame overlapped, however deep the line ran');
eq(offGlass, 0, 'every tag drawn was fully on the glass');
ok(maxDist > 20, `they were measured from ${maxDist.toFixed(1)} m out down to ${minDist.toFixed(1)} m, which is the pop point`);
ok(bigTags > 0, `${bigTags} of them were the 1.3x tag an accent word or a trigger row wears`);
ok(rowTags > 0, `and ${rowTags} were a trigger row's shared tag`);
const doubled = samples.reduce((n, s) => { const r = (s.tags || []).filter((t) => t.rowN > 1);
  return n + (r.length !== new Set(r.map((t) => t.w + '@' + Math.round(t.dist))).size ? 1 : 0); }, 0);
eq(doubled, 0, 'a row of five wears ONE tag, never five copies of it');
eq(errs.length, 0, 'with nothing on the console' + (errs.length ? ': ' + errs.slice(0, 4).join(' | ') : ''));
console.log(`  --  ${seen} tags over ${samples.length} frames, ${lines4} frames with a line of 4+, ${lines6} with 6+, cap ${minCap === Infinity ? 0 : minCap.toFixed(1)}..${maxCap.toFixed(1)} px`);

/* ---- the pictures the owner decides on ------------------------------------
 * Only with RACE_SHOTS=1: the check itself must not write into the worktree on a
 * plain run, and shots/ is never committed. One browser, so they are taken here
 * rather than in a second headless run. */
if (process.env.RACE_SHOTS) {
  mkdirSync(SHOTS, { recursive: true });
  const shoot = async (name) => {
    const r = await cdp('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
    const data = r.result?.data;
    if (!data) return null;
    const file = join(SHOTS, name);
    writeFileSync(file, Buffer.from(data, 'base64'));
    return file;
  };
  async function shotWhen(name, want, tries = 400) {   // wait for the frame the picture wants
    for (let i = 0; i < tries; i++) {
      const m = await json(`window.__race.race.wordTags()`);
      if (m && want(m.tags || [])) { const f = await shoot(name); return { file: f, tags: m.tags.length }; }
      await sleep(90);
    }
    return { file: null, tags: 0 };
  }
  const line = await shotWhen('words-line.png', (t) => t.length >= 4);
  ok(!!line.file, `shot: a line of ${line.tags} word tags -> ${line.file}`);
  // near enough to read: a row's tag at 30 m is a true picture of nothing much
  const row = await shotWhen('words-row.png', (t) => t.some((x) => x.rowN > 1 && x.dist < 20));
  ok(!!row.file, `shot: a trigger row's shared tag -> ${row.file}`);
}

console.log(fails ? `\nlabel-px-check: ${fails} failed` : '\nlabel-px-check: all good');
await done(fails ? 1 : 0);
