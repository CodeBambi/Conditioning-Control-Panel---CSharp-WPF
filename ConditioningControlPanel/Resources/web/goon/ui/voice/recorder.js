/* ============================================================================
 * ui/voice/recorder.js — the microphone, held for at most ten seconds.
 *
 * One MediaRecorder, wrapped so that the two callers (the mic HUD in a match,
 * the library screen at the title menu) can treat recording as three promises:
 *
 *   start()   -> {ok, reason}                      'recording' | denied | missing | …
 *   stop()    -> {ok, reason, blob, durMs, mime}   the note, measured
 *   cancel()  -> {ok:false, reason:'cancelled'}    the note, thrown away
 *
 * FIVE RULES, and each one is a promise made to somebody about their own voice:
 *
 *   1. NO HOT MIC. The stream is acquired when a recording STARTS and every
 *      track is stopped when it ends — stopped, cancelled, capped, errored,
 *      disposed, or abandoned half way through getUserMedia. The OS mic
 *      indicator is the only honest UI this feature has, and it must go out the
 *      instant the button is released. `release()` is the single door and every
 *      path below leaves through it.
 *   2. A REFUSAL IS AN ANSWER, NOT AN EXCEPTION. getUserMedia rejects for a
 *      denied permission, a missing device, a policy block and a browser that
 *      has none of this — all four come back as `{ok:false, reason}` and NOTHING
 *      here ever throws at its caller. "no" is a perfectly ordinary thing for a
 *      player to say to a microphone (see S.voice.micDenied — it is phrased that
 *      way on purpose).
 *   3. TEN SECONDS IS THE LOT. A timer stops the recorder at VN_MAX_MS and
 *      hands the finished blob to `onCapped` subscribers, so the HUD can
 *      auto-send and the library can auto-finish without either of them owning
 *      a second copy of the cap.
 *   4. NOTHING LATCHES. No state but `idle` may be permanent, because `start()`
 *      answers everything that is not idle with 'busy' — so anything that can
 *      park this machine outside `idle` with nobody waiting on it is a mic that
 *      is dead for the rest of the session. That is not hypothetical: it is the
 *      bug this file was rewritten for on 2026-08-05 (one unanswered permission
 *      prompt, and every press afterwards was refused). Hence VN_START_TIMEOUT_MS
 *      on the acquire, VN_STOP_TIMEOUT_MS on the close, the attempt token that
 *      lets a live press supersede a disowned one, and `reset()` as the door the
 *      UI can always reach for.
 *   5. NOTHING IS TOUCHED AT IMPORT TIME. No navigator, no window, no
 *      MediaRecorder lookup until a call actually needs one — the self-tests
 *      import this module under plain node and drive the whole state machine
 *      through the two injectable seams (`getUserMedia`, `recorderFactory`).
 *
 * The state machine, in full:
 *
 *      idle ──start()──> starting ──(stream+recorder)──> recording
 *       ▲                   │                              │
 *       │                   │ cancel() during the await    │ stop() / cancel() / cap
 *       │                   │ getUserMedia timed out       │ recorder error
 *       └───────────────────┴──────────────────────────────┴──> stopping ──> idle
 *
 * ...and `reset()` is that diagram collapsed into one arrow: any state, to idle,
 * now. Every edge into `idle` goes through `release()`, which is also the only
 * place a pending stop() promise is answered — dropping one strands the caller.
 *
 * `stopping` exists because MediaRecorder.stop() is asynchronous: the last
 * `dataavailable` lands with (or just before) `stop`, and a blob assembled
 * before it arrives is a note with its ending cut off.
 * ==========================================================================*/

import { VN_MAX_MS } from './voiceService.js';

/**
 * Container preference, best first. Opus in WebM is what Chromium (and therefore
 * WebView2, which is where nearly every duel is played) produces natively; the
 * mp4 rungs are Safari, which has never supported the WebM muxer. The final ''
 * is "let the UA pick its own default", which is not a container we chose but is
 * a great deal better than refusing to record because we did not recognise the
 * browser.
 */
export const VN_MIME_CANDIDATES = Object.freeze([
  'audio/webm;codecs=opus',
  'audio/webm',
  'audio/mp4;codecs=mp4a.40.2',
  'audio/mp4',
  'audio/ogg;codecs=opus',
  '',
]);

/**
 * ~32 kbps. Ten seconds of that is ~40 KB, which is a sixth of VN_MAX_BYTES and
 * about three control-lane frames — the cap exists so a note cannot be a
 * transfer, and the bitrate is what keeps an ordinary one nowhere near it.
 * Speech at 32 kbps opus is telephone-plus; the feature is a voice, not a mix.
 */
export const VN_AUDIO_BPS = 32_000;

/**
 * How long stop() will wait for the recorder's own `stop` event before giving
 * up and releasing the microphone anyway. A host that never fires it (a wedged
 * media pipeline) must not be able to leave the mic open forever, which is the
 * one failure mode of this file that a player would be right to be angry about.
 */
export const VN_STOP_TIMEOUT_MS = 1_500;

/**
 * How long `starting` may last before the attempt is written off.
 *
 * THE STUCK MIC THIS EXISTS FOR. getUserMedia is a HUMAN waiting — a permission
 * sheet that somebody has to read — so the ceiling is generous. But it is not
 * infinite, because on some hosts (iOS Safari, notably) a prompt that is
 * DISMISSED rather than answered leaves that promise pending forever, and a
 * recorder parked in `starting` answers every later start() with 'busy'. One
 * unanswered prompt would then be the reason the mic never worked again for the
 * rest of the session. Thirty seconds is longer than anyone spends deciding and
 * far shorter than "never".
 */
export const VN_START_TIMEOUT_MS = 30_000;

/**
 * The MediaRecorder timeslice — how often it must hand us a chunk.
 *
 * WHY THIS IS NOT LEFT TO THE UA. `MediaRecorder.start()` with no argument is
 * allowed to hold the entire recording and deliver it as one `dataavailable`
 * alongside `stop`, and WebKit does exactly that — so a short note, or one
 * stopped before the encoder flushes, can deliver NO data event at all. That
 * arrives at the caller as `reason:'empty'` and reads to the player as "that one
 * did not record", on a recording they definitely made. Asking for a chunk every
 * quarter second means there is always something assembled by the time stop()
 * lands, whatever the platform chooses to do at the end.
 */
export const VN_TIMESLICE_MS = 250;

/** performance.now() where it exists; Date.now() otherwise. */
function nowMs() {
  try {
    if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function') {
      return performance.now();
    }
  } catch (_e) { /* fall through */ }
  return Date.now();
}

/**
 * Is this container recordable here? Feature-detected at CALL time, never at
 * import time, and tolerant of a MediaRecorder without isTypeSupported (old
 * WebKit) — where the answer is "try it and find out".
 */
function defaultIsTypeSupported(mime) {
  try {
    const MR = globalThis.MediaRecorder;
    if (!MR) return false;
    if (typeof MR.isTypeSupported !== 'function') return mime === '';
    return !!MR.isTypeSupported(mime);
  } catch (_e) { return false; }
}

/**
 * The first container this host will record. PURE given its probe, so the suite
 * can sweep the whole fallback chain (Chromium, Safari, a browser that supports
 * nothing) without a browser.
 *
 * @param {(mime:string)=>boolean} [isSupported]
 * @returns {string} a mime string, or '' meaning "the UA's own default"
 */
export function pickVoiceMime(isSupported) {
  const probe = typeof isSupported === 'function' ? isSupported : defaultIsTypeSupported;
  for (const mime of VN_MIME_CANDIDATES) {
    if (mime === '') return '';        // the last rung is unconditional by design
    try { if (probe(mime)) return mime; } catch (_e) { /* a probe that throws is a no */ }
  }
  return '';
}

/**
 * getUserMedia's rejection -> one of our four reasons. The DOMException NAME is
 * the stable part of this (the message is localised and the code is deprecated),
 * and the two that matter to the copy deck are told apart here rather than in
 * the UI: "you said no" and "there is no microphone" are different sentences.
 *
 * @returns {'denied'|'missing'|'unsupported'|'failed'}
 */
export function micErrorReason(err) {
  const name = String((err && (err.name || err.constructor?.name)) || '');
  switch (name) {
    // The player (or an OS/enterprise policy) refused.
    case 'NotAllowedError':
    case 'PermissionDeniedError':
    case 'SecurityError':
      return 'denied';
    // There is no input device, or none that can satisfy audio:true.
    case 'NotFoundError':
    case 'DevicesNotFoundError':
    case 'OverconstrainedError':
    case 'ConstraintNotSatisfiedError':
      return 'missing';
    // Something else holds the device, or the pipeline died.
    case 'NotReadableError':
    case 'TrackStartError':
    case 'AbortError':
      return 'failed';
    // No getUserMedia here at all, or none that will do audio.
    case 'NotSupportedError':
    case 'TypeError':
      return 'unsupported';
    default:
      return 'failed';
  }
}

/** Stop every track on a stream. Never throws; safe on a null/odd stream. */
function stopTracks(stream) {
  try {
    if (!stream || typeof stream.getTracks !== 'function') return;
    for (const t of stream.getTracks() || []) {
      try { t.stop?.(); } catch (_e) { /* a track that is already dead is fine */ }
    }
  } catch (_e) { /* ignore */ }
}

/**
 * @param {object}   [o]
 * @param {Function} [o.getUserMedia]    () => Promise<MediaStream>. TEST SEAM.
 * @param {Function} [o.recorderFactory] (stream, opts) => MediaRecorder. TEST SEAM.
 * @param {Function} [o.isTypeSupported] (mime) => bool. TEST SEAM.
 * @param {number}   [o.maxMs]           the hard cap (defaults to VN_MAX_MS)
 * @param {number}   [o.startTimeoutMs]  the getUserMedia ceiling (VN_START_TIMEOUT_MS)
 * @param {number}   [o.timesliceMs]     the MediaRecorder timeslice (VN_TIMESLICE_MS)
 * @param {Function} [o.now]             the clock durMs is measured on
 * @param {object}   [o.logger]          console-shaped
 */
export function createVoiceRecorder({
  getUserMedia = null, recorderFactory = null, isTypeSupported = null,
  maxMs = VN_MAX_MS, startTimeoutMs = VN_START_TIMEOUT_MS, timesliceMs = VN_TIMESLICE_MS,
  now = nowMs, logger = null,
} = {}) {
  const clock = typeof now === 'function' ? now : nowMs;
  const cap = Math.max(500, Math.floor(Number(maxMs) || VN_MAX_MS));
  const startCap = Math.max(250, Math.floor(Number(startTimeoutMs) || VN_START_TIMEOUT_MS));
  const slice = Math.max(0, Math.floor(Number(timesliceMs) || 0));
  const warn = (m) => { try { logger?.warn?.('[GG rec] ' + m); } catch (_e) { /* ignore */ } };

  /** 'idle' | 'starting' | 'recording' | 'stopping' */
  let state = 'idle';
  let stream = null;
  let rec = null;
  let mime = '';
  let chunks = [];
  let startedAt = 0;
  let stoppedAt = 0;
  let capTimer = 0;
  let stopWait = null;        // {resolve, timer} while a stop is in flight
  /* WHY THE OUTCOME IS DECIDED BEFORE THE STOP, not after it. Both stop() and
   * cancel() go through MediaRecorder.stop() — that is the only way to get the
   * microphone back — so `onstop` cannot tell the two apart on its own. The
   * caller's intent is recorded here the moment they ask, and the settle path
   * reads it. Without this a cancelled note would be assembled into a blob and
   * then thrown away, which works but means the discarded audio existed. */
  let pendingReason = 'stopped';
  let abandon = false;        // cancel() landed while getUserMedia was still awaiting
  let disposed = false;
  /* THE ATTEMPT TOKEN. Every start() takes a number, and anything that comes
   * back from getUserMedia checks it before touching a single shared field. It
   * is what makes a LATE stream (one that lands after its attempt timed out, was
   * disowned or was superseded) harmless: it is stopped where it arrives and
   * nothing else happens. Without it, "recover from a stuck start" and "do not
   * clobber the recording that is running now" are the same edit fighting
   * itself. */
  let gen = 0;
  const capSubs = new Set();
  const failSubs = new Set();

  function clearCapTimer() {
    if (!capTimer) return;
    try { clearTimeout(capTimer); } catch (_e) { /* ignore */ }
    capTimer = 0;
  }

  /**
   * THE ONE DOOR OUT. Tracks stopped, recorder dropped, timers cleared, state
   * back to idle. Called from every terminal path including the error ones —
   * if you add a branch to this file, it ends here.
   */
  function release() {
    clearCapTimer();
    /* A PENDING stop() IS ANSWERED, NEVER DROPPED. This used to null `stopWait`
     * on its way past, which left whoever was awaiting stop()/cancel() holding a
     * promise that would never settle — and the mic HUD awaits exactly that
     * before it puts its button back. One release on the wrong path was a strip
     * stuck on "sending…" for the rest of the match. Releasing the microphone
     * out from under a stop IS its answer, so it is given one.
     * Captured BEFORE the cleanup and resolved AFTER it, so the caller wakes to
     * a machine that is already idle. */
    const pending = stopWait;
    stopWait = null;
    if (pending) { try { clearTimeout(pending.timer); } catch (_e) { /* ignore */ } }
    stopTracks(stream);
    stream = null;
    if (rec) {
      // Drop our handlers before letting go: a late `dataavailable` from a
      // recorder we have finished with must not push onto the NEXT note.
      try { rec.ondataavailable = null; rec.onstop = null; rec.onerror = null; } catch (_e) { /* ignore */ }
    }
    rec = null;
    state = 'idle';
    if (pending) {
      try { pending.resolve({ ok: false, reason: 'idle', blob: null, durMs: 0, mime }); }
      catch (_e) { /* a caller that threw at its own resolve is not our problem */ }
    }
  }

  /**
   * FORCE THIS THING BACK TO PRESSABLE, from any state, at any time.
   *
   * Everything release() does, plus the disowning of an in-flight getUserMedia
   * (via the attempt token) and an ask to stop a live MediaRecorder. It exists
   * because the UI needs ONE call it can make after a refusal it cannot explain
   * — the rule being that a single failure may never be the reason the NEXT
   * press is refused too.
   */
  function reset() {
    gen++;
    abandon = false;
    pendingReason = 'cancelled';
    if (state === 'recording' || state === 'stopping') {
      try { rec?.stop?.(); } catch (_e) { /* a recorder that will not stop still loses its tracks below */ }
    }
    chunks = [];
    release();
  }

  /** The recording died on its own (not at anybody's request). */
  function emitFailed(reason) {
    for (const fn of Array.from(failSubs)) {
      try { fn({ reason }); } catch (e) { warn('onFailed handler threw: ' + ((e && e.message) || e)); }
    }
  }

  /** Assemble what arrived into one blob (or null where there is no Blob ctor). */
  function buildBlob() {
    const parts = chunks.filter((b) => b && (b.size == null || b.size > 0));
    chunks = [];
    if (parts.length === 0) return null;
    try {
      const B = globalThis.Blob;
      if (typeof B === 'function') return new B(parts, mime ? { type: mime } : undefined);
    } catch (_e) { /* fall through */ }
    // No Blob here (node without the global, an exotic host): hand back the
    // first part rather than nothing. voiceService.voiceSourceToBytes takes
    // Blob | ArrayBuffer | TypedArray, so any of them is a sendable note.
    return parts.length === 1 ? parts[0] : null;
  }

  function finishStop(reason) {
    const durMs = Math.max(0, Math.min(cap, Math.round((stoppedAt || clock()) - startedAt)));
    const blob = reason === 'cancelled' ? null : buildBlob();
    chunks = [];
    release();
    if (reason === 'cancelled') return { ok: false, reason: 'cancelled', blob: null, durMs, mime };
    const size = blob && typeof blob.size === 'number' ? blob.size : (blob ? 1 : 0);
    if (!blob || size <= 0) return { ok: false, reason: 'empty', blob: null, durMs, mime };
    return { ok: true, reason: 'stopped', blob, durMs, mime };
  }

  /** Settle a pending stop() exactly once, with the outcome the CALLER asked for. */
  function settleStop() {
    const w = stopWait;
    stopWait = null;
    if (!w) return;
    try { clearTimeout(w.timer); } catch (_e) { /* ignore */ }
    try { w.resolve(finishStop(pendingReason)); } catch (_e) { /* ignore */ }
  }

  /** THE CAP. Ten seconds is the lot — stop ourselves and tell whoever asked. */
  function onCap() {
    capTimer = 0;
    if (state !== 'recording') return;
    void stop().then((res) => {
      const payload = Object.assign({ capped: true }, res);
      for (const fn of Array.from(capSubs)) {
        try { fn(payload); } catch (e) { warn('onCapped handler threw: ' + ((e && e.message) || e)); }
      }
    });
  }

  /**
   * getUserMedia, with a ceiling on how long it may hold us in `starting` — and
   * with the late stream stopped rather than leaked. See VN_START_TIMEOUT_MS.
   *
   * @param {Function} gum   the acquire function
   * @param {number}   mine  this attempt's token
   */
  function guardedGum(gum, mine) {
    return new Promise((resolve, reject) => {
      let settled = false;
      let timer = 0;
      const finish = (fn, v) => {
        if (settled) return;
        settled = true;
        try { clearTimeout(timer); } catch (_e) { /* ignore */ }
        fn(v);
      };
      try {
        timer = setTimeout(() => {
          finish(reject, Object.assign(new Error('getUserMedia never answered'), { name: 'TimeoutError' }));
        }, startCap);
      } catch (_e) { timer = 0; }
      let p = null;
      try { p = gum(); } catch (e) { finish(reject, e); return; }
      Promise.resolve(p).then(
        (s) => {
          /* THE LATE STREAM. The attempt that asked for this is over — it timed
           * out, it was superseded, or the whole recorder was disposed — so it
           * never becomes a recording. It does not get to leave the microphone
           * open either: rule 1 does not care that a stream arrived late. */
          if (settled || mine !== gen || disposed) { stopTracks(s); return; }
          finish(resolve, s);
        },
        (e) => { finish(reject, e); },
      );
    });
  }

  async function start() {
    if (disposed) return { ok: false, reason: 'unsupported' };
    /* A DISOWNED ATTEMPT IS NOT A BUSY ONE — and this is the stuck mic, in one
     * branch. `starting` means a getUserMedia is in flight; `abandon` means the
     * gesture that asked for it has already gone (a tap, or the permission
     * prompt itself stealing the pointer and firing pointercancel). NOBODY is
     * waiting on that promise, so the fresh press supersedes it rather than
     * being told 'busy' by a recorder that is only holding a prompt nobody
     * answered. The old attempt's stream is stopped on arrival by the token
     * check in guardedGum. */
    if (state === 'starting' && abandon) {
      gen++;
      abandon = false;
      state = 'idle';
    }
    if (state !== 'idle') return { ok: false, reason: 'busy' };

    const gum = typeof getUserMedia === 'function' ? getUserMedia : defaultGetUserMedia;
    const make = typeof recorderFactory === 'function' ? recorderFactory : defaultRecorderFactory;
    if (!gum || !make) return { ok: false, reason: 'unsupported' };

    const mine = ++gen;
    state = 'starting';
    abandon = false;
    pendingReason = 'stopped';
    chunks = [];
    let s = null;
    try {
      s = await guardedGum(gum, mine);
    } catch (e) {
      /* SUPERSEDED — and it answers 'cancelled', not a failure.
       *
       * A newer press already owns this machine (this attempt was disowned while
       * its prompt was up, and only heard about it when its own ceiling
       * expired). It must touch nothing: resetting here would take the
       * microphone off the press that replaced it. It must not report a FAILURE
       * either, because a caller that recovers from failures would then do the
       * same damage a beat later — thirty seconds after a gesture nobody
       * remembers. 'cancelled' is the truth: this attempt was called off. */
      if (mine !== gen) return { ok: false, reason: 'cancelled' };
      const reason = micErrorReason(e);
      abandon = false;
      release();
      warn('getUserMedia refused (' + reason + '): ' + ((e && e.message) || e));
      return { ok: false, reason };
    }
    if (mine !== gen || disposed) { stopTracks(s); return { ok: false, reason: 'cancelled' }; }
    if (!s) { abandon = false; release(); return { ok: false, reason: 'failed' }; }

    /* THE ABANDONED HOLD. A player can press and release faster than a
     * permission-primed getUserMedia resolves, and this is the branch where the
     * stream arrives after nobody wants it: it is stopped here and now, before
     * a single sample is recorded. Without this the mic would stay open until
     * the next start(), which is the whole thing rule 1 forbids. */
    if (abandon) {
      stopTracks(s);
      abandon = false;
      release();
      return { ok: false, reason: 'cancelled' };
    }

    stream = s;
    mime = pickVoiceMime(isTypeSupported || undefined);
    try {
      const opts = { audioBitsPerSecond: VN_AUDIO_BPS };
      if (mime) opts.mimeType = mime;
      rec = make(stream, opts);
      if (!rec) throw new Error('no recorder');
      rec.ondataavailable = (e) => {
        const data = e && e.data;
        if (data && (data.size == null || data.size > 0)) chunks.push(data);
      };
      rec.onstop = () => { stoppedAt = clock(); settleStop(); };
      rec.onerror = (e) => {
        warn('recorder error: ' + ((e && e.error && e.error.message) || (e && e.message) || 'unknown'));
        stoppedAt = clock();
        // An errored recorder still owes us the microphone back.
        if (stopWait) { settleStop(); return; }
        chunks = [];
        release();
        /* ...and it owes the UI a word. Nobody asked for this stop, so nobody is
         * awaiting an answer: without the callback the mic HUD would go on
         * counting seconds against a recorder that is no longer recording, and
         * only find out when the player let go. */
        emitFailed('recorder-error');
      };
      /* THE TIMESLICE (see VN_TIMESLICE_MS). A recorder that refuses the
       * argument outright is started the plain way rather than not at all —
       * it did not start, so this is not a double start. */
      if (slice > 0) {
        try { rec.start(slice); } catch (_e) { rec.start(); }
      } else {
        rec.start();
      }
    } catch (e) {
      warn('MediaRecorder start threw: ' + ((e && e.message) || e));
      chunks = [];
      abandon = false;
      release();
      return { ok: false, reason: 'failed' };
    }

    startedAt = clock();
    stoppedAt = 0;
    state = 'recording';
    clearCapTimer();
    try { capTimer = setTimeout(onCap, cap); } catch (_e) { capTimer = 0; }
    return { ok: true, reason: 'recording', mime };
  }

  /**
   * Stop and hand back the note. Resolves — never rejects — with
   * `{ok:false, reason:'idle'}` when nothing was recording, so a double
   * pointerup (or a cap racing a release) is not an error path.
   */
  function stop() {
    if (state === 'starting') { abandon = true; return Promise.resolve({ ok: false, reason: 'cancelled', blob: null, durMs: 0, mime }); }
    if (state !== 'recording') return Promise.resolve({ ok: false, reason: 'idle', blob: null, durMs: 0, mime });
    pendingReason = 'stopped';
    return closeOut();
  }

  /**
   * The shared tail of stop() and cancel(): ask the recorder to stop, wait for
   * its `stop` event (with a watchdog), and settle with `pendingReason`.
   */
  function closeOut() {
    state = 'stopping';
    clearCapTimer();
    return new Promise((resolve) => {
      let timer = 0;
      try {
        // THE WATCHDOG. If `stop` never lands we assemble what we have and let
        // the microphone go anyway — see VN_STOP_TIMEOUT_MS.
        timer = setTimeout(() => {
          warn('recorder never fired stop — releasing anyway');
          stoppedAt = clock();
          settleStop();
        }, VN_STOP_TIMEOUT_MS);
      } catch (_e) { timer = 0; }
      stopWait = { resolve, timer };
      try { rec.stop(); }
      catch (e) {
        warn('recorder.stop threw: ' + ((e && e.message) || e));
        stoppedAt = clock();
        settleStop();
      }
    });
  }

  /**
   * Throw the recording away. The tracks stop on exactly the same path a
   * successful stop() takes — a cancelled note and a sent one release the
   * microphone identically, because the player cannot tell them apart and
   * should not have to.
   */
  function cancel() {
    if (state === 'starting') {
      abandon = true;                       // start()'s post-await branch does the release
      return Promise.resolve({ ok: false, reason: 'cancelled', blob: null, durMs: 0, mime });
    }
    if (state !== 'recording') {
      chunks = [];
      return Promise.resolve({ ok: false, reason: 'idle', blob: null, durMs: 0, mime });
    }
    pendingReason = 'cancelled';
    return closeOut();
  }

  function defaultGetUserMedia() {
    const nav = globalThis.navigator;
    const md = nav && nav.mediaDevices;
    if (!md || typeof md.getUserMedia !== 'function') {
      return Promise.reject(Object.assign(new Error('no mediaDevices'), { name: 'NotFoundError' }));
    }
    // audio:true and nothing else. Echo cancellation / noise suppression are the
    // UA's defaults on purpose: a duel is two people talking over a stage full
    // of noise, and the browser's own AEC is better than anything we would pin.
    return md.getUserMedia({ audio: true });
  }

  function defaultRecorderFactory(s, opts) {
    const MR = globalThis.MediaRecorder;
    if (typeof MR !== 'function') throw new Error('no MediaRecorder');
    return new MR(s, opts);
  }

  return {
    /** Is recording possible on this host AT ALL? Checked at call time. */
    supported() {
      if (typeof getUserMedia === 'function' && typeof recorderFactory === 'function') return true;
      try {
        const nav = globalThis.navigator;
        return !!(nav && nav.mediaDevices && typeof nav.mediaDevices.getUserMedia === 'function'
          && typeof globalThis.MediaRecorder === 'function');
      } catch (_e) { return false; }
    },

    start,
    stop,
    cancel,

    /** 'idle' | 'starting' | 'recording' | 'stopping' */
    state() { return state; },
    isRecording() { return state === 'recording'; },
    /** Milliseconds held so far, clamped to the cap. 0 when not recording. */
    elapsedMs() { return state === 'recording' ? Math.max(0, Math.min(cap, Math.round(clock() - startedAt))) : 0; },
    /** The cap this recorder enforces (the HUD's timer reads it rather than VN_MAX_MS). */
    maxMs() { return cap; },
    /** The container in use, once a recording has started. '' = the UA's default. */
    mimeType() { return mime; },

    /** fn({ok, reason, blob, durMs, capped:true}) when the 10s cap stops us -> unsub. */
    onCapped(fn) {
      if (typeof fn !== 'function') return () => {};
      capSubs.add(fn);
      return () => capSubs.delete(fn);
    },

    /**
     * fn({reason}) when the recording died WITHOUT anybody asking — a
     * MediaRecorder error mid-note, a device pulled out. The microphone is
     * already released by the time this fires; what is left is telling the
     * player, which is the caller's job. -> unsub.
     */
    onFailed(fn) {
      if (typeof fn !== 'function') return () => {};
      failSubs.add(fn);
      return () => failSubs.delete(fn);
    },

    /**
     * Back to a pressable idle from wherever we are. The recovery door for a UI
     * that has just been refused and does not know why — ONE failure may not be
     * the reason the next press fails too. Safe to call in any state, including
     * mid-recording (the note is discarded) and mid-getUserMedia (the attempt is
     * disowned, its stream stopped on arrival).
     */
    reset,

    /** Tear down. The microphone goes with it, whatever state we were in. */
    dispose() {
      if (disposed) return;
      disposed = true;
      abandon = true;
      gen++;                       // any getUserMedia still in flight is disowned
      capSubs.clear();
      failSubs.clear();
      if (state === 'recording' || state === 'stopping') {
        pendingReason = 'cancelled';
        try { rec?.stop?.(); } catch (_e) { /* ignore */ }
      }
      settleStop();
      chunks = [];
      release();
    },
  };
}

export default createVoiceRecorder;
