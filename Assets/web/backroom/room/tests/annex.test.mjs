import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { START, EYE, RADIUS, WALLS, ANNEX, DOORWAY, ANNEX_PORTALS, VISIT_RANGE, inAnnex, inHall, blocked, step, reachable, nearestStation, normaliseStations, BLOCKERS } from '../walk.js';
import { validatedRoomPose } from '../race-portal.js';

const stations = normaliseStations(JSON.parse(readFileSync(new URL('../../stations.json', import.meta.url), 'utf8')));
const doorZ = (DOORWAY.min[1] + DOORWAY.max[1]) / 2;

test('the doorway is cut where the west wall carries nothing', () => {
  // The gap between the wheel plinth and the rose slot, in walkable terms, holds the whole doorway.
  const plinth = BLOCKERS.find((b) => b.min[0] < -6 && b.max[1] < 0 && b.max[1] > -1);
  const rose = stations.find((s) => s.key === 'slot:rose');
  assert.ok(plinth.max[1] <= DOORWAY.min[1], 'wheel plinth ends before the doorway');
  assert.ok(rose.fixture.bounds.min[2] - RADIUS >= DOORWAY.max[1], 'the rose slot starts after the doorway');
  assert.ok(DOORWAY.max[0] > -WALLS.x && DOORWAY.min[0] < ANNEX.max[0], 'the doorway overlaps both rooms');
});

test('collision is the union of the two rooms joined by the doorway', () => {
  assert.equal(blocked(-6.4, doorZ, stations), false, 'casino side of the door');
  assert.equal(blocked(-7.2, doorZ, stations), false, 'in the doorway');
  assert.equal(blocked(-10, 1, stations), false, 'annex middle');
  assert.equal(blocked(-7.2, 2.5, stations), true, 'the wall beside the doorway still stands');
  assert.equal(blocked(-7.2, -0.5, stations), true, 'and on the other side');
  assert.equal(blocked(-12.5, 1, stations), true, 'annex west wall');
  assert.equal(blocked(-10, -1.4, stations), true, 'annex north wall');
  assert.equal(blocked(-10, 3.4, stations), true, 'annex south wall');
  assert.equal(inHall(0, 0), true); assert.equal(inHall(-10, 1), true); assert.equal(inHall(-7.2, 2.5), false);
  assert.equal(inAnnex(0, 0), false); assert.equal(inAnnex(-7.2, doorZ), true); assert.equal(inAnnex(-10, 1), true);
  // The main room's own rules are untouched: the east wall flares and the counter platform blocks.
  assert.equal(blocked(7.15, 5.1, stations), false); assert.equal(blocked(7.15, -6, stations), true); assert.equal(blocked(0, -5, stations), true);
});

test('a walker slides through the doorway and the flood fill reaches every door from the entrance', () => {
  const pos = [-6.4, EYE, doorZ];
  for (let i = 0; i < 12; i++) step(pos, -0.4, 0, stations);
  assert.ok(pos[0] < ANNEX.max[0], 'walked into the annex: ' + pos[0]);
  const wall = [-6.4, EYE, 2.5];
  step(wall, -0.4, 0, stations);
  assert.equal(wall[0], -6.4, 'the wall beside the doorway stops a walker');
  for (const p of ANNEX_PORTALS) assert.ok(reachable(START, p.approach, stations), p.key + ' reachable from the entrance');
  for (const s of stations) assert.ok(reachable(START, s.approach, stations), s.key + ' still reachable');
});

test('the three doors are E targets like stations: one live race door, two locked', () => {
  const keys = ANNEX_PORTALS.map((p) => p.key);
  assert.equal(new Set(keys).size, 3);
  assert.deepEqual(ANNEX_PORTALS.filter((p) => p.portal === 'race').map((p) => p.key), ['annex_race']);
  assert.equal(ANNEX_PORTALS.filter((p) => p.portal === 'locked').length, 2);
  assert.equal(ANNEX_PORTALS.find((p) => p.portal === 'race').labelKey, 'br_station_race', 'the race door shares the cabinet label');
  const rows = [...stations, ...ANNEX_PORTALS];
  for (const p of ANNEX_PORTALS) {
    assert.equal(blocked(p.approach[0], p.approach[2], stations), false, p.key + ' approach is standable');
    assert.equal(nearestStation(p.approach, rows), p, p.key + ' is the nearest when standing at its approach');
    assert.ok(Math.hypot(p.look[0] - p.approach[0], p.look[2] - p.approach[2]) <= VISIT_RANGE, p.key + ' looks at its own door');
  }
  assert.equal(nearestStation([-6.4, EYE, doorZ], rows), null, 'nothing prompts in the casino by the doorway');
  assert.equal(normaliseStations(ANNEX_PORTALS).length, 0, 'portals are never stations');
});

test('a race return pose inside the annex is accepted; outside both rooms is not', () => {
  const race = ANNEX_PORTALS.find((p) => p.portal === 'race');
  const back = validatedRoomPose({ position: race.approach.slice(), yaw: 1.5, pitch: 0 });
  assert.deepEqual(back?.position, race.approach.slice());
  assert.ok(validatedRoomPose({ position: [-7.2, EYE, doorZ], yaw: 0, pitch: 0 }), 'in the doorway');
  assert.equal(validatedRoomPose({ position: [-7.2, EYE, 2.5], yaw: 0, pitch: 0 }), null, 'in the wall beside it');
  assert.equal(validatedRoomPose({ position: [-13, EYE, 1], yaw: 0, pitch: 0 }), null, 'past the annex');
  assert.equal(validatedRoomPose({ position: [7, EYE, 6], yaw: 0, pitch: 0 }), null, 'the old main-room bound still holds');
});
