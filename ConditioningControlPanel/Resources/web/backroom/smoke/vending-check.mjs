/* ============================================================================
 * backroom/smoke/room-stations-check.mjs - every live station through the REAL room, one page, one host.
 *
 *   node backroom/smoke/room-stations-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * What it proves that the per-station checks cannot: the room reads stations.json and sets out every fixture,
 * the fake host answers init with the stations the C# host whitelists (BackRoomApi.Ops keys: slot, wheel,
 * cards, roulette, counter), and each of the five stations mounts from its stations.json entry through the loader,
 * opens (station-open, hostBack), plays one moment against its own mock-server.js, and closes on the room's
 * Back (station-close, the loader empty, the room loop running again, no tunnel left above 0). The slot here
 * is the REAL slot station, not smoke/mock-station.js.
 *
 * Nothing leaves the machine: Resources/web is served on 127.0.0.1 (ROOM_STATIONS_PORT, default 8931, debug
 * +500). The only process it stops is the Chrome it started, by its own handle.
 * CHROME: CHROME_PATH, else the usual Windows install.
 * ==========================================================================*/

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
const PORT = 8982, DEBUG_PORT = 9482;

const REGISTRY = JSON.parse(readFileSync(join(BACKROOM, 'stations.json'), 'utf8'));
const LIVE = REGISTRY.filter((s) => s.state === 'live');

const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
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



await cdp('Page.addScriptToEvaluateOnNewDocument',{source:`window.__vendingContexts=new Set();const get=HTMLCanvasElement.prototype.getContext;HTMLCanvasElement.prototype.getContext=function(type,...args){const c=get.call(this,type,...args);if(c&&/webgl/.test(type))window.__vendingContexts.add(c);return c;};`});
const reports=[];
for(const [width,height] of [[400,730],[730,400]]){
 await cdp('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:true});await cdp('Page.navigate',{url:`http://127.0.0.1:${PORT}/backroom/index.html`});ok(await until("document.documentElement.classList.contains('br-ready')",45000),'boot '+width+'x'+height);
 await ev('window.__backroom.scene.customization.open()');await sleep(1200);
 ok(await ev('document.querySelectorAll(".br-custom-items button").length===2 && ![...document.querySelectorAll(".br-custom-panel button")].some(b=>/^0?\\d+$/.test(b.textContent))'),'no numbered item picker');
 ok(await ev(`getComputedStyle(document.querySelector('.br-bell')).visibility==='hidden' && getComputedStyle(document.querySelector('.br-nav')).visibility==='visible'`),'ticker hidden and navigation stays visible');
 await shot(width+'-machine.png');
 for(const page of [0,1]){
  await ev(`document.querySelectorAll('.br-custom-items button')[${page}].click()`);await sleep(400);
  ok(await ev(`window.__backroom.scene.customization.debug().view.visibleItems.length===${page?6:9} && window.__backroom.scene.customization.debug().view.visibleItems.every(n=>n.startsWith('${page?'vending_decoration_':'vending_item_'}'))`),'only the active display miniatures are visible');
  for(let i=0;i<(page?6:9);i++){
   const pick=await ev(`window.__backroom.scene.customization.debug().view.picks[${i}]`);
   await cdp('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:pick.x,y:pick.y}]});await cdp('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});await sleep(180);
   ok(await ev(`window.__backroom.scene.customization.debug().view.selected===${page*9+i}`),'actual item tap '+(page*9+i));
   if(i===0||i===4)await shot(width+'-page'+page+'-item'+i+'.png');
  }
 }
 ok(await ev('window.__backroom.scene.customization.getState().props.every(x=>!x)'),'preview never grants or equips locked decorations');
 await ev(`window.__backroom.scene.customization.setOwned(['monstera']);document.querySelector('.br-vending-pick').click();document.querySelector('.br-custom-actions button').click()`);
 ok(await ev('window.__backroom.scene.customization.getState().props[0]'),'owned decoration Use applies');
 await ev(`document.querySelectorAll('.br-custom-items button')[0].click();document.querySelectorAll('.br-vending-pick')[4].click();document.querySelector('.br-custom-more').open=true;document.querySelectorAll('.br-custom-more .br-custom-actions')[1].querySelectorAll('button')[1].click();document.querySelector('.br-custom-hud > .br-custom-actions button').click()`);
 ok(await until('window.__backroom.scene.customization.getState().handles[1]===1'),'placement uses selected queen on Candy Violet');await shot(width+'-placement.png');
 await ev(`document.querySelectorAll('.br-custom-hud > .br-custom-actions button')[1].click()`);ok(await until('window.__backroom.scene.customization.getState().handles[1]===-1'),'Original restores the chosen handle');
 // Lever arrows: the pane pans to the cabinet being modified, and the centred cabinet IS the Placement target.
 const arrowsDbg=()=>ev('window.__backroom.scene.customization.debug().arrows');
 const centredOn=async()=>{const a=await arrowsDbg();const goal=a.slots[a.target];return !a.travel&&!!a.view&&goal&&a.view.look.every((v,i)=>Math.abs(v-goal.look[i])<1e-6)&&a.centred===['Candy Rose','Candy Violet','Candy Mint'][a.target]&&(await ev(`[...document.querySelectorAll('.br-custom-more .br-custom-actions')[1].querySelectorAll('button')].map(b=>b.getAttribute('aria-pressed'))`)).indexOf('true')===a.target;};
 let a=await arrowsDbg();ok(a.visible&&a.target===1&&a.centred==='Candy Violet','arrows show for a lever item, centred on the Placement target');
 ok(a.buttons.length===2&&a.buttons.every(b=>b.w>=44&&b.h>=44&&b.x>=0&&b.y>=0&&b.x+b.w<=width&&b.y+b.h<=height)&&a.buttons.every(b=>width<height?b.y+b.h<=height*.44+1:b.x+b.w<=width*.5+1),'arrow touch targets at least 44px, inside the room pane');
 ok(a.order.length===3&&new Set(a.order).size===3,'three cabinets in screen order '+a.order.join(','));
 const seen=new Set();
 for(let step=0;step<3;step++){await ev(`document.querySelector('.br-custom-arrow.is-next').click()`);await sleep(40);const mid=await arrowsDbg();if(step===0)ok(mid.travel,'next arrow starts an eased pan');
  ok(await until('!window.__backroom.scene.customization.debug().arrows.travel',3000),'pan settles');a=await arrowsDbg();seen.add(a.target);ok(await centredOn(),'next arrow centres slot '+a.target+' and the Placement target follows');if(step===0)await shot(width+'-arrow-next.png');}
 ok(seen.size===3,'next arrow visits every cabinet');
 await ev(`document.querySelector('.br-custom-arrow.is-prev').click()`);ok(await until('!window.__backroom.scene.customization.debug().arrows.travel',3000),'prev pan settles');ok(await centredOn(),'prev arrow centres the previous cabinet');
 await ev(`document.querySelector('.br-custom-arrows').focus()`);const before=(await arrowsDbg()).target;
 await cdp('Input.dispatchKeyEvent',{type:'keyDown',code:'ArrowRight',key:'ArrowRight',windowsVirtualKeyCode:39});await cdp('Input.dispatchKeyEvent',{type:'keyUp',code:'ArrowRight',key:'ArrowRight',windowsVirtualKeyCode:39});await sleep(40);
 ok(await until('!window.__backroom.scene.customization.debug().arrows.travel',3000)&&(await arrowsDbg()).target!==before&&await centredOn(),'ArrowRight on the focused pane pans to the next cabinet');
 await cdp('Input.dispatchKeyEvent',{type:'keyDown',code:'ArrowLeft',key:'ArrowLeft',windowsVirtualKeyCode:37});await cdp('Input.dispatchKeyEvent',{type:'keyUp',code:'ArrowLeft',key:'ArrowLeft',windowsVirtualKeyCode:37});await sleep(40);
 ok(await until('!window.__backroom.scene.customization.debug().arrows.travel',3000)&&(await arrowsDbg()).target===before&&await centredOn(),'ArrowLeft pans back');
 // A finger swipe on the room pane steps cabinets too: left for the next, right for the previous.
 const swipe=async dx=>{await ev(`(()=>{const c=document.querySelector('canvas');const at=(t,x)=>c.dispatchEvent(new PointerEvent(t,{bubbles:true,button:0,pointerId:7,pointerType:'touch',clientX:x,clientY:120}));at('pointerdown',200);at('pointerup',200+(${dx}));})()`);return until('!window.__backroom.scene.customization.debug().arrows.travel',3000);};
 ok(await swipe(-90)&&(await arrowsDbg()).target!==before&&await centredOn(),'swipe left on the pane pans to the next cabinet');
 ok(await swipe(90)&&(await arrowsDbg()).target===before&&await centredOn(),'swipe right pans back');
 ok(await swipe(-20)&&(await arrowsDbg()).target===before,'a short drag is not a swipe');
 await ev(`document.querySelectorAll('.br-vending-pick')[3].click()`);await sleep(40);ok(await until('!window.__backroom.scene.customization.debug().arrows.travel',3000)&&(await arrowsDbg()).target===before&&await centredOn(),'picking another lever item keeps the cabinet in view');
 await ev(`document.querySelectorAll('.br-vending-pick')[4].click()`);await sleep(40);
 ok(await ev('window.__backroom.scene.customization.opened'),'arrow keys leave the sheet open');
 await ev(`document.querySelector('.br-custom-more').open=true;document.querySelectorAll('.br-custom-more .br-custom-actions')[1].querySelectorAll('button')[0].click()`);ok(await until('!window.__backroom.scene.customization.debug().arrows.travel',3000)&&(await arrowsDbg()).target===0&&await centredOn(),'choosing a Placement target pans the pane to it');
 await ev(`document.querySelectorAll('.br-nav .br-pill')[1].click()`);ok(await ev(`document.querySelectorAll('.br-nav .br-pill')[1].getAttribute('aria-pressed')==='true'`),'Motion Off from the room pill');await sleep(60);
 await ev(`document.querySelector('.br-custom-arrow.is-next').click()`);ok(!(await arrowsDbg()).travel&&await centredOn(),'Motion Off snaps straight to the next cabinet');
 await ev(`document.querySelectorAll('.br-nav .br-pill')[1].click()`);await sleep(60);
 await ev(`document.querySelector('.br-custom-more').open=true;document.querySelectorAll('.br-custom-more .br-custom-actions')[0].querySelectorAll('button')[0].click()`);ok(await ev(`document.querySelector('.br-custom-arrows').hidden`),'no arrows for the pedestal placement');
 await ev(`document.querySelector('.br-custom-more').open=true;document.querySelectorAll('.br-custom-more .br-custom-actions')[0].querySelectorAll('button')[1].click()`);ok(await ev(`!document.querySelector('.br-custom-arrows').hidden`),'arrows return for the lever placement');
 await ev(`document.querySelectorAll('.br-vending-pick')[0].click()`);ok(await ev(`document.querySelector('.br-custom-arrows').hidden`),'no arrows for a screen pack');
 await ev(`document.querySelectorAll('.br-custom-items button')[1].click();document.querySelectorAll('.br-vending-pick')[0].click()`);ok(await ev(`document.querySelector('.br-custom-arrows').hidden`),'no arrows for a decoration');
 await ev(`document.querySelectorAll('.br-custom-items button')[0].click()`);
 await ev(`document.querySelector('.br-custom-overview').click()`);ok(await ev(`window.__backroom.scene.customization.debug().view.selected===-1 && !document.querySelector('.br-vending-pick[aria-pressed="true"]')`),'All items clears selection and outline');
 const sizes=await ev(`(()=>{const r=n=>{const b=n.getBoundingClientRect();return {x:b.x,y:b.y,w:b.width,h:b.height};};return {panel:r(document.querySelector('.br-custom-panel')),stage:r(document.querySelector('.br-custom-stage')),contexts:window.__vendingContexts.size,canvases:document.querySelectorAll('canvas').length,view:window.__backroom.scene.customization.debug().view};})()`);reports.push({width,height,...sizes});ok(sizes.contexts===1&&sizes.canvases===1,'shared renderer only');ok(width<height?sizes.panel.y>=height*.4:sizes.panel.x>=width*.45,'substantial room preview');await cdp('Input.dispatchKeyEvent',{type:'keyDown',code:'Escape',key:'Escape',windowsVirtualKeyCode:27});await sleep(100);ok(await ev('!window.__backroom.scene.customization.opened'),'Escape closes');
}
ok(!errs.length,'no page errors '+errs.join(' | '));await writeFile(join(OUT,'vending.json'),JSON.stringify({reports,errs,fails},null,2));await done(fails?1:0);
