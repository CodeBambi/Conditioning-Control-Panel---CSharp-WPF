import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createGame} from './game.js';
import {distributeGreyMetal,permanentBrick} from './grey-metal.js';
const seeded=()=>{let n=81;return()=>((n=Math.imul(n,1664525)+1013904223>>>0)/4294967296);};
const plain=i=>({alive:true,x:i*40,y:80,w:30,h:20,row:0,col:i,gif:-1});
test('metal leaves breakout requirement plus 25 percent or three extra permanent bricks',()=>{
 for(const n of [1,4,16,30])for(const total of [2,5,18,24,70]){
  const bricks=Array.from({length:total},(_,i)=>plain(i));
  const count=distributeGreyMetal(bricks,n);
  assert.equal(count,bricks.filter(b=>b.greyMetal).length);
  if(count)assert.ok(total-count>=n+Math.max(3,Math.ceil(n*.25)));
  if(total<=n+Math.max(3,Math.ceil(n*.25)))assert.equal(count,0);
 }
});
test('temporary streams and bespoke targets cannot fund or receive metal',()=>{
 const bricks=Array.from({length:20},(_,i)=>({...plain(i),irisAge:0}));
 bricks.push(...Array.from({length:5},(_,i)=>plain(i)));
 assert.equal(distributeGreyMetal(bricks,16),0);
 const special=[{split:true},{word:'SINK'},{gif:0},{irisCore:true,hp:3},{pendulumAnchor:true,hp:3},{pendulumGuard:true},{strength:3,hp:3}];
 const all=[...Array.from({length:70},(_,i)=>plain(i)),...special.map((extra,i)=>({...plain(i),...extra}))];
 distributeGreyMetal(all,16);assert.ok(all.slice(70).every(b=>!b.greyMetal));
});
test('ordinary levels receive metal; locked hits pay nothing and colour restores breakability',()=>{
 const events=[],game=createGame({rng:seeded(),onEvent:(n)=>events.push(n)}),s=game.snapshot();
 for(let wall=1;wall<=7;wall++){
  game.jumpToWall(wall);const metals=s.bricks.filter(b=>b.greyMetal);
  assert.ok(metals.length,`wall ${wall}`);
  assert.ok(s.bricks.filter(b=>permanentBrick(b)&&!b.greyMetal).length>=s.breakoutN+Math.max(3,Math.ceil(s.breakoutN*.25)));
  const br=metals[0],i=s.bricks.indexOf(br),before=s.stats.bricks;events.length=0;
  game.breakBrick(i);assert.ok(br.alive);assert.equal(s.stats.bricks,before);assert.equal(s.greyBricks,0);
  assert.deepEqual(events,['metalHit']);
  s.state='colour';game.breakBrick(i);assert.equal(br.alive,false);s.state='grey';
 }
 game.jumpToWall(8);assert.ok(s.bricks.every(b=>!b.greyMetal));
});
test('relapse recalculates metal and removes it when too few bricks remain',()=>{
 const game=createGame({rng:seeded()}),s=game.snapshot();
 const metal=s.bricks.find(b=>b.greyMetal);s.bricks=[metal,...s.bricks.filter(b=>b!==metal).slice(0,2)];
 s.state='colour';game.relapseNow();for(let i=0;i<9;i++)game.step(.1);
 assert.equal(s.state,'grey');assert.ok(s.bricks.every(b=>!b.greyMetal));
});
test('breakout can be reached while grey metal preserves pieces for colour',()=>{
 const game=createGame({rng:seeded()}),s=game.snapshot();s.balls=[];
 for(const b of s.bricks.filter(b=>!b.greyMetal)){
  while(b.alive&&!s.pendingBreakout)game.breakBrick(s.bricks.indexOf(b));
  if(s.pendingBreakout)break;
 }
 assert.ok(s.pendingBreakout);assert.ok(s.bricks.some(b=>b.alive&&b.greyMetal));
 for(let i=0;i<5;i++)game.step(.1);assert.equal(s.state,'colour');
});

test('metal collision rebounds without damage and higher escape requirements remove unsafe metal',()=>{
 const game=createGame({rng:seeded()}),s=game.snapshot(),br=s.bricks.find(b=>b.greyMetal);
 s.bricks=[br];s.wallAge=2;Object.assign(br,{x:600,y:200,w:60,h:24,angle:0});
 Object.assign(s.balls[0],{stuck:false,x:630,y:233,vx:0,vy:-220});
 game.step(.02);assert.ok(s.balls[0].vy>0);assert.ok(br.alive);assert.equal(s.greyBricks,0);
 game.setBreakoutN(30);assert.equal(br.greyMetal,false);
});
