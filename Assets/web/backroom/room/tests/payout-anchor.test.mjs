import {test} from 'node:test';
import assert from 'node:assert/strict';
import {SHOWER, frontAxis, payoutAnchor, hostAt, payoutHost, usableBox} from '../payout-anchor.js';

/* The Candy Rose cabinet, in its OWN space: stations.json's world bounds and approach point folded
 * back through the row's yaw (+90 deg) and scale (1.21). This is the fixture the shower's numbers were
 * written against, so it is the one the generalisation has to keep. */
const SLOT_BOX = {min:{x:-0.5546,y:0,z:-0.5850},max:{x:0.7157,y:1.7977,z:0.6265}};
const SLOT_APPROACH = {x:0,y:1.342,z:1.612};
const near=(a,b,tol)=>assert.ok(Math.abs(a-b)<=tol,a+' is not within '+tol+' of '+b);

test('the front is the side the player walks up to, and nothing else is guessed',()=>{
 assert.deepEqual({...frontAxis(SLOT_APPROACH)},{axis:'z',sign:1});
 assert.deepEqual({...frontAxis({x:-3.2,y:1.6,z:.4})},{axis:'x',sign:-1});
 assert.deepEqual({...frontAxis({x:.4,y:1.6,z:-3.2})},{axis:'z',sign:-1});
 // A dead-centre or junk approach reads as the authored front (+z), never as NaN.
 for(const bad of [null,undefined,{x:0,y:0,z:0},{x:NaN,y:0,z:1}])assert.deepEqual({...frontAxis(bad)},{axis:'z',sign:1});
});

test('the calibrated slot tray survives losing its own gate',()=>{
 const a=payoutAnchor(SLOT_BOX,SLOT_APPROACH);
 near(a.z,SHOWER.REST.z,.02);     // out of the middle toward the face the player stands at
 near(a.y,SHOWER.REST.y,.02);     // and up to the tray line
 near(a.x,SHOWER.REST.x,.12);     // the cabinet's own mass is a touch off its origin; the spread covers it
 // The coins stay on the cabinet: the shower is SPAN wide and the fixture is wider than that.
 assert.ok(a.x-SHOWER.SPAN/2>SLOT_BOX.min.x && a.x+SHOWER.SPAN/2<SLOT_BOX.max.x);
});

test('the host puts the shower own rest point on the anchor, whatever the fixture is scaled to',()=>{
 for(const [scale,height] of [[1.21,1],[1.68,1],[0.8,1],[1,0.78],[1.1,1]]) {
  const host=payoutHost(SLOT_BOX,SLOT_APPROACH,scale,height), a=payoutAnchor(SLOT_BOX,SLOT_APPROACH);
  near(host.position.x+host.scale.x*SHOWER.REST.x,a.x,1e-9);
  near(host.position.y+host.scale.y*SHOWER.REST.y,a.y,1e-9);
  near(host.position.z+host.scale.z*SHOWER.REST.z,a.z,1e-9);
  // A coin is a coin: the same size in the ROOM at every fixture scale, and never squashed by a
  // holder that is short in y alone (the roulette's .78).
  near(host.scale.x*scale,SHOWER.REF_SCALE,1e-9);
  near(host.scale.y*scale*height,SHOWER.REF_SCALE,1e-9);
  near(host.scale.z*scale,SHOWER.REF_SCALE,1e-9);
 }
 // The fixture the numbers came from asks for no correction at all.
 const rose=hostAt(SHOWER.REST,1.21,1);
 assert.deepEqual([rose.scale.x,rose.scale.y,rose.scale.z],[1,1,1]);
 assert.deepEqual([rose.position.x,rose.position.y,rose.position.z],[0,0,0]);
});

test('an unmeasurable fixture is refused, never placed at the origin',()=>{
 for(const bad of [null,undefined,{},{min:{x:0,y:0,z:0}},{min:{x:0,y:0,z:0},max:{x:0,y:0,z:0}},
   {min:{x:0,y:0,z:0},max:{x:NaN,y:1,z:1}},{min:{x:1,y:1,z:1},max:{x:0,y:0,z:0}}]) {
  assert.equal(usableBox(bad),false);
  assert.equal(payoutAnchor(bad,SLOT_APPROACH),null);
  assert.equal(payoutHost(bad,SLOT_APPROACH,1,1),null);
 }
 assert.equal(hostAt(null,1,1),null);
 assert.equal(hostAt({x:0,y:NaN,z:0},1,1),null);
 // A junk scale is a scale of 1, not a division by zero.
 assert.equal(hostAt(SHOWER.REST,0,-2).scale.x,SHOWER.REF_SCALE);
});
