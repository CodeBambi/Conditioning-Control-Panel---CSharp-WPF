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
// RACE_TRACK_FILE: a local copy of a track the words index knows (the hash is a length and the first
// MiB, chartSource.js, so the stub serves the same bytes and the transcript lands by hash), which is
// how section 3c measures the sync against a real voice with nothing leaving this machine.
const TRACK_FILE = process.env.RACE_TRACK_FILE ? resolve(process.env.RACE_TRACK_FILE) : resolve(WEB, 'dtrh/assets/audio/drone1.mp3');
const PORT = 8865;
const REAL = process.env.RACE_REAL_URL || '';
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };

if (!existsSync(TRACK_FILE)) { console.error('FAIL no mp3 at ' + TRACK_FILE); process.exit(1); }
const TRACK = readFileSync(TRACK_FILE);
console.log(`the file: ${TRACK_FILE.startsWith(WEB) ? TRACK_FILE.slice(WEB.length + 1) : 'RACE_TRACK_FILE'}, ${TRACK.length} bytes (${(TRACK.length / 1048576).toFixed(2)} MiB)`);

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
// The player makes its clock with `new Audio` (CLOUD.md), so it is in no document and cannot be
// queried for. Section 3b needs to seek it, so keep a handle on the last one the page makes.
await ev(`(()=>{const A=window.Audio; window.Audio=function(){const el=new A(); window.__rcAudio=el; return el;}; return 1;})()`);
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
 * 3b. the voice on the glass, when this file has a transcript
 *
 * The clock here is the <audio> element (CLOUD.md), so this seeks the element and
 * lets the player's own tick carry the second through: the caption and the plate
 * are read at seconds the ROAD chose, not at seconds this file made up. It counts
 * what is on the glass and never reads a word of it back.
 * ==========================================================================*/
if (c.words && c.words !== 'none') {
  // Seek the file, then let the player's own 250 ms tick carry the second in: the caption and the
  // plate are read at seconds the ROAD chose, off real playback, never at a clock this file forced.
  const glass = async (t, wait = 900) => {
    await ev(`(()=>{const a=window.__rcAudio; if(a) a.currentTime=${Math.max(0, t)}; return 1;})()`);
    await sleep(wait);
    return json(`(()=>{const cap=document.querySelector('.rc-cap'), pl=document.querySelector('.rc-plate');
      return { t: window.__race.race.track.t, hidden: !cap || cap.hidden, said: cap?cap.querySelectorAll('.rc-word.is-said').length:0,
        words: cap?cap.querySelectorAll('.rc-word').length:0, theme: pl?pl.getAttribute('data-theme'):null };})()`);
  };
  const w = await json(`(()=>{const ch=window.__race.race.track.chart, tr=ch.events.filter(e=>e.kind==='trigger');
    return { n: ch.words.length, t0: ch.words.length?ch.words[0].t:-1, trig: tr.length, at: tr.length?tr[0].t:-1 };})()`);
  ok(w.n > 0, `the real chart carries its caption track (${w.n} words, ${w.trig} triggers)`);
  ok(await ev(`!!document.querySelector('.rc-layer')`), 'and the run built the caption layer');
  ok(await ev(`!!window.__rcAudio`), 'and the smoke has the file itself to seek');
  if (w.t0 > 4) {
    const before = await glass(w.t0 - 3, 700);
    ok(before.hidden, `the glass is empty a second before the first word (at ${before.t.toFixed(1)}s)`);
  }
  const on = await glass(w.t0 - 0.4);
  ok(!on.hidden && on.words > 0, `the phrase is on the glass as the voice reaches it (${on.words} words at ${on.t.toFixed(1)}s, first word ${w.t0.toFixed(1)}s)`);
  ok(on.said >= 1 && on.said <= on.words, `and it is inked only as far as the second (${on.said} of ${on.words})`);
  if (w.at > 0) {
    const plated = await glass(w.at - 0.6, 1100);
    ok(!!plated.theme, `the trigger at ${w.at.toFixed(1)}s flew its plate as the run passed it (${plated.theme} at ${plated.t.toFixed(1)}s)`);
    await sleep(1700);
    ok(await ev(`!document.querySelector('.rc-plate')`), 'and the plate is off the glass a second and a half later: fleeting, as asked');
  }
} else {
  console.log('  --  3b skipped: this file has no transcript, so there is nothing to caption');
}

/* ============================================================================
 * 3c. the sync, measured against the real voice
 *
 * The owner heard the plate land "too soon by 2 sec or so" on the phone. This
 * seeks a few seconds ahead of the first sure triggers, lets the file PLAY, and
 * watches: the track second each plate reached the DOM, and race/sync.js's own
 * trace (the second the scheduler handed the event over, the second its visible
 * half fired, the second the kart met its row). Everything is a delta from
 * event.t; nothing here is a word of the transcript. The clock is the <audio>
 * element through the host's 250 ms tick, so a quarter second of slack is real
 * playback, not the sync. RACE_SYNC_REPORT=1 prints and does not judge.
 * ==========================================================================*/
if (c.words && c.words !== 'none') {
  const trig = await json(`(()=>{const ch=window.__race.race.track.chart; return ch.events.filter(e=>e.kind==='trigger'&&(e.conf==null||e.conf>=0.55)).map(e=>({id:String(e.id),t:e.t}));})()`);
  const WINDOW = 12, TOL = 0.3, REPORT = !!process.env.RACE_SYNC_REPORT;
  if (trig.length) {
    // the window: the WINDOW seconds with the most sure triggers in them, starting 3 s before the first
    const count = (t0) => trig.filter((e) => e.t >= t0 && e.t <= t0 + WINDOW).length;
    // (RACE_SYNC_T0 pins it: the number an isolated trigger gives is the one to quote)
    const t0 = process.env.RACE_SYNC_T0 ? Number(process.env.RACE_SYNC_T0) : trig.map((e) => Math.max(0, e.t - 3)).reduce((best, v) => (count(v) > count(best) ? v : best), Math.max(0, trig[0].t - 3));
    const watched = trig.filter((e) => e.t >= t0 && e.t <= t0 + WINDOW);
    await ev(`(()=>{const S=window.__sync={plates:[]}; const R=window.__race.race;
      new MutationObserver((ms)=>{for(const m of ms)for(const n of m.addedNodes){if(n.nodeType===1&&n.classList&&n.classList.contains('rc-plate'))S.plates.push({t:R.track?R.track.t:-1});}}).observe(document.body,{childList:true,subtree:true});
      const a=window.__rcAudio; if(a){a.currentTime=${t0}; if(a.paused)a.play().catch(()=>{});} return 1;})()`);
    let now = -1;
    for (let i = 0; i < (WINDOW + 4) * 4 && now < t0 + WINDOW + 1; i++) { await sleep(250); now = await ev(`window.__race.race.track.t`); }
    ok(now >= t0 + WINDOW, `the file played the ${WINDOW}s window on its own clock (${t0.toFixed(1)}s to ${now.toFixed(1)}s)`);
    const plates = await json(`window.__sync.plates`);
    const gates = await json(`(()=>{const ch=window.__race.race.track.chart; return ch.events.filter(e=>e.kind==='silence'||e.kind==='build'||e.kind==='release').map(e=>e.kind+'@'+e.t.toFixed(1)+(e.dur?'x'+Number(e.dur).toFixed(1):''));})()`);
    console.log('  --  density gates on the road: ' + (gates.join(' ') || 'none') + `; ${await ev('window.__race.race.perf().bubbles')} bubbles live`);
    const trace = await json(`window.__race.race.syncTrace ? window.__race.race.syncTrace() : []`);
    const byId = new Map(trace.map((r) => [String(r.id), r]));
    const f2 = (v) => (v == null ? '   -   ' : (v >= 0 ? '+' : '') + v.toFixed(2));
    console.log(`  --  ${watched.length} sure triggers in the window, ${plates.length} plates seen, ${trace.length} trace lines`);
    // every plate against the sure trigger nearest to it: the raw picture, whichever way the sync leans
    const nearest = (t) => trig.reduce((b, e) => (Math.abs(e.t - t) < Math.abs(b.t - t) ? e : b), trig[0]);
    console.log('  plates, each against the nearest sure trigger: ' + (plates.map((p) => f2(p.t - nearest(p.t).t)).join('  ') || 'none'));
    console.log('  event.t   plate    handed   fired    placed   rowAt');
    const plateOff = [], rowOff = [], used = new Set();
    for (const e of watched) {
      const r = byId.get(e.id) || {};
      const pl = plates.find((p, i) => !used.has(i) && p.t >= e.t - 0.5 && p.t <= e.t + 1.5 && used.add(i));   // the plate on or just after the second, once
      const dp = pl ? pl.t - e.t : null, dr = r.rowAt != null ? r.rowAt - e.t : null;
      if (dp != null) plateOff.push(dp);
      if (dr != null) rowOff.push(dr);
      console.log(`  ${e.t.toFixed(2).padStart(7)}  ${f2(dp)}  ${f2(r.handedAt != null ? r.handedAt - e.t : null)}  ${f2(r.firedAt != null ? r.firedAt - e.t : null)}  ${f2(r.rowPlacedAt != null ? r.rowPlacedAt - e.t : null)}  ${f2(dr)}${r.dropped ? '  dropped' : ''}`);
    }
    const worst = (a) => a.reduce((m, v) => (Math.abs(v) > Math.abs(m) ? v : m), 0);
    if (plateOff.length) console.log(`  --  plate - t: worst ${f2(worst(plateOff))}s over ${plateOff.length};  row - t: worst ${f2(worst(rowOff))}s over ${rowOff.length}`);
    if (!REPORT) {
      ok(plateOff.length === watched.length, `every sure trigger in the window flew a plate (${plateOff.length} of ${watched.length})`);
      ok(plateOff.every((v) => v >= -0.05 && v <= TOL), `every plate reached the DOM on its word, never early, within ${TOL}s of real playback`);
      ok(watched.every((e) => !(byId.get(e.id) || {}).dropped), 'and no cue in the window was dropped (a seek forward in 3b may drop what the voice already said)');
      const laid = watched.filter((e) => (byId.get(e.id) || {}).rowPlacedAt != null);   // the density gate may keep a row off the road
      ok(laid.length > 0, `at least one row went down in the window (${laid.length} of ${watched.length})`);
      ok(laid.every((e) => { const r = byId.get(e.id); return r.rowAt != null && Math.abs(r.rowAt - e.t) <= TOL; }), `every row that went down was under the kart within ${TOL}s of its word`);
    }
  } else {
    console.log('  --  3c skipped: this transcript has no sure trigger to measure against');
  }
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
