/* ============================================================================
 * race/smoke/audio-check.mjs - node self-check for the pure parts of race/audio.js.
 *
 *   node race/smoke/audio-check.mjs      (exits 0 on pass, 1 with a count of failures)
 *
 * audio.js touches WebAudio and <audio> only inside createRaceAudio, so the module
 * itself imports clean in node and its exported helpers can be walked here: the combo
 * pitch ladder, the chime ladder, the voice cap's choice, the per-run playlist roll,
 * and (the audio pass) the level maths behind setLevels plus the ui blip rate limit.
 * Nothing here makes a sound: real listening is the owner's, on real speakers.
 * ==========================================================================*/

import {
  pitchSemis, chimeFor, pickVoiceToDrop, rollPlaylist, levelGain, uiAllowed,
  PITCH_CAP_SEMIS, MUSIC_LEVEL, UI_GAP_MS, UI_NAMES, MENU_TRACK, MENU_ROOM, TRACK_BY_NAME, ROOM_POOLS,
} from '../audio.js';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const near = (a, b) => Math.abs(a - b) < 1e-9;

/* ---- 1. the combo and chime ladders ------------------------------------- */
{
  ok(pitchSemis(0) === 0 && pitchSemis(3) === 0 && pitchSemis(4) === 1, 'the pop pitch climbs one semitone every four combo');
  ok(pitchSemis(999) === PITCH_CAP_SEMIS, 'and stops at the cap');
  ok(chimeFor(2).file === 'chime1' && chimeFor(3).file === 'chime2' && chimeFor(4).file === 'chime3', 'the rung ladder walks chime1 -> chime2 -> chime3');
  ok(chimeFor(8).semis > chimeFor(6).semis && chimeFor(99).semis <= PITCH_CAP_SEMIS, 'above x4 chime3 keeps rising, capped');
}

/* ---- 2. the voice cap picks the quietest -------------------------------- */
{
  ok(pickVoiceToDrop([{ level: 0.4 }, { level: 0.1 }, { level: 0.9 }]) === 1, 'the quietest live voice is the one dropped');
  ok(pickVoiceToDrop([]) === -1, 'an empty rack drops nobody');
}

/* ---- 3. the per-run playlist -------------------------------------------- */
{
  const rooms = Object.keys(ROOM_POOLS);
  const rng = () => 0.5;
  const a = rollPlaylist(rng, rooms), b = rollPlaylist(rng, rooms);
  ok(JSON.stringify(a) === JSON.stringify(b), 'the same rng rolls the same playlist twice');
  ok(rooms.every((r) => !a[r] || TRACK_BY_NAME[a[r]]), 'every pick is a real track row');
  const dead = new Set(['ost_campus']);
  ok(rollPlaylist(rng, rooms, ROOM_POOLS, dead).teagarden === undefined, 'a dead track leaves its only room silent, it does not throw');
}

/* ---- 4. the menu theme -------------------------------------------------- */
{
  ok(!!TRACK_BY_NAME[MENU_TRACK], 'MENU_TRACK names a track that exists in TRACKS');
  ok(!ROOM_POOLS[MENU_ROOM], 'MENU_ROOM is not a real room, so the run never draws it');
  ok(UI_NAMES.length === 5 && UI_NAMES.indexOf('tick') === 0, 'the ui blip names are a closed set of five');
}

/* ---- 5. the level maths (setLevels) ------------------------------------- */
{
  ok(near(levelGain(MUSIC_LEVEL, 1), MUSIC_LEVEL), 'a slider at 1 leaves the base gain alone');
  ok(near(levelGain(MUSIC_LEVEL, 0), 0), 'a slider at 0 is silence');
  ok(near(levelGain(MUSIC_LEVEL, 0.5), MUSIC_LEVEL / 2), 'and half is half');
  ok(near(levelGain(0.8, 2), 0.8) && near(levelGain(0.8, -3), 0), 'out of range clamps to 0..1');
  ok(near(levelGain(0.8, undefined), 0.8) && near(levelGain(0.8, 'loud'), 0.8), 'a value that is not a number leaves the gain where it was');
  ok(levelGain(MUSIC_LEVEL, 0.8) < MUSIC_LEVEL, 'the default option (0.8) sits under the mastered level');
}

/* ---- 6. the ui blip rate limit ------------------------------------------ */
{
  ok(uiAllowed(1000, 1000 - (UI_GAP_MS - 1)) === false, 'two blips inside the gap are one blip');
  ok(uiAllowed(1000, 1000 - UI_GAP_MS) === true, 'exactly the gap passes');
  ok(uiAllowed(1000, 500) === true, 'a slow move always sounds');
  let last = -1e9, heard = 0;
  for (let ms = 0; ms < 500; ms += 10) if (uiAllowed(ms, last)) { last = ms; heard++; }   // key repeat at 100 Hz
  ok(heard <= Math.ceil(500 / UI_GAP_MS) + 1, `key repeat for 500 ms is ${heard} blips, not 50`);
}

if (fails) { console.error(`\naudio-check: ${fails} failure(s)`); process.exit(1); }
console.log('\naudio-check: all good');
