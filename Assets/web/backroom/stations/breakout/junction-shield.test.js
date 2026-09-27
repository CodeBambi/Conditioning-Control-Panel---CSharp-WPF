import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createGame} from './game.js';
import {junctionProtected} from './junction-shield.js';

test('junction protection covers transitions but ordinary play stays vulnerable',()=>{
  const s={wallAge:2};assert.equal(junctionProtected(s),false);
  for(const state of [{wallAge:0},{transition:{kind:'breakout'}},{reform:{moving:true}},
    {spell:{celebrate:1}},{finale:{phase:'interrupt'}},{finale:{phase:'released',stage:2}}])
    assert.equal(junctionProtected({...s,...state}),true);
  assert.equal(junctionProtected({...s,finale:{phase:'released',stage:1}}),false);
});
for(const scene of ['arrival','spiral'])test(scene+' shield bounces repeated misses without losing a ball or starting relapse',()=>{
  const events=[];const game=createGame({onEvent:n=>events.push(n)});
  if(scene==='spiral')game.jumpToFinaleBeat('spiral');else game.jumpToWall(2);
  const s=game.snapshot();s.state='colour';s.freeze=0;s.transition=null;s.pendingBreakout=false;
  s.breakoutShield=null;s.noLose=false;
  if(scene==='spiral')s.finale.stageAge=10;
  for(let i=0;i<5;i++){
    s.wallAge=scene==='arrival'?.2:2;
    const b=s.balls[0];Object.assign(b,{stuck:false,lost:false,falling:false,orbit:null,x:20,y:s.paddle.y+s.paddle.h/2+17,vx:30,vy:400});
    game.step(1/60);
    assert.equal(s.balls.includes(b),true);assert.equal(b.lost,false);assert.equal(b.falling,false);
    assert.ok(b.vy<0);assert.equal(s.transition,null);assert.equal(s.state,'colour');
  }
  assert.equal(events.includes('lost'),false);assert.equal(events.includes('relapseStart'),false);
});
