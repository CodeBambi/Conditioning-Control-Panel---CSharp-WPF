import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createGame} from './game.js';
import {finaleChaosMetadata,finaleChaosPose,finaleWhirlPose,finalePulse,FINALE_BRICK_LIMIT,FINALE_THREAD_COUNT} from './finale-chaos.js';
test('chaos roster contains each legacy motif, varied sizes and 20 percent more payload seats',()=>{
 const game=createGame({rng:()=>.5});game.jumpToFinaleBeat('spiral');const s=game.snapshot();
 assert.equal(s.bricks.length,FINALE_BRICK_LIMIT);
 const feeds=s.bricks.filter(b=>b.finaleFeed);
 assert.equal(new Set(feeds.map(b=>b.finaleMotif)).size,6);
 assert.ok(new Set(feeds.map(b=>b.w)).size>=4);
 assert.ok(feeds.some(b=>b.finaleLetter));
 const fresh=feeds.filter(b=>!b.finaleGrey&&!b.flyTarget&&b.chaosId>=0);
 const seats=fresh.filter(b=>b.gif>=0||b.word||b.spiral||b.split);
 assert.ok(seats.length>0);
 assert.ok(s.bricks.filter(b=>b.finaleGate).some(b=>b.y<0));
 assert.ok(s.bricks.filter(b=>b.finaleGate).some(b=>b.y>s.h));
});
test('whirlwind takes an orbit instead of crossing straight through the eye',()=>{
 const f={centreX:0,centreY:0};
 const pose=finaleWhirlPose({x:500,y:0,angle:0},{x:-100,y:0,angle:0},f,.5,0,0);
 assert.ok(Math.abs(pose.y)>350);
 assert.ok(Math.hypot(pose.x,pose.y)>350);
});
test('ripple and local wobble move actual poses and reduced motion suppresses them',()=>{
 const br={...finaleChaosMetadata(35),feedAge:24};
 const f={centreX:640,centreY:200,stageAge:2};
 const first=finaleChaosPose(br,f,1280,720),next=finaleChaosPose(br,{...f,stageAge:2.4},1280,720);
 assert.notDeepEqual(first,next);
 assert.deepEqual(finaleChaosPose(br,f,1280,720,true),finaleChaosPose(br,{...f,stageAge:9},1280,720,true));
 assert.notEqual(finalePulse(2,250),0);assert.equal(finalePulse(2,250,true),0);
 for(let i=0;i<120;i++) {
  const b={...finaleChaosMetadata(i),feedAge:35.9};
  const p=finaleChaosPose(b,f,1280,720);
  assert.ok(Math.hypot(p.x+b.w/2-f.centreX,p.y+b.h/2-f.centreY)>=125.99);
 }
});

test('moving whirlwind destination crosses angular wrap without teleporting',()=>{
 const f={centreX:0,centreY:0}, from={x:500,y:0,angle:0};
 const target=a=>({x:100*Math.cos(a),y:100*Math.sin(a),angle:a});
 const before=finaleWhirlPose(from,target(-.0001),f,.5,0,0);
 const after=finaleWhirlPose(from,target(.0001),f,.5,0,0);
 assert.ok(Math.hypot(after.x-before.x,after.y-before.y)<.1);
 const prior=finaleWhirlPose(from,target(Math.PI-.0001),f,.7,0,0);
 const next=finaleWhirlPose(from,target(-Math.PI+.0001),f,.7,0,0);
 assert.ok(Math.hypot(next.x-prior.x,next.y-prior.y)<.1);
});
test('new feed payloads independently include all four word effects',()=>{
 const game=createGame({rng:()=>.5});game.jumpToFinaleBeat('spiral');const s=game.snapshot();
 const words=new Set();
 for(let i=0;i<1800;i++){game.step(.05);for(const b of s.bricks)if(b.finaleFeed&&b.word)words.add(b.word);}
 assert.deepEqual([...words].sort(),['DROP','LET GO','RELAX','SINK']);
});

test('main galaxy stays compact and each brick follows one of five loosely scattered threads',()=>{
 const f={centreX:640,centreY:288,stageAge:2};
 for(let id=0;id<120;id++) {
  const q=(Math.floor(id/FINALE_THREAD_COUNT)+.5)/24;
  const b={...finaleChaosMetadata(id),feedAge:36*(.2+.8*q)};
  const p=finaleChaosPose(b,f,1280,720,true);
  const dx=p.x+b.w/2-f.centreX,dy=p.y+b.h/2-f.centreY;
  assert.ok(Math.abs(dx)<580&&Math.abs(dy)<310);
  assert.ok(Math.hypot(dx,dy)>=125.99);
  const expected=(id%FINALE_THREAD_COUNT)*Math.PI*2/FINALE_THREAD_COUNT+.25+q*3.8;
  const delta=Math.atan2(Math.sin(Math.atan2(dy,dx)-expected),Math.cos(Math.atan2(dy,dx)-expected));
  assert.ok(Math.abs(delta)<.4);
 }
});
test('hinges are sparse circular seats and most faces remain small',()=>{
 const all=Array.from({length:204},(_,i)=>finaleChaosMetadata(i));
 assert.equal(all.filter(b=>b.finaleHinge).length,6);
 assert.ok(all.filter(b=>Math.abs(b.w-27.6)<.001).length>100);
 assert.ok(all.every(b=>b.w>=24.83&&b.w<=35.89));
 assert.ok(all.filter(b=>b.finaleHinge).every(b=>b.w===b.h));
});


test('loose outer flow leaves ball-width radial routes while streams replenish',()=>{
 const game=createGame({rng:()=>.5});game.jumpToFinaleBeat('spiral');const s=game.snapshot();
 for(let beat=0;beat<4;beat++){
  for(let i=0;i<200;i++)game.step(.05);
  let routes=0;
  for(let seat=0;seat<120;seat++){
   const a=seat*Math.PI/60;let clear=true;
   for(let radius=155;radius<600&&clear;radius+=6){
    const x=s.finale.centreX+Math.cos(a)*radius,y=s.finale.centreY+Math.sin(a)*radius;
    for(const b of s.bricks){
     if(!b.finaleFeed||!b.alive||b.irisAlpha<.15)continue;
     const dx=x-b.x-b.w/2,dy=y-b.y-b.h/2,c=Math.cos(b.angle),sn=Math.sin(b.angle);
     if(Math.abs(dx*c+dy*sn)<b.w/2+8&&Math.abs(-dx*sn+dy*c)<b.h/2+8){clear=false;break;}
    }
   }
   if(clear)routes++;
  }
  assert.ok(routes>0,`ball-width routes at ${(beat+1)*10}s`);
  assert.ok(s.bricks.length<=154);
 }
});
