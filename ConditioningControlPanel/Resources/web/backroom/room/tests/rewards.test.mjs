import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createRoomRewards,DECORATIONS} from '../rewards.js';
test('unowned room starts empty; snapshots grant only the six decoration ids',()=>{
 const r=createRoomRewards();assert.deepEqual(r.snapshot(100).owned,[]);
 r.apply({decorations:{owned:['monstera','floor','__proto__','monstera']}});
 assert.deepEqual(r.snapshot().owned,['monstera']);assert.equal(DECORATIONS.length,6);
});
test('late snapshots cannot remove earned decorations or shorten Seeing Double',()=>{
 const r=createRoomRewards();r.apply({decorations:{owned:['ivy']},bonus:{until:20000,multiplier:2}});
 r.apply({decorations:{owned:[]},bonus:{until:10000,multiplier:2}});
 assert.ok(r.has('ivy'));assert.equal(r.snapshot(15000).multiplier,2);assert.equal(r.snapshot(20000).multiplier,1);
 r.apply({bonus:{until:0,multiplier:1}});assert.equal(r.snapshot(15000).until,20000);
});
test('malformed and absent rewards do not mint an entitlement',()=>{
 const r=createRoomRewards();r.apply({bonus:{until:Infinity,multiplier:2},decorations:{owned:'*'}});
 assert.deepEqual(r.snapshot(0),{owned:[],until:0,multiplier:1});
 r.apply({bonus:{until:99999,multiplier:1}});assert.equal(r.snapshot(0).multiplier,1);
});

test('a new room/account clears the in-memory reward snapshot',()=>{const r=createRoomRewards();r.apply({decorations:{owned:['ivy']},bonus:{until:99999,multiplier:2}});r.reset();assert.deepEqual(r.snapshot(0),{owned:[],until:0,multiplier:1});});
