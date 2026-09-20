import { test } from 'node:test';
import assert from 'node:assert/strict';
import { flashPreviewFrames } from '../flash-preview.js';
test('Motion Off keeps every sampled V2 preview still', () => {
  for(let variant=0; variant<9; variant++) {
    const f=flashPreviewFrames({width:200,height:150,variant,motion:false});
    assert.equal(new Set(f.map(x=>x.transform)).size,1);
    assert.equal(f[0].opacity,0); assert.equal(f.at(-1).opacity,0);
  }
});
test('motion samples vary while preserving a readable hold and finite transforms', () => {
  for(const portrait of [false,true]) for(const variant of [0,1,2,3,4,5]) {
    const f=flashPreviewFrames({width:200,height:150,variant,portrait,opacity:.6});
    assert.ok(new Set(f.map(x=>x.transform)).size>2);
    assert.ok(f.slice(1,-1).every(x=>x.opacity===.6));
    assert.ok(f.every(x=>! /NaN|Infinity/.test(x.transform)));
    assert.ok(f.every((x,i)=>i===0||x.offset>f[i-1].offset));
  }
});
