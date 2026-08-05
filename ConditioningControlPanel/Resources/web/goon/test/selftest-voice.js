// Self-contained sanity pass over VOICE NOTES — the ten seconds of somebody's
// real voice that can cross a duel.
//
//   node Resources/web/goon/test/selftest-voice.js
//
// It exists because this is the first feature on the page that carries a PERSON
// rather than a picture, and almost all of it is refusals. Four properties are
// worth more than the rest of the file put together, and every one of them is
// pinned below against the code that would quietly undo it:
//
//   1. NOTHING CROSSES WITHOUT BOTH CONSENTS. `voice_notes` is a per-side
//      declaration on the consent frame, cloned from `media_transfer` — which
//      means it must round-trip, parse absent-as-false, survive the peer's echo,
//      and stay OUT of the sheet fingerprint (a peer that drops the member would
//      otherwise wedge the lobby forever; see the wedge test in selftest-core).
//   2. WITH THE LOCAL OPT-IN OFF, AN INBOUND NOTE IS DROPPED UNREAD. Not hidden,
//      not muted — never accumulated, never base64-decoded, never handed to an
//      AudioContext. That is the promise the acknowledgment modal makes.
//   3. THE CEILINGS HOLD AGAINST A LYING PEER. The declared size and the
//      accumulated size are two different checks on purpose, and the second one
//      is the one that catches a meta claiming 40 KB in front of 4 MB.
//   4. A FRAME ALWAYS FITS THE LANE. Voice rides the CONTROL channel (the relay
//      has no bulk one), which clamps at 16 KB including the JSON envelope, so
//      the worst-case chunk frame is serialized here and measured.
//
// Everything runs under plain node: no DOM, no mic, no AudioContext. The one
// place a graph is needed (the playback queue) gets a fake one.

import { serialize, parse, wireByteLength, MAX_WIRE_BYTES } from '../core/wire.js';
import {
  makeConsent, makeVoice, makeHello, makeCaps,
  GoonMatchPhase, VOICE_SUBS, VOICE_CAP_VERSION, clampVoiceSub, peerSpeaksVoice,
} from '../core/contracts.js';
import { local as localCaps } from '../core/caps.js';
import { GoonMatchService } from '../core/match.js';
import { EMOTE_ICON_MAX_CHARS } from '../exec/sanitize.js';
import {
  createVoiceService, planVoiceChunks, voiceChunkAt, b64CharsFor,
  bytesToVoiceB64, voiceB64ToBytes, voiceSourceToBytes,
  VN_MAX_MS, VN_MAX_BYTES, VN_B64_CHUNK_CHARS, VN_SEND_MIN_GAP_MS, VN_RECV_MIN_GAP_MS,
  VN_QUEUE_MAX, VN_MAX_NOTES, VN_MAX_PARTS,
} from '../ui/voice/voiceService.js';
import { PREF_DEFAULTS, createPrefs } from '../ui/prefs.js';
import { S } from '../ui/strings.js';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const quiet = { info() {}, warn() {}, error() {}, debug() {}, log() {} };

/** n bytes of not-quite-random noise. Deterministic, so a failure is reproducible. */
function bytes(count, seed = 7) {
  const u8 = new Uint8Array(count);
  let x = seed >>> 0;
  for (let i = 0; i < count; i++) { x = (x * 1664525 + 1013904223) >>> 0; u8[i] = x & 0xff; }
  return u8;
}

/**
 * A match stub with exactly the surface ui/voice/voiceService.js consumes, plus
 * a `frames` log. Deliberately NOT a GoonMatchService: the service's own gates
 * have to be provable with the engine's answers pinned by hand, and the real
 * engine is exercised separately in section 2.
 */
function fakeMatch({ agreed = true, phaseOpen = true } = {}) {
  const frames = [];
  const voiceSubs = new Set();
  const m = {
    frames,
    get voiceNotesAgreed() { return agreed; },
    get voicePhaseOpen() { return phaseOpen; },
    setAgreed(v) { agreed = v; },
    setPhaseOpen(v) { phaseOpen = v; },
    onVoiceFrame(fn) { voiceSubs.add(fn); return () => voiceSubs.delete(fn); },
    onConsentChanged() { return () => {}; },
    onPhaseChanged() { return () => {}; },
    /** Push a frame at the service the way core/match.js would (parsed + clamped). */
    deliver(fields) {
      const f = parse(serialize(makeVoice(fields)));
      for (const fn of Array.from(voiceSubs)) fn(f);
      return f;
    },
    sendVoiceMeta(o) { if (!agreed || !phaseOpen) return false; frames.push({ sub: 'meta', ...o }); return true; },
    sendVoiceChunk(id, seq, data) { if (!agreed || !phaseOpen) return false; frames.push({ sub: 'chunk', id, seq, data }); return true; },
    sendVoiceEnd(id) { if (!agreed || !phaseOpen) return false; frames.push({ sub: 'end', id }); return true; },
  };
  return m;
}

/** An audio stub with the two verbs the service uses. Records what it was asked to play. */
function fakeAudio({ outcome = 'played' } = {}) {
  const played = [];
  return {
    played,
    stopped: 0,
    playVoiceNote(src, o = {}) {
      played.push(src);
      if (outcome === 'played') { try { o.onStart?.(); } catch (_e) { /* ignore */ } }
      return Promise.resolve(outcome);
    },
    stopVoice() { this.stopped++; return true; },
  };
}

/** A prefs stub. The real store is exercised in section 6. */
function fakePrefs(initial = {}) {
  const v = Object.assign({ voiceNotesEnabled: true }, initial);
  const subs = new Set();
  return {
    get(k) { return v[k]; },
    set(k, val) { v[k] = val; for (const fn of Array.from(subs)) fn(k, val, v); return true; },
    subscribe(fn) { subs.add(fn); return () => subs.delete(fn); },
  };
}

const tick = () => new Promise((r) => setTimeout(r, 0));

// ============================================================ 1. the wire family
{
  ok(JSON.stringify(VOICE_SUBS) === '["meta","chunk","end"]',
    'the family has exactly three subs, in order', JSON.stringify(VOICE_SUBS));

  // --- the three pinned shapes. Written out literally, because these bytes are a
  // cross-client contract and a rename here is a silent no-op at the far end.
  const meta = serialize(makeVoice({ sub: 'meta', id: 3, bytes: 40960, durMs: 4200, parts: 6, emote: '🔥' }));
  ok(meta === '{"t":"voice","v":1,"sub":"meta","id":3,"seq":0,"bytes":40960,"parts":6,"durMs":4200,"emote":"🔥"}',
    'a meta frame serializes exactly as pinned', meta);
  const chunk = serialize(makeVoice({ sub: 'chunk', id: 3, seq: 2, data: 'AAAA' }));
  ok(chunk === '{"t":"voice","v":1,"sub":"chunk","id":3,"seq":2,"bytes":0,"parts":0,"durMs":0,"data":"AAAA"}',
    'a chunk frame serializes exactly as pinned', chunk);
  const end = serialize(makeVoice({ sub: 'end', id: 3 }));
  ok(end === '{"t":"voice","v":1,"sub":"end","id":3,"seq":0,"bytes":0,"parts":0,"durMs":0}',
    'an end frame serializes exactly as pinned', end);

  // `emote` and `data` default to null so stripNulls keeps the small frames small.
  ok(!meta.includes('"data"'), 'a meta carries no empty data member', meta);
  ok(!end.includes('"emote"') && !end.includes('"data"'), 'and an end frame carries neither', end);

  // --- round trip + the untrusted-shape clamps -------------------------------------------
  const back = parse(chunk);
  ok(back.t === 'voice' && back.sub === 'chunk' && back.id === 3 && back.seq === 2 && back.data === 'AAAA',
    'a chunk round-trips through parse');
  ok(parse(meta).emote === '🔥', 'and the emote comes back with it');
  ok(parse('{"t":"voice","v":1,"sub":"chunk","id":"5","seq":"1","data":"AA"}').id === 5,
    'a quoted number is read forgivingly');
  ok(parse('{"t":"voice","v":1,"sub":"chunk","id":-4,"bytes":-9}').id === 0,
    'and a negative one lands as zero, never as a negative index');
  ok(parse('{"t":"voice","v":1,"sub":"chunk","id":true,"seq":[1]}').id === 0,
    'a boolean id is a broken frame, not a 1');
  ok(parse('{"t":"voice","v":1,"sub":"whisper","id":1}').sub === '',
    'an unknown sub collapses to "" so it can never reach a handler', clampVoiceSub('whisper'));
  ok(parse('{"t":"voice","v":1}').sub === '', 'a frame with no sub at all reads the same way');
  // The forward-compatibility floor: an OLD peer drops the whole family, silently.
  ok(parse('{"t":"vioce","v":1}', { logger: quiet }) === null, 'a typo t is unroutable, as ever');

  // --- THE 16 KB LANE. Worst case: the largest ids the clamp allows, a full chunk,
  // and every optional member present. This is the check that says "voice fits on a relay".
  const worstData = 'A'.repeat(VN_B64_CHUNK_CHARS);
  const worst = serialize(makeVoice({
    sub: 'chunk', id: Number.MAX_SAFE_INTEGER, seq: Number.MAX_SAFE_INTEGER,
    bytes: VN_MAX_BYTES, parts: VN_MAX_PARTS, durMs: VN_MAX_MS, data: worstData,
  }));
  const worstBytes = wireByteLength(worst);
  ok(worstBytes < MAX_WIRE_BYTES,
    'the worst-case chunk frame fits the control lane with room to spare',
    `${worstBytes} of ${MAX_WIRE_BYTES}`);
  ok(MAX_WIRE_BYTES - worstBytes > 4096,
    'and the headroom is real slack, not a coincidence a new field would eat',
    String(MAX_WIRE_BYTES - worstBytes));
  ok(VN_B64_CHUNK_CHARS % 4 === 0,
    'the chunk size is a multiple of 4, so every frame is independently decodable base64',
    String(VN_B64_CHUNK_CHARS));

  // A meta with an over-long emote is sanitized by the engine, not by the wire — pinned in §2.
  ok(makeVoice({ sub: 'meta', emote: null }).emote === null, 'a live note carries a null emote');
}

// ================================================= 2. the engine's send/receive gates
{
  const mk = (tag) => new GoonMatchService({
    send() { return Promise.resolve(true); },
    onMessageReceived() { return () => {}; },
    onStateChanged() { return () => {}; },
  }, true, { logger: quiet, tag });

  // --- the SEND door -----------------------------------------------------------------
  {
    const m = mk('GG:vsend');
    const sent = [];
    m._send = (msg) => { sent.push(msg); };

    ok(m.sendVoiceMeta({ id: 1, bytes: 10, durMs: 100, parts: 1 }) === false,
      'Idle sends nothing — there is no duel to speak into');

    m._phase = GoonMatchPhase.Live;
    ok(m.sendVoiceMeta({ id: 1, bytes: 10, durMs: 100, parts: 1 }) === false,
      'and neither does a live match with no agreement');

    m._localVoiceNotes = true;
    m._remoteVoiceNotes = true;
    ok(m.sendVoiceMeta({ id: 1, bytes: 10, durMs: 100, parts: 1 }) === false,
      'two opt-ins are still not enough — their BUILD has to speak the family');

    m._peerSupportsVoice = true;
    ok(m.sendVoiceMeta({ id: 1, bytes: 10, durMs: 100, parts: 1 }) === true, 'all three, and it goes');
    ok(sent.length === 1 && sent[0].t === 'voice' && sent[0].sub === 'meta', 'as a voice meta frame');

    ok(m.sendVoiceChunk(1, 0, 'AAAA') === true && m.sendVoiceEnd(1) === true, 'chunk and end follow');
    ok(m.sendVoiceChunk(1, 0, '') === false && m.sendVoiceChunk(1, 0, null) === false,
      'an empty chunk is refused rather than put on the wire as a frame with nothing in it');
    ok(m.sendVoiceFrame('whisper', { id: 1 }) === false,
      'and a sub nobody knows is refused at the door, not serialized');

    // The phase gate, at every edge.
    const phases = [
      [GoonMatchPhase.Countdown, true], [GoonMatchPhase.Live, true], [GoonMatchPhase.SuddenDeath, true],
      [GoonMatchPhase.Lobby, false], [GoonMatchPhase.Consent, false], [GoonMatchPhase.Draft, false],
      [GoonMatchPhase.Recap, false], [GoonMatchPhase.Idle, false],
    ];
    let phaseOk = true;
    for (const [phase, want] of phases) {
      m._phase = phase;
      if (m.sendVoiceEnd(9) !== want) phaseOk = false;
      if (m.voicePhaseOpen !== want) phaseOk = false;
    }
    ok(phaseOk, 'Countdown/Live/SuddenDeath only — a lobby mic and a recap mic are both refused');

    m._phase = GoonMatchPhase.Live;
    // The one human string on the family, capped on the way OUT as well as in.
    m.sendVoiceMeta({ id: 2, bytes: 1, durMs: 1, parts: 1, emote: 'x'.repeat(400) });
    const long = sent[sent.length - 1];
    ok(long.emote !== null && long.emote.length <= EMOTE_ICON_MAX_CHARS,
      'an over-long emote is capped before it leaves, at the emote family\'s own icon limit',
      String(long.emote && long.emote.length));
    m.sendVoiceMeta({ id: 3, bytes: 1, durMs: 1, parts: 1, emote: '🔥' });
    ok(sent[sent.length - 1].emote === '🔥', 'while a real emoji survives intact');

    m._ended = true;
    ok(m.sendVoiceEnd(1) === false, 'an ended match is silent even mid-phase');
    m.dispose();
  }

  // --- the RECEIVE door ----------------------------------------------------------------
  {
    const m = mk('GG:vrecv');
    const got = [];
    m.onVoiceFrame((f) => got.push(f));

    m._handleVoice(parse(serialize(makeVoice({ sub: 'meta', id: 1, bytes: 9, parts: 1 }))));
    ok(got.length === 0, 'a frame outside the open phases is dropped without being delivered');

    m._phase = GoonMatchPhase.Live;
    m._handleVoice(parse(serialize(makeVoice({ sub: 'meta', id: 1, bytes: 9, parts: 1 }))));
    ok(got.length === 1 && got[0].sub === 'meta', 'inside them it reaches the subscriber');

    // NOT consent-gated here on purpose: the unread drop belongs to ONE owner, the
    // service, which is the tier that can promise "never decoded" (see §4).
    ok(m.localVoiceNotes === false && m.remoteVoiceNotes === false,
      'and it arrived with no consent at all — core delivers, the service refuses');

    m._handleVoice(parse('{"t":"voice","v":1,"sub":"whisper","id":2}'));
    ok(got.length === 1, "a newer peer's unknown sub is ignored, not delivered");

    m._handleVoice(Object.assign(parse(serialize(makeVoice({ sub: 'meta', id: 3, parts: 1, bytes: 1 }))),
      { emote: 'y'.repeat(400) }));
    ok(got[got.length - 1].emote.length <= EMOTE_ICON_MAX_CHARS, 'an inbound emote is capped too',
      String(got[got.length - 1].emote.length));

    m._phase = GoonMatchPhase.Recap;
    const before = got.length;
    m._handleVoice(parse(serialize(makeVoice({ sub: 'chunk', id: 3, seq: 0, data: 'AAAA' }))));
    ok(got.length === before, 'and a post-match frame is ignored — nobody is owed a note after mercy');
    m.dispose();
  }

  // --- the consent field, from the service's point of view -------------------------------
  {
    ok(makeConsent().voice_notes === false, 'consent.voice_notes defaults to false');
    ok(parse(serialize(makeConsent({ voice_notes: true }))).voice_notes === true, 'and round-trips');
    ok(parse('{"t":"consent","v":1,"live_duration_sec":720,"toy_cap":0.7,'
      + '"payload_min_gap_ms":30000,"confirmed":false}').voice_notes === false,
    'a sheet from a peer that never heard of it parses to false');

    // THE FINGERPRINT EXCLUSION, proved by behaviour rather than by reading the source:
    // a peer's echo that DIFFERS only in this field must still be "the same sheet", i.e.
    // it must confirm rather than counter-propose.
    const m = mk('GG:vsheet');
    m._phase = GoonMatchPhase.Lobby;
    m._handleHello(makeHello({ caps: localCaps({ voice: VOICE_CAP_VERSION }) }));
    m.proposeConsent(600, 0.5, 30000);
    m.setLocalVoiceNotes(true);
    m.confirmConsent();
    m._handleConsent(parse('{"t":"consent","v":1,"live_duration_sec":600,"toy_cap":0.5,'
      + '"payload_min_gap_ms":30000,"confirmed":true,"voice_notes":false}'));
    ok(m.phase === GoonMatchPhase.Draft,
      'a sheet differing ONLY in voice_notes still counts as the same terms — no wedge', String(m.phase));
    ok(m.localVoiceNotes === true, 'and cloneSheet kept OUR value, not theirs');
    ok(m.remoteVoiceNotes === false && m.voiceNotesAgreed === false,
      'their opt-out is recorded as theirs and nothing is agreed');
    m.dispose();
  }

  // --- caps.voice is the version discriminator, and it gates the send ---------------------
  ok(peerSpeaksVoice(makeCaps({ voice: VOICE_CAP_VERSION })) === true, 'our own caps satisfy the check');
  ok(peerSpeaksVoice(makeCaps()) === false, 'a default caps object does not');
}

// ==================================================== 3. the chunk plan (pure arithmetic)
{
  ok(b64CharsFor(0) === 0 && b64CharsFor(1) === 4 && b64CharsFor(3) === 4 && b64CharsFor(4) === 8,
    'base64 is 4 characters per 3 bytes, padded up');

  ok(planVoiceChunks(0).ok === false && planVoiceChunks(0).reason === 'empty', 'an empty note is refused');
  ok(planVoiceChunks(VN_MAX_BYTES + 1).reason === 'too-big', 'and one over the cap is refused BEFORE any frame goes out');
  ok(planVoiceChunks(VN_MAX_BYTES).ok === true, 'exactly at the cap is fine — the ceiling is inclusive');

  const one = planVoiceChunks(1);
  ok(one.ok && one.parts === 1 && one.chars === 4, 'one byte is one chunk', JSON.stringify(one));

  // The exact boundary: 7680 raw bytes is 10240 base64 chars, which is ONE full chunk.
  const exact = planVoiceChunks(7680);
  ok(exact.parts === 1 && exact.chars === VN_B64_CHUNK_CHARS,
    '7680 bytes is precisely one full chunk', JSON.stringify(exact));
  ok(planVoiceChunks(7683).parts === 2, 'and three bytes more needs a second one');

  const big = planVoiceChunks(VN_MAX_BYTES);
  ok(big.parts === VN_MAX_PARTS,
    'a maximum note is exactly VN_MAX_PARTS chunks — the derived cap cannot disagree with itself',
    `${big.parts} vs ${VN_MAX_PARTS}`);
  ok(big.parts <= 40, 'which is a few dozen frames, not a few thousand', String(big.parts));

  // Slicing and rejoining is the whole codec.
  const src = bytes(20000);
  const b64 = bytesToVoiceB64(src);
  const plan = planVoiceChunks(src.length);
  let joined = '';
  for (let i = 0; i < plan.parts; i++) {
    const part = voiceChunkAt(b64, i);
    ok(part.length <= VN_B64_CHUNK_CHARS, 'no chunk exceeds the per-frame cap', String(part.length));
    joined += part;
  }
  ok(joined === b64, 'the chunks concatenate back to exactly the base64 that was sliced');
  const round = voiceB64ToBytes(joined);
  ok(round.length === src.length, 'which decodes to the original length', `${round.length} vs ${src.length}`);
  let same = true;
  for (let i = 0; i < src.length; i++) if (src[i] !== round[i]) same = false;
  ok(same, 'byte for byte');
  ok(voiceChunkAt(b64, plan.parts) === '', 'a chunk past the end is empty, not undefined');

  // Base64 has TWO implementations here (Buffer under node, btoa in the page) and both ship.
  const B = globalThis.Buffer;
  try {
    delete globalThis.Buffer;
    const viaBtoa = bytesToVoiceB64(src);
    ok(viaBtoa === b64, 'the btoa path agrees with the Buffer path, byte for byte');
    const backViaAtob = voiceB64ToBytes(viaBtoa);
    ok(backViaAtob.length === src.length && backViaAtob[123] === src[123], 'and so does atob on the way back');
  } finally { globalThis.Buffer = B; }

  ok(voiceB64ToBytes('!!!not base64!!!').length >= 0, 'garbage base64 answers bytes, never a throw');
  ok(voiceB64ToBytes('').length === 0 && voiceB64ToBytes(null).length === 0, 'and empty is empty');
}

// ============================================ 4. the receiver: assembly and every refusal
{
  const feed = (svc, match, id, data, o = {}) => {
    const parts = Math.max(1, data.length);
    match.deliver({ sub: 'meta', id, bytes: o.bytes ?? 1, parts: o.parts ?? parts, durMs: o.durMs ?? 1000, emote: o.emote ?? null });
    for (let i = 0; i < data.length; i++) match.deliver({ sub: 'chunk', id, seq: i, data: data[i] });
    match.deliver({ sub: 'end', id });
  };

  // --- the happy path: three frames in, one note out ----------------------------------
  {
    const match = fakeMatch();
    const audio = fakeAudio();
    const heard = [];
    const svc = createVoiceService({ match, audio, prefs: fakePrefs(), logger: quiet, now: () => 1e6 });
    svc.onIncoming((e) => heard.push(e));

    const src = bytes(9000);
    const b64 = bytesToVoiceB64(src);
    const plan = planVoiceChunks(src.length);
    match.deliver({ sub: 'meta', id: 1, bytes: src.length, parts: plan.parts, durMs: 3300, emote: '🔥' });
    for (let i = 0; i < plan.parts; i++) match.deliver({ sub: 'chunk', id: 1, seq: i, data: voiceChunkAt(b64, i) });
    match.deliver({ sub: 'end', id: 1 });
    await tick();
    await tick();

    ok(audio.played.length === 1, 'an assembled note reaches the voice bus exactly once');
    ok(audio.played[0] && audio.played[0].length === src.length,
      'with every byte that was sent', String(audio.played[0] && audio.played[0].length));
    ok(heard.length === 1 && heard[0].emote === '🔥' && heard[0].durMs === 3300,
      'and onIncoming fires when it STARTS, carrying the emote and the length', JSON.stringify(heard[0]));
    ok(svc.stats().received === 1, 'the counter agrees');
    svc.dispose();
  }

  // --- THE UNREAD DROP: the local opt-in is off ----------------------------------------
  {
    const match = fakeMatch();
    const audio = fakeAudio();
    const prefs = fakePrefs({ voiceNotesEnabled: false });
    const heard = [];
    const svc = createVoiceService({ match, audio, prefs, logger: quiet, now: () => 1e6 });
    svc.onIncoming((e) => heard.push(e));

    feed(svc, match, 1, ['AAAA', 'BBBB']);
    await tick();
    await tick();
    ok(audio.played.length === 0, 'with the opt-in off, nothing is played');
    ok(heard.length === 0, 'nothing is announced');
    ok(svc.stats().inbound === 0,
      'and NOTHING WAS EVEN ACCUMULATED — the chunks were dropped unread, not buffered and discarded');
    ok(svc.available() === false, 'the feature reports itself off, so no mic can appear either');

    // ...and it comes back the moment the player says so, with no restart.
    prefs.set('voiceNotesEnabled', true);
    ok(svc.available() === true, 'flipping the pref makes it live again');
    feed(svc, match, 2, ['AAAA']);
    await tick();
    await tick();
    ok(audio.played.length === 1, 'and the next note plays');
    svc.dispose();
  }

  // --- a mid-flight opt-out wins ---------------------------------------------------------
  {
    const match = fakeMatch();
    const audio = fakeAudio();
    const prefs = fakePrefs();
    const svc = createVoiceService({ match, audio, prefs, logger: quiet, now: () => 1e6 });
    match.deliver({ sub: 'meta', id: 1, bytes: 3, parts: 1, durMs: 900 });
    match.deliver({ sub: 'chunk', id: 1, seq: 0, data: 'AAAA' });
    prefs.set('voiceNotesEnabled', false);          // they change their mind mid-transfer
    match.deliver({ sub: 'end', id: 1 });
    await tick();
    await tick();
    ok(audio.played.length === 0, 'a note that was in flight when the player opted out is not played');
    svc.dispose();
  }

  // --- OVERSIZE, both ways ---------------------------------------------------------------
  {
    const match = fakeMatch();
    const audio = fakeAudio();
    const svc = createVoiceService({ match, audio, prefs: fakePrefs(), logger: quiet, now: () => 1e6 });

    // 1. a meta that DECLARES more than the cap is refused at the door.
    match.deliver({ sub: 'meta', id: 1, bytes: VN_MAX_BYTES + 1, parts: 1, durMs: 500 });
    ok(svc.stats().inbound === 0, 'a meta declaring more than the cap is refused before a chunk arrives');
    match.deliver({ sub: 'chunk', id: 1, seq: 0, data: 'AAAA' });
    match.deliver({ sub: 'end', id: 1 });
    await tick();
    ok(audio.played.length === 0, 'and its frames go nowhere');

    // 2. ...and a meta that LIES: small declaration, huge frames. This is the check that
    // matters, because the first one only ever catches an honest sender.
    match.deliver({ sub: 'meta', id: 2, bytes: 12, parts: 3, durMs: 500 });
    ok(svc.stats().inbound === 2, 'the small claim was accepted');
    match.deliver({ sub: 'chunk', id: 2, seq: 0, data: 'A'.repeat(VN_B64_CHUNK_CHARS) });
    ok(svc.stats().inbound === 0,
      'and the FIRST oversize chunk aborts it — the accumulated size is measured, never assumed');
    match.deliver({ sub: 'chunk', id: 2, seq: 1, data: 'A'.repeat(VN_B64_CHUNK_CHARS) });
    match.deliver({ sub: 'end', id: 2 });
    await tick();
    ok(audio.played.length === 0, 'nothing is played from an aborted transfer');

    // 3. an absurd part count is refused too (the frame budget, not the byte budget).
    match.deliver({ sub: 'meta', id: 3, bytes: 100, parts: VN_MAX_PARTS + 1, durMs: 500 });
    ok(svc.stats().inbound === 0, 'a meta claiming more parts than a maximum note could need is refused');
    svc.dispose();
  }

  // --- a new meta ABORTS an unfinished one ------------------------------------------------
  {
    const match = fakeMatch();
    const audio = fakeAudio();
    let t = 1e6;
    const svc = createVoiceService({ match, audio, prefs: fakePrefs(), logger: quiet, now: () => t });

    const a = bytes(600);
    const b = bytes(900, 99);
    match.deliver({ sub: 'meta', id: 1, bytes: a.length, parts: 1, durMs: 700 });
    match.deliver({ sub: 'chunk', id: 1, seq: 0, data: bytesToVoiceB64(a) });
    ok(svc.stats().inbound === 1, 'the first transfer is in flight');

    t += VN_RECV_MIN_GAP_MS;      // far enough apart that the RATE limit is not what refuses it
    match.deliver({ sub: 'meta', id: 2, bytes: b.length, parts: 1, durMs: 800 });
    ok(svc.stats().inbound === 2, 'a new meta takes over');
    match.deliver({ sub: 'chunk', id: 2, seq: 0, data: bytesToVoiceB64(b) });
    match.deliver({ sub: 'end', id: 2 });
    await tick();
    await tick();
    ok(audio.played.length === 1 && audio.played[0].length === b.length,
      'and the note that plays is the SECOND one — the abandoned frames were never mixed in',
      String(audio.played[0] && audio.played[0].length));

    // The abandoned transfer's own end frame is now stale and must change nothing.
    match.deliver({ sub: 'end', id: 1 });
    await tick();
    ok(audio.played.length === 1, "the first transfer's late end frame is ignored");
    svc.dispose();
  }

  // --- out-of-order / short / stray frames -------------------------------------------------
  {
    const match = fakeMatch();
    const audio = fakeAudio();
    let t = 1e6;
    const svc = createVoiceService({ match, audio, prefs: fakePrefs(), logger: quiet, now: () => t });

    // A gap in an ORDERED lane means loss, and half a voice is noise.
    match.deliver({ sub: 'meta', id: 1, bytes: 30, parts: 3, durMs: 500 });
    match.deliver({ sub: 'chunk', id: 1, seq: 0, data: 'AAAA' });
    match.deliver({ sub: 'chunk', id: 1, seq: 2, data: 'BBBB' });
    ok(svc.stats().inbound === 0, 'a missing chunk aborts the transfer rather than assembling a hole');

    // An end that arrives before all the declared parts do.
    t += VN_RECV_MIN_GAP_MS;
    match.deliver({ sub: 'meta', id: 2, bytes: 30, parts: 3, durMs: 500 });
    match.deliver({ sub: 'chunk', id: 2, seq: 0, data: 'AAAA' });
    match.deliver({ sub: 'end', id: 2 });
    await tick();
    ok(audio.played.length === 0, 'a truncated transfer is dropped, never played short');

    // Frames for a transfer that was never announced.
    match.deliver({ sub: 'chunk', id: 77, seq: 0, data: 'AAAA' });
    match.deliver({ sub: 'end', id: 77 });
    await tick();
    ok(audio.played.length === 0, 'and chunks with no meta in front of them go nowhere');
    svc.dispose();
  }

  // --- the receiver's own rate floor --------------------------------------------------------
  {
    const match = fakeMatch();
    const audio = fakeAudio();
    let t = 1e6;
    const svc = createVoiceService({ match, audio, prefs: fakePrefs(), logger: quiet, now: () => t });
    const one = bytesToVoiceB64(bytes(300));

    match.deliver({ sub: 'meta', id: 1, bytes: 300, parts: 1, durMs: 500 });
    match.deliver({ sub: 'chunk', id: 1, seq: 0, data: one });
    match.deliver({ sub: 'end', id: 1 });
    await tick();
    await tick();
    ok(audio.played.length === 1, 'the first note plays');

    t += VN_RECV_MIN_GAP_MS - 1;
    match.deliver({ sub: 'meta', id: 2, bytes: 300, parts: 1, durMs: 500 });
    match.deliver({ sub: 'chunk', id: 2, seq: 0, data: one });
    match.deliver({ sub: 'end', id: 2 });
    await tick();
    await tick();
    ok(audio.played.length === 1, 'a second one a millisecond too soon is refused at the meta');

    t += 2;
    match.deliver({ sub: 'meta', id: 3, bytes: 300, parts: 1, durMs: 500 });
    match.deliver({ sub: 'chunk', id: 3, seq: 0, data: one });
    match.deliver({ sub: 'end', id: 3 });
    await tick();
    await tick();
    ok(audio.played.length === 2, 'and one a millisecond late is fine');
    ok(VN_RECV_MIN_GAP_MS < VN_SEND_MIN_GAP_MS,
      'the receiver floor is BELOW our own send gap, so honest jitter never trips it',
      `${VN_RECV_MIN_GAP_MS} vs ${VN_SEND_MIN_GAP_MS}`);
    svc.dispose();
  }

  // --- the blocklist consult ------------------------------------------------------------
  {
    const match = fakeMatch();
    const audio = fakeAudio();
    const src = bytes(400);
    // Answer "blocked" for everything, so the consult is provably wired; the real
    // one answers from a local map and fails open (net/blocklist.js).
    const blocklist = { isBlocked: () => true, check: () => 0 };
    const svc = createVoiceService({ match, audio, prefs: fakePrefs(), blocklist, logger: quiet, now: () => 1e6 });
    match.deliver({ sub: 'meta', id: 1, bytes: src.length, parts: 1, durMs: 500 });
    match.deliver({ sub: 'chunk', id: 1, seq: 0, data: bytesToVoiceB64(src) });
    match.deliver({ sub: 'end', id: 1 });
    await tick();
    await tick();
    const hasSubtle = !!(globalThis.crypto && globalThis.crypto.subtle);
    ok(hasSubtle ? audio.played.length === 0 : true,
      'a blocklisted note is dropped before it reaches the bus (where the host can hash)');

    // ...and the same note with no blocklist at all still plays: the gate FAILS OPEN.
    const match2 = fakeMatch();
    const audio2 = fakeAudio();
    const svc2 = createVoiceService({ match: match2, audio: audio2, prefs: fakePrefs(), logger: quiet, now: () => 1e6 });
    match2.deliver({ sub: 'meta', id: 1, bytes: src.length, parts: 1, durMs: 500 });
    match2.deliver({ sub: 'chunk', id: 1, seq: 0, data: bytesToVoiceB64(src) });
    match2.deliver({ sub: 'end', id: 1 });
    await tick();
    await tick();
    ok(audio2.played.length === 1, 'and with no blocklist wired at all it plays — the consult fails OPEN');
    svc.dispose();
    svc2.dispose();
  }
}

// ================================================================= 5. the sender
{
  // --- the happy path, end to end through the chunker --------------------------------
  {
    const match = fakeMatch();
    let t = 1e6;
    const svc = createVoiceService({ match, audio: fakeAudio(), prefs: fakePrefs(), logger: quiet, now: () => t });
    const src = bytes(12000);

    const r = await svc.sendBlob(src, { emote: '💦', durMs: 4000 });
    ok(r.ok && r.reason === 'sent', 'a note goes out', JSON.stringify(r));
    const metas = match.frames.filter((f) => f.sub === 'meta');
    const chunks = match.frames.filter((f) => f.sub === 'chunk');
    const ends = match.frames.filter((f) => f.sub === 'end');
    ok(metas.length === 1 && ends.length === 1, 'one meta and one end, whatever the size');
    ok(chunks.length === r.parts && chunks.length === planVoiceChunks(src.length).parts,
      'and exactly as many chunks as the plan said', `${chunks.length} vs ${r.parts}`);
    ok(metas[0].bytes === src.length && metas[0].parts === chunks.length && metas[0].emote === '💦',
      'the meta declares the truth about what follows', JSON.stringify(metas[0]));
    ok(metas[0].durMs === 4000, 'and carries the recorded length for their chip');
    ok(chunks.every((c, i) => c.seq === i), 'chunks are numbered from zero, in order');
    ok(chunks.every((c) => c.data.length <= VN_B64_CHUNK_CHARS), 'and none exceeds the frame cap');
    ok(voiceB64ToBytes(chunks.map((c) => c.data).join('')).length === src.length,
      'the frames reassemble to the original blob');

    // The order is meta, chunks, end — the receiver relies on it and an ordered lane grants it.
    ok(match.frames[0].sub === 'meta' && match.frames[match.frames.length - 1].sub === 'end',
      'in that order, always');

    // --- the 4 s floor -----------------------------------------------------------------
    const tooSoon = await svc.sendBlob(bytes(100));
    ok(!tooSoon.ok && tooSoon.reason === 'too-soon', 'a second note inside the gap is refused', tooSoon.reason);
    t += VN_SEND_MIN_GAP_MS;
    const later = await svc.sendBlob(bytes(100));
    ok(later.ok, 'and allowed once the gap has passed');
    ok(later.id === 2, 'the transfer id is monotonic and sender-local', String(later.id));
    svc.dispose();
  }

  // --- every refusal -------------------------------------------------------------------
  {
    const match = fakeMatch();
    let t = 1e6;
    const svc = createVoiceService({ match, audio: fakeAudio(), prefs: fakePrefs(), logger: quiet, now: () => t });

    ok((await svc.sendBlob(new Uint8Array(0))).reason === 'empty', 'an empty recording is refused');
    ok((await svc.sendBlob(bytes(VN_MAX_BYTES + 1))).reason === 'too-big',
      'and one over the cap is refused BEFORE a single frame goes out');
    ok(match.frames.length === 0, 'literally nothing was sent for either of them');
    ok((await svc.sendBlob(null)).reason === 'unreadable', 'nothing at all is unreadable, not a crash');
    ok((await svc.sendBlob({ nope: true })).reason === 'unreadable', 'and so is a shape we cannot read');

    match.setAgreed(false);
    ok((await svc.sendBlob(bytes(100))).reason === 'unavailable', 'no agreement, no note');
    match.setAgreed(true);
    match.setPhaseOpen(false);
    ok((await svc.sendBlob(bytes(100))).reason === 'unavailable', 'no open phase, no note');
    match.setPhaseOpen(true);

    const prefs = fakePrefs({ voiceNotesEnabled: false });
    const off = createVoiceService({ match, audio: fakeAudio(), prefs, logger: quiet, now: () => t });
    ok((await off.sendBlob(bytes(100))).reason === 'unavailable',
      'and the LOCAL opt-in gates sending as well as receiving — one switch, both directions');
    off.dispose();
    svc.dispose();
  }

  // --- sendNote leans on the store, and degrades without one ---------------------------
  {
    const match = fakeMatch();
    const svc = createVoiceService({ match, audio: fakeAudio(), prefs: fakePrefs(), logger: quiet, now: () => 1e6 });
    ok((await svc.sendNote('n1')).reason === 'unavailable',
      'with no note store (wave 2) sendNote answers unavailable rather than throwing');
    svc.dispose();

    const store = { get: async (id) => (id === 'n1' ? { blob: bytes(500), durMs: 2500, emote: '👀' } : null) };
    const svc2 = createVoiceService({ match, audio: fakeAudio(), prefs: fakePrefs(), noteStore: store, logger: quiet, now: () => 1e6 });
    const r = await svc2.sendNote('n1');
    ok(r.ok, 'with a store it loads the note and sends it', JSON.stringify(r));
    const meta = match.frames.filter((f) => f.sub === 'meta').pop();
    ok(meta.emote === '👀' && meta.durMs === 2500, "and carries the note's own emote and length", JSON.stringify(meta));
    ok((await svc2.sendNote('nope')).reason === 'empty', 'an id the store does not know is refused');
    svc2.dispose();
  }

  // --- the availability edge, which is what the mic HUD subscribes to -------------------
  {
    const match = fakeMatch({ agreed: false });
    const prefs = fakePrefs();
    const svc = createVoiceService({ match, audio: fakeAudio(), prefs, logger: quiet, now: () => 1e6 });
    const edges = [];
    svc.onStateChanged((v) => edges.push(v));
    ok(svc.available() === false, 'no agreement, not available');

    match.setAgreed(true);
    prefs.set('voiceNotesEnabled', false);        // fires the subscription
    ok(edges.length === 0, 'an edge that does not change availability raises nothing');
    prefs.set('voiceNotesEnabled', true);
    ok(edges.length === 1 && edges[0] === true, 'and the real edge fires exactly once', JSON.stringify(edges));
    svc.dispose();
  }

  // --- dispose really lets go -----------------------------------------------------------
  {
    const match = fakeMatch();
    const audio = fakeAudio();
    const svc = createVoiceService({ match, audio, prefs: fakePrefs(), logger: quiet, now: () => 1e6 });
    svc.dispose();
    ok(audio.stopped === 1, 'disposing cuts whatever is on the bus — no voice over the recap');
    ok(svc.available() === false, 'a disposed service is never available');
    ok((await svc.sendBlob(bytes(100))).reason === 'unavailable', 'and sends nothing');
    match.deliver({ sub: 'meta', id: 1, bytes: 10, parts: 1, durMs: 100 });
    match.deliver({ sub: 'chunk', id: 1, seq: 0, data: 'AAAA' });
    match.deliver({ sub: 'end', id: 1 });
    await tick();
    ok(audio.played.length === 0, 'and hears nothing');
  }
}

// ==================================================== 6. prefs, options and the strings
{
  ok(PREF_DEFAULTS.voiceNotesEnabled === false,
    'the opt-in is OFF by default — this one is never opt-out', String(PREF_DEFAULTS.voiceNotesEnabled));
  ok(PREF_DEFAULTS.voiceAckSeen === false,
    'and the acknowledgment has not been seen, so the toggle starts locked');
  ok(PREF_DEFAULTS.voiceVolume === 0.9, 'the slider defaults to 0.9', String(PREF_DEFAULTS.voiceVolume));
  ok(typeof PREF_DEFAULTS.voiceEmoteMap === 'object' && PREF_DEFAULTS.voiceEmoteMap !== null
    && !Array.isArray(PREF_DEFAULTS.voiceEmoteMap), 'and the emote map is a plain object');
  ok(Object.keys(PREF_DEFAULTS.voiceEmoteMap).length === 0, 'with nothing in it');

  // THE OBJECT PREF. Everything else on this store is a scalar, and the sanitizer's
  // fall-through is `String(value)` — which would turn this map into "[object Object]"
  // on the first save and hand every reader a truthy nine-character string.
  {
    const p = createPrefs({});
    ok(p.set('voiceEmoteMap', { '🔥': 'n1', '👀': 'n2' }) === true, 'the map can be set');
    const readBack = p.get('voiceEmoteMap');
    ok(typeof readBack === 'object' && readBack['🔥'] === 'n1' && readBack['👀'] === 'n2',
      'and comes back as an object with its entries', JSON.stringify(readBack));
    ok(JSON.parse(JSON.stringify(p.all())).voiceEmoteMap['🔥'] === 'n1',
      'it survives the JSON round trip the storage sinks put it through');
    ok(p.set('voiceEmoteMap', { '🔥': 'n1', '👀': 'n2' }) === false,
      'setting the same content again is not a change — objects compare by VALUE, not identity');

    // A caller must not be able to reach into the store through a getter.
    const escaped = p.get('voiceEmoteMap');
    escaped['💦'] = 'n3';
    ok(p.get('voiceEmoteMap')['💦'] === undefined, 'the value handed out is a copy, not the store itself');

    // A corrupt store cannot poison it either.
    ok(p.set('voiceEmoteMap', 'not an object') === true
      && Object.keys(p.get('voiceEmoteMap')).length === 0,
    'garbage lands as an empty map, never as a string');
    p.set('voiceEmoteMap', { a: 'x', bad: { nested: 1 }, worse: [1, 2] });
    const cleaned = p.get('voiceEmoteMap');
    ok(cleaned.a === 'x' && cleaned.bad === undefined && cleaned.worse === undefined,
      'and only JSON-safe scalar members survive', JSON.stringify(cleaned));

    // ...and the shared default is never handed out by reference.
    p.reset();
    p.get('voiceEmoteMap')['leak'] = 'yes';
    ok(Object.keys(PREF_DEFAULTS.voiceEmoteMap).length === 0,
      'a reset does not hand out the frozen DEFAULT object for someone to edit');
    ok(createPrefs({}).get('voiceEmoteMap').leak === undefined, 'and a fresh store is still empty');
  }

  // Volume prefs are clamped like the other six.
  {
    const p = createPrefs({ voiceVolume: 9 });
    ok(p.get('voiceVolume') === 1, 'voiceVolume is clamped to 0..1 like every other *Volume key');
    p.set('voiceVolume', -3);
    ok(p.get('voiceVolume') === 0, 'in both directions');
  }

  // --- the copy deck. Wave 2 is READ-ONLY on strings.js, so everything it needs
  // has to exist NOW — this is the list, and a missing key fails here rather than
  // rendering as `undefined` on somebody's screen mid-duel.
  {
    ok(typeof S.options.voice === 'string' && S.options.voice.length > 0,
      'the drawer has a label for the slider', String(S.options.voice));
    const V = S.voice;
    ok(!!V, 'S.voice exists');
    const flat = [
      'menu', 'menuNote', 'eyebrow', 'lead', 'back',
      'toggle', 'toggleOn', 'toggleOff', 'toggleLocked',
      'record', 'recording', 'recordStop', 'recordCapped',
      'play', 'stop', 'delete', 'empty', 'deleteConfirm',
      'linkLabel', 'linkNone', 'linkHelp',
      'hudLabel', 'holdHint', 'slideToCancel', 'sending', 'sent', 'cancelled', 'sendFailed',
      'incoming', 'incomingWithEmote',
      'lobbyBoth', 'lobbyYours', 'lobbyTheirs', 'lobbyPeerOld',
      'micDenied', 'micMissing', 'notActive', 'volumeHint',
    ];
    let missing = '';
    for (const k of flat) if (typeof V[k] !== 'string' || V[k] === '') missing += k + ' ';
    ok(missing === '', 'every flat string wave 2 needs is present', missing);

    const fns = ['noteName', 'noteLength', 'recordTimer', 'recordCountdown', 'full', 'linkedTo', 'linkMoved', 'tooSoon'];
    let notFn = '';
    for (const k of fns) if (typeof V[k] !== 'function') notFn += k + ' ';
    ok(notFn === '', 'and every interpolator is a FUNCTION, so this module stays import-safe', notFn);
    ok(V.noteName(3) === 'Note 3', 'the auto-name is "Note N"', V.noteName(3));
    ok(V.full(VN_MAX_NOTES).includes(String(VN_MAX_NOTES)),
      'the "library is full" line carries the real ceiling', V.full(VN_MAX_NOTES));
    ok(V.recordTimer(4200, VN_MAX_MS).includes('4.2'), 'the record timer reads in seconds', V.recordTimer(4200, VN_MAX_MS));
    /* TWO READINGS OF ONE CLOCK, and they have to be tellable apart at a glance:
     * the library screen and the first seven seconds of a hold say how much has
     * been said; the last three say how much is LEFT (ui/voice/micHud.js
     * MIC_COUNTDOWN_MS). A countdown that still looked like a fraction would be
     * a warning nobody notices. */
    ok(V.recordCountdown(2).includes('2') && !V.recordCountdown(2).includes('/'),
      'the countdown is a bare remaining number, not a fraction to subtract', V.recordCountdown(2));
    ok(V.recordCountdown(2) !== V.recordTimer(8000, VN_MAX_MS),
      'and never renders as the elapsed line for the same instant',
      V.recordCountdown(2) + ' | ' + V.recordTimer(8000, VN_MAX_MS));
    ok(V.recordCountdown(-1) === V.recordCountdown(0),
      'a clock that overshot the cap still says something sane', V.recordCountdown(-1));

    // THE ACK GATE. Two paragraphs, and the second one is the one that is easy to
    // leave out: this switch is also a consent to HEAR them.
    ok(V.ack && typeof V.ack.headline === 'string' && typeof V.ack.line === 'string'
      && typeof V.ack.lineTwo === 'string' && typeof V.ack.go === 'string' && typeof V.ack.cancel === 'string',
    'the acknowledgment modal has a headline, two lines and both buttons');
    ok(/record/i.test(V.ack.headline + ' ' + V.ack.line) && /voice/i.test(V.ack.headline + ' ' + V.ack.line),
      'the headline and first line say, in words, that this records their real voice',
      V.ack.headline + ' | ' + V.ack.line);
    ok(/hear/i.test(V.ack.lineTwo),
      'and the SECOND says you may hear THEM — the half a player can otherwise miss', V.ack.lineTwo);
    ok(/server/i.test(V.ack.line) || /server/i.test(V.voice === undefined ? '' : V.lead),
      'and somewhere on the way in it says no server is involved');
    ok(/hear|hears/i.test(V.toggleOn) && /drop/i.test(V.toggleOff),
      'the toggle hints name both directions on and the unread drop off',
      V.toggleOn + ' / ' + V.toggleOff);
    ok(!/error|fail/i.test(V.cancelled) && !/error/i.test(V.micDenied),
      'cancelling and refusing the mic are ordinary answers, not errors',
      V.cancelled + ' / ' + V.micDenied);
  }
}

// ============================================ 7. ui/audio.js: the bus and the queue
{
  const audioMod = await import('../ui/audio.js');
  const { createAudio, VOICE_MAX_PLAY_MS, VOICE_QUEUE_MAX, BUS_GLIDE_SEC } = audioMod;

  ok(VOICE_MAX_PLAY_MS === 10_500,
    'playback is hard-stopped at 10.5s — 500ms over the record cap, and NOT derived from the blob',
    String(VOICE_MAX_PLAY_MS));
  ok(VOICE_MAX_PLAY_MS > VN_MAX_MS, 'which is above the record cap, so an honest note is never clipped');
  ok(VOICE_QUEUE_MAX === 2 && VN_QUEUE_MAX === 2, 'one playing, one waiting, and the rest dropped');

  // Under node there is no AudioContext at all: every call has to answer, not throw.
  {
    const a = createAudio({ prefs: null, logger: quiet });
    ok(a.volumes.voice === 0.9, 'the mixer has a voice bus seeded from the default', String(a.volumes.voice));
    const r = await a.playVoiceNote(bytes(64));
    ok(r === 'unavailable', 'and answers "unavailable" under node rather than throwing', String(r));
    ok(a.stopVoice() === true && a.voiceInFlight === 0, 'stopVoice is safe with nothing playing');
    a.dispose();
  }

  // ...and with a fake graph, the real queue behaviour.
  {
    const started = [];
    const stopped = [];
    const g = () => ({
      gain: {
        value: 1,
        cancelScheduledValues() {}, setValueAtTime() {}, setTargetAtTime() {}, linearRampToValueAtTime() {},
      },
      connect() {},
    });
    let live = 0;
    class FakeCtx {
      constructor() { this.state = 'running'; this.currentTime = 0; this.destination = {}; }
      createGain() { return g(); }
      createBufferSource() {
        const src = {
          buffer: null, onended: null, connect() {},
          start() { live++; started.push(1); },
          stop() { stopped.push(1); },
        };
        return src;
      }
      decodeAudioData(ab) {
        // A blob shorter than 8 bytes stands in for "not audio we can read".
        return (ab && ab.byteLength >= 8) ? Promise.resolve({ duration: 3 }) : Promise.reject(new Error('bad'));
      }
      resume() { return Promise.resolve(); }
      addEventListener() {} removeEventListener() {} close() {}
    }
    const priorWindow = globalThis.window;
    globalThis.window = { AudioContext: FakeCtx, addEventListener() {}, removeEventListener() {} };
    try {
      const prefs = createPrefs({ voiceVolume: 0.5, masterVolume: 1 });
      const a = createAudio({ prefs, logger: quiet });
      ok(a.volumes.voice === 0.5, 'the bus reads the pref', String(a.volumes.voice));

      const first = a.playVoiceNote(bytes(64));
      const second = a.playVoiceNote(bytes(64));
      const third = await a.playVoiceNote(bytes(64));
      ok(third === 'dropped-full',
        'the third note is DROPPED rather than queued — six seconds late is not a message any more',
        String(third));
      ok(await first === 'played', 'the first plays');
      ok(a.voiceInFlight >= 1, 'and the lane reports what it is holding', String(a.voiceInFlight));

      a.stopVoice();
      ok(await second === 'stopped', 'stopVoice resolves the one that was waiting rather than stranding it');
      ok(stopped.length >= 1, 'and really stops the source that was playing');
      ok(a.voiceInFlight === 0, 'leaving the lane empty');

      const bad = await a.playVoiceNote(bytes(2));
      ok(bad === 'decode-failed', 'a blob that is not audio answers decode-failed, never a throw', String(bad));

      // A note that ENDS on its own frees the lane for the next one.
      const p = a.playVoiceNote(bytes(64));
      ok(await p === 'played', 'the lane recovers after a failure');
      a.dispose();
      ok(started.length >= 2, 'the fake graph really ran the sources', String(started.length));
      void live;
    } finally {
      if (priorWindow === undefined) delete globalThis.window; else globalThis.window = priorWindow;
    }
  }

  // The hard stop is a TIMER on the constant, not a trust in the container's header.
  // (10.5 real seconds is not a thing a self-test may wait for, so the wiring is pinned
  // at the source — the behaviour it guards is the one thing here that cannot be faked.)
  {
    const fs = await import('node:fs');
    const url = await import('node:url');
    const src = fs.readFileSync(url.fileURLToPath(new URL('../ui/audio.js', import.meta.url)), 'utf8');
    ok(/setTimeout\(\(\) => \{ if \(voiceNow === rec\) voiceFinish\('capped'\); \}, VOICE_MAX_PLAY_MS\)/.test(src),
      'ui/audio.js arms the hard stop from VOICE_MAX_PLAY_MS on every note it starts');
    ok(/voiceBus\.connect\(masterBus\)/.test(src),
      'and the voice bus hangs under masterBus, so the master still takes the room quiet');
    ok(/glide\(voiceBus\.gain, clamp01\(vol\.voice\), BUS_GLIDE_SEC\)/.test(src),
      'the slider glides onto a note that is already playing', String(BUS_GLIDE_SEC));
  }
}

// ================================================= 8. boot wiring, at its call site
{
  const fs = await import('node:fs');
  const url = await import('node:url');
  const read = (p) => fs.readFileSync(url.fileURLToPath(new URL(p, import.meta.url)), 'utf8').replace(/\r\n/g, '\n');
  const boot = read('../boot.js');

  ok(/createVoiceService\(\{/.test(boot), 'boot.js builds the voice service');
  ok(/function attachMatch\([\s\S]{0,4000}?voice = createVoiceService/.test(boot),
    'inside attachMatch — MATCH-SCOPED, so a relay rebuild gets a service bound to the new match');
  ok(/function detachMatch\([\s\S]{0,2000}?voice\?\.dispose\?\.\(\)/.test(boot),
    'and disposes it in detachMatch, which is what stops a note talking over the recap');
  ok(/mountHud\(\{[\s\S]{0,600}?discord, voice,/.test(boot),
    'the service is threaded into mountHud deps for wave 2');
  ok(/getVoice: \(\) => voice/.test(boot), 'and into the screen ctx as a THUNK, never a snapshot');
  ok(/localCapsOf\(\{ elements, payloads, rounds, platform: 'web', voice: voiceCap, transfer: true \}\)/.test(boot),
    'boot advertises caps.voice AND caps.transfer — the only way a fire-and-forget sender ever learns the peer can hear it');
  ok(/const voiceCap = VOICE_CAP_VERSION;/.test(boot), 'from the pinned revision constant');

  // exec/ must not have learned anything about this.
  const execFiles = fs.readdirSync(url.fileURLToPath(new URL('../exec/', import.meta.url)));
  let execTouched = '';
  for (const f of execFiles) {
    if (!f.endsWith('.js')) continue;
    const s = read('../exec/' + f);
    if (/voiceNote|voiceService|sendVoiceMeta|voice_notes/.test(s)) execTouched += f + ' ';
  }
  ok(execTouched === '', 'nothing in exec/ knows this feature exists — the fence held', execTouched);
}

// =========================================== 9. END TO END, over two real engines
//
// Everything above tests one seam at a time against a stub. This runs a note all
// the way through the REAL pieces — service, engine, serializer, parser, engine,
// service — with nothing faked but the socket and the speaker. If the chunker and
// the assembler ever disagree about a boundary, this is where it shows.
{
  /** A transport pair that puts every frame through serialize + parse, as the wire does. */
  function wirePair() {
    let host = null;
    let guest = null;
    const mk = (getPeer) => {
      const msgSubs = new Set();
      return {
        state: 3,
        send(msg) {
          const json = serialize(msg);
          const back = parse(json, { logger: quiet });
          // A frame the wire refuses (oversize) is DROPPED, exactly as the real
          // transport drops it — no exception, no retry, no note.
          if (back) { const p = getPeer(); if (p) p._deliver(back); }
          return Promise.resolve(true);
        },
        onMessageReceived(fn) { msgSubs.add(fn); return () => msgSubs.delete(fn); },
        onStateChanged() { return () => {}; },
        _deliver(m) { for (const fn of Array.from(msgSubs)) fn(m); },
      };
    };
    const th = mk(() => tg);
    const tg = mk(() => th);
    host = new GoonMatchService(th, true, { logger: quiet, tag: 'GG:vh' });
    guest = new GoonMatchService(tg, false, { logger: quiet, tag: 'GG:vg' });
    return { host, guest, th, tg };
  }

  const { host, guest } = wirePair();
  // Straight to a live, fully-agreed duel: the handshake itself is selftest-core's job.
  for (const m of [host, guest]) {
    m._phase = GoonMatchPhase.Live;
    m._localVoiceNotes = true;
    m._remoteVoiceNotes = true;
    m._peerSupportsVoice = true;
  }

  const audioG = fakeAudio();
  const svcH = createVoiceService({ match: host, audio: fakeAudio(), prefs: fakePrefs(), logger: quiet, now: () => 1e6 });
  const svcG = createVoiceService({ match: guest, audio: audioG, prefs: fakePrefs(), logger: quiet, now: () => 1e6 });
  const heard = [];
  svcG.onIncoming((e) => heard.push(e));

  // Big enough to need several frames, and an odd size so the last chunk is a runt.
  const src = bytes(41_237, 31);
  const r = await svcH.sendBlob(src, { emote: '😏', durMs: 9800 });
  await tick();
  await tick();

  ok(r.ok && r.parts > 1, 'a multi-chunk note goes out over the real engine', JSON.stringify(r));
  ok(audioG.played.length === 1, 'and arrives on the other side as exactly one note');
  const got = audioG.played[0];
  ok(got && got.length === src.length, 'the same number of bytes', `${got && got.length} vs ${src.length}`);
  let identical = !!got && got.length === src.length;
  if (identical) for (let i = 0; i < src.length; i++) if (src[i] !== got[i]) { identical = false; break; }
  ok(identical, 'byte for byte, through serialize/parse and back');
  ok(heard.length === 1 && heard[0].emote === '😏' && heard[0].durMs === 9800,
    'with the emote and the duration intact', JSON.stringify(heard[0]));

  // ...and the same note refused once the duel is over.
  for (const m of [host, guest]) m._phase = GoonMatchPhase.Recap;
  const after = await svcH.sendBlob(bytes(400));
  ok(!after.ok && after.reason === 'unavailable', 'nothing crosses after the match ends', after.reason);
  ok(audioG.played.length === 1, 'and the far side heard nothing more');

  svcH.dispose();
  svcG.dispose();
  host.dispose();
  guest.dispose();
}

console.log(failures === 0 ? `PASS — ${n} checks` : `FAILED — ${failures}/${n} checks`);
process.exit(failures === 0 ? 0 : 1);
