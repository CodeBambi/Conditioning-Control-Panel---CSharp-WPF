/* ============================================================================
 * race/smoke/menu-return-check.mjs - headless self-check for THE WAY BACK: the
 * End screen's `surface` puts the menu back instead of closing the page.
 *
 *   node race/smoke/menu-return-check.mjs      (exits 0 on pass, 1 with a count)
 *
 * Nothing is stubbed and nothing leaves localhost: this drives the shipped page
 * the way a player does - menu -> `race` -> Esc (the Brake) -> `end the run` ->
 * the End card -> `surface` - and holds what has to be true on the other side.
 *
 * What it holds:
 *   1. the End card's `surface` brings the MENU back: the column is on the
 *      screen, the hud is in the lobby again, the menu stage has the frame and
 *      the world was dropped
 *   2. nothing posts `exit` on that path (only the menu's own verb and the
 *      host's exit-request may), and `run-ended` was sent exactly once
 *   3. `race` fires again from that menu and rebuilds the world it dropped
 *   4. a charted run comes back with its track still loaded and its clock at
 *      zero, so taking that same level again is a replay
 *
 * CHROME: `CHROME_PATH` if it is set, else the usual Windows install. Nothing is
 * installed by this file and the profile it makes is deleted on the way out.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const WEB = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');   // .../Resources/web
const PORT = 8853, DEBUG = 9349;
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };

if (!existsSync(CHROME)) { console.error('no chrome at ' + CHROME + ' (set CHROME_PATH)'); process.exit(1); }
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(req.url.split('?')[0]);
  if (path.indexOf('..') >= 0) { res.writeHead(400); return res.end(); }
  try {
    const body = await readFile(join(WEB, path));
    res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' });
    res.end(body);
  } catch (e) { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));
const prof = mkdtempSync(join(tmpdir(), 'race-return-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DEBUG}`, `--user-data-dir=${prof}`,
  '--no-first-run', '--no-default-browser-check', '--disable-gpu', '--mute-audio', '--enable-unsafe-swiftshader',
  '--autoplay-policy=no-user-gesture-required', '--window-size=1280,720', 'about:blank'], { stdio: 'ignore' });
let page = null;
for (let i = 0; i < 60 && !page; i++) {
  await sleep(250);
  try { page = (await (await fetch(`http://127.0.0.1:${DEBUG}/json/list`)).json()).find((t) => t.type === 'page'); } catch (e) { /* not up yet */ }
}
if (!page) { console.error('chrome never answered'); chrome.kill(); server.close(); process.exit(1); }
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r) => { ws.onopen = r; });
let id = 0; const waits = new Map(); let frames = [];
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Runtime.consoleAPICalled') {
    const line = m.params.args.map((a) => a.value ?? a.description ?? '?').join(' ');
    if (line.indexOf('[race->host]') === 0) frames.push(line.slice(13));
  }
};
const cdp = (method, params) => new Promise((res) => { const i = ++id; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
async function until(expr, tries = 80) {
  for (let i = 0; i < tries; i++) { await sleep(250); if (await ev(expr)) return true; }
  return false;
}
const sent = (type) => frames.filter((f) => f.indexOf(`"type":"${type}"`) >= 0).length;
const MENU_UP = `!!(window.__race && window.__race.menu) && !document.querySelector('.rm-root').hidden`;
const RUNNING = `!!(window.__race.race.perf().running)`;
const CARD = `!!document.querySelector('.rh-screen.is-on')`;
const END_CARD = `(()=>{const s=document.querySelector('.rh-screen.is-on');return !!s && !!s.querySelector('.rh-rows');})()`;
const press = (label) => ev(`[...document.querySelectorAll('.rh-screen.is-on .rh-btn')].find((b)=>b.textContent===${JSON.stringify(label)}).click()`);
/** menu -> race -> the Brake -> end the run -> the End card. */
async function runToTheEndCard() {
  await ev(`document.querySelector('.rm-btn[data-id=race]').click()`);
  if (!await until(RUNNING)) return false;
  await sleep(1200);
  await ev(`window.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',code:'Escape',bubbles:true}))`);
  if (!await until(CARD, 20)) return false;
  await press('end the run');
  return until(END_CARD, 40);
}
await cdp('Runtime.enable'); await cdp('Page.enable');
await cdp('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false, screenWidth: 1280, screenHeight: 720 });

console.log('1-3. the seeded run: the End card sends the page back to the menu');
await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/dtrh/race.html?intro=0&cards=0` });
ok(await until(MENU_UP), 'the menu is the resting state');
ok(await runToTheEndCard(), 'a run, the Brake, and the End card is up');
ok(await ev(`window.__race.race.perf().running === false`), 'the run is over');
await press('surface');
ok(await until(MENU_UP, 40), 'and `surface` brings the menu back instead of closing the page');
ok(await ev(`document.querySelector('.race-hud').classList.contains('is-lobby')`), 'the hud is back in the lobby');
ok(await ev(`window.__race.race.perf().stage === true`), 'the menu stage has the frame again');
ok(await ev(`window.__race.race.perf().world === false`), 'and the world was dropped, not left standing');
ok(await ev(`!document.querySelector('.rh-screen.is-on')`), 'no card is left over the menu');
ok(sent('run-ended') === 1, 'run-ended was sent exactly once for that run');
ok(sent('exit') === 0, 'and nothing posted exit: only the menu verb and the host may');
await ev(`document.querySelector('.rm-btn[data-id=race]').click()`);
ok(await until(RUNNING, 60), '`race` fires again from that menu');
ok(await ev(`window.__race.race.perf().world === true`), 'and prepare() rebuilt the world it dropped');

console.log('4. a charted run comes back with its track re-armed at zero');
frames = [];
await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/dtrh/race.html?intro=0&cards=0&chart=demo&dur=90` });
ok(await until(MENU_UP), 'the menu is up with the demo chart loaded');
ok(await ev(`!!window.__race.race.track`), 'the track is on the run');
ok(await runToTheEndCard(), 'a charted run, ended from the Brake');
ok(await ev(`window.__race.race.track.t > 1`), 'its clock ran');
await press('surface');
ok(await until(MENU_UP, 40), 'the menu is back');
ok(await ev(`!!window.__race.race.track`), 'the track is still loaded on the menu');
ok(await ev(`window.__race.race.track.t === 0`), 'and its clock is back at zero: the same level plays again');
ok(await ev(`(()=>{const p=document.querySelector('.rm-track');return !!p && !p.hidden;})()`), 'the plate still names it');
await ev(`document.querySelector('.rm-btn[data-id=race]').click()`);
ok(await until(RUNNING, 60), 'the second take starts');
await sleep(2000);
ok(await ev(`window.__race.race.track.t > 1`), 'and the clock runs with it');

ws.close(); chrome.kill(); server.close();
try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* the profile is locked, windows will take it */ }
console.log(fails ? `\nmenu-return-check: ${fails} failed` : '\nmenu-return-check: all good');
process.exit(fails ? 1 : 0);
