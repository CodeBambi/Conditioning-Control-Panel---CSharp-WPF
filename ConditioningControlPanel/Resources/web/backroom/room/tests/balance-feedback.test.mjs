import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createBalanceFeedback} from '../balance-feedback.js';
test('balance feedback uses displayed differences, combines countup and separates spends',()=>{
 let t=0;const classes=new Set(),box={dataset:{},offsetWidth:100,classList:{add:c=>classes.add(c),remove:c=>classes.delete(c)}};
 const fx=createBalanceFeedback({target:()=>box,still:()=>false,now:()=>t});
 fx.update(100);assert.equal(box.dataset.flow,undefined);
 t=10;fx.update(103);assert.equal(box.dataset.delta,'+3 SP');
 t=30;fx.update(105);assert.equal(box.dataset.delta,'+5 SP');
 t=40;fx.update(95);assert.equal(box.dataset.delta,'-10 SP');assert.equal(box.dataset.flow,'out');
 t=1000;fx.update(96);assert.equal(box.dataset.delta,'+1 SP');
});
test('still balance changes carry direction without motion',()=>{
 const box={dataset:{},classList:{add(){},remove(){}},offsetWidth:1};
 const fx=createBalanceFeedback({target:()=>box,still:()=>true});fx.update(10);fx.update(8);
 assert.equal(box.dataset.balanceStill,'true');assert.equal(box.dataset.delta,'-2 SP');
});
