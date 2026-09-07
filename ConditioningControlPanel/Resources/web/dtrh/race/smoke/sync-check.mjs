/* ============================================================================
 * race/smoke/sync-check.mjs - the plate and the row land on the spoken word.
 *
 *   node race/smoke/sync-check.mjs     (from Resources/web/dtrh; 0 on pass, 1 with a count)
 *   RACE_SYNC_DUR=90 RACE_SYNC_REPORT=1 ...   a longer demo, and the numbers only
 *
 * Headless Chrome at a PHONE viewport (390x844) drives the demo track (`?chart=demo`,
 * the wall is the clock) for one whole run and measures, off the page itself:
 *
 *   plate    the track second a `.rc-plate` node reached the DOM, by a MutationObserver
 *   taken    the track second `trackStats().taken` went up, polled every frame, which
 *            is the pop of the row's own bubble: the row was under the kart's nose
 *   trace    `race.syncTrace()`, the standalone-only log race/sync.js keeps: when each
 *            event was handed over by the scheduler, when its visible half fired,
 *            when its row went down and when the kart reached it
 *
 * against `event.t` off the chart. The owner heard the effect ~2 s before the
 * word; the scheduler's lookahead is 2.5 s. This is where that number is held down:
 * the plate inside PLATE_TOL of the word, the row under the kart inside ROW_TOL,
 * through a build (the boost ramps the speed mid-lookahead) and a drop (a jump).
 *
 * Nothing leaves this machine: the web folder is served off localhost and the demo
 * road has no audio at all. It never prints a line of a transcript.
 *
 * CHROME: `CHROME_PATH` if it is set, else the usual Windows install.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { demoChart } from '../chart.js';

let fails = 0;
const REPORT = process.env.RACE_SYNC_REPORT === '1';   // the numbers only: nothing fails (the "before" run)
const ok = (cond, what) => { if (!cond && !REPORT) { console.error('FAIL ' + what); fails++; } else console.log((cond ? '  ok  ' : '  --  ') + what); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const f2 = (v) => (v == null ? '-' : (v >= 0 ? '+' : '') + v.toFixed(2));

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const WEB = resolve(HERE, '../../..');                     // Resources/web
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 8871;
const DUR = Math.max(40, Number(process.env.RACE_SYNC_DUR) || 60);
/** The plate may be one frame late (it is fired from the frame loop) and never early. */
const PLATE_TOL = 0.15;
/** The row: the brief's own number. The pop box reads 1.4 m ahead, 0.06 s at base speed. */
const ROW_TOL = 0.15;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };

const chart = demoChart({ durationSec: DUR });
const triggers = chart.events.filter((e) => e.kind === 'trigger' && e.conf >= 0.55);
const builds = chart.events.filter((e) => e.kind === 'build');
console.log(`the demo: ${DUR}s, ${chart.events.length} events, ${triggers.length} sure triggers at ${triggers.map((e) => e.t.toFixed(1)).join(', ')}s, a build at ${builds.map((e) => e.t.toFixed(1)).join(', ')}s`);

if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME + ' (set CHROME_PATH)'); process.exit(1); }
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.indexOf('..') >= 0) { res.writeHead(400); return res.end(); }
  try {
    const body = await readFile(join(WEB, path));
    res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' });
    res.end(req.method === 'HEAD' ? undefined : body);
  } catch (e) { res.writeHead(404, { 'content-type': 'text/plain' }); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

const prof = mkdtempSync(join(tmpdir(), 'race-sync-'));
const chrome = spawn(CHROME, [
  '--headless=new', '--remote-debugging-port=9341', `--user-data-dir=${prof}`,
  '--no-first-run', '--no-default-browser-check', '--disable-gpu', '--mute-audio',
  '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required',
  '--window-size=390,844', 'about:blank',
], { stdio: 'ignore' });

let page = null;
for (let i = 0; i < 60 && !page; i++) {
  await sleep(250);
  try { page = (await (await fetch('http://127.0.0.1:9341/json/list')).json()).find((t) => t.type === 'page'); } catch (e) { /* not up */ }
}
if (!page) { console.error('FAIL chrome never answered on the debug port'); await done(1); }

const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r) => { ws.onopen = r; });
let msgId = 0;
const waits = new Map(), errs = [], logs = [];
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Runtime.exceptionThrown') errs.push('thrown: ' + (m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text || '?'));
  if (m.method === 'Runtime.consoleAPICalled') {
    const line = m.params.args.map((a) => a.value ?? a.description ?? '?').join(' ');
    logs.push(line);
    if (m.params.type === 'error') errs.push(line);
  }
};
const cdp = (method, params) => new Promise((res) => { const i = ++msgId; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
const json = async (x) => JSON.parse(await ev(`JSON.stringify(${x})`));
await cdp('Runtime.enable'); await cdp('Page.enable');
await cdp('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });

/* ============================================================================
 * 1. the run rolls, with the probe on it
 * ==========================================================================*/
const site = `http://127.0.0.1:${PORT}`;
await cdp('Page.navigate', { url: `${site}/dtrh/race.html?autostart=1&intro=0&cards=0&chart=demo&dur=${DUR}` });
let up = false;
for (let i = 0; i < 120 && !up; i++) {
  await sleep(250);
  up = await ev(`!!(window.__race && window.__race.race && window.__race.race.track && document.querySelector('.rc-layer'))`);
}
ok(up, 'the page boots the demo road at 390x844');
if (!up) { if (errs.length) console.error('    said: ' + errs.slice(0, 6).join(' | ')); await done(1); }

// THE PROBE. Two things the page cannot lie about: the frame a plate node entered the DOM and
// the frame the scheduler's `taken` count went up (one credit per event, so one line per row).
await ev(`(()=>{
  const race = window.__race.race;
  const S = window.__sync = { plates: [], taken: [] };
  new MutationObserver((muts) => { for (const m of muts) for (const n of m.addedNodes) {
    if (n && n.classList && n.classList.contains('rc-plate')) S.plates.push({ t: race.track.t, word: n.textContent.trim() });
  } }).observe(document.body, { childList: true, subtree: true });
  let last = race.trackStats().taken;
  (function poll() { const s = race.trackStats(); if (s.taken > last) { last = s.taken; S.taken.push({ t: race.track.t, n: s.taken }); } requestAnimationFrame(poll); })();
  return 1;
})()`);

let t = 0, ended = false;
for (let i = 0; i < (DUR + 30) * 4 && !ended; i++) {
  await sleep(250);
  const st = await json(`(()=>{ const tr = window.__race.race.track; return { t: tr ? tr.t : -1, ended: !tr || tr.t >= ${DUR} - 0.3 }; })()`);
  t = st.t; ended = st.ended;
}
ok(ended, `the run drove the whole demo (clock at ${t.toFixed(1)}s of ${DUR}s)`);

/* ============================================================================
 * 2. the numbers
 * ==========================================================================*/
const probe = await json('window.__sync');
const trace = await json(`(window.__race.race.syncTrace ? window.__race.race.syncTrace() : [])`);
const byId = new Map(trace.map((r) => [r.id, r]));
console.log(`  --  ${probe.plates.length} plates seen, ${probe.taken.length} events taken, ${trace.length} trace lines`);

const nearest = (list, t0, win) => { let best = null; for (const x of list) { const d = x.t - t0; if (Math.abs(d) <= win && (!best || Math.abs(d) < Math.abs(best.t - t0))) best = x; } return best; };
console.log('  event         t      plate    row(pop)  handed   fired    rowAt');
const plateDs = [], rowDs = [], handDs = [];
for (const e of triggers) {
  const p = nearest(probe.plates.filter((x) => x.word === e.label), e.t, 3.5);
  const k = nearest(probe.taken, e.t, 3.5);
  const tr = byId.get(e.id) || {};
  const pd = p ? p.t - e.t : null, kd = k ? k.t - e.t : null;
  if (pd != null) plateDs.push(pd);
  if (kd != null) rowDs.push(kd);
  if (tr.handedAt != null) handDs.push(tr.handedAt - e.t);
  console.log(`  ${e.label.padEnd(12)} ${e.t.toFixed(2).padStart(6)}  ${f2(pd).padStart(7)}  ${f2(kd).padStart(8)}  ${f2(tr.handedAt == null ? null : tr.handedAt - e.t).padStart(7)}  ${f2(tr.firedAt == null ? null : tr.firedAt - e.t).padStart(7)}  ${f2(tr.rowAt == null ? null : tr.rowAt - e.t).padStart(7)}`);
}
const stat = (a) => (a.length ? `median ${f2(a.slice().sort((x, y) => x - y)[a.length >> 1])} worst ${f2(a.reduce((m, v) => (Math.abs(v) > Math.abs(m) ? v : m), 0))}` : 'none');
console.log(`  --  plate - t: ${stat(plateDs)};  row pop - t: ${stat(rowDs)};  handover - t: ${stat(handDs)}`);

ok(plateDs.length === triggers.length, `every sure trigger flew a plate (${plateDs.length} of ${triggers.length})`);
ok(plateDs.every((d) => d >= -0.02 && d <= PLATE_TOL), `every plate reached the DOM ON its word, never early, at most a frame late (within ${PLATE_TOL}s)`);
ok(rowDs.length === triggers.length, `every row was met by the kart (${rowDs.length} of ${triggers.length})`);
ok(rowDs.every((d) => Math.abs(d) <= ROW_TOL), `every row was under the kart within ${ROW_TOL}s of its word, through the build's boost`);
ok(trace.length > 0, 'the standalone sync trace is on (race.syncTrace)');
ok(trace.filter((r) => r.kind === 'trigger').every((r) => r.firedAt != null && !r.dropped), 'and no trigger was dropped on the way');

/* ============================================================================
 * 3. nothing went wrong out loud
 * ==========================================================================*/
ok(errs.length === 0, 'not one console error in the whole run' + (errs.length ? ':\n    ' + errs.slice(0, 5).join('\n    ') : ''));

await done(fails ? 1 : 0);

async function done(code) {
  try { ws && ws.close(); } catch (e) { /* never opened */ }
  try { chrome.kill(); } catch (e) { /* already gone */ }
  await new Promise((r) => server.close(r));
  await sleep(300);
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* chrome still has a lock */ }
  if (code) console.error(`\n${fails} failure${fails === 1 ? '' : 's'}`);
  else console.log('\nsync-check: all good');
  process.exit(code);
}
