import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createGame} from './game.js';
import {pickPower,POWER_WEIGHT,createPowerups,ordinaryTarget,swayX,DROP_REACH} from './powerups.js';
function fixture(opts={}){
 const events=[],s={w:1280,h:720,state:'colour',wallAge:3,paddle:{x:640,y:680,w:160,h:12},balls:[{x:640,y:600,vx:30,vy:-220}],bricks:[]};
 const damage=[];const power=createPowerups(s,{rng:()=>.5,emit:(...e)=>events.push(e),newBall:()=>({r:7,trail:[]}),damage:br=>damage.push(br),...opts});
 return {s,power,events,damage};
}
function catchDrop(f,kind){f.s.power.drops.push({kind,x:640,y:660,age:0});f.power.step(.1);}
test('all four pickups activate only after a paddle catch; missed drops do nothing',()=>{
 for(const kind of ['multiball','fireball','laser','shield']){
  const f=fixture();f.s.power.drops.push({kind,x:20,y:660,age:0});f.power.step(.5);assert.equal(f.s.power[kind],0);assert.deepEqual(f.events.map(e=>e[0]),['powerMiss']);
  catchDrop(f,kind);assert.ok(f.s.power[kind]>0);assert.equal(f.events[1][0],'powerCatch');assert.equal(f.events[1][1].kind,kind);
 }
});
test('multiball expiry promotes a surviving extra and releases retired orbit owner',()=>{
 const f=fixture();catchDrop(f,'multiball');assert.equal(f.s.balls.length,3);
 f.s.balls.shift();f.s.well={captured:f.s.balls[1],used:true};f.power.step(13);
 assert.equal(f.s.balls.length,1);assert.equal(f.s.balls[0].temporary,false);assert.equal(f.s.well.captured,null);assert.equal(f.events.filter(e=>e[0]==='lost').length,0);
});
test('shield caps at two and reset clears every timer, pickup and projectile',()=>{
 const f=fixture();for(let i=0;i<3;i++)catchDrop(f,'shield');assert.equal(f.s.power.charges,2);
 catchDrop(f,'laser');catchDrop(f,'fireball');catchDrop(f,'multiball');f.s.power.drops.push({kind:'laser'});f.power.reset();
 assert.equal(f.s.balls.length,1);for(const k of ['shield','laser','fireball','multiball','charges'])assert.equal(f.s.power[k],0);
 assert.equal(f.s.power.drops.length+f.s.power.shots.length,0);
});
test('lasers stop at metal and scripted defenses without damaging them',()=>{
 for(const tag of ['finaleGate','finaleRing','finaleDefense','finaleCenterGuard','finaleMetal','pendulumAnchor','irisCore','finaleHinge']){
 const f=fixture(),br={alive:true,x:620,y:600,w:40,h:20,[tag]:true};f.s.bricks=[br];f.s.power.shots=[{x:640,y:625,r:3}];f.power.step(.04);
 assert.equal(ordinaryTarget(br,f.s),false);assert.equal(f.damage.length,0);assert.equal(f.s.power.shots.length,0);
 }
 const f=fixture();f.s.bricks=[{alive:true,x:620,y:600,w:40,h:20}];f.s.power.shots=[{x:640,y:625,r:3}];f.power.step(.04);assert.equal(f.damage.length,1);
});
test('reinforced split brick drops once only when broken, never creates an immediate ball',()=>{
 const game=createGame({rng:()=>.5,greyMetal:false}),s=game.snapshot();s.state='colour';s.bricks[0].strength=3;s.bricks[0].hp=3;s.bricks[0].split=true;
 const n=s.balls.length;game.breakBrick(0);game.breakBrick(0);assert.equal(s.power.drops.length,0);game.breakBrick(0);
 assert.equal(s.power.drops.length,1);assert.equal(s.power.drops[0].kind,'multiball');assert.equal(s.balls.length,n);
 game.jumpToWall(2);assert.equal(s.power.drops.length,0);
});
test('automatic junction shield never spends collected saves; ordinary miss spends one',()=>{
 const game=createGame({rng:()=>.5}),s=game.snapshot();s.state='colour';s.power.charges=2;s.power.shield=20;
 function miss(){Object.assign(s.balls[0],{stuck:false,x:20,y:s.paddle.y+20,vx:20,vy:300});game.step(1/60);assert.ok(s.balls[0].vy<0);}
 s.wallAge=.2;miss();assert.equal(s.power.charges,2);s.wallAge=3;miss();assert.equal(s.power.charges,1);assert.equal(s.transition,null);
});
test('fireball crosses an ordinary reinforced face but damages it only once per contact',()=>{
 const game=createGame({rng:()=>.5,greyMetal:false}),s=game.snapshot();s.state='colour';s.wallAge=3;s.power.fireball=8;
 s.bricks=[{...s.bricks[0],x:600,y:350,w:80,h:30,strength:3,hp:3,split:false,gif:-1,spiral:null,word:null,jackpot:false}];
 Object.assign(s.balls[0],{stuck:false,x:640,y:390,vx:0,vy:-300});for(let i=0;i<5;i++)game.step(1/60);
 assert.equal(s.bricks[0].hp,2);assert.ok(s.balls[0].vy<0);
});

test('grey world suppresses drops and clears powers before a pending catch',()=>{
 const f=fixture();catchDrop(f,'multiball');catchDrop(f,'shield');catchDrop(f,'laser');catchDrop(f,'fireball');
 f.s.state='grey';f.power.drop({powerup:'shield'});f.power.drop({split:true});assert.equal(f.s.power.drops.length,0);
 const events=f.events.length;f.s.power.drops.push({kind:'shield',x:640,y:660,age:0});f.power.step(.1);
 assert.equal(f.events.length,events);assert.equal(f.s.balls.length,1);
 for(const k of ['multiball','shield','laser','fireball','charges'])assert.equal(f.s.power[k],0);
 assert.equal(f.s.power.drops.length+f.s.power.shots.length,0);
 f.s.state='colour';f.power.drop({powerup:'shield',x:620,y:650,w:40,h:20});f.power.step(.1);assert.equal(f.s.power.charges,1);
});

const names=(f,name)=>f.events.filter(e=>e[0]===name);
test('laser fires one volley per eighth note of the handed beat clock, never per frame',()=>{
 let beats=0;const f=fixture({beatTime:()=>beats});catchDrop(f,'laser');
 for(let i=0;i<240;i++){beats+=1/120/.625;f.power.step(1/120);}                       // two seconds at 96 bpm = 6.4 eighths
 const shots=names(f,'laserShot');assert.ok(shots.length===6||shots.length===7,String(shots.length));
 assert.equal(shots[0][1].x,640);assert.ok(f.s.power.muzzle>=0);
 const frozen=shots.length;for(let i=0;i<60;i++)f.power.step(1/120);                   // the clock stands still: silence
 assert.equal(names(f,'laserShot').length,frozen);
 beats+=40;f.power.step(1/120);assert.equal(names(f,'laserShot').length,frozen+1);     // a tab sleep is one volley, not a burst
});
test('laser falls back to the sim clock at 96 bpm and pairs its barrels per volley',()=>{
 const f=fixture();catchDrop(f,'laser');let shotsSeen=0;
 for(let i=0;i<120;i++){f.power.step(1/120);shotsSeen=Math.max(shotsSeen,f.s.power.shots.length);}
 const volleys=names(f,'laserShot').length;assert.ok(volleys>=3&&volleys<=4,String(volleys));assert.ok(shotsSeen>=2);
 f.s.wallAge=.5;const n=volleys;for(let i=0;i<120;i++)f.power.step(1/120);assert.equal(names(f,'laserShot').length,n);
});
test('laser impact emits laserHit at the bolt, on ordinary bricks and on metal',()=>{
 for(const extra of [{},{finaleMetal:true}]){
  const f=fixture();f.s.bricks=[{alive:true,x:620,y:600,w:40,h:20,...extra}];f.s.power.shots=[{x:640,y:625,r:3}];f.power.step(.04);
  const hit=names(f,'laserHit');assert.equal(hit.length,1);assert.equal(hit[0][1].x,640);assert.ok(hit[0][1].y<=625&&hit[0][1].y>=600);
 }
});
test('timed powers warn once at two seconds and expire once; a fresh catch re-arms the warning',()=>{
 for(const kind of ['fireball','laser','shield']){
  const f=fixture();catchDrop(f,kind);const life=f.s.power[kind];
  for(let t=0;t<life-2.5;t+=.1)f.power.step(.1);assert.equal(names(f,'powerWarn').length,0);
  for(let i=0;i<10;i++)f.power.step(.1);assert.deepEqual(names(f,'powerWarn').map(e=>e[1].kind),[kind]);
  catchDrop(f,kind);for(let t=0;t<life+1;t+=.1)f.power.step(.1);
  assert.equal(names(f,'powerWarn').length,2);assert.deepEqual(names(f,'powerExpire').map(e=>e[1].kind),[kind]);
  for(let i=0;i<20;i++)f.power.step(.1);assert.equal(names(f,'powerExpire').length,1);
 }
});
test('multiball speaks only while an extra ball lives; a spent shield ends without a word',()=>{
 const f=fixture();catchDrop(f,'multiball');for(let t=0;t<13;t+=.1)f.power.step(.1);
 assert.equal(names(f,'powerWarn').length,1);assert.equal(names(f,'powerExpire').length,1);
 const g=fixture();catchDrop(g,'multiball');g.s.balls=g.s.balls.filter(b=>!b.temporary);g.power.step(.1);
 assert.equal(g.s.power.multiball,0);catchDrop(g,'shield');g.s.power.charges=0;g.power.step(.1);assert.equal(g.s.power.shield,0);
 for(let i=0;i<250;i++)g.power.step(.1);assert.equal(names(g,'powerWarn').length+names(g,'powerExpire').length,0);
});
test('a drop announces itself, sways inside the field, speeds up, and a miss speaks once',()=>{
 const f=fixture();f.power.drop({powerup:'laser',x:0,y:100,w:20,h:20});
 assert.deepEqual(f.events[0],['powerDrop',{kind:'laser',x:10,y:110}]);
 const d=f.s.power.drops[0];let v0=0,xs=new Set();
 for(let i=0;i<700&&f.s.power.drops.length;i++){f.power.step(1/120);if(i===0)v0=d.vy;xs.add(Math.round(d.x));assert.ok(d.x>=14&&d.x<=f.s.w-14);assert.equal(d.x,Math.min(f.s.w-14,Math.max(14,swayX(d))));}
 assert.ok(d.vy>v0);assert.ok(xs.size>1);
 assert.equal(names(f,'powerMiss').length,1);assert.equal(names(f,'powerMiss')[0][1].kind,'laser');assert.equal(f.s.power.drops.length,0);
});
test('the catch never got harder: anything the old straight 11 px reach caught is still caught',()=>{
 for(const edge of [-1,1])for(const ph of [0,1.5,3,4.7]){
  const f=fixture(),x0=640+edge*(80+11-6);                                              // the old reach, less the full sway
  f.s.power.drops.push({kind:'shield',x:x0,x0,y:600,age:3,ph});for(let i=0;i<120&&f.s.power.drops.length;i++)f.power.step(1/120);
  assert.equal(f.s.power.charges,1,`edge ${edge} phase ${ph}`);
 }
 assert.ok(DROP_REACH>=11);
});
test('grey and reset are silent: no shot, warning, expiry or miss after the colour is gone',()=>{
 const f=fixture();for(const k of ['multiball','shield','laser','fireball'])catchDrop(f,k);f.power.drop({powerup:'laser',x:620,y:100,w:40,h:20});
 const before=f.events.length;f.power.reset();for(let i=0;i<40;i++)f.power.step(.1);assert.equal(f.events.length,before);
 for(const k of ['laser','fireball'])catchDrop(f,k);f.s.power.drops.push({kind:'shield',x:20,y:600,age:0});
 const mid=f.events.length;f.s.state='grey';for(let i=0;i<120;i++)f.power.step(.1);assert.equal(f.events.length,mid);
 assert.equal(f.s.power.muzzle,0);assert.equal(f.s.power.eighth,null);
});
test('multiball copies wear their own tints and the split speaks after the catch',()=>{
 const f=fixture();catchDrop(f,'multiball');
 assert.deepEqual(f.s.balls.map(b=>b.tint|0),[0,1,2]);
 assert.deepEqual(f.events.map(e=>e[0]),['powerCatch','multiSplit']);assert.deepEqual(f.events[1][1],{x:640,y:600});
 const full=fixture({maxBalls:1});catchDrop(full,'multiball');assert.deepEqual(full.events.map(e=>e[0]),['powerCatch'],'no copies, no split');
});
test('a copy promoted to the last ball drops its tint: the player ball is violet again',()=>{
 const f=fixture();catchDrop(f,'multiball');f.s.balls.shift();f.power.step(13);
 assert.equal(f.s.balls.length,1);assert.equal(f.s.balls[0].temporary,false);assert.equal(f.s.balls[0].tint,0);
});
test('the random drop mix: multiball is the rare one, every kind still falls, one roll picks',()=>{
 const n={multiball:0,fireball:0,laser:0,shield:0};for(let i=0;i<1000;i++)n[pickPower(i/1000)]++;
 assert.deepEqual(n,{multiball:100,fireball:300,laser:300,shield:300});
 assert.equal(pickPower(0),'multiball');assert.equal(pickPower(1),'shield');assert.equal(pickPower(-1),'multiball');
 assert.ok(POWER_WEIGHT.multiball<POWER_WEIGHT.laser);
});

test('a power-up glyph in a brick bobs and breathes a little, never under reduced motion', async () => {
  const { glyphIdle, GLYPH_IDLE } = await import('./powerups-render.js');
  assert.deepEqual(glyphIdle(3.3, 100, 80, true), { dy: 0, scale: 1 });
  let lo = 9, hi = -9, sLo = 9, sHi = 0;
  for (let t = 0; t < 6; t += .02) { const i = glyphIdle(t, 100, 80); lo = Math.min(lo, i.dy); hi = Math.max(hi, i.dy); sLo = Math.min(sLo, i.scale); sHi = Math.max(sHi, i.scale); }
  assert.ok(hi > 1 && lo < -1 && hi <= GLYPH_IDLE.bob && lo >= -GLYPH_IDLE.bob, 'it moves, and only slightly');
  assert.ok(sHi > 1.05 && sLo < .95 && sHi <= 1.1 && sLo >= .9, 'it breathes within ten percent');
  assert.notEqual(glyphIdle(1, 100, 80).dy, glyphIdle(1, 182, 80).dy, 'neighbours are out of phase');
});
