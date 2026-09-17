import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
const source=(await readFile(new URL('../media.js',import.meta.url),'utf8'))
.replace("import { decodedSource } from '../../room/gif-decode.js';",'const decodedSource=(...args)=>globalThis.deckDecode(...args);')
.replace("import { clipSource, isClip } from '../../room/clip-source.js';",'const isClip=()=>false;const clipSource=async()=>null;')
.replace("'../../room/media-limits.js'",JSON.stringify(new URL('../../../room/media-limits.js',import.meta.url).href));
const {createDeck}=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));
const settle=()=>new Promise(resolve=>setImmediate(resolve));
function setup(t,decode){
 const saved={document:globalThis.document,deckDecode:globalThis.deckDecode,Image:globalThis.Image};
 globalThis.document={hidden:false};globalThis.deckDecode=decode;
 t.after(()=>{for(const[k,v]of Object.entries(saved)){if(v===undefined)delete globalThis[k];else globalThis[k]=v;}});
 return createDeck({media:async()=>({gifs:Array.from({length:13},(_,i)=>({key:'g'+i,url:'https://ccp.assets/'+i+'.gif'}))})});
}
test('visible values start first and idle prefetch starts only one unseen value',async t=>{
 t.mock.timers.enable({apis:['setTimeout']});const requests=[];
 const deck=await setup(t,(url)=>{requests.push(url);return new Promise(()=>{});});t.after(()=>deck.dispose());
 assert.equal(requests.length,0);deck.image('g12');assert.match(requests[0],/12.gif$/);
 t.mock.timers.tick(750);assert.equal(requests.length,2,'one background warm can coexist with visible loading');
 t.mock.timers.tick(5000);assert.equal(requests.length,2,'pending prefetch cannot fan out into all ranks');
});
test('dispose aborts active work and cancels future prefetch',async t=>{
 t.mock.timers.enable({apis:['setTimeout']});const signals=[];
 const deck=await setup(t,(_,o)=>{signals.push(o.signal);return new Promise(()=>{});});
 deck.image('g1');deck.dispose();assert.equal(signals[0].aborted,true);
 t.mock.timers.tick(2000);assert.equal(signals.length,1);
});
test('budget refusals never bypass limits using a browser image',async t=>{
 let images=0;const deck=await setup(t,async()=>{const e=new Error('too large');e.name='MediaLimitError';throw e;});
 globalThis.Image=class{constructor(){images++;}};t.after(()=>deck.dispose());
 deck.image('g0');await settle();assert.equal(images,0);assert.equal(deck.debug().failed,1);
});
test('hidden document performs no prefetch and resumes visible demand',async t=>{
 t.mock.timers.enable({apis:['setTimeout']});let loads=0;
 const deck=await setup(t,()=>{loads++;return new Promise(()=>{});});t.after(()=>deck.dispose());
 document.hidden=true;t.mock.timers.tick(750);deck.image('g2');assert.equal(loads,0);
 document.hidden=false;deck.tick(100);assert.equal(loads,1);
});
test('deck retains at most eight decoders and evicts an unseen rank for visible demand',async t=>{
 let now=0;t.mock.method(performance,'now',()=>now);
 let closed=0;const requests=[];
 const deck=await setup(t,async(url,o)=>{requests.push(url);assert.equal(o.maxBytes,4*1024*1024);return {canvas:{},animated:true,byteLength:o.maxBytes,tick:()=>false,dispose(){closed++;}};});
 t.after(()=>deck.dispose());
 for(let i=0;i<8;i++){deck.image('g'+i);await settle();}
 assert.equal(deck.debug().cached,8);assert.equal(deck.debug().retainedBytes,32*1024*1024);
 now=2000;deck.tick(1);deck.tick(2);deck.image('g0');deck.image('g8');await settle();
 assert.equal(closed,1);assert.equal(deck.debug().cached,8);assert.ok(deck.image('g0'));assert.ok(deck.image('g8'));
 assert.equal(requests.length,9,'only the newly visible rank reloads');
});
test('60Hz ticks cannot evict a card whose face repaints at 15Hz',async t=>{
 let now=0;t.mock.method(performance,'now',()=>now);const evicted=[];
 const deck=await setup(t,async url=>({canvas:{},animated:true,byteLength:100,tick:()=>false,dispose(){evicted.push(url);}}));t.after(()=>deck.dispose());
 for(let i=0;i<8;i++){deck.image('g'+i);await settle();}
 deck.image('g8');
 for(let frame=1;frame<=64;frame++){
   now=frame*1000/60;
   if(frame%4===0)deck.image('g0');
   deck.tick(now);await settle();
 }
 assert.equal(evicted.length,1,'one long-unseen rank yields to the pending visible rank');
 assert.ok(!evicted[0].endsWith('/0.gif'),'the visible 15Hz face stays resident between paints');
 assert.ok(deck.image('g0'));assert.ok(deck.image('g8'));
});
