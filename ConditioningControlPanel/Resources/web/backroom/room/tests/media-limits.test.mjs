import test from 'node:test';
import assert from 'node:assert/strict';
import { MEDIA_LIMITS, MediaLimitError, imageDimensions, boundedImageBytes } from '../media-limits.js';
import { decodedSource } from '../gif-decode.js';
const gif=(w,h)=>new Uint8Array([71,73,70,56,57,97,w&255,w>>8,h&255,h>>8]);
function native(t,{width=30,height=40,frames=2}={}) {
 const prior={fetch:globalThis.fetch,location:globalThis.location,ImageDecoder:globalThis.ImageDecoder,document:globalThis.document};
 const stats={created:0,closed:0,frameClosed:0};
 globalThis.location={href:'https://ccp.game/'};
 globalThis.document={createElement:()=>({getContext:()=>({drawImage(){}})})};
 globalThis.ImageDecoder=class {
   constructor(){stats.created++;} static async isTypeSupported(){return true;}
   tracks={ready:Promise.resolve(),selectedTrack:{frameCount:frames}};completed=Promise.resolve();
   async decode(){return {image:{displayWidth:width,displayHeight:height,close(){stats.frameClosed++;}}};}close(){stats.closed++;}
 };
 t.after(()=>{for(const[k,v]of Object.entries(prior)){if(v===undefined)delete globalThis[k];else globalThis[k]=v;}});
 return stats;
}
test('oversized content length refuses before reading bytes',async()=>{
 let cancelled=false;
 const r=new Response(new ReadableStream({cancel(){cancelled=true;}}),{headers:{'content-length':String(MEDIA_LIMITS.bytes+1)}});
 await assert.rejects(boundedImageBytes(r),MediaLimitError);assert.equal(cancelled,true);
});
test('chunked image is cancelled when cumulative bytes exceed budget',async()=>{
 let cancelled=false, sent=0;
 const r=new Response(new ReadableStream({pull(c){c.enqueue(new Uint8Array(1024*1024));sent++;},cancel(){cancelled=true;}}));
 await assert.rejects(boundedImageBytes(r),MediaLimitError);assert.equal(cancelled,true);assert.ok(sent<=34);
});
test('GIF dimensions are refused before constructing native decoder',async t=>{
 const stats=native(t);globalThis.fetch=async()=>new Response(gif(4096,4096),{headers:{'content-type':'image/gif'}});
 await assert.rejects(decodedSource('/large.gif'),MediaLimitError);assert.equal(stats.created,0);
});
test('native decoded dimensions are checked too and rejected frames close',async t=>{
 const stats=native(t,{width:4096,height:4096});globalThis.fetch=async()=>new Response(gif(30,40),{headers:{'content-type':'image/gif'}});
 await assert.rejects(decodedSource('/bad.gif'),MediaLimitError);assert.equal(stats.frameClosed,1);assert.equal(stats.closed,1);
});
test('excess animation frame count closes decoder before first decode',async t=>{
 const stats=native(t,{frames:2001});globalThis.fetch=async()=>new Response(gif(30,40),{headers:{'content-type':'image/gif'}});
 await assert.rejects(decodedSource('/long.gif'),MediaLimitError);assert.equal(stats.frameClosed,0);assert.equal(stats.closed,1);
});
test('disposal signal cancels a stalled download promptly',async t=>{
 native(t);let signal;globalThis.fetch=(_,options)=>{signal=options.signal;return new Promise(()=>{});};
 const controller=new AbortController(),pending=decodedSource('/slow.gif',{signal:controller.signal});controller.abort();
 await assert.rejects(pending,{name:'AbortError'});assert.equal(signal.aborted,true);
});
test('stalled loads expire at the media deadline',async t=>{
 native(t);t.mock.timers.enable({apis:['setTimeout']});globalThis.fetch=()=>new Promise(()=>{});
 const pending=decodedSource('/slow.gif');t.mock.timers.tick(MEDIA_LIMITS.loadMs);
 await assert.rejects(pending,{name:'AbortError'});
});
test('bounded normal image still decodes and releases cleanly',async t=>{
 const stats=native(t);globalThis.fetch=async()=>new Response(gif(30,40),{headers:{'content-type':'image/gif'}});
 const src=await decodedSource('/ok.gif');assert.deepEqual([src.canvas.width,src.canvas.height],[30,40]);src.dispose();assert.equal(stats.closed,1);
});
test('header dimensions cover PNG and extended WebP',()=>{
 const png=new Uint8Array(24),p=new DataView(png.buffer);p.setUint32(0,0x89504e47);p.setUint32(16,1200);p.setUint32(20,800);
 assert.deepEqual(imageDimensions(png.buffer,'image/png'),[1200,800]);
 const webp=new Uint8Array(30),w=new DataView(webp.buffer);webp.set(new TextEncoder().encode('RIFF'),0);webp.set(new TextEncoder().encode('WEBPVP8X'),8);w.setUint32(16,10,true);webp[24]=99;webp[27]=49;
 assert.deepEqual(imageDimensions(webp.buffer,'image/webp'),[100,50]);
});
