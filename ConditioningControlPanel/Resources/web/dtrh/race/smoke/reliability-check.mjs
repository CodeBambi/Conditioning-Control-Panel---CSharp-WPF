/** Browser regressions for overlapping pause owners, Escape, held Start and obsolete dialogs.
 * Uses a temporary localhost demo and headless Chrome, never the desktop app. */
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const WEB = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');   // .../Resources/web
const PORT = 8857, DEBUG = 9357;
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
const prof = mkdtempSync(join(tmpdir(), 'race-reliability-'));
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
let id = 0; const waits = new Map(); let frames = []; const errors = [];
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Runtime.exceptionThrown') errors.push(m.params.exceptionDetails.text);
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
const RUN = 'window.__race.race', PERF = RUN + '.perf()';
const BRAKE = "!!document.querySelector('.rh-screen.is-on .rh-btn')";
const esc = () => ev("window.dispatchEvent(new KeyboardEvent('keydown',{code:'Escape',key:'Escape',bubbles:true}))");
await cdp('Runtime.enable'); await cdp('Page.enable');
await cdp('Page.navigate', {url: 'http://127.0.0.1:'+PORT+'/dtrh/race.html?intro=0&cards=0&chart=demo'});
ok(await until("!!window.__race && !!window.__race.race && !!window.__race.race.track"), 'demo chart is loaded');
ok(await until("!!window.__race.menu && !document.querySelector('.rm-root').hidden && document.querySelector('#race-splash').hidden"), 'menu is ready after the splash');
await ev("document.querySelector('.rm-btn[data-id=race]').click()");
ok(await until(PERF+'.running'), 'run starts');
await ev(RUN+".audio.sfx('surface',0.1)");
ok(await ev(RUN+'.audio._voices.some(v=>v.src.buffer && !v.contact)'), 'decoded sample playback still starts and is not tagged as contact');
await ev(RUN+".audio.kartBeat({type:'landing',impact:1,clean:true})");
ok(await ev(RUN+'.audio._voices.filter(v=>v.contact).length === 2'), 'landing contact has one thump and a short surface puff');
await sleep(300);
ok(await ev(RUN+'.audio._voices.filter(v=>v.contact).length === 0'), 'contact audio settles promptly');
await ev(RUN+".audio.kartBeat({type:'driftBoost',tier:3});"+RUN+'.setPaused(true)');
await sleep(60);
ok(await ev(RUN+'.audio._voices.filter(v=>v.contact).length === 0'), 'pause cancels an unfinished contact sound');
await ev(RUN+'.setPaused(false)');
await esc(); ok(await until(PERF+'.paused'), 'Escape opens Brake');
const stopped = await ev(PERF+'.elapsed'); await sleep(300);
ok(await ev(PERF+'.elapsed') === stopped, 'paused simulation does not move');
await esc(); ok(await until('!'+PERF+'.paused'), 'Escape resumes');

await esc(); await until(PERF+'.paused');
await ev(RUN+'.setPaused(true);'+RUN+'.setPaused(false)');
ok(await ev(PERF+'.paused && '+PERF+'.trackPaused'), 'host video ending cannot resume Brake or track');
await ev(RUN+'.setPaused(true)'); await esc(); await sleep(100);
ok(await ev(PERF+'.paused && '+PERF+'.trackPaused'), 'closing Brake cannot resume active host pause');
await ev(RUN+'.setPaused(false)');
ok(await until('!'+PERF+'.paused && !'+PERF+'.trackPaused'), 'last pause owner resumes road and track');

await ev("window.__pad={connected:true,axes:[0],buttons:Array.from({length:10},()=>({value:0}))}; Object.defineProperty(navigator,'getGamepads',{configurable:true,value:()=>[window.__pad]}); window.__pad.buttons[9].value=1");
ok(await until(PERF+'.paused'), 'gamepad Start opens Brake');
await sleep(200); ok(await ev(PERF+'.paused'), 'held Start does not immediately resume');
await ev('window.__pad.buttons[9].value=0'); await sleep(100);
await ev('window.__pad.buttons[9].value=1');
ok(await until('!'+PERF+'.paused'), 'Start is still polled under Brake and resumes');
await sleep(200); ok(await ev('!'+PERF+'.paused'), 'resume flush does not reinterpret held Start');
await ev('window.__pad.buttons[9].value=0');

// A hidden page adds Brake even if native video already holds the host pause.
await ev(RUN+".setPaused(true); Object.defineProperty(document,'hidden',{configurable:true,value:true}); document.dispatchEvent(new Event('visibilitychange')); "+RUN+'.setPaused(false)');
ok(await ev(PERF+'.paused && '+PERF+'.trackPaused'), 'screen hiding during video keeps audio stopped after video ends');
await ev("delete document.hidden; document.dispatchEvent(new Event('visibilitychange'))");
await esc(); await until('!'+PERF+'.paused');

// Resolve the old dialog only after a new run and new Brake exist in the same turn.
await esc(); await until(PERF+'.paused');
await ev(RUN+".hud.setPaused(false); "+RUN+".reseed(7); "+RUN+".start(); window.dispatchEvent(new KeyboardEvent('keydown',{code:'Escape',key:'Escape',bubbles:true}))");
await sleep(150);
ok(await ev(PERF+'.paused'), 'obsolete Brake continuation cannot resume a new run');
await esc(); ok(await until('!'+PERF+'.paused'), 'new Brake still resumes normally');
ok(errors.length===0, 'no uncaught browser exceptions: '+errors.join(', '));
ws.close(); chrome.kill(); server.close();
try { rmSync(prof, {recursive:true, force:true}); } catch(e) { /* Chrome may still hold its temporary profile. */ }
console.log(fails ? 'reliability-check: '+fails+' failed' : 'reliability-check: all good');
process.exit(fails ? 1 : 0);
