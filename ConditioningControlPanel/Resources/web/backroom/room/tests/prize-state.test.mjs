import {test} from 'node:test';
import assert from 'node:assert/strict';
import {prizeState,prizeFrame} from '../../shared/prize-state.js';
test('ownership comes from successful state, including demo access granted by an expansion',()=>{
 assert.equal(prizeState({ok:false,catalog:[]}),null);
 assert.equal(prizeState({ok:true,open:false,catalog:[]}),null);
 assert.deepEqual(prizeState({ok:true,catalog:[{id:'rt_bundle_2',owned:{at:1}},{id:'rt_demo'}],prizes:{grants:['rt.original.00']}}),{owned:['rt_bundle_2'],demo:true,racing:true,tracks:[0]});
 assert.deepEqual(prizeState({ok:true,catalog:[]}),{owned:[],demo:false,racing:false,tracks:[]});
});
test('reveal finishes on its resting pose and motion off settles immediately',()=>{
 assert.equal(prizeFrame(0).alpha,0);assert.equal(prizeFrame(.6).done,false);
 for(const f of [prizeFrame(4),prizeFrame(.2,true)]){assert.equal(f.alpha,1);assert.equal(f.done,true);assert.ok(Math.abs(f.lift)<1e-10);}
});

test('bundle without demo unlocks racing and malformed grants do not',()=>{
 const state=prizeState({ok:true,catalog:[{id:'rt_bundle_1',owned:true}],prizes:{grants:['rt.original.01','rt.original.02','rt.original.03','rt.original.11','rt.original.1']}});
 assert.equal(state.racing,true);assert.equal(state.demo,false);assert.deepEqual(state.tracks,[1,2,3]);
});
