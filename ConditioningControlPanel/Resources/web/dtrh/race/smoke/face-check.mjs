/* ============================================================================
 * race/smoke/face-check.mjs - the word is painted ON the bubble, and it stays cheap.
 *
 *   node race/smoke/face-check.mjs      (from Resources/web/dtrh; 0 on pass, 1 with a count)
 *   RACE_SHOTS=1 node race/smoke/face-check.mjs      (also writes shots/, untracked)
 *
 * THE THING THIS FILE EXISTS FOR. The first cut hung the word over the bubble on its own
 * plate with a screen-size floor. The owner drove it and asked for the word INSIDE the
 * bubble instead, skewed and small far away, because the reading moment is the flash when it
 * pops. race/wordFace.js therefore paints the kind's sprite and the word into one
 * CanvasTexture and bubbles.js hangs that on the sprite. Two things can quietly go wrong with
 * that and neither shows up as an exception: the word can fail to land on the canvas at all
 * (a font that never resolved, a fit loop that shrank to nothing), and the texture cache can
 * grow with the script instead of holding its cap, which on a two thousand word road is a
 * slow leak of GPU memory. Both are measured here, on a phone (390x844 at device scale 2,
 * the narrowest window the web build supports), on a real transcript, in a run driving itself.
 *
 * `?chart=<url>` hands race.html a whole road built here off the Bubble Induction transcript,
 * the same door race/smoke/captions-check.mjs uses. No audio is fetched or played and no third
 * party is touched.
 *
 * What it holds, over every frame it samples:
 *   1. the cache cap and the fit rule are in the file (a source guard: wordFace.js needs a DOM)
 *   2. a worded road actually puts faced bubbles on the road, in LINES of four and more
 *   3. every face laid ink down inside the circle, and never filled it (the paint is measured
 *      by wordFace.js at paint time off its own scratch canvas)
 *   4. the texture cache never goes over CACHE_MAX, however long the script runs
 *   5. the bubble pool is exactly what it was: the faces buy no extra sprites
 *   6. a trigger row wears the word on EVERY bubble, and exactly one of them is the big one
 *   7. nothing on the console
 *
 * It never prints a word of the transcript: everything below is counted.
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
const SHOTS = resolve(WEB, '../../../shots');              // C:/wt-race-words/shots, untracked
const read = (rel) => JSON.parse(readFileSync(resolve(RACE, rel), 'utf8'));

/* ============================================================================
 * 1. the rules are written down (wordFace.js needs a canvas: source guard)
 * ==========================================================================*/
const src = readFileSync(resolve(RACE, 'wordFace.js'), 'utf8');
const constOf = (name) => Number((src.match(new RegExp(name + ' = ([0-9.]+)')) || [])[1]);
const CACHE_MAX = constOf('CACHE_MAX');
eq(CACHE_MAX, 96, 'the face cache holds 96 textures');
eq(constOf('TEX_PX'), 256, 'each one 256 px square (128 until 0909: too soft once the word grew)');
eq(constOf('TEXT_FRAC'), 0.8, 'and the word fits inside 80 percent of the diameter');
eq(constOf('MAX_LINES'), 2, 'over at most two lines');
eq(constOf('OUTLINE_PX'), 6, 'with a 6 px dark outline (and a HALO_PX halo) so it reads on the bubble highlight');
ok(/--rh-font/.test(src) && /800 \$\{size\}px/.test(src), 'set in the HUD sans at weight 800');
ok(/toLowerCase/.test(src), 'lowercase');
ok(!/serif/.test(src.replace(/sans-serif/g, 'sans')), 'with no serif anywhere near it');
ok(/tex\.dispose\(\)/.test(src), 'and an evicted texture is disposed, not dropped on the floor');
ok(!existsSync(resolve(RACE, 'wordTags.js')), 'the tag plate over the bubble is gone');

/* ============================================================================
 * the road: the Bubble Induction transcript, charted here, served to ?chart=
 * ==========================================================================*/
const rows = read('words/index.json').rows;
const ROW = rows.find((r) => /bubble induction/i.test(String(r.title || ''))) || rows[0];
// the aligner stamp lives on the index row, not on the race copy of the transcript, and
// race/words.js loadWords() hands it to the road: the fixture is read the same way here.
const WORDS = { ...read('words/' + ROW.file), engine: ROW.engine };
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

const prof = mkdtempSync(join(tmpdir(), 'race-face-'));
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
  up = await ev(`!!(window.__race && window.__race.race && window.__race.race.track && window.__race.race.wordFaces())`);
}
ok(up, 'the page boots the worded road and paints faces');
if (!up) {
  console.error('    boot: ' + await ev(`JSON.stringify({ race: !!window.__race, run: !!(window.__race&&window.__race.race), track: !!(window.__race&&window.__race.race&&window.__race.race.track), faces: !!(window.__race&&window.__race.race&&window.__race.race.wordFaces) })`));
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
  const s = await json(`(()=>{ const r=window.__race.race, m=r.wordFaces();
    return { t: r.track ? r.track.t : 0, cap: m.cap, count: m.count, peak: m.peak, made: m.made, evicted: m.evicted,
      pool: m.pool, ratios: m.faces.map((f)=>f.ratio),
      worn: m.worn.map((b)=>({ n: b.w.length, words: b.w.trim().split(/\\s+/).length, faced: b.faced, big: b.big, rowN: b.rowN, rowId: b.rowId, ahead: b.ahead })) }; })()`);
  if (s) samples.push(s);
  await sleep(EVERY_MS);
}
ok(samples.length > 100, `sampled ${samples.length} frames of the run`);

let worn = 0, framesWorn = 0, lines4 = 0, lines6 = 0, unfaced = 0, overCap = 0, blank = 0, filled = 0;
let minRatio = Infinity, maxRatio = 0, mostWorn = 0, twoLine = 0, bigOnes = 0;
let peakCount = 0, made = 0, evicted = 0, rowsSeen = 0, rowsPartial = 0, rowsMultiBig = 0;
const pools = new Set();
for (const s of samples) {
  const list = s.worn || [];
  worn += list.length;
  if (list.length) framesWorn++;
  if (list.length >= 4) lines4++;
  if (list.length >= 6) lines6++;
  if (list.length > mostWorn) mostWorn = list.length;
  if (s.count > s.cap) overCap++;
  if (s.peak > peakCount) peakCount = s.peak;
  made = Math.max(made, s.made); evicted = Math.max(evicted, s.evicted);
  pools.add(s.pool);
  for (const r of s.ratios || []) {
    if (r < minRatio) minRatio = r;
    if (r > maxRatio) maxRatio = r;
    if (!(r > 0.005)) blank++;                 // nothing was painted inside the circle
    if (r > 0.6) filled++;                     // the word ate the whole bubble
  }
  const byRow = new Map();
  for (const b of list) {
    if (!b.faced) unfaced++;
    if (b.words > 1) twoLine++;
    if (b.big) bigOnes++;
    if (b.rowN > 1) byRow.set(b.rowId, (byRow.get(b.rowId) || []).concat([b]));
  }
  for (const [, bs] of byRow) {
    rowsSeen++;
    if (bs.length > 1 && bs.some((b) => b.n !== bs[0].n)) rowsPartial++;   // one row, one word, all of it
    if (bs.filter((b) => b.big).length > 1) rowsMultiBig++;
  }
}
ok(framesWorn > samples.length * 0.5, `the road wears its script: ${framesWorn} of ${samples.length} frames had a faced bubble on it, ${worn} in all`);
ok(lines4 > 0, `and ${lines4} of them carried a LINE of four or more (the most at once was ${mostWorn})`);
eq(unfaced, 0, 'every word bubble was wearing a painted face, not the plain sprite');
eq(blank, 0, `and every face had ink inside the circle (coverage ${minRatio === Infinity ? 0 : (minRatio * 100).toFixed(1)}..${(maxRatio * 100).toFixed(1)} percent of it)`);
eq(filled, 0, 'with none of them filling the bubble edge to edge');
eq(overCap, 0, `the texture cache never went over its cap of ${CACHE_MAX} on the real road (high water ${peakCount}, ${made} painted, ${evicted} evicted)`);
eq(pools.size, 1, `the bubble pool is unchanged all run (${[...pools].join('/')} sprites, faces buy none)`);
ok(twoLine > 0, `${twoLine} of them carried a phrase over two lines`);
ok(bigOnes > 0, `${bigOnes} were the bigger bubble an accent word or a row centre wears`);
ok(rowsSeen > 0, `${rowsSeen} trigger rows passed under the sampler`);
eq(rowsPartial, 0, 'every bubble of a row wore the same word, not one of five');
eq(rowsMultiBig, 0, 'and exactly one bubble of a row was the big one');
eq(errs.length, 0, 'with nothing on the console' + (errs.length ? ': ' + errs.slice(0, 4).join(' | ') : ''));
console.log(`  --  ${worn} faced bubbles over ${samples.length} frames, ${lines4} frames with a line of 4+, ${lines6} with 6+, cache ${peakCount}/${CACHE_MAX}`);

/* ---- the cap itself -------------------------------------------------------
 * Eighteen seconds of a real road paints about forty faces, so the sampling above proves the
 * cache stays inside its cap but never reaches it. A chant, or the far side of a 17 minute
 * file, does. That is asked of the painter directly, in the page (it needs a canvas), with
 * more words than the cap holds and then an old key asked for again: the LRU must hold at the
 * cap, dispose what it drops, and keep the one that was just used. */
// json() stringifies before the promise settles, so this one is awaited in the page and
// handed back as text.
const lru = JSON.parse(await ev(`(async () => {
  const m = await import('${site}/dtrh/race/wordFace.js');
  const f = m.createWordFaces();
  const over = 40;
  for (let i = 0; i < m.CACHE_MAX; i++) f.faceFor({ key: 'treat:png', word: 'w' + i });
  const first = f.faceFor({ key: 'treat:png', word: 'w0' });   // touch the oldest: it must survive
  const atCap = f.report().count;
  for (let i = m.CACHE_MAX; i < m.CACHE_MAX + over; i++) f.faceFor({ key: 'treat:png', word: 'w' + i });
  const r = f.report();
  const kept = f.faceFor({ key: 'treat:png', word: 'w0' }) === first;
  f.dispose();
  const after = f.report();
  return JSON.stringify({ cap: m.CACHE_MAX, atCap, count: r.count, made: r.made, evicted: r.evicted,
    kept, over, gone: after.count, twoLines: m.linesOf('good girl').length, oneLine: m.linesOf('deeper').length });
})()`));
eq(lru.atCap, lru.cap, 'asked for its cap in words, the painter holds exactly that many textures');
eq(lru.count, lru.cap, `and asked for ${lru.cap + lru.over} it still holds ${lru.cap}`);
eq(lru.evicted, lru.over, 'having disposed every one it dropped');
ok(lru.kept, 'the word it was asked for most recently is the one it kept');
eq(lru.gone, 0, 'and dispose() lets the whole lot go');
eq(lru.twoLines, 2, 'a two word bubble is set over two lines');
eq(lru.oneLine, 1, 'a single word over one');

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
  async function shotWhen(name, want, tries = 500) {   // wait for the frame the picture wants
    for (let i = 0; i < tries; i++) {
      const m = await json(`window.__race.race.wordFaces().worn.map((x)=>({ big: x.big, rowN: x.rowN, ahead: x.ahead, popped: x.popped }))`);
      if (m && want(m)) { const f = await shoot(name); return { file: f, n: m.length }; }
      await sleep(90);
    }
    return { file: null, n: 0 };
  }
  // popped ones are still in the list for the 0.14 s of their flash and the kart pops a whole
  // line as it drives down it, so the picture waits for four that are still ON the road ahead
  const line = await shotWhen('words-line.png', (b) => {
    const on = b.filter((x) => !x.popped && x.rowN <= 1 && x.ahead > 2 && x.ahead < 36);
    return on.length >= 4 && on.some((x) => x.ahead < 13);   // and one of them near enough to read
  });
  ok(!!line.file, `shot: ${line.n} word bubbles wearing the script -> ${line.file}`);
  // near enough to read: a row at 30 m is a true picture of nothing much
  const row = await shotWhen('words-row.png', (b) => b.filter((x) => !x.popped && x.rowN > 1 && x.ahead > 3 && x.ahead < 18).length >= 3);
  ok(!!row.file, `shot: a trigger row, every bubble wearing the word -> ${row.file}`);
  // THE GUARD, in one frame: a row with the script still running on BOTH sides of it. The old rule
  // took every word within 0.4 s of a trigger and left the row standing on empty road; it owns the
  // seconds its own phrase is being said now, and the sentence around it is there to be driven.
  const guard = await shotWhen('guard.png', (b) => {
    const on = b.filter((x) => !x.popped);
    // the row near enough to read, and a word bubble close on either side of it: both of them
    // inside the metres the guard clears, which is the whole picture being asked for
    const at = on.filter((x) => x.rowN > 1 && x.ahead > 7 && x.ahead < 17).map((x) => x.ahead).sort((p, q) => p - q)[0];
    if (at == null) return false;
    return on.some((x) => x.rowN <= 1 && x.ahead > 3 && x.ahead < at - 1)
      && on.some((x) => x.rowN <= 1 && x.ahead > at + 1 && x.ahead < at + 9);
  });
  ok(!!guard.file, `shot: a row with the words either side of it still on the road -> ${guard.file}`);
}

console.log(fails ? `\nface-check: ${fails} failed` : '\nface-check: all good');
await done(fails ? 1 : 0);
