import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {webpFrames} from '../../../room/image-frames.js';
const data=i=>{const b=readFileSync(new URL(`../../../stations/slot/fallback/gif${i}.webp`,import.meta.url));return b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength);};
test('compatibility decoder preserves the four shipped WebP loops and frame boundaries',()=>{
  for(const [i,count] of [20,6,2,18].entries()){
    const parsed=webpFrames(data(i));assert.equal(parsed.frames.length,count);
    assert.ok(parsed.width>0&&parsed.height>0);
    for(const f of parsed.frames){
      assert.ok(f.x+f.width<=parsed.width&&f.y+f.height<=parsed.height);
      assert.ok(f.delay>0);assert.equal(new TextDecoder().decode(f.file.slice(0,4)),'RIFF');
      assert.equal(new DataView(f.file.buffer).getUint32(4,true),f.file.length-8);
    }
  }
});
test('truncated WebP chunks are rejected rather than reading incomplete frame data',()=>{
  assert.throws(()=>webpFrames(data(0).slice(0,-10)),/Truncated/);
  assert.throws(()=>webpFrames(new ArrayBuffer(16)),/Not WebP/);
});
