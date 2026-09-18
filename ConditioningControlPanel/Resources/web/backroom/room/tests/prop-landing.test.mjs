import {test} from 'node:test';
import assert from 'node:assert/strict';
import {LAND, PLACE_TIER, placePlan, placeFeel, sampleLanding} from '../prop-landing.js';
import {freshSit, afterParty, PARTY} from '../../shared/win/plan.js';

const feelFor=(ctx,sit=freshSit())=>placeFeel(placePlan(sit,ctx),ctx);

test('a placement is a good win, and it is plan.js that says so',()=>{
 const plan=placePlan(freshSit(),{});
 assert.equal(plan.tier,PLACE_TIER);
 assert.equal(plan.glow,PARTY.GLOW_MS);
 assert.equal(plan.reveal,false);      // a plant is not a jackpot
 assert.equal(plan.sparkle,0);
 const feel=placeFeel(plan,{});
 assert.equal(feel.travelMs,LAND.MS);  // and the whole move is under the 620 ms ceiling
 assert.equal(feel.glowMs,PARTY.GLOW_MS);
 assert.equal(feel.drop,LAND.DROP);
});

test('Law VI: reduced motion is the state, Calm is the glow without the travel',()=>{
 const reduced=feelFor({reduced:true});
 assert.deepEqual([reduced.travelMs,reduced.glowMs,reduced.drop,reduced.ms],[0,0,0,0]);
 assert.equal(sampleLanding(0,reduced).done,true);
 assert.equal(sampleLanding(0,reduced).lift,0);
 assert.equal(sampleLanding(0,reduced).scaleY,1);

 const calm=feelFor({still:true});
 assert.equal(calm.travelMs,0);
 assert.equal(calm.drop,0);
 assert.equal(calm.glowMs,PARTY.GLOW_MS);   // a warm cut is not travel
 assert.ok(sampleLanding(80,calm).glow>0);
 assert.equal(sampleLanding(80,calm).lift,0);
});

test('it lands: down onto its spot, a squash, and back to exactly where it was authored',()=>{
 const feel=feelFor({});
 assert.equal(sampleLanding(0,feel).lift,feel.drop);
 let last=Infinity;
 for(let t=0;t<=feel.travelMs*LAND.TOUCH;t+=5) {
  const at=sampleLanding(t,feel);
  assert.ok(at.lift<=last+1e-9,'a prop never rises on its way down');
  assert.ok(at.lift>=0,'and it never sinks through the carpet');
  last=at.lift;
 }
 assert.equal(sampleLanding(feel.travelMs*LAND.TOUCH,feel).lift,0);
 // The squash lives entirely after the touch, and it is a squash: y down, xz out.
 const mid=sampleLanding(feel.travelMs*(LAND.TOUCH+1)/2,feel);
 assert.ok(mid.scaleY<1&&mid.scaleXZ>1);
 assert.ok(mid.scaleY>=1-LAND.SQUASH);
 const end=sampleLanding(feel.ms,feel);
 assert.deepEqual([end.lift,end.scaleY,end.scaleXZ,end.glow,end.done],[0,1,1,0,true]);
});

test('THE GLOW is in fast and out slow, and it is over when the plan says',()=>{
 const feel=feelFor({});
 const peak=feel.glowMs*LAND.GLOW_IN;
 assert.equal(sampleLanding(0,feel).glow,0);
 assert.ok(sampleLanding(peak,feel).glow>.999);
 // In fast, out slow: it is lit in under a quarter of its life and spends the rest going out.
 assert.ok(peak<feel.glowMs-peak);
 const up=sampleLanding(peak,feel).glow-sampleLanding(peak-50,feel).glow;
 const down=sampleLanding(peak,feel).glow-sampleLanding(peak+50,feel).glow;
 assert.ok(up>down*2);
 assert.ok(sampleLanding(feel.glowMs-1,feel).glow>0);
 assert.equal(sampleLanding(feel.glowMs,feel).glow,0);
 for(let t=-50;t<feel.ms+200;t+=7){const g=sampleLanding(t,feel).glow;assert.ok(g>=0&&g<=1);}
});

test('Brake 3: flicking the same plant on and off wears the landing down, never away',()=>{
 let sit=freshSit();
 const drops=[];
 for(let i=0;i<6;i++){const plan=placePlan(sit,{});sit=afterParty(sit,plan);drops.push(placeFeel(plan,{}).drop);}
 assert.equal(drops[0],LAND.DROP);
 assert.ok(drops[5]<drops[0]);
 assert.ok(drops[5]>0);
});

test('junk never moves a prop',()=>{
 assert.equal(placeFeel(null,null).ms,0);
 assert.equal(sampleLanding(NaN,null).done,true);
 assert.equal(sampleLanding(-10,feelFor({})).lift,feelFor({}).drop);
});
