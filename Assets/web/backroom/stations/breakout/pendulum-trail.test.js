import {test} from 'node:test';
import assert from 'node:assert/strict';
import {pendulumTrailSegments} from './pendulum-trail.js';
test('trail never joins across a portal jump',()=>{
  const segments=pendulumTrailSegments({x:920,y:100,trail:[{x:100,y:100},{x:900,y:100},{x:910,y:100}],mode:'flight'});
  assert.equal(segments.length,2);assert.equal(segments[0].a.x,900);
});
test('orbit trail has a fixed length, tapered tail and attached head',()=>{
  const segments=pendulumTrailSegments({x:200,y:0,trail:Array.from({length:20},(_,i)=>({x:i*10,y:0})),mode:'orbit'});
  assert.equal(segments[0].a.x,85);assert.equal(segments.at(-1).b.x,200);
  assert.ok(segments[0].strength<segments.at(-1).strength);
});
