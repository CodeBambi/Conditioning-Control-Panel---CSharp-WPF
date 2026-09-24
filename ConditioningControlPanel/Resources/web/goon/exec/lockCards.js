import { rollLockBounty, lockPrize } from '../core/lockBounty.js';
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
/* exec/ stays clear of ui/: the chrome is read by key (ui/strings.js S.lockCard). */
import { t } from '../core/i18n.js';
// Juice pass (2026-09-23): an unsolved card LEAVES (shrink + fade, 220 ms)
// instead of vanishing in one frame. A solved card has its own exit (fx.css).
import { fadeOut } from './motion.js';

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
  const timed = Number(o.durationMs) > 0;
  const started = Date.now();
  let pausedAt = null;
  let unobservePause = null;
  let deadline = started + (Number(o.durationMs) || 30000);
  const initialPrize = Number(o.bounty) || 0;
  let clockTimer = 0, lastTick = -1;
  const cue = (id) => { try { o.audio?.sfx?.(id); } catch (_e) { /* optional audio */ } };

  /* ---- DOM ---------------------------------------------------------- */
  const card = document.createElement('div');
  card.className = 'gg-card gg-lock';
  if (timed) {
    card.classList.add('gg-lock--bounty');
    document.documentElement.setAttribute('data-gg-lock-active', '1');
  }

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
  eyebrow.appendChild(document.createTextNode(t('gg_lockCard_eyebrow')));
  inner.appendChild(eyebrow);
  const prize = document.createElement('div');
  prize.className = 'gg-lock-prize';
  const amount = document.createElement('strong');
  amount.textContent = String(initialPrize);
  prize.appendChild(amount);
  for (let i = 0; i < 2; i++) {
    const chain = document.createElement('i');
    chain.className = 'gg-lock-chain gg-lock-chain--' + i;
    prize.appendChild(chain);
  }
  const instruction = document.createElement('small');
  instruction.textContent = t('gg_lockCard_unlock');
  prize.appendChild(instruction);
  if (timed) inner.appendChild(prize);

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
  input.setAttribute('aria-label', t('gg_lockCard_typePhrase'));
  inner.appendChild(input);

  const foot = document.createElement('div');
  foot.className = 'gg-lock-foot';
  const hint = document.createElement('span');
  hint.className = 'gg-lock-hint';
  hint.textContent = strict ? t('gg_lockCard_exact') : t('gg_lockCard_unlock');
  const mistakeEl = document.createElement('span');
  mistakeEl.className = 'gg-lock-mistakes';
  const give = document.createElement('button');
  give.type = 'button';
  give.className = 'gg-btn gg-btn--ghost';
  give.textContent = t('gg_lockCard_dismiss');
  foot.appendChild(hint);
  foot.appendChild(mistakeEl);
  if (!timed) foot.appendChild(give);
  const clock = document.createElement('span');
  clock.className = 'gg-lock-clock';
  if (timed) foot.prepend(clock);
  inner.appendChild(foot);

  container.appendChild(card);
  if (timed) cue('lock-in');
  function tick() {
    if (disposed || solved || pausedAt !== null) return;
    const left = Math.max(0, deadline - Date.now());
    clock.textContent = Math.ceil(left / 1000) + 's';
    const urgent = left <= 8000;
    card.classList.toggle('is-urgent', urgent);
    card.style.setProperty('--gg-lock-beat', Math.max(240, left / 30) + 'ms');
    const beat = Math.floor((Date.now() - started) / (urgent ? 300 : 1000));
    if (beat !== lastTick) { lastTick = beat; cue('lock-tick'); }
    if (!left) { if (onAbandoned) onAbandoned(); return; }
    clockTimer = soon(tick, 100);
  }

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
    if (timed && Date.now() >= deadline) { if (onAbandoned) onAbandoned(); return; }
    solved = true;
    clearTimeout(clockTimer);
    cue('lock-solved');
    input.disabled = true;
    card.classList.add('is-solved');
    if (timed) {
      for (let i = 0; i < 12; i++) {
        const spark = document.createElement('i');
        spark.className = 'gg-lock-win-spark';
        spark.style.setProperty('--a', (i * 30) + 'deg');
        prize.appendChild(spark);
      }
    }
    if (onSolved) { try { onSolved({ mistakes, prize: lockPrize(initialPrize, mistakes) }); } catch (_e) { /* caller's problem, not ours */ } }
  }

  function evaluate() {
    if (disposed || solved || composing || pausedAt !== null) return;
    if (timed && Date.now() >= deadline) { if (onAbandoned) onAbandoned(); return; }
    cue('lock-type');
    const val = String(input.value || '');
    let i = 0;
    while (i < val.length && i < phrase.length && same(val[i], phrase[i])) i++;

    if (i < val.length) {
      // Everything from i on is wrong. Count ONE mistake per wrong keystroke
      // burst (a pasted/composed run of bad chars is still one slip).
      mistakes++;
      input.value = val.slice(0, i);
      flashWrong();
      amount.textContent = String(lockPrize(initialPrize, mistakes));
      cue('lock-slip');
      const wrong = chars[i];
      soon(() => { if (!disposed && wrong) wrong.classList.add('is-error'); }, 0);
      soon(() => { if (wrong) wrong.classList.remove('is-error'); }, 280);
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

  // A duel temporarily owns the field. Keep the typed prefix and freeze the deadline.
  function pause(on) {
    if (disposed || (pausedAt !== null) === on) return;
    if (on) {
      pausedAt = Date.now();
      clearTimeout(clockTimer);
      card.style.display = 'none';
      input.disabled = true;
      document.documentElement.removeAttribute('data-gg-lock-active');
    } else {
      deadline += Date.now() - pausedAt;
      pausedAt = null;
      card.style.display = '';
      input.disabled = solved;
      document.documentElement.setAttribute('data-gg-lock-active', '1');
      if (!solved) { tick(); input.focus({ preventScroll: true }); }
    }
  }
  if (timed && typeof o.observePause === 'function') unobservePause = o.observePause(pause);
  if (timed) tick();

  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      unobservePause?.();
      clearTimeout(clockTimer);
      if (timed) document.documentElement.removeAttribute('data-gg-lock-active');
      try {
        input.removeEventListener('input', onInput);
        input.removeEventListener('compositionstart', onCompStart);
        input.removeEventListener('compositionend', onCompEnd);
        input.removeEventListener('paste', onPaste);
        give.removeEventListener('click', onGive);
      } catch (_e) { /* ignore */ }
      // The card's own timing is already over (dispose IS the end); the exit
      // below is visual only and removes the node when it lands.
      const gone = () => { try { card.remove(); } catch (_e) { /* ignore */ } };
      const out = fadeOut(card);
      if (out) {
        try { input.disabled = true; } catch (_e) { /* ignore */ }
        try { out.addEventListener('finish', gone, { once: true }); } catch (_e) { gone(); }
        soon(gone, 400);
      } else gone();
    },
    focus() {
      if (pausedAt !== null) return;
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
    gapMs: Math.round(lerp(120000, 75000, i)),          // between element cards
  };
}

export function createLockCards({ layers, media, audio, logger, phrases, getLead = () => 0, onBounty = null, observePause = null } = {}) {
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

  const views = new Set();
  function mountView(host, options) {
    const view = createLockCardView(host, { ...options, observePause });
    views.add(view);
    const dispose = view.dispose;
    view.dispose = () => { views.delete(view); dispose(); };
    return view;
  }

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
    if (views.size || document.documentElement.getAttribute('data-gg-lock-active')) { run.timer = soon(() => mountElementCard(run), 2000); return; }
    run.view = mountView(host, {
      durationMs: 30000, bounty: rollLockBounty(getLead()), audio,
      phrase: draw(),
      repeats: tune.repeats,
      onSolved: ({ prize }) => {
        if (onBounty) onBounty(prize);
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
      const settle = (endured) => {
        if (finished) return;
        finished = true;
        if (view) { const v = view; view = null; try { v.dispose(); } catch (_e) { /* ignore */ } }
        if (typeof done === 'function') { try { done(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
      };

      const host = stage();
      if (!host) { settle(false); return () => settle(false); }   // nowhere to mount: receipt completed

      if (views.size || document.documentElement.getAttribute('data-gg-lock-active')) { settle(false); return () => {}; }
      view = mountView(host, {
        durationMs: runMs, bounty: rollLockBounty(getLead()), audio,
        phrase: theirs || draw(),
        repeats: tune.repeats,
        onSolved: ({ prize }) => { if (onBounty) onBounty(prize); settle(true); },
        onAbandoned: () => settle(false),
      });
      view.focus();
      // The view owns the deadline, including time suspended behind a duel.
      return () => settle(false);
    },
  };
}

export default createLockCards;
