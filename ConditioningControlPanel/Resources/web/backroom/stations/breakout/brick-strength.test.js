import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createGame} from './game.js';
import {distributeStrength,hasPayload,STRENGTH_BY_WALL} from './brick-strength.js';
const seeded=()=>{let seed=71;return()=>((seed=Math.imul(seed,1664525)+1013904223>>>0)/4294967296);};
for(const strength of [2,3])for(const state of ['grey','colour'])test(`${strength}-hit brick pays only on destruction in ${state}`,()=>{
  const events=[],game=createGame({rng:seeded(),onEvent:(n,d)=>events.push([n,d])}),s=game.snapshot();
  s.state=state;s.sat=state==='colour'?.5:0;s.balls=[];s.wallAge=2;
  const br=s.bricks.find(b=>b.strength===strength),i=s.bricks.indexOf(br);
  assert.ok(br);events.length=0;const sat=s.sat;
  for(let hit=1;hit<strength;hit++){
    game.breakBrick(i);assert.equal(br.alive,true);assert.equal(br.hp,strength-hit);
    assert.equal(s.stats.bricks,0);assert.equal(s.greyBricks,0);assert.equal(s.combo,0);assert.equal(s.sat,sat);
    assert.equal(events.some(([n])=>['brick','word','split','jackpot','spellFill'].includes(n)),false);
  }
  assert.equal(events.filter(([n])=>n==='brickDamage').length,strength-1);
  game.breakBrick(i);assert.equal(br.alive,false);assert.equal(br.hp,0);assert.equal(s.stats.bricks,1);
  assert.equal(events.filter(([n])=>n==='brick').length,1);assert.equal(s.greyBricks,state==='grey'?1:0);
});
test('durability keeps real ball rebounds and survives breakout and relapse',()=>{
 const game=createGame({rng:seeded()}),s=game.snapshot(),br=s.bricks.find(b=>b.strength===3);
 s.bricks=[br];s.wallAge=2;Object.assign(br,{x:600,y:200,w:60,h:24,angle:0});
 const b=s.balls[0];Object.assign(b,{stuck:false,x:630,y:233,vx:0,vy:-220});
 game.step(.02);assert.equal(br.hp,2);assert.ok(b.vy>0,'surviving brick reflects the ball');
 b.stuck=true;game.breakoutNow();for(let i=0;i<5;i++)game.step(.1);
 assert.equal(s.state,'colour');assert.equal(br.hp,2);game.relapseNow();for(let i=0;i<8;i++)game.step(.1);
 assert.equal(s.state,'grey');assert.equal(br.hp,2);
});
test('placement favours payload neighbours without replacing effects or bespoke mechanisms',()=>{
 const plain=x=>({alive:true,x,y:100,w:30,h:20,gif:-1});
 const payload={...plain(100),split:true};const near=plain(135),far=plain(900),core={...plain(110),irisCore:true,hp:3};
 const stream={...plain(115),irisAge:2};const guard={...plain(120),pendulumGuard:true};
 distributeStrength([far,payload,core,stream,guard,near],{two:.5,three:0},()=>.5);
 assert.equal(near.strength,2);assert.equal(far.strength,undefined);
 for(const b of [payload,core,stream,guard])assert.equal(b.strength,undefined);
 assert.equal(payload.split,true);assert.equal(core.hp,3);
});
test('default levels distribute both strengths without hiding payloads; mix remains configurable',()=>{
 const game=createGame({rng:seeded()});
 for(let wall=1;wall<=7;wall++){
  game.jumpToWall(wall);const s=game.snapshot();
  assert.ok(s.bricks.some(b=>b.strength===2),`wall ${wall} two-hit`);
  assert.ok(s.bricks.some(b=>b.strength===3),`wall ${wall} three-hit`);
  assert.ok(s.bricks.filter(b=>b.strength).every(b=>!hasPayload(b)&&(b.hp===b.strength||((b.pendulumGuard||b.curtain)&&b.strength===3&&b.hp===2))));
 }
 game.jumpToWall(8);assert.ok(game.snapshot().bricks.every(b=>!b.strength),'scripted finale kept intact');
 const off=createGame({brickStrength:[]});assert.ok(off.snapshot().bricks.every(b=>!b.strength));
 assert.ok(STRENGTH_BY_WALL[6].two>STRENGTH_BY_WALL[0].two);
});
test('released pendulum destroys its reinforced curtain instead of leaving frozen survivors',()=>{
 const game=createGame({rng:seeded()} );game.jumpToWall(7);const s=game.snapshot();s.state='colour';s.sat=.5;s.balls=[];
 const anchor=s.bricks.find(b=>b.pendulumAnchor),curtain=s.bricks.filter(b=>b.curtain&&b.pendulumId===anchor.pendulumId);
 assert.ok(curtain.some(b=>b.strength));
 for(const b of s.bricks.filter(b=>b.pendulumGuard&&b.pendulumId===anchor.pendulumId))game.breakBrick(s.bricks.indexOf(b));
 for(let i=0;i<3;i++)game.breakBrick(s.bricks.indexOf(anchor));
 for(let i=0;i<500;i++)game.step(.01);
 assert.ok(curtain.every(b=>!b.alive));
});
