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
 *   THE CAPTION. `chart.words` (the caption track L2 puts on the road) is cut into
 *   phrases, one on screen at a time, low on the glass. Every word of the phrase is
 *   in the DOM from the moment the phrase opens but only INKED when the clock
 *   reaches its own timestamp, so the line types itself at the speed the voice
 *   actually spoke it and never reflows while it does. That is the typewriter: the
 *   voice is the timer, not a timer.
 *
 *   THE PLATE. A sure trigger flies at the camera from the vanishing point, themed
 *   off race/triggerTheme.js, one at a time, a new one taking the old one's place.
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
 */
export function createCaptions(root) {
  const doc = root && root.ownerDocument;
  if (!doc) return null;
  const reduced = !!(typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches);
  const el = (cls, parent) => { const d = doc.createElement('div'); d.className = cls; parent.appendChild(d); return d; };

  const layer = el('rc-layer', root);
  const dim = el('rc-dim', layer);                 // the ink theme's 300 ms screen dip
  const plateWrap = el('rc-plates', layer);
  const cap = el('rc-cap', layer);
  const line = el('rc-line', cap);
  cap.hidden = true;

  let phrases = [];
  let shown = -1;              // which phrase is in the DOM
  let inked = -1;              // how many of its words have been inked
  let spans = [];              // the word elements of the phrase in the DOM
  let plate = null, plateTimer = 0, dimTimer = 0;

  function clearPhrase() {
    line.textContent = '';
    spans = [];
    shown = -1; inked = -1;
    cap.hidden = true;
  }

  function draw(i) {
    clearPhrase();
    const p = phrases[i];
    if (!p) return;
    for (const w of p.words) {
      const s = doc.createElement('span');
      s.className = 'rc-word';
      s.textContent = w.w;
      if (w.color) s.style.setProperty('--rc-ink', w.color);
      line.appendChild(s);
      line.appendChild(doc.createTextNode(' '));
      spans.push(s);
    }
    shown = i; inked = 0;
    cap.hidden = false;
  }

  /**
   * One frame, off the track clock. Everything below is derived from `t`, so a seek or a resume
   * needs no telling: the same second always draws the same line.
   */
  function update(t) {
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
      clearPhrase();
      return phrases.length;
    },
    update,
    showPlate,
    /** The run is over or the file was cleared: nothing of the last one stays on the glass. */
    clear() {
      clearPhrase();
      if (plateTimer) { clearTimeout(plateTimer); plateTimer = 0; }
      if (plate) { plate.remove(); plate = null; }
      if (dimTimer) { clearTimeout(dimTimer); dimTimer = 0; }
      dim.classList.remove('is-on');
    },
    dispose() { this.clear(); phrases = []; layer.remove(); },
    get phraseCount() { return phrases.length; },
    get phrases() { return phrases; },
  };
}

// self-check: node race/smoke/captions-check.mjs cuts the real caption track and drives the layer.
