import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createRenderBudget} from './render-budget.js';
function frames(q,from,to,cpu=22,dt=1000/30){let changed=0;for(let t=from;t<to;t+=dt)changed+=q.sample(t,cpu,dt)?1:0;return changed;}
test('sustained expensive software frames lower bounded pixel budget',()=>{
 const q=createRenderBudget(true);frames(q,0,1500);assert.equal(q.pixels,900000);
 assert.ok(frames(q,1500,12000)>0);assert.equal(q.pixels,720000);
});
test('brief spikes and paused intervals do not lower quality; recovery is slow',()=>{
 const q=createRenderBudget(true);frames(q,0,6000,7,1000/60);q.sample(6001,80,80);frames(q,6018,7000,7,1000/60);assert.equal(q.pixels,900000);
 frames(q,7000,18000);assert.equal(q.pixels,720000);
 q.idle(20000);frames(q,20000,20900,7,1000/60);assert.equal(q.pixels,720000);
 frames(q,21000,27000,7,1000/60);assert.equal(q.pixels,720000);
 frames(q,27000,34000,7,1000/60);assert.ok(q.pixels>720000&&q.pixels<900000);
});
test('accelerated renderer retains its existing pixel ceiling',()=>{
 const q=createRenderBudget(false);frames(q,0,20000,50);assert.equal(q.pixels,1500000);
});
