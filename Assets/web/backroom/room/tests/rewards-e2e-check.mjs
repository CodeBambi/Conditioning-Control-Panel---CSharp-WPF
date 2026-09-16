// Actual room + wheel reward timing. ROOM_TEST_WEB can point at an integrated Resources/web tree.
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
const WEB = resolve(process.env.ROOM_TEST_WEB || resolve(HERE, '../../..'));   // Resources/web
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });

const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = Number(process.env.ROOM_REWARD_E2E_PORT || 8975), DEBUG_PORT = PORT + 500;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));
const prof = mkdtempSync(join(tmpdir(), 'backroom-reward-e2e-'));
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
 const listeners=[],emit=data=>setTimeout(()=>listeners.forEach(fn=>fn({data})),0);
 const slices=[['jackpot',500,'jackpot',7.2],['pocket_sparkles',15,'prize',105.84],['good_behaviour',30,'prize',88.2],['keep_the_change',60,'prize',52.92],['spoiled_rotten',150,'prize',17.64],['room_service',0,'decoration',35.28],['seeing_double',0,'double',42.336],['head_empty',0,'nothing',10.584]].map(([id,pay,kind,width])=>({id,label:id,pay,kind,width,odds:'test'}));
 const empty={decorations:{owned:[]},bonus:{until:0,multiplier:1}};
 window.__rewardCase='room_service';window.__replySent=false;window.__landEvents=[];
 window.chrome={webview:{addEventListener(_,fn){listeners.push(fn)},postMessage(m){
 if(m.type==='ready')emit({type:'init',protocol:1,sp:100,reduced:false,motion:'full',intensity:'normal',lang:'en',lex:{},gates:{spiral:true},stations:['wheel','bell'],open:true});
 if(m.type==='log')window.__landEvents.push(m);
 if(m.type==='station-request'){
 const state={ok:true,sp:100,spun:false,slices,jackpot:{amount:500},nextResetAt:new Date(Date.now()+3600000).toISOString(),...empty};
 let body=m.station==='bell'?{ok:true,entries:[],...empty}:state;
 if(m.station==='wheel'&&m.op==='spin'){
 const double=window.__rewardCase==='seeing_double',until=double?Date.now()+86400000:0;
 body={...state,spun:true,decorations:{owned:['ivy']},bonus:{until,multiplier:double?2:1},result:{day:'2026-09-15',sliceId:window.__rewardCase,sliceIndex:double?6:5,pay:0,total:0,credited:0,reward:double?{kind:'double',until}:{kind:'decoration',decorationId:'ivy'}}};window.__replySent=true;
 }
 emit({type:'station-result',reqId:m.reqId,ok:true,body});
 }
 if(m.type==='media-request')emit({type:'media-result',reqId:m.reqId,gifs:[],words:[]});
 }}};
})()`});
await cdp('Page.navigate',{url:`http://127.0.0.1:${PORT}/backroom/index.html`});
for(let i=0;i<150&&!await ev('!!window.__backroom?.scene');i++)await sleep(100);
ok(await ev('!!window.__backroom?.scene'),'actual room boots');
const results=[];
for(const kind of ['room_service','seeing_double']){
 await ev(`window.__rewardCase='${kind}';window.__replySent=false;window.__backroom.loader.open(window.__backroom.stations.find(s=>s.key==='wheel'))`);
 for(let i=0;i<150&&!await ev("!!document.querySelector('.wheel-spin')&&!document.querySelector('.wheel-spin').disabled");i++)await sleep(100);
 ok(await ev("!!document.querySelector('.wheel-spin')&&!document.querySelector('.wheel-spin').disabled"),kind+' actual wheel opens');
 await ev("document.querySelector('.wheel-spin').click()");
 for(let i=0;i<50&&!await ev('window.__replySent');i++)await sleep(10);
 // Let the bridge consume the reply while the real wheel is still travelling.
 await sleep(100);
 const early=await ev(`({owned:window.__backroom.rewards.has('ivy'),charm:!!document.querySelector('.br-double-charm:not([hidden])'),shown:!!document.querySelector('.daze-delivery:not([hidden])')})`);
 ok(kind==='room_service'?!early?.owned:!early?.charm,kind+' reply does not grant before landing');
 ok(!early?.shown,kind+' reveal waits for landing');
 for(let i=0;i<600&&!await ev("!!document.querySelector('.daze-delivery:not([hidden])')");i++)await sleep(100);
 const landed=await ev(`({owned:window.__backroom.rewards.has('ivy'),charm:!!document.querySelector('.br-double-charm:not([hidden])'),shown:!!document.querySelector('.daze-delivery:not([hidden])'),enabled:window.__backroom.scene.debug().customization.selected.props,canvases:document.querySelectorAll('canvas').length})`);
 ok(landed?.shown&&landed?.owned&&(kind!=='seeing_double'||landed.charm),kind+' landing grants ownership/charm');
 ok(landed?.enabled.every(x=>!x),kind+' never auto-enables a collectible');
 ok(landed?.canvases===2,kind+' uses room canvas plus face HUD');
 await shot(kind+'-landed.png');await ev("document.querySelector('#br-back').click()");for(let i=0;i<50&&await ev("!!document.querySelector('.wheel-spin')");i++)await sleep(50);await sleep(200);
 const after=await ev(`({owned:window.__backroom.rewards.has('ivy'),charm:!!document.querySelector('.br-double-charm:not([hidden])')})`);
 ok(after?.owned&&(kind!=='seeing_double'||after.charm),kind+' Back and stale bell preserve snapshot');
 results.push({kind,early,landed,after});
}
ok(!errs.length,'no browser errors: '+errs.join('; '));
await writeFile(join(OUT,'rewards-e2e.json'),JSON.stringify({results,errors:errs},null,2));
await done(fails?1:0);
