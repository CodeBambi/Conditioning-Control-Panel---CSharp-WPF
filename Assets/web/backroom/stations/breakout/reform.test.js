import {test} from 'node:test';
import assert from 'node:assert/strict';
import {REFORM_WORDS,reformMinimum,reformLayout,rotatedBrickContact} from './reform.js';
import {createGame} from './game.js';

test('compact finale uses twelve strokes for YES and four for I',()=>{
 assert.equal(reformMinimum('YES'),12);assert.equal(reformMinimum('I'),4);
 const cells=reformLayout('I',4);assert.equal(cells.filter(c=>c.angle===0).length,2);
 assert.equal(cells.filter(c=>c.angle===Math.PI/2).length,2);
 assert.equal(reformLayout('I',3).length,0);
});
test('every feasible formation uses exactly the survivors without leaving the playfield',()=>{
 for(const word of REFORM_WORDS)for(const n of [reformMinimum(word),100]){
  const cells=reformLayout(word,n);assert.equal(cells.length,n);
  for(const c of cells){assert.ok(c.w>0&&c.h>0);assert.ok(c.x>0&&c.x+c.w<1280);assert.ok(c.y+c.h<400);}
 }
});
test('rotated collision uses the slanted face, not its surrounding box',()=>{
 const brick={x:100,y:100,w:100,h:10,angle:Math.PI/4};
 assert.equal(rotatedBrickContact({x:110,y:135,r:5},brick),null);
 const hit=rotatedBrickContact({x:150,y:110,r:5},brick);assert.ok(hit);
 assert.ok(Math.abs(Math.hypot(hit.nx,hit.ny)-1)<1e-8);
});
function rig(){let clock=0;const events=[];const game=createGame({audio:{beat:{spb:.625,phase:()=>clock/.625%1}},onEvent:(n,d)=>events.push([n,d])});
 game.jumpToWall(5);const s=game.snapshot();s.balls=[];
 return {game,s,events,advance(seconds){for(let i=0;i<seconds*100;i++){clock+=.01;game.step(.01);}}};}
test('wall five reforms on eight music beats, preserving surviving payload objects',()=>{
 const {s,events,advance}=rig();const original=[...s.bricks];original[0].alive=false;
 const payload=original[1];payload.gif=7;payload.tier=3;
 advance(5.8);assert.equal(s.reform.word,'BREATHE');assert.equal(s.bricks.filter(b=>b.alive).length,99);
 assert.equal(s.bricks[1],payload);assert.equal(payload.gif,7);assert.equal(payload.tier,3);
 assert.equal(original[0].alive,false);assert.ok(events.some(e=>e[0]==='reform'));
 assert.equal(s.reform.moving,0);
});
test('few survivors skip to I then stop reforming without resurrecting bricks',()=>{
 const {s,advance}=rig();for(let i=4;i<s.bricks.length;i++)s.bricks[i].alive=false;
 advance(5.8);assert.equal(s.reform.word,'I');assert.equal(s.bricks.filter(b=>b.alive).length,4);
 advance(5);assert.equal(s.reform.stopped,true);assert.equal(s.bricks.filter(b=>b.alive).length,4);
});

test('many survivors keep long words across repeated timer cycles',()=>{
 const {s,advance}=rig();advance(65);
 assert.ok(['RELEASE','BREATHE'].includes(s.reform.word));
 assert.equal(s.reform.stopped,false);assert.equal(s.bricks.filter(b=>b.alive).length,100);
 for(let i=20;i<s.bricks.length;i++)s.bricks[i].alive=false;
 advance(6);assert.ok(['SOFTEN','RELAX','LET GO','SINK'].includes(s.reform.word));
});

test('reformation grows bricks within bounds while preserving face proportions',()=>{
 for(const word of REFORM_WORDS)for(const count of [reformMinimum(word),50,100]){
  for(const b of reformLayout(word,count)){assert.ok(b.w>=35.2&&b.w<=52.800001);assert.ok(b.h>=21.6&&b.h<=32.400001);assert.ok(Math.abs(b.w/b.h-44/27)<1e-10);}
 }
});

test('rhythm wall includes word effects and keeps them through rearrangement',()=>{
 const {s,advance}=rig();const words=s.bricks.filter(b=>b.word);
 assert.ok(words.length>0);assert.ok(words.every(b=>b.gif<0&&!b.spiral));
 advance(6);assert.ok(words.every(b=>s.bricks.includes(b)&&b.word));
});
