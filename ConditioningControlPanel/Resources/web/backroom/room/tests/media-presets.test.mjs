import test from 'node:test';
import assert from 'node:assert/strict';
import { MEDIA_PRESETS, presetOffer } from '../hud.js';

const CAP = 8;   // AppSettings.BackRoomMediaSubCap, the host's own number

test('an empty picker offers every preset', () => {
  const offer = presetOffer([], CAP);
  assert.deepEqual(offer.names, [...MEDIA_PRESETS]);
  assert.equal(offer.room, CAP);
});

test('a preset already added is not offered as a duplicate, whatever its case', () => {
  const offer = presetOffer(['erotichypnosis', 'NSFWANIMEGIFS'], CAP);
  assert.ok(!offer.names.some((n) => n.toLowerCase() === 'erotichypnosis'));
  assert.ok(!offer.names.some((n) => n.toLowerCase() === 'nsfwanimegifs'));
  assert.equal(offer.names.length, MEDIA_PRESETS.length - 2);
});

test('a full list has no room, so a preset can never evict a niche the player chose', () => {
  const mine = ['mine1', 'mine2', 'mine3', 'mine4', 'mine5', 'mine6', 'mine7', 'mine8'];
  const offer = presetOffer(mine, CAP);
  assert.equal(offer.room, 0);
  // The names are still listed: the row explains the cap on the press rather than vanishing.
  assert.equal(offer.names.length, MEDIA_PRESETS.length);
});

test('room counts down as niches are added and never goes negative', () => {
  assert.equal(presetOffer(['a', 'b', 'c'], CAP).room, 5);
  assert.equal(presetOffer(new Array(11).fill('x'), CAP).room, 0);
});

test('a missing list or a missing cap degrades instead of throwing', () => {
  assert.equal(presetOffer(undefined, CAP).names.length, MEDIA_PRESETS.length);
  assert.equal(presetOffer(undefined, undefined).room, 0);
});

test('every preset is a bare community name the host will accept', () => {
  for (const name of MEDIA_PRESETS) assert.match(name, /^[A-Za-z0-9_]{2,40}$/);
  const lower = MEDIA_PRESETS.map((n) => n.toLowerCase());
  assert.equal(new Set(lower).size, MEDIA_PRESETS.length);
  // Presets must fit inside the cap on their own, or tapping through them locks the field out.
  assert.ok(MEDIA_PRESETS.length <= CAP);
});
