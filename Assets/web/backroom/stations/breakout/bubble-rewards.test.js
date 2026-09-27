import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createRewardPicker,rewardBounds} from './bubble-rewards.js';
import {createGame} from './game.js';
test('reward bag exhausts distinct pictures before repeating, including across cycles',()=>{
 const pick=createRewardPicker(()=>.5),keys=['a','b','a','c','d'];
 const first=Array.from({length:4},()=>keys[pick(keys)]);
 assert.equal(new Set(first).size,4);
 const next=keys[pick(keys)];assert.notEqual(next,first.at(-1));
 assert.equal(pick(['only']),0);assert.equal(pick([]),-1);
 assert.equal(pick(['fresh']),0);
});
test('bubble images preserve aspect ratio, remain bounded and limit source upscaling',()=>{
 for(const [fw,fh] of [[96,64],[640,360],[200,800]])for(const hero of [false,true]){
  const b=rewardBounds(1920,1080,fw,fh,false,hero);
  assert.ok(b.w<=1920*.6&&b.h<=1080*.65);
  assert.ok(b.w<=fw*2&&b.h<=fh*2);
  assert.ok(Math.abs(b.w/b.h-fw/fh)<1e-9);
 }
});
test('replenishing finale cycles all GIF seats and includes jackpots behind three-hit core shields',()=>{
 const game=createGame({rng:()=>.5});game.jumpToFinaleBeat('spiral');const s=game.snapshot();
 assert.ok(s.bricks.filter(b=>b.finaleGate).every(b=>b.hp===3));
 const gifs=new Set();let jackpot=false;
 for(let i=0;i<1800;i++){
  game.step(.05);
  for(const b of s.bricks)if(b.finaleFeed){if(b.gif>=0)gifs.add(b.gif);jackpot ||= b.jackpot;}
 }
 assert.equal(gifs.size,8);assert.ok(jackpot);
});
