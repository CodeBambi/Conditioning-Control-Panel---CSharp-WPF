import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createEndingCard} from './ending-card.js';
test('end card waits for shutdown, caches its raster and fits portrait without stretching',()=>{
 const log=[];let allocations=0;
 const ctx=new Proxy({}, {get(t,k){if(k==='createLinearGradient'||k==='createRadialGradient')return ()=>({addColorStop(){}});return (...args)=>log.push([k,...args]);}});
 const card=createEndingCard(()=>{allocations++;return {getContext:()=>ctx};});
 assert.equal(card.draw(ctx,480,720,3),false);assert.equal(allocations,0);
 card.draw(ctx,480,720,5);assert.equal(allocations,1);
 assert.ok(log.some(x=>x[0]==='fillText'&&x[1]==='YOU BROKE OUT'));
 const draw=log.findLast(x=>x[0]==='drawImage');assert.deepEqual(draw.slice(2),[0,225,480,270]);
 card.draw(ctx,480,720,50,true);assert.equal(allocations,1);
 card.reset();card.draw(ctx,1280,720,5);assert.equal(allocations,2);
});
