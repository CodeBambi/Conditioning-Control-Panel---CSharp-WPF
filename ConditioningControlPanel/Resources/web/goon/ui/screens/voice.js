/* ============================================================================
 * ui/screens/voice.js — SEND VOICE NOTES. The tenth screen.
 *
 * The library half of the feature: record up to eight ten-second notes ahead of
 * time, hear them back, and pin one to an emote so that firing the emote in a
 * duel sends the note with it. Reached from the title menu, left with Back.
 *
 * THREE THINGS ON THIS SCREEN ARE NOT UI, THEY ARE THE FEATURE'S SAFETY:
 *
 *   1. THE ACK GATE. The opt-in toggle is INERT until the acknowledgment modal
 *      has been read once (`prefs.voiceAckSeen`). The modal says both halves —
 *      your real voice goes to them, AND theirs can come back to you — because
 *      one switch grants both and a player who only reads the label would only
 *      learn the first half. Cancel leaves everything exactly as it was.
 *   2. THE TOGGLE IS THE WHOLE LOCAL GATE. `prefs.voiceNotesEnabled` is what
 *      ui/voice/voiceService.js reads on the RECEIVE path as well as the send
 *      one: off, an inbound note is dropped without being decoded. So this
 *      checkbox is not a "hide the button" switch and the copy never says it is.
 *   3. RECORDING IS OPT-IN PER PRESS. The mic is opened when the record button
 *      is pressed and released the moment it stops (ui/voice/recorder.js owns
 *      the track). Nothing on this screen holds a hot mic.
 *
 * WHAT IT DOES NOT DO: it never sends. Nothing recorded here crosses until an
 * emote fires it (ui/emotes.js) or the HUD mic sends a live one, both of which
 * are gated on the match's two consents by the service. A player can record,
 * listen and delete all day with no match in existence.
 *
 * Structure follows ui/screens/assets.js — the other title-menu screen with a
 * store-driven list: router `el`/`button` helpers, one ledger, a full repaint on
 * every change (eight rows is not a virtualization problem).
 * ==========================================================================*/

import { createLedger, el, button } from '../router.js';
import { S } from '../strings.js';
import { EMOTE_ICONS } from '../emotes.js';
import { VN_MAX_MS, VN_MAX_NOTES } from '../voice/voiceService.js';

/** How often the live recording counter repaints. Ten a second reads as smooth. */
const TICK_MS = 100;
/** How long past the cap this screen waits before stopping a recorder that has
 *  not stopped itself. See THE CEILING IS OWNED BY WHOEVER GETS THERE FIRST. */
const CAP_GRACE_MS = 400;

/* ----------------------------------------------------------------------------
 * THE RECORDER SEAM.
 *
 * ui/voice/recorder.js is built by the OTHER wave-2 agent, in parallel with this
 * file, and may not exist yet at all. So it is reached by DYNAMIC import at the
 * moment the record button is pressed (a missing module is then one toast, not a
 * screen that fails to mount), and everything this screen assumes about its shape
 * is written down here, in one adapter, so that a mismatch is a three-line fix
 * rather than a hunt through the screen.
 *
 * WHAT THIS SCREEN EXPECTS OF IT:
 *
 *   import('../voice/recorder.js') → a factory under one of RECORDER_FACTORIES
 *   factory({ maxMs, logger }) → recorder
 *   recorder.start()  → Promise<{ok:boolean, reason?:string}|undefined>
 *                       (`undefined`/no `ok` field = started; ok:false = refused,
 *                        with reason 'denied' | 'missing' | anything else)
 *   recorder.stop()   → Promise<{ok?, reason?, blob, durMs}|Blob>
 *   recorder.onCapped(fn) → optional; fn({ok, reason, blob, durMs}) when the
 *                       recorder stopped ITSELF at the ceiling
 *   recorder.cancel() → optional; discard without producing a blob
 *   recorder.dispose()→ optional; release the mic track
 *
 * THE CEILING IS OWNED BY WHOEVER GETS THERE FIRST, and that is the one subtle
 * thing in here. The recorder stops itself at maxMs; this screen also runs a
 * timer, because the ten seconds is the contract's number and a screen that
 * relies on somebody else's timer to keep a promise about somebody's voice is
 * relying on the wrong thing. But a stop() AFTER the recorder already capped
 * answers `{ok:false, reason:'idle'}` — the note is already made — so the
 * screen subscribes to onCapped where it exists and its own timer runs at a
 * grace beyond the cap as the fallback for a recorder that has none. Whichever
 * arrives first clears `recorder`, and the other one then does nothing.
 * -------------------------------------------------------------------------- */
export const RECORDER_FACTORIES = ['createVoiceRecorder', 'createRecorder', 'createMicRecorder', 'default'];

/**
 * Normalize whatever stop() resolved into {ok, reason, blob, durMs}.
 * EXPORTED so test/selftest-voice-ui.js can pin the seam's tolerances without
 * importing ui/voice/recorder.js (a sibling wave's file, and one that reaches a
 * microphone) — the adapter is the contract, so the adapter is what is tested.
 */
export function normalizeRecording(res) {
  if (!res) return { ok: false, reason: 'empty', blob: null, durMs: 0 };
  // A bare Blob is a perfectly reasonable thing for a recorder to resolve.
  const blob = (res && typeof res === 'object' && ('size' in res || 'byteLength' in res) && !('blob' in res))
    ? res : (res.blob || null);
  const durMs = Math.max(0, Math.floor(Number(res.durMs || res.durationMs || res.ms || 0)));
  if (res.ok === false) return { ok: false, reason: String(res.reason || 'failed'), blob: null, durMs };
  if (!blob) return { ok: false, reason: String(res.reason || 'empty'), blob: null, durMs };
  return { ok: true, reason: 'recorded', blob, durMs };
}

export function mount(container, ctx) {
  const ledger = createLedger();
  ledger.logger = ctx?.logger || null;

  const { actions, audio, prefs, sheets, toasts, logger } = ctx || {};
  const store = (ctx && ctx.notes) || null;
  const getMatch = (ctx && ctx.getMatch) || null;

  /* ---- state ------------------------------------------------------------- */
  let notes = [];                 // the metadata list, oldest first
  let recorder = null;            // the live recorder handle, or null
  let recordStartedAt = 0;
  let tickTimer = 0;
  let capOff = null;              // the recorder's onCapped unsubscribe, while recording
  let playingId = '';             // the note currently previewing, or ''
  let busy = false;               // one storage verb at a time (double-click guard)

  const acked = () => !!(prefs && prefs.get && prefs.get('voiceAckSeen'));
  const enabled = () => !!(prefs && prefs.get && prefs.get('voiceNotesEnabled'));
  const toast = (text, kind) => { try { toasts?.show?.(text, { kind: kind || 'info' }); } catch (_e) { /* stub */ } };

  /* ---- head -------------------------------------------------------------- */
  const head = el('div', { class: 'gg-voice-head' }, [
    el('div', { class: 'gg-eyebrow' }, [el('i'), el('span', { text: S.voice.eyebrow })]),
    el('p', { class: 'gg-lead', text: S.voice.lead }),
  ]);

  /* ---- the opt-in --------------------------------------------------------
   * A CHECKBOX with `disabled` until the ack has been read, and a click on the
   * ROW (which is not disabled) opens the modal. Two affordances for one thing
   * on purpose: a disabled control that does nothing when you click it is the
   * single most reported "bug" in this codebase's history, so the row answers. */
  const optInput = el('input', { type: 'checkbox', 'aria-label': S.voice.toggle });
  const optRow = el('div', { class: 'gg-row gg-row--check gg-voice-optin' }, [
    el('span', { class: 'gg-row-label', text: S.voice.toggle }),
    optInput,
  ]);
  const optSub = el('p', { class: 'gg-row-sub gg-voice-optsub', text: '' });

  ledger.listen(optInput, 'change', () => {
    if (!acked()) {
      // Belt and braces — a disabled input should never fire this, but a host
      // that ignores `disabled` must not be able to switch the mic on silently.
      optInput.checked = false;
      void openAck();
      return;
    }
    setEnabled(!!optInput.checked);
  });

  ledger.listen(optRow, 'click', (e) => {
    if (acked()) return;                       // the input handles itself
    if (e && e.target === optInput) return;    // a disabled input's click, ignored
    void openAck();
  });

  /* ---- recording --------------------------------------------------------- */
  const recBtn = button(ledger, S.voice.record, () => { void onRecordPress(); }, { variant: 'primary', audio });
  recBtn.classList.add('gg-voice-record');
  const recTimer = el('span', { class: 'gg-voice-timer', text: '', role: 'status' });
  const recNote = el('p', { class: 'gg-voice-recnote', text: '', hidden: true });
  const recRow = el('div', { class: 'gg-voice-recrow' }, [recBtn, recTimer]);

  /* ---- the list ---------------------------------------------------------- */
  const list = el('div', { class: 'gg-voice-list', role: 'list' });
  const emptyLine = el('p', { class: 'gg-voice-empty', text: S.voice.empty });

  /* ---- footer ------------------------------------------------------------ */
  const backBtn = button(ledger, S.voice.back, () => { actions?.goTitle?.(); }, { variant: 'ghost', audio, sfx: 'ui-back' });
  const footer = el('div', { class: 'gg-voice-foot' }, [
    el('p', { class: 'gg-voice-hint', text: S.voice.volumeHint }),
    el('div', { class: 'gg-voice-foot-actions' }, [backBtn]),
  ]);

  container.appendChild(el('div', { class: 'gg-card gg-voice' }, [
    head,
    el('div', { class: 'gg-consent gg-voice-consent' }, [optRow, optSub]),
    recRow, recNote,
    emptyLine, list,
    footer,
  ]));

  /* ------------------------------------------------------------ the ack gate */

  /**
   * The acknowledgment modal. Two paragraphs, two actions, and the second
   * paragraph is the one that matters most (it says you may HEAR them), so it
   * is rendered even when the injection below cannot find the sheet's body.
   *
   * ui/sheets.js `open()` takes ONE line; this consent needs two. Rather than
   * change a module three other screens share, the second paragraph is appended
   * to the sheet that has just been built — the same DOM seam sheets.openNode()
   * itself uses — and if that fails for any reason the two paragraphs are merged
   * into the one line instead. A safety sentence is never allowed to be the part
   * that goes missing.
   */
  async function openAck() {
    if (!sheets || typeof sheets.open !== 'function') return false;
    const promise = sheets.open({
      icon: S.voice.ack.icon,
      headline: S.voice.ack.headline,
      line: S.voice.ack.line,
      actions: [
        { id: 'cancel', label: S.voice.ack.cancel, variant: 'ghost' },
        { id: 'go', label: S.voice.ack.go, variant: 'primary' },
      ],
    });
    injectSecondLine();
    const answer = await promise;
    if (ledger.isDisposed) return false;
    if (answer !== 'go') { paintOptIn(); return false; }
    prefs?.set?.('voiceAckSeen', true);
    // Reading the modal is not the same as switching it on — but the player got
    // here by reaching for the switch, so the acknowledgment IS the switch. Any
    // other reading makes them press the same thing twice for one decision.
    setEnabled(true);
    return true;
  }

  function injectSecondLine() {
    try {
      if (typeof document === 'undefined' || !document) return;
      const modal = document.getElementById('gg-modal');
      const line = modal && modal.querySelector ? modal.querySelector('.gg-sheet-line') : null;
      if (!line) return;
      const p = el('p', { class: 'gg-sheet-line gg-voice-ack-2', text: S.voice.ack.lineTwo });
      if (line.parentNode && typeof line.parentNode.insertBefore === 'function') {
        line.parentNode.insertBefore(p, line.nextSibling);
      } else {
        line.textContent = S.voice.ack.line + ' ' + S.voice.ack.lineTwo;
      }
    } catch (e) {
      // The merge fallback, in the one place it can still be reached.
      try {
        const modal = document.getElementById('gg-modal');
        const line = modal && modal.querySelector ? modal.querySelector('.gg-sheet-line') : null;
        if (line) line.textContent = S.voice.ack.line + ' ' + S.voice.ack.lineTwo;
      } catch (_e2) { /* no DOM at all */ }
      ledger._err('ack line', e);
    }
  }

  /**
   * The opt-in, written to the ONE place that decides it (prefs) and mirrored
   * onto the match when there is one.
   *
   * The mirror matters even though this screen is reached from the title menu:
   * core/match.js only accepts the declaration in Lobby/Consent, and boot.js
   * seeds it from this same pref when a match attaches — so the pref is the
   * source of truth and this call is the "you are already in a lobby" case.
   * Flipping it there clears both signatures, which is exactly right: nobody
   * gets advanced onto a term they signed a sheet without.
   */
  function setEnabled(on) {
    const want = !!on;
    prefs?.set?.('voiceNotesEnabled', want);
    try { getMatch?.()?.setLocalVoiceNotes?.(want); } catch (e) { ledger._err('setLocalVoiceNotes', e); }
    paintOptIn();
  }

  function paintOptIn() {
    const isAcked = acked();
    const isOn = enabled();
    optInput.checked = isOn;
    optInput.disabled = !isAcked;
    optRow.classList.toggle('is-disabled', !isAcked);
    optSub.textContent = !isAcked ? S.voice.toggleLocked : (isOn ? S.voice.toggleOn : S.voice.toggleOff);
  }

  /* ------------------------------------------------------------- recording */

  /** Load the recorder module and start it. See THE RECORDER SEAM above. */
  async function openRecorder() {
    let mod = null;
    /* TEST SEAM, and the same one ui/voice/recorder.js gives itself: a factory
     * on the ctx replaces the dynamic import, so the self-tests can drive the
     * whole record→store→list path without a microphone, a MediaRecorder or a
     * dependency on a sibling wave's file. Never set in production. */
    if (ctx && typeof ctx.recorderFactory === 'function') mod = { createVoiceRecorder: ctx.recorderFactory };
    else try { mod = await import('../voice/recorder.js'); }
    catch (e) {
      // The module is not there (or threw on import). The player-visible truth
      // is the same as a machine with no input device, and S.voice.micMissing is
      // the string for it — strings.js is read-only for this wave, and inventing
      // a key for a state that only exists between two agents' commits is not
      // worth holding a file three screens share.
      logger?.info?.('[GG voice] recorder module unavailable: ' + ((e && e.message) || e));
      return { ok: false, reason: 'missing' };
    }
    let factory = null;
    for (const name of RECORDER_FACTORIES) {
      if (mod && typeof mod[name] === 'function') { factory = mod[name]; break; }
    }
    if (!factory) return { ok: false, reason: 'missing' };

    let rec = null;
    try { rec = factory({ maxMs: VN_MAX_MS, logger }); }
    catch (e) { logger?.warn?.('[GG voice] recorder factory threw: ' + ((e && e.message) || e)); return { ok: false, reason: 'missing' }; }
    if (!rec || typeof rec.start !== 'function' || typeof rec.stop !== 'function') return { ok: false, reason: 'missing' };

    let started = null;
    try { started = await rec.start(); }
    catch (e) { logger?.info?.('[GG voice] recorder.start threw: ' + ((e && e.message) || e)); started = { ok: false, reason: 'denied' }; }
    if (started && started.ok === false) {
      try { rec.dispose?.(); } catch (_e) { /* ignore */ }
      return { ok: false, reason: String(started.reason || 'denied') };
    }
    return { ok: true, rec };
  }

  async function onRecordPress() {
    if (recorder) { await finishRecording(false); return; }
    if (busy) return;
    if (!store) { toast(S.voice.micMissing, 'warn'); return; }
    if (notes.length >= VN_MAX_NOTES) { toast(S.voice.full(VN_MAX_NOTES), 'warn'); return; }

    // A new take clears the last one's footnote ("ten seconds is the lot") —
    // paintRecording puts the "library is full" line back if that is still true.
    recNote.textContent = '';
    recNote.hidden = true;

    busy = true;
    recBtn.disabled = true;
    let opened = null;
    try { opened = await openRecorder(); } finally { busy = false; recBtn.disabled = false; }
    if (ledger.isDisposed) return;
    if (!opened.ok) {
      toast(opened.reason === 'denied' ? S.voice.micDenied : S.voice.micMissing, 'warn');
      return;
    }

    const rec = opened.rec;
    recorder = rec;
    recordStartedAt = Date.now();
    paintRecording();

    // The recorder's OWN ceiling, where it has one. This is the path that
    // normally runs: it hands over a finished note rather than a stop() that
    // arrives after the fact and finds nothing left to stop.
    if (typeof rec.onCapped === 'function') {
      try {
        capOff = rec.onCapped((payload) => {
          if (ledger.isDisposed || recorder !== rec) return;   // a stop() already claimed it
          recorder = null;
          clearTick();
          void completeRecording(rec, normalizeRecording(payload), true);
        }) || null;
      } catch (e) { ledger._err('onCapped', e); capOff = null; }
    }

    // The counter, and the fallback ceiling for a recorder with none of its own.
    tickTimer = ledger.interval(() => {
      const ms = Date.now() - recordStartedAt;
      recTimer.textContent = S.voice.recordTimer(Math.min(ms, VN_MAX_MS), VN_MAX_MS);
      if (ms >= VN_MAX_MS + CAP_GRACE_MS) { void finishRecording(true); }
    }, TICK_MS);
  }

  function clearTick() {
    try { clearInterval(tickTimer); } catch (_e) { /* ignore */ }
    tickTimer = 0;
    if (capOff) { try { capOff(); } catch (_e) { /* ignore */ } capOff = null; }
  }

  async function finishRecording(capped) {
    const rec = recorder;
    if (!rec) return;
    recorder = null;
    clearTick();

    let out = { ok: false, reason: 'empty', blob: null, durMs: 0 };
    try { out = normalizeRecording(await rec.stop()); }
    catch (e) { logger?.warn?.('[GG voice] recorder.stop threw: ' + ((e && e.message) || e)); }
    await completeRecording(rec, out, capped);
  }

  /** The shared tail: release the mic, store the note, repaint. */
  async function completeRecording(rec, out, capped) {
    try { rec.dispose?.(); } catch (_e) { /* ignore */ }
    if (ledger.isDisposed) return;
    paintRecording();
    if (!out.ok) { toast(S.voice.sendFailed, 'warn'); return; }

    const durMs = out.durMs > 0 ? Math.min(VN_MAX_MS, out.durMs) : Math.min(VN_MAX_MS, Date.now() - recordStartedAt);
    const res = await store.add(out.blob, { durMs, name: nextName() });
    if (ledger.isDisposed) return;
    if (!res.ok) {
      toast(res.reason === 'full' ? S.voice.full(VN_MAX_NOTES) : S.voice.sendFailed, 'warn');
      return;
    }
    if (capped) { recNote.textContent = S.voice.recordCapped; recNote.hidden = false; }
    await refresh();
  }

  /** The smallest unused "Note N", so a delete-then-record reuses the gap. */
  function nextName() {
    const taken = new Set(notes.map((x) => x.name));
    for (let i = 1; i <= VN_MAX_NOTES + 1; i++) {
      const name = S.voice.noteName(i);
      if (!taken.has(name)) return name;
    }
    return S.voice.noteName(notes.length + 1);
  }

  function paintRecording() {
    const on = !!recorder;
    recBtn.textContent = on ? S.voice.recordStop : S.voice.record;
    recBtn.classList.toggle('is-recording', on);
    recTimer.textContent = on ? S.voice.recordTimer(0, VN_MAX_MS) : '';
    recTimer.hidden = !on;
    if (!on && notes.length >= VN_MAX_NOTES) {
      recNote.textContent = S.voice.full(VN_MAX_NOTES);
      recNote.hidden = false;
      recBtn.disabled = true;
    } else if (!on) {
      recBtn.disabled = false;
      if (recNote.textContent === S.voice.full(VN_MAX_NOTES)) { recNote.hidden = true; recNote.textContent = ''; }
    }
  }

  /* -------------------------------------------------------------- the list */

  async function refresh() {
    if (!store) { notes = []; paintList(); paintRecording(); return; }
    let next = [];
    try { next = await store.list(); } catch (e) { ledger._err('notes.list', e); next = []; }
    if (ledger.isDisposed) return;
    notes = Array.isArray(next) ? next : [];
    paintList();
    paintRecording();
  }

  function paintList() {
    list.replaceChildren();
    emptyLine.hidden = notes.length > 0;
    for (const note of notes) list.appendChild(noteRow(note));
  }

  function noteRow(note) {
    const nameEl = el('span', { class: 'gg-voice-note-name', text: note.name || S.voice.noteName(1) });
    const lenEl = el('span', { class: 'gg-chip gg-voice-note-len', text: S.voice.noteLength(note.durMs) });
    const linkEl = el('span', {
      class: 'gg-voice-note-link' + (note.emote ? ' is-linked' : ''),
      text: note.emote ? S.voice.linkedTo(note.emote) : S.voice.linkNone,
    });

    const isPlaying = playingId === note.id;
    const playBtn = button(ledger, isPlaying ? S.voice.stop : S.voice.play, () => { void onPlay(note); }, { audio });
    playBtn.classList.add('gg-voice-note-btn');
    const delBtn = button(ledger, S.voice.delete, () => { void onDelete(note); }, { variant: 'ghost', audio });
    delBtn.classList.add('gg-voice-note-btn');

    /* THE PICKER, inline rather than in a popup: six glyphs is a row, and a
     * player deciding which emote carries which note wants to see all eight
     * rows' bindings at once. The "on its own" chip is first so that unbinding
     * is as reachable as binding. */
    const picker = el('div', { class: 'gg-voice-picker', role: 'group', 'aria-label': S.voice.linkLabel }, [
      el('span', { class: 'gg-voice-picker-label', text: S.voice.linkLabel }),
    ]);

    const noneBtn = el('button', {
      type: 'button',
      class: 'gg-voice-emote is-none',
      'aria-pressed': String(!note.emote),
      text: S.voice.linkNone,
    });
    ledger.listen(noneBtn, 'click', () => { void onUnlink(note); });
    picker.appendChild(noneBtn);

    for (const icon of EMOTE_ICONS) {
      const b = el('button', {
        type: 'button',
        class: 'gg-voice-emote',
        'aria-pressed': String(note.emote === icon),
        'aria-label': icon,
        text: icon,
      });
      ledger.listen(b, 'click', () => { void onLink(note, icon); });
      picker.appendChild(b);
    }

    return el('div', { class: 'gg-voice-note' + (isPlaying ? ' is-playing' : ''), role: 'listitem' }, [
      el('div', { class: 'gg-voice-note-main' }, [nameEl, lenEl, linkEl]),
      el('div', { class: 'gg-voice-note-acts' }, [playBtn, delBtn]),
      picker,
    ]);
  }

  /* ------------------------------------------------------------- the verbs */

  /**
   * Preview, THROUGH THE VOICE BUS (audio.playVoiceNote) rather than an <audio>
   * element — so the player hears the note at exactly the volume their opponent's
   * notes will arrive at, with the master applied once, the way ui/audio.js
   * builds every other sound on the page.
   */
  async function onPlay(note) {
    if (playingId === note.id) { try { audio?.stopVoice?.(); } catch (_e) { /* stub */ } playingId = ''; paintList(); return; }
    if (!store || !audio || typeof audio.playVoiceNote !== 'function') return;
    try { audio.stopVoice?.(); } catch (_e) { /* stub */ }
    const full = await store.get(note.id);
    if (ledger.isDisposed || !full || !full.blob) return;
    playingId = note.id;
    paintList();
    // No await on the outcome for the paint: playVoiceNote resolves when the
    // note's FATE is known (it started, or it did not), and the row has to go
    // back to "play" when it ENDS, which is what onEnd is for.
    void audio.playVoiceNote(full.blob, {
      onEnd: () => {
        if (ledger.isDisposed || playingId !== note.id) return;
        playingId = '';
        paintList();
      },
    }).then((outcome) => {
      if (ledger.isDisposed) return;
      if (outcome !== 'played' && playingId === note.id) { playingId = ''; paintList(); }
    });
  }

  async function onDelete(note) {
    if (!store || busy) return;
    if (sheets && typeof sheets.open === 'function') {
      const answer = await sheets.open({
        headline: S.voice.deleteConfirm,
        line: note.name || '',
        actions: [
          { id: 'cancel', label: S.sheets.cancel, variant: 'ghost' },
          { id: 'go', label: S.voice.delete, variant: 'primary' },
        ],
      });
      if (ledger.isDisposed || answer !== 'go') return;
    }
    busy = true;
    try {
      if (playingId === note.id) { try { audio?.stopVoice?.(); } catch (_e) { /* stub */ } playingId = ''; }
      await store.remove(note.id);
    } catch (e) { ledger._err('notes.remove', e); } finally { busy = false; }
    await refresh();
  }

  async function onLink(note, icon) {
    if (!store) return;
    let res = { ok: true, moved: '' };
    try { res = store.link(icon, note.id); } catch (e) { ledger._err('notes.link', e); }
    // One note per emote: say so when the pick took an emote off another note,
    // because that other row is about to change under the player's eyes.
    if (res && res.moved) toast(S.voice.linkMoved(icon), 'info');
    await refresh();
  }

  async function onUnlink(note) {
    if (!store) return;
    try { store.unlink(note.id); } catch (e) { ledger._err('notes.unlink', e); }
    await refresh();
  }

  /* ------------------------------------------------------------ first paint */

  paintOptIn();
  paintRecording();
  emptyLine.hidden = false;

  // A pref changed somewhere else (options reset, another screen) repaints the
  // toggle — the pref is the truth and this screen is one of its readers.
  if (prefs && typeof prefs.subscribe === 'function') {
    ledger.sub(prefs.subscribe((key) => {
      if (ledger.isDisposed) return;
      if (key === 'voiceNotesEnabled' || key === 'voiceAckSeen') paintOptIn();
    }));
  }

  // Bindings can outlive the notes they point at (a database cleared behind our
  // back, a second tab). Pruning on mount means the list and the emote hook can
  // never disagree about what exists.
  if (store && typeof store.prune === 'function') { void store.prune(); }
  void refresh();

  try { audio?.music?.('title'); } catch (_e) { /* stub bus */ }
  ledger.add(() => { try { audio?.stopMusic?.(); } catch (_e) { /* stub bus */ } });

  return {
    unmount() {
      // A recording in progress dies with the screen — the mic is released and
      // the bytes are dropped. Leaving the page IS a cancel.
      const rec = recorder;
      recorder = null;
      clearTick();
      if (rec) {
        try { rec.cancel?.(); } catch (_e) { /* ignore */ }
        try { rec.dispose?.(); } catch (_e) { /* ignore */ }
      }
      try { if (playingId) audio?.stopVoice?.(); } catch (_e) { /* stub */ }
      playingId = '';
      ledger.dispose();
    },
  };
}

export default { mount };
