import {test} from 'node:test';
import assert from 'node:assert/strict';
import {finaleHelpStrength,finaleHelpSeat,finaleHelpAngle} from './finale-help.js';
import {createGame} from './game.js';
const context=()=>({state:'grey',finale:{phase:'locked',age:120,wordIndex:24},
 paddle:{x:640,y:680},w:1280,h:720,bricks:[]});
const word=(x,y,ttl=5)=>({x:x-65,y:y-14,w:130,h:28,alive:true,finaleWord:'STOP',ttl});
test('help waits through 30 seconds buildup and 60 seconds of words, then ramps and resets',()=>{
 const g=context();
 for(const age of [0,29,30,60,89.99,90]){g.finale.age=age;assert.equal(finaleHelpStrength(g.finale,g.state),0);assert.equal(finaleHelpSeat(g),null);}
 g.finale.age=105;assert.equal(finaleHelpStrength(g.finale,g.state),.5);
 g.finale.age=150;assert.equal(finaleHelpStrength(g.finale,g.state),1);
 for(const phase of ['released','forming','interrupt']){g.finale.phase=phase;assert.equal(finaleHelpStrength(g.finale,g.state),0);}
 g.finale.phase='locked';g.finale.age=0;assert.equal(finaleHelpStrength(g.finale,g.state),0);
 g.finale.age=120;assert.equal(finaleHelpStrength(g.finale,'colour'),0);
});
test('occasional help seats follow paddle, stay bounded, avoid bricks and remain above paddle',()=>{
 const g=context();
 for(const x of [0,640,1280]) {
  g.paddle.x=x;const seat=finaleHelpSeat(g);assert.ok(seat);
  assert.ok(seat.x>=20 && seat.x+seat.w<=g.w-20);
  assert.ok(seat.y>g.h*.52 && seat.y+seat.h<g.paddle.y-100);
 }
 g.paddle.x=640;const first=finaleHelpSeat(g);g.bricks=[{...first,alive:true}];
 const next=finaleHelpSeat(g);assert.ok(next);assert.notEqual(next.x,first.x);
 g.bricks=[{alive:true,x:0,y:0,w:1280,h:720}];assert.equal(finaleHelpSeat(g),null);
 g.bricks=[];g.finale.wordIndex++;assert.equal(finaleHelpSeat(g),null);
});
test('rebound help targets only reachable live words and preserves speed with at most ten degrees',()=>{
 const g=context(),ball={x:640,y:660,r:8},speed=300,angle=0;
 g.bricks=[word(750,475)];
 const result=finaleHelpAngle({...g,ball,speed,angle});assert.ok(result>0 && result<=10*Math.PI/180+1e-12);
 assert.ok(Math.abs(Math.hypot(Math.sin(result)*speed,-Math.cos(result)*speed)-speed)<1e-10);
 g.finale.age=91;assert.ok(finaleHelpAngle({...g,ball,speed,angle})<result);
 g.finale.age=120;
 for(const target of [word(750,475,.1),{...word(750,475),alive:false},{...word(750,475),finaleWord:null},word(750,690)]) {
  g.bricks=[target];assert.equal(finaleHelpAngle({...g,ball,speed,angle}),angle);
 }
 g.bricks=[word(750,475),{alive:true,x:600,y:530,w:200,h:25}];
 assert.equal(finaleHelpAngle({...g,ball,speed,angle}),angle);
});
test('locked finale schedules low targets only after 90 seconds and new wall clears assistance',()=>{
 const game=createGame({rng:()=>.5}),s=game.snapshot();game.jumpToWall(8);
 s.state='grey';s.finale.phase='locked';s.finale.age=89;s.finale.wordClock=0;s.finale.wordIndex=24;
 game.step(.02);assert.ok(s.bricks.filter(b=>b.finaleWord).every(b=>b.y<s.h*.45));
 s.finale.age=91;s.finale.wordClock=0;s.finale.wordIndex=27;
 game.step(.02);assert.ok(s.bricks.some(b=>b.finaleWord&&b.y>s.h*.6));
 game.jumpToWall(8);assert.equal(finaleHelpStrength(s.finale,s.state),0);
});
test('live paddle bounce receives help without speed change; midflight remains unsteered',()=>{
 function bounce(age) {
  const game=createGame({rng:()=>.5}),s=game.snapshot();game.jumpToWall(8);
  s.state='grey';s.finale.phase='locked';s.finale.age=age;s.finale.wordClock=100;s.wallAge=2;
  s.bricks.push(word(750,475));const b=s.balls[0];
  Object.assign(b,{x:640,y:s.paddle.y-s.paddle.h/2-b.r-1,vx:0,vy:300,stuck:false,ghost:true});
  game.step(.02,{x:640});return {game,s,b};
 }
 const before=bounce(89),after=bounce(120);
 assert.equal(before.b.vx,0);assert.ok(after.b.vx>0 && after.b.vy<0);
 assert.ok(Math.abs(Math.hypot(before.b.vx,before.b.vy)-Math.hypot(after.b.vx,after.b.vy))<1e-8);
 const vx=after.b.vx,vy=after.b.vy;after.game.step(.02,{x:200});
 assert.equal(after.b.vx,vx);assert.equal(after.b.vy,vy);
});