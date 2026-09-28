import {test} from 'node:test';
import assert from 'node:assert/strict';
import {rallyBoost,RALLY_RAMP_S} from './rally-speed.js';
import {createGame} from './game.js';
function rig(options={}) {
 const game=createGame({rng:()=>.7,greyMetal:false,brickStrength:[],...options}),g=game.snapshot();
 g.well=null;g.dome=false;g.portals=[];g.pendulums=null;g.tide=null;
 for(const br of g.bricks){br.y=-1000;br.word=null;}
 game.setNoLose(true);game.launchNow();
 return {game,g,advance(seconds){for(let i=0;i<Math.round(seconds*120);i++)game.step(1/120,{x:g.balls[0]?.x});}};
}
test('rally multiplier adds 0.4 percent per second and caps at 30 percent',()=>{
 assert.equal(rallyBoost(-4),1);assert.equal(rallyBoost(NaN),1);
 assert.equal(rallyBoost(25),1.1);assert.equal(rallyBoost(75),1.3);assert.equal(rallyBoost(500),1.3);
});
test('Story and Endless gradually accelerate live balls and retain the selected pace',()=>{
 for(const endless of [false,true])for(const reduced of [false,true]) {
  const {game,g,advance}=rig({endless,from:3,reduced,speedScale:.4});
  const base=.4*g.h/(.625*(1+.5*g.sat));
  advance(25);assert.ok(Math.abs(g.rally-25)<.02);assert.ok(Math.abs(g.speed/base-1.1)<.001);
  advance(60);assert.equal(g.rally,RALLY_RAMP_S);assert.ok(Math.abs(g.speed/base-1.3)<.001);
  game.setSpeedScale(.8);advance(.1);assert.ok(Math.abs(g.speed/base-2.6)<.001);
 }
});
test('waiting and hit freezes do not age a rally; slow motion advances its physical clock',()=>{
 const {game,g,advance}=rig({endless:true,from:3,savedSaturation:.4});
 g.balls[0].stuck=true;advance(.5);assert.equal(g.rally,0);
 game.launchNow();g.freeze=1;advance(.5);assert.equal(g.rally,0);g.freeze=0;
 game.fireWordNow('RELAX');advance(1);assert.ok(g.rally>.8&&g.rally<1);
 assert.ok(g.timeScale<1);const before=g.rally;game.setReduced(true);advance(.5);
 assert.ok(Math.abs(g.rally-before-.5)<.02);assert.equal(g.timeScale,1);
});
test('paddle hits and power catches keep rally; last-ball loss resets it in both states',()=>{
 for(const colour of [false,true]) {
  const {game,g,advance}=rig({endless:true,from:3,...(colour?{savedSaturation:.4}:{})});
  g.rally=30;const b=g.balls[0];Object.assign(b,{x:g.paddle.x,y:g.paddle.y-g.paddle.h/2-b.r-1,vx:0,vy:400});
  advance(.04);assert.ok(b.vy<0);assert.ok(g.rally>=30);
  if(colour){g.power.drops=[{kind:'fireball',x:g.paddle.x,x0:g.paddle.x,y:g.paddle.y-15,vy:190,age:1,ph:0}];advance(.1);assert.ok(g.power.fireball>0);}
  const copy={...b,temporary:true};g.balls.push(copy);g.power.multiball=5;
  game.loseBall();assert.ok(g.rally>=30);game.loseBall();assert.equal(g.rally,0);
 }
});
test('Endless board transitions preserve the live rally instead of restarting its ramp',()=>{
 const {game,g}=rig({endless:true,from:3,savedSaturation:.4});g.rally=23;
 for(let i=0;i<g.bricks.length;i++){Object.assign(g.bricks[i],{hp:1,strength:0,gif:-1,spiral:null,split:false,word:null,jackpot:false});game.breakBrick(i);}
 assert.equal(g.stats.walls,4);assert.equal(g.rally,23);
});
