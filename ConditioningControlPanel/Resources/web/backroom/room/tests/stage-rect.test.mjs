import test from 'node:test';
import assert from 'node:assert/strict';
import { stageRect, ndcIn, viewportPageRect } from '../stage-rect.js';

const rect=(x,y,w,h)=>({left:x,top:y,right:x+w,bottom:y+h,width:w,height:h});
/* A bay's corner as the close-up places it and as a finger reads it back: vending-view.js projects the bay
 * into the DOM box to put a pick button there, so a press in the middle of that button has to normalise to
 * the very NDC the projection came from. Any box mismatch shows up here as an offset row. */
const roundTrip=(box,ndcX,ndcY)=>{
  const x=box.dom.x+(ndcX+1)*box.dom.w/2, y=box.dom.y+(1-ndcY)*box.dom.h/2;
  return ndcIn(box,x+(box.page.x-box.dom.x),y+(box.page.y-box.dom.y));
};

test('a stage whose buffer matches its canvas maps exactly as the room did by hand',()=>{
  const canvas=rect(0,0,1000,800), mount=rect(500,120,500,360);
  const box=stageRect(canvas,mount,1000,800);
  assert.deepEqual(box.dom,{x:0,y:0,w:500,h:360});
  assert.deepEqual(box.gl,{x:500,y:320,w:500,h:360});   // y up from the buffer's bottom edge
  assert.deepEqual(box.page,{x:500,y:120,w:500,h:360});
});

test('a canvas that clips the stage keeps the picks on the bays it draws',()=>{
  // The phone sheet: the stage runs past the bottom of the canvas, and the canvas starts right of the page.
  const canvas=rect(40,0,900,600), mount=rect(0,300,500,400);
  const box=stageRect(canvas,mount,900,600);
  assert.deepEqual(box.page,{x:40,y:300,w:460,h:300});
  assert.equal(box.dom.x,40,'the clip moved the left edge, so the buttons move with it');
  assert.equal(box.dom.y,0);
  const back=roundTrip(box,-0.5,0.75);
  assert.ok(Math.abs(back.x+0.5)<1e-9 && Math.abs(back.y-0.75)<1e-9,'a press in a button reads its own bay');
});

test('a drawing buffer that no longer matches the CSS box scales the pass, not the buttons',()=>{
  // A phone mid-rotation: resize() applied the portrait measurement, the canvas box has gone landscape.
  const canvas=rect(0,0,800,380), mount=rect(400,60,400,240);
  const box=stageRect(canvas,mount,390,790);
  assert.deepEqual(box.dom,{x:0,y:0,w:400,h:240},'the DOM box stays in page pixels');
  assert.equal(box.gl.x,Math.round(400*390/800));
  assert.equal(box.gl.w,Math.round(400*390/800));
  assert.equal(box.gl.h,Math.round(240*790/380));
  assert.equal(box.gl.y,Math.round((380-300)*790/380));
  const back=roundTrip(box,0.25,-0.6);
  assert.ok(Math.abs(back.x-0.25)<1e-9 && Math.abs(back.y+0.6)<1e-9,'the pick is unaffected by the buffer');
});

test('a stage too small or a canvas with no box has no rectangle to draw in',()=>{
  const canvas=rect(0,0,1000,800);
  assert.equal(stageRect(canvas,rect(500,0,3,300),1000,800),null);
  assert.equal(stageRect(canvas,rect(2000,0,400,300),1000,800),null,'a stage right of the canvas');
  assert.equal(stageRect(rect(0,0,0,0),rect(0,0,400,300),0,0),null);
  assert.equal(stageRect(null,rect(0,0,400,300),1,1),null);
});

test('a buffer reported as zero falls back to the canvas box instead of collapsing the viewport',()=>{
  const box=stageRect(rect(0,0,1000,800),rect(0,0,400,300),0,0);
  assert.deepEqual(box.gl,{x:0,y:500,w:400,h:300});
});

/* The room's own picks (scene.js pickAt): the room renders into customization.previewBox while the Room
 * Service panel is up, so a finger has to be normalised into that pass and not into the canvas. */
const full = (w, h) => ({ page: { x: 0, y: 0, w, h } });

test('a full-canvas viewport is the canvas rect itself, so the room pick stays the arithmetic it was',()=>{
  const canvas=rect(0,0,1000,800);
  const r=viewportPageRect(canvas,{x:0,y:0,w:1000,h:800},1000,800);
  assert.deepEqual([r.left,r.top,r.right,r.bottom],[0,0,1000,800]);
  const box=stageRect(canvas,r,1000,800);
  assert.deepEqual(box.page,{x:0,y:0,w:1000,h:800});
  // Bit for bit what pickAt() computed by hand from canvas.getBoundingClientRect().
  assert.deepEqual(ndcIn(box,731,219),{x:(731-0)/1000*2-1,y:1-(219-0)/800*2});
});

test('a panel docked down one edge leaves the room a strip, and a pick in the strip is centred in it',()=>{
  const canvas=rect(0,0,1000,800);
  // previewBox with the sheet at x=420: the strip left of it, y up from the canvas bottom.
  const box=stageRect(canvas,viewportPageRect(canvas,{x:0,y:0,w:420,h:800},1000,800),1000,800);
  assert.deepEqual(box.page,{x:0,y:0,w:420,h:800});
  const mid=ndcIn(box,210,400);
  assert.ok(Math.abs(mid.x)<1e-9&&Math.abs(mid.y)<1e-9,'the middle of the pane is the middle of the room');
  assert.ok(Math.abs(ndcIn(full(1000,800),210,400).x+0.58)<1e-9,'the full canvas rect is the bug: -0.58');
});

test('the phone sheet leaves a band above it, and a pointer in the band reads its own point',()=>{
  const canvas=rect(0,60,420,760);       // a phone canvas that does not start at the top of the page
  // previewBox for a sheet whose top edge is 300px down the mount: the band is the top 300 viewport pixels.
  const pageRect=viewportPageRect(canvas,{x:0,y:760-300,w:420,h:300},420,760);
  assert.deepEqual([pageRect.left,pageRect.top,pageRect.width,pageRect.height],[0,60,420,300]);
  const box=stageRect(canvas,pageRect,420,760);
  assert.deepEqual(box.page,{x:0,y:60,w:420,h:300});
  const back=roundTrip(box,-0.5,-0.9);
  assert.ok(Math.abs(back.x+0.5)<1e-9&&Math.abs(back.y+0.9)<1e-9,'a tap projected into the band reads back');
  // The same finger, normalised against the canvas rect the way pickAt() used to: near the room's floor
  // reads as most of the way up its wall, which is the offset a room-pane tap would have had.
  const buggy=ndcIn({page:{x:0,y:60,w:420,h:760}},105,345);
  assert.ok(Math.abs(buggy.y-back.y)>1,'the full canvas rect is the bug');
});

test('a viewport measured before the canvas box changed scales instead of drifting',()=>{
  // The canvas has since gone half as wide in CSS pixels; the pass was measured at the old renderer size.
  const canvas=rect(0,0,500,400);
  const r=viewportPageRect(canvas,{x:0,y:0,w:420,h:800},1000,800);
  assert.deepEqual([r.left,r.top,r.width,r.height],[0,0,210,400]);
});
