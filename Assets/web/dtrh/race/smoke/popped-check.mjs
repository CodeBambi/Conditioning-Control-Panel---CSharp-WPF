/* ============================================================================
 * race/smoke/popped-check.mjs - THE THOUGHTS COUNT and the per-track best
 * (race/popped.js), pure first and then driven through the REAL page.
 *
 *   node race/smoke/popped-check.mjs      (exits 0 on pass, 1 with a count)
 *
 * A thought is one word bubble: one `word` event of the chart. The owner's ask,
 * phone testing 2026-09-09: "lets have the ui actually track how many bubbles out
 * of the total they got and show in the main menu the personal best on each
 * completed track (eg Popped 345/560 thoughts)".
 *
 * NOTHING LEAVES THIS MACHINE. The road is generated here out of a transcript
 * that ships with the game (race/words), served at /fixture.json, and the levels
 * list is a stub of two rows whose files are never fetched: no page in here taps
 * a level, so not one byte of audio is asked for.
 *
 * What it holds:
 *   1. the store, pure: what a key is, what beats a best, what a private window
 *      does to all of it, and the two lines it writes
 *   2. THE TOTAL IS KNOWN AT START: the run reads it off the chart before the
 *      first metre, and the HUD says `popped 0 / M` there and then
 *   3. the count goes up as the kart takes words, and the HUD follows it
 *   4. a run still going files NOTHING
 *   5. the end of the chart files the best, and the End card carries the row
 *   6. the menu: the track plate and the `track ·` status line carry the best
 *   7. the levels panel: the level's own row carries it, looked up with no chart
 *      and no network at all
 *
 * CHROME: `CHROME_PATH` if it is set, else the usual Windows install. Nothing is
 * installed by this file and the profile it makes is deleted on the way out.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { readFileSync, mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { POPPED_KEY, keyFor, readBests, bestFor, saveBest, bestLine, poppedLine } from '../popped.js';
import { wordedRoad, PEAKS_PER_SEC } from '../cloudChart.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const RACE = resolve(HERE, '..');
const WEB = resolve(RACE, '../..');                        // Resources/web
const read = (rel) => JSON.parse(readFileSync(resolve(RACE, rel), 'utf8'));

/* ============================================================================
 * 1. the store, pure
 * ==========================================================================*/
/** localStorage's shape, in a Map, so the store can be driven without a browser. */
function fakeStore(seed = null) {
  const m = new Map(seed ? [[POPPED_KEY, JSON.stringify(seed)]] : []);
  return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), _map: m };
}
/** A store that throws on every touch: a private window, or site data turned off. */
const deadStore = { getItem() { throw new Error('nope'); }, setItem() { throw new Error('nope'); } };

eq(keyFor({ hash: 'abc' }), 'abc', 'a track with a hash files under it');
eq(keyFor({ cloudId: 'AbC' }), 'cid:abc', 'a track with no hash files under its cloud id, lower cased');
eq(keyFor({}), '', 'and a track with neither has nowhere to file');
{
  const s = fakeStore();
  const a = saveBest(s, { hash: 'h1', cloudId: 'c1', name: 'one', popped: 300, total: 560 });
  ok(a.wrote && a.rec.popped === 300, 'the first completed run is the best there is');
  const b = saveBest(s, { hash: 'h1', cloudId: 'c1', name: 'one', popped: 210, total: 560 });
  ok(!b.wrote && b.rec.popped === 300, 'a worse run does not overwrite it');
  const c = saveBest(s, { hash: 'h1', cloudId: 'c1', name: 'one', popped: 345, total: 560 });
  ok(c.wrote && c.rec.popped === 345, 'and a better one does');
  const bests = readBests(s);
  ok(bestFor(bests, { hash: 'h1' }).popped === 345, 'the record comes back by hash');
  ok(bestFor(bests, { cloudId: 'c1' }).popped === 345, 'and by cloud id, which is all a levels row knows');
  ok(bestFor(bests, { hash: 'nope', cloudId: 'nope' }) === null, 'a track nobody finished has no record');
  eq(bestLine(bests.h1), 'popped 345 / 560 thoughts', 'the menu line reads');
  eq(poppedLine(12, 560), 'popped 12 / 560', 'and the hud line reads');
  eq(bestLine(null), '', 'no record is no line');
  eq(poppedLine(4, 0), '', 'and no total is no line');
}
{
  const s = fakeStore({ h1: { popped: 12, total: 0 }, h2: { popped: 900, total: 100 }, h3: 'rubbish' });
  eq(Object.keys(readBests(s)).length, 0, 'a corrupt record is dropped rather than believed');
  eq(Object.keys(readBests(deadStore)).length, 0, 'a store that throws reads as an empty map');
  ok(saveBest(deadStore, { hash: 'h', popped: 5, total: 9 }).wrote === true, 'and a write into one is swallowed, not thrown');
  ok(saveBest(fakeStore(), { hash: '', cloudId: '', popped: 5, total: 9 }).wrote === false, 'a track with no key files nothing');
}

/* ============================================================================
 * the browser: the web folder, one generated road, and a stub levels list
 * ==========================================================================*/
const ROW = read('words/index.json').rows[0];              // the opening level, whole and shipped
const WORDS = { ...read('words/' + ROW.file), engine: ROW.engine };
/** A curve shaped like a spoken track (the swell lyrics-road-check.mjs uses). */
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
/** The number the whole feature is about: every `word` event of the chart, counted here. */
const TOTAL = road.events.filter((e) => e.kind === 'word').length;
ok(TOTAL > 50, `the fixture road carries ${TOTAL} word bubbles to take`);

const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 8873, DEBUG_PORT = 9345;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };
/** Two rows. The first IS the fixture track (same hash), the second is a file nobody has driven. */
const LEVELS = {
  version: 1,
  sets: [{
    id: 'stub-set', title: 'A Stub Set', source: 'stub', playlistId: 'stub', playlistUrl: 'https://bambicloud.com/playlist/stub',
    levels: [
      { n: 1, id: 'stub-one', title: ROW.title, url: `http://127.0.0.1:${PORT}/stub/one.wav`, durationSec: Math.round(WORDS.durationSec), bytes: 1024, hash: ROW.hash, trackNum: 0 },
      { n: 2, id: 'stub-two', title: 'The Second One', url: `http://127.0.0.1:${PORT}/stub/two.wav`, durationSec: 90, bytes: 1024, hash: 'nothing-has-this-hash', trackNum: 1 },
    ],
  }],
};

if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME + ' (set CHROME_PATH)'); process.exit(1); }
const asked = [];
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path === '/fixture.json') { res.writeHead(200, { 'content-type': 'application/json' }); return res.end(FIXTURE); }
  if (path === '/stub/levels.json') { res.writeHead(200, { 'content-type': 'application/json' }); return res.end(JSON.stringify(LEVELS)); }
  if (path.indexOf('/stub/') === 0) { asked.push(path); res.writeHead(404); return res.end('no'); }
  if (path.indexOf('..') >= 0) { res.writeHead(400); return res.end(); }
  try {
    const body = await readFile(join(WEB, path));
    res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' });
    res.end(body);
  } catch (e) { res.writeHead(404, { 'content-type': 'text/plain' }); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

const prof = mkdtempSync(join(tmpdir(), 'race-popped-'));
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
};
const cdp = (method, params) => new Promise((res) => { const i = ++msgId; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
const json = async (x) => JSON.parse(await ev(`JSON.stringify(${x})`));
const text = async (sel) => ev(`(()=>{const n=document.querySelector(${JSON.stringify(sel)}); return n ? (n.hidden ? '(hidden) ' : '') + n.textContent : null;})()`);
const click = (sel) => ev(`(()=>{const b=document.querySelector(${JSON.stringify(sel)}); if(!b) return 0; b.click(); return 1;})()`);
await cdp('Runtime.enable'); await cdp('Page.enable');

const site = `http://127.0.0.1:${PORT}`;
const CHART = `chart=${encodeURIComponent(`${site}/fixture.json`)}`;

/* ============================================================================
 * 2. the total is known at start
 * ==========================================================================*/
await cdp('Page.navigate', { url: `${site}/dtrh/race.html?autostart=1&intro=0&cards=0&${CHART}` });
let up = false;
for (let i = 0; i < 120 && !up; i++) {
  await sleep(250);
  up = await ev(`!!(window.__race && window.__race.race && window.__race.race.track && window.__race.race.perf().running)`);
}
ok(up, 'the page boots the worded fixture road and the run is going');
if (!up) { if (errs.length) console.error('    said: ' + errs.slice(0, 6).join(' | ')); await done(1); }
{
  const th = await json(`window.__race.race.thoughts()`);
  eq(th.total, TOTAL, 'the run knows the whole total off the chart, before anybody pops anything');
  eq(th.popped, 0, 'and nothing is popped yet');
  eq(await text('.rh-popped'), `popped 0 / ${TOTAL}`, 'the hud says so in the house style');
}

/* ============================================================================
 * 3. the count goes up, and the hud follows
 *
 * The pump is the honest way to make a headless run TAKE the words: nobody is
 * steering, and a word bubble sits in the lane its phrase was given.
 * ==========================================================================*/
const WANT = 20;
let th = { popped: 0, total: TOTAL };
// The words of a spoken file do not start at second zero, so this waits the way
// word-flash-check.mjs waits: a pump every half second until the count climbs.
for (let i = 0; i < 240 && th.popped < WANT; i++) {
  await ev(`window.__race.race.debugPickup('the_pump')`);
  await sleep(500);
  th = await json(`window.__race.race.thoughts()`);
}
ok(th.popped >= WANT, `the run took ${th.popped} word bubbles of ${th.total} without anyone steering`);
eq(th.total, TOTAL, 'and the total never moved under it');
eq(await text('.rh-popped'), `popped ${th.popped} / ${TOTAL}`, 'the hud line is the count, live');

/* ============================================================================
 * 4. a run still going files nothing
 * ==========================================================================*/
eq(await ev(`localStorage.getItem('race.popped')`), null, 'a run in progress has filed no best: the end of the chart is what counts');

/* ============================================================================
 * 5. the end of the chart files it, and the End card carries the row
 * ==========================================================================*/
const RUN = (await json(`window.__race.race.thoughts()`)).popped;
await ev(`window.__race.race.trackEnded()`);
await sleep(3500);                                   // the payout wait, then the card
{
  const bests = await json(`JSON.parse(localStorage.getItem('race.popped') || 'null')`);
  const keys = bests ? Object.keys(bests) : [];
  eq(keys.length, 1, 'the completed run filed one record');
  const rec = bests ? bests[keys[0]] : null;
  eq(keys[0], ROW.hash, 'under the chart\'s own file hash');
  ok(rec && rec.popped === RUN && rec.total === TOTAL, `and it is the run that just happened: ${JSON.stringify(rec && [rec.popped, rec.total])} of ${[RUN, TOTAL]}`);
  const rows = await json(`(()=>{const d=[...document.querySelectorAll('.rh-rows > *')].map(n=>n.textContent);
    const o={}; for(let i=0;i+1<d.length;i+=2) o[d[i]]=d[i+1]; return o;})()`);
  ok(!!rows.thoughts, 'the End card grew a `thoughts` row: ' + JSON.stringify(rows.thoughts || null));
  eq(rows.thoughts, `${RUN.toLocaleString('en-US')} of ${TOTAL.toLocaleString('en-US')}`, 'reading the run against the whole file');
  ok(!!rows.taken, 'and the older `taken` row, which counts a whole line as one thing, is still beside it: ' + rows.taken);
}
const LINE = `popped ${RUN} / ${TOTAL} thoughts`;

/* ============================================================================
 * 6. the menu: the plate and the status line carry it
 * ==========================================================================*/
await cdp('Page.navigate', { url: `${site}/dtrh/race.html?intro=0&cards=0&${CHART}` });
up = false;
for (let i = 0; i < 120 && !up; i++) {
  await sleep(250);
  up = await ev(`!!(window.__race && window.__race.menu) && !!document.querySelector('.rm-track') && document.querySelector('.rm-track').hidden === false`);
}
ok(up, 'the same road comes back to the menu with its plate up');
eq(await text('.rm-track-best'), LINE, 'the track plate carries the best on this file');
eq(await text('.rm-status-track'), `(hidden) track · ${ROW.title} · ${LINE}`,
  'and the `track ·` status line carries it too, standing down only because the plate is up');

/* ============================================================================
 * 7. the levels panel: the row itself, with no chart and no network
 * ==========================================================================*/
await cdp('Page.navigate', { url: `${site}/dtrh/race.html?cloud=1&intro=0&cards=0&levels=${encodeURIComponent('/stub/levels.json')}` });
up = false;
for (let i = 0; i < 120 && !up; i++) {
  await sleep(250);
  up = await ev(`!!(window.__race && window.__race.menu) && !!document.querySelector('.rm-root') && !document.querySelector('.rm-root').hidden`);
}
ok(up, 'the page boots the levels panel with a stub list');
await click('.rm-list .rm-btn[data-id=cloud]');
await sleep(400);
{
  const rows = await json(`[...document.querySelectorAll('.rm-levels .rm-level-btn')].map(b=>({
    id: b.dataset.id, title: b.querySelector('.rm-level-title').textContent,
    best: b.querySelector('.rm-level-best') ? b.querySelector('.rm-level-best').textContent : null,
    shown: b.querySelector('.rm-level-best') ? !b.querySelector('.rm-level-best').hidden : false,
    tall: Math.round(b.getBoundingClientRect().height),
  }))`);
  const one = rows.find((r) => r.id === 'lv-1'), two = rows.find((r) => r.id === 'lv-2');
  ok(one && one.shown && one.best === LINE, `the completed level's own row carries the best: ${JSON.stringify(one && one.best)}`);
  ok(two && !two.shown, 'and the level nobody has finished carries no line at all');
  ok(rows.every((r) => r.tall >= 48), `every row is still at least 48 px tall for a thumb (${rows.map((r) => r.tall).join(', ')})`);
  ok(asked.length === 0, 'and not one byte of a level file was asked for to paint any of it' + (asked.length ? ': ' + asked.join(', ') : ''));
}

await done(fails ? 1 : 0);

async function done(code) {
  try { ws && ws.close(); } catch (e) { /* never opened */ }
  try { chrome.kill(); } catch (e) { /* already gone */ }
  await new Promise((r) => server.close(r));
  await sleep(300);
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* chrome still has a lock */ }
  if (code) console.error(`\n${fails} failure${fails === 1 ? '' : 's'}`);
  else console.log('\npopped-check: all good');
  process.exit(code);
}
