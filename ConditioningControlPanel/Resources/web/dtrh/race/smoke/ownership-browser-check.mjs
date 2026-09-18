/** Live ownership filtering and native keyboard focus, using the real menu and catalogue.
 * Audio is never loaded. External URLs are blocked; ports 8868 and 9368 are isolated. */
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const WEB = resolve(fileURLToPath(new URL('../../../', import.meta.url)));   // .../Resources/web
const PORT = 8868, DEBUG = 9368;
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };

if (!existsSync(CHROME)) { console.error('no chrome at ' + CHROME + ' (set CHROME_PATH)'); process.exit(1); }
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(req.url.split('?')[0]);
  if (path === '/ownership.html') { res.writeHead(200, {'content-type':'text/html'}); return res.end(`<!doctype html><html><head><link rel="stylesheet" href="/dtrh/race/menu.css"><script type="importmap">{"imports":{"three":"/dtrh/vendor/three/three.module.min.js","three/addons/":"/dtrh/vendor/three/addons/"}}</script></head><body><div id="race-root"></div></body></html>`); }
  if (path.indexOf('..') >= 0) { res.writeHead(400); return res.end(); }
  try {
    let body = await readFile(join(WEB, path));
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
    if (m.params.type==='error') console.log('BROWSER '+line);
    if (line.indexOf('[race->host]') === 0) frames.push(line.slice(13));
  }
};
const cdp = (method, params) => new Promise((res) => { const i = ++id; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
async function until(expr, tries = 80) {
  for (let i = 0; i < tries; i++) { await sleep(250); if (await ev(expr)) return true; }
  return false;
}
await cdp('Runtime.enable'); await cdp('Page.enable'); await cdp('Network.enable');
await cdp('Network.setBlockedURLs',{urls:['https://*','http://bambicloud.com/*']});
await cdp('Page.navigate',{url:`http://127.0.0.1:${PORT}/ownership.html`});
await sleep(200); await cdp('Page.bringToFront'); await cdp('Emulation.setFocusEmulationEnabled',{enabled:true});
await ev(`(async()=>{
 const [{createLevels,parseSets},{createMenu}]=await Promise.all([import('/dtrh/race/levels.js'),import('/dtrh/race/menu.js')]);
 const sets=parseSets(await (await fetch('/dtrh/race/levels.json')).json());
 window.expected=(nums)=>sets[0].levels.filter(l=>nums.includes(l.trackNum)).map(l=>l.title);
 window.build=(owned)=>{
 if(window.mm)mm.dispose();document.getElementById('race-root').innerHTML='';window.opened=[];
 const settings={cloud:true,trackPick:true,racingTracks:owned};
 window.ll=createLevels({settings,sets,hooks:{open:(url,title)=>opened.push(title)},store:null});
 window.mm=createMenu({root:document.getElementById('race-root'),renderer:null,pixel:{block:0,setBlock(n){this.block=n},retexture(){},render(){}},audio:{ui(){},menu(){},setLevels(){}},settings,levels:ll});
 mm.show();document.querySelector('.rm-list [data-id=cloud]').click();
 };
 window.visibleTitles=()=>Array.from(document.querySelectorAll('.rm-level-title')).map(b=>b.textContent);
 window.key=(code)=>window.dispatchEvent(new KeyboardEvent('keydown',{code,bubbles:true}));
})()`);
for(const owned of [[0],[1,2,3],[]]) {
 await ev(`build(${JSON.stringify(owned)})`);
 ok(await ev(`JSON.stringify(visibleTitles())===JSON.stringify(expected(${JSON.stringify(owned)}))`),'initial ownership '+JSON.stringify(owned));
}
await ev('build([0]);ll.setOwnership([1,2,3])');
ok(await ev('JSON.stringify(visibleTitles())===JSON.stringify(expected([1,2,3]))'),'live replacement excludes unowned demo');
ok(await ev("!document.querySelector('.rm-cloud').hidden && document.querySelectorAll('.rm-cloud .is-focus').length===1"),'open panel rebuild keeps one navigation focus');
await ev("key('ArrowDown');key('Enter')");
ok(await ev('opened[0]===expected([2])[0]'),'arrow selection after rebuild opens owned second row');
await ev("document.querySelector('.rm-list [data-id=cloud]').click();ll.setOwnership([0,1,2,3,4,5,6])");
ok(await ev('JSON.stringify(visibleTitles())===JSON.stringify(expected([0,1,2,3,4,5,6]))'),'live additions rebuild all unlocked rows');
await ev("for(let i=0;i<8;i++)key('ArrowDown');ll.setOwnership([])");
ok(await ev("visibleTitles().length===0 && document.querySelector('.rm-cloud .is-focus')?.dataset.id==='back'"),'explicit empty ownership removes all tracks and clamps focus to back');
await ev("key('Enter')");
ok(await ev("document.querySelector('.rm-cloud').hidden && !document.querySelector('.rm-list').hidden"),'empty panel back remains operable');
await ev("build([1,2,3]);ll.setOwnership([1,2,3,4]);document.querySelectorAll('.rm-level-btn')[2].focus();key('Enter')");
ok(await ev('opened[0]===expected([3])[0]'),'Tab-style native focus then Enter chooses focused row after rebuild');
console.log(JSON.stringify({errors,fails}));
ws.close();chrome.kill();server.close();process.exit(fails||errors.length?1:0);
