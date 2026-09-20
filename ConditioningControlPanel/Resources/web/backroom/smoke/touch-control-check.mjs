
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
const WEB = resolve(HERE, '../..');   // Resources/web
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });

const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = Number(process.env.TOUCH_CHECK_PORT || 8924), DEBUG_PORT = PORT + 500;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));
const prof = mkdtempSync(join(tmpdir(), 'backroom-cards-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`, '--no-first-run',
  '--no-default-browser-check', '--mute-audio', '--hide-scrollbars', '--window-size=1280,720', '--ignore-gpu-blocklist', '--enable-unsafe-swiftshader',
  'about:blank'], { stdio: 'ignore' });
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
await cdp('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });

const summary = {};
const dbg = () => ev('window.dev.station.debug()');

/* ---------------------------------------------------------------- 9. through the room */
const PICS = ['/backroom/stations/slot/fallback/gif0.webp', '/dtrh/assets/bubbles/effects/spirals/sp6.gif', '/arcademy/art/bugle/g1.webp', '/backroom/stations/slot/fallback/gif1.webp',
  '/backroom/room/assets/ads/dtrh.webp', '/arcademy/art/bugle/g2.webp', '/backroom/stations/slot/fallback/gif2.webp', '/dtrh/assets/bubbles/effects/spirals/sp7.gif',
  '/arcademy/art/bugle/g3.webp', '/backroom/stations/slot/fallback/gif3.webp', '/backroom/room/assets/ads/arcademy.webp', '/arcademy/art/bugle/g4.webp', '/backroom/room/assets/ads/focus-gaze.webp'];
const FAKE_HOST = `(() => {
  const listeners = [], emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  const PICS = ${JSON.stringify(PICS)};
  window.__posted = []; window.__emit = emit;
  const serverP = import('/backroom/stations/cards/mock-server.js').then((m) => { const s = m.createMockServer({ sp: 57, floorMs: 600 }); s.script('Th', '9d', '8c', '8s'); window.__server = s; return s; });
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(type, fn) { if (type === 'message') listeners.push(fn); },
    postMessage(m) {
      window.__posted.push(m);
      if (m.type === 'ready') emit({ type: 'init', protocol: 1, sp: 57, reduced: false, motion: 'full', intensity: 'normal', lang: 'en', open: null,
        gates: { flash: true, subliminal: true, spiral: true, brainDrain: true, tunnel: true }, lex: { br_back: 'Back', br_balance: 'SP' }, stations: ['slot', 'wheel', 'cards'] });
      if (m.type === 'station-request') serverP.then((s) => s.handle(m.op, m.body, m.idem)).then((r) => emit({ type: 'station-result', reqId: m.reqId, ok: r.ok, status: r.status, reason: r.reason, body: r.body || {} }));
      if (m.type === 'media-request') { const n = m.count || 4; emit({ type: 'media', reqId: m.reqId, seed: 1, words: [], gifs: PICS.slice(0, n).map((url, i) => ({ key: 'g' + i, url, w: 0, h: 0, src: 'pool' })) }); }
      if (m.type === 'fx') emit({ type: 'fx-ack', token: m.token, fired: [m.fxId], skipped: [] });
    },
  };
})();`;
const until = async (expr, ms = 12000) => { for (let i = 0; i < ms / 50; i++) { if (await ev(expr)) return true; await sleep(50); } return false; };
const click = (selector) => ev(`document.querySelector(${JSON.stringify(selector)}).click()`);
const shot = async (name) => { const r = await cdp('Page.captureScreenshot', { format: 'png' }); await writeFile(join(OUT, name), Buffer.from(r.result.data, 'base64')); };
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: FAKE_HOST });
const phone = true;
await cdp('Emulation.setTouchEmulationEnabled', {enabled:true,maxTouchPoints:2});
if (phone) {
  await cdp('Emulation.setDeviceMetricsOverride', { width: 400, height: 800, deviceScaleFactor: 1, mobile: true });
  await cdp('Emulation.setUserAgentOverride', { userAgent: 'Mozilla/5.0 (Linux; Android 12; Pixel 5) AppleWebKit/537.36 Chrome/120.0 Mobile Safari/537.36' });
  await cdp('Page.addScriptToEvaluateOnNewDocument', { source: "Object.defineProperty(navigator,'deviceMemory',{get:()=>4});Object.defineProperty(navigator,'hardwareConcurrency',{get:()=>4});" });
}
await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/index.html` });
ok(await until("document.documentElement.classList.contains('br-ready')", 45000), 'room boots');

const state=()=>ev('window.__backroom.scene.debug()');
const rect=await ev("(()=>{const r=document.querySelector('.br-thumbstick').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2,w:r.width};})()");
ok((await state()).touch.visible && rect.w>=112&&rect.w<=116,'phone has a visible thumbstick');
const pt=(id,x,y)=>({id,x,y,radiusX:4,radiusY:4,force:1});
const send=async(type,points)=>{const result=await cdp('Input.dispatchTouchEvent',{type,touchPoints:points});if(result.error)throw new Error(JSON.stringify(result.error));return result;};
const stick=(amount)=>pt(1,rect.x,rect.y-36*amount);
async function walk(amount) {
  await ev('window.__backroom.scene.pose([0,1.65,4],0,0)');
  await send('touchStart',[stick(0)]);await send('touchMove',[stick(amount)]);
  await sleep(450);const s=await state();await send('touchEnd',[]);return 4-s.position[2];
}
const half=await walk(.56),full=await walk(1);
ok(half>.3&&full/half>1.6&&full/half<2.5,'analog half throw walks slower than full: '+JSON.stringify({half,full}));
const released=await state();await sleep(180);const stopped=await state();
ok(!released.touch.active&&released.touch.x===0&&released.touch.z===0&&Math.abs(stopped.position[2]-released.position[2])<.03,'release resets movement without drift');
await ev('window.__backroom.scene.pose([0,1.65,4],0,0)');
await send('touchStart',[stick(1)]);await send('touchStart',[stick(1),pt(2,300,310)]);
await send('touchMove',[stick(1),pt(2,350,320)]);await sleep(200);
const both=await state();ok(both.yaw<-.1&&both.position[2]<3.8,'second finger looks while thumbstick walks');
await send('touchMove',[pt(2,350,320)]);const yaw=both.yaw;
await send('touchMove',[pt(2,320,320)]);await sleep(60);
ok((await state()).yaw>yaw+.05,'releasing walking finger leaves drag-look finger captured');await send('touchEnd',[]);
await ev('window.__backroom.scene.pose([0,1.65,-3.5],0,0)');
await send('touchStart',[stick(1)]);await sleep(500);const collision=await state();await send('touchEnd',[]);
ok(collision.position[2]>-4.06 && collision.position[2]<-3.5,'thumbstick uses existing counter collision');
await send('touchStart',[stick(1)]);await send('touchCancel',[]);await sleep(50);
ok(!(await state()).touch.active && (await state()).touch.z===0,'touch cancel clears control');
await send('touchStart',[stick(1)]);await ev("window.dispatchEvent(new Event('blur'))");
ok(!(await state()).touch.active && (await state()).touch.z===0,'blur clears held walking input');await send('touchEnd',[]);
await send('touchStart',[stick(1)]);
await ev("Object.defineProperty(document,'hidden',{configurable:true,value:true});document.dispatchEvent(new Event('visibilitychange'))");
ok(!(await state()).touch.visible&&!(await state()).touch.active,'visibility loss hides and clears walking input');
await send('touchEnd',[]);await ev("delete document.hidden;document.dispatchEvent(new Event('visibilitychange'))");await sleep(80);
for(const [name,enter,leave] of [
 ['held','hold()','release()'],
 ['seated',"seat(window.__backroom.stations.find(s=>s.id==='cards'))",'unseat()'],
 ['customization','customization.open()','customization.dismiss()'],
 ['paused','pause(true)','pause(false)']]) {
 await send('touchStart',[stick(1)]);await ev('window.__backroom.scene.'+enter);await sleep(80);
 const s=await state();ok(!s.touch.visible&&!s.touch.active&&s.touch.z===0,name+' hides and resets thumbstick');
 await send('touchEnd',[]);await ev('window.__backroom.scene.'+leave);await until('!window.__backroom.scene.transitioning');await sleep(80);
 ok((await state()).touch.visible,name+' exit restores walking control');
}
ok(await ev("!document.querySelector('.br-visit') && getComputedStyle(document.querySelector('.br-hint')).display==='none'"),'phone has no Visit button or keyboard instructions');
await ev("window.__backroom.scene.go(window.__backroom.stations.find(r=>r.id==='cards'))");await until('!window.__backroom.scene.transitioning');
const targetPoint=await ev(`(()=>{const s=window.__backroom.scene;for(let y=170;y<650;y+=25)for(let x=30;x<380;x+=25){if(document.elementFromPoint(x,y)?.tagName!=='CANVAS')continue;const h=s.pickAt({clientX:x,clientY:y},s.scene.children).find(h=>{for(let n=h.object;n;n=n.parent)if(!n.visible)return false;return true;});for(let n=h?.object;n;n=n.parent)if(n.name==='station_cards')return {x,y};}return null;})()`);
ok(!!targetPoint,'find exposed card fixture for physical touch');
if(targetPoint){
 await send('touchStart',[pt(5,targetPoint.x,targetPoint.y)]);await send('touchMove',[pt(5,targetPoint.x+25,targetPoint.y+5)]);await send('touchEnd',[]);await sleep(100);
 ok(!await ev('window.__backroom.scene.seated'),'drag over a game does not enter');
 await ev("window.__backroom.scene.go(window.__backroom.stations.find(r=>r.id==='cards'))");await until('!window.__backroom.scene.transitioning');
 await send('touchStart',[pt(6,targetPoint.x,targetPoint.y)]);await send('touchEnd',[]);
 ok(await until('window.__backroom.scene.seated'),'physical game tap starts entry');
 await until('!window.__backroom.scene.transitioning');await shot('phone-direct-cards.png');
 await ev('window.__backroom.back()');await until('!window.__backroom.scene.transitioning && !window.__backroom.scene.seated');
}
await ev('window.__backroom.scene.pose([0,1.65,6.5],0,0)');await sleep(150);await shot('phone-thumbstick.png');
await cdp('Emulation.setTouchEmulationEnabled',{enabled:false});await cdp('Emulation.setDeviceMetricsOverride',{width:1280,height:720,deviceScaleFactor:1,mobile:false});await sleep(100);
ok(!(await state()).touch.visible,'mouse-only desktop hides thumbstick');
ok(!errs.length,'no page exceptions: '+errs.join('\n'));
await writeFile(join(OUT,'touch-check.json'),JSON.stringify({half,full,collision:collision.position,errors:errs,fails},null,2));
await done(fails?1:0);
