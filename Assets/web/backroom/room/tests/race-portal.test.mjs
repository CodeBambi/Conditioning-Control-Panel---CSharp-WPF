import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRacePortal, racePortalUrl, consumeRoomPose, validatedRoomPose, raceOwnershipTracks, RACE_POSE_KEY, RACE_OWNERSHIP_KEY, CASINO_RETURN } from '../race-portal.js';
const loc = { href: 'http://localhost:8798/backroom/index.html' };
const returning = { href: loc.href + '?raceReturn=1' };
const pose = { position: [1, 1.65, 6], yaw: 0.5, pitch: -0.2, sp: 9999, grants: ['fake'] };
const storage = () => { const data = new Map(); return { getItem: k => data.get(k), setItem: (k,v) => data.set(k,v), removeItem: k => data.delete(k) }; };

test('preview navigation stays on the same origin and the two approved race paths', () => {
  for (const path of ['/dtrh/race.html', '/backroom/racing/race.html']) {
    const url = new URL(racePortalUrl(loc.href, path));
    assert.equal(url.origin, 'http://localhost:8798'); assert.equal(url.pathname, path);
    assert.equal(url.searchParams.get('casino'), '1'); assert.equal(url.searchParams.get('back'), CASINO_RETURN);
  }
  for (const path of ['https://example.com/race', '//example.com/race', '/backroom/index.html', '/dtrh/race.html?back=evil', '/dtrh/../race.html'])
    assert.equal(racePortalUrl(loc.href, path), null);
  assert.equal(racePortalUrl('javascript:alert(1)'), null);
});

test('local full navigation disposes once and stores only a bounded camera pose', () => {
  const store = storage(), calls = [];
  const portal = createRacePortal({ location: loc, storage: store, racePath: '/backroom/racing/race.html',
    getPose: () => pose, getOwnership: () => ({ owned: ['rt_demo'], racing: true, tracks: [3, 0, 3, 11, -1, 1.5, '2'] }),
    beforeNavigate: () => calls.push('dispose'), navigate: url => calls.push(url) });
  assert.equal(portal.open(), true); assert.equal(portal.open(), false);
  assert.deepEqual(JSON.parse(store.getItem(RACE_OWNERSHIP_KEY)), { version: 1, tracks: [0, 3] }, 'the owned tracks wait for the race page, cleaned');
  assert.equal(calls.length, 2); assert.equal(calls[0], 'dispose');
  const saved = JSON.parse(store.getItem(RACE_POSE_KEY));
  assert.deepEqual(Object.keys(saved.pose).sort(), ['pitch', 'position', 'yaw']);
  assert.equal(consumeRoomPose({ location: loc, storage: store }), null, 'ordinary reload does not consume');
  const restored = consumeRoomPose({ location: returning, storage: store });
  assert.deepEqual(restored.position, pose.position); assert.equal(restored.pitch, pose.pitch);
  assert.equal(consumeRoomPose({ location: returning, storage: store }), null, 'restore is one-shot');
});

test('native game-open sends no URL or balance and a refusal rearms the same portal', () => {
  const store = storage(), sent = []; let listener, stopped = 0, refused = '';
  const portal = createRacePortal({ hosted: true, storage: store, getPose: () => pose,
    send: m => sent.push(m), on: (type, fn) => { assert.equal(type, 'game-open-result'); listener = fn; return () => stopped++; },
    beforeNavigate: () => assert.fail('native close owns disposal'), onRefused: m => { refused = m.reason; } });
  assert.equal(portal.open(), true); assert.deepEqual(sent, [{ type: 'game-open', game: 'race' }]);
  listener({ game: 'other', ok: false }); assert.equal(portal.pending, true);
  listener({ game: 'race', ok: false, reason: 'locked' });
  assert.equal(portal.pending, false); assert.equal(refused, 'locked'); assert.equal(store.getItem(RACE_POSE_KEY), undefined);
  assert.equal(store.getItem(RACE_OWNERSHIP_KEY), undefined, 'a native host sends racingTracks itself');
  assert.equal(portal.open(), true); portal.dispose(); assert.equal(stopped, 1); assert.equal(portal.open(), false);
});

test('host denial and broken navigation never leave a stale saved camera', () => {
  const store = storage(); let reason;
  const portal = createRacePortal({ location: loc, storage: store, getPose: () => pose,
    navigate: () => { throw Error('blocked'); }, onRefused: m => { reason = m.reason; } });
  assert.equal(portal.open(), false); assert.equal(reason, 'navigation'); assert.equal(portal.pending, false);
  assert.equal(store.getItem(RACE_POSE_KEY), undefined);
});

test('invalid room positions and malformed data fall back to normal spawn', () => {
  for (const bad of [null, { ...pose, position: ['1',1.65,6] }, { ...pose, position: [7,1.65,6] },
    { ...pose, position: [1,1.65,8] }, { ...pose, position: [1,4,6] }, { ...pose, yaw: Infinity },
    { ...pose, pitch: 1.3 }, { ...pose, position: [NaN,1.65,6] }]) assert.equal(validatedRoomPose(bad), null);
  assert.ok(Math.abs(validatedRoomPose({ ...pose, yaw: Math.PI * 8 + 0.5 }).yaw - 0.5) < 1e-9);
  for (const raw of ['broken', '{}', JSON.stringify({ version: 2, pose }), 'x'.repeat(513)]) {
    const store = storage(); store.setItem(RACE_POSE_KEY, raw);
    assert.equal(consumeRoomPose({ location: returning, storage: store }), null); assert.equal(store.getItem(RACE_POSE_KEY), undefined);
  }
});

test('unavailable browser storage does not block entering or leaving the game', () => {
  const store = { getItem() { throw Error('private'); }, setItem() { throw Error('private'); }, removeItem() { throw Error('private'); } };
  let target;
  const portal = createRacePortal({ location: loc, storage: store, getPose: () => pose, navigate: url => { target = url; } });
  assert.equal(portal.open(), true); assert.ok(target.includes('/dtrh/race.html'));
  assert.equal(consumeRoomPose({ location: returning, storage: store }), null);
});

test('the ownership record is the prize snapshot track numbers and nothing else', () => {
  assert.deepEqual(raceOwnershipTracks({ tracks: [10, 0, 4] }), [0, 4, 10]);
  assert.deepEqual(raceOwnershipTracks(null), []);
  assert.deepEqual(raceOwnershipTracks({ racing: true }), []);
  assert.deepEqual(raceOwnershipTracks({ tracks: 'all' }), []);
  const store = storage(), portal = createRacePortal({ location: loc, storage: store, getPose: () => pose, navigate: () => {} });
  assert.equal(portal.open(), true);
  assert.deepEqual(JSON.parse(store.getItem(RACE_OWNERSHIP_KEY)), { version: 1, tracks: [] }, 'no snapshot yet: the door still opens, the race owns nothing');
});
