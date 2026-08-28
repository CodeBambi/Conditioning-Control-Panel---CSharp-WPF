/* ============================================================================
 * shell/pacaption.js - THE WORDS UNDER THE TANNOY.
 *
 * The PA pack got a voice in Counter Stock and the owner asked for the other
 * half of it (2026-08-28): "we have to add a speech bubble, like the EMI one,
 * on the bottom of the screen, visual novel style, that reads out the announcer
 * text." This is that surface, and it is the ONLY one - pa.js still renders
 * nothing, still owns no element, and still knows about exactly one bus.
 *
 * ---------------------------------------------------------------------------
 * THE LAWS
 * ---------------------------------------------------------------------------
 *  1. IT LISTENS TO THE CUE, NOT TO THE SCHEDULER. Every PA line already leaves
 *     the building as `arcademy-sfx` with a `pa_NN` name (pa.js trap 18: one
 *     audio door). This module hears the same shout the mixer hears, so a line
 *     spoken by the plan, by a purchase request, or by a test rig all get the
 *     same caption and pa.js needs no second channel to us. Nothing else in the
 *     page has to know captions exist.
 *  2. THE TEXT COMES FROM THE LEXICON, NEVER FROM HERE. The thirty-six strings
 *     are `pa_line_01`..`pa_line_36` in core/lexicon.js DEFAULT_LEXICON, in FILE
 *     order. The cue carries `detail.caption` as a courtesy (pa.js already
 *     looked it up); if it does not, we ask `t(key)` ourselves - with NO
 *     fallback argument, because a fallback beats DEFAULT_LEXICON in lexicon.js
 *     `t()` and would hide the very rows this ships.
 *  3. NO TEXT, NO BOX. A name that is not one of the thirty-six, a lexicon that
 *     has been skinned to empty, a host with no table at all: the bubble simply
 *     does not appear. A silent announcement with an empty box under it is
 *     worse than an announcement with nothing under it.
 *  4. IT DIES WITH THE LINE. audio.js answers `detail.onEnded` exactly once on
 *     every road out of a clip and pa.js re-broadcasts that as
 *     `arcademy-pa-ended`, so the caption leaves when she stops talking. The
 *     LINE_MAX_MS + tail timer underneath it is a dead man's handle, not the
 *     plan: a cue fired by something that is not pa.js (the smoke rig) has no
 *     end signal and still must not leave a box on the glass all night.
 *  5. IT NEVER EATS A TAP. `pointer-events: none` on the whole layer, always,
 *     with no exceptions - the input-trust law. A tap DISMISSES it, but the tap
 *     is heard on `document` and is not consumed, so the campus underneath gets
 *     it too. Esc is the same: heard, never stopped, so the shell's ladder is
 *     untouched.
 *  6. LITE STILL GETS THE WORDS. Text is cheap. The lite rung takes the
 *     typewriter and the crackle, not the caption - a player on a slow machine
 *     who bought the announcer still bought the announcements. Reduced motion
 *     takes the same two things for the other reason.
 *  7. SHE SITS UNDER EMI. z-index 47: over the campus and over a game plate,
 *     under the annex reveal (48) and under `.arc-emi` (50), so the mascot's own
 *     bubble is never covered by the school's. shell.js also hands this box's
 *     rect to EMI's keep-off rule so she walks around it rather than standing on
 *     it in the first place.
 *
 * ---------------------------------------------------------------------------
 * THE PUBLIC SURFACE
 * ---------------------------------------------------------------------------
 *   installPaCaption({ doc, t, log, lite, reduced }) -> handle
 *     doc      Document              defaults to the global one.
 *     t        (key)=>string         core/lexicon.js `t`. Law 2.
 *     log      (msg)=>void           the shell's `say`.
 *     lite     bool | ()=>bool       performance cap. Read at show time.
 *     reduced  bool | ()=>bool       prefers-reduced-motion. Read at show time.
 *
 *   handle.show(name)   force a caption up (the rig, and `pa.caption` parity).
 *   handle.hide()       take it down now.
 *   handle.node()       the root element, or null - shell.js wants its rect.
 *   handle.text()       what is on screen, or ''.
 *   handle.destroy()    every listener off, the layer out of the DOM.
 * ========================================================================= */

/** The cue names this surface answers to. Same shape audio.js's `isPaName`
 *  tests, spelled again rather than imported: this module must stay loadable
 *  without dragging the mixer in, exactly like pa.js. */
const PA_NAME = /^pa_\d\d$/;

/** The dead man's handle (law 4). pa.js's LINE_MAX_MS plus the tail audio.js
 *  leaves after the line, plus a beat - long enough that it never cuts a real
 *  announcement short, short enough that a rogue cue is gone within a screen. */
const MAX_ON_MS = 12000 + 900;

/** Typewriter pace. A PA line runs 5.4s to 9.7s on disk and the longest is 120
 *  characters, so ~42ms a character lands the last letter comfortably inside
 *  the shortest clip. The clamp is the whole safety story: a short line is not
 *  over before it is read, and no line is still typing when she stops. */
const CHAR_MS = 42;
const REVEAL_MIN_MS = 900;
const REVEAL_MAX_MS = 5200;

/** The sheet, linked once and lazily - prizecounter.js's pattern. */
const SHEET_ID = 'pa-cap-styles';
/** How long the box takes to rise into place. Must match the `.pa-cap-box`
 *  transform transition in pacaption.css; it is only ever used to decide when
 *  the plate has stopped moving and is safe to MEASURE. */
const RISE_MS = 280;
function sheetHref() {
  try { return new URL('./pacaption.css', import.meta.url).href; }
  catch (e) { return 'shell/pacaption.css'; }
}
/** Link the sheet, once. `onReady` is called when it has actually LANDED (or
 *  at once if it was already there), which matters more than it looks: until
 *  the sheet loads the box is an unstyled div at the end of `<body>`, so its
 *  rect is a lie - and that rect is what the shell hands to EMI's keep-off
 *  rule. Measured before the sheet, she steps aside from a phantom and then
 *  stands on the real announcement. THAT is why this file prewarms the sheet
 *  at install time instead of waiting for the first line. */
function ensureSheet(doc, log, onReady) {
  const ready = () => { if (typeof onReady === 'function') { try { onReady(); } catch (e) { /* noop */ } } };
  try {
    if (!doc || typeof doc.createElement !== 'function') return null;
    const had = typeof doc.getElementById === 'function' ? doc.getElementById(SHEET_ID) : null;
    if (had) {
      if (had.sheet) ready();
      else had.addEventListener('load', ready, { once: true });
      return had;
    }
    const link = doc.createElement('link');
    link.id = SHEET_ID;
    link.rel = 'stylesheet';
    link.href = sheetHref();
    link.addEventListener('load', ready, { once: true });
    link.addEventListener('error', ready, { once: true });
    const head = doc.head || doc.body || null;
    if (head && typeof head.appendChild === 'function') head.appendChild(link);
    return link;
  } catch (e) {
    if (typeof log === 'function') log('pa caption stylesheet failed to link');
    return null;
  }
}

/** One frame from now, with a timer as the floor for a host with no rAF (and
 *  for a tab in the background, where rAF never fires at all). */
function nextFrame(fn) {
  try {
    if (typeof requestAnimationFrame === 'function') { requestAnimationFrame(() => { try { fn(); } catch (e) { /* noop */ } }); return; }
  } catch (e) { /* fall through */ }
  try { setTimeout(fn, 32); } catch (e) { /* noop */ }
}

/** A cap that may be a value or a getter. Read EVERY time; never cached. */
function flag(v) {
  if (typeof v === 'function') { try { return v() === true; } catch (e) { return false; } }
  return v === true;
}

/** `pa_03` -> `pa_line_03`, or null. A local copy of pa.js's `captionKey` for
 *  the same reason pa.js keeps a local copy of audio.js's `paName`: this file
 *  imports nothing from the shell and can be loaded on its own. */
function keyFor(name) {
  const m = PA_NAME.exec(String(name == null ? '' : name));
  if (!m) return null;
  return 'pa_line_' + String(name).slice(3);
}

/** The house's own reduced-motion answer, the way every other shell module asks
 *  it: the root class the shell writes, then the media query underneath. */
function motionOff(doc) {
  try {
    const de = doc && doc.documentElement;
    if (de && de.classList && de.classList.contains('arc-reduced')) return true;
  } catch (e) { /* noop */ }
  try {
    if (typeof matchMedia === 'function') {
      return matchMedia('(prefers-reduced-motion: reduce)').matches === true;
    }
  } catch (e) { /* noop */ }
  return false;
}

/**
 * Build the caption surface. Always returns a handle, even on a host with no
 * document - a shell that installs this on a headless page gets an inert
 * object rather than a throw.
 *
 * @param {{doc?:Document, t?:Function, log?:Function,
 *          lite?:(boolean|Function), reduced?:(boolean|Function)}=} caps
 */
export function installPaCaption(caps) {
  const c = caps || {};
  const doc = c.doc || ((typeof document !== 'undefined') ? document : null);
  const say = (typeof c.log === 'function') ? c.log : function () {};
  const tr = (typeof c.t === 'function') ? c.t : null;

  let root = null;      // .pa-cap
  let textEl = null;    // .pa-cap-text
  let whoEl = null;     // .pa-cap-who
  let offTimer = 0;     // the dead man's handle
  let typeTimer = 0;    // the typewriter's rAF/interval id
  let settleTimer = 0;  // the re-measure after the box has finished rising
  let shown = '';       // what is on the glass, or ''
  let dead = false;

  /* PREWARM. The sheet is linked at install, minutes before the first line,
   * so that by the time a caption goes up the box measures as the band it is
   * rather than as a bare div at the end of the document. */
  ensureSheet(doc, say, () => { if (shown) changed(true); });

  function liteNow() { return flag(c.lite); }
  function reducedNow() { return flag(c.reduced) || motionOff(doc); }

  /** The words for a cue. `detail.caption` first (pa.js already paid for the
   *  lookup), then the lexicon. Law 2: `t(key)` with NO fallback. */
  function textFor(name, seeded) {
    if (typeof seeded === 'string' && seeded.length) return seeded;
    const key = keyFor(name);
    if (!key || !tr) return '';
    let s = '';
    try { s = String(tr(key) || ''); } catch (e) { return ''; }
    // lexicon.js's floor is `humanize(key)`, which for a deleted row reads
    // "Pa Line 03" and would go on screen as if it were an announcement.
    if (/^Pa Line \d+$/.test(s)) return '';
    return s;
  }

  function build() {
    if (root || !doc || typeof doc.createElement !== 'function') return root;
    ensureSheet(doc, say, () => { if (shown) changed(true); });
    const el = doc.createElement('div');
    el.className = 'pa-cap';
    el.hidden = true;
    // A caption is an announcement, so it is announced. `polite` and not
    // `assertive`: a screen reader must not be interrupted by the schedule.
    el.setAttribute('role', 'status');
    el.setAttribute('aria-live', 'polite');
    el.innerHTML = ''
      + '<div class="pa-cap-box">'
      +   '<div class="pa-cap-plate">'
      +     '<span class="pa-cap-horn" aria-hidden="true"></span>'
      +     '<span class="pa-cap-who"></span>'
      +   '</div>'
      +   '<p class="pa-cap-text"></p>'
      + '</div>';
    const body = doc.body || doc.documentElement;
    if (!body || typeof body.appendChild !== 'function') return null;
    body.appendChild(el);
    root = el;
    textEl = el.querySelector('.pa-cap-text');
    whoEl = el.querySelector('.pa-cap-who');
    return root;
  }

  function clearTimers() {
    try { if (offTimer) clearTimeout(offTimer); } catch (e) { /* noop */ }
    try { if (typeTimer) clearInterval(typeTimer); } catch (e) { /* noop */ }
    try { if (settleTimer) clearTimeout(settleTimer); } catch (e) { /* noop */ }
    offTimer = 0; typeTimer = 0; settleTimer = 0;
  }

  /* THE TYPEWRITER, AND WHY IT IS SPANS AND NOT A GROWING STRING.
   * Slicing `textContent` re-measures the box on every character, so the bubble
   * grows and the campus under it jumps a hundred times a line. Every character
   * is laid out ONCE, at full size, and then faded in one at a time: the box is
   * the right shape from the first frame and nothing reflows after it. Words
   * are kept whole (`.pa-cap-w`) so the reveal never breaks mid-word at a wrap. */
  function typeOut(s) {
    if (!textEl) return;
    textEl.textContent = '';
    const still = liteNow() || reducedNow();
    if (still) {
      // Law 6. No reveal, no crackle - the whole line, arriving on a fade.
      textEl.textContent = s;
      textEl.classList.remove('is-typing');
      return;
    }
    const frag = doc.createDocumentFragment();
    const cells = [];
    for (const word of s.split(' ')) {
      const w = doc.createElement('span');
      w.className = 'pa-cap-w';
      for (const ch of word) {
        const g = doc.createElement('span');
        g.className = 'pa-cap-c';
        g.textContent = ch;
        w.appendChild(g);
        cells.push(g);
      }
      frag.appendChild(w);
      const sp = doc.createElement('span');
      sp.className = 'pa-cap-c pa-cap-sp';
      sp.textContent = ' ';
      frag.appendChild(sp);
      cells.push(sp);
    }
    textEl.appendChild(frag);
    textEl.classList.add('is-typing');
    const total = Math.max(REVEAL_MIN_MS, Math.min(REVEAL_MAX_MS, cells.length * CHAR_MS));
    const step = Math.max(16, Math.round(total / Math.max(1, cells.length)));
    let i = 0;
    typeTimer = setInterval(() => {
      if (dead || !textEl) { clearTimers(); return; }
      // More than one cell a tick when the clamp bit, so a long line still
      // finishes on time instead of running past the audio.
      const per = Math.max(1, Math.ceil(cells.length / Math.max(1, Math.round(total / step))));
      for (let n = 0; n < per && i < cells.length; n += 1, i += 1) cells[i].classList.add('on');
      if (i >= cells.length) {
        clearTimers();
        textEl.classList.remove('is-typing');
        // The dead man's handle is re-armed here rather than lost with the
        // typewriter's timer - they share `offTimer`'s slot only by accident.
        armDeadMan();
      }
    }, step);
  }

  function armDeadMan() {
    try { if (offTimer) clearTimeout(offTimer); } catch (e) { /* noop */ }
    offTimer = setTimeout(() => { offTimer = 0; hide(); }, MAX_ON_MS);
  }

  /**
   * Put a line on the glass.
   * @param {string} name    a `pa_NN` cue name
   * @param {string=} seeded the cue's own `detail.caption`, if it carried one
   * @returns {boolean} true if a caption is now showing
   */
  function show(name, seeded) {
    if (dead) return false;
    const s = textFor(name, seeded);
    if (!s) return false;              // law 3
    if (!build()) return false;
    clearTimers();
    try { if (whoEl) whoEl.textContent = tr ? String(tr('pa_speaker') || 'Front Office') : 'Front Office'; }
    catch (e) { if (whoEl) whoEl.textContent = 'Front Office'; }
    shown = s;
    root.hidden = false;
    // The crackle is a class, so reduced motion and lite can drop it in CSS
    // without this file having two code paths for the same box.
    root.classList.toggle('is-still', liteNow() || reducedNow());
    // Re-trigger the entry animation on a back-to-back line: remove, force a
    // reflow, add. (Two lines in a row is not a thing pa.js does, but the
    // request event means it is now a thing a HOST can do.)
    root.classList.remove('is-on');
    try { void root.offsetWidth; } catch (e) { /* noop */ }
    root.classList.add('is-on');
    typeOut(s);
    armDeadMan();
    try { say('[pa-cap] ' + name); } catch (e) { /* noop */ }
    // AFTER `is-on` is on, never before: the shell's listener re-arms EMI's
    // keep-off rule and that rule measures `.pa-cap.is-on .pa-cap-box`.
    // TWICE, deliberately: once now so she starts moving inside the same tick
    // the line lands, and once a frame later because the box is still rising
    // (`transform: translateY(10px)`) and a rect taken mid-transition is 10px
    // short of where the plate finally sits. Two place() calls are cheap; a
    // mascot standing on the words is not.
    changed(true);
    nextFrame(() => { if (shown) changed(true); });
    // And once more when the .22s rise has finished. Measured mid-transition
    // the plate is up to ten pixels short, which is the difference between
    // EMI standing clear of the words and standing two pixels into them.
    settleTimer = setTimeout(() => { settleTimer = 0; if (shown) changed(true); }, RISE_MS);
    return true;
  }

  /** Told whenever the box goes up or comes down. The shell uses it to nudge
   *  EMI off the announcement - she re-places on the spot when the rule is
   *  re-armed, so without this she would only step aside at the next resize.
   *  A listener that throws is the listener's problem, never the caption's. */
  function changed(on) {
    if (typeof c.onChange !== 'function') return;
    try { c.onChange(on === true); } catch (e) { /* noop */ }
  }

  function hide() {
    const wasShowing = shown;
    clearTimers();
    shown = '';
    if (!root) return;
    root.classList.remove('is-on');
    if (wasShowing) changed(false);
    // Let the fade finish before the box leaves the layout, but never leave a
    // half-faded box behind if something shows again in the meantime.
    const was = root;
    setTimeout(() => {
      if (was && !was.classList.contains('is-on')) {
        was.hidden = true;
        const tx = was.querySelector('.pa-cap-text');
        if (tx) tx.textContent = '';
      }
    }, 240);
  }

  /* ---- the ears ---------------------------------------------------------- */

  /* THE ENDING THAT ARRIVES FIRST. audio.js's consumer is installed at BOOT and
   * this one at SHELL BUILD, so on `document`'s listener list the mixer always
   * runs ahead of us - and a cue the mixer DROPS (muted, no context, a
   * SAMPLE_ONLY name whose file never shipped) answers 'dropped' synchronously,
   * inside the very dispatch we are queued behind. pa.js's `onEnded` therefore
   * fires `arcademy-pa-ended` BEFORE our `arcademy-sfx` listener has run, and a
   * caption that only knew about time would put a box on the glass for an
   * announcement that never sounded. `detail.cueId` is the join: an ending we
   * have already been told about strikes its own cue off before it can draw.
   * The set is bounded by construction (an id is consumed the moment its cue
   * arrives) but is swept anyway, because "bounded by construction" is how a
   * leak gets written. */
  const endedIds = new Set();

  function onSfx(e) {
    const d = (e && e.detail) || {};
    // A `stop` is a control message about a held bed, never a spoken line.
    if (d.stop === true) return;
    const name = String(d.name || '');
    if (!PA_NAME.test(name)) return;
    if (d.cueId != null && endedIds.has(d.cueId)) { endedIds.delete(d.cueId); return; }
    try { show(name, typeof d.caption === 'string' ? d.caption : ''); }
    catch (err) { /* a caption must never break the cue bus */ }
  }

  /** pa.js re-broadcasts audio.js's one-shot `onEnded` verdict here. Law 4. */
  function onEnded(e) {
    const d = (e && e.detail) || {};
    if (shown) { try { hide(); } catch (err) { /* noop */ } return; }
    // Nothing on screen: this is the ending that beat its own beginning.
    if (d.cueId != null) {
      endedIds.add(d.cueId);
      if (endedIds.size > 8) endedIds.delete(endedIds.values().next().value);
    }
  }

  /** Law 5: heard, never consumed. No preventDefault, no stopPropagation, and
   *  the bubble phase so the shell's own handlers are not skipped. */
  function onPointer() { if (shown) hide(); }
  function onKey(e) {
    if (!shown) return;
    const k = e && e.key;
    if (k === 'Escape' || k === 'Esc') hide();
  }

  if (doc && typeof doc.addEventListener === 'function') {
    doc.addEventListener('arcademy-sfx', onSfx);
    doc.addEventListener('arcademy-pa-ended', onEnded);
    doc.addEventListener('pointerdown', onPointer);
    doc.addEventListener('keydown', onKey);
  }

  return {
    show: (name) => show(name, ''),
    hide,
    node: () => root,
    text: () => shown,
    destroy() {
      dead = true;
      clearTimers();
      if (doc && typeof doc.removeEventListener === 'function') {
        doc.removeEventListener('arcademy-sfx', onSfx);
        doc.removeEventListener('arcademy-pa-ended', onEnded);
        doc.removeEventListener('pointerdown', onPointer);
        doc.removeEventListener('keydown', onKey);
      }
      try { if (root && root.parentNode) root.parentNode.removeChild(root); } catch (e) { /* noop */ }
      root = null; textEl = null; whoEl = null; shown = '';
    },
  };
}

export default installPaCaption;
