/* Real room camera checks: responsive game framing, continuous arrival, cancellation and return. */

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { readFileSync, mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

let fails = 0;
const ok = (c, what) => { if (!c) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const HERE = resolve(fileURLToPath(import.meta.url), '..');
const BACKROOM = resolve(HERE, '..');
const WEB = resolve(BACKROOM, '..');
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = 8934, DEBUG_PORT = 9434;

const REGISTRY = JSON.parse(readFileSync(join(BACKROOM, 'stations.json'), 'utf8'));
const LIVE = REGISTRY.filter((s) => s.state === 'live');

const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { if(path.endsWith('/cards/station.js'))await sleep(400); const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

// One fake host for the whole room: a mock server per station, created on its first request.
const FAKE_HOST = `(() => {
  const listeners = [], emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  const gates = { flash: true, subliminal: true, spiral: true, brainDrain: true, tunnel: true };
  const made = {};
  // The floor bell is not a station (10.16.B): the room reads it, and its mock lives in smoke/.
  const mock = (id) => made[id] || (made[id] = import(id === 'bell' ? '/backroom/smoke/mock-bell.js' : '/backroom/stations/' + id + '/mock-server.js').then((m) => {
    const s = id === 'bell' ? m.createBellMock({}) : id === 'cards' ? m.createMockServer({ sp: 57, floorMs: 600 }) : id === 'roulette' ? m.createMockServer({ sp: 57, floorMs: 0 })
      : id === 'counter' ? m.createMockServer({ sp: 57, on: 'jackpot_remix,rt_demo,high_roller' }) : m.createMockServer({ sp: 57 });
    if (id === 'wheel') s.script('deep');
    if (id === 'cards') s.script('Th', '9d', '8c', '8s');
    if (id === 'roulette') s.script({ pocket: 36 });
    window.__servers[id] = s; return s; }));
  window.__hostEmit = emit; window.__posted = []; window.__servers = {};
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(type, fn) { if (type === 'message') listeners.push(fn); },
    postMessage(m) {
      window.__posted.push(JSON.parse(JSON.stringify(m)));
      if (m.type === 'ready') emit({ type: 'init', protocol: 1, sp: 57, reduced: false, motion: 'full', intensity: 'normal', lang: 'en', gates,
        lex: { br_back: 'Back', br_balance: 'SP' }, stations: ['slot', 'wheel', 'cards', 'roulette', 'counter', 'bell'], open: true });
      if (m.type === 'station-request') mock(m.station).then((s) => s.handle(m.op, m.body || {}, m.idem))
        .then((r) => emit({ type: 'station-result', reqId: m.reqId, ok: r.ok, status: r.status, reason: r.reason, body: r.body || {} }));
      if (m.type === 'media-request') emit({ type: 'media', reqId: m.reqId, seed: 1, words: [],
        gifs: Array.from({ length: m.count || 4 }, (_, i) => ({ key: 'g' + i, url: '/backroom/stations/slot/fallback/gif' + (i % 4) + '.webp', w: 180, h: 180, src: 'pool' })) });
      if (m.type === 'fx') emit({ type: 'fx-ack', token: m.token, fired: [m.fxId], skipped: [] });
    },
  };
})();`;

const prof = mkdtempSync(join(tmpdir(), 'backroom-room-stations-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`, '--no-first-run',
  '--no-default-browser-check', '--mute-audio', '--hide-scrollbars', '--window-size=1280,720', '--ignore-gpu-blocklist', '--enable-unsafe-swiftshader',
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
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: FAKE_HOST });
await cdp('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });
const shot = async (name) => { const r = await cdp('Page.captureScreenshot', { format: 'png' }); await writeFile(join(OUT, name), Buffer.from(r.result.data, 'base64')); console.log('  shot  ' + name); };
const posted = (type) => ev(`window.__posted.filter((m) => m.type === ${JSON.stringify(type)})`);
async function until(expr, ms = 15000, step = 50) { const t = Date.now(); while (Date.now() - t < ms) { if (await ev(expr)) return true; await sleep(step); } return false; }
const key = async (code, type = 'keyDown') => cdp('Input.dispatchKeyEvent', { type, code, key: code.slice(3).toLowerCase(), windowsVirtualKeyCode: code.charCodeAt(3) });
async function click(x, y) {
  await cdp('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y });
  await cdp('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'left', buttons: 1, clickCount: 1 });
  await cdp('Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button: 'left', clickCount: 1 });
}
const clickSel = (sel) => ev(`(() => { const b = document.querySelector(${JSON.stringify(sel)}); if (!b || b.disabled) return false; b.click(); return true; })()`);
const report = { stations: {}, registry: LIVE.map((s) => s.id + (s.variant ? ':' + s.variant : '')) };

/** Every roulette bet target through the room camera, as viewport rects, with what the DOM shows at its centre. */
const RECTS = `(async () => { const T = await import('three'); const s = window.__backroom.scene, mat = s.scene.getObjectByName('roulette_runtime_mat'); if (!mat) return null;
  const r = s.renderer.domElement.getBoundingClientRect(), out = {}, v = new T.Vector3();
  mat.traverse((o) => { if (!o.userData.spot) return; const g = o.geometry.parameters; let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    for (const x of [-g.width / 2, g.width / 2]) for (const y of [-g.height / 2, g.height / 2]) { o.localToWorld(v.set(x, y, 0)).project(s.camera); const px = r.left + (v.x + 1) * r.width / 2, py = r.top + (1 - v.y) * r.height / 2; x0 = Math.min(x0, px); y0 = Math.min(y0, py); x1 = Math.max(x1, px); y1 = Math.max(y1, py); }
    out[o.userData.spot] = { x: x0, y: y0, w: x1 - x0, h: y1 - y0, cover: document.elementFromPoint((x0 + x1) / 2, (y0 + y1) / 2)?.tagName || null }; });
  return out; })()`;
const POCKETS = `(async () => { const T = await import('three'); const s = window.__backroom.scene, f = s.scene.getObjectByName('station_roulette'), r = s.renderer.domElement.getBoundingClientRect(), out = [];
  for (let n = 0; n < 37; n++) { const v = new T.Box3().setFromObject(f.getObjectByName('pocket_' + n)).getCenter(new T.Vector3()).project(s.camera); out.push({ n, x: r.left + (v.x + 1) * r.width / 2, y: r.top + (1 - v.y) * r.height / 2 }); }
  return out; })()`;
/* The seated roulette: a phone frames the mat while bets are open (every target in view, none under the control bar,
 * number cells wide enough for a fingertip), Spin eases out to the whole table and the landing brings the mat frame back.
 * A desktop keeps its single whole-table pose through the spin. */
async function rouletteSeat(width,height){
 const phone=width<=800||height<=500, name='roulette '+width+'x'+height, floor=phone?(width>height?26:20):17;
 ok(await until('!!document.querySelector(".roul-station[data-phase=bet]")',8000),name+': bets open on the room fixture');
 const rects=await ev(RECTS), list=Object.entries(rects||{});
 ok(list.length===42&&!await ev("!!document.querySelector('.roul-mat-strip')"),name+': 42 targets on the 3D mat and no DOM betting grid');
 const inside=list.filter(([,r])=>r.x>=0&&r.y>=0&&r.x+r.w<=width&&r.y+r.h<=height).length, clear=list.filter(([,r])=>r.cover==='CANVAS').length;
 ok(inside===42&&clear===42,name+': every target inside the viewport and under no control ('+inside+' inside, '+clear+' clear)');
 const controls=await ev("(()=>{const b=document.querySelector('.roul-controls').getBoundingClientRect();return {x:b.x,y:b.y,w:b.width,h:b.height};})()");
 const covered=list.filter(([,r])=>r.x<controls.x+controls.w&&r.x+r.w>controls.x&&r.y<controls.y+controls.h&&r.y+r.h>controls.y).map(([k])=>k);
 ok(covered.length===0,name+': the control bar covers no target'+(covered.length?': '+covered.join(', '):''));
 const numbers=list.filter(([k])=>/^s[1-9]/.test(k)), minW=Math.min(...numbers.map(([,r])=>r.w)), minH=Math.min(...numbers.map(([,r])=>r.h));
 ok(minW>=floor&&minH>=floor*.85,name+': number cells at least '+floor+' px wide (min '+minW.toFixed(1)+' x '+minH.toFixed(1)+')');
 report.stations[width+'-roulette-cells']={minW,minH,controls};
 const s36=rects.s36; await click(Math.round(s36.x+s36.w/2),Math.round(s36.y+s36.h/2)); await sleep(150);
 ok(await ev("window.__backroom.scene.scene.getObjectByName('roulette_live_chips')?.count===1"),name+': a tap on 36 lands a chip on the 3D mat');
 await shot(width+'-roulette-bet.png');
 const before=await ev('window.__backroom.scene.debug().position');
 await clickSel('.roul-spin');
 if(phone){
  ok(await until('window.__backroom.scene.transitioning',1500),name+': Spin eases the camera out to the whole table');
  ok(await until('!window.__backroom.scene.transitioning',3000),name+': the table frame arrives');
  const pockets=await ev(POCKETS), run=Object.values(await ev(RECTS)||{});
  ok(pockets.length===37&&pockets.every(p=>p.x>=0&&p.x<=width&&p.y>=0&&p.y<=height),name+': all 37 pockets in view for the landing');
  ok(run.length===42&&run.every(r=>r.x>=0&&r.y>=0&&r.x+r.w<=width&&r.y+r.h<=height),name+': the mat stays in view while the ball runs');
  await sleep(500); await shot(width+'-roulette-run.png');
 } else { await sleep(400); ok(!await ev('window.__backroom.scene.transitioning'),name+': a desktop keeps its pose through the spin'); }
 ok(await until("(document.querySelector('.roul-history')||{}).childElementCount>=1",14000),name+': the ball lands');
 await sleep(400); await shot(width+'-roulette-landing.png');
 if(phone){
  ok(await until('window.__backroom.scene.transitioning',9000),name+': the camera returns to the mat once bets reopen');
  await until('!window.__backroom.scene.transitioning',3000); await sleep(100);
  const after=await ev('window.__backroom.scene.debug().position');
  ok(after.every((v,i)=>Math.abs(v-before[i])<.01),name+': the bet frame is the pose it left');
 } else ok(await until('!!document.querySelector(".roul-station[data-phase=bet]")',9000),name+': bets reopen without a camera move');
}


// An intentionally slow import must leave the last walking frame visible, never a blank layer.
await cdp('Emulation.setDeviceMetricsOverride',{width:400,height:730,deviceScaleFactor:1,mobile:true});
await cdp('Page.navigate',{url:`http://127.0.0.1:${PORT}/backroom/index.html`});
ok(await until("document.documentElement.classList.contains('br-ready')",45000),'continuity room boots');
await ev('window.__backroom.scene.setStill(true)');await sleep(100);await shot('continuity-0-walking.png');
const prior=await ev('window.__backroom.scene.debug()');
await ev("window.__backroom.visit(window.__backroom.stations.find(s=>s.id==='cards'));true");
await sleep(40);await shot('continuity-1-importing.png');
const importing=await ev("({scene:window.__backroom.scene.debug(),background:getComputedStyle(document.querySelector('.br-station')).backgroundColor})");
ok(importing.background==='rgba(0, 0, 0, 0)'&&importing.scene.running&&!importing.scene.held,'pending import preserves visible room renderer');
ok(importing.scene.position.every((v,i)=>v===prior.position[i]),'pending import preserves walking pose');
ok(await until('window.__backroom.scene.transitioning',3000),'arrival follows import');
await sleep(40);await shot('continuity-2-pan-start.png');await sleep(500);await shot('continuity-3-pan-middle.png');
await ev('window.__backroom.back();true');ok(await until('!window.__backroom.scene.transitioning',6000),'continuity return completes');
for(const [width,height] of (process.argv.includes('--phone')?[[400,730]]:process.argv.includes('--landscape')?[[730,400]]:[[400,730],[1280,720],[730,400]])){
 await cdp('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:width<800});
 await cdp('Page.navigate',{url:`http://127.0.0.1:${PORT}/backroom/index.html`});
 ok(await until("document.documentElement.classList.contains('br-ready')",45000),'room boots '+width+'x'+height);
 for(const id of ['wheel','cards','roulette']){
  const before=await ev('window.__backroom.scene.debug()');
  await ev(`window.__backroom.visit(window.__backroom.stations.find(s=>s.id==='${id}'));true`);
  ok(await until('window.__backroom.scene.transitioning',3000),'pan begins '+id);
  ok(!await ev(`!!document.querySelector('.${id==='roulette'?'roul':id==='wheel'?'wheel':'cards'}-station')`),'game waits for arrival '+id);
  await sleep(650);await shot(width+'-'+id+'-moving.png');
  ok(await until('!window.__backroom.scene.transitioning',6000),'pan arrives '+id);
  await sleep(id==='cards'?4700:1300);
  if(id==='cards'){await clickSel('.cards-deal');await sleep(3800);}
  await shot(width+'-'+id+'-seated.png');
  report.stations[width+'-'+id]=await ev('window.__backroom.scene.debug()');
  if(id==='roulette')await rouletteSeat(width,height);
  await ev('window.__backroom.back();true');
  ok(await until('window.__backroom.scene.transitioning',1500),'return pan begins '+id);
  ok(await until('!window.__backroom.scene.transitioning',6000),'return pan finishes '+id);
  const after=await ev('window.__backroom.scene.debug()');
  ok(after.position.every((v,i)=>Math.abs(v-before.position[i])<.01)&&Math.abs(after.yaw-before.yaw)<.001,'return restores walking pose '+id);
 }
}
// Back cancels arrival before a station can mount or send station-open.
await ev("window.__backroom.visit(window.__backroom.stations.find(s=>s.id==='wheel'));true");
ok(await until('window.__backroom.scene.transitioning',3000),'cancellable arrival starts');
await ev('window.__backroom.back();true');
ok(await until('!window.__backroom.scene.transitioning',6000),'early Back finishes return');
await sleep(300);ok(await ev("!window.__backroom.loader.current&&!document.querySelector('.wheel-station')"),'canceled arrival never opens later');
await ev("window.__backroom.state.motion='off'");
await ev("window.__backroom.visit(window.__backroom.stations.find(s=>s.id==='wheel'));true");
await sleep(100);ok(!await ev('window.__backroom.scene.transitioning'),'Motion off has no arrival wait');
await ev('window.__backroom.back();true');await sleep(100);
ok(!await ev('window.__backroom.scene.transitioning'),'Motion off has no return wait');
await ev("window.__backroom.state.motion='full'");
for(const id of ['slot','counter']){
 const count=await ev("window.__posted.filter(m=>m.type==='station-open').length");
 await ev(`window.__backroom.visit(window.__backroom.stations.find(s=>s.id==='${id}'));true`);
 ok(await until('window.__backroom.scene.transitioning',3000),'legacy approach pan '+id);
 ok(await until(`window.__posted.filter(m=>m.type==='station-open').length>${count}`,10000),'legacy opens after arrival '+id);
 ok(await ev('window.__backroom.scene.held'),'legacy holds room only after arrival '+id);
 await ev('window.__backroom.back();true');
 ok(await until('window.__backroom.scene.transitioning',2000),'legacy return pan '+id);
 ok(await until('!window.__backroom.scene.transitioning',6000),'legacy return completes '+id);
}
ok(!errs.length,'no page exceptions: '+errs.join('\n'));
await writeFile(join(OUT,'camera-check.json'),JSON.stringify({...report,errors:errs,fails},null,2));await done(fails?1:0);
