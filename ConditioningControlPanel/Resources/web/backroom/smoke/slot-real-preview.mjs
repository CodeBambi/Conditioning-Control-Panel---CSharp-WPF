// Real preview layout check. Set SHARED_SLOT_URL or SLOT_LAYOUT_ROOT to a preview host.
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

let fails = 0;
const ok = (c, what) => { if (!c) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const HERE = resolve(fileURLToPath(import.meta.url), '..');
if (!process.env.SLOT_LAYOUT_ROOT && !process.env.SHARED_SLOT_URL) throw new Error('Set SLOT_LAYOUT_ROOT or SHARED_SLOT_URL to the real preview host.');
const WEB = resolve(process.env.SLOT_LAYOUT_ROOT || resolve(HERE, '../..'));
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = Number(process.env.SHARED_SLOT_PORT || 8947), DEBUG_PORT = PORT + 500;
const PAGE_URL = process.env.SHARED_SLOT_URL || `http://127.0.0.1:${PORT}/backroom/index.html`;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

const prof = mkdtempSync(join(tmpdir(), 'backroom-shared-slot-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`, '--no-first-run',
  '--no-default-browser-check', '--mute-audio', '--hide-scrollbars', '--window-size=390,844', '--ignore-gpu-blocklist', '--enable-unsafe-swiftshader',
  '--autoplay-policy=no-user-gesture-required', 'about:blank'], { stdio: 'ignore' });
async function done(code) { try { chrome.kill(); } catch { /* our own child only */ } server.close(); await sleep(500); try { rmSync(prof, { recursive: true, force: true }); } catch { /* noop */ } process.exit(code); }
let target = null;
for (let i = 0; i < 60 && !target; i++) { await sleep(250); try { target = (await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/list`)).json()).find((t) => t.type === 'page'); } catch { /* not up */ } }
if (!target) { console.error('FAIL chrome never answered'); await done(1); }
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((r) => { ws.onopen = r; });
let msgId = 0;
const waits = new Map(), errs = [];
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Runtime.exceptionThrown') errs.push(m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text);
  if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') errs.push('console: ' + m.params.args.map((a) => a.value || a.description).join(' '));
};
const cdp = (method, params) => new Promise((res) => { const i = ++msgId; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
await cdp('Runtime.enable'); await cdp('Page.enable');


const shot = async (name) => { const r = await cdp('Page.captureScreenshot', { format: 'png' }); await writeFile(join(OUT, name), Buffer.from(r.result.data, 'base64')); console.log('  shot  ' + name); };
async function until(expr, ms = 15000, step = 50) { const t = Date.now(); while (Date.now() - t < ms) { if (await ev(expr)) return true; await sleep(step); } return false; }
const metrics = (width, height) => cdp('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: true, screenOrientation: { type: width > height ? 'landscapePrimary' : 'portraitPrimary', angle: width > height ? 90 : 0 } });
const pt = (id, x, y) => ({ id, x, y, radiusX: 4, radiusY: 4, force: 1 });
const send = async (type, points) => { const r = await cdp('Input.dispatchTouchEvent', { type, touchPoints: points }); if (r.error) throw new Error(JSON.stringify(r.error)); };
const tap = async (x, y) => { await send('touchStart', [pt(9, x, y)]); await send('touchEnd', []); };
const state = () => ev('window.__backroom.scene.debug()');
const opens = () => ev(`window.__posted.filter((m) => m.type === 'station-open' && m.station === 'slot').length`);
const goTo = async (key) => { await ev(`window.__backroom.scene.go(window.__backroom.stations.find((s) => s.key === ${JSON.stringify(key)}))`); await until('!window.__backroom.scene.transitioning', 6000); await sleep(120); };
// Back only with a station open: an empty room's Back leaves the room (Law VI), which is not this check's business.
const leave = async () => { if (!(await ev('!!window.__backroom.loader.current || window.__backroom.scene.seated || window.__backroom.scene.transitioning'))) return; await ev('window.__backroom.back(); true'); ok(await until('!window.__backroom.loader.current && !window.__backroom.scene.transitioning && !window.__backroom.scene.seated && window.__backroom.scene.debug().running', 8000), 'Back returns to walking'); await sleep(150); };
const report = { views: [], errors: errs };
try {
  for(const [width,height] of [[1280,720],[390,844],[844,390]]) {
    await metrics(width,height);
    await cdp('Page.navigate',{url:PAGE_URL});
    ok(await until("document.documentElement.classList.contains('br-ready')",60000,100),'real preview boots '+width);
    await goTo('slot:rose');
    await ev("window.__backroom.visit(window.__backroom.stations.find(s=>s.key==='slot:rose'));true");
    ok(await until("document.querySelector('.slot-station')?.dataset.phase==='play'&&!window.__backroom.scene.transitioning",25000,100),'real preview slot ready '+width);
    await sleep(500);
    const controls=await ev(`(()=>[...document.querySelectorAll('.slot-station button,.slot-odds summary,.br-nav>button,.br-sp,#__opt-btn')].filter(n=>n.getClientRects().length&&getComputedStyle(n).visibility!=='hidden').map(n=>{const r=n.getBoundingClientRect(),hit=document.elementFromPoint(r.x+r.width/2,r.y+r.height/2);return {text:n.textContent.trim(),id:n.id,rect:{x:r.x,y:r.y,w:r.width,h:r.height},unobscured:n.matches('button,summary')?n===hit||n.contains(hit):true}}))()`);
    ok(controls.every(c=>c.rect.x>=0&&c.rect.y>=0&&c.rect.x+c.rect.w<=width+1&&c.rect.y+c.rect.h<=height+1),'real host controls fit '+width);
    ok(controls.every(c=>c.unobscured),'real host controls clickable '+width+' '+JSON.stringify(controls.filter(c=>!c.unobscured)));
    report.views.push({width,height,controls});
    await shot('real-preview-'+width+'.png');
    await leave();
  }
} catch(e){ok(false,e.stack||String(e));}
ok(errs.length===0,'no real preview errors '+JSON.stringify(errs));
await writeFile(join(OUT,'slot-real-preview.json'),JSON.stringify({...report,fails},null,2));
await done(fails?1:0);