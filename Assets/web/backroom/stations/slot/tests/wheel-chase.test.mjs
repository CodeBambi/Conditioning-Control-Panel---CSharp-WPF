import test from 'node:test';
import assert from 'node:assert/strict';
import { createTape } from '../tape.js';

test('chase registers before purchase, stays pinned on prepaid spins, and hides prepaid bonus totals', async () => {
  const calls = [], chase = { id: 'target-a', symbols: ['gif2', '*', 'spiral'], oneIn: 48, bonusSpins: 9 };
  const outcome = { kind: 'plain', symbols: ['gif2', 'sub1', 'spiral3'], pay: 0, wheelBonus: 1 };
  const tape = createTape({ chaseSession: 'a'.repeat(32), mint: () => 'b'.repeat(32), request: async (op, body) => {
    calls.push({op, body});
    if (op === 'state') return { body: {sp:100, table:{stake:1}, wheelChase:chase} };
    if (op === 'chase') return { body:{wheelChase:chase} };
    return { body:{sp:98, wheelChase:chase, tape:{id:'t1',played:0,outcomes:[outcome,{...outcome,wheelBonus:0}]}} };
  }});
  assert.equal((await tape.open()).ok, true);
  assert.deepEqual(tape.snapshot().wheelChase, {id:chase.id,symbols:chase.symbols,oneIn:48});
  const first = await tape.press();
  assert.deepEqual(calls.map(c=>c.op), ['state','chase','chase','tape']);
  assert.equal(first.outcome.wheelBonus,1);
  assert.equal(tape.land(first.outcome),true);
  await tape.press();
  assert.equal(calls.length,4);
  assert.equal(tape.snapshot().wheelChase.bonusSpins,undefined);
});

test('failed chase registration refuses the purchase instead of selling an unregistered target', async () => {
  let purchases=0;
  const tape=createTape({chaseSession:'a'.repeat(32),mint:()=> 'b'.repeat(32),request:async op=> {
    if(op==='state')return {body:{sp:100}};
    if(op==='chase')return {ok:false,reason:'closed'};
    purchases++;return {body:{}};
  }});
  assert.equal((await tape.open()).reason,'closed');
  assert.equal((await tape.press()).reason,'closed');
  assert.equal(purchases,0);
});


test('an older backend without chase keeps the existing slot playable', async () => {
  let chaseCalls = 0;
  const outcome={kind:'plain',symbols:['emi','sub0','spiral1'],pay:0};
  const tape=createTape({chaseSession:'a'.repeat(32),mint:()=> 'b'.repeat(32),request:async op=> {
    if(op==='state')return {body:{sp:100}};
    if(op==='chase'){chaseCalls++;return {ok:false,reason:'bad_op'};}
    return {body:{sp:99,tape:{id:'legacy',played:0,outcomes:[outcome]}}};
  }});
  assert.equal((await tape.open()).ok,true);
  assert.equal((await tape.press()).kind,'play');
  assert.equal(chaseCalls,1);
  assert.equal(tape.snapshot().wheelChase,null);
});
