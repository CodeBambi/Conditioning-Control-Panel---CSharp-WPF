import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createGame} from './game.js';
const advance=(game,t,input={})=>{for(let n=0;n<Math.ceil(t/.05);n++)game.step(.05,input);};
function setup(){const calls=[];const game=createGame({rng:()=>.5,audio:{metal:d=>calls.push(d)}});game.jumpToWall(8);return {game,s:game.snapshot(),calls};}
function locked(){const x=setup();advance(x.game,2);x.game.step(.02,{launch:true});x.game.breakBrick(0);advance(x.game,4.05);return x;}
test('finale forms in colour, awaits explicit launch and intercepts first contact before reflection or damage',()=>{
 const {game,s}=setup();assert.equal(s.state,'colour');advance(game,3);assert.equal(s.finale.phase,'ready');assert.equal(s.balls[0].stuck,true);
 game.step(.02,{launch:true});assert.equal(s.finale.phase,'approach');
 const br=s.bricks[0];br.angle=0;const b=s.balls[0];b.x=br.x+br.w/2;b.y=br.y+br.h+b.r+1;b.vx=0;b.vy=-400;
 game.step(.02);assert.equal(s.finale.phase,'interrupt');assert.equal(br.alive,true);assert.ok(b.vy<0);const y=b.y;
 advance(game,.5);assert.equal(b.y,y);assert.equal(s.stats.bricks,0);
 advance(game,.55);assert.equal(b.y,y);assert.equal(s.finale.phase,'interrupt');
 advance(game,3);assert.equal(s.state,'grey');assert.equal(s.finale.phase,'locked');assert.equal(s.balls[0].stuck,true);
});
test('sealed ring makes metal contacts, words alone add three and unlock, preserving normal counter',()=>{
 const {game,s,calls}=locked();assert.equal(s.breakoutN,20);game.breakBrick(0);assert.equal(s.bricks[0].alive,true);assert.equal(calls.length,1);assert.equal(s.greyBricks,0);
 advance(game,30);
 for(let hit=0;hit<Math.ceil(s.breakoutN/3);hit++) {
  advance(game,3.1);const index=s.bricks.findIndex(br=>br.alive&&br.finaleWord);assert.ok(index>=0);game.breakBrick(index);
  if((hit+1)*3<s.breakoutN)assert.equal(s.greyBricks,(hit+1)*3);
 }
 advance(game,.5);assert.equal(s.state,'colour');assert.equal(s.finale.phase,'released');assert.equal(s.bricks.some(br=>br.finaleWord),false);
 const ordinary=s.bricks.findIndex(b=>b.alive&&!b.finaleMetal&&!b.strength);game.breakBrick(ordinary);assert.equal(s.bricks[ordinary].alive,false);
});
test('word targets stay bounded and outside ring; lost grey ball retains progress',()=>{
 const {game,s}=locked();advance(game,34);const words=s.bricks.filter(br=>br.finaleWord);assert.ok(words.length<=4);assert.ok(words.length>0);
 for(const br of words)assert.ok(Math.hypot(br.x+br.w/2-s.finale.centreX,br.y+br.h/2-s.finale.centreY)>180);
 game.breakBrick(s.bricks.indexOf(words[0]));game.loseBall();assert.equal(s.finale.phase,'locked');assert.equal(s.greyBricks,3);assert.equal(s.balls[0].stuck,true);
});
test('last wall7 grey brick cannot die but metal hits finish counter without softlock',()=>{
 const game=createGame({rng:()=>.5,breakoutN:3});game.jumpToWall(7);const s=game.snapshot();s.bricks.slice(1).forEach(br=>br.alive=false);
 for(let i=0;i<3;i++)game.breakBrick(0);assert.equal(s.bricks[0].alive,true);advance(game,.5);assert.equal(s.state,'colour');
 for(let i=0;i<3;i++)game.breakBrick(0);assert.equal(s.stats.walls,7);assert.equal(s.finale.phase,'forming');assert.equal(s.balls[0].stuck,true);
});

test('raised smaller finale leaves paddle space and defense breaks without triggering interruption',()=>{
 const {game,s}=setup();advance(game,2);game.step(.02,{launch:true});
 const rings=s.bricks.filter(br=>br.finaleRing);assert.ok(rings.every(br=>br.y+br.h<400));
 const defense=s.bricks.filter(br=>br.finaleDefense);assert.equal(defense.length,26);
 assert.ok(defense[0].x<=2);assert.ok(defense.at(-1).x+defense.at(-1).w>=s.w-2);
 const br=defense[13],b=s.balls[0];b.x=br.x+br.w/2;b.y=br.y+br.h+b.r+1;b.vx=0;b.vy=-400;
 game.step(.02);assert.equal(br.alive,false);assert.equal(s.finale.phase,'approach');assert.ok(b.vy>0);
 game.breakBrick(0);advance(game,4.05);assert.equal(s.finale.phase,'locked');assert.equal(br.alive,false);
});
test('grey word delay, independent expiry and paced arrivals',()=>{
 const {game,s}=locked();advance(game,29);assert.equal(s.bricks.some(br=>br.finaleWord),false);
 advance(game,1.1);const first=s.bricks.find(br=>br.finaleWord);assert.ok(first);assert.ok(first.y<s.h*.45);
 advance(game,2);assert.equal(s.bricks.filter(br=>br.finaleWord).length,1);
 advance(game,1.1);assert.equal(s.bricks.filter(br=>br.finaleWord).length,2);
 advance(game,2.5);assert.equal(first.alive,false);assert.ok(s.finale.bursts.length>0);
});
test('inner breach builds continuous bounded flared arms without resetting ball, and keeps feeding after defensive hits',()=>{
 const {game,s}=setup();game.jumpToFinaleBeat('rings');s.wallAge=2;
 const ball=s.balls[0],x=ball.x,y=ball.y;
 s.finale.stageAge=300;advance(game,.02);
 assert.equal(s.finale.stage,2);assert.equal(s.balls[0],ball);assert.equal(ball.x,x);assert.equal(ball.y,y);
 const initial=s.bricks.filter(br=>br.finaleFeed);assert.ok(initial.some(br=>br.x<0||br.x>s.w||br.y<0||br.y>s.h));
 advance(game,2);const feed=s.bricks.find(br=>br.finaleFeed);const before={x:feed.x,y:feed.y};
 advance(game,1);assert.ok(feed.x!==before.x||feed.y!==before.y);
 advance(game,70);assert.ok(s.bricks.length<=248);assert.equal(s.finale.stage,2);
 const gates=s.bricks.filter(br=>br.finaleGate);
 assert.equal(gates.length,34);
 assert.ok(gates.filter(b=>b.gateRing===0).every(b=>b.strength===3));
 assert.ok(gates.filter(b=>b.gateRing===1).every(b=>b.strength===3));
 const gate=gates.find(b=>!b.finaleMetal),index=s.bricks.indexOf(gate);
 for(let i=0;i<3;i++)game.breakBrick(index);
 assert.equal(gate.alive,false);assert.equal(s.finale.stageComplete,false);
 const feedIndex=s.finale.feedIndex;advance(game,5);assert.ok(s.finale.feedIndex>feedIndex);
 for(const br of s.bricks.filter(b=>b.finaleFeed))assert.ok(Math.hypot(br.x+br.w/2-s.finale.centreX,br.y+br.h/2-s.finale.centreY)>=126);
 const metal=s.bricks.find(b=>b.finaleGate&&b.finaleMetal);game.breakBrick(s.bricks.indexOf(metal));assert.equal(metal.alive,true);
 assert.equal(s.stats.walls,7);

});


test('grey defensive row breaks without metal or bypassing the word counter',()=>{
 const {game,s,calls}=locked();
 const index=s.bricks.findIndex(br=>br.finaleDefense),br=s.bricks[index];
 game.breakBrick(index);
 assert.equal(br.alive,false);assert.equal(calls.length,0);
 assert.equal(s.greyBricks,0);assert.equal(s.finale.phase,'locked');
 game.breakBrick(0);assert.equal(s.bricks[0].alive,true);assert.equal(calls.length,1);
});


test('debug finale beats reset cleanly and expose words, rings and spiral',()=>{
 const game=createGame({rng:()=>.5});
 for (const beat of ['spiral','words','rings','opening','words']) {
  game.jumpToFinaleBeat(beat);const s=game.snapshot();
  assert.equal(s.stats.walls,7);assert.equal(s.balls.length,1);assert.equal(s.balls[0].stuck,true);
  assert.equal(s.finale.stageComplete,false);
  if(beat==='opening') assert.equal(s.finale.phase,'forming');
  else if(beat==='words') {
   assert.equal(s.state,'grey');assert.equal(s.greyBricks,0);
   advance(game,.05);assert.ok(s.bricks.some(b=>b.finaleWord));
  } else {
   assert.equal(s.state,'colour');assert.equal(s.finale.phase,'released');
   assert.equal(s.finale.stage,beat==='spiral'?2:1);
   assert.equal(s.bricks.some(b=>b.finaleWord),false);
  }
 }
});


test('mixed finale preserves metal and grey strength and advances at 80 percent',()=>{
 const game=createGame({rng:()=>.5});game.jumpToFinaleBeat('rings');const s=game.snapshot();
 const metal=s.bricks.find(b=>b.finaleMetal),tough=s.bricks.find(b=>b.finaleGrey);
 game.breakBrick(s.bricks.indexOf(metal));assert.equal(metal.alive,true);
 game.breakBrick(s.bricks.indexOf(tough));assert.equal(tough.hp,1);assert.equal(tough.alive,true);
 assert.ok(s.bricks.some(b=>b.gif>=0));assert.ok(s.bricks.some(b=>b.split));assert.ok(s.bricks.some(b=>b.word));
 const inner=s.bricks.findIndex(b=>b.finaleRing&&b.ring===2&&!b.finaleMetal&&!b.strength);
 game.breakBrick(inner);assert.equal(s.finale.stage,1);
 for(const br of [...s.bricks]){
  if(s.finale.stage!==1)break;
  if(br.finaleMetal)continue;
  while(br.alive&&s.finale.stage===1)game.breakBrick(s.bricks.indexOf(br));
 }
 assert.equal(s.finale.stage,2);assert.ok(s.bricks.some(b=>b.finaleGate&&b.finaleMetal));assert.equal(s.bricks.some(b=>b.finaleFeed&&b.finaleMetal),false);
});

test('finale rings rotate in grey and honour reduced motion',()=>{
 const game=createGame({rng:()=>.5});game.jumpToFinaleBeat('words');const s=game.snapshot();
 const br=s.bricks[0],x=br.x;advance(game,1);assert.notEqual(br.x,x);
 game.setReduced(true);advance(game,.05);const still=br.x;advance(game,1);assert.equal(br.x,still);
});


test('ENOUGH words count triple at targets 15 and above, single below',()=>{
 for(const target of [10,14,15,20,25]) {
  const game=createGame({rng:()=>.5,breakoutN:target});game.jumpToFinaleBeat('words');
  advance(game,.05);const s=game.snapshot();
  game.breakBrick(s.bricks.findIndex(b=>b.finaleWord));
  assert.equal(s.greyBricks,target>=15?3:1);
 }
});


test('20 percent shortcut replays restructuring and actual center collision alone triggers frozen outro',()=>{
 const game=createGame({rng:()=>.5});game.jumpToFinaleBeat('remaining');const s=game.snapshot();
 assert.equal(s.finale.stage,1);
 assert.equal(s.bricks.filter(b=>b.alive).length,Math.floor(s.finale.initialBricks*.2));
 advance(game,.05);assert.equal(s.finale.stage,2);assert.ok(s.bricks.some(b=>b.finaleGate&&b.flyFrom));
 advance(game,2);assert.ok(s.bricks.some(b=>b.finaleGate&&b.flyFrom));
 advance(game,3);
 const ball=s.balls[0];ball.stuck=false;ball.falling=false;ball.x=s.finale.centreX;ball.y=s.finale.centreY+15;ball.vx=0;ball.vy=-300;
 game.step(.02);assert.equal(s.finale.phase,'outro');assert.equal(s.finale.stageComplete,true);
 const positions=s.bricks.map(b=>[b.x,b.y]),feedIndex=s.finale.feedIndex,x=ball.x,y=ball.y;
 advance(game,4);assert.ok(s.finale.outroAge>=3.8);assert.equal(s.finale.feedIndex,feedIndex);
 assert.deepEqual(s.bricks.map(b=>[b.x,b.y]),positions);assert.equal(ball.x,x);assert.equal(ball.y,y);
 game.jumpToFinaleBeat('spiral');assert.equal(s.finale.phase,'released');assert.equal(s.finale.outroAge,undefined);
});


test('stage one waits past 25 percent, advances at 20 percent, or accepts a protected center hit',()=>{
 const game=createGame({rng:()=>.5});game.jumpToFinaleBeat('rings');let s=game.snapshot();
 const guards=s.bricks.filter(b=>b.finaleCenterGuard);
 assert.equal(guards.length,12);assert.deepEqual([...new Set(guards.map(b=>b.hp))].sort(),[2,3]);
 const shield=guards[0];game.breakBrick(s.bricks.indexOf(shield));assert.equal(shield.hp,2);assert.equal(shield.alive,true);
 const keep=Math.ceil(s.finale.initialBricks*.25);s.bricks.forEach((b,i)=>b.alive=i<keep);
 advance(game,.05);assert.equal(s.finale.stage,1);
 s.bricks.forEach((b,i)=>b.alive=i<Math.floor(s.finale.initialBricks*.2));advance(game,.05);assert.equal(s.finale.stage,2);
 game.jumpToFinaleBeat('rings');s=game.snapshot();s.wallAge=2;
 const ball=s.balls[0];ball.stuck=false;ball.x=s.finale.centreX;ball.y=s.finale.centreY+10;ball.vx=0;ball.vy=-100;
 game.step(.02);assert.equal(s.finale.stage,2);assert.equal(s.finale.phase,'released');
});

test('galaxy hinges use three hits and release spiral payload without a pendulum structure',()=>{
 const game=createGame({rng:()=>.5});game.jumpToFinaleBeat('spiral');const s=game.snapshot();
 const hinge=s.bricks.find(b=>b.finaleHinge),i=s.bricks.indexOf(hinge);
 assert.ok(hinge.spiral);assert.equal(hinge.hp,3);
 game.breakBrick(i);assert.equal(hinge.hp,2);assert.equal(hinge.alive,true);
 game.breakBrick(i);assert.equal(hinge.hp,1);assert.equal(hinge.alive,true);
 game.breakBrick(i);assert.equal(hinge.alive,false);assert.ok(s.pops.some(p=>p.spiral));
});
