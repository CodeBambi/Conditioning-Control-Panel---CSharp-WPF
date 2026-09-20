import test from 'node:test';
import assert from 'node:assert/strict';
import { drawSymbol, reelFace, CELL } from '../symbols.js';

test('tall and wide GIFs fill the cell behind an uncropped subject using the same frame', () => {
  for (const [width,height] of [[90,300],[400,70],[224,256]]) {
    const calls=[], image={width,height};
    const ctx={filter:'none',save(){},restore(){},beginPath(){},roundRect(){},clip(){},fillRect(){},
      drawImage(img,x,y,w,h){calls.push({img,x,y,w,h,filter:this.filter});}};
    drawSymbol(ctx,'gif0',0,{gif:()=>image},CELL);
    const sharp=calls.at(-1);
    assert.equal(sharp.img,image);
    assert.ok(Math.abs(sharp.w/sharp.h-width/height)<1e-9);
    assert.ok(sharp.w<=CELL.hw*1.88+.01&&sharp.h<=CELL.hh*1.88+.01);
    if(calls.length>1){
      assert.equal(calls[0].img,image);
      assert.match(calls[0].filter,/blur/);
      assert.ok(calls[0].w>=CELL.hw*1.88&&calls[0].h>=CELL.hh*1.88);
    }
  }
});
test('EMI idle cycles vary, reaction ends, and Motion Off has no movement', () => {
  const frames=new Set([0,3600,7800,12000].map(t=>reelFace(t).frame));
  assert.ok(frames.size>=3);
  assert.equal(reelFace(0,{faceMood:'near',faceAge:600}).frame,2);
  assert.equal(reelFace(0,{faceMood:'near',faceAge:2000}).frame,3);
  assert.equal(reelFace(0,{faceMood:'jackpot',faceAge:0}).frame,10);
  assert.notEqual(reelFace(0,{faceMood:'jackpot',faceAge:400}).frame,10);
  assert.deepEqual(reelFace(0,{reduced:true}),reelFace(9999,{reduced:true}));
});
