/* ============================================================================
 * exec/lockCards.js — GoonElement.LockCards (4) + GoonPayloadKind.LockCard (4),
 * and the typed-phrase PRIMITIVE both this element and the QuickDraw sudden-
 * death round are built out of.
 *
 * TWO EXPORTS, TWO AUDIENCES:
 *   createLockCardView(container, opts) -> {dispose, focus}
 *       The primitive. rounds/quickDraw (sibling tier) mounts this into its own
 *       container and reads onSolved/onMistake for the race. It renders, counts
 *       keystrokes and reports; it owns NO timers, NO scoring and NO Esc.
 *   createLockCards({...}) -> the uniform renderer (see exec/flashes.js banner)
 *       The element/payload driver that mounts the primitive on #gg-stage.
 *
 * ESCAPE IS NOT OURS. Esc belongs to the global mercy ladder (protocol §11:
 * mercy is available in EVERY phase). A lock card that swallowed Esc would be a
 * card that can trap a player, so this file installs NO key handler for it — the
 * card's own way out is the dismiss button, which reports onAbandoned().
 *
 * INPUT: a real <input>, not a keydown trap. That is what makes AltGr layouts,
 * dead keys and IME composition work (the WPF lock card had to be fixed twice
 * for exactly those); composition is held until compositionend so a candidate
 * window never counts as a mistake. Paste is blocked — typing is the point.
 * ==========================================================================*/

import { sanitizeText, TEXT_MAX_CHARS } from './sanitize.js';

/** Built-in phrases. Short, neutral, duel-flavoured; typable on any layout. */
export const LOCK_PHRASES = Object.freeze([
  'i can hold on longer than you',
  'steady hands, steady breath',
  'i am not the one who breaks',
  'focus is a choice i keep making',
  'slow down and stay with it',
  'one more minute is nothing',
  'i chose this and i can take it',
  'keep still, keep counting',
]);

const clamp01 = (n) => (typeof n === 'number' && n === n ? (n < 0 ? 0 : n > 1 ? 1 : n) : 0);
const lerp = (a, b, t) => a + (b - a) * clamp01(t);
const soon = (fn, ms) => {
  const t = setTimeout(fn, Math.max(0, ms | 0));
  if (t && typeof t.unref === 'function') t.unref();
  return t;
};

/* ----------------------------------------------------------------------------
 * THE PRIMITIVE
 * -------------------------------------------------------------------------- */

/**
 * A typed-phrase lock card.
 *
 * @param {HTMLElement} container where the card is mounted (appended, not replacing)
 * @param {object} o
 * @param {string} o.phrase       the phrase to type (sanitized here regardless of source)
 * @param {number} [o.repeats=1]  how many correct completions solve the card
 * @param {boolean} [o.strict=false] exact case/whitespace matching
 * @param {(r:{mistakes:number})=>void} [o.onSolved]
 * @param {(count:number)=>void} [o.onMistake]  per wrong keystroke, with the running total
 * @param {()=>void} [o.onAbandoned]            the dismiss affordance (never Esc)
 * @returns {{dispose:()=>void, focus:()=>void}}
 */
export function createLockCardView(container, o = {}) {
  const noop = { dispose() {}, focus() {} };
  if (!container || typeof document === 'undefined') return noop;

  const phrase = sanitizeText(o.phrase, TEXT_MAX_CHARS) || LOCK_PHRASES[0];
  const repeats = Math.max(1, Math.min(9, (o.repeats | 0) || 1));
  const strict = !!o.strict;
  const onSolved = typeof o.onSolved === 'function' ? o.onSolved : null;
  const onMistake = typeof o.onMistake === 'function' ? o.onMistake : null;
  const onAbandoned = typeof o.onAbandoned === 'function' ? o.onAbandoned : null;

  let typed = 0;
  let doneRepeats = 0;
  let mistakes = 0;
  let composing = false;
  let disposed = false;
  let solved = false;

  /* ---- DOM ---------------------------------------------------------- */
  const card = document.createElement('div');
  card.className = 'gg-card gg-lock';

  const fill = document.createElement('div');
  fill.className = 'gg-lock-fill';
  card.appendChild(fill);

  const inner = document.createElement('div');
  inner.className = 'gg-lock-inner';
  card.appendChild(inner);

  const eyebrow = document.createElement('div');
  eyebrow.className = 'gg-eyebrow gg-lock-eyebrow';
  const dot = document.createElement('i');
  eyebrow.appendChild(dot);
  eyebrow.appendChild(document.createTextNode('lock card'));
  inner.appendChild(eyebrow);

  const phraseEl = document.createElement('p');
  phraseEl.className = 'gg-lock-phrase';
  const chars = [];
  for (let i = 0; i < phrase.length; i++) {
    const s = document.createElement('span');
    s.className = phrase[i] === ' ' ? 'gg-lock-ch is-space' : 'gg-lock-ch';
    s.textContent = phrase[i];
    phraseEl.appendChild(s);
    chars.push(s);
  }
  inner.appendChild(phraseEl);

  const dots = document.createElement('div');
  dots.className = 'gg-lock-dots';
  const dotEls = [];
  for (let i = 0; i < repeats; i++) {
    const d = document.createElement('span');
    d.className = 'gg-lock-dot';
    dots.appendChild(d);
    dotEls.push(d);
  }
  if (repeats > 1) inner.appendChild(dots);

  const input = document.createElement('input');
  input.className = 'gg-lock-input';
  input.type = 'text';
  input.autocomplete = 'off';
  input.autocapitalize = 'off';
  input.spellcheck = false;
  input.setAttribute('aria-label', 'type the phrase');
  inner.appendChild(input);

  const foot = document.createElement('div');
  foot.className = 'gg-lock-foot';
  const hint = document.createElement('span');
  hint.className = 'gg-lock-hint';
  hint.textContent = strict ? 'type it exactly' : 'type it to unlock';
  const mistakeEl = document.createElement('span');
  mistakeEl.className = 'gg-lock-mistakes';
  const give = document.createElement('button');
  give.type = 'button';
  give.className = 'gg-btn gg-btn--ghost';
  give.textContent = 'dismiss';
  foot.appendChild(hint);
  foot.appendChild(mistakeEl);
  foot.appendChild(give);
  inner.appendChild(foot);

  container.appendChild(card);

  /* ---- matching ----------------------------------------------------- */
  const same = (a, b) => {
    if (a === b) return true;
    if (strict) return false;
    if (/\s/.test(b)) return /\s/.test(a);       // any whitespace satisfies a space
    return String(a).toLowerCase() === String(b).toLowerCase();
  };

  function paint() {
    for (let i = 0; i < chars.length; i++) {
      const cls = phrase[i] === ' ' ? 'gg-lock-ch is-space' : 'gg-lock-ch';
      chars[i].className = i < typed ? `${cls} is-done` : (i === typed ? `${cls} is-cur` : cls);
    }
    const pct = phrase.length ? (typed / phrase.length) * 100 : 0;
    fill.style.width = `${pct.toFixed(1)}%`;
    mistakeEl.textContent = mistakes ? `${mistakes} slip${mistakes === 1 ? '' : 's'}` : '';
    for (let i = 0; i < dotEls.length; i++) dotEls[i].className = i < doneRepeats ? 'gg-lock-dot is-done' : 'gg-lock-dot';
  }

  function flashWrong() {
    card.classList.remove('is-wrong');
    void card.offsetWidth;      // restart the shake
    card.classList.add('is-wrong');
    soon(() => { if (!disposed) card.classList.remove('is-wrong'); }, 300);
  }

  function completeRepeat() {
    doneRepeats++;
    typed = 0;
    input.value = '';
    paint();
    if (doneRepeats < repeats) return;
    solved = true;
    input.disabled = true;
    card.classList.add('is-solved');
    if (onSolved) { try { onSolved({ mistakes }); } catch (_e) { /* caller's problem, not ours */ } }
  }

  function evaluate() {
    if (disposed || solved || composing) return;
    const val = String(input.value || '');
    let i = 0;
    while (i < val.length && i < phrase.length && same(val[i], phrase[i])) i++;

    if (i < val.length) {
      // Everything from i on is wrong. Count ONE mistake per wrong keystroke
      // burst (a pasted/composed run of bad chars is still one slip).
      mistakes++;
      input.value = val.slice(0, i);
      flashWrong();
      if (onMistake) { try { onMistake(mistakes); } catch (_e) { /* ignore */ } }
    }
    typed = i;
    paint();
    if (typed >= phrase.length) completeRepeat();
  }

  const onInput = () => evaluate();
  const onCompStart = () => { composing = true; };
  const onCompEnd = () => { composing = false; evaluate(); };
  const onPaste = (e) => { try { e.preventDefault(); } catch (_e) { /* ignore */ } };
  const onGive = () => { if (!disposed && onAbandoned) { try { onAbandoned(); } catch (_e) { /* ignore */ } } };

  try {
    input.addEventListener('input', onInput);
    input.addEventListener('compositionstart', onCompStart);
    input.addEventListener('compositionend', onCompEnd);
    input.addEventListener('paste', onPaste);
    give.addEventListener('click', onGive);
  } catch (_e) { /* a stub DOM without listeners still renders */ }

  paint();

  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      try {
        input.removeEventListener('input', onInput);
        input.removeEventListener('compositionstart', onCompStart);
        input.removeEventListener('compositionend', onCompEnd);
        input.removeEventListener('paste', onPaste);
        give.removeEventListener('click', onGive);
      } catch (_e) { /* ignore */ }
      try { card.remove(); } catch (_e) { /* ignore */ }
    },
    focus() {
      try { input.focus({ preventScroll: true }); } catch (_e) { try { input.focus(); } catch (_e2) { /* ignore */ } }
    },
  };
}

/* ----------------------------------------------------------------------------
 * THE RENDERER
 * -------------------------------------------------------------------------- */

/** Cue intensity -> how many repeats and how soon the next card lands. */
export function lockTuning(intensity) {
  const i = clamp01(intensity);
  return {
    repeats: 1 + Math.floor(i * 2.4),                 // 1..3
    gapMs: Math.round(lerp(45000, 12000, i)),          // between element cards
  };
}

export function createLockCards({ layers, media, audio, logger, phrases } = {}) {
  const log = logger || null;
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:lockcards] ${m}`); };

  const pool = (Array.isArray(phrases) ? phrases : [])
    .map((p) => sanitizeText(typeof p === 'string' ? p : (p && p.text), TEXT_MAX_CHARS))
    .filter((p) => p.length >= 8);                     // a 2-word phrase is not a lock card
  const draw = () => {
    const src = pool.length ? pool : LOCK_PHRASES;
    return src[(Math.random() * src.length) | 0];
  };

  const stage = () => (layers && typeof layers.get === 'function' ? layers.get('stage') : null);

  let sustained = null;      // {alive, intensity, view, timer}

  function mountElementCard(run) {
    if (!run.alive) return;
    const host = stage();
    if (!host) { run.timer = soon(() => mountElementCard(run), 2000); return; }
    const tune = lockTuning(run.intensity);
    const nextIn = () => {
      if (!run.alive) return;
      run.timer = soon(() => mountElementCard(run), lockTuning(run.intensity).gapMs);
    };
    run.view = createLockCardView(host, {
      phrase: draw(),
      repeats: tune.repeats,
      // One slip = one small buzz. The card ALREADY shakes (`is-wrong`); this is
      // the shake's ear half, at well under the solved chime's gain — a lock
      // card is a typing task, not a punishment, and a loud error tone on every
      // fat-fingered key would turn it into one.
      onMistake: () => {
        if (audio && typeof audio.sfx === 'function') { try { audio.sfx('lock-slip'); } catch (_e) { /* ignore */ } }
      },
      onSolved: () => {
        if (audio && typeof audio.sfx === 'function') { try { audio.sfx('lock-solved'); } catch (_e) { /* ignore */ } }
        const v = run.view;
        run.view = null;
        soon(() => { if (v) v.dispose(); }, 460);      // let the solved animation play
        nextIn();
      },
      onAbandoned: () => {
        const v = run.view;
        run.view = null;
        if (v) v.dispose();
        nextIn();
      },
    });
    run.view.focus();
  }

  return {
    name: 'lockCards',

    start(cue) {
      const intensity = clamp01(cue && cue.intensity);
      if (sustained) { sustained.intensity = intensity; return; }
      sustained = { alive: true, intensity, view: null, timer: 0 };
      mountElementCard(sustained);
    },

    setIntensity(v) { if (sustained) sustained.intensity = clamp01(v); },

    stop() {
      if (!sustained) return;
      sustained.alive = false;
      try { clearTimeout(sustained.timer); } catch (_e) { /* ignore */ }
      if (sustained.view) { try { sustained.view.dispose(); } catch (_e) { /* ignore */ } }
      sustained = null;
    },

    /**
     * LockCard payload. The opponent may name the phrase (payload.text); we
     * sanitize it again here and fall back to our own pool when it is empty.
     * Solved before the deadline = ENDURED (+1 charge): you beat the card.
     * Timeout or dismiss = completed — the receipt still goes out (so the
     * sender is neither refunded nor left hanging) but no charge is earned,
     * because waiting a card out is not the same as typing your way through it.
     */
    renderPayload(payload, done) {
      const p = payload || {};
      const theirs = sanitizeText(p.text, TEXT_MAX_CHARS);
      const runMs = Math.max(1000, (p.duration_ms | 0) || 30000);
      const tune = lockTuning(p.intensity !== undefined ? p.intensity : 0.5);

      let finished = false;
      let view = null;
      let endTimer = 0;
      const settle = (endured) => {
        if (finished) return;
        finished = true;
        try { clearTimeout(endTimer); } catch (_e) { /* ignore */ }
        if (view) { const v = view; view = null; try { v.dispose(); } catch (_e) { /* ignore */ } }
        if (typeof done === 'function') { try { done(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
      };

      const host = stage();
      if (!host) { settle(false); return () => settle(false); }   // nowhere to mount: receipt completed

      view = createLockCardView(host, {
        phrase: theirs || draw(),
        repeats: tune.repeats,
        onSolved: () => settle(true),
        onAbandoned: () => settle(false),
      });
      view.focus();
      endTimer = soon(() => settle(false), runMs);
      return () => settle(false);
    },
  };
}

export default createLockCards;
