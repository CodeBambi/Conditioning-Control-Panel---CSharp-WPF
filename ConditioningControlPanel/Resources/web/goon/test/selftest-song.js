// Self-contained sanity pass over GAME NIGHT: THE SONG.
//
//   node Resources/web/goon/test/selftest-song.js
//
// What is pinned here:
//   1. the pure link rules (core/song.js, copied from race/cloud.js): the CDN or
//      same origin locally, the CDN alone off the wire, nothing with credentials;
//   2. the clamps: a song length is 0 or 60..1200, pinned in wire.js both ways;
//   3. caps.night: advertised by makeCaps, read by peerSpeaksNight, and a peer that
//      never heard of it is NEVER sent a `t:'song'` frame;
//   4. the engine seam: the host's pick becomes the consent sheet's length and the
//      guest learns the url before Live; a peer's bad url is ignored;
//   5. the player follows the match clock: seeks past the drift limit, stops when
//      Live ends, and a load failure is silence, not an error.

import { serialize, parse } from '../core/wire.js';
import {
  GoonMatchPhase, NIGHT_CAP_VERSION, makeCaps, makeSong, peerSpeaksNight,
} from '../core/contracts.js';
import { local as localCaps } from '../core/caps.js';
import {
  SONG_DRIFT_MS, SONG_HOST, clampSongSec, clampSongSub, parseSongLink, songClock,
  songSyncAction, wireSongUrl,
} from '../core/song.js';
import { GoonMatchService } from '../core/match.js';
import { createLoopbackPair, loopbackOptions } from '../net/loopbackTransport.js';
import { createSongPlayer } from '../ui/songPlayer.js';
import { S } from '../ui/strings.js';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const quiet = { info() {}, warn() {}, error() {}, debug() {}, log() {} };
const tick = (ms) => new Promise((r) => setTimeout(r, ms));
const CDN = 'https://' + SONG_HOST + '/0b7c1e2a-aaaa-bbbb-cccc-1234567890ab.mp3';

// ============================================================ 1. link rules
{
  const good = parseSongLink('  ' + CDN + '  ');
  ok(good.url === CDN, 'a cdn track link is playable', JSON.stringify(good));
  ok(typeof good.title === 'string' && good.title.length > 0, 'and gets a readable title');
  ok(parseSongLink('https://bambicloud.com/file/abc').refused === 'page', 'a bambicloud PAGE link is refused as a page');
  // The owner's paste (2026-09-23): a track's PAGE link maps straight onto its CDN file.
  const ID = 'a15c22e0-d347-4d92-9f78-0fb37099e549';
  const WANT = 'https://' + SONG_HOST + '/' + ID + '.mp3';
  for (const link of [
    'https://bambicloud.com/file/' + ID,
    'https://www.bambicloud.com/file/' + ID,
    'http://bambicloud.com/file/' + ID,
    'https://bambicloud.com/file/' + ID + '/',
    'https://bambicloud.com/file/' + ID + '?ref=share#top',
    'https://BambiCloud.com/file/' + ID.toUpperCase(),
  ]) {
    const v = parseSongLink('  ' + link + ' ');
    ok(v.url === WANT, 'a track page link plays its cdn file: ' + link, JSON.stringify(v));
    ok(v.url && wireSongUrl(v.url) === WANT, 'and that url passes the wire rule too: ' + link);
  }
  ok(parseSongLink('https://bambicloud.com/playlist/' + ID).refused === 'page', 'a playlist link is still a page');
  ok(parseSongLink('https://bambicloud.com/file/' + ID + '/extra').refused === 'page', 'a deeper path is still a page');
  ok(parseSongLink('https://evil.bambicloud.com/file/' + ID).refused === 'page', 'only bambicloud.com and www map a file link');
  ok(parseSongLink('https://evil.example/x.mp3').refused === 'host', 'another host is refused');
  ok(parseSongLink('').refused === 'empty', 'nothing is "empty"');
  ok(parseSongLink('not a link').refused === 'bad', 'not a link is "bad"');
  ok(parseSongLink('http://' + SONG_HOST + '/a.mp3').refused !== undefined, 'plain http on the cdn is refused');
  ok(parseSongLink('https://u:p@' + SONG_HOST + '/a.mp3').refused === 'bad', 'credentials in the url are refused');
  ok(parseSongLink('https://app.example/own.mp3', 'https://app.example').url === 'https://app.example/own.mp3',
    'same origin is playable for a LOCAL pick');

  ok(wireSongUrl(CDN) === CDN, 'the wire accepts a cdn url');
  ok(wireSongUrl('https://app.example/own.mp3') === '', 'the wire NEVER accepts "same origin"');
  ok(wireSongUrl('http://' + SONG_HOST + '/a.mp3') === '', 'the wire refuses http');
  ok(wireSongUrl('https://' + SONG_HOST + ':8443/a.mp3') === '', 'the wire refuses a port');
  ok(wireSongUrl('https://x@' + SONG_HOST + '/a.mp3') === '', 'the wire refuses credentials');
  ok(wireSongUrl('https://' + SONG_HOST + '/' + 'a'.repeat(600)) === '', 'the wire refuses an oversized url');
  ok(wireSongUrl(42) === '' && wireSongUrl(null) === '', 'the wire refuses non-strings');
}

// ============================================================ 2. clamps + wire
{
  ok(clampSongSec(0) === 0 && clampSongSec(-5) === 0 && clampSongSec(NaN) === 0, 'no length reads 0');
  ok(clampSongSec(true) === 0 && clampSongSec({}) === 0, 'booleans and objects read 0');
  ok(clampSongSec(12) === 60, 'a short song is clamped up to 60 s');
  ok(clampSongSec(5000) === 1200, 'a long one is clamped down to 1200 s');
  ok(clampSongSec(247.4) === 247 && clampSongSec('300') === 300, 'rounded, quoted numbers read');
  ok(clampSongSub('set') === 'set' && clampSongSub('clear') === 'clear' && clampSongSub('boom') === '', 'subs are pinned');
  ok(songClock(247) === '4:07', 'm:ss label');

  const out = JSON.parse(serialize(makeSong({ sub: 'set', url: CDN, title: 't', dur_sec: 99999 })));
  ok(out.t === 'song' && out.dur_sec === 1200, 'outbound dur_sec is clamped', JSON.stringify(out));
  const clear = JSON.parse(serialize(makeSong({ sub: 'clear' })));
  ok(clear.url === undefined && clear.title === undefined, 'a clear frame strips its null url/title');
  const bad = parse(JSON.stringify({ t: 'song', sub: 'nuke', url: CDN, dur_sec: -3 }), { logger: quiet });
  ok(bad && bad.sub === '' && bad.dur_sec === 0, 'inbound sub and dur_sec are clamped', JSON.stringify(bad));
  const nan = parse(JSON.stringify({ t: 'song', sub: 'set', dur_sec: 'lots' }), { logger: quiet });
  ok(nan && nan.dur_sec === 0, 'a non-number length reads 0');
}

// ============================================================ 3. caps.night
{
  ok(makeCaps({}).night === 0, 'caps default night 0');
  ok(makeCaps({ night: NIGHT_CAP_VERSION }).night === 1, 'NIGHT_CAP_VERSION is 1 and round-trips');
  ok(localCaps({ night: NIGHT_CAP_VERSION }).night === 1, 'core/caps.js local() passes night through');
  ok(peerSpeaksNight({ night: 1 }) && peerSpeaksNight({ night: '1' }), 'night 1 (or "1") speaks night');
  ok(!peerSpeaksNight({ night: true }) && !peerSpeaksNight({}) && !peerSpeaksNight(null), 'true/absent/null do not');
  const hello = parse(serialize({ t: 'hello', v: 1, caps: makeCaps({ night: 1 }) }), { logger: quiet });
  ok(hello && hello.caps.night === 1, 'caps.night survives a hello round trip');
}

// ============================================================ 4. engine seam
async function pairOf(guestNight) {
  const pair = createLoopbackPair(loopbackOptions({ latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, logger: quiet }));
  const a = new GoonMatchService(pair.host, true, { logger: quiet, caps: localCaps({ night: 1 }), displayName: 'A', tag: 'GG:A' });
  const b = new GoonMatchService(pair.guest, false, {
    logger: quiet, caps: localCaps(guestNight ? { night: 1 } : {}), displayName: 'B', tag: 'GG:B',
  });
  const sentByA = [];
  const origSend = a._send.bind(a);
  a._send = (m) => { if (m) sentByA.push(m.t); return origSend(m); };
  a.adoptLobby();
  b.adoptLobby();
  await pair.connect();
  await tick(60);
  return { a, b, sentByA };
}

{
  // A host that picks BEFORE the guest's hello: the frame goes out on hello.
  const pair = createLoopbackPair(loopbackOptions({ latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0, logger: quiet }));
  const a = new GoonMatchService(pair.host, true, { logger: quiet, caps: localCaps({ night: 1 }), displayName: 'A', tag: 'GG:A' });
  const b = new GoonMatchService(pair.guest, false, { logger: quiet, caps: localCaps({ night: 1 }), displayName: 'B', tag: 'GG:B' });
  a.adoptLobby();
  ok(a.setSong({ url: CDN, title: 'early', durSec: 300 }) === true, 'the host can pick while waiting in the lobby');
  b.adoptLobby();
  await pair.connect();
  await tick(80);
  ok(b.song && b.song.url === CDN && b.song.title === 'early', 'an early pick reaches the guest on hello', JSON.stringify(b.song));
  ok(b.consentSheet.live_duration_sec === 300, 'and the sheet length is the song', String(b.consentSheet.live_duration_sec));
  a.dispose?.(); b.dispose?.();
}

{
  const { a, b } = await pairOf(true);
  ok(a.peerSupportsNight === true && b.peerSupportsNight === true, 'both sides read caps.night off the hello');
  ok(b.setSong({ url: CDN, title: 'x', durSec: 300 }) === false, 'the GUEST cannot pick the song');
  ok(a.setSong({ url: CDN, title: 'a <b>song</b>', durSec: 247.2 }) === true, 'the host picks');
  await tick(60);
  ok(a.consentSheet.live_duration_sec === 247, 'the pick becomes the sheet length', String(a.consentSheet.live_duration_sec));
  ok(b.consentSheet.live_duration_sec === 247, 'on both sides, through proposeConsent');
  ok(b.song && b.song.url === CDN && b.song.durSec === 247, 'the guest has the url before Live', JSON.stringify(b.song));
  ok(b.song && !/[<>]/.test(b.song.title), 'the title is sanitized');
  ok(a.song === a.localSong && b.song === b.remoteSong, '`song` is the host pick on both sides');

  // A forged url straight onto the guest's pump: ignored.
  b._handleSong(parse(JSON.stringify({ t: 'song', sub: 'set', url: 'https://evil.example/x.mp3', dur_sec: 300 }), { logger: quiet }));
  ok(b.song && b.song.url === CDN, 'a non-cdn url from the peer is ignored');

  a.setSong(null);
  await tick(40);
  ok(b.song === null, 'clearing the pick reaches the guest');

  // Past the lobby, a frame changes nothing.
  a.setSong({ url: CDN, title: 'late', durSec: 200 });
  await tick(1000);   // the guest applies song frames about once a second (latest wins)
  b._phase = GoonMatchPhase.Live;
  b._handleSong(parse(JSON.stringify({ t: 'song', sub: 'clear' }), { logger: quiet }));
  ok(b.song && b.song.title === 'late', 'a song frame during Live is ignored');
  a._phase = GoonMatchPhase.Live;
  ok(a.setSong(null) === false, 'the host cannot change the song during Live');
  a.dispose?.(); b.dispose?.();
}

{
  const { a, b, sentByA } = await pairOf(false);
  ok(a.peerSupportsNight === false, 'a peer without caps.night is read as not speaking it');
  a.setSong({ url: CDN, title: 'x', durSec: 300 });
  await tick(40);
  ok(!sentByA.includes('song'), 'NO song frame is ever sent to a peer without caps.night', sentByA.join(','));
  ok(b.consentSheet.live_duration_sec === 300, 'the length still travels on the ordinary consent sheet');
  a.dispose?.(); b.dispose?.();
}

// ============================================================ 5. the player
{
  const a0 = songSyncAction({ elapsedMs: 5000, audioSec: NaN });
  ok(a0 && a0.seek === 5, 'no position yet -> seek to the match clock');
  ok(songSyncAction({ elapsedMs: 5000, audioSec: 5.2 }) === null, 'inside the drift limit -> leave it');
  const a1 = songSyncAction({ elapsedMs: 5000, audioSec: 5 + (SONG_DRIFT_MS + 50) / 1000 });
  ok(a1 && a1.seek === 5, 'past the drift limit -> re-seek');
  ok(songSyncAction({ elapsedMs: 61000, audioSec: 60, liveMs: 60000 }).stop === true, 'past Live -> stop');
  ok(songSyncAction({ elapsedMs: 30000, audioSec: 20, durationSec: 25 }).stop === true, 'past the file -> stop');

  function fakeAudio() {
    const listeners = {};
    return {
      paused: true, currentTime: 0, readyState: 1, duration: 300, volume: 1, src: '',
      plays: 0,
      addEventListener(ev, fn) { (listeners[ev] = listeners[ev] || []).push(fn); },
      fire(ev) { for (const fn of listeners[ev] || []) fn(); },
      load() {}, removeAttribute() {},
      play() { this.plays++; this.paused = false; return Promise.resolve(); },
      pause() { this.paused = true; },
    };
  }
  const phaseFns = [];
  const fakeMatch = {
    phase: GoonMatchPhase.Consent,
    song: { url: CDN, title: 't', durSec: 300 },
    liveElapsedMs: 0,
    consentSheet: { live_duration_sec: 300 },
    onPhaseChanged(fn) { phaseFns.push(fn); return () => {}; },
    onSongChanged() { return () => {}; },
  };
  const made = [];
  const player = createSongPlayer({
    match: fakeMatch,
    audio: { volumes: { master: 0.5, music: 0.5 } },
    logger: quiet,
    makeAudio: () => { const e = fakeAudio(); made.push(e); return e; },
  });
  ok(made.length === 1 && made[0].src === CDN, 'the player preloads the song before Live');
  ok(made[0].paused, 'and does not play it before Live');

  fakeMatch.phase = GoonMatchPhase.Live;
  fakeMatch.liveElapsedMs = 2000;
  for (const fn of phaseFns) fn(GoonMatchPhase.Live);
  ok(!made[0].paused && made[0].currentTime === 2, 'Live start plays from the match clock', String(made[0].currentTime));
  ok(Math.abs(made[0].volume - 0.25) < 1e-9, 'volume is music x master', String(made[0].volume));

  made[0].currentTime = 9;   // drifted
  fakeMatch.liveElapsedMs = 4000;
  player.tick();
  ok(made[0].currentTime === 4, 'a drift is pulled back to the match clock');

  fakeMatch.phase = GoonMatchPhase.Recap;
  for (const fn of phaseFns) fn(GoonMatchPhase.Recap);
  ok(made[0].paused && player.playing === false, 'the recap (and so Mercy) stops the song');
  player.dispose();

  // A load failure is silence, not an error.
  const made2 = [];
  const m2 = Object.assign({}, fakeMatch, { phase: GoonMatchPhase.Consent });
  const phase2 = [];
  m2.onPhaseChanged = (fn) => { phase2.push(fn); return () => {}; };
  const p2 = createSongPlayer({ match: m2, logger: quiet, makeAudio: () => { const e = fakeAudio(); made2.push(e); return e; } });
  made2[0].fire('error');
  m2.phase = GoonMatchPhase.Live;
  let threw = false;
  try { for (const fn of phase2) fn(GoonMatchPhase.Live); } catch (_e) { threw = true; }
  ok(!threw && made2[0].plays === 0 && p2.failed === true, 'a track that will not load stays silent and never throws');
  p2.dispose();

  // No song, no element.
  const made3 = [];
  const p3 = createSongPlayer({ match: Object.assign({}, fakeMatch, { song: null }), logger: quiet, makeAudio: () => { made3.push(1); return fakeAudio(); } });
  ok(made3.length === 0, 'no song -> no audio element at all');
  p3.dispose();
}

// ============================================================ 6. copy voice
{
  const all = JSON.stringify(Object.entries(S.song).map(([k, v]) => (typeof v === 'function' ? v('t', '1:00') : v)));
  ok(!all.includes(String.fromCharCode(0x2014)), 'no em-dashes in the song copy');
  ok(!/!/.test(all), 'no exclamation marks in the song copy');
}

// ============================================================ review fixes: skip, counter-proposals, floods
{
  const { a, b } = await pairOf(true);
  a.setSong({ url: CDN, title: 'one', durSec: 247 });
  await tick(60);
  ok(a.consentSheet.live_duration_sec === 247, 'the pick sets the length');
  a.setSong(null, { fallbackSec: 900 });
  ok(a.consentSheet.live_duration_sec === 900, 'skip re-proposes the remembered length', String(a.consentSheet.live_duration_sec));
  a.setSong({ url: CDN, title: 'two', durSec: 247 });
  a.setSong(null);
  ok(a.consentSheet.live_duration_sec === 720, 'skip with no remembered length goes back to the default');
  a.setSong({ url: CDN, title: 'three', durSec: 300 });
  await tick(1000);
  ok(b.song && b.song.title === 'three', 'the guest ends on the latest pick');

  // A counter-proposal (an older guest that never heard of songs) moves the length: the song goes.
  const sheet = Object.assign({}, a.consentSheet, { live_duration_sec: 600, confirmed: false });
  let dropped = 0;
  a.onSongChanged((s) => { if (s === null) dropped++; });
  a._handleConsent(sheet);
  ok(a.song === null && dropped === 1, 'a length moved off the song drops the host song');
  ok(a.consentSheet.live_duration_sec === 600, 'and the counter-proposed length stands');
  a.dispose?.(); b.dispose?.();
}
{
  const { a, b } = await pairOf(true);
  let changes = 0;
  b.onSongChanged(() => { changes++; });
  const frame = () => parse(JSON.stringify({ t: 'song', sub: 'set', url: CDN, title: 'same', dur_sec: 300 }), { logger: quiet });
  b._lastSongFrameMs = -Infinity;
  b._handleSong(frame());
  ok(changes === 1, 'the first song frame lands');
  b._lastSongFrameMs = -Infinity;
  b._handleSong(frame());
  ok(changes === 1, 'an identical song frame is ignored');
  for (let i = 0; i < 20; i++) b._handleSong(parse(JSON.stringify({ t: 'song', sub: 'set', url: CDN, title: 'flood ' + i, dur_sec: 300 }), { logger: quiet }));
  ok(changes === 1, 'a flood inside a second changes nothing yet');
  await tick(1000);
  ok(changes === 2 && b.song.title === 'flood 19', 'then only the latest lands', changes + ' ' + (b.song && b.song.title));
  a.dispose?.(); b.dispose?.();
}

console.log(`selftest-song: ${n - failures}/${n} passed`);
if (failures) process.exit(1);
