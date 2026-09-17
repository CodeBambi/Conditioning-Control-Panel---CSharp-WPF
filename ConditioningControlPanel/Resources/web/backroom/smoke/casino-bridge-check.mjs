// Same-window cabinet regression on the current combined local preview.
// A fresh Chrome profile isolates every local ledger purchase from the owner's browser.
import {spawn} from 'node:child_process';
import {mkdtempSync} from 'node:fs';
import {mkdir,writeFile} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
const mobile=process.argv.includes('--phone'), outDir='tmp-race-juice/casino-bridge'+(mobile?'-phone':'');
const port=9374, base='http://127.0.0.1:8798';
const profile=mkdtempSync(join(tmpdir(),'race-cabinet-'));
const chrome=spawn('C:/Program Files/Google/Chrome/Application/chrome.exe',[
 '--headless=new','--enable-unsafe-swiftshader','--no-first-run','--no-default-browser-check',
 '--mute-audio',`--remote-debugging-port=${port}`,`--user-data-dir=${profile}`,'--window-size=1280,800','about:blank'
],{stdio:'ignore'});
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
let ws;const errors=[],results=[];
try {
 let page;for(let i=0;i<60&&!page;i++){try{page=(await(await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t=>t.type==='page');}catch{}if(!page)await sleep(200);}
 if(!page)throw Error('Chrome unavailable');
 ws=new WebSocket(page.webSocketDebuggerUrl);await new Promise(r=>ws.onopen=r);
 let id=0;const pending=new Map();
 ws.onmessage=e=>{const m=JSON.parse(e.data);if(m.id){pending.get(m.id)?.(m);pending.delete(m.id);}else if(m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails);};
 const cdp=(method,params={})=>new Promise((resolve,reject)=>{const n=++id;const timer=setTimeout(()=>{pending.delete(n);reject(Error('CDP timeout '+method));},20000);pending.set(n,m=>{clearTimeout(timer);resolve(m)});ws.send(JSON.stringify({id:n,method,params}));});
 const ev=async expression=>{const m=await cdp('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});if(m.result?.exceptionDetails)throw Error(m.result.exceptionDetails.exception?.description||m.result.exceptionDetails.text);return m.result?.result?.value;};
 const until=async expression=>{for(let i=0;i<100;i++){if(await ev(expression))return true;await sleep(250);}return false;};
 const ok=(condition,label)=>{results.push({label,passed:!!condition});console.log(condition?'PASS':'FAIL',label);if(!condition)throw Error(label);};
 const key=async(code,key=code)=>{await cdp('Input.dispatchKeyEvent',{type:'keyDown',code,key});await cdp('Input.dispatchKeyEvent',{type:'keyUp',code,key});};
 const shot=async name=>{await mkdir(outDir,{recursive:true});const s=await cdp('Page.captureScreenshot',{format:'png'});await writeFile(`${outDir}/${name}.png`,Buffer.from(s.result.data,'base64'));};
 const pointerClick=async({x,y})=>{if(mobile){await cdp('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x,y}]});await cdp('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});}else{await cdp('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,x,y});await cdp('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,x,y});}};
 const raceDoc="document";
 const raceWindow="window";
 await cdp('Runtime.enable');await cdp('Page.enable');await cdp('Network.enable');
 await cdp('Network.setBlockedURLs',{urls:['https://*','http://127.0.0.1:8798/api/*']});
 if(mobile){await cdp('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});await cdp('Emulation.setTouchEmulationEnabled',{enabled:true});}
 await cdp('Emulation.setFocusEmulationEnabled',{enabled:true});
 await cdp('Page.navigate',{url:base+'/backroom/index.html?check=race-cabinet'});
 ok(await until("!!window.__backroom?.scene && !!window.__brServer && document.documentElement.classList.contains('br-ready')"),'combined preview ready');
 await ev("window.__brServer.ledger.grant(10000);window.__brSettings.balance(window.__brServer.ledger.sp())");
 const initial=await ev("window.__brServer.handle('counter','state',{})");
 ok(initial.ok&&!initial.body.prizes?.grants?.some(g=>g.startsWith('rt.original.')),'fresh isolated ledger has no racing ownership');
 ok(await ev("typeof window.__backroom.openRace==='function'"),'cabinet session route exposed');
 await ev('window.__backroom.openRace();undefined');
 ok(await until("window.__backroom.loader.current?.id==='counter' || document.querySelector('.counter-card')"),'locked cabinet opens prize counter');
 ok(await ev("location.pathname==='/backroom/index.html'"),'locked route remains in casino');
 const state=await ev("window.__brServer.handle('counter','state',{})");
 const bought=await ev(`window.__brServer.handle('counter','buy',{prizeId:'rt_bundle_1',catalogVersion:${state.body.catalogVersion}},'race-cabinet-'+crypto.randomUUID())`);
 ok(bought.ok,'real local ledger accepts bundle1 purchase');
 await ev(`(async()=>{const blob=await (await fetch('/backroom/stations/slot/fallback/gif0.webp')).blob();await window.__brMedia.files([new File([blob],'shared.webp',{type:'image/webp'})]);})()`);
 const postPurchaseSp=await ev('window.__brServer.ledger.sp()');
 const owned=await ev("window.__brServer.handle('counter','state',{})");
 const grants=owned.body.prizes?.grants||[];
 ok(['rt.original.01','rt.original.02','rt.original.03'].every(g=>grants.includes(g))&&!grants.includes('rt.original.00'),'bundle1 grants exactly its tracks without demo');
 await ev('window.__backroom.back();undefined');
 ok(await until("!window.__backroom.loader.current && !window.__backroom.scene.seated && !window.__backroom.scene.transitioning"),'counter closes cleanly');
 ok(await until("window.__backroom.scene.scene.getObjectByName('unlocked_arcade')?.visible"),'bundle1 reveals cabinet without demo');
 await ev("window.__backroom.scene.pose([4.1,1.7,-4.8],0,0)");await sleep(600);
 const pose=await ev("(()=>{const d=window.__backroom.scene.debug();return {position:d.position,yaw:d.yaw,pitch:d.pitch}})()");
 // Actual pointer path, projecting the cabinet centre into the visible canvas.
 const hit=await ev("(async()=>{const T=await import('/vendor/three/three.module.min.js');const s=window.__backroom.scene;const node=s.scene.getObjectByName('unlocked_arcade');const v=new T.Vector3();new T.Box3().setFromObject(node).getCenter(v);v.project(s.camera);const r=s.renderer.domElement.getBoundingClientRect();return {x:r.left+(v.x+1)*r.width/2,y:r.top+(1-v.y)*r.height/2};})()");
 console.log('POINTER',JSON.stringify({hit,pose}));await shot('before-cabinet-click');
 await pointerClick(hit);
 ok(await until("location.pathname.startsWith('/backroom/racing/')"),'cabinet pointer navigates to race in same tab');
 await sleep(2000);await key('Escape','Escape');
 ok(await until("!!document.querySelector('.rm-root:not([hidden])')"),'race menu visible after skipping introduction');
 ok(await ev("location.pathname.startsWith('/backroom/racing/') && !window.__backroom"),'race replaces and disposes the room document');
 ok((await(await fetch(`http://127.0.0.1:${port}/json/list`)).json()).filter(t=>t.type==='page').length===1,'race uses the original browser tab');
 await ev(`(${raceDoc}).querySelector('.rm-list [data-id=cloud]').click()`);
 const names=await ev(`Array.from((${raceDoc}).querySelectorAll('.rm-level-title')).map(n=>n.textContent)`);
 ok(names.length===3&&!names.includes('Rapid Induction'),'race menu contains only bundle1 levels');
 ok(await until("window.__brMedia?.get().mode==='local' && window.__brMedia.get().localCount===1"),'selected casino file survives into Racing');
 await ev("window.__mediaFrames=[];chrome.webview.addEventListener('message',e=>{if(e.data.type==='manifest')window.__mediaFrames.push(e.data)});window.__brOptions.openMedia()");
 ok(await ev("document.querySelector('#__opt').dataset.open==='1'"),'Racing media controls open');
 await ev("window.__brMedia.set('bundled')");
 ok(await until("window.__mediaFrames.some(m=>m.images?.length===4)"),'Racing receives changed media manifest');
 await ev("document.querySelector('.br-options-back').click()");
 await shot('owned-levels');
 await ev(`(${raceDoc}).querySelector('.rm-cloud [data-id=back]').click();(${raceDoc}).querySelector('.rm-list [data-id=surface]').click()`);
 ok(await until("location.pathname==='/backroom/index.html' && !!window.__backroom?.scene && document.documentElement.classList.contains('br-ready')"),'return control restores casino');await sleep(400);
 ok(await ev("window.__brMedia.get().mode==='bundled'"),'Racing media choice carries back into casino');
 const after=await ev("(()=>{const d=window.__backroom.scene.debug();return {position:d.position,yaw:d.yaw,pitch:d.pitch}})()");
 ok(JSON.stringify(after)===JSON.stringify(pose),'return restores exact room pose');
 if(mobile){await pointerClick(hit);}else await key('KeyE','e');
 ok(await until("location.pathname.startsWith('/backroom/racing/') && !!document.querySelector('.rm-root:not([hidden])')"),(mobile?'cabinet tap':'E')+' launches a repeat cabinet session');
 await ev("window.__checkSent=[];const send=chrome.webview.postMessage;chrome.webview.postMessage=m=>{window.__checkSent.push(m.type);send(m)};document.querySelector('.rm-list [data-id=race]').click()");
 ok(await until("!!document.querySelector('.race-hud:not(.is-lobby)')"),'casino-launched race reaches driving view');
 await sleep(3500);await shot('running-race');
 const clickVisible=async expression=>{
  const button=await ev(`(()=>{const b=${expression};if(!b)return null;const r=b.getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2,visible:r.width>0&&r.height>0&&getComputedStyle(b).visibility!=='hidden'&&document.elementFromPoint(r.left+r.width/2,r.top+r.height/2)===b}})()`);
  if(!button?.visible) console.log('BLOCKER',await ev(`(()=>{const b=${expression};const r=b?.getBoundingClientRect();return {rect:r?.toJSON(),hit:r?document.elementFromPoint(r.left+r.width/2,r.top+r.height/2)?.outerHTML:null}})()`));
  ok(button?.visible,'return step is visible and unobstructed');
  await pointerClick(button);
 };
 if(mobile)await clickVisible("document.querySelector('.rt-pause')");else await key('Escape','Escape');
 ok(await until("Array.from(document.querySelectorAll('.rh-screen.is-on button')).some(b=>b.textContent==='end the run')"),(mobile?'Pause tap':'Escape')+' opens Brake during driving');
 await clickVisible("Array.from(document.querySelectorAll('.rh-screen.is-on button')).find(b=>b.textContent==='end the run')");
 ok(await until("Array.from(document.querySelectorAll('.rh-screen.is-on button')).some(b=>b.textContent==='surface')"),'ending run shows results');
 await clickVisible("Array.from(document.querySelectorAll('.rh-screen.is-on button')).find(b=>b.textContent==='surface')");
 ok(await until("!!document.querySelector('.rm-root:not([hidden])')"),'results returns to menu');
 ok(await ev("window.__checkSent.filter(t=>t==='run-ended').length===1"),'one completed run sends exactly one payout request');
 await clickVisible("document.querySelector('.rm-list [data-id=surface]')");
 ok(await until("location.pathname==='/backroom/index.html' && !!window.__backroom?.scene && document.documentElement.classList.contains('br-ready')"),'repeat session returns cleanly');
 const finalOwned=await ev("window.__brServer.handle('counter','state',{})");
 ok(await ev('window.__brServer.ledger.sp()')===postPurchaseSp,'race return preserves post-purchase casino SP');
 ok(JSON.stringify(finalOwned.body.prizes.grants.slice().sort())===JSON.stringify(grants.slice().sort()),'race return preserves exact purchased grants');
 await shot('returned-room');
 ok(errors.length===0,'no browser page exceptions');
 console.log(JSON.stringify({results,errors}));
 await writeFile(outDir+'/results.json',JSON.stringify({results,errors},null,2));
} catch(e) {console.error(e.stack);console.error(JSON.stringify({results,errors}));process.exitCode=1;}
finally{ws?.close();chrome.kill();}
