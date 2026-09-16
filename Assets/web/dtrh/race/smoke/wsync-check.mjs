/* ============================================================================
 * race/smoke/wsync-check.mjs - THE SYNC WIN: the pop log, the offset, the overlay.
 *
 *   node race/smoke/wsync-check.mjs      (from Resources/web/dtrh; 0 on pass, 1 with a count)
 *   RACE_SHOTS=1 node race/smoke/wsync-check.mjs      (also writes shots/wsync.png, untracked)
 *
 * The node half holds race/wordSync.js on its own: the keys collide with nothing, the shift
 * moves every word-derived second and keeps the ids, the index row's offsetSec is read, the
 * ring never passes LOG_MAX. The browser half drives the Rapid Induction road on a 390x844
 * phone: every pop logs inside 0.06 s of its word, a miss logs `missed: true`, three presses
 * of `]` move the next un-spawned word by 0.15 s and the panel says so, `\` exports the row,
 * and with `?wsync=1` the panel sits inside the glass and on nothing else. Counted, never
 * printed: no word of the transcript is read out.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { readFileSync, writeFileSync, mkdirSync, mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { wordedRoad, PEAKS_PER_SEC } from '../cloudChart.js';
import { normalizeChart } from '../chart.js';
import { KART_X_MAX } from '../consts.js';
import { loadWords, loadWordsRow, forgetWords } from '../words.js';
import { shiftRoad, createPopLog, statsOf, exportOf, LOG_MAX, LOG_KEY, WORD_KINDS } from '../wordSync.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const HERE = resolve(fileURLToPath(import.meta.url), '..');
const RACE = resolve(HERE, '..');
const WEB = resolve(RACE, '../..');
const SHOTS = resolve(WEB, '../../../shots');
const read = (rel) => JSON.parse(readFileSync(resolve(RACE, rel), 'utf8'));
const src = (rel) => readFileSync(resolve(RACE, rel), 'utf8');

/* ---- 1. the keys are free ---- */
const KEYS = /BracketLeft|BracketRight|Backslash/g;
eq((src('input.js').match(KEYS) || []).length, 3, '`[` `]` `\\` are wired once each in input.js ACTIONS');
eq(((src('menu.js') + src('cards.js') + src('intro.js')).match(KEYS) || []).length, 0, 'and no menu, card or intro key reads them');

/* ---- 2. the shift: every word-derived second moves, ids stay ---- */
const rows = read('words/index.json').rows;
const ROW = rows.find((r) => /rapid induction/i.test(String(r.title || ''))) || rows[0];
// the aligner stamp lives on the index row, not on the race copy of the transcript, and
// race/words.js loadWords() hands it to the road: the fixture is read the same way here.
const WORDS = { ...read('words/' + ROW.file), engine: ROW.engine };
function swell(durationSec, perSec = PEAKS_PER_SEC) {
  const n = Math.ceil(durationSec * perSec), peaks = new Float32Array(n * 2);
  for (let i = 0; i < n; i++) { const a = 0.22 + 0.5 * Math.max(0, Math.sin(((i / perSec) / 40) * Math.PI * 2)) ** 2; peaks[i * 2] = -a; peaks[i * 2 + 1] = a; }
  return peaks;
}
const road = wordedRoad({ peaks: swell(WORDS.durationSec), durationSec: WORDS.durationSec, name: ROW.title, hash: ROW.hash, words: WORDS });
road.source.cloudId = ROW.cloudId;
const base = normalizeChart(road), moved = normalizeChart(shiftRoad(base, 0.15, { trackId: ROW.cloudId }));
const byId = new Map(moved.events.map((e) => [e.id, e]));
const off = (e) => Math.round((byId.get(e.id).t - e.t) * 1000) / 1000;
const edge = (e) => byId.get(e.id).t === 0 || byId.get(e.id).t === base.source.durationSec;   // clamped at the file's ends
ok(base.events.every((e) => byId.has(e.id)), 'every event keeps its id through the shift');
ok(base.events.filter((e) => WORD_KINDS.includes(e.kind)).every((e) => off(e) === 0.15 || edge(e)), 'every word, row, count, drop and chant moved by +0.15 s');
ok(base.events.filter((e) => !WORD_KINDS.includes(e.kind)).every((e) => off(e) === 0), 'and not one energy event did');
ok(base.words.every((w, i) => Math.abs(moved.words[i].t - w.t - 0.15) < 1e-6), 'the caption track moved with them');
eq(moved.analysis.offsetSec, 0.15, 'analysis.offsetSec says what was applied');
eq(moved.source.cloudId, ROW.cloudId, 'and source.cloudId survives normalizeChart');
const back = normalizeChart(shiftRoad(moved, -0.15));
ok(base.events.every((e, i) => back.events[i].id === e.id && (edge(e) || Math.abs(back.events[i].t - e.t) < 1e-6)) && back.analysis.offsetSec === 0, 'shifting back lands on the original road');

/* ---- 3. words.js reads offsetSec off the row ---- */
const table = { version: 1, rows: [{ cloudId: 'aaaa', hash: 'h1', title: 'a', file: 'a.json', offsetSec: 0.12 }, { cloudId: 'bbbb', hash: 'h2', title: 'b', file: 'b.json' }] };
const fakeFetch = async (url) => ({ ok: true, status: 200, json: async () => (/index\.json$/.test(url) ? table : { version: 1, hash: 'h', durationSec: 5, words: [{ t: 1, d: 0.3, w: 'ok' }] }) });
const IX = 'http://x/words/index.json';
forgetWords();
eq((await loadWordsRow({ cloudId: 'AAAA', fetch: fakeFetch, indexUrl: IX })).offsetSec, 0.12, 'loadWordsRow hands back the row\'s offsetSec');
eq((await loadWordsRow({ hash: 'h2', fetch: fakeFetch, indexUrl: IX })).offsetSec, 0, 'and 0 for a row without one');
eq((await loadWords({ cloudId: 'aaaa', fetch: fakeFetch, indexUrl: IX })).offsetSec, 0.12, 'loadWords carries it too');
eq(await loadWordsRow({ cloudId: 'zzzz', fetch: fakeFetch, indexUrl: IX }), null, 'and a track with no row is null');

/* ---- 4. the ring, the stats, the export ---- */
const mem = new Map(), storage = { getItem: (k) => (mem.has(k) ? mem.get(k) : null), setItem: (k, v) => mem.set(k, v), removeItem: (k) => mem.delete(k) };
const log = createPopLog({ storage });
for (let i = 0; i < 600; i++) log.push({ trackId: 't', i, dt: (i % 7) * 0.01 - 0.03, missed: i % 5 === 0 });
eq(log.size, LOG_MAX, `600 pushes leave ${LOG_MAX} rows in the ring`);
eq(JSON.parse(mem.get(LOG_KEY)).length, LOG_MAX, 'and localStorage holds exactly those');
eq(JSON.parse(mem.get(LOG_KEY))[0].i, 600 - LOG_MAX, 'the oldest fell off, the newest stayed');
const angry = createPopLog({ storage: { getItem: () => { throw new Error('no'); }, setItem: () => { throw new Error('no'); } } });
angry.push({ dt: 0 });
eq(angry.size, 1, 'a storage that throws is a ring in memory, never an exception');
const st = statsOf([{ dt: 0.01 }, { dt: 0.03 }, { dt: -0.02 }, { dt: 0.9 }, { dt: 0.02, missed: true }, { dt: 2 }]);
eq(st.n, 5, 'the stats count pops, never misses'); eq(st.median, 0.03, 'median'); eq(st.p90, 2, 'p90');
eq(st.hist.length, 21, '21 bins'); eq(st.hist[10], 3, 'three of them on the word'); eq(st.hist[19], 1, 'and one out at +0.9 s');
eq(Object.keys(exportOf({ cloudId: 'c', offsetSec: 0.05, rows: [] })).join(','), 'cloudId,offsetSec,n,medianDt,p90Dt', 'the export is the index row\'s shape');

/* ---- the browser ---- */
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 8875, DEBUG_PORT = 9345;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };
const FIXTURE = JSON.stringify(road);
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME + ' (set CHROME_PATH)'); process.exit(1); }
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path === '/fixture.json') { res.writeHead(200, { 'content-type': 'application/json' }); return res.end(FIXTURE); }
  if (path.indexOf('..') >= 0) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(req.method === 'HEAD' ? undefined : body); }
  catch (e) { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));
const prof = mkdtempSync(join(tmpdir(), 'race-wsync-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`, '--no-first-run', '--no-default-browser-check', '--disable-gpu', '--mute-audio', '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required', '--window-size=390,844', 'about:blank'], { stdio: 'ignore' });
async function done(code) {
  try { chrome.kill(); } catch (e) { /* gone */ }
  await new Promise((r) => server.close(r));
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* windows holds the profile */ }
  console.log(code ? `\nwsync-check: ${fails} failed` : '\nwsync-check: all good');
  process.exit(code);
}
let page = null;
for (let i = 0; i < 60 && !page; i++) { await sleep(250); try { page = (await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/list`)).json()).find((t) => t.type === 'page'); } catch (e) { /* not up */ } }
if (!page) { console.error('FAIL chrome never answered'); await done(1); }
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
const firstWord = road.events.find((e) => e.kind === 'word').t;
// THE DRIVE: the fixture lays each phrase in a lane of its own (random per build) and a phrase
// steps one lane at a time. Take the first lane step with a word inside 2.5 s of it, and pin the
// kart (`?x=`, race/kart.js's screenshot aid) one lane BEYOND the new lane: the new lane sits
// 0.8 m off, inside the 1.15 m pop box, the old lane 1.6 m off, outside it. One drive logs both.
const wordsOn = road.events.filter((e) => e.kind === 'word');
const turn = wordsOn.findIndex((e, i) => i > 2 && e.x !== wordsOn[i - 1].x && e.t - wordsOn[i - 1].t < 2.5 && Math.abs(e.x + 0.8 * Math.sign(e.x - wordsOn[i - 1].x)) <= KART_X_MAX);
const laneA = wordsOn[turn - 1].x, laneB = wordsOn[turn].x, pinX = laneB + 0.8 * Math.sign(laneB - laneA), seekT = wordsOn[turn].t - 6;
const PIN = (pinX / KART_X_MAX).toFixed(3);
const counted = (r) => r.t >= seekT + 3;   // a word inside the scheduler's lead at the seek had no run-up: not the sync's fault
const URL0 = `${site}/dtrh/race.html?autostart=1&intro=0&cards=0&x=${PIN}&chart=${encodeURIComponent(`${site}/fixture.json`)}`;
async function boot(url) {
  await cdp('Page.navigate', { url });
  for (let i = 0; i < 120; i++) { await sleep(250); if (await ev(`!!(window.__race && window.__race.race && window.__race.race.track && document.querySelector('.rc-layer'))`)) return true; }
  return false;
}
const key = (code, shift = false) => ev(`window.dispatchEvent(new KeyboardEvent('keydown', { code: '${code}', shiftKey: ${shift}, bubbles: true })), 1`);
const rect = `(el)=>{ if(!el) return null; const b=el.getBoundingClientRect(); return { x:b.left, y:b.top, w:b.width, h:b.height }; }`;
const hits = (a, b) => !!a && !!b && a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;

/* ---- 5. the log, off a real road being driven; no panel without ?wsync ---- */
ok(await boot(URL0), 'the page boots the Rapid Induction road');
eq(await ev(`document.querySelectorAll('.rw-sync').length`), 0, 'without ?wsync=1 no overlay is built');
await ev(`window.__race.race.trackClock(${seekT.toFixed(2)}, true)`);
await sleep(12000);
const got = await json(`({ rows: window.__race.race.wordSync.rows(), size: window.__race.race.wordSync.size(), stored: JSON.parse(localStorage.getItem('${LOG_KEY}')||'[]').length, t: window.__race.race.track.t })`);
const pops = got.rows.filter((r) => !r.missed && counted(r)), misses = got.rows.filter((r) => r.missed && counted(r));

ok(turn > 0 && got.t > seekT + 10, `the road rolled from ${seekT.toFixed(1)} s across the lane step at ${wordsOn[turn].t.toFixed(1)} s (x ${laneA} to ${laneB}, kart pinned on ${pinX.toFixed(1)}; to ${got.t.toFixed(1)} s)`);
ok(pops.length > 0, `${pops.length} pops wrote a log row`);
ok(pops.every((r) => Math.abs(r.dt) <= 0.06), `and every one landed inside 0.06 s of its word (worst ${Math.max(...pops.map((r) => Math.abs(r.dt))).toFixed(3)})`);
ok(misses.length > 0 && misses.every((r) => r.missed === true), `${misses.length} words driven past wrote missed: true`);
ok(got.rows.every((r) => r.trackId === ROW.cloudId && ['i', 'p', 'w', 't', 'popT', 'dt', 'lane', 'missed'].every((k) => k in r)), 'every row carries trackId, i, p, w, t, popT, dt, lane, missed');
eq(got.stored, got.size, 'and the ring is written through to localStorage on every pop');
ok(got.size <= LOG_MAX, `the ring is at ${got.size}, never over ${LOG_MAX}`);

/* ---- 6. the nudge moves the next un-spawned word, and says so ---- */
const next = await json(`(()=>{ const r=window.__race.race, t=r.track.t; const e=r.track.chart.events.find((e)=>e.kind==='word'&&e.t>t+4); return { id:e.id, t:e.t, n:r.track.chart.events.length }; })()`);
await key('BracketRight'); await key('BracketRight'); await key('BracketRight');
await sleep(150);
const after = await json(`(()=>{ const r=window.__race.race, e=r.track.chart.events.find((e)=>e.id==='${next.id}'); const p=document.querySelector('.rw-sync');
  return { t:e.t, n:r.track.chart.events.length, offset:r.wordSync.offset(), stored:localStorage.getItem('rt-word-offset:${ROW.cloudId}'), panel:!!p, shown:!!p&&!p.hidden, reads:p?p.querySelector('.rw-sync-off').textContent:'', btns:document.querySelectorAll('.rw-sync-btn').length }; })()`);
ok(Math.abs(after.t - next.t - 0.15) < 1e-6, `three presses of ] moved the next un-spawned word by +0.15 s (${next.t} -> ${after.t})`);
eq(after.n, next.n, 'with the same events on the road, none doubled and none lost');
eq(after.offset, 0.15, 'the track reads its offset as +0.15');
eq(after.stored, '0.15', 'and localStorage rt-word-offset:<cloudId> holds it');
ok(after.panel && after.shown, 'the panel showed itself on the nudge');
eq(after.reads, '+0.15 s', 'reading "+0.15 s"');
eq(after.btns, 0, 'with no buttons on it: those are ?wsync=1 only');
await sleep(2300);
eq(await ev(`document.querySelector('.rw-sync').hidden`), true, 'and it put itself away after 2 s');
await key('BracketLeft', true);
eq(await ev(`window.__race.race.wordSync.offset()`), -0.1, 'shift+[ takes 0.25 s off');

/* ---- 7. the export ---- */
const exp = JSON.parse(await ev(`window.__race.race.wordSync.exportJson()`));
eq(Object.keys(exp).join(','), 'cloudId,offsetSec,n,medianDt,p90Dt', 'the export is {cloudId, offsetSec, n, medianDt, p90Dt}');
eq(exp.cloudId, ROW.cloudId, 'for this track'); eq(exp.offsetSec, -0.1, 'at the offset it has now');
await key('Backslash'); await sleep(150);
ok(await ev(`[...document.querySelectorAll('.rh-toast')].some((t)=>/copied/.test(t.textContent))`), 'and \\ says "offset copied" on the rail');

/* ---- 8. ?wsync=1: the panel is on the glass and on nothing else ---- */
ok(await boot(URL0 + '&wsync=1'), 'the page boots again with ?wsync=1');
await ev(`window.__race.race.debugWord('ok'); window.__race.race.hud.toast('+10', 'pop')`);
await sleep(250);
const lay = await json(`(()=>{ const r=${rect}; const q=(s)=>r(document.querySelector(s)); const p=document.querySelector('.rw-sync');
  return { panel:q('.rw-sync'), shown:!!p&&!p.hidden, btns:document.querySelectorAll('.rw-sync-btn').length, score:q('.rh-score-wrap'), cap:q('.rc-cap'), rail:q('.rh-toasts'), speed:q('.rh-speed'), chips:q('.rh-passive'),
    plateY:parseFloat(getComputedStyle(document.querySelector('.rc-layer')).getPropertyValue('--rc-plate-y'))||0, serif:/serif/.test(getComputedStyle(p).fontFamily.replace(/sans-serif/g,'')), tnum:getComputedStyle(p.querySelector('.rw-sync-row')).fontVariantNumeric, vw:innerWidth, vh:innerHeight }; })()`);
ok(lay.shown && lay.btns === 3, 'the panel is up from the start, with - + and export on it');
ok(lay.panel.x >= 0 && lay.panel.y >= 0 && lay.panel.x + lay.panel.w <= lay.vw && lay.panel.y + lay.panel.h <= lay.vh, `inside the 390x844 glass (${Math.round(lay.panel.y)}..${Math.round(lay.panel.y + lay.panel.h)})`);
ok(!hits(lay.panel, lay.score), 'clear of the score plate'); ok(!hits(lay.panel, lay.cap), 'clear of the band');
ok(!hits(lay.panel, lay.rail), 'clear of the toast rail'); ok(!hits(lay.panel, lay.speed), 'clear of the speed box'); ok(!hits(lay.panel, lay.chips), 'clear of the pickup chips');
ok(lay.plateY < lay.panel.y || lay.plateY > lay.panel.y + lay.panel.h, `and the plate's rest spot (y ${Math.round(lay.plateY)}) is not under it`);
ok(!lay.serif && lay.tnum === 'tabular-nums', 'HUD sans, tabular digits, no serif');
await ev(`document.querySelectorAll('.rw-sync-btn')[1].click()`);
eq(await ev(`window.__race.race.wordSync.offset()`), 0.05, 'the + button is a 50 ms nudge');
if (process.env.RACE_SHOTS) {
  await ev(`window.__race.race.trackClock(${(firstWord - 2).toFixed(2)}, true)`);
  await sleep(6000);
  mkdirSync(SHOTS, { recursive: true });
  const shot = await cdp('Page.captureScreenshot', { format: 'png' });
  if (shot.result?.data) { writeFileSync(join(SHOTS, 'wsync.png'), Buffer.from(shot.result.data, 'base64')); console.log('  --  shot: ' + join(SHOTS, 'wsync.png')); }
}
eq(errs.length, 0, 'nothing on the console' + (errs.length ? ': ' + errs.slice(0, 4).join(' | ') : ''));
await done(fails ? 1 : 0);
