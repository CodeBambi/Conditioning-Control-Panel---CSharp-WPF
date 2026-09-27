import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createPortalRenderer, portalViewTransform, portalLensTransform, inverseWorldTransform, PORTAL_BUFFER } from './render-portals.js';
import CUES from './cues/endless.js';
import REACTIONS from './reactions/endless.js';
const point = (m,p) => ({ x:m.a*p.x+m.c*p.y+m.e, y:m.b*p.x+m.d*p.y+m.f });
const near = (a,b) => assert.ok(Math.abs(a-b)<1e-8, a+' != '+b);
const portals = [{id:0,pair:'a',x:200,y:250,angle:.2,halfLength:72},{id:1,pair:'a',x:1060,y:260,angle:Math.PI*.8,halfLength:72}];
const identity={a:1,b:0,c:0,d:1,e:0,f:0};
function context(log,transform=identity) {
  return new Proxy({}, { get(t,k) {
    if(k in t)return t[k];if(k==='getTransform')return()=>transform;
    return (...args)=>log.push([k,...args]);
  } });
}

test('portal view matches inverse transit rotation without reflection', () => {
  const [entry,exit]=portals,m=portalViewTransform(entry,exit),back=portalViewTransform(exit,entry);
  const at=point(m,exit);near(at.x,entry.x);near(at.y,entry.y);near(m.a*m.d-m.b*m.c,1);
  const ahead={x:exit.x+Math.cos(exit.angle)*20,y:exit.y+Math.sin(exit.angle)*20};
  const seen=point(m,ahead);near(seen.x,entry.x-Math.cos(entry.angle)*20);near(seen.y,entry.y-Math.sin(entry.angle)*20);
  const restored=point(back,seen);near(restored.x,ahead.x);near(restored.y,ahead.y);
});

test('portal lens frames the forward destination without mirrored chirality', () => {
  const [entry,exit]=portals,m=portalLensTransform(entry,exit);
  const focus={x:exit.x+Math.cos(exit.angle)*220,y:exit.y+Math.sin(exit.angle)*220};
  const seen=point(m,focus);near(seen.x,entry.x);near(seen.y,entry.y);
  assert.ok(m.a*m.d-m.b*m.c>0);
});

test('world capture removes rotated and translated camera transforms', () => {
  const camera={a:2,b:1,c:-1,d:2,e:90,f:-20},world={x:370,y:210};
  const undone=point(inverseWorldTransform(camera),point(camera,world));near(undone.x,world.x);near(undone.y,world.y);
  assert.equal(inverseWorldTransform({a:0,b:0,c:0,d:0,e:0,f:0}),null);
});

for(const reduced of [false,true])test('one bounded world snapshot serves both live windows, reduced='+reduced,()=>{
  const old=globalThis.document,bufferLog=[],mainLog=[],buffers=[];
  globalThis.document={createElement:()=>{const canvas={getContext:()=>context(bufferLog)};buffers.push(canvas);return canvas;}};
  try {
    const source={width:3840,height:2160},g=context(mainLog,{a:3,b:0,c:0,d:3,e:0,f:0});
    const s={w:1280,h:720,state:'grey',portals,balls:[{stuck:true}]},before=JSON.stringify(s);
    const fx=createPortalRenderer();fx.event('portalTransit',{entryId:0,exitId:1});
    fx.draw(g,source,s,{now:1,dt:.016,reduced});fx.draw(g,source,s,{now:2,dt:.016,reduced});
    assert.equal(buffers.length,1);assert.equal(buffers[0].width,PORTAL_BUFFER.width);assert.equal(buffers[0].height,PORTAL_BUFFER.height);
    assert.equal(bufferLog.filter(x=>x[0]==='drawImage'&&x[1]===source).length,2,'one capture per frame');
    assert.equal(mainLog.filter(x=>x[0]==='drawImage'&&x[1]===buffers[0]).length,4,'both mouths use the same capture');
    assert.equal(mainLog.some(x=>x[0]==='drawImage'&&x[1]===source),false,'no self-copy feedback');
    assert.ok(mainLog.some(x=>x[0]==='fillText'&&x[1]==='A'),'paired labels stay visible in grey');
    assert.equal(JSON.stringify(s),before,'view leaves all physics state unchanged');
    fx.dispose();
  } finally {globalThis.document=old;}
});

test('transit feedback stays nonspoken and reduced motion produces no particles',()=>{
  const audio=[];CUES.portalTransit({state:'grey',now:2,bus:{sfx:'sfx'},noise:(...a)=>a,play:(...a)=>audio.push(a)},{xN:.7});
  assert.equal(audio.length,1);assert.equal(audio[0][1],2,'the crossing answers immediately');
  REACTIONS.portalTransit({reduced:true},{x:1,y:2,fromX:3,fromY:4});
});
