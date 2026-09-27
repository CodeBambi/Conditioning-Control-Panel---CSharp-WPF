import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createGame} from './game.js';
import {advanceEndlessDemolition, DEMOLITION_CAPTURES} from './endless-physics.js';

function rig(options = {}) {
  const events = [], game = createGame({endless:true,seed:1701,savedSaturation:.6,rng:()=>.6,
    onEvent:(name,data)=>events.push([name,data]),...options});
  const g = game.snapshot(); g.balls = [];
  return {game,g,events,advance(seconds) { for(let t=0;t<seconds;t+=.01)game.step(.01); }};
}
function breakAnchor(game,g,p) {
  const b=g.bricks.find(b=>b.pendulumAnchor&&b.pendulumId===p.id),i=g.bricks.indexOf(b);
  while(b.alive)game.breakBrick(i);
}

test('Endless starts with two mechanisms and restores a board beyond Story',()=>{
  const {g}=rig();assert.equal(g.endless,true);assert.equal(g.endlessBoard.index,0);
  assert.ok(g.endlessBoard.mechanics.length>=2);assert.ok(g.dome&&g.pendulums.length>=2);
  const {g:later}=rig({from:41,bestCombo:57,savedSaturation:.82});
  assert.equal(later.stats.walls,41);assert.equal(later.endlessBoard.index,41);
  assert.equal(later.comboBest,57);assert.equal(later.state,'colour');assert.equal(later.sat,.82);
  assert.equal(later.finale,null);
});

test('Story keeps its eight stages and Endless excludes the grey wall-seven gate',()=>{
  const story=createGame({rng:()=>.6});story.jumpToWall(8);
  assert.equal(story.snapshot().endless,false);assert.ok(story.snapshot().finale);
  const {game,g}=rig({from:6});g.state='grey';g.breakoutN=1000;
  g.bricks.forEach(b=>b.alive=false);
  const last=g.bricks.find(b=>!b.pendulumAnchor);last.alive=true;last.greyMetal=false;last.hp=1;
  game.breakBrick(g.bricks.indexOf(last));
  assert.equal(g.stats.walls,7);assert.equal(g.finale,null);
});

test('last-brick clear stays pending then advances to a clean Endless board',()=>{
  const {game,g,advance}=rig({from:12});
  g.bricks.forEach(b=>b.alive=false);
  const last=g.bricks.find(b=>!b.pendulumAnchor);last.alive=true;last.hp=1;
  game.breakBrick(g.bricks.indexOf(last),true);
  assert.equal(g.stats.walls,12);assert.ok(g.clearing?.wall);
  advance(.8);assert.equal(g.stats.walls,13);assert.equal(g.endlessBoard.index,13);
  assert.equal(g.finale,null);assert.equal(g.balls.length,1);assert.equal(g.balls[0].orbit,null);
  assert.ok(g.balls[0].stuck);assert.ok(g.sat>=.6);
});

test('released demolition ball is caught, visibly orbits, launches at a hinge and chains',()=>{
  const {game,g,events,advance}=rig(),p=g.pendulums[0];
  breakAnchor(game,g,p);advance(.15);
  assert.equal(p.mode,'orbit');assert.ok(events.some(e=>e[0]==='demolitionCapture'));
  const first={x:p.x,y:p.y};advance(.3);
  assert.ok(Math.hypot(p.x-first.x,p.y-first.y)>20);assert.ok(p.trail.length<=18);
  advance(1);
  const launch=events.find(e=>e[0]==='demolitionLaunch');assert.ok(launch);
  const second=g.bricks.find(b=>b.pendulumAnchor&&b.pendulumId!==p.id);
  assert.equal(launch[1].tx,second.x+second.w/2);assert.equal(launch[1].ty,second.y+second.h/2);
  advance(2);
  assert.ok(events.filter(e=>e[0]==='pendulumRelease').length>=2,'flight destroys a second hinge');
});

test('demolition capture leaves an ordinary ball in the same spiral',()=>{
  const {game,g,advance}=rig(),p=g.pendulums[0];
  const b={x:g.well.x+80,y:g.well.y,vx:0,vy:-300,r:8,spin:0,ghost:false,stuck:false,
    orbit:null,lost:false,falling:false,trail:[],squash:0};
  g.balls=[b];advance(.02);assert.equal(g.well.captured,b);
  breakAnchor(game,g,p);advance(.15);
  assert.equal(p.mode,'orbit');assert.equal(g.well.captured,b);assert.ok(b.orbit);
});

test('demolition lifetime is finite and remains clear of the paddle',()=>{
  const {g}=rig(),p=g.pendulums[0],events=[];
  p.mode='sweep';p.x=g.well.x+100;p.y=g.well.y;
  const emit=(n,d)=>events.push([n,d]);
  for(let i=0;i<2000;i++) {
    advanceEndlessDemolition(p,g,.01,emit);
    assert.ok(Number.isFinite(p.x)&&Number.isFinite(p.y));
    assert.ok(p.y+p.r<g.paddle.y-70);assert.ok(p.trail.length<=18);
  }
  assert.equal(p.mode,'spent');assert.ok(p.demolitionCaptures<=DEMOLITION_CAPTURES);
});

test('a relapse removes a demolition orbit without leaving it held forever',()=>{
  const {game,g,advance}=rig(),p=g.pendulums[0];
  breakAnchor(game,g,p);advance(.15);assert.equal(p.mode,'orbit');
  g.state='grey';g.well=null;advance(.05);
  assert.equal(p.mode,'flight');assert.equal(p.orbitWell,null);
});

test('moving tide and reform pieces cannot drag pendulum hinges',()=>{
  let selected;
  for(let from=0;from<24;from++) {
    const r=rig({from});if(r.g.tide&&r.g.pendulums){selected=r;break;}
  }
  assert.ok(selected,'generator provides a tide/pendulum mixture');
  const {g,advance}=selected,anchor=g.bricks.find(b=>b.pendulumAnchor),before={x:anchor.x,y:anchor.y};
  advance(2);assert.deepEqual({x:anchor.x,y:anchor.y},before);
});

test('checkpoint rebuild preserves payloads and armour independently of prior play randomness',()=>{
  const first=rig({from:14,rng:()=>.01}), second=rig({from:14,rng:()=>.99});
  const payloads=g=>g.bricks.map(({x,y,gif,tier,word,spiral,split,jackpot,hp,strength,powerup,greyMetal})=>
    ({x,y,gif,tier,word,spiral,split,jackpot,hp,strength,powerup,greyMetal}));
  assert.deepEqual(payloads(first.g),payloads(second.g));
  const traveled=rig({rng:()=>.4});traveled.game.jumpToWall(15);
  assert.deepEqual(payloads(first.g),payloads(traveled.g));
});
