import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createRotation} from './__phone-music.js';
import {createQuality} from '../../ConditioningControlPanel/Resources/web/backroom/shared/quality.js';

test('soundtrack plays each track per round and never repeats within three intervening songs',()=>{
  let seed=17;const random=()=>((seed=(seed*1664525+1013904223)>>>0)/2**32);
  const tracks=['a','b','c','d','e'], r=createRotation(tracks,random), played=[];
  for(let i=0;i<1000;i++){const x=r.next();assert.ok(!played.slice(-3).includes(x));played.push(x);}
  for(let i=0;i<played.length;i+=5)assert.equal(new Set(played.slice(i,i+5)).size,5);
});
test('auto lowers after sustained misses and only recovers after stable time outside a game',()=>{
  const q=createQuality({hardwareConcurrency:8});assert.equal(q.performance,false);
  for(let i=0;i<90;i++)q.sample(60,33.33);assert.equal(q.performance,true);
  for(let i=0;i<1200;i++)q.sample(33.33,33.33,false);assert.equal(q.performance,true);
  for(let i=0;i<160;i++)q.sample(33.33,33.33,true);assert.equal(q.performance,false);
});
test('device hint stays conservative; explicit modes persist and loading gaps do not lower quality',()=>{
  const saved=new Map(), storage={getItem:k=>saved.get(k),setItem:(k,v)=>saved.set(k,v)};
  const q=createQuality({},storage);for(let i=0;i<200;i++)q.sample(800,16.67);assert.equal(q.performance,false);
  q.setMode('performance');assert.equal(createQuality({},storage).performance,true);
  q.setMode('full');for(let i=0;i<500;i++)q.sample(80,16.67);assert.equal(q.performance,false);
  assert.equal(createQuality({userAgent:'iPhone'}).performance,true);
});
