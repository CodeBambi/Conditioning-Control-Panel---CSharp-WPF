import { test } from 'node:test';
import assert from 'node:assert/strict';
import { rewardOf, rewardText, sliceText, rewardOdds } from '../rewards.js';
import { readResult, layoutOf, landingAngle, sliceAt } from '../wheel.js';
import { tierOf, landPose } from '../feel.js';
const t = (_, fallback, vars = {}) => fallback.replace(/\{(\w+)\}/g, (all,k) => vars[k] ?? all);
test('special receipts preserve granted identity through result parsing', () => {
  const input = { pay: 0, reward: { kind: 'decoration', decorationId: 'ivy' } };
  const result = readResult(input);
  assert.equal(rewardOf(result).decorationId, 'ivy');
  assert.equal(rewardText(result, t), 'Hanging ivy delivered. Place it at Room Service.');
  assert.equal(tierOf(result), 2);
});
test('complete collection displays actual credited payout including an active bonus', () => {
  for (const total of [75, 150, 12]) assert.equal(rewardText(readResult({pay:total,total,reward:{kind:'decoration',fallback:true}}), t), `Collection complete. +${total} SP.`);
});
test('nothing is a zero reward, double is a distinct non-cash award', () => {
  assert.equal(rewardText({reward:{kind:'nothing'}}, t), 'Not a thought. Not a sparkle.');
  const result = readResult({pay:0,reward:{kind:'double',until:123456}});
  assert.equal(rewardOf(result).until,123456); assert.equal(landPose(result),'spirals');
  assert.equal(sliceText({id:'seeing_double',kind:'double',label:'Seeing Double'},t).big,'\u00d72');
  assert.equal(rewardText({pay:15,reward:{kind:'sp'}},t),null);
  assert.equal(rewardOf({reward:{kind:'unknown'}}),null);
});
test('noncash sectors remain data-driven and land inside every arc', () => {
  const kinds=['prize','decoration','double','nothing','jackpot','prize','prize'];
  const layout=layoutOf(kinds.map((kind,i)=>({id:'s'+i,kind,label:kind,pay:0,width:i?45:90})));
  for (const row of layout) {assert.equal(row.kind,kinds[row.index]);assert.equal(sliceAt(layout,landingAngle(layout,row.index,'day')).id,row.id);}
  assert.equal(rewardOdds(layout[1],t,String),'Unowned decoration, or 75 SP if complete');
});

test('collection at the balance cap displays persisted credited amount', () => {
  const r=readResult({pay:75,total:75,credited:4,capped:true,reward:{kind:'decoration',fallback:true}});
  assert.equal(rewardText(r,t),'Collection complete. +4 SP.');
  assert.equal(rewardText(readResult({...r,credited:0}),t),'Collection complete. +0 SP.');
});
