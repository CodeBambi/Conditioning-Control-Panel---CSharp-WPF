/* ============================================================================
 * roulette-check.mjs - the Velvet Vortex station in headless Chrome, driven over CDP, on dev.html (the kit's mock
 * host and mock-server.js) and inside the real room page.
 *
 *   node backroom/stations/roulette/tests/roulette-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * Nothing leaves the machine: Resources/web is served on 127.0.0.1 (ROULETTE_PORT, default 8909, debug +500) and the
 * mocks answer every call. The only process this stops is the Chrome it started, by its own handle.
 * Evidence: a screenshot of every key moment in CONTRACT 10.13.F (idle table, lighthouse sweep, the mat, a
 * refused cover-all, the tunnel run, the fret rattle, the Spiral Wake turret, big / win / miss landings, Full,
 * a gated-off run, a Calm run, the room) and roulette-check.json. The dev page's "host preview" layer paints
 * what the mock host acked, so the fullscreen moments show in the shots; the real overlays are the app's.
 * CHROME: CHROME_PATH, else the usual Windows install.
 * ==========================================================================*/

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
const WEB = resolve(HERE, '../../..');   // Resources/web
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });

const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = Number(process.env.ROOM_REWARDS_PORT || 8929), DEBUG_PORT = PORT + 500;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));
const prof = mkdtempSync(join(tmpdir(), 'backroom-roulette-'));
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
const ev = async (x) => {
  const r = await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true });
  if (r.result?.exceptionDetails) errs.push('eval: ' + (r.result.exceptionDetails.exception?.description || r.result.exceptionDetails.text));
  return r.result?.result?.value;
};
await cdp('Runtime.enable'); await cdp('Page.enable');
await cdp('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });
async function shot(name) {
  const r = await cdp('Page.captureScreenshot', { format: 'png' });
  await writeFile(join(OUT, name), Buffer.from(r.result.data, 'base64'));
  console.log('  shot ' + name);
}


await cdp('Page.addScriptToEvaluateOnNewDocument',{source:`(()=>{
 const listeners=[];window.__rewardLogs=[];window.chrome={webview:{addEventListener(_,fn){listeners.push(fn)},postMessage(m){
 if(m.type==='log')window.__rewardLogs.push(m);
 const emit=data=>setTimeout(()=>listeners.forEach(fn=>fn({data})),0);
 if(m.type==='ready')emit({type:'init',protocol:1,sp:50,reduced:false,motion:'full',intensity:'normal',lang:'en',lex:{},gates:{},stations:['wheel','bell'],open:true});
 if(m.type==='station-request'&&m.station!=='bell')emit({type:'station-result',reqId:m.reqId,ok:true,body:{ok:true,decorations:{owned:['ivy']}}});
 if(m.type==='station-request'&&m.station==='bell')emit({type:'station-result',reqId:m.reqId,ok:true,body:{ok:true,entries:[],decorations:{owned:[]},bonus:{until:0,multiplier:1}}});
 }}};
})()`});
await cdp('Page.navigate',{url:`http://127.0.0.1:${PORT}/backroom/index.html`});
for(let i=0;i<100;i++){if(await ev('!!window.__backroom?.scene'))break;await sleep(100);}
const r=await ev(`(async()=>{
 const b=window.__backroom,s=b.scene;
 s.customization.open();
 const buttons=[...document.querySelectorAll('.br-custom-items button')];buttons[9].click();
 const locked=document.querySelector('.br-custom-hud').textContent.includes('Collect this decoration');
 const rejected=s.customization.select('props',true,0)===false;
 const before=s.debug().customization.selected.props;
 const granted={owned:['monstera']};s.setRewards(granted);
 const afterGrant=s.debug().customization.selected.props;
 const on=[...document.querySelectorAll('.br-custom-hud button')].find(x=>x.textContent==='On');on?.click();
 const afterUse=s.debug().customization.selected.props;
 s.dismissEmi();
 await b.loader.open({id:'wheel',key:'wheel',state:'live',entry:'smoke/mock-station.js',name:'wheel'});
 const noEarlyGrant=!b.rewards.has('ivy'),ctx=window.__mockStation.ctx;
 ctx.rewardLanded({decorations:{owned:['ivy']},bonus:{until:Date.now()+120000,multiplier:2}});
 const landedGrant=b.rewards.has('ivy'),closing=b.loader.close();
 ctx.rewardLanded({decorations:{owned:['terrarium']}});await closing;
 const noLateGrant=!b.rewards.has('terrarium');
 const {createRoomRewards,createDoubleCharm}=await import('/backroom/room/rewards.js');
 const store=createRoomRewards();store.apply({bonus:{until:Date.now()+120000,multiplier:2}});
 const host=document.createElement('div');document.body.append(host);
 const charm=createDoubleCharm({mount:host,lex:(_,f)=>f,read:t=>store.snapshot(t)});
 const button=host.querySelector('button');button.click();const charmOn=!button.hidden&&button.title.includes('Prepaid');
 charm.dispose();host.remove();
 return {locked,rejected,before,afterGrant,afterUse,charmOn,noEarlyGrant,landedGrant,noLateGrant};
})()`);
ok(r?.locked&&r?.rejected,'unowned collectible can be previewed but cannot be applied');
ok(r?.before?.every(x=>!x),'all collectible props start disabled');
ok(r?.afterGrant?.every(x=>!x),'ownership never auto-enables a decoration');
ok(r?.afterUse?.[0]===true&&r.afterUse.slice(1).every(x=>!x),'explicit On applies only the owned decoration');
ok(r?.charmOn,'Seeing Double charm opens accessible prepaid-tape explanation');
ok(r?.noEarlyGrant&&r?.landedGrant&&r?.noLateGrant,'wheel grants apply only on its live landed callback, never on reply or after Back');
ok(errs.length===0,`no page errors: ${errs.join('; ')}`);
await writeFile(join(OUT,'rewards-check.json'),JSON.stringify({result:r,errors:errs},null,2));
await shot('room-rewards.png');
await cdp('Emulation.setDeviceMetricsOverride',{width:400,height:800,deviceScaleFactor:1,mobile:true});
await ev("document.querySelector('.br-double-charm').click()");await sleep(100);
ok(await ev(`(()=>{const b=document.querySelector('#br-back').getBoundingClientRect(),n=document.querySelector('.br-nav').getBoundingClientRect();return n.top>b.bottom;})()`),'active phone charm keeps navigation below Back');
await shot('room-rewards-phone.png');await done(fails?1:0);
