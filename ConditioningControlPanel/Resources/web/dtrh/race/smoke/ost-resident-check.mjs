/* ============================================================================
 * race/smoke/ost-resident-check.mjs - node self-check for the resident-music rule in race/audio.js.
 *
 *   node race/smoke/ost-resident-check.mjs      (exits 0 on pass, 1 with a count of failures)
 *
 * residentTracks(playlist, order, roomId) names the <audio> elements the player keeps once a room
 * plays: that room's track and the next room's (the lap wraps). Everything else is released after
 * its crossfade. This walks a rolled playlist through a whole lap and the menu pseudo-room.
 * ==========================================================================*/

import { residentTracks, rollPlaylist, ROOM_POOLS, MENU_TRACK, MENU_ROOM } from '../audio.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };

const order = Object.keys(ROOM_POOLS);
let s = 12345;
const rng = () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; };
const playlist = rollPlaylist(rng, order, ROOM_POOLS);

/* ---- 1. a whole lap: never more than two, always the room's own and the next room's ---- */
{
  let maxKeep = 0;
  for (let i = 0; i < order.length; i++) {
    const room = order[i], next = order[(i + 1) % order.length];
    const keep = residentTracks(playlist, order, room);
    maxKeep = Math.max(maxKeep, keep.size);
    ok(keep.has(playlist[room]), `${room}: keeps its own track ${playlist[room]}`);
    ok(keep.has(playlist[next]), `${room}: keeps the next room's (${next}: ${playlist[next]})`);
  }
  ok(maxKeep <= 2, `at most two resident over the lap (saw ${maxKeep})`);
  const last = order[order.length - 1];
  ok(residentTracks(playlist, order, last).has(playlist[order[0]]), 'the last room prefetches the first: the lap wraps');
}

/* ---- 2. the menu and the odd cases ---- */
{
  const menuList = { [MENU_ROOM]: MENU_TRACK };
  const keep = residentTracks(menuList, order, MENU_ROOM);
  ok(keep.size === 1 && keep.has(MENU_TRACK), 'under the menu only the theme is resident: nothing is prefetched before race');
  ok(residentTracks(playlist, order, null).size === 0, 'no room, nothing kept');
  ok(residentTracks(null, order, order[0]).size === 0, 'no playlist, nothing kept (and no throw)');
  ok(residentTracks(playlist, undefined, order[0]).size === 1, 'no order yet (before attach): just the room\'s own');
  const dup = { a: 'ost_sort', b: 'ost_sort' };
  ok(residentTracks(dup, ['a', 'b'], 'a').size === 1, 'the same track twice in a row is one element, not two');
  const gap = { a: 'ost_sort', c: 'ost_prizes' };
  ok(residentTracks(gap, ['a', 'b', 'c'], 'a').size === 1, 'a room with no track (all dead) prefetches nothing');
  const twice = { a: 'ost_sort', b: 'ost_prizes', c: 'ost_records' };
  ok(residentTracks(twice, ['a', 'b', 'a', 'c'], 'a', 2).has('ost_records'), 'a route that repeats a room: the position names the right neighbour');
  ok(residentTracks(twice, ['a', 'b', 'a', 'c'], 'a', 1).has('ost_prizes'), 'and a position that does not match the room falls back to the first visit');
}

if (fails) { console.error(`\nost-resident-check: ${fails} failure(s)`); process.exit(1); }
console.log('\nost-resident-check: all good');
