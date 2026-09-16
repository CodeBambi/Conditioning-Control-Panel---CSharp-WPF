// Shared room/play cabinet identity, input, animation and responsive browser regression.
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

// One fake host: the slot's own mock-server.js, the bell's mock, fallback art for the deal.
const BELL_MOCK_URL = 'data:text/javascript;base64,' + (await readFile(join(HERE, 'mock-bell.js'))).toString('base64');
const FAKE_HOST = `(() => {
  const original = HTMLCanvasElement.prototype.getContext, contexts = new Set();
  window.__webgl = contexts;
  HTMLCanvasElement.prototype.getContext = function(type, ...args) { const result = original.call(this, type, ...args); if (/^webgl/.test(type) && result) contexts.add(result); return result; };
  const listeners = [], emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  const made = {};
  window.__mocks = made;   // the flow check below scripts a row on the slot's own mock (a promise per station id)
  const mock = (id) => made[id] || (made[id] = import(id === 'bell' ? '${BELL_MOCK_URL}' : '/backroom/stations/' + id + '/mock-server.js')
    .then((m) => (id === 'bell' ? m.createBellMock({}) : m.createMockServer({ sp: 57 }))));
  window.__posted = [];
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(type, fn) { if (type === 'message') listeners.push(fn); },
    postMessage(m) {
      window.__posted.push(JSON.parse(JSON.stringify(m)));
      if (m.type === 'ready') emit({ type: 'init', protocol: 1, sp: 57, reduced: false, motion: 'full', intensity: 'normal', lang: 'en',
        gates: { flash: true, subliminal: true, spiral: true, brainDrain: true, tunnel: true }, lex: { br_back: 'Back', br_balance: 'SP' },
        stations: ['slot', 'wheel', 'cards', 'roulette', 'counter', 'bell'], open: true });
      if (m.type === 'station-request') mock(m.station).then((s) => s.handle(m.op, m.body || {}, m.idem))
        .then((r) => emit({ type: 'station-result', reqId: m.reqId, ok: r.ok, status: r.status, reason: r.reason, body: r.body || {} }));
      if (m.type === 'media-request') emit({ type: 'media', reqId: m.reqId, seed: 1, words: [],
        gifs: Array.from({ length: m.count || 4 }, (_, i) => ({ key: 'g' + i, url: '/backroom/stations/slot/fallback/gif' + (i % 4) + '.webp', w: 180, h: 180, src: 'pool' })) });
      if (m.type === 'fx') emit({ type: 'fx-ack', token: m.token, fired: [m.fxId], skipped: [] });
    },
  };
})();`;

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
if(process.env.SHARED_SLOT_URL){await cdp('Network.enable');await cdp('Network.setBlockedURLs',{urls:['*__phone*.js']});}
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: FAKE_HOST });
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
const sceneDebug = () => ev('window.__backroom.loader.current?.debug().feel.scene');
const inside = (b,w,h) => !!b && b.width > 0 && b.height > 0 && b.left >= -1 && b.top >= -1 && b.left+b.width <= w+1 && b.top+b.height <= h+1;
const centre = b => [b.left+b.width/2,b.top+b.height/2];
const controlOverlaps = controls => {
  const spins=controls.filter(b=>b.selector==='.slot-spin'),freezes=controls.filter(b=>b.selector==='.slot-freeze button');
  return spins.flatMap(spin=>freezes.filter(freeze=>Math.min(spin.left+spin.width,freeze.left+freeze.width)-Math.max(spin.left,freeze.left)>.5 && Math.min(spin.top+spin.height,freeze.top+freeze.height)-Math.max(spin.top,freeze.top)>.5).map(freeze=>({spin,freeze})));
};
const visiblePlayControls = () => ev(`['.slot-spin','.slot-freeze button'].flatMap(selector=>[...document.querySelectorAll(selector)].filter(n=>n.getClientRects().length&&getComputedStyle(n).visibility!=='hidden').map(n=>{const r=n.getBoundingClientRect();return {selector,text:n.textContent.trim(),left:r.x,top:r.y,width:r.width,height:r.height};}))`);
const restoredView = (before,after,tag) => {
  ok(JSON.stringify(before.fixtureVisibility)===JSON.stringify(after.fixtureVisibility),tag+': other room fixtures restored');
  ok(after.scale.every((v,i)=>Math.abs(v-before.scale[i])<1e-8),tag+': fixture scale restored '+JSON.stringify({before:before.scale,after:after.scale}));
  ok(Math.abs(after.fov-before.fov)<1e-8,tag+': room camera FOV restored '+JSON.stringify({before:before.fov,after:after.fov}));
};
const snapshot = key => ev(`(() => {
  const h = window.__backroom.scene.scene.getObjectByName('station_' + ${JSON.stringify(key)});
  return {holder: h.uuid, fixtureVisibility:window.__backroom.scene.scene.children.filter(n=>n.name.startsWith('station_')).map(n=>[n.name,n.visible]), scale:h.scale.toArray(), fov:window.__backroom.scene.camera.fov, reels: [1,2,3].map(i => { const n=h.getObjectByName('reel_'+i); return n ? { uuid:n.uuid, y:n.position.y, rotation:n.rotation.toArray(), offset:n.material?.map?.offset.toArray() } : null; }), contexts:window.__webgl.size}; })()`);
const reelPicture = key => ev(`(()=>{
 const h=window.__backroom.scene.scene.getObjectByName('station_'+${JSON.stringify(key)});
 return [1,2,3].map(i=>{const map=h.getObjectByName('reel_'+i).material.map,c=map.image,ctx=c.getContext?.('2d');if(!ctx)return null;
 const data=ctx.getImageData(0,Math.floor(c.height/2),c.width,1).data;let hash=2166136261;for(const b of data)hash=Math.imul(hash^b,16777619)>>>0;
 return {w:c.width,h:c.height,hash,offset:map.offset.toArray(),repeat:map.repeat.toArray()};});})()`);
const leverVisibility = key => ev(`(async()=>{
 const T=await import('/vendor/three/three.module.min.js'),s=window.__backroom.scene;
 const holder=s.scene.getObjectByName('station_'+${JSON.stringify(key)}),lever=holder.getObjectByName('lever'),ball=holder.getObjectByName('wand_head');
 if(!ball)return {visible:false,reason:'missing ball'};
 const target=new T.Box3().setFromObject(ball).getCenter(new T.Vector3()),screen=target.clone().project(s.camera),ray=new T.Raycaster();ray.setFromCamera(new T.Vector2(screen.x,screen.y),s.camera);
 const hits=ray.intersectObjects(s.scene.children,true).filter(h=>{let n=h.object;while(n){if(!n.visible)return false;n=n.parent;}const m=h.object.material;if(!m)return false;return !(Array.isArray(m)?m:[m]).every(x=>x.transparent||x.opacity<.99);});
 const hit=hits[0]?.object;let n=hit;while(n&&n!==lever)n=n.parent;
 return {visible:n===lever,first:hit?.name,ball:ball.name,x:(screen.x+1)*innerWidth/2,y:(1-screen.y)*innerHeight/2};
})()`);
const settled = () => until("window.__backroom.loader.current && !window.__backroom.loader.current.debug().busy && !window.__backroom.loader.current.debug().spinning && !document.querySelector('.slot-spin').disabled",18000,100);
try {
for (const [label,width,height] of [['desktop',1280,720],['portrait',390,844],['landscape',844,390]].filter(v=>!process.env.SHARED_SLOT_VIEW||process.env.SHARED_SLOT_VIEW.split(',').includes(v[0]))) {
  const desktop=label==='desktop';
  await cdp('Emulation.setTouchEmulationEnabled',{enabled:!desktop,maxTouchPoints:2});
  await cdp('Emulation.setUserAgentOverride',{userAgent:desktop?'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36':'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1'});
  await cdp('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:!desktop});
  const press = async (x,y,endY=y) => {
    if(desktop){await cdp('Input.dispatchMouseEvent',{type:'mousePressed',x,y,button:'left',clickCount:1});await cdp('Input.dispatchMouseEvent',{type:'mouseMoved',x,y:endY,button:'left',buttons:1});await cdp('Input.dispatchMouseEvent',{type:'mouseReleased',x,y:endY,button:'left',clickCount:1});}
    else {await send('touchStart',[pt(9,x,y)]);if(endY!==y)await send('touchMove',[pt(9,x,endY)]);await send('touchEnd',[]);}
  };
  await cdp('Page.navigate',{url:PAGE_URL});
  ok(await until("document.documentElement.classList.contains('br-ready')",45000,100),label+': room boots');
  for (const key of ['slot:rose','slot:violet','slot:mint'].filter(k=>!process.env.SHARED_SLOT_VARIANT||k==='slot:'+process.env.SHARED_SLOT_VARIANT)) {
    const tag=label+'-'+key.split(':')[1];
    await goTo(key);
    const before=await snapshot(key), walking=await state();
    await ev(`window.__backroom.visit(window.__backroom.stations.find(s=>s.key===${JSON.stringify(key)})); true`);
    if(label==='portrait' && key==='slot:mint') {
      await cdp('Emulation.setDeviceMetricsOverride',{width:844,height:390,deviceScaleFactor:1,mobile:true});
      await sleep(120);
      await cdp('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:true});
    }
    const playable=await until("document.querySelector('.slot-station')?.dataset.phase === 'play'",20000,100);
    ok(playable,tag+': playable');
    if(!playable){await shot(tag+'-failed.png');throw new Error(tag+': cabinet initialization failed');}
    await until('!window.__backroom.scene.transitioning',8000);
    await sleep(200);
    const seated=await snapshot(key), d=await sceneDebug();
    ok(before.contexts===seated.contexts,tag+': entry creates no WebGL context');
    ok(before.holder===seated.holder && before.reels.every((r,i)=>r?.uuid===seated.reels[i]?.uuid),tag+': same room holder and reels survive entry');
    ok(d?.shared===true && d.reelIds.every((id,i)=>id===before.reels[i]?.uuid),tag+': play uses original room reel objects');
    ok(inside(d?.window,width,height),tag+': entire reel window in frame');
    ok(inside(d?.seat?.lever,width,height),tag+': lever in frame');
    ok(inside(d?.seat?.freeze,width,height),tag+': freeze in frame');
    const leverVisible=await leverVisibility(key);
    ok(leverVisible.visible,tag+': lever ball visible ahead of opaque cabinet '+JSON.stringify(leverVisible));
    const controls = await ev(`(() => {
      const selectors=['#br-back','.slot-jackpot','.slot-spirals','.slot-status','.slot-freeze button','.slot-spin','.slot-odds summary','.br-nav > button:not(:first-child)','.br-sp'];
      return selectors.flatMap(selector=>[...document.querySelectorAll(selector)].filter(n=>n.getClientRects().length&&getComputedStyle(n).visibility!=='hidden').map(n=>{
        const r=n.getBoundingClientRect(),hit=document.elementFromPoint(r.x+r.width/2,r.y+r.height/2);
        return {selector,text:n.textContent.trim(),left:r.x,top:r.y,width:r.width,height:r.height,clickable:n.matches('button,summary')?n===hit||n.contains(hit):true};
      }));
    })()`);
    ok(controls.every(b=>inside(b,width,height)),tag+': compact controls stay in frame '+JSON.stringify(controls.filter(b=>!inside(b,width,height))));
    ok(controls.every(b=>b.clickable),tag+': compact buttons are unobscured '+JSON.stringify(controls.filter(b=>!b.clickable)));
    const overlaps=controlOverlaps(controls);
    ok(overlaps.length===0,tag+': visible Spin and Freeze controls do not overlap '+JSON.stringify(overlaps));
    report.views.push({label:'controls',key,width,height,controls});
    await shot(tag+'-play.png');
    // Exercise the actual cabinet's raycast controls, with deterministic tape outcomes.
    await ev("(async()=>{const s=await window.__mocks.slot;s.script(['gif0','sub1','spiral2'],['gif1','sub2','spiral0']);return true})()");
    const start=await snapshot(key);
    if(d?.seat?.lever) {
      const [x,y]=centre(d.seat.lever);
      await press(x,y,Math.min(height-8,y+100));
    }
    ok(await until('window.__backroom.loader.current?.debug().spinning',5000),tag+': lever pull starts real spin');
    let changed=false;
    for(let i=0;i<10;i++){await sleep(100);const now=await snapshot(key);changed ||= JSON.stringify(start.reels)!==JSON.stringify(now.reels);}
    ok(changed,tag+': original room reel nodes animate');
    await shot(tag+'-spin.png');
    ok(await settled(),tag+': spin settles and controls unlock');
    const freeze=await sceneDebug();
    if(freeze?.seat?.freeze){await press(...centre(freeze.seat.freeze));}
    ok(await until("document.querySelector('.slot-freeze button[aria-pressed=true]')",3000),tag+': physical freeze button is clickable');
    if(label==='portrait' && key==='slot:rose') {
      for(const [rw,rh] of [[844,390],[390,844]]) {
        await cdp('Emulation.setDeviceMetricsOverride',{width:rw,height:rh,deviceScaleFactor:1,mobile:true});
        await sleep(400);
        const rotated=await sceneDebug(),same=await snapshot(key);
        ok(inside(rotated?.window,rw,rh)&&inside(rotated?.seat?.lever,rw,rh)&&inside(rotated?.seat?.freeze,rw,rh),'seated rotation '+rw+': window and physical controls fit');
        ok(same.reels.every((r,i)=>r?.uuid===before.reels[i]?.uuid),'seated rotation '+rw+': original reel identities');
        const rotatedControls=await visiblePlayControls(),overlaps=controlOverlaps(rotatedControls);
        ok(overlaps.length===0,'seated rotation '+rw+': visible Spin and Freeze controls do not overlap '+JSON.stringify(overlaps));
        report.views.push({label:'seated-rotation',width:rw,height:rh,debug:rotated,view:same,controls:rotatedControls});
        await shot('rotated-'+rw+'-play.png');
      }
    }
    const final=await snapshot(key);
    ok(final.contexts===before.contexts,tag+': play still uses original context count');
    // Freeze cosmetic redraw before fingerprinting the exit frame.
    await ev("window.__backroom.state.motion='off'");await sleep(120);
    const lastPicture=await reelPicture(key);
    await leave();
    const idlePicture=await reelPicture(key);
    ok(JSON.stringify(lastPicture)===JSON.stringify(idlePicture),tag+': Back preserves last reel pictures and UV position');
    if(key==='slot:rose')await shot(label+'-room-after-play.png');
    await ev("window.__backroom.state.motion='full'");
    const returned=await state();
    ok(returned.position.every((v,i)=>Math.abs(v-walking.position[i])<.02),tag+': Back restores walking position');
    const restored=await snapshot(key);
    ok(restored.holder===before.holder && restored.reels.every((r,i)=>r?.uuid===before.reels[i]?.uuid),tag+': room model retained after Back');
    restoredView(before,restored,tag);
    report.views.push({label,key,width,height,before,seated,debug:d,final,returned,restored});
  }
  // Reentry with motion disabled must not depend on a transition completing.
  const beforeOff=await snapshot('slot:rose');
  await ev("window.__backroom.state.motion='off';window.__backroom.visit(window.__backroom.stations.find(s=>s.key==='slot:rose'));true");
  ok(await until("document.querySelector('.slot-station')?.dataset.phase==='play'",15000,100),label+': motion off reentry playable');
  ok(!await ev('window.__backroom.scene.transitioning'),label+': motion off entry snaps');
  // Also toggle Off while already seated, not only before entry.
  await ev("window.__backroom.state.motion='full'");await sleep(200);
  await ev("window.__backroom.state.motion='off'");await sleep(120);
  const emiPose=()=>ev("(()=>{const e=window.__backroom.scene.scene.getObjectByName('station_slot:rose').getObjectByName('emi_topper');return [e.position.toArray(),e.rotation.toArray(),e.scale.toArray()]})()");
  const off1=await sceneDebug(),emi1=await emiPose();await sleep(240);const off2=await sceneDebug(),emi2=await emiPose();
  ok(JSON.stringify(emi1)===JSON.stringify(emi2),label+': live Motion Off stops EMI idle movement');
  ok(off1.lever===off2.lever && Math.abs(off2.lever)<1e-8 && off2.shiverPx===0,label+': live Motion Off stops idle lever and shiver');
  await shot(label+'-motion-off.png');
  await leave();
  const afterOff=await snapshot('slot:rose');
  restoredView(beforeOff,afterOff,label+': motion off exit');
  report.views.push({label:'motion-off-restoration',width,height,before:beforeOff,restored:afterOff});
  await ev("window.__backroom.state.motion='full'");
}
} catch(error){ok(false,error.stack||String(error));}
ok(errs.length===0,'no page errors'+(errs.length?': '+errs.join(' | '):''));
await writeFile(join(OUT,'shared-slot-check.json'),JSON.stringify({...report,fails},null,2));
console.log(fails ? `${fails} FAILED` : 'all shared slot checks passed');
await done(fails?1:0);
