/* ============================================================================
 * race/smoke/cloud-check.mjs - headless self-check for race/cloud.js, the
 * BambiCloud mini-player, driven through the REAL page.
 *
 *   node race/smoke/cloud-check.mjs      (exits 0 on pass, 1 with a count of failures)
 *
 * The other checks in this folder are pure node because the units under them
 * are pure. This one is not: the thing being held down is a menu verb, a panel,
 * an <audio> element and the CHART.md frames run.js posts as a run starts,
 * pauses and ends. So this file serves Resources/web itself, drives a headless
 * Chrome over CDP (node's own WebSocket, no dependency) and walks the panel the
 * way a finger would.
 *
 * IT NEVER TOUCHES BAMBICLOUD. The two tracks are WAV files this file writes in
 * memory and serves from its own origin, and every request the page makes is
 * recorded: section 8 fails if a single one left localhost. That is the point of
 * the recording, not a detail - the third-party rule is the one rule in this
 * lane that cannot be checked by reading the code.
 *
 * What it holds:
 *   1. the verb appears with `?cloud=1` and is GONE without it (the gate)
 *   2. the panel opens and carries the paste box
 *   3. a link that will not load fails LOUD and ONCE: exactly two requests, the
 *      entry is marked, the panel goes back to the paste box
 *   4. a paste of two tracks and one page link gives two playable and one
 *      LOCKED, and the locked one is never requested
 *   5. the plate names the track and its place in the list
 *   6. the run's clock follows the element, second for second
 *   7. the Brake stops the file and resume starts it, both ways
 *   8. the file running out ends the lap and rolls to the next track
 *
 * CHROME: `CHROME_PATH` if it is set, else the usual Windows install. Nothing is
 * installed by this file and the profile it makes is deleted on the way out.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const WEB = resolve(HERE, '../../..');          // Resources/web
const PORT = 8862;
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };

/** A real, decodable 4 second mono WAV. Written here so no binary is committed for a smoke. */
function wav(seconds = 4, rate = 8000) {
  const n = seconds * rate, body = Buffer.alloc(n * 2);
  for (let i = 0; i < n; i++) body.writeInt16LE(Math.round(3000 * Math.sin((2 * Math.PI * 220 * i) / rate)), i * 2);
  const head = Buffer.alloc(44);
  head.write('RIFF', 0); head.writeUInt32LE(36 + body.length, 4); head.write('WAVEfmt ', 8);
  head.writeUInt32LE(16, 16); head.writeUInt16LE(1, 20); head.writeUInt16LE(1, 22);
  head.writeUInt32LE(rate, 24); head.writeUInt32LE(rate * 2, 28); head.writeUInt16LE(2, 32); head.writeUInt16LE(16, 34);
  head.write('data', 36); head.writeUInt32LE(body.length, 40);
  return Buffer.concat([head, body]);
}
const TRACK = wav();
const hits = new Map();                          // path -> how many times the page asked for it
const bump = (p) => hits.set(p, (hits.get(p) || 0) + 1);

const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  bump(path);
  if (path === '/stub/one.wav' || path === '/stub/two.wav') {
    res.writeHead(200, { 'content-type': 'audio/wav', 'content-length': TRACK.length, 'accept-ranges': 'none' });
    return res.end(TRACK);
  }
  if (path.indexOf('..') >= 0) { res.writeHead(400); return res.end(); }
  try {
    const body = await readFile(join(WEB, path));
    res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' });
    res.end(body);
  } catch (e) { res.writeHead(404, { 'content-type': 'text/plain' }); res.end('no'); }
});

if (!existsSync(CHROME)) {
  console.error('FAIL no chrome at ' + CHROME + ' (set CHROME_PATH)');
  process.exit(1);
}
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

const prof = mkdtempSync(join(tmpdir(), 'race-cloud-'));
const chrome = spawn(CHROME, [
  '--headless=new', '--remote-debugging-port=9334', `--user-data-dir=${prof}`,
  '--no-first-run', '--no-default-browser-check', '--disable-gpu', '--mute-audio',
  // swiftshader: the page builds a WebGLRenderer on the first line of createRace.
  // no-user-gesture: nothing in a headless run can click before the run starts the file.
  '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required',
  '--window-size=1280,800', 'about:blank',
], { stdio: 'ignore' });

let page = null;
for (let i = 0; i < 60 && !page; i++) {
  await sleep(250);
  try { page = (await (await fetch('http://127.0.0.1:9334/json/list')).json()).find((t) => t.type === 'page'); } catch (e) { /* not up */ }
}
if (!page) { console.error('FAIL chrome never answered on the debug port'); await done(1); }

const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r) => { ws.onopen = r; });
let msgId = 0;
const waits = new Map(), logs = [], asked = [];
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Network.requestWillBeSent') asked.push(m.params.request.url);
  if (m.method === 'Runtime.consoleAPICalled') logs.push(m.params.args.map((a) => a.value ?? a.description ?? '?').join(' '));
};
const cdp = (method, params) => new Promise((res) => { const i = ++msgId; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
/** Evaluate in the page and hand back the value. Every probe in here is one of these. */
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
const json = async (x) => JSON.parse(await ev(`JSON.stringify(${x})`));
const click = (sel) => ev(`(()=>{const b=document.querySelector(${JSON.stringify(sel)}); if(!b) return 0; b.click(); return 1;})()`);
const verbs = () => json(`[...document.querySelectorAll('.rm-list .rm-btn')].filter(b=>!b.hidden).map(b=>b.dataset.id)`);
const rowIds = () => json(`[...document.querySelectorAll('.rm-cloud .rm-cloud-btn')].map(b=>b.dataset.id+'='+b.lastChild.textContent)`);
/**
 * The page has to reach the menu AND HAVE SHOWN IT before anything here means
 * anything: race/menu.js drops every press while the column is hidden, so a
 * click sent under the splash is a click that silently did nothing.
 */
async function bootAt(url) {
  await cdp('Page.navigate', { url });
  for (let i = 0; i < 80; i++) {
    await sleep(250);
    const up = await ev(`!!(window.__race && window.__race.menu) && !!document.querySelector('.rm-root') && !document.querySelector('.rm-root').hidden`);
    if (up) { await sleep(300); return true; }
  }
  return false;
}
const site = `http://127.0.0.1:${PORT}`;

await cdp('Runtime.enable'); await cdp('Page.enable'); await cdp('Network.enable');

/* ---- 1. the gate -------------------------------------------------------- */
ok(await bootAt(`${site}/dtrh/race.html?cloud=1&intro=0&cards=0`), 'the page boots to the menu with ?cloud=1');
ok((await verbs()).includes('cloud'), 'the `play from bambicloud` verb is on the list');
ok(await ev(`document.querySelector('.rm-list .rm-btn[data-id=cloud]').textContent`) === 'play from bambicloud', 'and it is labelled in the menu\'s own lower case');

/* ---- 2. the panel ------------------------------------------------------- */
await click('.rm-list .rm-btn[data-id=cloud]');
await sleep(400);
ok(await ev(`!document.querySelector('.rm-cloud').hidden`), 'pressing the verb opens the panel');
ok(await ev(`!!document.querySelector('.rm-cloud-in')`), 'the panel carries the paste box');
ok((await rowIds()).map((r) => r.split('=')[0]).join(',') === 'add,back', 'an empty panel is the add row and back, nothing else');

/* ---- 3. a link that will not load: loud, and once ------------------------ */
await ev(`(()=>{const i=document.querySelector('.rm-cloud-in'); i.value='${site}/stub/missing.wav'; return 1;})()`);
await click('.rm-cloud .rm-cloud-btn[data-id=add]');
await sleep(4000);
ok(hits.get('/stub/missing.wav') === 2, `a dead link is asked for exactly twice, one retry and no more (${hits.get('/stub/missing.wav')})`);
{
  const st = await json(`window.__race.cloud.state`);
  ok(st.list.length === 1 && st.list[0].failed === true, 'the entry is marked failed rather than tried again');
  ok(st.at === -1 && st.view === 'paste', 'and the panel goes back to the paste box with nothing loaded');
  ok(logs.some((l) => l.indexOf('bambicloud is not answering') >= 0), 'the plain line is said out loud');
}
await click('.rm-cloud .rm-cloud-btn[data-id=forget]');
await sleep(300);

/* ---- 4. a playlist, with one locked entry -------------------------------- */
await ev(`(()=>{const i=document.querySelector('.rm-cloud-in'); i.value='${site}/stub/one.wav ${site}/stub/two.wav https://bambicloud.com/playlist/aaaa'; return 1;})()`);
await click('.rm-cloud .rm-cloud-btn[data-id=add]');
await sleep(3000);
{
  const st = await json(`window.__race.cloud.state`);
  ok(st.list.length === 3, 'three links pasted, three entries');
  ok(st.list[2].locked === true && st.list[2].url === null, 'a link to a page on the site is LOCKED and carries no url');
  ok(!hits.has('/playlist/aaaa'), 'and a locked entry is never requested');
  ok(st.at === 0 && st.busy === false, 'the first playable track is the one in hand');
  const words = await rowIds();
  ok(words.some((w) => w.indexOf('locked on bambicloud') >= 0), 'the locked row says why: ' + words.filter((w) => w.startsWith('trk-2'))[0]);
  ok(hits.get('/stub/one.wav') >= 1 && !hits.has('/stub/two.wav'), 'only the track being played is fetched: the rest of the list waits');
}

/* ---- 5. the plate -------------------------------------------------------- */
{
  const name = await ev(`document.querySelector('.rm-track-name').textContent`);
  ok(name === 'one \u00b7 1 of 3', 'the plate names the track and where it sits in the list: ' + JSON.stringify(name));
  ok((await ev(`document.querySelector('.rm-cloud-next').textContent`)) === 'next up: two', 'and the next one is named under the box');
}

/* ---- 6. the run's clock follows the file --------------------------------- */
await click('.rm-cloud .rm-cloud-btn[data-id=back]');
await click('.rm-list .rm-btn[data-id=race]');
await sleep(2000);
{
  const s = await json(`({ el: window.__race.cloud.state.t, run: window.__race.race.track.t, playing: window.__race.race.track.playing })`);
  ok(s.playing === true, 'the run reads the track as playing once the lap starts');
  ok(s.el > 0.5, `the element is running (${s.el.toFixed(2)}s)`);
  ok(Math.abs(s.el - s.run) < 0.5, `and the run's clock is the element's clock (${s.run.toFixed(2)}s vs ${s.el.toFixed(2)}s)`);
}

/* ---- 7. the Brake, both ways --------------------------------------------- */
await ev(`window.__race.race.setPaused(true)`);
await sleep(700);
{
  const s = await json(`({ run: window.__race.race.track.playing, el: window.__race.cloud.state.playing })`);
  ok(s.run === false && s.el === false, 'a pause stops the file and the run says playing:false');
}
await ev(`window.__race.race.setPaused(false)`);
await sleep(700);
{
  const s = await json(`({ run: window.__race.race.track.playing, el: window.__race.cloud.state.playing })`);
  ok(s.run === true && s.el === true, 'and letting go starts them both again');
}

/* ---- 8. the file runs out: the lap ends and the next track is next -------- */
// `at` moves the moment the next load starts; the chart lands a beat later, so wait for both.
for (let i = 0; i < 60; i++) {
  const st = await json(`window.__race.cloud.state`);
  if (st.at === 1 && st.busy === false) break;
  await sleep(250);
}
{
  const st = await json(`window.__race.cloud.state`);
  ok(st.at === 1, 'the end of the file rolls the player on to the next track');
  ok(hits.get('/stub/two.wav') >= 1, 'which is fetched only now, when it is its turn');
  ok((await ev(`window.__race.race.track.name`)) === 'two', 'and the run is holding the next track\'s chart, ready for the next lap');
}

/* ---- 9. nothing left this machine ---------------------------------------- */
{
  const away = asked.filter((u) => u.indexOf('http') === 0 && u.indexOf('127.0.0.1') < 0 && u.indexOf('localhost') < 0);
  ok(away.length === 0, 'not one request left localhost in the whole run' + (away.length ? ': ' + away.slice(0, 3).join(', ') : ''));
}

/* ---- 10. the gate again, from the other side ----------------------------- */
ok(await bootAt(`${site}/dtrh/race.html?intro=0&cards=0`), 'the page boots again with the switch off');
ok(!(await verbs()).includes('cloud'), 'and without `cloud` the verb is not on the list at all');
ok(await ev(`window.__race.cloud === null`), 'the player is never built, so no link of any kind can be loaded');

await done(fails ? 1 : 0);

async function done(code) {
  try { ws && ws.close(); } catch (e) { /* never opened */ }
  try { chrome.kill(); } catch (e) { /* already gone */ }
  await new Promise((r) => server.close(r));
  await sleep(300);
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* chrome still has a lock */ }
  if (code) console.error(`\n${fails} failure${fails === 1 ? '' : 's'}`);
  else console.log('\ncloud-check: all good');
  process.exit(code);
}
