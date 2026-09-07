/* ============================================================================
 * race/smoke/real-audio-check.mjs - the same road, off a real compressed file.
 *
 *   node race/smoke/real-audio-check.mjs            (0 on pass, 1 with a count)
 *   RACE_REAL_URL=https://... node race/smoke/real-audio-check.mjs
 *
 * cloud-chart-check.mjs charts a WAV this machine wrote a second earlier. That
 * proves the maths and proves nothing about mp3: a real file is compressed, its
 * first megabyte is not its first seconds, decodeAudioData has to do real work,
 * and the length the container claims and the length the decoder finds are two
 * different numbers. This holds that down.
 *
 * BY DEFAULT IT TOUCHES NOTHING OFF THIS MACHINE. The track is a real mp3 already
 * in the web folder, served back by a handler that behaves the way a public cdn
 * behaves: `Access-Control-Allow-Origin: *`, `Accept-Ranges: bytes`, a 206 with a
 * `Content-Range` for a ranged GET, and `Content-Range` exposed to the page. That
 * is the contract chartSource.js hashUrl() is written against, and section 5
 * fails if a single request left localhost.
 *
 * RACE_REAL_URL points it at a real cdn instead. That is the only way this file
 * ever reaches the network, it is off by default, and it is the one thing the
 * localhost stub cannot answer: whether a real cdn allows a cross origin HEAD,
 * answers the `Range` preflight, and exposes `Content-Range`. With it set,
 * section 0 prints the cdn's own headers and section 5 is skipped.
 *
 * What it records either way: the hash and which door it came through, how long
 * the decode took, the bins, events and acts on the road, every console error,
 * and whether the run actually rolled.
 *
 * CHROME: `CHROME_PATH` if it is set, else the usual Windows install.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { mkdtempSync, rmSync, existsSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const WEB = resolve(HERE, '../../..');                     // Resources/web
const TRACK_FILE = resolve(WEB, 'dtrh/assets/audio/drone1.mp3');
const PORT = 8865;
const REAL = process.env.RACE_REAL_URL || '';
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };

if (!existsSync(TRACK_FILE)) { console.error('FAIL no mp3 at ' + TRACK_FILE); process.exit(1); }
const TRACK = readFileSync(TRACK_FILE);
console.log(`the file: ${TRACK_FILE.slice(WEB.length + 1)}, ${TRACK.length} bytes (${(TRACK.length / 1048576).toFixed(2)} MiB)`);

/* ============================================================================
 * 0. the cdn's own headers, only when a real url was handed in
 * ==========================================================================*/
const SHOW = ['access-control-allow-origin', 'access-control-allow-headers', 'access-control-expose-headers', 'accept-ranges', 'content-range', 'content-length', 'content-type'];
if (REAL) {
  console.log('\nRACE_REAL_URL is set, so this run reaches a real cdn: ' + REAL);
  for (const [what, init] of [['HEAD', { method: 'HEAD' }], ['ranged GET', { method: 'GET', headers: { Range: 'bytes=0-1048575' } }]]) {
    try {
      const res = await fetch(REAL, { ...init, headers: { Origin: 'https://app.cclabs.app', ...(init.headers || {}) } });
      console.log(`  ${what} -> ${res.status}`);
      for (const h of SHOW) { const v = res.headers.get(h); if (v) console.log(`    ${h}: ${v}`); }
      if (what === 'ranged GET') ok(res.status === 206, 'the cdn answers a ranged GET with a 206, so the hash costs a megabyte and not a file');
      else ok(res.ok, 'the cdn answers a cross origin HEAD');
    } catch (e) { ok(false, `the cdn would not answer a ${what}: ${(e && e.message) || e}`); }
  }
}

/* ============================================================================
 * the server: the web folder, plus one path that behaves like a cdn
 * ==========================================================================*/
const asks = [];                                 // { method, path, ranged, status }
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  const ranged = !!req.headers.range;
  const cdn = () => {
    res.setHeader('access-control-allow-origin', '*');
    res.setHeader('access-control-allow-headers', 'Range');
    res.setHeader('access-control-expose-headers', 'Content-Range, Content-Length, Accept-Ranges');
    res.setHeader('accept-ranges', 'bytes');
  };
  if (path === '/stub/real.mp3') {
    asks.push({ method: req.method, path, ranged });
    cdn();
    if (req.method === 'OPTIONS') { res.writeHead(204, { 'content-length': 0 }); return res.end(); }
    const m = /^bytes=(\d+)-(\d*)$/.exec(req.headers.range || '');
    if (m) {
      const a = Number(m[1]), b = Math.min(TRACK.length - 1, m[2] ? Number(m[2]) : TRACK.length - 1);
      const cut = TRACK.subarray(a, b + 1);
      res.writeHead(206, { 'content-type': 'audio/mpeg', 'content-length': cut.length, 'content-range': `bytes ${a}-${b}/${TRACK.length}` });
      return res.end(req.method === 'HEAD' ? undefined : cut);
    }
    res.writeHead(200, { 'content-type': 'audio/mpeg', 'content-length': TRACK.length });
    return res.end(req.method === 'HEAD' ? undefined : TRACK);
  }
  asks.push({ method: req.method, path, ranged });
  if (path.indexOf('..') >= 0) { res.writeHead(400); return res.end(); }
  try {
    const body = await readFile(join(WEB, path));
    res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' });
    res.end(req.method === 'HEAD' ? undefined : body);
  } catch (e) { res.writeHead(404, { 'content-type': 'text/plain' }); res.end('no'); }
});

if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME + ' (set CHROME_PATH)'); process.exit(1); }
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

const prof = mkdtempSync(join(tmpdir(), 'race-real-'));
const chrome = spawn(CHROME, [
  '--headless=new', '--remote-debugging-port=9337', `--user-data-dir=${prof}`,
  '--no-first-run', '--no-default-browser-check', '--disable-gpu', '--mute-audio',
  '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required',
  '--window-size=1280,800', 'about:blank',
], { stdio: 'ignore' });

let page = null;
for (let i = 0; i < 60 && !page; i++) {
  await sleep(250);
  try { page = (await (await fetch('http://127.0.0.1:9337/json/list')).json()).find((t) => t.type === 'page'); } catch (e) { /* not up */ }
}
if (!page) { console.error('FAIL chrome never answered on the debug port'); await done(1); }

const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r) => { ws.onopen = r; });
let msgId = 0;
const waits = new Map(), logs = [], errs = [], net = [];
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Network.requestWillBeSent') net.push(m.params.request.url);
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
const click = (sel) => ev(`(()=>{const b=document.querySelector(${JSON.stringify(sel)}); if(!b) return 0; b.click(); return 1;})()`);
const heads = (p) => asks.filter((a) => a.method === 'HEAD' && a.path === p).length;

await cdp('Runtime.enable'); await cdp('Page.enable'); await cdp('Network.enable');

const site = `http://127.0.0.1:${PORT}`;
const URL_UNDER_TEST = REAL || `${site}/stub/real.mp3`;

/* ============================================================================
 * 1. a real mp3 is decoded and charted in the browser
 * ==========================================================================*/
await cdp('Page.navigate', { url: `${site}/dtrh/race.html?cloud=1&intro=0&cards=0` });
let up = false;
for (let i = 0; i < 80 && !up; i++) {
  await sleep(250);
  up = await ev(`!!(window.__race && window.__race.menu) && !!document.querySelector('.rm-root') && !document.querySelector('.rm-root').hidden`);
}
ok(up, 'the page boots to the menu with ?cloud=1');
await click('.rm-list .rm-btn[data-id=cloud]');
await sleep(400);
// lane W4: the paste box lives in a drawer under the levels list
await click('.rm-levels-btn[data-id=paste]');
await sleep(300);
await click('.rm-cloud .rm-cloud-btn[data-id=forget]');
await sleep(300);
await ev(`(()=>{const i=document.querySelector('.rm-cloud-in'); i.value=${JSON.stringify(URL_UNDER_TEST)}; return 1;})()`);

const t0 = Date.now();
await click('.rm-cloud .rm-cloud-btn[data-id=add]');
let charted = false;
for (let i = 0; i < 240 && !charted; i++) {
  await sleep(250);
  charted = await ev(`(()=>{const t=window.__race.race.track; return !!(t && t.chart && !t.chart.analysis.partial);})()`);
}
const took = Date.now() - t0;
ok(charted, `a real mp3 was decoded and charted in the browser (${(took / 1000).toFixed(1)}s from paste to full chart)`);
if (!charted) await done(1);

const c = await json(`(()=>{const c=window.__race.race.track.chart; const k={}; for(const e of c.events) k[e.kind]=(k[e.kind]||0)+1;
  return { hand:c.hand, bins:c.energy.length, binSec:c.binSec, n:c.events.length, kinds:k, acts:c.acts.map(a=>a.kind),
           words:c.analysis.words, dur:c.source.durationSec, hash:c.source.hash, name:c.source.name };})()`);
console.log('\n  chart: ' + JSON.stringify(c) + '\n');
ok(/^[0-9a-f]{40}$/.test(c.hash), 'the file is stamped with a CHART.md hash: ' + c.hash);
ok(c.bins > 100, `the energy curve is real, not a stub (${c.bins} bins of ${c.binSec}s)`);
ok(c.n > 0, `the road has events on it (${c.n}: ${JSON.stringify(c.kinds)})`);
ok(c.acts.length >= 1, `the file is at least one room (${c.acts.join(' -> ')})`);
ok(c.dur > 30, `the road is the length of the file (${c.dur}s), not the length of the megabyte the hash read`);
ok(c.hand === false, 'and it is generated, so it does not claim to be authored');

/* ============================================================================
 * 2. the hash cost a megabyte, not a file
 * ==========================================================================*/
const timed = logs.filter((l) => /generated in|cached in|authored by/.test(l));
console.log('  the door it came through: ' + (timed[timed.length - 1] || '(nothing said)'));
if (!REAL) {
  ok(heads('/stub/real.mp3') === 1, `one HEAD, not a second download (${heads('/stub/real.mp3')})`);
  ok(asks.some((a) => a.path === '/stub/real.mp3' && a.ranged && a.method === 'GET'), 'and the head of the file was read with a Range before the file was');
}

/* ============================================================================
 * 3. the run rolls on it
 * ==========================================================================*/
{
  // The chart loading is not the lap starting: leave the panel and drive.
  await click('.rm-levels-foot .rm-btn[data-id=back]');
  await click('.rm-list .rm-btn[data-id=race]');
  await sleep(2000);
  const a = await json(`({ t: window.__race.race.track.t, playing: window.__race.race.track.playing })`);
  await sleep(2500);
  const b = await json(`({ el: window.__race.cloud.state.t, t: window.__race.race.track.t, playing: window.__race.race.track.playing })`);
  ok(b.playing === true, 'the file is playing, so the race has a clock');
  ok(b.t > a.t, `and the clock is moving, so the run rolled (${a.t.toFixed(2)}s -> ${b.t.toFixed(2)}s)`);
  ok(Math.abs(b.el - b.t) < 0.5, `the run's clock is the file's clock, on a real mp3 (${b.t.toFixed(2)}s vs ${b.el.toFixed(2)}s)`);
}

/* ============================================================================
 * 4. nothing went wrong out loud
 * ==========================================================================*/
ok(errs.length === 0, 'not one console error in the whole run' + (errs.length ? ':\n    ' + errs.slice(0, 5).join('\n    ') : ''));

/* ============================================================================
 * 5. nothing left this machine (only when no real url was asked for)
 * ==========================================================================*/
if (!REAL) {
  const away = net.filter((u) => u.indexOf('http') === 0 && u.indexOf('127.0.0.1') < 0 && u.indexOf('localhost') < 0);
  ok(away.length === 0, 'not one request left localhost' + (away.length ? ': ' + away.slice(0, 3).join(', ') : ''));
} else {
  console.log('  --  section 5 skipped: RACE_REAL_URL was set, so leaving this machine is the point');
}

await done(fails ? 1 : 0);

async function done(code) {
  try { ws && ws.close(); } catch (e) { /* never opened */ }
  try { chrome.kill(); } catch (e) { /* already gone */ }
  await new Promise((r) => server.close(r));
  await sleep(300);
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* chrome still has a lock */ }
  if (code) console.error(`\n${fails} failure${fails === 1 ? '' : 's'}`);
  else console.log('\nreal-audio-check: all good');
  process.exit(code);
}
