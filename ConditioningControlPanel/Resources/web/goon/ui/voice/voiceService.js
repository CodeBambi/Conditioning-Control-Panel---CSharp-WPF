/* ============================================================================
 * ui/voice/voiceService.js — the one thing that knows how a voice note crosses.
 *
 * A duelist holds a mic button for up to ten seconds and the other one hears it.
 * That sentence is the entire feature, and almost all of this file is the work
 * of making it true ONLY when both people said it could be.
 *
 *   ┌────────────────────────────────────────────────────────────────────────┐
 *   │ SEND    blob -> base64 -> meta / chunk* / end on the CONTROL lane       │
 *   │ RECEIVE meta / chunk* / end -> assembled bytes -> audio.playVoiceNote() │
 *   └────────────────────────────────────────────────────────────────────────┘
 *
 * WHY THE CONTROL LANE AND NOT `goon-media`. The bulk channel does not exist on
 * the relay fallback (net/mediaQueue.js degrades to nothing there, by design) and
 * a voice note has to work on the worst link a duel can end up on. So it rides
 * the same ordered/reliable channel as every other message — which costs a 4/3
 * base64 inflation and a 16 KB per-frame ceiling, and both are paid for in the
 * chunk plan below. 256 KB of opus is about 35 frames; a tick is 400 bytes.
 *
 * IT IS THE `t:'emote'` FAMILY, NOT A PAYLOAD. No charges, no receipts, no ACKs,
 * no retries, nothing in the ledger, nothing in active_effects. A dropped frame
 * is a note that never plays, and that is the whole of the failure handling: a
 * feature where somebody's voice can be OWED to them — queued, retried, delivered
 * three minutes later out of context — is a different and much worse feature.
 *
 * THE FIVE GATES, and every one of them is enforced on BOTH ends because the
 * wire is untrusted and the other end is a stranger's build:
 *
 *   1. CONSENT. Both sides declared `voice_notes` on the consent frame AND the
 *      peer's build advertises `caps.voice` (core/match.js voiceNotesAgreed).
 *   2. PHASE. Countdown, Live, SuddenDeath. Nothing before, nothing after —
 *      core/match.js refuses to send or deliver outside them.
 *   3. THE LOCAL OPT-IN, ON THE RECEIVE PATH, SEPARATELY. With
 *      `prefs.voiceNotesEnabled` false an inbound note is dropped UNREAD: the
 *      chunks are never accumulated, the base64 is never decoded, the bytes never
 *      reach an AudioContext. That is the promise the ack modal makes and it is
 *      deliberately checked here rather than upstream, because "we hid the
 *      button" and "we never listened to it" are different guarantees.
 *   4. SIZE. VN_MAX_BYTES, checked against the DECLARED size at `meta` and again
 *      against the ACCUMULATED size on every chunk. A peer that lies in the meta
 *      is caught by the second one.
 *   5. RATE. 4 s between our own sends; 3 s before the receiver will accept
 *      another one; one transfer in flight per direction, and a new `meta`
 *      ABORTS an unfinished one rather than interleaving with it.
 *
 * NODE-IMPORT-SAFE: no DOM, no AudioContext, no fetch at import time. The base64
 * paths feature-detect Buffer before FileReader/atob precisely so the self-tests
 * can drive the whole chunk/assembly path under plain node.
 * ==========================================================================*/

import { VOICE_QUEUE_MAX } from '../audio.js';

/* ----------------------------------------------------------------------------
 * THE PINNED NUMBERS (docs/GOON_VOICE_PLAN.md). Exported because the recorder,
 * the mic HUD, the library screen and the self-tests must all read the SAME ones
 * — a second copy of "ten seconds" somewhere else is how a cap drifts.
 * -------------------------------------------------------------------------- */

/** Record cap. The recorder stops itself here; nothing longer is ever made. */
export const VN_MAX_MS = 10_000;
/** Total blob ceiling, declared AND accumulated. ~32 kbps opus x 10 s is ~40 KB,
 *  so this is roughly six times what an honest note weighs — generous enough for
 *  a Safari mp4 fallback and small enough that a hostile peer cannot use it. */
export const VN_MAX_BYTES = 262_144;
/**
 * BASE64 CHARACTERS per chunk frame — not bytes of audio.
 *
 * The relay clamps a frame at MAX_WIRE_BYTES (16 KB) INCLUDING the JSON envelope,
 * so the budget is 16384 minus `{"t":"voice","v":1,"sub":"chunk","id":…,"seq":…,
 * "bytes":0,"parts":0,"durMs":0,"data":"…"}` — about 120 bytes with generous
 * integers. 10240 leaves ~6 KB of headroom, which is deliberate slack rather than
 * waste: base64 is ASCII so chars are bytes, and a future field on this family
 * must not be the thing that starts silently dropping frames.
 *
 * A MULTIPLE OF 4, which is what makes each chunk independently decodable as well
 * as concatenable — base64 sliced off a 4-char boundary only decodes once it is
 * joined back up, and a debugger looking at one frame should see audio, not a
 * shifted-alphabet smear.
 */
export const VN_B64_CHUNK_CHARS = 10_240;
/** Our own floor between two sends. Four seconds is "you may say a thing, then
 *  another thing", not "you may hold the button down forever". */
export const VN_SEND_MIN_GAP_MS = 4_000;
/** ...and the receiver's own, lower one. Lower ON PURPOSE: it is a defence
 *  against a modified sender, not a duplicate of the rule above, and clamping it
 *  to the same 4 s would drop honest notes to ordinary jitter. */
export const VN_RECV_MIN_GAP_MS = 3_000;
/** Playback depth, owned by ui/audio.js and re-exported so nobody re-derives it. */
export const VN_QUEUE_MAX = VOICE_QUEUE_MAX;
/** How many pre-recorded notes the library holds (ui/voice/noteStore.js, wave 2). */
export const VN_MAX_NOTES = 8;
/** Ceiling on `parts` a meta may claim — derived, so it can never disagree with
 *  the two numbers it is made of. A peer claiming more is refused at the door. */
export const VN_MAX_PARTS = Math.ceil(Math.ceil(VN_MAX_BYTES / 3) * 4 / VN_B64_CHUNK_CHARS);

/* ----------------------------------------------------------------------------
 * PURE HELPERS — chunk arithmetic and base64, exported and testable without a
 * match, a mic, an AudioContext or a DOM.
 * -------------------------------------------------------------------------- */

/**
 * How many base64 characters a blob of `byteLength` bytes becomes. Base64 is
 * 4 chars per 3 bytes, padded up to the next multiple of 4.
 */
export function b64CharsFor(byteLength) {
  const n = Math.max(0, Math.floor(Number(byteLength) || 0));
  return Math.ceil(n / 3) * 4;
}

/**
 * The chunk plan for one note. PURE, and the ONLY place the ceilings are applied
 * on the send side: a note that cannot fit must fail HERE, before a single frame
 * goes out, rather than half way through a transfer the receiver then has to
 * time out of.
 *
 * @returns {{ok:boolean, reason:string, parts:number, chars:number}}
 *   `parts` is the number of chunk frames; `chars` the total base64 length.
 */
export function planVoiceChunks(byteLength, o = {}) {
  const per = Math.max(4, Math.floor(Number(o.chunkChars) || VN_B64_CHUNK_CHARS));
  const maxBytes = Math.max(1, Math.floor(Number(o.maxBytes) || VN_MAX_BYTES));
  const total = Math.max(0, Math.floor(Number(byteLength) || 0));
  if (total === 0) return { ok: false, reason: 'empty', parts: 0, chars: 0 };
  if (total > maxBytes) return { ok: false, reason: 'too-big', parts: 0, chars: 0 };
  const chars = b64CharsFor(total);
  return { ok: true, reason: '', parts: Math.ceil(chars / per), chars };
}

/** The base64 slice for one part index. Pure; the inverse is plain concatenation. */
export function voiceChunkAt(b64, seq, chunkChars = VN_B64_CHUNK_CHARS) {
  const per = Math.max(4, Math.floor(Number(chunkChars) || VN_B64_CHUNK_CHARS));
  const i = Math.max(0, Math.floor(Number(seq) || 0));
  return String(b64 || '').slice(i * per, (i + 1) * per);
}

/**
 * Bytes -> base64. Buffer under node (the self-tests), btoa in the page.
 * 32 KB at a time: String.fromCharCode.apply blows the argument limit on a big
 * buffer and the failure mode is a RangeError mid-send. (ui/assetsStore.js
 * bytesToB64 is the same helper for the same reason; it is duplicated rather
 * than imported because that module reaches the cache bridge at import time and
 * this one must stay free of every host.)
 */
export function bytesToVoiceB64(bytes) {
  const u8 = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes || 0);
  const B = globalThis.Buffer;
  if (B && typeof B.from === 'function') return B.from(u8).toString('base64');
  let s = '';
  const STEP = 0x8000;
  for (let i = 0; i < u8.length; i += STEP) {
    s += String.fromCharCode.apply(null, u8.subarray(i, Math.min(u8.length, i + STEP)));
  }
  try { return globalThis.btoa(s); } catch (_e) { return ''; }
}

/** base64 -> bytes. Returns an EMPTY array for anything unreadable, never throws. */
export function voiceB64ToBytes(b64) {
  const s = typeof b64 === 'string' ? b64 : '';
  if (s === '') return new Uint8Array(0);
  const B = globalThis.Buffer;
  if (B && typeof B.from === 'function') {
    try { const b = B.from(s, 'base64'); return new Uint8Array(b.buffer, b.byteOffset, b.byteLength); }
    catch (_e) { return new Uint8Array(0); }
  }
  try {
    const bin = globalThis.atob(s);
    const out = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i) & 0xff;
    return out;
  } catch (_e) { return new Uint8Array(0); }
}

/** Blob | ArrayBuffer | TypedArray -> Uint8Array. Resolves null rather than rejecting. */
export async function voiceSourceToBytes(src) {
  try {
    if (!src) return null;
    if (src instanceof Uint8Array) return src;
    if (src instanceof ArrayBuffer) return new Uint8Array(src);
    if (ArrayBuffer.isView(src)) return new Uint8Array(src.buffer, src.byteOffset, src.byteLength);
    // Blob/File. Node's Blob answers arrayBuffer() too, which is what lets the
    // self-tests drive the whole send path without a browser.
    if (typeof src.arrayBuffer === 'function') return new Uint8Array(await src.arrayBuffer());
    // Very old WebKit: no Blob.arrayBuffer, but FileReader is always there.
    if (typeof FileReader === 'function') {
      return await new Promise((resolve) => {
        let fr = null;
        try { fr = new FileReader(); } catch (_e) { resolve(null); return; }
        fr.onerror = () => resolve(null);
        fr.onabort = () => resolve(null);
        fr.onload = () => {
          try { resolve(new Uint8Array(fr.result)); } catch (_e) { resolve(null); }
        };
        try { fr.readAsArrayBuffer(src); } catch (_e) { resolve(null); }
      });
    }
    return null;
  } catch (_e) { return null; }
}

/** Lowercase hex sha-256, or '' where the host has no subtle crypto. Never throws. */
async function sha256Hex(bytes) {
  try {
    const subtle = globalThis.crypto && globalThis.crypto.subtle;
    if (!subtle || typeof subtle.digest !== 'function') return '';
    // A copy, because digest() may neuter a view backed by a shared buffer.
    const buf = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
    const d = await subtle.digest('SHA-256', buf);
    return Array.from(new Uint8Array(d)).map((b) => b.toString(16).padStart(2, '0')).join('');
  } catch (_e) { return ''; }
}

/** performance.now() where it exists; Date.now() otherwise. Monotonic-ish either way. */
function nowMs() {
  try {
    if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function') {
      return performance.now();
    }
  } catch (_e) { /* fall through */ }
  return Date.now();
}

/* ----------------------------------------------------------------------------
 * THE SERVICE
 * -------------------------------------------------------------------------- */

/**
 * @param {object}   o
 * @param {object}   o.match          core/match.js GoonMatchService (the wire + the consents)
 * @param {object}   o.audio          ui/audio.js handle (the `voice` bus)
 * @param {object}   o.prefs          ui/prefs.js handle (`voiceNotesEnabled` is the receive gate)
 * @param {object}   [o.noteStore]    ui/voice/noteStore.js (wave 2). null = live notes only
 * @param {object}   [o.blocklist]    net/blocklist.js — see the consult below
 * @param {object}   [o.logger]       console-shaped
 * @param {Function} [o.now]          TEST AFFORDANCE — the clock the rate limits read
 */
export function createVoiceService({
  match = null, audio = null, prefs = null, noteStore = null,
  blocklist = null, logger = null, now = nowMs,
} = {}) {
  const clock = typeof now === 'function' ? now : nowMs;
  const log = logger;
  const info = (m) => { try { log?.info?.('[GG voice] ' + m); } catch (_e) { /* ignore */ } };
  const warn = (m) => { try { log?.warn?.('[GG voice] ' + m); } catch (_e) { /* ignore */ } };

  let disposed = false;
  const unsubs = [];
  const incomingSubs = new Set();
  const stateSubs = new Set();

  /* ---- SEND state -------------------------------------------------------- */
  let outSeq = 0;             // the monotonic transfer id (sender-local, per the wire)
  let lastSentAt = -Infinity; // for VN_SEND_MIN_GAP_MS
  let sending = false;        // one transfer in flight OUT
  const stats = {
    sent: 0, sendDropped: 0,
    received: 0, recvDropped: 0, aborted: 0,
  };

  /* ---- RECEIVE state ------------------------------------------------------
   * ONE in-flight transfer, and `refusedId` is its shadow: when the local opt-in
   * is off we remember the id we refused so the chunks that follow can be thrown
   * away without ever being looked at, instead of each one being re-judged. */
  let inbound = null;         // {id, bytes, parts, durMs, emote, chars: [], got: 0, chars0: 0}
  let refusedId = -1;
  let lastAcceptedAt = -Infinity;

  /* ---- availability ------------------------------------------------------- */
  let lastAvailable = false;

  /** The local half of the gate: the opt-in the ack modal guards. */
  function localEnabled() {
    try { return !!(prefs && prefs.get('voiceNotesEnabled')); } catch (_e) { return false; }
  }

  /**
   * The ONE predicate. Both consents, the peer's build, the phase, and our own
   * opt-in — all five, ANDed, in one place, so the mic button and the send path
   * can never disagree about whether this feature is live.
   *
   * Note that a SOLO match satisfies none of it in practice (the practice
   * opponent never declares `voice_notes`), which is how the mic stays off
   * against a bot without anything here having to know what practice is.
   */
  function available() {
    if (disposed || !match) return false;
    if (!localEnabled()) return false;
    try { return !!match.voiceNotesAgreed && !!match.voicePhaseOpen; } catch (_e) { return false; }
  }

  /**
   * WHY is there no mic on the desk? One short phrase naming the FIRST failing
   * gate, in the SAME ORDER available() applies them — the net/mediaQueue.js
   * `whyNoTags()` pattern, and it exists for exactly the same reason that one
   * does.
   *
   * THE BUG THIS EXISTS FOR (owner, 2026-08-05): the mic was play-tested three
   * times across three setups — iPhone Safari, iPhone Safari incognito, and a
   * desktop pair — and never once appeared. Every one of those was the FIRST
   * gate below (their own opt-in had never been switched on, because until
   * today the only switch was on a title-menu screen nobody visits before a
   * duel), and the page said nothing at all about it in any of the three. Five
   * ANDed booleans with no readout is a feature that can only be debugged by
   * archaeology.
   *
   * `''` MEANS THE MIC IS LIVE. Everything else is a sentence to put in a warn.
   * `micSupported` is passed in rather than sniffed here because this module is
   * node-import-safe by charter and never touches `navigator`; a caller that
   * does not know simply omits it and that gate is not reported.
   *
   * @param {object} [o]
   * @param {boolean} [o.micSupported]  ui/voice/recorder.js `supported()`
   * @param {boolean} [o.hudHidden]     the desk's zen bit (ui/hud.js)
   * @returns {string} '' when live, else the first failing gate
   */
  function whyUnavailable(o = {}) {
    if (disposed) return 'the voice service is disposed';
    if (!match) return 'no match';
    if (!localEnabled()) return 'local pref off (voice notes are not switched on for this seat)';
    let peerBuild = false;
    let mine = false;
    let theirs = false;
    let phase = false;
    try {
      peerBuild = !!match.peerSupportsVoice;
      mine = !!match.localVoiceNotes;
      theirs = !!match.remoteVoiceNotes;
      phase = !!match.voicePhaseOpen;
    } catch (_e) { return 'the match refused to answer'; }
    // Our own declaration BEFORE theirs: with the pref on and this false, the
    // seed at attachMatch was refused (a phase window) and that is our bug, not
    // the peer's — a different sentence entirely from "they said no".
    if (!mine) return 'our declaration never rode a consent frame (the seed was refused)';
    if (!peerBuild) return 'the peer\'s build does not advertise caps.voice (stale page?)';
    if (!theirs) return 'the peer never declared voice_notes (their side is switched off)';
    if (!phase) return 'the phase is closed to voice (not Countdown/Live/SuddenDeath)';
    if (o.micSupported === false) return 'no getUserMedia / MediaRecorder on this host';
    if (o.hudHidden === true) return 'zen-hidden (the desk chrome is toggled off)';
    return '';
  }

  function emitState() {
    const next = available();
    if (next === lastAvailable) return;
    lastAvailable = next;
    for (const fn of Array.from(stateSubs)) {
      try { fn(next); } catch (e) { warn('onStateChanged handler threw: ' + ((e && e.message) || e)); }
    }
  }

  function emitIncoming(payload) {
    for (const fn of Array.from(incomingSubs)) {
      try { fn(payload); } catch (e) { warn('onIncoming handler threw: ' + ((e && e.message) || e)); }
    }
  }

  /* ------------------------------------------------------------------ SEND */

  /**
   * Chunk a recorded blob and put it on the wire.
   *
   * @param {Blob|ArrayBuffer|Uint8Array} blob
   * @param {object} [o]
   * @param {string} [o.emote]  the emote this note rides with, or null for a live one
   * @param {number} [o.durMs]  recorded length, for their chip. Cosmetic.
   * @returns {Promise<{ok:boolean, reason:string, id:number, parts:number}>}
   *   reason is 'sent' | 'unavailable' | 'too-soon' | 'busy' | 'empty' | 'too-big' |
   *   'unreadable' | 'aborted' | 'send-failed'.
   */
  async function sendBlob(blob, o = {}) {
    const fail = (reason) => { stats.sendDropped++; return { ok: false, reason, id: 0, parts: 0 }; };
    if (disposed) return fail('unavailable');
    if (!available()) return fail('unavailable');

    // ONE AT A TIME, OUT. Interleaving two transfers on one id space would need
    // the receiver to hold two assemblies, and the receiver deliberately holds
    // exactly one (see the header).
    if (sending) return fail('busy');

    const t = clock();
    const since = t - lastSentAt;
    if (since < VN_SEND_MIN_GAP_MS) return fail('too-soon');

    const bytes = await voiceSourceToBytes(blob);
    if (disposed) return fail('unavailable');
    if (!bytes) return fail('unreadable');
    if (bytes.length === 0) return fail('empty');

    const plan = planVoiceChunks(bytes.length);
    if (!plan.ok) return fail(plan.reason);

    // Re-checked AFTER the await: a match can end, or a phase turn, while a blob
    // is being read off disk, and the gate that mattered is the one at send time.
    if (!available()) return fail('unavailable');

    const b64 = bytesToVoiceB64(bytes);
    if (!b64) return fail('unreadable');

    const id = ++outSeq;
    const emote = o && o.emote ? String(o.emote) : null;
    const durMs = Math.max(0, Math.min(VN_MAX_MS, Math.floor(Number(o && o.durMs) || 0)));

    sending = true;
    // Stamped BEFORE the frames rather than after: the gap is between the moments
    // a player pressed send, and a slow transfer must not earn them a faster one.
    const prevSentAt = lastSentAt;
    lastSentAt = t;
    try {
      if (!match.sendVoiceMeta({ id, bytes: bytes.length, durMs, parts: plan.parts, emote })) {
        // NOTHING left the machine, so nothing is owed to the rate limit either —
        // making a player sit out four seconds for a send that never happened is
        // punishing them for our refusal.
        lastSentAt = prevSentAt;
        return fail('send-failed');
      }
      for (let seq = 0; seq < plan.parts; seq++) {
        // Yield between frames. 35 sends in one synchronous loop is 35 JSON
        // serializations on the UI thread in the middle of a live match, and this
        // is a fire-and-forget family — nothing is waiting on the last one.
        if (seq > 0) await new Promise((r) => { try { setTimeout(r, 0); } catch (_e) { r(); } });
        if (disposed || !available()) { stats.aborted++; return fail('aborted'); }
        if (!match.sendVoiceChunk(id, seq, voiceChunkAt(b64, seq))) return fail('send-failed');
      }
      if (!match.sendVoiceEnd(id)) return fail('send-failed');
      stats.sent++;
      info(`sent note ${id} (${bytes.length}B, ${plan.parts} part(s)${emote ? ', emote ' + emote : ''})`);
      return { ok: true, reason: 'sent', id, parts: plan.parts };
    } catch (e) {
      warn('send threw: ' + ((e && e.message) || e));
      return fail('send-failed');
    } finally {
      sending = false;
    }
  }

  /**
   * Send a PRE-RECORDED note by id. Thin on purpose: the store owns the blob and
   * its metadata, this owns the wire, and the emote comes off the note unless the
   * caller names one (the emote hook passes the emote it just fired).
   */
  async function sendNote(noteId, o = {}) {
    if (disposed) return { ok: false, reason: 'unavailable', id: 0, parts: 0 };
    if (!noteStore || typeof noteStore.get !== 'function') {
      return { ok: false, reason: 'unavailable', id: 0, parts: 0 };
    }
    let note = null;
    try { note = await noteStore.get(noteId); } catch (e) { warn('noteStore.get threw: ' + ((e && e.message) || e)); }
    if (!note || !note.blob) return { ok: false, reason: 'empty', id: 0, parts: 0 };
    return sendBlob(note.blob, {
      emote: (o && o.emote) || note.emote || null,
      durMs: note.durMs || 0,
    });
  }

  /* --------------------------------------------------------------- RECEIVE */

  function dropInbound(why) {
    if (inbound) {
      stats.recvDropped++;
      info(`inbound note ${inbound.id} dropped (${why})`);
    }
    inbound = null;
  }

  function onMeta(f) {
    // THE UNREAD DROP, and it is the first thing that happens. With the local
    // opt-in off nothing about this note is kept: no buffer is allocated, no
    // chunk is accumulated, no base64 is decoded, no AudioContext is touched.
    // The id is remembered ONLY so the chunks behind it can be discarded without
    // being reconsidered one at a time.
    if (!localEnabled()) {
      refusedId = f.id;
      inbound = null;
      stats.recvDropped++;
      return;
    }
    // A NEW META ABORTS AN UNFINISHED ONE. Ordered lane, one transfer per
    // direction: a meta arriving mid-transfer means the sender moved on (or is
    // misbehaving), and holding both would be the only way to end up playing a
    // note assembled out of two people's frames.
    if (inbound) { stats.aborted++; dropInbound('superseded by a new meta'); }
    refusedId = -1;

    if (f.id <= 0) { stats.recvDropped++; return; }
    if (f.bytes <= 0 || f.bytes > VN_MAX_BYTES) {
      stats.recvDropped++;
      warn(`refused note ${f.id}: declared ${f.bytes} bytes (cap ${VN_MAX_BYTES})`);
      return;
    }
    if (f.parts <= 0 || f.parts > VN_MAX_PARTS) {
      stats.recvDropped++;
      warn(`refused note ${f.id}: ${f.parts} part(s) (cap ${VN_MAX_PARTS})`);
      return;
    }
    // The receiver's own floor. Measured from the last note we ACCEPTED, not from
    // the last one that arrived, so a peer spraying refused metas cannot use this
    // as a way to keep pushing the window out in front of an honest note.
    const t = clock();
    if (t - lastAcceptedAt < VN_RECV_MIN_GAP_MS) {
      stats.recvDropped++;
      info(`refused note ${f.id}: ${Math.round(t - lastAcceptedAt)}ms since the last one`);
      return;
    }

    lastAcceptedAt = t;
    inbound = {
      id: f.id,
      bytes: f.bytes,
      parts: f.parts,
      durMs: f.durMs,
      emote: f.emote || null,
      chunks: [],
      chars: 0,
      next: 0,
      // The ceiling in the units the chunks actually arrive in, computed once.
      maxChars: b64CharsFor(f.bytes),
    };
  }

  function onChunk(f) {
    if (refusedId === f.id) return;               // dropped unread; never even measured
    if (!inbound || inbound.id !== f.id) { stats.recvDropped++; return; }
    if (typeof f.data !== 'string' || f.data === '') { dropInbound('empty chunk'); return; }
    // ORDERED LANE, so this is a CHECK and not a sort: a gap means frames were
    // lost or reordered, which on this channel means something is wrong enough
    // that assembling the rest would produce noise rather than a voice.
    if (f.seq !== inbound.next) { dropInbound(`chunk ${f.seq} out of order (wanted ${inbound.next})`); return; }
    if (inbound.next >= inbound.parts) { dropInbound('more chunks than declared'); return; }

    inbound.chars += f.data.length;
    // THE SECOND SIZE CHECK, against what has actually arrived. The meta's number
    // is a claim; this is the measurement, and it is what catches a peer that
    // declared 40 KB and is sending 4 MB.
    if (inbound.chars > inbound.maxChars || inbound.chars > b64CharsFor(VN_MAX_BYTES)) {
      stats.aborted++;
      dropInbound(`over the declared size (${inbound.chars} b64 chars > ${inbound.maxChars})`);
      return;
    }
    inbound.chunks.push(f.data);
    inbound.next++;
  }

  async function onEnd(f) {
    if (refusedId === f.id) { refusedId = -1; return; }
    if (!inbound || inbound.id !== f.id) { stats.recvDropped++; return; }
    const note = inbound;
    inbound = null;

    if (note.next !== note.parts) { stats.recvDropped++; info(`note ${note.id} ended short`); return; }

    const bytes = voiceB64ToBytes(note.chunks.join(''));
    if (!bytes.length) { stats.recvDropped++; warn(`note ${note.id} decoded to nothing`); return; }
    if (bytes.length > VN_MAX_BYTES) { stats.recvDropped++; warn(`note ${note.id} over the cap after decode`); return; }
    if (bytes.length !== note.bytes) {
      // Not fatal — the ceilings above already held — but it means their meta and
      // their frames disagree, which is worth a line when a note sounds wrong.
      info(`note ${note.id}: declared ${note.bytes}B, assembled ${bytes.length}B`);
    }

    // Re-checked after the assembly, because everything above is asynchronous in
    // practice: a player who switched the feature off while a note was arriving
    // has said no, and no is the answer that wins.
    if (!localEnabled() || disposed) { stats.recvDropped++; return; }

    /* THE BLOCKLIST CONSULT (contract §"Limits"). net/blocklist.js is a CONTENT
     * check, not a peer one: it answers "has a moderator banned these bytes",
     * keyed by sha-256, and it is the same gate the media lane applies at render
     * time. So this asks the same question about the same kind of thing — the
     * bytes about to be played — from the LOCAL map only, exactly as the offer
     * gate does, because waiting on a network round trip here would put latency
     * in front of a live voice AND hand a sender a timing oracle.
     *
     * FAILS OPEN in every direction: no blocklist, no subtle crypto (a plain
     * file:// page has none), an unknown hash — all of them play the note. Only a
     * hash the moderator list already carries is refused. */
    try {
      if (blocklist && typeof blocklist.isBlocked === 'function') {
        const sha = await sha256Hex(bytes);
        if (sha && blocklist.isBlocked(sha)) {
          stats.recvDropped++;
          warn(`note ${note.id} is blocklisted — dropped`);
          return;
        }
        // Not a wait: enqueue it so a repeat of the same note is answered from
        // the map next time. `check` is fire-and-forget by contract.
        if (sha && typeof blocklist.check === 'function') { try { blocklist.check([sha]); } catch (_e) { /* ignore */ } }
      }
    } catch (_e) { /* fail open — a moderation outage never kills the feature */ }

    if (!audio || typeof audio.playVoiceNote !== 'function') { stats.recvDropped++; return; }

    stats.received++;
    // The QUEUE lives in ui/audio.js (one playing, one waiting, the rest dropped)
    // rather than here, because it is a property of the BUS: a note that arrives
    // while another is playing has to be judged against what is audible, not
    // against what is assembled.
    const outcome = await audio.playVoiceNote(bytes, {
      onStart: () => emitIncoming({ emote: note.emote, durMs: note.durMs, bytes: bytes.length }),
    });
    if (outcome !== 'played') { stats.recvDropped++; info(`note ${note.id} not played (${outcome})`); }
  }

  /** One inbound frame, already phase-gated and shape-clamped by core/match.js. */
  function onFrame(f) {
    if (disposed || !f) return;
    try {
      if (f.sub === 'meta') onMeta(f);
      else if (f.sub === 'chunk') onChunk(f);
      else if (f.sub === 'end') void onEnd(f);
      // ...and no default. A sub this build does not know is a NEWER peer, and
      // ignoring it is the same forward-compatibility unknown `t` gets.
    } catch (e) {
      warn('inbound frame threw: ' + ((e && e.stack) || e));
      inbound = null;
    }
  }

  /* ------------------------------------------------------------ wiring up */

  if (match && typeof match.onVoiceFrame === 'function') {
    try { unsubs.push(match.onVoiceFrame(onFrame) || (() => {})); }
    catch (e) { warn('onVoiceFrame subscribe threw: ' + ((e && e.message) || e)); }
  }
  // The availability edge, from all three things that can move it. The mic HUD
  // subscribes to the RESULT rather than to these, so it never has to know that
  // "voice is live" is made of a consent frame, a phase and a checkbox.
  for (const [name, hook] of [['onConsentChanged', 'consent'], ['onPhaseChanged', 'phase']]) {
    if (match && typeof match[name] === 'function') {
      try { unsubs.push(match[name](() => emitState()) || (() => {})); }
      catch (e) { warn(`${hook} subscribe threw: ` + ((e && e.message) || e)); }
    }
  }
  if (prefs && typeof prefs.subscribe === 'function') {
    try {
      unsubs.push(prefs.subscribe((key) => { if (key === 'voiceNotesEnabled') emitState(); }) || (() => {}));
    } catch (e) { warn('prefs subscribe threw: ' + ((e && e.message) || e)); }
  }
  lastAvailable = available();

  return {
    available,
    /** '' when the mic is live, else the FIRST failing gate. See the block above. */
    whyUnavailable,
    sendBlob,
    sendNote,

    /** fn({emote, durMs, bytes}) when a note STARTS playing -> unsubscribe. */
    onIncoming(fn) {
      if (typeof fn !== 'function') return () => {};
      incomingSubs.add(fn);
      return () => incomingSubs.delete(fn);
    },

    /** fn(isAvailable) on the availability EDGE -> unsubscribe. */
    onStateChanged(fn) {
      if (typeof fn !== 'function') return () => {};
      stateSubs.add(fn);
      return () => stateSubs.delete(fn);
    },

    /** Diagnostics for the probes and the self-tests. Never load-bearing. */
    stats() {
      return Object.assign({}, stats, {
        sending,
        inbound: inbound ? inbound.id : 0,
        refused: refusedId > 0,
      });
    },

    dispose() {
      if (disposed) return;
      disposed = true;
      for (const off of unsubs) { try { off(); } catch (_e) { /* already gone */ } }
      unsubs.length = 0;
      incomingSubs.clear();
      stateSubs.clear();
      inbound = null;
      // Whatever is on the bus goes with the match: a note that arrived in the
      // last second of a duel must not still be talking over the recap.
      try { audio?.stopVoice?.(); } catch (_e) { /* ignore */ }
    },
  };
}

export default createVoiceService;
