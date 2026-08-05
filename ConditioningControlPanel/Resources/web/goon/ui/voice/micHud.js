/* ============================================================================
 * ui/voice/micHud.js — the held mic, and the chip that says they answered.
 *
 * Two small things, mounted from ui/hud.js into two slots that point in opposite
 * directions, because one of them is a CONTROL and the other is a READOUT:
 *
 *   THE MIC        a 48px button in the arsenal drawer, directly under the tiles
 *                  — the emote is the last one on the right rail, and this is
 *                  the thing a player reaches for next. (It sat under the
 *                  opponent's monitor until 2026-08-05, where three play-tests
 *                  running never found it: the owner looked "right under the
 *                  Emote icon" every time, because holding a mic is a thing you
 *                  DO.) Press and hold; the strip grows out of the button with a
 *                  pulsing dot, the elapsed seconds and "slide left to cancel".
 *                  Release sends. Which WAY it grows is a stylesheet fact — the
 *                  drawer is the left gutter, so ui/hud.css anchors the strip
 *                  left there and flips the paint order; nothing in this file
 *                  knows or cares.
 *   THE CHIP       a speaking indicator directly under THEIR bezel while one of
 *                  their notes is playing, so a voice never arrives from nowhere.
 *
 * ────────────────────────────────────────────────────────────── the gesture ──
 *
 *   pointerdown ──(<250ms)── pointerup ──> "hold the mic to record" (no note)
 *        │
 *        └──(>=250ms held)──> RECORDING ──┬── pointerup ─────────> stop + send
 *                                         ├── slid left >80px ───> cancel
 *                                         ├── 10.0s cap ─────────> stop + send
 *                                         ├── the HUD hiding ────> cancel
 *                                         └── pointercancel /
 *                                             lostpointercapture > cancel
 *
 * THE LAST THREE SECONDS ARE A COUNTDOWN, NOT A FRACTION. For the first seven
 * the strip reads "3.4s / 10s", which answers "how much have I said". Inside
 * MIC_COUNTDOWN_MS it becomes "2s left" and the number turns hot — because the
 * question has changed to "how long have I got", and a player mid-sentence
 * should not have to subtract two numbers to find out. The cap itself is
 * unchanged and still lives in ui/voice/recorder.js; this is only how it is
 * SAID, and it is read off `rec.maxMs()` so a shrunken test recorder counts down
 * from its own cap rather than from a second copy of ten seconds.
 *
 * WHY THE RECORDER STARTS ON pointerdown AND NOT AT THE 250ms MARK. The hold
 * threshold decides whether a note EXISTS, not when the microphone opens: a
 * player who starts talking the instant they press must not lose their first
 * quarter of a second. A tap therefore opens the mic for less than 250ms and
 * then cancels it, which discards the audio and stops the track on exactly the
 * path a real cancel takes (ui/voice/recorder.js rule 1). The strip does not
 * appear until the threshold, so a tap never flashes one.
 *
 * ESCAPE IS NOT TOUCHED — THERE IS NO KEY HANDLER IN THIS FILE AT ALL. Escape
 * during Live is Mercy (boot.js's ladder) and a voice note may not add a rung to
 * it: a player reaching for the way out must never instead cancel a recording
 * and stay in the match. A held recording is released by the pointer that is
 * holding it, or by that pointer being taken away.
 *
 * ────────────────────────────────────── a failure is never the last failure ──
 *
 * ONE BAD RECORDING MAY NOT COST A PLAYER THE FEATURE. Reported 2026-08-05: the
 * mic worked once, the first attempt failed, and every press after it answered
 * "that one did not record" for the rest of the session. Two halves, and both
 * are pinned in test/selftest-hud.js §20b-bis and §20c-quater:
 *
 *   THE RECORDER LATCHED. ui/voice/recorder.js refuses any start() that is not
 *   from `idle`, so anything parking it in `starting` (a permission prompt
 *   nobody answered) refused every later press. Fixed there — but this file
 *   still calls recoverRecorder() on EVERY refusal, because a UI that cannot
 *   explain a "no" should not be the thing that decides to live with it.
 *
 *   THE STRIP LATCHED. `phase` gates onDown, and both awaits in stopAndSend are
 *   on injectable seams: a rejection used to leave the strip on 'send' with no
 *   path back. Every await is now inside a try/catch inside a try/finally that
 *   guarantees a pressable phase, MIC_SETTLE_MAX_MS is the timer under that, and
 *   a press DURING a flash cuts the word short rather than being swallowed.
 *
 * And the copy tells the two apart: S.voice.sendFailed is a note that failed,
 * S.voice.micFailed is a microphone that never opened. See micReasonLine.
 *
 * ────────────────────────────────────────────────────────── the four traps ──
 *
 *   ONE ANIMATION SLOT. The pulse lives on `.gg-voice-dot` and the level bars on
 *   `.gg-voice-bar` — leaf elements that animate and do nothing else. Nothing is
 *   ever animated on the strip or the chip themselves, which already carry state
 *   classes and would silently cancel each other's `animation` shorthand.
 *
 *   THE 0,2,0 POINTER-EVENTS TRAP. `.gg-hud-frame .gg-plate { pointer-events:
 *   auto }` is specificity 0,2,0, and the strip wears .gg-plate while recording
 *   for the data-gg-fx="hot" hardening. ui/hud.css opts the strip back out at
 *   the SAME weight, later in the file, and re-opts the button in — otherwise a
 *   transparent pill would eat clicks meant for #gg-stage.
 *
 *   RE-APPENDING A CAPTURED NODE RELEASES ITS POINTER CAPTURE (exec/flashes.js
 *   lift() documents the swallow-one dance it needs because of that). This file
 *   sidesteps it instead: every node is built ONCE at mount and the strip
 *   expands by CLASS ONLY. Nothing is re-appended, replaced or re-created while
 *   a gesture is live — including on an availability edge, which hides the host
 *   rather than rebuilding it. Keep it that way and there is no stale
 *   lostpointercapture to swallow.
 *
 *   MOUSE, TOUCH AND PEN ALIKE. Pointer events only, and NOT gated on
 *   `pointerType` the way the pinch gesture is — desktop is a mouse and this is
 *   a desktop app first.
 *
 *   A HIDDEN HUD IS A CLOSED MICROPHONE. ui/hud.css already takes both voice
 *   slots away by name under zen, but CSS alone is not enough: pointer capture
 *   BYPASSES HIT TESTING, so a `display:none` (or the `pointer-events:none` that
 *   sudden death lays over the body) does not end a hold that is already
 *   captured. Left to the stylesheet, tapping zen with a second finger while the
 *   first one holds the mic would leave a recording running with the OS mic
 *   indicator lit and no strip anywhere on screen to say so — exactly the hot mic
 *   recorder.js rule 1 forbids. ui/hud.js therefore PUSHES the bit in via
 *   `setHudHidden()`, and it ends a live gesture on the same cancel path the
 *   feature going away takes. See applyPresence().
 *
 * Node-import-safe: no DOM at import time, only inside mountMicHud().
 * ==========================================================================*/

import { S } from '../strings.js';
import { VN_MAX_MS, VN_SEND_MIN_GAP_MS } from './voiceService.js';
import { createVoiceRecorder } from './recorder.js';

/** How long the button must be held before a recording counts as wanted. */
export const MIC_HOLD_MS = 250;
/** How far left the pointer travels to cancel. 80px is past a thumb's wobble. */
export const MIC_CANCEL_PX = 80;
/** ...and where the strip starts SAYING it is about to cancel. Half way. */
export const MIC_ARM_PX = 40;
/** How long "sent" / "cancelled" stays on the strip before it folds away. */
export const MIC_FLASH_MS = 1200;
/**
 * How close to the cap the elapsed timer turns into a countdown. Three seconds
 * is two beats of warning at the pace people actually talk — long enough to
 * finish a short clause, short enough that the strip is not nagging for a third
 * of every note.
 */
export const MIC_COUNTDOWN_MS = 3000;
/** The hint caption's dwell (a tap, a refusal). Long enough to read once. */
export const MIC_HINT_MS = 2200;
/**
 * THE DEAD-MAN'S SWITCH ON THE STRIP.
 *
 * Everything this file awaits is documented to settle and every one of those
 * awaits is now wrapped — but `recorder` and `voice` are both INJECTABLE seams,
 * and a promise that never settles would leave the strip on 'send' with no way
 * back. A mic button that is dead for the rest of the match is the exact bug
 * this whole pass exists to make impossible, so there is a timer under it as
 * well as a `finally`. Comfortably longer than a ten-second note plus a send,
 * so it never fires on anything that is merely slow.
 */
export const MIC_SETTLE_MAX_MS = 20_000;
/** Timer repaint cadence while recording. 10 fps is smooth for one decimal. */
const TICK_MS = 100;

const doc = () => (typeof document !== 'undefined' ? document : null);

function el(tag, cls, text) {
  const d = doc();
  if (!d || typeof d.createElement !== 'function') return null;
  const n = d.createElement(tag);
  if (cls && n) n.className = cls;
  if (text != null && n) n.textContent = String(text);
  return n;
}

function add(parent, child) {
  if (parent && child && typeof parent.appendChild === 'function') parent.appendChild(child);
  return child;
}

function cls(node, name, on) {
  if (!node || !node.classList) return;
  try { node.classList[on ? 'add' : 'remove'](name); } catch (_e) { /* stub DOM */ }
}

function text(node, value) {
  if (node) node.textContent = value == null ? '' : String(value);
}

function attr(node, key, value) {
  try { if (node && typeof node.setAttribute === 'function') node.setAttribute(key, String(value)); }
  catch (_e) { /* stub DOM */ }
}

function sfx(audio, id) {
  try { if (audio && typeof audio.sfx === 'function') audio.sfx(id); } catch (_e) { /* stub */ }
}

function nowMs() {
  try {
    if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function') return performance.now();
  } catch (_e) { /* fall through */ }
  return Date.now();
}

function createLedger() {
  const list = [];
  return {
    add(fn) { if (typeof fn === 'function') list.push(fn); },
    listen(target, type, fn, opts) {
      if (!target || typeof target.addEventListener !== 'function') return;
      target.addEventListener(type, fn, opts);
      list.push(() => { try { target.removeEventListener(type, fn, opts); } catch (_e) { /* gone */ } });
    },
    interval(ms, fn) {
      if (typeof setInterval !== 'function') return 0;
      const id = setInterval(fn, ms);
      list.push(() => { try { clearInterval(id); } catch (_e) { /* gone */ } });
      return id;
    },
    run() { while (list.length) { const fn = list.pop(); try { fn(); } catch (_e) { /* keep unwinding */ } } },
  };
}

/**
 * A refusal from voiceService.sendBlob -> the line the player sees. Exported so
 * the suite pins the mapping rather than re-deriving it, and so a reason nobody
 * anticipated still says something true instead of nothing.
 *
 * @param {string} reason  sent|unavailable|too-soon|busy|empty|too-big|unreadable|aborted|send-failed
 * @param {number} [waitSec] seconds left on the 4s floor, for 'too-soon'
 */
export function sendReasonLine(reason, waitSec = 1) {
  switch (reason) {
    case 'sent':        return S.voice.sent;
    case 'too-soon':    return S.voice.tooSoon(Math.max(1, Math.ceil(waitSec)));
    case 'unavailable': return S.voice.notActive;
    case 'aborted':
    case 'busy':        return S.voice.cancelled;
    default:            return S.voice.sendFailed;   // empty | too-big | unreadable | send-failed
  }
}

/**
 * A refusal from the RECORDER (ui/voice/recorder.js start/stop) -> the line the
 * player sees. Separate from sendReasonLine on purpose: those reasons are about
 * a note that failed to cross, these are about a microphone that never opened,
 * and telling somebody "that one did not record" when nothing ever started is
 * how a mic problem gets mistaken for a recording problem for three play-tests
 * running. Exported so the suite pins the mapping.
 *
 * @param {string} reason  denied|missing|unsupported|cancelled|busy|failed|idle
 */
export function micReasonLine(reason) {
  switch (reason) {
    case 'denied':      return S.voice.micDenied;
    case 'missing':
    case 'unsupported': return S.voice.micMissing;
    // The player's own gesture ended it. There is nothing to report.
    case 'cancelled':   return '';
    // busy | failed | idle | anything a newer recorder invents.
    default:            return S.voice.micFailed;
  }
}

/**
 * @param {object}   o
 * @param {Element}  o.host        the mic's slot in the arsenal drawer (.gg-voice-host)
 * @param {Element}  [o.chipHost]  the incoming chip's slot, under their bezel
 * @param {object}   [o.voice]     ui/voice/voiceService.js handle. null = no mic, ever
 * @param {object}   [o.audio]     {sfx(id)}
 * @param {object}   [o.recorder]  TEST SEAM — a pre-built recorder (see recorder.js)
 * @param {Function} [o.now]       TEST SEAM — the clock the hold/slide read
 * @param {Function} [o.onLog]
 * @returns {{unmount:Function, isRecording:Function, available:Function,
 *            shown:Function, setHudHidden:Function, parts:object}}
 */
export function mountMicHud({
  host, chipHost = null, voice = null, audio = null,
  recorder = null, now = nowMs, onLog = null,
} = {}) {
  const led = createLedger();
  const clock = typeof now === 'function' ? now : nowMs;

  /* NO SERVICE, NO MIC — and no DOM either. boot.js can hand the HUD a null
   * voice (the service is built per match and a construction failure is
   * survivable), and the correct shape of this feature in that case is that it
   * does not exist. */
  const strip = host ? el('div', 'gg-voice') : null;
  if (!host || !strip || !voice) {
    // An empty flex child still costs the column a `gap` — the slots are hidden
    // rather than merely left blank so a build without voice notes has exactly
    // the desk it had before this feature existed.
    try { if (host) host.hidden = true; if (chipHost) chipHost.hidden = true; } catch (_e) { /* stub */ }
    return {
      unmount() { led.run(); },
      isRecording() { return false; },
      available() { return false; },
      /** Nothing to hide, but the desk pushes the bit unconditionally. */
      setHudHidden() {},
      shown() { return false; },
      parts: {},
    };
  }

  /* ---------------------------------------------------------------- the DOM
   * Built ONCE. Everything below only ever changes classes and text — see the
   * pointer-capture trap in the header. */
  const meta = add(strip, el('div', 'gg-voice-meta'));
  const dot = add(meta, el('i', 'gg-voice-dot'));            // the ONE animated node
  const timeEl = add(meta, el('span', 'gg-voice-time', ''));
  const slideEl = add(meta, el('span', 'gg-voice-slide', S.voice.slideToCancel));
  const btn = add(strip, el('button', 'gg-voice-btn', '🎙'));
  if (btn) {
    btn.type = 'button';
    attr(btn, 'aria-label', S.voice.hudLabel);
    attr(btn, 'title', S.voice.holdHint);
    // Chromium turns a long press into a text selection / callout otherwise, and
    // on a phone it would also try to scroll the page out from under the hold.
    try { btn.style.touchAction = 'none'; btn.style.userSelect = 'none'; } catch (_e) { /* stub */ }
  }
  add(host, strip);

  /* The caption. It is NOT a toast: ui/toasts.js is a boot.js singleton the HUD
   * has no handle on (the desk raises `gg-options-open` for the same reason),
   * and standing up a second toast manager would put a second stack on
   * #gg-toasts. A line that appears an inch from the finger that caused it is
   * also simply the better place for "hold the mic to record". */
  const hint = add(host, el('div', 'gg-voice-hint', ''));
  if (hint) { hint.hidden = true; attr(hint, 'role', 'status'); }

  /* Their end: a chip under the bezel while a note plays. Three bars, each with
   * its own animation on its own leaf (the animation-slot rule), plus the words
   * — a moving bar is never the only channel. */
  let chip = null;
  let chipText = null;
  if (chipHost) {
    // Hidden AT THE HOST, not only at the chip: a zero-height flex child still
    // takes a `gap` off the monitor column, and the column is the one place on
    // the desk where 0.45rem of dead space pushes the rail into the deck.
    chipHost.hidden = true;
    chip = add(chipHost, el('div', 'gg-voice-chip'));
    if (chip) {
      chip.hidden = true;
      attr(chip, 'role', 'status');
      const bars = add(chip, el('span', 'gg-voice-bars'));
      for (let i = 0; i < 3; i++) add(bars, el('i', 'gg-voice-bar'));
      chipText = add(chip, el('span', 'gg-voice-chip-text', S.voice.incoming));
    }
  }

  /* ------------------------------------------------------------ the machine */

  const rec = recorder || createVoiceRecorder({ maxMs: VN_MAX_MS });
  const ownsRecorder = !recorder;

  /** 'idle' | 'hold' | 'rec' | 'send' | 'flash' */
  let phase = 'idle';
  let pointerId = null;
  let downX = 0;
  let downAt = 0;
  let holdTimer = 0;
  let flashTimer = 0;
  let hintTimer = 0;
  let chipTimer = 0;
  let settleTimer = 0;
  /* One number per press. rec.start() is awaited, and its answer can arrive long
   * after the gesture that asked for it (a permission prompt is a human), so the
   * answer carries the number it was asked under and is dropped if it is stale. */
  let gestureSeq = 0;
  /* An await is in flight inside stopAndSend / cancelRecording. The phase alone
   * cannot say so — 'flash' is worn both while cancel() is being waited on and
   * while a finished word is merely sitting on the strip — and the difference
   * matters to onDown, which may interrupt the second but not the first. */
  let settling = false;
  let willCancel = false;
  let ending = false;            // inside MIC_COUNTDOWN_MS of the cap
  let lastSentAt = -Infinity;
  let live = false;              // the availability bit, from voice.onStateChanged
  let hudHidden = false;         // the zen bit, pushed in by ui/hud.js
  let unmounted = false;

  function log(entry) {
    if (typeof onLog !== 'function') return;
    try { onLog(entry); } catch (_e) { /* the log is never load-bearing */ }
  }

  function clearTimer(id) { if (id) { try { clearTimeout(id); } catch (_e) { /* gone */ } } return 0; }

  /**
   * ONE FAILURE MAY NOT POISON THE NEXT PRESS.
   *
   * The recorder is asked to go back to idle whatever it thinks it is holding —
   * a getUserMedia nobody answered, a MediaRecorder that would not construct, a
   * note half assembled. This is the whole cure for the reported bug: before it,
   * a first attempt that failed could leave the recorder parked in `starting` or
   * `stopping`, and every later press was answered 'busy' and shown a refusal,
   * for the rest of the session.
   *
   * `reset()` is the recorder's own door; `cancel()` is the fallback for an
   * older/injected handle that has no such thing, and its promise is swallowed
   * because nobody is waiting on a recovery.
   */
  function recoverRecorder() {
    try {
      if (typeof rec.reset === 'function') { rec.reset(); return; }
      const p = rec.cancel?.();
      if (p && typeof p.catch === 'function') p.catch(() => {});
    } catch (_e) { /* a handle that throws at its own recovery is already gone */ }
  }

  /** rec.cancel() where nothing is waiting for the answer. Never unhandled. */
  function safeCancel() {
    try {
      const p = rec.cancel?.();
      if (p && typeof p.catch === 'function') p.catch(() => {});
    } catch (_e) { /* ignore */ }
  }

  /** Put the strip back on the button, from anywhere. Never throws. */
  function goIdle() {
    holdTimer = clearTimer(holdTimer);
    flashTimer = clearTimer(flashTimer);
    releasePointer();
    willCancel = false;
    setPhase('idle');
    text(timeEl, '');
  }

  /** See MIC_SETTLE_MAX_MS. Armed around every await, disarmed by the finally. */
  function armSettleGuard() {
    settleTimer = clearTimer(settleTimer);
    try {
      settleTimer = setTimeout(() => {
        settleTimer = 0;
        if (unmounted) return;
        settling = false;
        recoverRecorder();
        goIdle();
        showHint(S.voice.micFailed);
        log({ t: 'voice-out', ok: false, reason: 'stuck' });
      }, MIC_SETTLE_MAX_MS);
    } catch (_e) { settleTimer = 0; }
  }

  function clearSettleGuard() { settleTimer = clearTimer(settleTimer); }

  function showHint(line) {
    if (!hint || !line) return;
    text(hint, line);
    hint.hidden = false;
    hintTimer = clearTimer(hintTimer);
    try { hintTimer = setTimeout(() => { if (hint) hint.hidden = true; }, MIC_HINT_MS); } catch (_e) { /* ignore */ }
  }

  /** The strip's whole appearance is this attribute plus two classes. */
  function paintPhase() {
    attr(strip, 'data-gg-voice', phase);
    cls(strip, 'is-rec', phase === 'rec');
    cls(strip, 'is-cancel', phase === 'rec' && willCancel);
    cls(strip, 'is-ending', phase === 'rec' && ending);
    // .gg-plate ONLY while the strip is a strip: idle, it is a bare round
    // button and a plate behind it would be a box around a circle. goon.css's
    // html[data-gg-fx="hot"] rule hardens it for free while it is on.
    cls(strip, 'gg-plate', phase === 'rec' || phase === 'send' || phase === 'flash');
    /* The affordance is a WORD the whole time it is on screen — "slide left to
     * cancel", standing, from the first frame of the strip. The half-way state
     * brightens it rather than rewriting it: a past tense ("cancelled") on a
     * recording that is still running would be a lie, and the moment it stops
     * being one the strip is already showing the real thing. */
    text(slideEl, S.voice.slideToCancel);
  }

  /**
   * The cap THIS recorder enforces. Asked every time rather than cached: the
   * handle can be injected (the suite shrinks it to hundreds of milliseconds so
   * it can wait a cap out), and VN_MAX_MS is only the fallback for a recorder
   * old enough not to answer.
   */
  function capMs() {
    try {
      const m = typeof rec.maxMs === 'function' ? Number(rec.maxMs()) : 0;
      if (m > 0) return m;
    } catch (_e) { /* fall through */ }
    return VN_MAX_MS;
  }

  function paintTimer() {
    if (phase !== 'rec') return;
    const max = capMs();
    const elapsed = rec.elapsedMs();
    const left = Math.max(0, max - elapsed);
    /* THE HANDOVER. Two readings of the same clock — "how much have I said" and
     * "how long have I got" — and the strip only ever shows the one the player
     * is asking. The class change is the colour half and rides the same repaint,
     * so the words and the red never disagree by a tick. */
    const nextEnding = left <= MIC_COUNTDOWN_MS;
    if (nextEnding !== ending) { ending = nextEnding; paintPhase(); }
    // ceil, so the last whole second is spoken as "1s left" rather than "0s
    // left" for its entire duration.
    text(timeEl, ending ? S.voice.recordCountdown(Math.ceil(left / 1000)) : S.voice.recordTimer(elapsed, max));
  }

  function setPhase(next) {
    phase = next;
    // The countdown belongs to ONE recording. Leaving 'rec' for any reason —
    // sent, cancelled, taken away — puts the strip back on the elapsed reading
    // before the next hold can inherit a hot number from the last one.
    if (next !== 'rec') ending = false;
    paintPhase();
    if (next === 'rec') paintTimer();
  }

  /** A terminal word on the strip (sent / cancelled / a refusal), then fold. */
  function flash(line) {
    setPhase('flash');
    text(timeEl, line);
    text(slideEl, '');
    flashTimer = clearTimer(flashTimer);
    try {
      flashTimer = setTimeout(() => {
        flashTimer = 0;
        if (unmounted) return;
        setPhase('idle');
        text(timeEl, '');
      }, MIC_FLASH_MS);
    } catch (_e) {
      // No timer here (a host without setTimeout, a stub DOM). A word nobody can
      // clear would be a button nobody can press: skip the dwell, keep the mic.
      flashTimer = 0;
      setPhase('idle');
      text(timeEl, '');
    }
  }

  /** Let go of the pointer, whatever happened to it. Never throws. */
  function releasePointer() {
    const id = pointerId;
    pointerId = null;
    if (id == null || !btn) return;
    try {
      if (typeof btn.hasPointerCapture === 'function' && !btn.hasPointerCapture(id)) return;
      btn.releasePointerCapture?.(id);
    } catch (_e) { /* the UA already took it back */ }
  }

  /* ---------------------------------------------------------------- sending */

  /**
   * Stop, and put the note on the wire.
   *
   * EVERY EXIT LEAVES A PRESSABLE BUTTON. Two awaits here, on two injectable
   * seams, and before 2026-08-05 a throw from either one walked straight out of
   * this function as an unhandled rejection with the strip still reading
   * "sending…" — a phase onDown refuses, forever. Both are caught into a REASON
   * now (a thrown recorder is a failed recording, which is a thing the copy deck
   * has a sentence for), and the `finally` is the backstop for the paths a
   * future edit adds.
   */
  async function stopAndSend() {
    settling = true;
    armSettleGuard();
    setPhase('send');
    text(timeEl, S.voice.sending);
    text(slideEl, '');
    try {
      let res = null;
      let threw = '';
      try { res = await rec.stop(); }
      catch (e) { threw = 'stop-threw'; log({ t: 'voice-out', ok: false, reason: threw, err: (e && e.message) || String(e) }); res = null; }
      if (unmounted) return;
      if (!res || !res.ok || !res.blob) {
        const reason = threw || (res && res.reason) || 'empty';
        // ...and the recorder does not get to stay in whatever state produced
        // that. The next press starts from scratch.
        recoverRecorder();
        /* WHICH SENTENCE. 'empty' is a mic that produced silence (a muted
         * device, a track that never delivered a sample) — the recording
         * failed, and sendFailed says exactly that. 'idle' / 'cancelled' mean
         * there was no recording to fail: the recorder was already gone when we
         * asked, which is a MIC failure and gets the mic's sentence. */
        flash(reason === 'empty' ? S.voice.sendFailed : S.voice.micFailed);
        log({ t: 'voice-out', ok: false, reason });
        return;
      }
      // The two cues are AFTER the microphone is closed, never before or during:
      // a sound played while recording is a sound that ends up in the note.
      sfx(audio, 'ui-select');
      let out = null;
      try { out = await voice.sendBlob(res.blob, { durMs: res.durMs }); }
      catch (e) { log({ t: 'voice-out', ok: false, reason: 'send-threw', err: (e && e.message) || String(e) }); out = null; }
      if (unmounted) return;
      const reason = (out && out.reason) || 'send-failed';
      if (out && out.ok) lastSentAt = clock();
      const waitSec = Math.max(1, Math.ceil((VN_SEND_MIN_GAP_MS - (clock() - lastSentAt)) / 1000));
      flash(sendReasonLine(reason, waitSec));
      log({ t: 'voice-out', ok: !!(out && out.ok), reason, durMs: res.durMs });
    } finally {
      settling = false;
      clearSettleGuard();
      /* THE GUARANTEE. However this exited — a resolved send, a thrown seam, an
       * early return somebody adds next year — the strip may not be left on a
       * phase that refuses the next press. 'flash' folds itself; anything else
       * is put back by hand. */
      if (!unmounted && phase !== 'flash' && phase !== 'idle') goIdle();
    }
  }

  async function cancelRecording(why) {
    settling = true;
    armSettleGuard();
    setPhase('flash');
    try {
      try { await rec.cancel(); }
      catch (e) { log({ t: 'voice-cancel', why: 'cancel-threw', err: (e && e.message) || String(e) }); recoverRecorder(); }
      if (unmounted) return;
      sfx(audio, 'ui-back');
      flash(S.voice.cancelled);
      log({ t: 'voice-cancel', why: why || 'pointer' });
    } finally {
      settling = false;
      clearSettleGuard();
      // A cancel that never reached flash() still owes the player their button.
      if (!unmounted && phase !== 'flash' && phase !== 'idle') goIdle();
    }
  }

  /* --------------------------------------------------------- the pointer -- */

  /**
   * Open the microphone for this gesture.
   *
   * A REFUSAL IS RECOVERED FROM, NOT JUST REPORTED. Whatever the recorder is
   * holding onto when it says no, it is forced back to idle here — so the very
   * next pointerdown is a fresh attempt instead of a second refusal. That one
   * line is the reported bug: without it a first failure latched the recorder
   * out of `idle` and every press afterwards was answered 'busy' and told "that
   * one did not record", which was neither true nor recoverable.
   */
  async function startMic(mine) {
    let res = null;
    try { res = await rec.start(); }
    catch (e) {
      log({ t: 'voice-mic', ok: false, reason: 'start-threw', err: (e && e.message) || String(e) });
      res = { ok: false, reason: 'failed' };
    }
    if (unmounted) return;
    /* A LATER PRESS OWNS THE MIC NOW. An answer to a gesture that is over may
     * not act on the one that replaced it — recovering "from" this refusal would
     * take the microphone off a recording that is running perfectly well, and a
     * getUserMedia ceiling means that answer can arrive half a minute late. */
    if (mine !== gestureSeq) {
      // ...and a stale attempt that somehow SUCCEEDED has opened a microphone no
      // gesture owns. That is rule 1's problem, so it is given back here.
      if (res && res.ok) recoverRecorder();
      log({ t: 'voice-mic', ok: false, reason: 'stale', stale: true });
      return;
    }
    if (res && res.ok) return;
    const reason = (res && res.reason) || 'failed';
    // 'cancelled' is our own gesture ending the attempt — there is nothing stuck
    // and nothing to say. Everything else gets the machine put back.
    if (reason !== 'cancelled') recoverRecorder();
    /* THE ONE MOMENT A PLAYER CAN BE TOLD WHY. If the gesture has already moved
     * on (a tap that finished, a release that is mid-send) the refusal is that
     * path's to report, and saying it twice would be two words on one strip. */
    if (phase === 'hold' || phase === 'rec') {
      releasePointer();
      holdTimer = clearTimer(holdTimer);
      setPhase('idle');
      showHint(micReasonLine(reason));
    }
    log({ t: 'voice-mic', ok: false, reason });
  }

  function onDown(e) {
    if (!live || settling) return;
    /* A FLASH IS NOT A LOCK. "sent" / "cancelled" / a refusal holds the strip for
     * MIC_FLASH_MS, and a player who has just been told a note failed reaches
     * straight back for the button — a mic that ignores that press is
     * indistinguishable from the stuck one everything above exists to prevent.
     * The word is cut short and the new hold starts from a clean idle. (The
     * awaits behind those words are already finished; `settling` above is what
     * guards the ones that are not.) */
    if (phase === 'flash') {
      flashTimer = clearTimer(flashTimer);
      setPhase('idle');
      text(timeEl, '');
    }
    if (phase !== 'idle') return;
    if (e && typeof e.preventDefault === 'function') e.preventDefault();
    pointerId = e && e.pointerId != null ? e.pointerId : null;
    downX = e && typeof e.clientX === 'number' ? e.clientX : 0;
    downAt = clock();
    willCancel = false;
    setPhase('hold');
    // CAPTURE FIRST. Everything after this — the slide, the release, the OS
    // stealing the gesture — has to arrive on this button even when the finger
    // has long since left it.
    try { if (pointerId != null) btn.setPointerCapture?.(pointerId); } catch (_e) { /* ignore */ }

    // The microphone opens NOW (see the header); the STRIP waits for the hold.
    void startMic(++gestureSeq);

    holdTimer = clearTimer(holdTimer);
    try {
      holdTimer = setTimeout(() => {
        holdTimer = 0;
        if (unmounted || phase !== 'hold') return;
        setPhase('rec');
      }, MIC_HOLD_MS);
    } catch (_e) { /* ignore */ }
  }

  function onMove(e) {
    if (phase !== 'rec' && phase !== 'hold') return;
    if (pointerId != null && e && e.pointerId != null && e.pointerId !== pointerId) return;
    const x = e && typeof e.clientX === 'number' ? e.clientX : downX;
    const dx = x - downX;
    const next = dx <= -MIC_CANCEL_PX;
    const arming = dx <= -MIC_ARM_PX;
    if (arming !== willCancel || next) {
      willCancel = arming;
      paintPhase();
    }
    if (next && phase === 'rec') { finishGesture('slide'); }
  }

  function onUp(e) {
    if (phase !== 'rec' && phase !== 'hold') return;
    if (pointerId != null && e && e.pointerId != null && e.pointerId !== pointerId) return;
    if (e && typeof e.preventDefault === 'function') e.preventDefault();
    finishGesture(willCancel ? 'slide' : 'up');
  }

  /** pointercancel / lostpointercapture: the hand is empty. Nothing strands. */
  function onLost(e) {
    if (phase !== 'rec' && phase !== 'hold') return;
    if (pointerId != null && e && e.pointerId != null && e.pointerId !== pointerId) return;
    finishGesture('lost');
  }

  /**
   * The ONE exit from a gesture. `how` decides what happens to the audio:
   *   'up'    held long enough -> stop and send; too short -> hint, no note
   *   'slide' the cancel gesture
   *   'lost'  pointercancel / lostpointercapture / the feature going away
   */
  function finishGesture(how) {
    const wasRec = phase === 'rec';
    const held = clock() - downAt;
    holdTimer = clearTimer(holdTimer);
    releasePointer();
    willCancel = false;

    if (how === 'up' && wasRec) { void stopAndSend(); return; }
    if (how === 'up' && !wasRec) {
      // A TAP. Nothing was wanted, so nothing is kept — and the mic that opened
      // on pointerdown closes on the same call a real cancel makes. The strip is
      // put back FIRST: a tap is the commonest press there is, and the recorder
      // may still be sitting on a permission prompt when it lands.
      setPhase('idle');
      safeCancel();
      if (held < MIC_HOLD_MS) showHint(S.voice.holdHint);
      log({ t: 'voice-tap', heldMs: Math.round(held) });
      return;
    }
    if (wasRec) { void cancelRecording(how); return; }
    setPhase('idle');
    safeCancel();
  }

  if (btn) {
    led.listen(btn, 'pointerdown', onDown);
    led.listen(btn, 'pointermove', onMove);
    led.listen(btn, 'pointerup', onUp);
    led.listen(btn, 'pointercancel', onLost);
    led.listen(btn, 'lostpointercapture', onLost);
  }

  /* THE CAP. Ten seconds auto-stops AND auto-sends: the recorder has already
   * closed the microphone by the time this runs, so the only thing left is to
   * put the note on the wire and let the pointer go. */
  led.add(rec.onCapped((res) => {
    if (unmounted || phase !== 'rec') return;
    releasePointer();
    holdTimer = clearTimer(holdTimer);
    setPhase('send');
    text(timeEl, S.voice.sending);
    if (!res || !res.ok || !res.blob) {
      recoverRecorder();
      flash(res && res.reason === 'empty' ? S.voice.sendFailed : S.voice.micFailed);
      log({ t: 'voice-out', ok: false, reason: (res && res.reason) || 'empty', capped: true });
      return;
    }
    sfx(audio, 'ui-select');
    void sendCapped(res);
  }));

  /**
   * The cap's own send. Split out of the subscriber so it can be a plain
   * try/finally like stopAndSend's — a `.then()` with no `.catch()` on the same
   * two seams was the other way the strip could be left reading "sending…" with
   * no path back to the button.
   */
  async function sendCapped(res) {
    settling = true;
    armSettleGuard();
    try {
      let out = null;
      try { out = await voice.sendBlob(res.blob, { durMs: res.durMs }); }
      catch (e) { log({ t: 'voice-out', ok: false, reason: 'send-threw', err: (e && e.message) || String(e) }); out = null; }
      if (unmounted) return;
      const reason = (out && out.reason) || 'send-failed';
      if (out && out.ok) lastSentAt = clock();
      const waitSec = Math.max(1, Math.ceil((VN_SEND_MIN_GAP_MS - (clock() - lastSentAt)) / 1000));
      flash(sendReasonLine(reason, waitSec));
      log({ t: 'voice-out', ok: !!(out && out.ok), reason, durMs: res.durMs, capped: true });
    } finally {
      settling = false;
      clearSettleGuard();
      if (!unmounted && phase !== 'flash' && phase !== 'idle') goIdle();
    }
  }

  /* THE RECORDER DYING UNDER A LIVE HOLD. A MediaRecorder can fall over mid-note
   * (a device unplugged, a pipeline that gave up); ui/voice/recorder.js releases
   * the microphone itself and says so here. Without this the strip would go on
   * counting seconds against a recorder that stopped recording, and the player
   * would only find out when they let go of a note that no longer existed. */
  if (typeof rec.onFailed === 'function') {
    try {
      led.add(rec.onFailed(() => {
        if (unmounted || settling) return;
        if (phase !== 'rec' && phase !== 'hold') return;
        releasePointer();
        holdTimer = clearTimer(holdTimer);
        flash(S.voice.micFailed);
        log({ t: 'voice-mic', ok: false, reason: 'recorder-error' });
      }) || (() => {}));
    } catch (_e) { /* a recorder without the hook simply never reports one */ }
  }

  /* --------------------------------------------------------- availability -- */

  /**
   * THE ONE PRESENCE RULE. Two independent bits decide whether the mic is on the
   * desk at all, and NEITHER of them may leave a gesture running:
   *
   *   live       ui/voice/voiceService.js's five-fact predicate — both consents,
   *              the peer's build, the phase, your own opt-in.
   *   hudHidden  the desk's zen toggle, pushed in by ui/hud.js.
   *
   * The mic is not DISABLED when either is false — it is NOT THERE. A greyed-out
   * button would be a standing invitation to a feature the other player has not
   * agreed to, and there is nothing useful to say on it (S.voice.notActive is
   * the lobby's job, once, where the toggle is).
   *
   * The host is HIDDEN rather than emptied: rebuilding these nodes is what would
   * release a live pointer capture (see the header). And it is `hidden` on the
   * host rather than a class, both times — the stylesheet's zen rule already
   * hides the slot, but only the attribute survives a stylesheet that has not
   * loaded, and only the JS below closes the microphone. A recording that
   * outlives its own strip is the failure mode this function exists to make
   * impossible.
   *
   * @param {'lost'|'zen'} why  what to log the abandoned recording as
   */
  function applyPresence(why) {
    const shown = live && !hudHidden;
    host.hidden = !shown;
    cls(host, 'is-live', shown);
    // A caption that outlived its slot: the hint is a child of the host, so it
    // goes dark with it, but it must not come BACK when zen does.
    if (!shown && hint) hint.hidden = true;
    // Going away mid-hold: the gesture dies with the surface it was drawn on,
    // and the recorder is cancelled rather than left holding the microphone.
    if (!shown && (phase === 'rec' || phase === 'hold')) finishGesture(why || 'lost');
  }

  function setLive(on) {
    const next = !!on;
    if (next === live) return;
    live = next;
    applyPresence('lost');
  }

  /**
   * The desk hiding (zen) or showing its chrome. Idempotent, so ui/hud.js can
   * push the remembered preference at mount time without knowing whether it
   * differs from the default.
   */
  function setHudHidden(on) {
    const next = !!on;
    if (next === hudHidden) return;
    hudHidden = next;
    applyPresence('zen');
  }
  host.hidden = true;
  try { setLive(voice.available()); } catch (_e) { setLive(false); }
  try { led.add(voice.onStateChanged((on) => { try { setLive(on); } catch (_e) { /* ignore */ } })); }
  catch (_e) { /* a service without the hook simply never changes */ }

  /* ------------------------------------------------------------ their note -- */

  led.add(voice.onIncoming((info) => {
    if (unmounted || !chip) return;
    const emote = info && info.emote;
    const label = emote ? S.voice.incomingWithEmote : S.voice.incoming;
    text(chipText, emote ? emote + ' ' + label : label);
    chip.hidden = false;
    if (chipHost) chipHost.hidden = false;
    cls(chip, 'is-in', true);
    chipTimer = clearTimer(chipTimer);
    // Their declared duration, clamped: a peer that claims a minute gets ten
    // seconds and a half of chip, which is what ui/audio.js will play anyway.
    const ms = Math.max(1200, Math.min(VN_MAX_MS + 500, Math.round(Number(info && info.durMs) || 0) || 2000));
    try {
      chipTimer = setTimeout(() => {
        if (unmounted || !chip) return;
        chip.hidden = true;
        if (chipHost) chipHost.hidden = true;
        cls(chip, 'is-in', false);
      }, ms);
    } catch (_e) { /* ignore */ }
    log({ t: 'voice-in', emote: emote || '', durMs: info && info.durMs });
  }));

  // One cheap ticker for the elapsed seconds. It repaints nothing when nothing
  // is being held, so it costs a comparison every tenth of a second.
  led.interval(TICK_MS, () => { try { paintTimer(); } catch (_e) { /* ignore */ } });

  paintPhase();

  return {
    /** For a play-test driver / the suite: the live nodes, without re-deriving them. */
    parts: { strip, button: btn, meta, dot, timeEl, slideEl, hint, chip },
    isRecording() { return phase === 'rec'; },
    /** The phase name, for a driver that wants more than a boolean. */
    phase() { return phase; },
    available() { return live; },
    /** Is it actually on the desk? available() AND the HUD not hidden. */
    shown() { return live && !hudHidden; },
    /** ui/hud.js's zen toggle, pushed in. See applyPresence(). */
    setHudHidden,
    /** The recorder, so the library screen could share one if it ever wants to. */
    recorder: rec,

    unmount() {
      unmounted = true;
      holdTimer = clearTimer(holdTimer);
      flashTimer = clearTimer(flashTimer);
      hintTimer = clearTimer(hintTimer);
      chipTimer = clearTimer(chipTimer);
      settleTimer = clearTimer(settleTimer);
      settling = false;
      releasePointer();
      led.run();
      // The microphone NEVER outlives the desk. dispose() stops the tracks
      // whatever state the recorder was in, including mid-hold.
      if (ownsRecorder) { try { rec.dispose(); } catch (_e) { /* gone */ } }
      else { try { rec.cancel(); } catch (_e) { /* gone */ } }
      try { strip.remove(); } catch (_e) { /* gone */ }
      try { hint?.remove(); } catch (_e) { /* gone */ }
      try { chip?.remove(); } catch (_e) { /* gone */ }
    },
  };
}

export default mountMicHud;
