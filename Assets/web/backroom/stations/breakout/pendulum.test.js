import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createGame} from './game.js';
import {createPendulums,advancePendulum,curtainPose,releasePendulum,collidePendulum} from './pendulum.js';
function rig(reduced=false) {
 const events=[],game=createGame({rng:()=>.6,reduced,onEvent:(n,d)=>events.push([n,d])});
 game.jumpToWall(7);const s=game.snapshot();s.state='colour';s.sat=.6;s.balls=[];
 return {game,s,events,advance(t){for(let i=0;i<t*100;i++)game.step(.01);}}; }
for(const reduced of [false,true])test(`pendulum geometry remains above paddle, reduced=${reduced}`,()=>{
 const ps=createPendulums(1280,720);
 for(const p of ps){p.energy=1;for(let i=0;i<2000;i++){
  advancePendulum(p,.01,1280,720,reduced);
  assert.ok(p.x-p.r>0 && p.x+p.r<1280 && p.y+p.r<560);
  for(let row=0;row<8;row++)for(let col=0;col<6;col++){
   const b=curtainPose(p,row,col);assert.ok(b.x>0 && b.x+b.w<1280 && b.y>0 && b.y+b.h<560);
  }
 }
 releasePendulum(p,1280);for(let i=0;i<1300;i++){
  advancePendulum(p,.01,1280,720,reduced);assert.ok(p.y-p.r>0 && p.y+p.r<560);
 }assert.equal(p.mode,'spent');assert.ok(p.trail.length<=18);
 }
});
test('anchor release collapses only finite existing bricks and enables neighbouring chain reaction',()=>{
 const {game,s,advance,events}=rig();assert.equal(s.bricks.length,171);
 const p=s.pendulums[0],anchor=s.bricks.find(b=>b.pendulumAnchor);
 // Prepare the neighbouring target so a wrecking sweep can finish a player-earned chain.
 for(const b of s.bricks.filter(b=>b.pendulumGuard))while(b.alive)game.breakBrick(s.bricks.indexOf(b));
 for(const b of s.bricks.filter(b=>b.pendulumAnchor&&b!==anchor)) {game.breakBrick(s.bricks.indexOf(b));game.breakBrick(s.bricks.indexOf(b));}
 game.breakBrick(s.bricks.indexOf(anchor));game.breakBrick(s.bricks.indexOf(anchor));
 assert.equal(p.mode,'hung');game.breakBrick(s.bricks.indexOf(anchor));assert.equal(p.mode,'sweep');
 advance(1.5);assert.ok(s.bricks.filter(b=>b.alive&&b.curtain&&b.pendulumId===0).length<48);
 assert.ok(events.some(e=>e[0]==='pendulumRelease'));
 advance(2);assert.ok(events.filter(e=>e[0]==='pendulumRelease').length>=2,'sweep releases another anchor');
 assert.ok(s.stats.walls===7 || s.bricks.length<=171);
});
test('bob impacts reflect at capped speed, build energy, and do not repeatedly hit a separating ball',()=>{
 const p=createPendulums(1280,720)[0];advancePendulum(p,0,1280,720,false);
 const b={x:p.x,y:p.y+34,r:8,vx:0,vy:-900};
 assert.equal(collidePendulum(b,p,400),true);assert.ok(b.vy>0);
 assert.ok(Math.abs(Math.hypot(b.vx,b.vy)-400)<1e-6);assert.equal(p.energy,.34);
 assert.equal(collidePendulum(b,p,400),false);
});
test('grey final brick keeps earning escape and colour completion clears pendulum state',()=>{
 const {game,s}=rig();s.state='grey';s.breakoutN=1000;
 s.bricks.forEach(b=>b.alive=false);const last=s.bricks.find(b=>b.curtain);last.alive=true;
 game.breakBrick(s.bricks.indexOf(last));assert.equal(last.alive,true);assert.equal(s.greyBricks,1);
 s.state='colour';while(last.alive)game.breakBrick(s.bricks.indexOf(last));assert.equal(s.stats.walls,7);assert.equal(s.pendulums,null);
});
test('wall entrance holds the pendulum simulation and reduced motion fixes the curtains',()=>{
 const {game,s,advance}=rig(true);const p=s.pendulums[1],x=p.x;
 game.replayEntrance();advance(1);assert.equal(p.age,0);advance(2);assert.equal(p.x,x);assert.equal(p.angle,0);
});

test('anchor takes three hits even while its reinforced shields survive',()=>{
 const {game,s,events}=rig(),anchor=s.bricks.find(b=>b.pendulumAnchor),index=s.bricks.indexOf(anchor);
 const guards=s.bricks.filter(b=>b.pendulumGuard&&b.pendulumId===anchor.pendulumId);
 assert.equal(guards.length,8);assert.equal(s.bricks.filter(b=>b.curtain).length,144);
 for(const guard of guards)assert.deepEqual([guard.strength,guard.hp,guard.alive],[3,(guard.col+guard.pendulumId)%3===0?2:3,true]);
 game.breakBrick(index);assert.equal(anchor.hp,2);assert.equal(s.pendulums[0].mode,'hung');
 game.breakBrick(index);assert.equal(anchor.hp,1);assert.equal(s.pendulums[0].mode,'hung');
 game.breakBrick(index);assert.equal(anchor.alive,false);assert.equal(s.pendulums[0].mode,'sweep');
 assert.ok(guards.every(b=>b.alive&&b.hp===((b.col+b.pendulumId)%3===0?2:3)),'remaining shields do not lock a reachable hinge');
 assert.equal(events.filter(e=>e[0]==='pendulumAnchorHit').length,2);
});
for(const state of ['grey','colour'])test(`pendulum shields crack over three hits in ${state}`,()=>{
 const {game,s,events}=rig();s.state=state;s.breakoutN=1000;
 const guard=s.bricks.find(b=>b.pendulumGuard&&b.hp===3),index=s.bricks.indexOf(guard),before=s.stats.bricks;
 for(const hp of [2,1]) {
  game.breakBrick(index);assert.equal(guard.hp,hp);assert.equal(guard.alive,true);
  assert.equal(s.stats.bricks,before);assert.equal(s.greyBricks,0);
 }
 assert.deepEqual(events.filter(e=>e[0]==='brickDamage').map(e=>e[1].hp),[2,1]);
 game.breakBrick(index);assert.equal(guard.hp,0);assert.equal(guard.alive,false);
 assert.equal(s.stats.bricks,before+1);
 assert.equal(s.greyBricks,state==='grey'?1:0);
});

for(const state of ['grey','colour'])test(`curtain perimeter requires three hits in ${state}`,()=>{
 const {game,s,events}=rig();s.state=state;s.breakoutN=1000;
 const edges=s.bricks.filter(b=>b.curtain&&(b.curtainRow===0||b.curtainRow===7||b.curtainCol===0||b.curtainCol===5));
 assert.equal(edges.length,72);
 assert.ok(edges.every(b=>b.strength===3&&(b.hp===2||b.hp===3)&&b.gif===-1&&!b.word&&!b.spiral&&!b.split&&!b.greyMetal));
 assert.equal(edges.filter(b=>b.hp===2).length,24,'one third of the border starts weathered');
 const brick=edges.find(b=>b.hp===3),index=s.bricks.indexOf(brick),before=s.stats.bricks;
 game.breakBrick(index);game.breakBrick(index);
 assert.equal(brick.alive,true);assert.equal(brick.hp,1);assert.equal(s.stats.bricks,before);assert.equal(s.greyBricks,0);
 game.breakBrick(index);assert.equal(brick.alive,false);assert.equal(s.stats.bricks,before+1);
 assert.equal(events.filter(e=>e[0]==='brickDamage').length,2);
});

test('weathered hinge shields and curtain edges need only two remaining hits',()=>{
 const {game,s}=rig();
 for(const kind of ['pendulumGuard','curtain']) {
  const worn=s.bricks.find(b=>b[kind]&&b.strength===3&&b.hp===2),i=s.bricks.indexOf(worn);
  assert.ok(worn);game.breakBrick(i);assert.equal(worn.hp,1);assert.equal(worn.alive,true);
  game.breakBrick(i);assert.equal(worn.alive,false);
 }
 const shields=s.bricks.filter(b=>b.pendulumGuard);
 assert.equal(shields.filter(b=>(b.col+b.pendulumId)%3===0).length,8);
});
