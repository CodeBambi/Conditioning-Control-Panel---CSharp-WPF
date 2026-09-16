/* ============================================================================
 * race/smoke/cloud-chart-check.mjs - the W2 half of the BambiCloud lane: the
 * road a real file gets, and the three doors it can come through first.
 *
 *   node race/smoke/cloud-chart-check.mjs     (0 on pass, 1 with a count of failures)
 *
 * cloud-check.mjs holds the player down: the panel, the clock, the pause, the
 * playlist walk. This one holds down what the player is handed - and half of it
 * is pure, so sections 1 and 2 run in node before Chrome is even started.
 *
 * IT NEVER TOUCHES BAMBICLOUD. The two tracks are WAV files this file writes in
 * memory, and section 8 fails if a single request left localhost.
 *
 * The authored index is served BY THIS FILE, not shipped: `race/charts/index.json`
 * in the repo stays empty, and the server below answers that one path with a test
 * row instead. A smoke must never need a fixture in the product.
 *
 * What it holds:
 *   1. cloudIdFrom and the CHART.md hash, pure
 *   2. the wordless road, pure: energy bins, build/peak/release, quiet where the
 *      file is quiet, NO fog on the start line, and more than one room
 *   3. normalizeChart keeps `hand`, `rules` and per-event hand/cue/note
 *   4. a real file decoded in the browser gives a real road
 *   5. an authored chart WINS: it is used as written, it keeps `hand`, and the
 *      chart pipeline never asks the CDN for the file at all
 *   6. the second load of the same file is a cache hit, with no decode
 *   7. the plate still names the track
 *   8. nothing left localhost
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
import { cloudIdFrom, hashBytes, findAuthored, isAuthored } from '../chartSource.js';
import { roadFromPeaks, quietFromEnergy, actsFromEnergy, BIN_SEC, QUIET_LEVEL } from '../cloudChart.js';
import { normalizeChart } from '../chart.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const WEB = resolve(HERE, '../../..');          // Resources/web
const PORT = 8863;
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };

/* ---- the test file ------------------------------------------------------- */
/**
 * A 90 second file shaped like a hypno track and nothing like music: it opens at
 * a speaking level (so there is nothing quiet at t=0 to fog), swells twice, and
 * has two long quiet stretches and a loud way out. Over 1 MiB on purpose, so the
 * hash really does read a head and not a whole file.
 */
const DUR = 90, RATE = 8000;
function levelAt(t) {
  if (t < 8) return 0.45;
  if (t < 16) return 0.45 + 0.5 * ((t - 8) / 8);       // the first climb
  if (t < 18) return 0.95;
  if (t < 24) return 0.95 - 0.9 * ((t - 18) / 6);      // and the fall away from it
  if (t < 34) return 0.02;                             // ten seconds of nothing
  if (t < 40) return 0.45;
  if (t < 50) return 0.45 + 0.5 * ((t - 40) / 10);     // the second climb
  if (t < 52) return 0.95;
  if (t < 58) return 0.95 - 0.9 * ((t - 52) / 6);
  if (t < 70) return 0.02;                             // twelve more
  return 0.85;                                          // the way up, louder than the file's mean
}
function wav(seconds = DUR, rate = RATE) {
  const n = seconds * rate, body = Buffer.alloc(n * 2);
  for (let i = 0; i < n; i++) {
    const t = i / rate;
    body.writeInt16LE(Math.round(32000 * levelAt(t) * Math.sin((2 * Math.PI * 220 * i) / rate)), i * 2);
  }
  const head = Buffer.alloc(44);
  head.write('RIFF', 0); head.writeUInt32LE(36 + body.length, 4); head.write('WAVEfmt ', 8);
  head.writeUInt32LE(16, 16); head.writeUInt16LE(1, 20); head.writeUInt16LE(1, 22);
  head.writeUInt32LE(rate, 24); head.writeUInt32LE(rate * 2, 28); head.writeUInt16LE(2, 32); head.writeUInt16LE(16, 34);
  head.write('data', 36); head.writeUInt32LE(body.length, 40);
  return Buffer.concat([head, body]);
}
const TRACK = wav();

/** The authored stub, and the index row that points at it. Served, never shipped. */
const HAND_CHART = {
  version: 1, hand: true, rules: { density: 'as written' }, binSec: 0.5,
  energy: new Array(Math.ceil(DUR / 0.5)).fill(0.4),
  acts: [{ t0: 0, t1: DUR, kind: 'mantra', name: 'by hand' }],
  events: [
    { id: 'h1', t: 5, kind: 'trigger', label: 'good girl', hand: true, cue: 'spiral', note: 'placed by hand' },
    { id: 'h2', t: 40, kind: 'drop', label: 'sink', strength: 1, hand: true },
  ],
  source: { name: 'the authored one', hash: '', durationSec: DUR, sampleRate: 16000 },
  analysis: { energy: 'hand', words: 'hand', generatedAt: '', partial: false },
};
const TEST_INDEX = { version: 1, tracks: [{ cloudId: 'hand', title: 'the authored one', durationSec: DUR, chart: 'charts/hand-stub.chart.json' }] };

/* ============================================================================
 * 1. the two names a track has (pure)
 * ==========================================================================*/
ok(cloudIdFrom('https://cdn.bambicloud.com/files/0f4c2a18-9d3b-4c77-b0e1-6a2d5f8c1234/track.mp3') === '0f4c2a18-9d3b-4c77-b0e1-6a2d5f8c1234',
  'a uuid anywhere in the path is the id, not the file name that can be renamed under it');
ok(cloudIdFrom('https://cdn.bambicloud.com/audio/The-Settle.MP3') === 'the-settle', 'a plain url is its last segment, lower case, with the extension off');
ok(cloudIdFrom('not a url') === '' && cloudIdFrom('https://cdn.bambicloud.com/') === '', 'a url with no path at all is no id rather than a throw');
{
  const bytes = new Uint8Array(3000);
  for (let i = 0; i < bytes.length; i++) bytes[i] = (i * 31) & 255;
  const want = new Uint8Array(8 + bytes.length);
  new DataView(want.buffer).setBigUint64(0, BigInt(bytes.length), true);
  want.set(bytes, 8);
  const wantHex = [...new Uint8Array(await crypto.subtle.digest('SHA-1', want))].map((b) => b.toString(16).padStart(2, '0')).join('');
  ok(await hashBytes(bytes, bytes.length) === wantHex, 'the hash off a range is CHART.md\'s own: the length as 8 bytes LE plus the head');
  ok(await hashBytes(bytes, 0) === '', 'and a file with no length at all is no hash rather than a wrong one');
}
ok(findAuthored(TEST_INDEX, { cloudId: 'HAND' }).by === 'cloudId', 'the index is matched on cloudId first, and case is ignored');
ok(findAuthored(TEST_INDEX, { cloudId: 'nope', hash: 'nope' }) === null, 'a track nobody wrote a chart for finds nothing');
ok(isAuthored(HAND_CHART) === true && isAuthored({ version: 1 }) === false, '`hand: true` is the whole authored test');

/* ============================================================================
 * 2. the wordless road (pure)
 * ==========================================================================*/
{
  const per = 50, peaks = new Float32Array(DUR * per * 2);
  for (let i = 0; i < DUR * per; i++) { const a = levelAt(i / per); peaks[i * 2] = -a; peaks[i * 2 + 1] = a; }
  const road = roadFromPeaks({ peaks, perSec: per, durationSec: DUR, name: 'the test file', hash: 'abc' });
  const kinds = (k) => road.events.filter((e) => e.kind === k);
  ok(road.energy.length === Math.ceil(DUR / BIN_SEC), `the curve is one number per ${BIN_SEC}s bin (${road.energy.length})`);
  ok(road.energy.some((v) => v > 0.8) && road.energy.some((v) => v < QUIET_LEVEL), 'the curve reaches the loud end and the quiet end of the file');
  ok(kinds('build').length >= 2 && kinds('peak').length >= 2 && kinds('release').length >= 2,
    `both swells became a build, a peak and a release (${kinds('build').length}/${kinds('peak').length}/${kinds('release').length})`);
  ok(kinds('silence').length === 2, `the two quiet stretches are the only quiet on the road (${kinds('silence').length})`);
  ok(!road.events.some((e) => e.kind === 'silence' && e.t < 1), 'and NOTHING fogs the start line: no words is not the same as no sound');
  ok(road.acts.length >= 2 && new Set(road.acts.map((a) => a.kind)).size >= 2,
    `the file is more than one room (${road.acts.map((a) => a.kind).join(' -> ')})`);
  ok(road.acts[0].t0 === 0 && road.acts[road.acts.length - 1].t1 === DUR, 'and the rooms cover the whole file');
  ok(road.analysis.words === 'none', 'the road says out loud that nobody heard any words in it');
  const n = normalizeChart(road);
  ok(n.events.length === road.events.length && n.hand === false, 'the whole road comes back out of normalizeChart, and it is not authored');
}
{
  const flat = new Array(400).fill(0.5);
  ok(quietFromEnergy(flat, BIN_SEC, 100).length === 0, 'a file that is never quiet gets no silence at all');
  const tail = new Array(400).fill(0.5); for (let i = 320; i < 400; i++) tail[i] = 0.01;
  ok(quietFromEnergy(tail, BIN_SEC, 100).length === 0, 'and a fade out at the end is not fog on the finish line');
  ok(actsFromEnergy(new Array(400).fill(0.5), BIN_SEC, 100)[0].kind === 'induction', 'every file starts in the settle');
}

/* ============================================================================
 * 3. what normalizeChart is allowed to throw away (pure)
 * ==========================================================================*/
{
  const n = normalizeChart(HAND_CHART);
  ok(n.hand === true, '`hand: true` survives the validation pass, so an authored chart stays authored');
  ok(n.rules && n.rules.density === 'as written', 'and so does the `rules` block an author wrote');
  const e = n.events.find((x) => x.id === 'h1');
  ok(e && e.hand === true && e.cue === 'spiral' && e.note === 'placed by hand', 'an event an author placed keeps its hand, its cue and its note');
  const d = n.events.find((x) => x.id === 'h2');
  ok(d && d.hand === true && d.strength === 1, 'and the fields the kind already had are still there beside them');
}

/* ============================================================================
 * the browser half
 * ==========================================================================*/
const asks = [];                                 // { method, path, ranged }
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  asks.push({ method: req.method, path, ranged: !!req.headers.range });
  // The authored index and its chart: served here so the shipped index stays empty.
  if (path === '/dtrh/race/charts/index.json' || path === '/dtrh/race/charts/hand-stub.chart.json') {
    const body = Buffer.from(JSON.stringify(path.endsWith('index.json') ? TEST_INDEX : HAND_CHART));
    res.writeHead(200, { 'content-type': 'application/json', 'content-length': body.length });
    return res.end(req.method === 'HEAD' ? undefined : body);
  }
  if (path === '/stub/gen.wav' || path === '/stub/hand.wav') {
    const m = /^bytes=(\d+)-(\d*)$/.exec(req.headers.range || '');
    if (m) {
      const a = Number(m[1]), b = Math.min(TRACK.length - 1, m[2] ? Number(m[2]) : TRACK.length - 1);
      const cut = TRACK.subarray(a, b + 1);
      res.writeHead(206, { 'content-type': 'audio/wav', 'content-length': cut.length, 'accept-ranges': 'bytes', 'content-range': `bytes ${a}-${b}/${TRACK.length}` });
      return res.end(req.method === 'HEAD' ? undefined : cut);
    }
    res.writeHead(200, { 'content-type': 'audio/wav', 'content-length': TRACK.length, 'accept-ranges': 'bytes' });
    return res.end(req.method === 'HEAD' ? undefined : TRACK);
  }
  if (path.indexOf('..') >= 0) { res.writeHead(400); return res.end(); }
  try {
    const body = await readFile(join(WEB, path));
    res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' });
    res.end(req.method === 'HEAD' ? undefined : body);
  } catch (e) { res.writeHead(404, { 'content-type': 'text/plain' }); res.end('no'); }
});

if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME + ' (set CHROME_PATH)'); process.exit(1); }
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

const prof = mkdtempSync(join(tmpdir(), 'race-chart-'));
const chrome = spawn(CHROME, [
  '--headless=new', '--remote-debugging-port=9335', `--user-data-dir=${prof}`,
  '--no-first-run', '--no-default-browser-check', '--disable-gpu', '--mute-audio',
  '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required',
  '--window-size=1280,800', 'about:blank',
], { stdio: 'ignore' });

let page = null;
for (let i = 0; i < 60 && !page; i++) {
  await sleep(250);
  try { page = (await (await fetch('http://127.0.0.1:9335/json/list')).json()).find((t) => t.type === 'page'); } catch (e) { /* not up */ }
}
if (!page) { console.error('FAIL chrome never answered on the debug port'); await done(1); }

const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r) => { ws.onopen = r; });
let msgId = 0;
const waits = new Map(), logs = [], net = [];
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Network.requestWillBeSent') net.push(m.params.request.url);
  if (m.method === 'Runtime.consoleAPICalled') logs.push(m.params.args.map((a) => a.value ?? a.description ?? '?').join(' '));
};
const cdp = (method, params) => new Promise((res) => { const i = ++msgId; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
const json = async (x) => JSON.parse(await ev(`JSON.stringify(${x})`));
const click = (sel) => ev(`(()=>{const b=document.querySelector(${JSON.stringify(sel)}); if(!b) return 0; b.click(); return 1;})()`);
const said = (s, from = 0) => logs.slice(from).some((l) => l.indexOf(s) >= 0);
const heads = (p) => asks.filter((a) => a.method === 'HEAD' && a.path === p).length;

async function bootAt(url) {
  await cdp('Page.navigate', { url });
  for (let i = 0; i < 80; i++) {
    await sleep(250);
    const up = await ev(`!!(window.__race && window.__race.menu) && !!document.querySelector('.rm-root') && !document.querySelector('.rm-root').hidden`);
    if (up) { await sleep(300); return true; }
  }
  return false;
}
/**
 * Clear the list, paste `links` and wait until the run is holding the chart called
 * `wantName`. The clear matters: `addTracks` only starts a track when nothing is
 * loaded, so without it a second paste sits in the list and this would pass on the
 * chart that was already there.
 */
async function play(links, wantName) {
  await click('.rm-cloud .rm-cloud-btn[data-id=forget]');
  await sleep(300);
  await ev(`(()=>{const i=document.querySelector('.rm-cloud-in'); i.value=${JSON.stringify(links)}; return 1;})()`);
  await click('.rm-cloud .rm-cloud-btn[data-id=add]');
  for (let i = 0; i < 120; i++) {
    await sleep(250);
    const got = await ev(`(()=>{const t=window.__race.race.track; return t && !t.chart.analysis.partial ? t.chart.source.name : '';})()`);
    if (got === wantName) return true;
  }
  return false;
}
const site = `http://127.0.0.1:${PORT}`;
// Lane W4 put the levels list on top and folded lane W1's paste box into a drawer under it.
// Everything below walks the drawer, so opening the panel means opening that too: while it is
// collapsed its rows are not part of the panel's walk and a press cannot land on one.
const openPanel = async () => {
  await click('.rm-list .rm-btn[data-id=cloud]'); await sleep(400);
  await click('.rm-levels-tail .rm-btn[data-id=paste]'); await sleep(300);
};

await cdp('Runtime.enable'); await cdp('Page.enable'); await cdp('Network.enable');

/* ---- 4. a real file gets a real road ------------------------------------- */
ok(await bootAt(`${site}/dtrh/race.html?cloud=1&intro=0&cards=0`), 'the page boots to the menu with ?cloud=1');
await openPanel();
ok(await play(`${site}/stub/gen.wav`, 'gen'), 'a pasted file is decoded and charted in the browser');
{
  const c = await json(`(()=>{const c=window.__race.race.track.chart; const k={}; for(const e of c.events) k[e.kind]=(k[e.kind]||0)+1;
    return { hand:c.hand, bins:c.energy.length, kinds:k, acts:c.acts.map(a=>a.kind), words:c.analysis.words, dur:c.source.durationSec, hash:c.source.hash, first:c.events.length?c.events[0].t:-1 };})()`);
  ok(c.bins > 300, `the chart carries an energy curve off the real file (${c.bins} bins)`);
  ok((c.kinds.build || 0) >= 2 && (c.kinds.peak || 0) >= 2 && (c.kinds.release || 0) >= 2,
    `and both swells are a build, a peak and a release (${JSON.stringify(c.kinds)})`);
  ok((c.kinds.silence || 0) >= 1 && c.first > 1, `the quiet stretches are quiet and the start line is not fogged (${c.kinds.silence || 0} of them)`);
  ok(new Set(c.acts).size >= 2, `the file is more than one room (${c.acts.join(' -> ')})`);
  ok(c.words === 'none' && c.hand === false, 'it says it heard no words, and it is not pretending to be authored');
  ok(/^[0-9a-f]{40}$/.test(c.hash), 'and it is stamped with the CHART.md hash of the file: ' + String(c.hash).slice(0, 12));
  ok(Math.abs(c.dur - DUR) < 1, `the road is the length of the file (${c.dur}s)`);
  ok(said('generated in'), 'the log says which door it came through');
  ok(heads('/stub/gen.wav') === 1, `the hash was worked out from one HEAD and a range, not from a second download (${heads('/stub/gen.wav')} HEADs)`);
  ok(asks.some((a) => a.path === '/stub/gen.wav' && a.ranged && a.method === 'GET'), 'the head of the file was read with a Range, before the file was');
}

/* ---- 5. an authored chart wins ------------------------------------------- */
{
  const mark = logs.length;
  ok(await play(`${site}/stub/hand.wav`, 'the authored one'), 'a track with an authored chart loads');
  const c = await json(`(()=>{const c=window.__race.race.track.chart; const e=c.events.find(x=>x.label==='good girl');
    return { hand:c.hand, rules:c.rules, name:c.source.name, acts:c.acts.map(a=>a.kind), n:c.events.length, ev:e||null };})()`);
  ok(c.hand === true, 'and it is used AS WRITTEN: the chart the run holds is the authored one');
  ok(c.n === 2 && c.acts.join() === 'mantra', 'nothing generated was merged into it: its two events and its one room are what is on the road');
  ok(c.ev && c.ev.hand === true && c.ev.cue === 'spiral' && c.ev.note === 'placed by hand', 'the author\'s own marks on an event came through the load');
  ok(c.rules && c.rules.density === 'as written', 'and so did the rules block');
  ok(said('authored by cloudId', mark), 'the log says it came in through the index, by cloudId');
  // The HEAD is the tell. The <audio> element ranges the file all by itself because that is
  // how a media element streams, and it has to: it is the clock. What must not happen is the
  // CHART pipeline going near it, and the pipeline's first move is always a HEAD.
  ok(heads('/stub/hand.wav') === 0, 'the chart pipeline never asked the CDN for that file: an authored track costs no download');
  ok(asks.some((a) => a.path === '/dtrh/race/charts/hand-stub.chart.json'), 'it read the authored chart instead, off our own origin');
}

/* ---- 5b. the next track is read while this one plays ---------------------- */
{
  const mark = logs.length;
  ok(await play(`${site}/stub/gen.wav ${site}/stub/hand.wav`, 'gen'), 'two links pasted, the first one is the lap being driven');
  let ahead = false;
  for (let i = 0; i < 20 && !ahead; i++) { await sleep(250); ahead = said('reading ahead: hand', mark); }
  ok(ahead, 'and the NEXT one is already being read, so the next lap starts with its road in hand');
  ok(heads('/stub/hand.wav') === 0, 'reading it ahead still cost no download: it is authored, and the url alone said so');
}

/* ---- 6. the second time, no decode --------------------------------------- */
{
  ok(await bootAt(`${site}/dtrh/race.html?cloud=1&intro=0&cards=0`), 'the page is loaded again, fresh');
  const mark = logs.length;
  await openPanel();
  ok(await play(`${site}/stub/gen.wav`, 'gen'), 'the same file loads a second time');
  ok(said('cached in', mark), 'and the road comes out of the cache this time, not out of a decode');
  ok(!said('generated in', mark), 'nothing was generated: the cache is keyed on the hash and the hash still names that file');
  const c = await json(`(()=>{const c=window.__race.race.track.chart; return { bins:c.energy.length, n:c.events.length, hand:c.hand };})()`);
  ok(c.bins > 300 && c.n > 4 && c.hand === false, 'the cached road is the same road, and it is still not authored');
}

/* ---- 7. the plate -------------------------------------------------------- */
ok((await ev(`document.querySelector('.rm-track-name').textContent`)) === 'gen', 'the plate still names the real track');

/* ---- 8. nothing left this machine ---------------------------------------- */
{
  const away = net.filter((u) => u.indexOf('http') === 0 && u.indexOf('127.0.0.1') < 0 && u.indexOf('localhost') < 0);
  ok(away.length === 0, 'not one request left localhost in the whole run' + (away.length ? ': ' + away.slice(0, 3).join(', ') : ''));
}

await done(fails ? 1 : 0);

async function done(code) {
  try { ws && ws.close(); } catch (e) { /* never opened */ }
  try { chrome.kill(); } catch (e) { /* already gone */ }
  await new Promise((r) => server.close(r));
  await sleep(300);
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* chrome still has a lock */ }
  if (code) console.error(`\n${fails} failure${fails === 1 ? '' : 's'}`);
  else console.log('\ncloud-chart-check: all good');
  process.exit(code);
}
