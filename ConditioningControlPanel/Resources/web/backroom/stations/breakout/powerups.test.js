import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createGame} from './game.js';
import {createPowerups,ordinaryTarget} from './powerups.js';
function fixture(){
 const events=[],s={w:1280,h:720,state:'colour',wallAge:3,paddle:{x:640,y:680,w:160,h:12},balls:[{x:640,y:600,vx:30,vy:-220}],bricks:[]};
 const damage=[];const power=createPowerups(s,{rng:()=>.5,emit:(...e)=>events.push(e),newBall:()=>({r:7,trail:[]}),damage:br=>damage.push(br)});
 return {s,power,events,damage};
}
function catchDrop(f,kind){f.s.power.drops.push({kind,x:640,y:660,age:0});f.power.step(.1);}
test('all four pickups activate only after a paddle catch; missed drops do nothing',()=>{
 for(const kind of ['multiball','fireball','laser','shield']){
  const f=fixture();f.s.power.drops.push({kind,x:20,y:660,age:0});f.power.step(.5);assert.equal(f.s.power[kind],0);assert.equal(f.events.length,0);
  catchDrop(f,kind);assert.ok(f.s.power[kind]>0);assert.equal(f.events[0][0],'powerCatch');
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
