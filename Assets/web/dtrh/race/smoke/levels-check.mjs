/* ============================================================================
 * race/smoke/levels-check.mjs - headless self-check for race/levels.js, the
 * LEVELS panel, driven through the REAL page.
 *
 *   node race/smoke/levels-check.mjs      (exits 0 on pass, 1 with a count)
 *
 * THE SHIPPED LIST IS NEVER USED HERE. race/levels.json names files on a public
 * cdn, and a check that reached them would be a check that phones a third party
 * every time anybody runs it. So this file serves its OWN two-level list at
 * /stub/levels.json and the page is pointed at it with `?levels=`, which
 * raceBoot only honours for a same-origin url. The two tracks are WAV files
 * this file writes in memory. Section 9 records every request the page made and
 * fails if a single one left localhost.
 *
 * The authored index is stubbed the same way: /dtrh/race/charts/index.json is
 * answered with one row for the FIRST stub track, so `hand-tuned` and `road`
 * are both on the screen at once and neither of them is a row that shipped.
 *
 * What it holds:
 *   1. the verb is `levels` and it sits directly under `race`
 *   2. the panel opens on the set title and one row per level, each with its
 *      number, its name and its length as m:ss
 *   3. the marks: `hand-tuned` where the index has a row, `road` where it does
 *      not, and neither of them cost a request
 *   4. a tap is a ONE TRACK run: that track and nothing else, and the run's
 *      clock is the file's clock. The tap closes the panel: the main list is
 *      back with the plate up, its name on it, the status line stood down for
 *      the plate, and the first verb reading `start · <name>`
 *  4b. the tapped row IS the status inside the panel: it alone is `is-picked`,
 *      it alone grows a progress bar, a road in hand fills that bar and reads
 *      `loaded`, the plate stays down while the panel is open, and `back` is
 *      pinned to the bottom of the column (still the last row the arrows walk)
 *   5. `again` lands on the last level played, and it survives a reload
 *   6. `play the set` is the whole list in order, the first one is the authored
 *      chart, and the end of a file rolls the lap on to the next level
 *   7. `or paste a link` opens lane W1's box under the list, unchanged
 *   8. on a desktop host nothing is loaded here: the row posts `cloud-open`
 *      with the track's page url and says to press play over there
 *   9. not one request left localhost
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
const PORT = 8866;
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };

/** A real, decodable mono WAV. Written here so no binary is committed for a smoke. */
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

/**
 * The stub list. Two levels, both on this origin, both with their byte length written
 * down the way the shipped file writes it: that length is what lets the hash skip the
 * HEAD, which is the one thing the real cdn will not answer cross origin.
 */
const LEVELS = {
  version: 1,
  sets: [{
    id: 'stub-set', title: 'A Stub Set', source: 'stub', playlistId: 'stub', playlistUrl: 'https://bambicloud.com/playlist/stub',
    levels: [
      { n: 1, id: 'stub-one', title: 'The First One', url: `http://127.0.0.1:${PORT}/stub/one.wav`, durationSec: 4, bytes: TRACK.length, trackNum: 0 },
      { n: 2, id: 'stub-two', title: 'The Second One', url: `http://127.0.0.1:${PORT}/stub/two.wav`, durationSec: 4, bytes: TRACK.length, trackNum: 1 },
    ],
  }],
};
// cloudIdFrom('/stub/one.wav') is 'one': no path part is a uuid or 20 id characters, so the rule
// falls through to the last part with its extension taken off. That is the key the index needs.
const INDEX = { version: 1, tracks: [{ cloudId: 'one', title: 'The First One', durationSec: 4, chart: 'charts/stub.chart.json' }] };
const AUTHORED = {
  version: 1, hand: true, binSec: 0.5, energy: [0.2, 0.4, 0.6, 0.4],
  acts: [{ t0: 0, t1: 4, kind: 'induction', name: 'the stub' }],
  events: [{ id: 'a0', kind: 'peak', t: 2, dur: 0.5, label: 'by hand', conf: 1, weight: 1 }],
  source: { name: 'The First One', hash: '', durationSec: 4, sampleRate: 16000 },
  analysis: { energy: 'by-hand', words: 'none', partial: false },
};

const hits = new Map();                          // path -> how many times the page asked for it
const bump = (p) => hits.set(p, (hits.get(p) || 0) + 1);
const jsonRes = (res, o) => { const b = Buffer.from(JSON.stringify(o)); res.writeHead(200, { 'content-type': 'application/json', 'content-length': b.length }); res.end(b); };

const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  bump(path);
  if (path === '/stub/levels.json') return jsonRes(res, LEVELS);
  // The stub index and its one chart stand in for the shipped pair, so no test row ever ships.
  if (path === '/dtrh/race/charts/index.json') return jsonRes(res, INDEX);
  if (path === '/dtrh/race/charts/stub.chart.json') return jsonRes(res, AUTHORED);
  if (path === '/stub/one.wav' || path === '/stub/two.wav') {
    const m = /^bytes=(\d+)-(\d*)$/.exec(req.headers.range || '');
    if (m) {
      const a = Number(m[1]), b = Math.min(TRACK.length - 1, m[2] ? Number(m[2]) : TRACK.length - 1);
      const cut = TRACK.subarray(a, b + 1);
      res.writeHead(206, { 'content-type': 'audio/wav', 'content-length': cut.length, 'content-range': `bytes ${a}-${b}/${TRACK.length}`, 'accept-ranges': 'bytes' });
      return res.end(cut);
    }
    res.writeHead(200, { 'content-type': 'audio/wav', 'content-length': TRACK.length, 'accept-ranges': 'bytes' });
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

const prof = mkdtempSync(join(tmpdir(), 'race-levels-'));
const chrome = spawn(CHROME, [
  '--headless=new', '--remote-debugging-port=9338', `--user-data-dir=${prof}`,
  '--no-first-run', '--no-default-browser-check', '--disable-gpu', '--mute-audio',
  '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required',
  '--window-size=390,844', 'about:blank',                       // a phone, because that is who this panel is for
], { stdio: 'ignore' });

let page = null;
for (let i = 0; i < 60 && !page; i++) {
  await sleep(250);
  try { page = (await (await fetch('http://127.0.0.1:9338/json/list')).json()).find((t) => t.type === 'page'); } catch (e) { /* not up */ }
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
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
const json = async (x) => JSON.parse(await ev(`JSON.stringify(${x})`));
const click = (sel) => ev(`(()=>{const b=document.querySelector(${JSON.stringify(sel)}); if(!b) return 0; b.click(); return 1;})()`);
const verbs = () => json(`[...document.querySelectorAll('.rm-list .rm-btn')].filter(b=>!b.hidden).map(b=>b.dataset.id)`);
/** Every level row as the screen reads it: number, title, length, mark, and whether it is lit. */
const levelRows = () => json(`[...document.querySelectorAll('.rm-levels .rm-level-btn')].map(b=>({
  id: b.dataset.id,
  n: b.querySelector('.rm-level-n').textContent,
  title: b.querySelector('.rm-level-title').textContent,
  len: b.querySelector('.rm-level-len').textContent,
  mark: b.querySelector('.rm-level-mark').textContent,
  hand: b.classList.contains('is-hand'),
  tall: Math.round(b.getBoundingClientRect().height),
  wide: Math.round(b.getBoundingClientRect().width),
}))`);

const site = `http://127.0.0.1:${PORT}`;
const LIST = `levels=${encodeURIComponent('/stub/levels.json')}`;

async function bootAt(url) {
  await cdp('Page.navigate', { url });
  for (let i = 0; i < 80; i++) {
    await sleep(250);
    const up = await ev(`!!(window.__race && window.__race.menu) && !!document.querySelector('.rm-root') && !document.querySelector('.rm-root').hidden`);
    if (up) { await sleep(300); return true; }
  }
  return false;
}

await cdp('Runtime.enable'); await cdp('Page.enable'); await cdp('Network.enable');

/* ---- 1. the verb -------------------------------------------------------- */
ok(await bootAt(`${site}/dtrh/race.html?cloud=1&intro=0&cards=0&${LIST}`), 'the page boots to the menu with ?cloud=1');
{
  const v = await verbs();
  ok(v.includes('cloud'), 'the levels verb is on the list');
  ok(v[0] === 'race' && v[1] === 'cloud', 'and it sits directly under `race`: ' + v.slice(0, 3).join(', '));
  ok(await ev(`document.querySelector('.rm-list .rm-btn[data-id=cloud]').textContent`) === 'levels', 'labelled `levels`, in the menu\'s own lower case');
}

/* ---- 2. the panel ------------------------------------------------------- */
await click('.rm-list .rm-btn[data-id=cloud]');
await sleep(500);
ok(await ev(`!document.querySelector('.rm-cloud').hidden`), 'pressing the verb opens the panel');
ok(await ev(`document.querySelector('.rm-levels-set').textContent`) === 'A Stub Set', 'the set is named at the top');
{
  const r = await levelRows();
  ok(r.length === 2, `one row per level (${r.length})`);
  ok(r[0].n === '01' && r[0].title === 'The First One' && r[0].len === '0:04', 'a row is its number, its name and its length: ' + JSON.stringify([r[0].n, r[0].title, r[0].len]));
  ok(r[1].n === '02' && r[1].title === 'The Second One' && r[1].len === '0:04', 'and so is the next one');
  ok(r.every((x) => x.tall >= 48), `every row is at least 48 px tall for a thumb (${r.map((x) => x.tall).join(', ')})`);
  ok(r.every((x) => x.wide > 200), `and full width (${r.map((x) => x.wide).join(', ')})`);
}

/* ---- 3. the marks, decided with no network ------------------------------ */
{
  const r = await levelRows();
  ok(r[0].mark === 'hand-tuned' && r[0].hand === true, 'the level the authored index has a row for is marked hand-tuned');
  ok(r[1].mark === 'road' && r[1].hand === false, 'and the one it does not is marked road');
  ok(!hits.has('/stub/one.wav') && !hits.has('/stub/two.wav'), 'neither mark cost a single request for a file');
}

/* ---- 4. a tap is a one track run ---------------------------------------- */
await click('.rm-levels .rm-level-btn[data-id=lv-2]');
await sleep(3000);
{
  const st = await json(`window.__race.cloud.state`);
  ok(st.list.length === 1 && st.list[0].id === 'stub-two', 'a tap plays that level and NOTHING else: ' + JSON.stringify(st.list.map((e) => e.id)));
  ok(st.at === 0 && st.busy === false, 'and it is the track in hand');
  ok(await ev(`document.querySelector('.rm-track-name').textContent`) === 'The Second One', 'the plate names it');
  ok(await ev(`document.querySelector('.rm-cloud').hidden === true && !document.querySelector('.rm-list').hidden`), 'the tap closed the panel: the main list is back');
  ok(await ev(`document.querySelector('.rm-track').hidden === false`), 'and the plate is UP on the main list, bar and all');
  ok(await ev(`document.querySelector('.rm-status-track').hidden === true`), 'the track status line stands down for the plate');
  ok(await ev(`document.querySelector('.rm-list .rm-btn[data-id=race]').textContent`) === 'start · The Second One', 'and the first verb reads `start · The Second One`');
}

/* ---- 4b. the picked row IS the status, and `back` never leaves the screen ---- */
await click('.rm-list .rm-btn[data-id=cloud]');
await sleep(300);
{
  const rows = await json(`[...document.querySelectorAll('.rm-levels .rm-level-btn')].map(b=>({
    id: b.dataset.id, picked: b.classList.contains('is-picked'), loaded: b.classList.contains('is-loaded'),
    bar: b.querySelector('.rm-level-bar') ? getComputedStyle(b.querySelector('.rm-level-bar')).display : 'none',
    fill: b.querySelector('.rm-level-bar > i') ? b.querySelector('.rm-level-bar > i').style.width : '',
    mark: b.querySelector('.rm-level-mark').textContent,
  }))`);
  const two = rows.find((r) => r.id === 'lv-2'), one = rows.find((r) => r.id === 'lv-1');
  ok(two.picked && !one.picked, 'the row that was tapped is the picked one, and it is the only one');
  ok(two.bar === 'block' && one.bar === 'none', 'the picked row grew its own progress bar and no other row has one');
  ok(two.fill === '100%' && two.loaded, `the road is in hand, so the bar is full and the row reads loaded (${two.fill}, mark "${two.mark}")`);
  ok(await ev(`window.__race.levels.state.picked`) === 'stub-two', 'and the panel names that level as the picked one');
  // inside the panel the picked row is the plate: two places saying the same thing is one too many
  ok(await ev(`document.querySelector('.rm-track').hidden === true`), 'the track plate stays down while the panel is open');
  const back = await json(`(()=>{const col=document.querySelector('.rm-col'); col.scrollTop = 99999;
    const b=document.querySelector('.rm-levels-foot .rm-btn[data-id=back]'), f=document.querySelector('.rm-levels-foot'), r=b.getBoundingClientRect();
    return { pos: getComputedStyle(f).position, top: Math.round(r.top), bottom: Math.round(r.bottom), h: innerHeight, last: b === [...document.querySelectorAll('.rm-cloud [role=menuitem]')].pop() };})()`);
  ok(back.pos === 'sticky', 'the foot is pinned to the bottom of the scrolling column');
  ok(back.top >= 0 && back.bottom <= back.h, `so back is on the screen with the column scrolled to the end (${back.top}..${back.bottom} of ${back.h})`);
  ok(back.last === true, 'and it is still the LAST row, so the arrows and the pad walk the order they always did');
}
await click('.rm-levels-foot .rm-btn[data-id=back]');
await click('.rm-list .rm-btn[data-id=race]');
await sleep(2000);
{
  const s = await json(`({ el: window.__race.cloud.state.t, run: window.__race.race.track.t, playing: window.__race.race.track.playing })`);
  ok(s.playing === true, 'the run reads the track as playing once the lap starts');
  ok(s.el > 0.5, `the file is running (${s.el.toFixed(2)}s)`);
  ok(Math.abs(s.el - s.run) < 0.5, `and the run's clock is the file's clock (${s.run.toFixed(2)}s vs ${s.el.toFixed(2)}s)`);
}

/* ---- 5. `again`, and it survives a reload -------------------------------- */
ok(await ev(`localStorage.getItem('race.level')`) === 'stub-two', 'the level just played is remembered under race.level');
ok(await bootAt(`${site}/dtrh/race.html?cloud=1&intro=0&cards=0&${LIST}`), 'the page boots again');
await click('.rm-list .rm-btn[data-id=cloud]');
await sleep(500);
{
  const r = await levelRows();
  ok(r[1].mark === 'again', 'and the level played last carries the `again` hint');
  ok(r[0].mark === 'hand-tuned', 'while the others still read as they did');
}

/* ---- 6. play the set ----------------------------------------------------- */
await click('.rm-levels-tail .rm-btn[data-id=set]');
await sleep(3000);
{
  const st = await json(`window.__race.cloud.state`);
  ok(st.list.length === 2, `the whole set is in hand (${st.list.length})`);
  ok(st.at === 0 && st.list[0].id === 'stub-one', 'starting at the first level');
  const held = await json(`(()=>{const t=window.__race.race.track; return { name: t ? t.name : null, hand: t ? t.chart.hand : null };})()`);
  ok(held.hand === true, 'and the authored chart won for it, off the index alone: ' + JSON.stringify(held));
  ok(await ev(`document.querySelector('.rm-cloud').hidden === true`), 'and `play the set` closed the panel like a tap does');
}
await click('.rm-list .rm-btn[data-id=race]');
for (let i = 0; i < 60; i++) {
  const st = await json(`window.__race.cloud.state`);
  if (st.at === 1 && st.busy === false) break;
  await sleep(250);
}
{
  const st = await json(`window.__race.cloud.state`);
  ok(st.at === 1, 'the end of the first level rolls the lap on to the second');
  const held = await json(`window.__race.race.track.name`);
  ok(held === 'The Second One', 'and the run holds the next one: ' + JSON.stringify(held));
}

/* ---- 7. the paste box is still under there ------------------------------- */
ok(await bootAt(`${site}/dtrh/race.html?cloud=1&intro=0&cards=0&${LIST}`), 'the page boots a third time');
await click('.rm-list .rm-btn[data-id=cloud]');
await sleep(500);
ok(await ev(`document.querySelector('.rm-cloud-paste').hidden === true`), 'lane W1\'s paste box starts collapsed');
await click('.rm-levels-tail .rm-btn[data-id=paste]');
await sleep(300);
ok(await ev(`!document.querySelector('.rm-cloud-paste').hidden`), '`or paste a link` opens it under the list');
ok(await ev(`!!document.querySelector('.rm-cloud-paste .rm-cloud-in')`), 'and the box itself is the one lane W1 built');

/* ---- 8. the desktop host: a door, not a download -------------------------- */
ok(await bootAt(`${site}/dtrh/race.html?cloud=1&trackpick=1&intro=0&cards=0&${LIST}`), 'the page boots claiming a desktop host');
ok(await ev(`window.__race.cloud === null`), 'the mini-player is never built there, so this page can load no audio at all');
await click('.rm-list .rm-btn[data-id=cloud]');
await sleep(500);
{
  const r = await levelRows();
  ok(r.length === 2, 'the same two rows are on the screen');
  ok(await ev(`!document.querySelector('.rm-levels-tail .rm-btn[data-id=set]')`), 'with no `play the set`: the desktop owns the playlist');
}
const before = asked.length;
await click('.rm-levels .rm-level-btn[data-id=lv-1]');
await sleep(700);
{
  const sent = logs.filter((l) => l.indexOf('cloud-open') >= 0).pop() || '';
  ok(sent.indexOf('"url":"https://bambicloud.com/file/stub-one"') >= 0, 'a tap posts cloud-open with the track\'s PAGE url: ' + sent.slice(0, 120));
  ok(logs.some((l) => l.indexOf('press play over there') >= 0), 'and the player is told to press play over there');
  const after = asked.slice(before).filter((u) => /\.wav|\.mp3/.test(u));
  ok(after.length === 0, 'and not one byte of audio was asked for on this page' + (after.length ? ': ' + after.join(', ') : ''));
}

/* ---- 9. nothing left this machine ---------------------------------------- */
{
  const away = asked.filter((u) => u.indexOf('http') === 0 && u.indexOf('127.0.0.1') < 0 && u.indexOf('localhost') < 0);
  ok(away.length === 0, 'not one request left localhost in the whole run' + (away.length ? ': ' + away.slice(0, 3).join(', ') : ''));
  ok(!hits.has('/dtrh/race/levels.json'), 'and the shipped level list was never read: this whole check ran on its own');
}

await done(fails ? 1 : 0);

async function done(code) {
  try { ws && ws.close(); } catch (e) { /* never opened */ }
  try { chrome.kill(); } catch (e) { /* already gone */ }
  await new Promise((r) => server.close(r));
  await sleep(300);
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* chrome still has a lock */ }
  if (code) console.error(`\n${fails} failure${fails === 1 ? '' : 's'}`);
  else console.log('\nlevels-check: all good');
  process.exit(code);
}
