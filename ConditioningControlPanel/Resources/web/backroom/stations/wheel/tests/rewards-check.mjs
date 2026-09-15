/* ============================================================================
 * wheel-check.mjs - the wheel station in headless Chrome, driven over CDP, on dev.html, mock-server.js and the
 * hypno kit's mock host.
 *
 *   node backroom/stations/wheel/tests/wheel-check.mjs [evidenceDir]    (exit 0 = pass)
 *
 * Nothing leaves the machine: Resources/ is served on 127.0.0.1 and the mocks answer every request. The only
 * process this stops is the Chrome it started, by its own handle. Evidence: a still of every v3 key moment
 * (CONTRACT 10.13.F: Loom hub, taffy and moire, the long last turn with the tunnel, the quiet room, the landing wash,
 * the big win GIF, the jackpot spiral with its picture), a gated-off run, a Calm run, the base lane's strips, the
 * on-screen hub handedness and wheel-check.json. The fullscreen set is drawn by tests/host-screen.js from the calls
 * the mock host recorded; the asserts read the calls. CHROME: CHROME_PATH, else the usual Windows install.
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
const sleep = ms => new Promise(r => setTimeout(r, ms));
const HERE = resolve(fileURLToPath(import.meta.url), '..');
const RES = resolve(HERE, '../../../../..');   // ConditioningControlPanel/Resources
const OUT = resolve(process.argv[2] || join(process.cwd(), '_evidence'));
await mkdir(OUT, { recursive: true });

const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME); process.exit(1); }
const PORT = Number(process.env.WHEEL_PORT || 8917), DEBUG_PORT = PORT + 500;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(RES, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise(r => server.listen(PORT, '127.0.0.1', r));
const prof = mkdtempSync(join(tmpdir(), 'backroom-wheel-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`, '--no-first-run',
  '--no-default-browser-check', '--mute-audio', '--hide-scrollbars', '--window-size=1280,720', '--ignore-gpu-blocklist', '--enable-unsafe-swiftshader',
  '--autoplay-policy=no-user-gesture-required', 'about:blank'], { stdio: 'ignore' });
async function done(code) { try { chrome.kill(); } catch { /* our own child only */ } server.close(); await sleep(500); try { rmSync(prof, { recursive: true, force: true }); } catch { /* noop */ } process.exit(code); }

let target = null;
for (let i = 0; i < 60 && !target; i++) { await sleep(250); try { target = (await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/list`)).json()).find(t => t.type === 'page'); } catch { /* not up */ } }
if (!target) { console.error('FAIL chrome never answered'); await done(1); }
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise(r => { ws.onopen = r; });
let msgId = 0;
const waits = new Map(), errs = [];
ws.onmessage = e => {
  const m = JSON.parse(e.data);
  if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Runtime.exceptionThrown') errs.push(m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text);
  if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') errs.push('console: ' + m.params.args.map(a => a.value || a.description).join(' '));
};
const cdp = (method, params) => new Promise(res => { const i = ++msgId; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async x => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
await cdp('Runtime.enable'); await cdp('Page.enable');
await cdp('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });


await cdp('Page.navigate', {url:`http://127.0.0.1:${PORT}/web/backroom/stations/wheel/dev.html?gates=off`});
for(let i=0;i<100&&!await ev('!!window.dev');i++)await sleep(100);
const cases=[['room_service',{kind:'decoration',decorationId:'ivy'},0],['room_service',{kind:'decoration',fallback:true},75],['seeing_double',{kind:'double',until:Date.now()+86400000},0],['head_empty',{kind:'nothing'},0],['room_service',{kind:'decoration',fallback:true},4],['seeing_double',{kind:'double',until:Date.now()+86400000},0]];
for(const [index,[id,reward,pay]] of cases.entries()){
  await cdp('Emulation.setDeviceMetricsOverride',{width:index%2?400:1280,height:index%2?800:720,deviceScaleFactor:1,mobile:index%2===1});
  const setup=await ev(`(async()=>{
    await window.rewardStation?.close(); await window.dev.station?.close();
    document.querySelector('#sit').hidden=true;document.querySelector('#dev').hidden=true;
    const {mount}=await import('./station.js');
    const rows=[['jackpot','JACKPOT',500,'jackpot',7.2],['pocket_sparkles','Pocket Sparkles',15,'prize',105.84],['good_behaviour','Good Behaviour',30,'prize',88.2],['keep_the_change','Keep the Change',60,'prize',52.92],['spoiled_rotten','Spoiled Rotten',150,'prize',17.64],['room_service','Room Service',0,'decoration',35.28],['seeing_double','Seeing Double',0,'double',42.336],['head_empty','Head Empty',0,'nothing',10.584]];
    const slices=rows.map(([id,label,pay,kind,width])=>({id,label,pay,kind,width,odds:'test'}));
    const st={ok:true,sp:100,spun:false,slices,jackpot:{amount:500},nextResetAt:new Date(Date.now()+3600000).toISOString()};
    const result={day:'2026-09-15',sliceId:${JSON.stringify(id)},sliceIndex:slices.findIndex(s=>s.id===${JSON.stringify(id)}),pay:${index===4?75:pay},total:${index===4?75:pay},credited:${pay},capped:${index===4},reward:${JSON.stringify(reward)}};
    let stateRequests=0;
    const oldMock=(await import('./mock-server.js')).createMockServer();oldMock.script('sip_a');await oldMock.handle('spin',{idem:'overnight_test_receipt01'});const oldState=(await oldMock.handle('state',{})).body;oldState.nextResetAt=new Date(Date.now()+200).toISOString();
    window.rewardCalls=0;window.landedCalls=0;
    window.rewardStation=await mount({root:document.querySelector('#root'),motion:'off',intensity:'calm',reduced:true,gates:{spiral:false,flash:false,tunnel:false,melt:false},media:async()=>({gifs:[],words:[]}),fx:()=>Promise.resolve({}),fxTunnel:()=>{},fxRelease:()=>{},rewardLanded:()=>window.landedCalls++,
      request:async(op)=>{if(op==='state')return{ok:true,body:${index}===5&&stateRequests++===0?oldState:st};window.rewardCalls++;await new Promise(r=>setTimeout(r,120));return{ok:true,body:{...st,sp:100+${pay},spun:true,result}};}});
    await window.rewardStation.open(); return window.rewardStation.debug();
  })()`);
  ok(setup?.phase==='play',id+' opens');
  if(index===5){ok(setup.state.slices.length===14,'starts on archived V3 layout');for(let i=0;i<120&&!await ev('window.rewardStation.debug().state.slices.length===8');i++)await sleep(50);ok(await ev('window.rewardStation.debug().state.slices.length===8'),'midnight adopts V4 state');}
  await ev("document.querySelector('.wheel-spin').click()");
  ok(await ev("document.querySelector('.daze-delivery').hidden"),id+' hidden before reply');
  for(let i=0;i<200&&!await ev('window.landedCalls===1');i++)await sleep(40);
  const r=await ev(`({calls:rewardCalls,landed:landedCalls,hidden:document.querySelector('.daze-delivery').hidden,text:document.querySelector('.daze-delivery p').textContent,scroll:document.documentElement.scrollWidth>innerWidth})`);
  ok(r.calls===1&&r.landed===1&&!r.hidden,id+' reveals once on landing');ok(!r.scroll,id+' no horizontal scroll');if(index===4)ok(r.text.includes('+4 SP'), 'capped gift displays credited amount');if(index===5)ok(await ev("window.rewardStation.debug().feel.scene.landed==='seeing_double'"),'midnight rebuilds physical sectors and lands new slice');
  await writeFile(join(OUT,`reward-${index}-${id}.png`),Buffer.from((await cdp('Page.captureScreenshot',{format:'png'})).result.data,'base64'));
  await ev('window.rewardStation.close()');ok(await ev("!document.querySelector('.daze-delivery')"),id+' disposes');
}
const motion = await ev(`(async()=>{const {createRewardReveal}=await import('./rewards.js');const root=document.querySelector('#root');const reveal=createRewardReveal(root,(_,s)=>s);reveal.show({reward:{kind:'decoration',decorationId:'ivy'}},false);await new Promise(r=>requestAnimationFrame(r));reveal.setStill(true);reveal.setStill(false);const still=root.querySelector('.daze-delivery').classList.contains('is-still'),running=root.querySelector('.daze-cloche i').getAnimations().some(a=>a.playState==='running');reveal.dispose();return{still,running};})()`);
ok(motion.still&&!motion.running,'live Motion Off drops cloche animation without replay');
ok(errs.length===0,'no browser errors');
await writeFile(join(OUT,'rewards-check.json'),JSON.stringify({fails,errs,cases:cases.length},null,2));
await done(fails?1:0);
