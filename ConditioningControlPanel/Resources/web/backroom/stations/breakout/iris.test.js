import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createGame} from './game.js';
import {irisPose,IRIS_LIFE} from './iris.js';
function rig(){const events=[],game=createGame({rng:()=>.6,onEvent:(n,d)=>events.push([n,d])});game.jumpToWall(6);const s=game.snapshot();s.balls=[];return {game,s,events,advance(t){for(let i=0;i<t*100;i++)game.step(.01);}};}
test('iris has six outer cores, dense streams and bounded geometry through a full turn',()=>{
 const {s}=rig();assert.equal(s.bricks.filter(b=>b.irisCore).length,6);assert.ok(s.bricks.length>110);
 for(let rotation=0;rotation<7;rotation+=.2)for(let arm=0;arm<6;arm++)for(let age=0;age<28;age++){
  const p=irisPose(arm,age,1280,720,rotation);assert.ok(p.x>90&&p.x+p.w<1190);assert.ok(p.y>50&&p.y+p.h<555);
 }
});
test('streams move, rotate and consume bricks without rewarding automatic consumption',()=>{
 const {s,advance,events}=rig(), b=s.bricks.find(b=>!b.irisCore&&!b.irisGuard),x=b.x,coreX=s.bricks[0].x;
 advance(2);assert.notEqual(b.x,x);assert.notEqual(s.bricks[0].x,coreX);
 advance(60);assert.equal(events.some(([name])=>['brick','hit','irisCore','burst'].includes(name)),false,'consumption emits no impact feedback');assert.ok(s.bricks.length<=168);assert.equal(s.stats.bricks,0);assert.equal(s.stats.walls,5);
 assert.ok(!s.bricks.includes(b));assert.equal(irisPose(0,IRIS_LIFE).irisAlpha,0);
});
test('destroyed core stops only its own stream; destroying all cores lets the wall finish',()=>{
 const {game,s,events,advance}=rig();game.breakoutNow();advance(.5);const first=s.bricks.find(b=>b.irisCore&&b.arm===0);
 game.breakBrick(s.bricks.indexOf(first));assert.equal(first.hp,2);assert.equal(first.alive,true);
 game.breakBrick(s.bricks.indexOf(first));assert.equal(first.hp,1);assert.equal(first.alive,true);
 assert.equal(s.stats.bricks,0);assert.equal(events.filter(e=>e[0]==='irisCore').length,0);
 game.breakBrick(s.bricks.indexOf(first));
 advance(29);assert.equal(s.bricks.filter(b=>b.arm===0&&!b.irisGuard).length,0);assert.equal(s.bricks.filter(b=>b.irisCore).length,5);
 assert.equal(events.filter(e=>e[0]==='irisCore').length,1);
 for(const core of s.bricks.filter(b=>b.irisCore))for(let i=0;i<3;i++)game.breakBrick(s.bricks.indexOf(core));
 for(const guard of s.bricks.filter(b=>b.irisGuard))while(guard.alive)game.breakBrick(s.bricks.indexOf(guard));
 advance(29);assert.equal(s.stats.walls,6);assert.equal(s.iris,null);
});

test('iris stays horizontally elliptical and centred while rotating',()=>{
 for(let r=0;r<7;r+=.2){const p=irisPose(0,0,1280,720,r);
 assert.ok(Math.abs(Math.hypot((p.x+p.w/2-640)/374.4,(p.y+p.h/2-302.4)/208.8)-1)<1e-8);}
});

test('six guards per core follow rotation and never respawn after a hit',()=>{
 const {game,s,advance}=rig();assert.equal(s.bricks.filter(b=>b.irisGuard).length,36);
 const guard=s.bricks.find(b=>b.irisGuard), x=guard.x;advance(1);assert.notEqual(guard.x,x);
 game.breakBrick(s.bricks.indexOf(guard));advance(30);
 assert.equal(s.bricks.filter(b=>b.irisGuard).length,35);
 assert.ok(s.bricks.every(b=>Number.isFinite(b.x)&&Number.isFinite(b.y)));
});
