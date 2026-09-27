import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createGame} from './game.js';
import {advanceEndlessDemolition} from './endless-physics.js';
import {brickOverlap} from './placement.js';
const mouths=(angle=0)=>[{id:'in',pair:'p',x:200,y:300,angle,halfLength:150},
 {id:'out',pair:'p',x:900,y:300,angle:Math.PI,halfLength:150}];
function rig() {
 const events=[],game=createGame({endless:true,seed:2709,from:3,savedSaturation:.8,rng:()=>.6,
 onEvent:(name,data)=>events.push({name,...data})});
 const g=game.snapshot();g.balls=[];g.portals=mouths();g.well=null;g.dome=false;
 for(const br of g.bricks){br.x=30;br.y=20;}
 return {game,g,events,advance(n){for(let i=0;i<n;i++)game.step(1/120);}};
}
test('normal, multiball and fire balls traverse without duplicating rewards or contacts',()=>{
 for(const variant of ['normal','multi','fire']) {
  const {game,g,events,advance}=rig();
  const b={x:202,y:300,vx:-400,vy:0,r:8,stuck:false,lost:false,falling:false,ghost:false,
   orbit:null,spin:0,trail:[],squash:0,aimed:10,temporary:variant==='multi',fireContacts:new Set([g.bricks[0]])};
  g.balls=[b];if(variant==='multi'){g.power.multiball=5;g.balls.push({...b,x:500,stuck:true,temporary:false});}
  if(variant==='fire')g.power.fireball=5;
  const before=g.stats.bricks;advance(2);
  assert.equal(events.filter(e=>e.name==='portalTransit'&&e.kind==='ball').length,1);
  assert.ok(b.x>800&&b.vx<0);assert.equal(g.stats.bricks,before);assert.equal(b.fireContacts.size,0);
 }
});
test('power-up continues on its exit velocity and can be caught exactly once',()=>{
 const {g,events,advance}=rig();g.portals=mouths(-Math.PI/2);
 const d={kind:'shield',x:200,y:299,x0:200,age:.5,vy:190,ph:0};g.power.drops=[d];
 advance(2);assert.equal(events.filter(e=>e.kind==='powerup').length,1);assert.ok(d.portalMotion&&d.vx<0);
 const x=d.x,age=d.age;advance(2);assert.ok(d.x<x);assert.ok(d.age>age);assert.ok(d.x>700);
 d.x=g.paddle.x;d.y=g.paddle.y-g.paddle.h/2-10;d.vx=0;d.vy=190;advance(10);
 assert.equal(events.filter(e=>e.name==='powerCatch').length,1);assert.equal(g.power.charges,1);
});
test('picture and spiral seeds transit before an expired seed blooms near the exit',()=>{
 for(const spiral of [null,'whirl']) {
  const {g,events,advance}=rig();g.portals=mouths(-Math.PI/2);
  const p={x:200,y:299,vx:0,vy:180,w:30,h:20,t:1.19,life:.5,rot:0,vr:1,gif:0,
   spiral,hue:0,spin:1,tier:1,color:'#fff',done:false};g.pops=[p];advance(1);
  const transit=events.find(e=>e.kind==='pop'),burst=events.find(e=>e.name==='burst');
  assert.ok(transit&&burst);assert.ok(events.indexOf(transit)<events.indexOf(burst));
  assert.ok(burst.x>700);assert.ok(p.t>1.19);assert.equal(g.pops.length,0);
  assert.equal(spiral?!!g.well:g.colliders.length===1,true);
 }
});
test('large picture bubbles touch a narrow mouth and retain age and hit count',()=>{
 const {g,events,advance}=rig();g.portals.forEach(p=>p.halfLength=35);
 const c={x:200.05,y:300,r:60,vx:-24,vy:0,hits:2,pulse:0,alpha:1,fading:false,solid:true,
  gif:0,tier:3,age:4,jelly:0,jnx:0,jny:0,ph:0};g.colliders=[c];advance(1);
 assert.equal(events.filter(e=>e.kind==='bubble').length,1);assert.ok(c.x>750);
 assert.equal(c.hits,2);assert.ok(c.age>4);assert.equal(g.colliders.length,1);
});
test('laser bolts turn through a portal and expire at arena bounds',()=>{
 const {g,events,advance}=rig();g.portals=mouths(Math.PI/2);
 const shot={x:200,y:301,r:3};g.power.shots=[shot];advance(1);
 assert.equal(events.filter(e=>e.kind==='shot').length,1);assert.ok(shot.vx<0);
 advance(400);assert.equal(g.power.shots.length,0);
});
test('released sweep becomes free flight without resetting its original lifetime',()=>{
 const {g,events}=rig(),p={x:198,y:300,r:26,mode:'sweep',age:1,trail:[],struck:new Set(),pulse:0};
 const emit=(name,data)=>events.push({name,...data});
 advanceEndlessDemolition(p,g,.01,emit,{x:202,y:300});
 assert.equal(p.mode,'flight');assert.equal(p.age,1);assert.ok(p.x>800&&p.vx<0);
 assert.equal(p.portalFlightLimit,12.4);assert.equal(events.filter(e=>e.kind==='demolition').length,1);
 p.age=12.399;advanceEndlessDemolition(p,g,.01,emit);assert.equal(p.mode,'spent');
});
test('Story has no portals and the next Endless board replaces all old endpoints',()=>{
 const story=createGame();story.jumpToWall(7);assert.deepEqual(story.snapshot().portals,[]);
 const {game,g}=rig();game.jumpToWall(1);assert.ok(g.portals.every(p=>p.pair!=='p'));
});

test('authored delivery seeds reach a low mouth before blooming without renewing lifetime',()=>{
 const {g,events,advance}=rig();g.portals=mouths(-Math.PI/2);g.portals[0].y=390;
 const p={x:200,y:300,vx:0,vy:300,w:30,h:20,t:.6,life:.5,rot:0,vr:0,gif:0,
  spiral:null,tier:1,color:'#fff',done:false,portalCargo:true};g.pops=[p];
 advance(1);assert.equal(p.done,false);assert.equal(g.pops.length,1);
 advance(25);assert.ok(events.some(e=>e.kind==='pop'));assert.equal(p.done,true);
 assert.ok(p.t<.9);assert.equal(events.filter(e=>e.name==='burst').length,1);
 const fallback={...p,x:600,y:300,t:.91,done:false};g.pops=[fallback];advance(1);
 assert.equal(fallback.done,true,'missed mouths cannot hold a seed indefinitely');
});

test('reduced motion retains physical picture and spiral delivery without tumbling',()=>{
 for(const spiral of [null,'whirl']) {
  const {game,g,events,advance}=rig();game.setReduced(true);
  g.portals=mouths(-Math.PI/2);g.portals[0].y=390;
  const br=g.bricks[0];Object.assign(br,{x:185,y:100,w:30,h:20,hp:1,strength:0,
   gif:spiral?-1:0,spiral,portalCargo:true,split:false,word:null,powerup:null,jackpot:false});
  game.breakBrick(0);assert.equal(g.pops.length,1,'cargo cannot burst before reaching its mouth');
  const pop=g.pops[0];advance(20);assert.equal(pop.rot,0);assert.equal(pop.vr,0);
  advance(100);assert.equal(events.filter(e=>e.kind==='pop').length,1);
  assert.equal(events.filter(e=>e.name==='burst').length,1);assert.equal(pop.done,true);
  assert.ok(events.find(e=>e.name==='burst').x>700);
 }
});

test('real balls transit from the back and through a grazing rim without centre crossing',()=>{
 for(const [x,y,vx] of [[178,300,400],[216,370,-400]]) {
  const {g,events,advance}=rig();g.portals.forEach(p=>p.halfLength=65);
  const b={x,y,vx,vy:0,r:8,stuck:false,lost:false,falling:false,ghost:false,
   orbit:null,spin:0,trail:[],squash:0,aimed:10};g.balls=[b];advance(3);
  const event=events.find(e=>e.name==='portalTransit'&&e.kind==='ball');assert.ok(event);
  assert.equal(event.entrySide,vx>0?-1:1);
  assert.ok(vx>0?b.x>900:b.x<900);assert.ok(b.x>800);
 }
});
test('a grown bubble is seated safely only when its portal exit is obstructed',()=>{
 const {g,events,advance}=rig();g.portals.forEach(p=>p.halfLength=65);
 Object.assign(g.bricks[0],{x:740,y:270,w:80,h:70,angle:0});
 const c={x:320,y:300,r:104,vx:-24,vy:0,hits:2,pulse:0,alpha:1,fading:false,solid:true,
  gif:0,tier:3,age:4,jelly:0,jnx:0,jny:0,ph:0};g.colliders=[c];advance(1);
 const event=events.find(e=>e.kind==='bubble');assert.ok(event);
 assert.equal(brickOverlap(c.x,c.y,c.r*1.15,g.bricks),0);
 assert.ok(Math.hypot(c.x-event.x,c.y-event.y)>1);assert.equal(c.hits,2);assert.ok(c.age>4);
 advance(60);assert.equal(events.filter(e=>e.kind==='bubble').length,1,'seating must not drop the bubble into another mouth');
});
