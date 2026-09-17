import test from 'node:test';
import assert from 'node:assert/strict';
import {createDecorationShop} from '../decoration-shop.js';
import {defaultDecorationLayout,ownedLayout} from '../decoration-catalog.js';
const response=(revision=0,owned=[],extra={})=>({ok:true,open:true,sp:100,catalogVersion:1,catalog:[{id:'ivy',priceSp:5,nameKey:'br_custom_ivy'}],decorations:{owned,layout:defaultDecorationLayout(),revision},...extra});
test('boot gates purchases and does not overwrite live wallet from background state',async()=>{
 const calls=[],balances=[];let wallet=77;
 const shop=createDecorationShop({request:async(op,args)=>{calls.push({op,args});return response();},onBalance:n=>balances.push(n),getBalance:()=>wallet});
 assert.equal(await shop.buy('ivy'),false);assert.equal(calls.length,0);await shop.load();
 assert.equal(shop.state.sp,77);assert.deepEqual(balances,[]);wallet=66;assert.equal(shop.state.sp,66);
});
test('double click has one charge request and lost response retries the identical idem',async()=>{
 const calls=[];let release;let attempts=0;
 const shop=createDecorationShop({makeId:()=> 'same-id',request:async(op,args)=>{calls.push({op,args});if(op==='state')return response();if(++attempts===1)return new Promise(resolve=>release=resolve);return response(1,['ivy'],{sp:95});}});
 await shop.load();const first=shop.buy('ivy');assert.equal(await shop.buy('ivy'),false);
 release({ok:false,reason:'timeout'});assert.equal(await first,false);assert.equal(shop.state.retry,true);
 assert.equal(await shop.load(),false,'background refresh cannot discard a pending charge');
 assert.equal(await shop.buy('ivy'),false);assert.equal(await shop.retry(),true);
 assert.deepEqual(calls[1].args,calls[2].args);assert.equal(calls[2].args.idem,'same-id');assert.equal(shop.state.owned.includes('ivy'),true);
});
test('catalog changed refusal adopts updated server price without retrying a charge',async()=>{
 let buys=0;const shop=createDecorationShop({makeId:()=>String(buys),request:async op=>op==='state'?response(): (++buys,response(0,[],{ok:false,reason:'catalog_changed',catalogVersion:2,catalog:[{id:'ivy',priceSp:9}]}))});
 await shop.load();await shop.buy('ivy');assert.equal(buys,1);assert.equal(shop.state.catalog[0].priceSp,9);assert.equal(shop.state.retry,false);
});
test('stale layout response cannot replace newer ownership or committed layout',async()=>{
 const shop=createDecorationShop({request:async op=>op==='state'?response(3,['ivy']):response(2,[])});
 await shop.load();await shop.save({...defaultDecorationLayout(),props:[false,true,false,false,false,false]});
 assert.equal(shop.state.revision,3);assert.ok(shop.state.owned.includes('ivy'));assert.equal(shop.state.retry,true);
});
test('free defaults and ownership checks sanitize every paid placement',()=>{
 const all={screens:[true,true,true],props:Array(6).fill(true),statues:[2,-1,0],handles:[0,1,2],floor:2,palette:2};
 assert.deepEqual(ownedLayout(all,[]),{...defaultDecorationLayout(),statues:[2,-1,0]});
 const owned=['screens4','ivy','lever_queen','floor_bloom','palette_sunset'];const layout=ownedLayout(all,owned);
 assert.deepEqual(layout.screens,[true,false,false]);assert.deepEqual(layout.handles,[-1,1,-1]);assert.equal(layout.floor,2);assert.equal(layout.palette,2);
});
test('wheel rewards union with bought items and layout save carries revision',async()=>{
 let saved;const shop=createDecorationShop({request:async(op,args)=>{if(op==='layout')saved=args;return response(4,['ivy']);}});
 await shop.load();shop.grant(['terrarium']);await shop.save(defaultDecorationLayout());
 assert.deepEqual(new Set(shop.state.owned),new Set(['ivy','terrarium']));assert.equal(saved.revision,4);
});
test('closed shop refuses purchases and layout changes',async()=>{
 const shop=createDecorationShop({request:async()=>response(0,[],{open:false})});await shop.load();
 assert.equal(await shop.buy('ivy'),false);assert.equal(await shop.save(defaultDecorationLayout()),false);
});
test('dispose ignores late purchase reply and balance update',async()=>{
 let resolve;const balances=[];const shop=createDecorationShop({makeId:()=> 'id',onBalance:n=>balances.push(n),request:async op=>op==='state'?response():new Promise(r=>resolve=r)});
 await shop.load();const buy=shop.buy('ivy');shop.dispose();resolve(response(1,['ivy'],{sp:95}));await buy;assert.deepEqual(balances,[]);
});
test('offline reply retains original purchase identity and retries safely',async()=>{
 const calls=[];let bought=false;
 const shop=createDecorationShop({makeId:()=> 'offline-id',request:async(op,args)=>{if(op==='state')return response();calls.push(args);if(!bought){bought=true;return {ok:false,reason:'offline'};}return response(1,['ivy']);}});
 await shop.load();await shop.buy('ivy');assert.equal(shop.state.retry,true);await shop.retry();assert.deepEqual(calls[0],calls[1]);
});
test('throwing UI listeners or wallet callbacks never wedge transaction settlement',async()=>{
 const shop=createDecorationShop({makeId:()=> 'id',getBalance(){throw new Error('ui');},onBalance(){throw new Error('ui');},request:async op=>op==='state'?response():response(1,['ivy'])});
 shop.subscribe(()=>{throw new Error('ui');});assert.equal(await shop.load(),true);assert.equal(await shop.buy('ivy'),true);
 assert.equal(shop.state.busy,false);assert.equal(shop.state.retry,false);assert.ok(shop.state.owned.includes('ivy'));
});
