import {test} from 'node:test';
import assert from 'node:assert/strict';
import {isBackKey, isBackwardMove, isStationHit, BACK_PUSH} from '../leave-intent.js';

test('only a step backwards stands you up',()=>{
  for(const code of ['KeyS','ArrowDown'])assert.ok(isBackKey(code),code+' is a step back');
  for(const code of ['KeyW','ArrowUp','KeyA','KeyD','ArrowLeft','ArrowRight','ShiftLeft','KeyE','KeyM'])assert.ok(!isBackKey(code),code+' is not');
});

test('the touch stick leaves on a firm push back, not on a nudge, a strafe or a pull forward',()=>{
  assert.ok(isBackwardMove({x:0,z:1}));
  assert.ok(isBackwardMove({x:.2,z:BACK_PUSH}));
  assert.ok(!isBackwardMove({x:0,z:BACK_PUSH-.01}),'a nudge back is still sitting');
  assert.ok(!isBackwardMove({x:0,z:-1}),'forward never leaves');
  assert.ok(!isBackwardMove({x:1,z:.6}),'a strafe with a little back in it never leaves');
  assert.ok(!isBackwardMove({x:0,z:0}));
  assert.ok(!isBackwardMove(null));
  assert.ok(!isBackwardMove({x:0,z:NaN}));
});

/** A three.js-shaped tree: what the raycast hands back is an object with a `parent` chain and a `name`. */
const node=(name,parent=null)=>({name,parent});

test('a tap on the station you are sitting at is the station, everything else is the room',()=>{
  const wheel=node('wheel_holder');
  const rim=node('rim',node('wheel_model',wheel));
  const floor=node('floor'), wall=node('wall_north'), otherStation=node('lever',node('slot_holder'));
  assert.ok(isStationHit(rim,wheel),'the rim belongs to the wheel');
  assert.ok(isStationHit(wheel,wheel),'so does the group itself');
  assert.ok(!isStationHit(floor,wheel));
  assert.ok(!isStationHit(wall,wheel));
  assert.ok(!isStationHit(otherStation,wheel),'another cabinet is not this seat');
  assert.ok(!isStationHit(null,wheel),'empty air is the room');
});

test('a station runtime group hung on the room scene still counts as the station',()=>{
  const table=node('cards_holder');
  for(const name of ['cards_runtime','roulette_runtime_mat','runtime_sectors','wheel_reward'])
    assert.ok(isStationHit(node('card_3',node(name)),table),name+' is the station');
  assert.ok(!isStationHit(node('card_3',node('room_customization')),table));
});
