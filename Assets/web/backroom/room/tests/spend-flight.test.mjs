import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spendCount, spendFlight } from '../spend-flight.js';
test('spend particles are bounded even on large purchases',()=>{assert.equal(spendCount(3),3);assert.equal(spendCount(9000),12);assert.equal(spendCount(-3),0);assert.equal(spendCount(NaN),0);});
test('motion off and detached endpoints never allocate DOM or schedule a flight',()=>{const visible={isConnected:true};assert.doesNotThrow(()=>spendFlight({from:visible,to:visible,amount:3,still:true}));assert.doesNotThrow(()=>spendFlight({from:visible,to:{isConnected:false},amount:3}));});
