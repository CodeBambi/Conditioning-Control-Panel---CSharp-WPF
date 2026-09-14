import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createRenderBudget} from '../render-budget.js';

test('4K and high-DPI phones stay within the pixel budget, including touch-only iPads',()=>{
  for(const [device,limit] of [[{},1500000],[{userAgent:'Android Mobile'},900000],[{userAgent:'Macintosh',maxTouchPoints:5},900000],[{deviceMemory:4},900000]]){
    const b=createRenderBudget(device,3);
    for(const [w,h] of [[3840,2160],[390,844],[844,390]])assert.ok(w*h*b.dpr(w,h)**2<=limit+1);
  }
});
test('sustained slow rendering lowers resolution, isolated stalls do not, and quality has a floor',()=>{
  const b=createRenderBudget({},2),initial=b.dpr(1280,720);
  for(let i=0;i<300;i++)b.sample(i===10?800:33.34,33.34);
  assert.equal(b.dpr(1280,720),initial);
  for(let i=0;i<300;i++)b.sample(70,33.34);
  assert.ok(b.dpr(1280,720)<initial);
  for(let i=0;i<3000;i++)b.sample(70,33.34);
  assert.equal(b.debug().scale,.6);
});

test('shipped customization models stay compressed and preserve clickable bays',async()=>{
  const {readFile}=await import('node:fs/promises');let bytes=0;
  for(const name of ['vending','knight','queen','rook']){
    const data=await readFile(new URL('../assets/customization/'+name+'.glb',import.meta.url));bytes+=data.length;
    const doc=JSON.parse(data.subarray(20,20+data.readUInt32LE(12)).toString());
    assert.ok(doc.extensionsUsed.includes('EXT_meshopt_compression'));
    if(name==='vending')for(let i=1;i<=9;i++)assert.ok(doc.nodes.some(n=>n.name==='bay_'+String(i).padStart(2,'0')));
  }
  assert.ok(bytes<1100000);
});
