/* ============================================================================
 * race/captions.js - the voice on the glass: the caption line and the zoom plate.
 *
 * The owner's ask, on the levels build: "i want the text to be displayed live as
 * captions when the words are being spoken, you can start the typewrite effect to
 * the corresponding timestamp for that phrase on the script we got. this text
 * should be visible but fleeting and the triggers should be shown with a zooming
 * animation (like they come towards the pov) and more visible and big, themed
 * animations for each trigger."
 *
 * Two layers, one file, because they are one idea: the road says the word, the
 * glass says the word, and the plate says it loudly.
 *
 *   THE FLASH (`mode: 'flash'`, what a worded road runs). The words are on the ROAD
 *   now, one to a bubble (race/wordBubbles.js), so the band is no longer where the
 *   script is read: it is where a POP is answered. The word the kart just took hits
 *   the band whole, holds FLASH_HOLD_MS, fades over FLASH_FADE_MS, and a pop inside
 *   FLASH_JOIN_MS of the last one joins the same line, so a line driven clean reads
 *   back as the sentence that was said. A word the kart drove past writes itself as
 *   a GHOST, grey and faint: the script is never hostage to the steering.
 *
 *   THE CAPTION (`mode: 'type'`, behind `?cap=type`). The old typewriter: `chart.words`
 *   cut into phrases, one on screen at a time, every word in the DOM from the moment
 *   the phrase opens but only INKED when the clock reaches its own timestamp, so the
 *   line types itself at the speed the voice actually spoke it and never reflows
 *   while it does. The voice is the timer, not a timer.
 *
 *   THE PLATE. A sure trigger flies at the camera from the vanishing point, themed
 *   off race/triggerTheme.js, one at a time, a new one taking the old one's place.
 *   It owns the glass while it flies: the plate clears the band under it.
 *
 * EVERYTHING IS A FUNCTION OF THE CLOCK. `update(t)` reads the track second and
 * nothing else: no elapsed frames, no timers of its own. A seek, a pause, a resume
 * and a chart swapped in under the run all land right for free, because there is no
 * state to get out of step. The only thing with a timer is the plate, which is a
 * one shot animation and belongs to no second in particular.
 *
 * `buildPhrases` is pure and node-clean; the layer wants a DOM.
 * ==========================================================================*/

import { themeFor } from './triggerTheme.js';

/** A silence longer than this between two words ends the phrase. */
export const GAP_SEC = 0.7;
/** And so does the ninth word, whatever the voice was doing. */
export const MAX_WORDS = 9;
/** A phrase holds this long after its last word ends, then fades. */
export const HOLD_SEC = 1.0;
/** It opens this early, so a line is never half drawn when its first word lands. */
export const LEAD_SEC = 0.12;
/** And it is off the glass this long before the next one opens: one phrase at a time. */
export const CLEAR_SEC = 0.15;
/** A word that ends a sentence ends the phrase with it. */
export const END_PUNCT = /[.?!]["')\]]?$/;
/** The plate: the zoom, the hold, and the fade, in milliseconds (race.css runs the same numbers). */
export const PLATE_MS = 1400;

/** THE FLASH. A popped word stands on the band this long before it starts to go. */
export const FLASH_HOLD_MS = 500;
/** And it takes this long to go (race.css `.rc-cap.is-flash` runs the same number). */
export const FLASH_FADE_MS = 300;
/** A pop this soon after the last one JOINS its line, so a clean line reads back as the sentence. */
export const FLASH_JOIN_MS = 300;
/** A word the kart drove past: the ghost's own opacity (race.css `.rc-flash-word.is-ghost`). */
export const GHOST_ALPHA = 0.35;

/** Two lines and no more. A third is shrunk into the two, never cut off the glass. */
export const MAX_LINES = 2;
/** The band sits this far under the chrome above it (the score plate, the sound / pause buttons). */
export const CAP_GAP = 8;
/** And the plate keeps this much air off the band above it and the toast rail below it. */
export const PLATE_GAP = 8;
/** The plate's peak scale: race.css rcZoom holds it here. */
export const PLATE_PEAK = 1.35;
/** What the roomiest theme's box costs over its own font size (the ink card's padding). */
const PLATE_ROOM = PLATE_PEAK * 1.08;
/** The steps the caption may take down to fit a phrase that wanted a third line into two. */
const FIT_STEPS = [1, 0.89, 0.78, 0.67, 0.58];
/** The plate never shrinks past this, whatever the air says: below it there is no point. */
const PLATE_MIN_PX = 26;

const num = (v, d = 0) => (Number.isFinite(Number(v)) ? Number(v) : d);

/**
 * Cut a caption track into phrases.
 * @param words `chart.words`: [{ t, d, w }] sorted by t
 * @returns [{ t0, t1, tStart, tEnd, words: [{ t, d, w }] }], no two windows overlapping
 */
export function buildPhrases(words) {
  const list = (Array.isArray(words) ? words : [])
    .filter((w) => w && typeof w.w === 'string' && w.w !== '' && Number.isFinite(Number(w.t)))
    .map((w) => ({ t: Math.max(0, num(w.t)), d: Math.max(0, num(w.d)), w: w.w }))
    .sort((a, b) => a.t - b.t);

  const out = [];
  let cur = [];
  const flush = () => {
    if (!cur.length) return;
    const last = cur[cur.length - 1];
    out.push({ t0: cur[0].t, t1: last.t + last.d, tStart: 0, tEnd: 0, words: cur });
    cur = [];
  };
  for (const w of list) {
    if (cur.length) {
      const prev = cur[cur.length - 1];
      if (w.t - (prev.t + prev.d) > GAP_SEC) flush();
    }
    cur.push(w);
    if (cur.length >= MAX_WORDS || END_PUNCT.test(w.w)) flush();
  }
  flush();

  // The windows. A phrase hangs on past its last word, but never into the next one's opening:
  // one phrase at a time is the whole point, and a caption that crossfades with another is noise.
  for (let i = 0; i < out.length; i++) {
    const p = out[i], next = out[i + 1];
    p.tEnd = next ? Math.max(p.t1, Math.min(p.t1 + HOLD_SEC, next.t0 - CLEAR_SEC)) : p.t1 + HOLD_SEC;
  }
  for (let i = 0; i < out.length; i++) {
    const p = out[i];
    p.tStart = Math.max(0, p.t0 - LEAD_SEC);
    if (i > 0) p.tStart = Math.max(p.tStart, out[i - 1].tEnd);
  }
  return out;
}

/**
 * The colour every caption word is inked in: a word inside a trigger event's span wears that
 * trigger's own colour, so the phrase itself shows you which of its words is the one that counts.
 * @param phrases from buildPhrases
 * @param events the chart's events (only `trigger` ones are read)
 */
export function paintTriggers(phrases, events) {
  const spans = [];
  for (const e of Array.isArray(events) ? events : []) {
    if (!e || e.kind !== 'trigger') continue;
    const row = themeFor(e);
    if (!row) continue;
    spans.push({ t0: num(e.t), t1: num(e.t) + Math.max(0.25, num(e.dur, 0.6)), color: row.color });
  }
  if (!spans.length) return phrases;
  for (const p of phrases) {
    for (const w of p.words) {
      const mid = w.t + w.d / 2;
      const hit = spans.find((s) => mid >= s.t0 - 0.15 && mid <= s.t1 + 0.15);
      if (hit) w.color = hit.color;
    }
  }
  return phrases;
}

/** The last phrase whose window has opened at `t`, by bisection. -1 before the first one. */
function phraseAt(phrases, t) {
  let lo = 0, hi = phrases.length - 1, found = -1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if (phrases[mid].tStart <= t) { found = mid; lo = mid + 1; } else hi = mid - 1;
  }
  if (found < 0) return -1;
  return t < phrases[found].tEnd ? found : -1;
}

/**
 * The layer. `root` is the `.race-hud` div; this sits above `.rh-chrome` so a caption is never
 * under the score plate, and it is click-through like the rest of the chrome.
 *
 * NOTHING BELOW GUESSES AT A BOX. The owner's phone cut the caption's second line in half and
 * dropped the zoom plate straight onto a jackpot toast, and both were the same mistake: three
 * layers guessing at each other's heights in CSS, on a phone that quietly inflates small text so
 * that an `em` height holds fewer lines than it reads. So the layer measures the chrome it has to
 * live between, and publishes what it found for the stylesheet to place things by:
 *   --rc-cap-t     the top of the caption band, under the score plate and the top buttons
 *   --rc-plate-y   the plate's resting centre, in the air between that band and the toast rail
 *   --rc-plate-fs  how big the plate may be and still fit that air, whatever its theme adds
 * and `--rc-cap-b` on the hud root, which is where hud.js parks the act ribbon out of the way.
 */
export function createCaptions(root, opts = {}) {
  const doc = root && root.ownerDocument;
  if (!doc) return null;
  /** 'flash' (the pops write the band) or 'type' (the old typewriter, behind `?cap=type`). */
  const mode = (opts && opts.mode) === 'type' ? 'type' : 'flash';
  const reduced = !!(typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches);
  const el = (cls, parent) => { const d = doc.createElement('div'); d.className = cls; parent.appendChild(d); return d; };

  const layer = el('rc-layer', root);
  const dim = el('rc-dim', layer);                 // the ink theme's 300 ms screen dip
  const plateWrap = el('rc-plates', layer);
  const cap = el('rc-cap', layer);
  const line = el('rc-cap-line', cap);
  cap.hidden = true;

  let phrases = [];
  let shown = -1;              // which phrase is in the DOM
  let inked = -1;              // how many of its words have been inked
  let spans = [];              // the word elements of the phrase in the DOM
  let plate = null, plateTimer = 0, dimTimer = 0;

  // ---- the measured layout: see the header of this function ----
  const win = doc.defaultView || (typeof window === 'object' ? window : null);
  let capT = -1, plateY = -1, plateFs = -1, bandBot = 0;

  /** The lowest edge of the chrome the band has to clear: the score plate, and the top buttons. */
  function chromeBottom() {
    let y = 0;
    for (const sel of ['.rh-score-wrap', '.rt-mute', '.rt-pause']) {
      const n = root.querySelector(sel);
      if (!n) continue;
      const r = n.getBoundingClientRect();
      if (r.height > 0) y = Math.max(y, r.bottom);
    }
    return y;
  }

  /**
   * How many lines the phrase is actually drawing, counted off the word boxes themselves: one
   * distinct top per line, whatever the font or the phone's own idea of small text. (A Range's
   * rects are not the answer: the spaces between the words draw their own shorter boxes at their
   * own tops, so a two line phrase counts as four.)
   */
  function lineCount() {
    const tops = new Set();
    for (const s of spans) {
      const b = s.getBoundingClientRect ? s.getBoundingClientRect() : null;
      if (b && b.height > 0) tops.add(Math.round(b.top));
    }
    return tops.size || 1;          // a stub DOM with no layout: the CSS size stands
  }

  /**
   * Two lines and no more, and never half of one. A phrase that wants a third line steps its own
   * size down until it does not, and the steps are judged off the RENDERED line boxes rather than
   * off the font size, because a phone inflates small text and an `em` height does not know it.
   */
  function fit() {
    for (let i = 0; i < FIT_STEPS.length; i++) {
      line.style.setProperty('--rc-fs', String(FIT_STEPS[i]));
      if (lineCount() <= MAX_LINES) return FIT_STEPS[i];
    }
    return FIT_STEPS[FIT_STEPS.length - 1];
  }

  /** Where the band sits, where the plate rests, and how big the plate may be. Cheap, idempotent. */
  function measure() {
    const vw = (win && win.innerWidth) || 390, vh = (win && win.innerHeight) || 844;
    const t = Math.round((chromeBottom() || vh * 0.02) + CAP_GAP);
    if (t !== capT) { capT = t; layer.style.setProperty('--rc-cap-t', `${t}px`); }
    // the band is always two lines tall whether or not a phrase is up, so the plate under it
    // never hops about between a spoken line and a silence
    let lh = 0;
    try { lh = parseFloat(win ? win.getComputedStyle(line).lineHeight : '') || 0; } catch (e) { lh = 0; }
    if (lh <= 0) lh = 23;
    if (spans.length) {                       // never smaller than what the phrase really drew
      const n = lineCount(), box = line.getBoundingClientRect();
      if (n > 0 && box.height > 0) lh = Math.max(lh, box.height / n);
    }
    bandBot = t + Math.round(lh * MAX_LINES);
    root.style.setProperty('--rc-cap-b', `${bandBot}px`);
    const rail = root.querySelector('.rh-toasts');
    const railTop = rail ? rail.getBoundingClientRect().top : vh * 0.34;
    const a0 = bandBot + PLATE_GAP, a1 = Math.max(a0 + 32, railTop - PLATE_GAP);
    const y = Math.round((a0 + a1) / 2);
    if (y !== plateY) { plateY = y; layer.style.setProperty('--rc-plate-y', `${y}px`); }
    const f = Math.round(Math.max(PLATE_MIN_PX, Math.min(vw * (vw >= 700 ? 0.07 : 0.12), (a1 - a0) / PLATE_ROOM)));
    if (f !== plateFs) { plateFs = f; layer.style.setProperty('--rc-plate-fs', `${f}px`); }
  }

  const onResize = () => { capT = -1; plateY = -1; plateFs = -1; measure(); };
  if (win && win.addEventListener) win.addEventListener('resize', onResize, { passive: true });
  measure();

  function clearPhrase() {
    line.textContent = '';
    spans = [];
    shown = -1; inked = -1;
    cap.hidden = true;
  }

  // ---- THE FLASH: the word the kart just took, on the band ------------------
  // Nothing here reads the track clock, and it must not: a pop is a thing the PLAYER did, at the
  // wall's own second, and the band answers it at that second. (The typewriter above is the exact
  // opposite and for the exact opposite reason: it says what the VOICE is doing, so it is a
  // function of the file's clock and of nothing else.)
  let flashN = 0, flashAt = 0, flashOut = false, holdTimer = 0, goneTimer = 0;
  const nowMs = () => (win && win.performance && win.performance.now ? win.performance.now() : Date.now());

  function clearFlash() {
    if (holdTimer) { clearTimeout(holdTimer); holdTimer = 0; }
    if (goneTimer) { clearTimeout(goneTimer); goneTimer = 0; }
    flashN = 0; flashOut = false;
    cap.classList.remove('is-flash');
    cap.style.removeProperty('--rc-out');
    clearPhrase();
  }

  /** The hold is up: let the line go over FLASH_FADE_MS, then take it off the glass. */
  function fadeFlash() {
    holdTimer = 0; flashOut = true;
    cap.style.setProperty('--rc-out', '0');
    goneTimer = setTimeout(() => { goneTimer = 0; clearFlash(); }, FLASH_FADE_MS + 40);
  }

  /**
   * One word on the band, whole, now.
   * @param text the word the bubble wore (a merged bubble's two words are one string)
   * @param o { ghost, accent, ink }: a word the kart drove past is grey and faint; an accent word
   *          is bigger, in the ink of the set the road says this second belongs to, and glows.
   * @returns the span, or null when this build is not the one that flashes
   */
  function showWord(text, o = {}) {
    if (mode !== 'flash') return null;
    const said = String(text == null ? '' : text).trim();
    if (!said) return null;
    const at = nowMs();
    // a fresh line: the first pop, one after the line began to go, or one the last line cannot hold
    if (!flashN || flashOut || at - flashAt > FLASH_JOIN_MS) clearFlash();
    if (goneTimer) { clearTimeout(goneTimer); goneTimer = 0; }
    flashOut = false;
    const s = doc.createElement('span');
    s.className = 'rc-cap-word rc-flash-word is-said'
      + (o.ghost ? ' is-ghost' : (o.accent ? ' is-accent' : ''))
      + (reduced ? ' is-still' : '');
    s.textContent = said;
    if (o.ink && !o.ghost) s.style.setProperty('--rc-ink', o.ink);
    const add = () => { line.appendChild(s); line.appendChild(doc.createTextNode(' ')); };
    add();
    spans.push(s); flashN++;
    cap.hidden = false;
    cap.classList.add('is-flash');
    cap.style.setProperty('--rc-out', '1');
    // MAX_LINES and not one more. A word that spilled the line into a third row does not get
    // shrunk to fit (the sentence is already read; this is the answer to a pop): it starts again.
    if (flashN > 1 && lineCount() > MAX_LINES) {
      line.textContent = ''; spans = [s]; flashN = 1;
      add();
    }
    flashAt = at;
    if (holdTimer) clearTimeout(holdTimer);
    holdTimer = setTimeout(fadeFlash, FLASH_HOLD_MS);
    measure();      // the plate under the band goes wherever this left room
    return s;
  }

  function draw(i) {
    clearPhrase();
    const p = phrases[i];
    if (!p) return;
    for (const w of p.words) {
      const s = doc.createElement('span');
      s.className = 'rc-cap-word';
      s.textContent = w.w;
      if (w.color) s.style.setProperty('--rc-ink', w.color);
      line.appendChild(s);
      line.appendChild(doc.createTextNode(' '));
      spans.push(s);
    }
    shown = i; inked = 0;
    cap.hidden = false;
    fit();          // two lines, measured off what the phrase actually drew
    measure();      // and the plate under it goes wherever that left room
  }

  /**
   * One frame, off the track clock. Everything below is derived from `t`, so a seek or a resume
   * needs no telling: the same second always draws the same line.
   */
  function update(t) {
    if (mode !== 'type') return;   // the flash is answered by the pops, not typed by the second
    if (!phrases.length) { if (shown >= 0) clearPhrase(); return; }
    const sec = num(t, 0);
    const i = phraseAt(phrases, sec);
    if (i !== shown) {
      if (i < 0) { clearPhrase(); return; }
      draw(i);
    }
    const p = phrases[shown];
    if (!p) return;
    // ink every word the voice has reached, and un-ink the ones a seek put back in the future
    let n = 0;
    while (n < p.words.length && p.words[n].t <= sec) n++;
    if (n !== inked) {
      for (let k = 0; k < spans.length; k++) spans[k].classList.toggle('is-said', k < n);
      inked = n;
    }
    // the word being spoken right now carries the light
    for (let k = 0; k < spans.length; k++) {
      const w = p.words[k];
      spans[k].classList.toggle('is-now', sec >= w.t && sec < w.t + Math.max(0.12, w.d));
    }
    // fleeting: the line lets go over its last second rather than blinking out
    const left = p.tEnd - sec;
    cap.style.setProperty('--rc-out', String(Math.max(0, Math.min(1, left / 0.45))));
  }

  /** A sure trigger, flying at the camera. One at a time: a new one takes the old one's place. */
  function showPlate(event) {
    const row = themeFor(event);
    const text = (event && typeof event.label === 'string' ? event.label : '').trim();
    if (!row || !text) return null;
    clearFlash();   // the plate owns the glass: the band under it goes quiet for the flight
    measure();      // it lands in the air the band and the toast rail left, never on either
    if (plateTimer) { clearTimeout(plateTimer); plateTimer = 0; }
    if (plate) plate.remove();
    plate = doc.createElement('div');
    plate.className = `rc-plate rc-plate--${row.theme}${reduced ? ' is-still' : ''}`;
    plate.setAttribute('data-theme', row.theme);
    plate.setAttribute('data-preset', row.preset);
    plate.style.setProperty('--rc-glow', row.color);
    plate.style.setProperty('--rc-ink', row.ink);
    const word = doc.createElement('span');
    word.className = 'rc-plate-word';
    word.textContent = text;
    plate.appendChild(word);
    const sparks = doc.createElement('span');   // only the gold row makes anything of these
    sparks.className = 'rc-plate-sparks';
    plate.appendChild(sparks);
    plateWrap.appendChild(plate);
    plateTimer = setTimeout(() => { plateTimer = 0; if (plate) { plate.remove(); plate = null; } }, PLATE_MS + 120);
    // the ink row takes the room away with it for a beat
    if (row.theme === 'ink' && !reduced) {
      if (dimTimer) clearTimeout(dimTimer);
      dim.classList.remove('is-on'); void dim.offsetWidth; dim.classList.add('is-on');
      dimTimer = setTimeout(() => { dim.classList.remove('is-on'); dimTimer = 0; }, 340);
    }
    return plate;
  }

  return {
    /** The loaded chart, or null to go quiet. Reads `words` and the trigger events, nothing else. */
    setTrack(chart) {
      phrases = chart ? paintTriggers(buildPhrases(chart.words), chart.events) : [];
      clearFlash();
      // hud.js reads this: with words on the glass the act ribbon's old spot under the score
      // plate belongs to the caption band, so the ribbon takes the clear air lower down instead
      root.classList.toggle('has-rc-cap', phrases.length > 0);
      measure();
      return phrases.length;
    },
    update,
    showPlate,
    showWord,
    /** Which half of this file is driving the band: 'flash' (the pops) or 'type' (the clock). */
    get mode() { return mode; },
    /** How many words the flash line is holding right now. The smoke reads it; the game does not. */
    get flashWords() { return mode === 'flash' ? flashN : 0; },
    /** The run is over or the file was cleared: nothing of the last one stays on the glass. */
    clear() {
      clearFlash();
      if (plateTimer) { clearTimeout(plateTimer); plateTimer = 0; }
      if (plate) { plate.remove(); plate = null; }
      if (dimTimer) { clearTimeout(dimTimer); dimTimer = 0; }
      dim.classList.remove('is-on');
    },
    dispose() {
      this.clear(); phrases = []; layer.remove();
      root.classList.remove('has-rc-cap');
      if (win && win.removeEventListener) win.removeEventListener('resize', onResize);
    },
    /** Re-read the chrome: the run calls nothing, the layer does it itself on a resize and a draw. */
    measure,
    get phraseCount() { return phrases.length; },
    get phrases() { return phrases; },
    /** What the last measure() found, so a check can hold the boxes against it. */
    get band() { return { top: capT, bottom: bandBot, plateY, plateFs }; },
  };
}

// self-check: node race/smoke/captions-check.mjs cuts the real caption track, pops words at the
// band and drives the typewriter behind ?cap=type.
