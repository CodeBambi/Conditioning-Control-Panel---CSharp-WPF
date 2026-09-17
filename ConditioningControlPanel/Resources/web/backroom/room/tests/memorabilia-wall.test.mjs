import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {PHOTO_GROUPS, VAULT_PHOTOS, badgeFor, wallPhotos, PICTURE_WIDTH} from '../memorabilia-wall.js';

/* The five walls and their slot counts, as memorabilia.js hangs them: one by the door, two on the
 * entrance wall, one over the card table, two at the counter, two over the slots. Eight in all. */
const SLOTS = {entrance: 1, southwest: 2, cards: 1, parlour: 2, slots: 2};

test('eight slots, one vault card each, nothing hung twice', () => {
 const photos = wallPhotos();
 assert.equal(photos.length, 8);
 const counted = {};
 for (const p of photos) counted[p.group] = (counted[p.group] || 0) + 1;
 assert.deepEqual(counted, SLOTS);
 const features = photos.map(p => p.feature);
 assert.equal(new Set(features).size, features.length, 'a card on two walls is an accident, not a lineup');
 for (const f of features) assert.ok(VAULT_PHOTOS[f], f + ' is not a vault card');
 // Every slot id is unique and stable, because the viewer and the picks both key off it.
 assert.equal(new Set(photos.map(p => p.id)).size, 8);
});

test('the wall never advertises the room it hangs in, the ad frame\u2019s doors, or a hidden feature', () => {
 // ADS in room/main.js already runs Arcademy, DtRH and Focus Gaze on a frame of their own, and
 // Piece by Piece is the owner's standing hide. The Back Room inside the Back Room is a mirror.
 const forbidden = ['backroom', 'arcademy', 'dtrh', 'focusgaze', 'focus-gaze', 'piecebypiece', 'pbp', 'aimemory'];
 for (const key of Object.keys(VAULT_PHOTOS)) assert.ok(!forbidden.includes(key), key + ' must not be on the wall');
 for (const p of wallPhotos()) assert.ok(!forbidden.includes(p.feature), p.feature + ' must not be on the wall');
});

test('the tier sign follows the livery tier, and tier 0 wears words instead', () => {
 assert.match(badgeFor(1), /tier-badge-basic\.png$/);
 assert.match(badgeFor(2), /tier-badge-prime\.png$/);
 // 0 is the weekly-pass card: no tier, so no sign at all rather than the cheaper of the two.
 assert.equal(badgeFor(0), null);
 // Anything that is not one of the two priced tiers wears no sign: a wall must not guess a price.
 for (const bad of [null, undefined, -1, 3, '1', NaN]) assert.equal(badgeFor(bad), null);
 for (const p of wallPhotos()) {
  if (p.tier === 0) { assert.equal(p.badge, null); assert.ok(p.chip && p.chipKey, 'a tier-0 card needs its pass chip'); }
  else { assert.equal(p.badge, badgeFor(p.tier)); assert.equal(p.chip, null); }
 }
});

test('every visible word is a lexicon key the room can actually be sent', () => {
 for (const [key, card] of Object.entries(VAULT_PHOTOS)) {
  // Only br_-prefixed rows reach the page (BackRoomHostService.LexPrefix), so a tab_* or
  // exclusives_* key here would silently never arrive and the fallback would be the only copy.
  for (const k of [card.titleKey, card.captionKey, card.chipKey].filter(Boolean)) {
   assert.ok(k.startsWith('br_photo_'), key + ' has a key the host will not send: ' + k);
  }
  assert.ok(card.title && card.caption, key + ' needs an English fallback for both');
 }
 const keys = Object.values(VAULT_PHOTOS).flatMap(c => [c.titleKey, c.captionKey]);
 assert.equal(new Set(keys).size, keys.length, 'two cards sharing a key would share their wording');
});

test('the notes read at a glance: one register, two short lines', () => {
 for (const [key, card] of Object.entries(VAULT_PHOTOS)) {
  const lines = card.caption.split('\n');
  assert.ok(lines.length <= 2, key + ' has more lines than the paper prints');
  for (const line of lines) {
   assert.ok(line.length <= 34, key + ' has a line too long to read on a wall: ' + line);
   // The vault's own register (pl6_card_blurb_*): lowercase, second person, ends on a full stop.
   assert.equal(line, line.toLowerCase(), key + ' breaks the lowercase register: ' + line);
   assert.match(line, /\.$/, key + ' has a line that is not a sentence: ' + line);
   assert.ok(!line.includes('\u2014'), 'no em-dashes');
  }
 }
});

test('the art is the vault card\u2019s own, and never upscaled onto the paper', () => {
 // Four vault cards have no landscape hero art. Two of them (Takeover 882x973, Haptics 910x884)
 // are still wider than the polaroid's picture area, so they compose without being blown up;
 // She's Listening (512x512) and Lockdown (an icon) are the two that were left off for it.
 const measured = {'feature-fyp': 1376, 'feature-justdrop': 1376, 'feature-blink-trainer': 1376,
  'feature-remote-control': 1376, 'feature-awareness': 1376, 'feature-graded-intake': 1376,
  'feature-takeover': 882, 'feature-haptics': 910};
 for (const [key, card] of Object.entries(VAULT_PHOTOS)) {
  assert.ok(measured[card.art], key + ' hangs art nobody measured: ' + card.art);
  assert.ok(measured[card.art] >= PICTURE_WIDTH, card.art + ' would be upscaled on the paper');
 }
});

test('the authored geometry survives the move out of memorabilia.js', () => {
 // The lean alternates and the second photo in a pair hangs a little lower: the wall should look
 // pinned up by hand, and these were the numbers it was pinned with.
 const [door] = wallPhotos().filter(p => p.group === 'entrance');
 assert.deepEqual(door.position.map(n => Math.round(n * 100) / 100), [-1.75, 1.94, 7.76]);
 const pair = wallPhotos().filter(p => p.group === 'slots');
 assert.deepEqual(pair.map(p => p.tilt), [-.065, .06]);
 assert.deepEqual(pair.map(p => Math.round(p.position[2] * 100) / 100), [5.25, 4.55]);
 assert.equal(PHOTO_GROUPS.length, 5);
});

test('en.json says exactly what the wall says in English', () => {
 // The host only sends br_ rows, and every other language falls back to English, so a row that
 // has drifted from the fallback beside it would show two different notes on the same photograph.
 const en = JSON.parse(readFileSync(new URL('../../../../../Localization/Languages/en.json', import.meta.url), 'utf8'));
 for (const [key, card] of Object.entries(VAULT_PHOTOS)) {
  assert.equal(en[card.titleKey], card.title, key + ' title row');
  assert.equal(en[card.captionKey], card.caption, key + ' note row');
  if (card.chipKey) assert.equal(en[card.chipKey], card.chip, key + ' chip row');
 }
});
