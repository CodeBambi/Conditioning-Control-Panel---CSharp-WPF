/* ============================================================================
 * games/daily-trigger/index.js - DAILY TRIGGER (Wordle, the fixed homeroom).
 *
 * One board a day, six rows, and the answer is the SAME for every player on
 * earth: it is derived in bank.js from the UTC date alone (never from the tier,
 * never from a shared rng stream), so homeroom is a global ritual, a retake
 * replays the identical script, and the emoji share grid is comparable.
 *
 * The ladder is the difficulty: every wrong row climbs one deterministic rung of
 * the Distraction Engine (ladder.js). Grade tiers raise the effect dials FIRST -
 * Year 4 opens at the top rung on row 1 - and only then classic difficulty
 * (forced hard mode). Solving absorbs the word in a ceremony; failing hands it
 * back inverted as detention FLAVOUR (strings only, DECISIONS #2) and mints a
 * study hint for tomorrow.
 *
 * WHAT THIS FILE MAY NOT DO (contract, not taste): import another game, touch
 * bridge.js, grade itself, re-expose a global setting, call ctx.endClass twice,
 * or fire an effect outside `manifest.effectsConsumed` (the shell's engine handle
 * refuses and logs). Honesty laws it MUST keep: glitch_swap moves keycap GLYPHS
 * only - never a hitbox, never the key under the finger - and marks are never
 * repainted by an effect.
 *
 * Files: bank.js (pools + the daily draw), board.js (marking rules, pure),
 * ladder.js (rung -> engine), styles.js (.g-dt-* stylesheet), words-*.js (bank).
 * ==========================================================================*/

import { injectStyles } from './styles.js';
import {
  dailyEntry, dateFromSeed, isAcceptable, pollutionWords, isThemeWord, REJECT,
} from './bank.js';
import {
  markGuess, isSolved, isNearMiss, hitCount, foldKeyboard, hardModeViolation,
  layoutRows, autoLayout, cellPlan, HIT,
} from './board.js';
import { createLadder, tierStartFor, rungCapFor } from './ladder.js';
import { makeTaggedRoll } from '../../core/rng.js';

/** Six rows, always, at every tier - the share grid must stay comparable. */
export const ROWS = 6;

/** Variable-ratio base chance for the jackpot absorb, by tier (Intake's .30-.60). */
const VR_BY_TIER = Object.freeze([0.30, 0.40, 0.50, 0.60]);

/** Composite bands. The letters come from core/grades.js; these are its inputs. */
const BANDS = Object.freeze({
  S: [0.92, 1.00], A: [0.75, 0.92], B: [0.50, 0.75], C: [0.00, 0.50],
});
const BAND_ORDER = Object.freeze(['S', 'A', 'B', 'C']);

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

/** Reduced motion: the shell stamps <html class="arc-reduced">; fall back to the probe. */
function probeReduced() {
  try {
    const root = document.documentElement;
    if (root && root.classList && root.classList.contains('arc-reduced')) return true;
  } catch (e) { /* noop */ }
  try {
    if (typeof window !== 'undefined' && window.matchMedia) {
      return !!window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }
  } catch (e) { /* noop */ }
  return false;
}

/** Coarse pointer / touch. ctx carries no platform flags, so probe defensively. */
function probeCoarse() {
  try {
    if (typeof window !== 'undefined' && window.matchMedia
      && window.matchMedia('(pointer:coarse)').matches) return true;
  } catch (e) { /* noop */ }
  try {
    if (typeof navigator !== 'undefined' && Number(navigator.maxTouchPoints) > 0) return true;
  } catch (e) { /* noop */ }
  return false;
}

export default {
  key: 'daily_trigger',
  family: 'word',
  meaty: false,
  flagship: true,
  timeBudgetSec: 90,
  title: 'Daily Trigger',

  manifest: {
    /* Every kind this class can fire. flash_burst / gif_burst / gif_rain are
     * deliberately ABSENT: the keyboard is a click-precision surface and the
     * input-trust law (DECISIONS #9) keeps clickable flashes off it entirely. */
    effectsConsumed: [
      'sub_flash', 'bubble_field', 'wash', 'glitch_swap', 'row_drift',
      'audio_trigger', 'ambient_field', 'crt',
    ],
    /* DOM-only, no canvas anywhere, so remote media is permitted but never
     * required: the decoy bubbles draw through the provider and fall back to
     * local/bundled art silently. */
    assetNeeds: { loops: 6, targets: 0, stills: 2, canvasSafe: false },
    boardSizes: null,          // one board shape a day; tier never resizes it
    keybinds: null,            // free-text typing, not a verb slot (see typeToGuess)
    settings: [
      {
        key: 'dt_keyboard_layout', kind: 'enum', default: 'auto',
        values: ['auto', 'qwerty', 'azerty', 'qwertz', 'alphabetical'],
        label_key: 'dt_keyboard_layout', hint_key: 'dt_keyboard_layout_hint',
      },
      {
        key: 'dt_hard_mode', kind: 'bool', default: false,
        label_key: 'dt_hard_mode', hint_key: 'dt_hard_mode_hint',
      },
      {
        key: 'dt_type_to_guess', kind: 'bool', default: true,
        label_key: 'dt_type_to_guess', hint_key: 'dt_type_to_guess_hint',
      },
    ],
    peek: false,               // no peek verb: a revealed letter is the study hint
  },

  create(ctx) {
    const t = (k, f) => {
      try { return ctx.lexicon(k, f); } catch (e) { return f; }
    };
    const say = (m) => { try { ctx.log(m); } catch (e) { /* noop */ } };
    const reduced = probeReduced();
    const coarse = probeCoarse();

    /* ---------------------- state ---------------------------------------- */
    let spec = { gradeTier: 1, seed: '', timeBudgetSec: 90 };
    let tier = 1;
    let entry = null;               // dailyEntry() - the day's board
    let roll = makeTaggedRoll('dt');
    let ladder = null;
    let hardMode = false;
    let typeToGuess = true;

    let plan = [];                  // cellPlan() for one row
    let cells = [];                 // [row][index] -> element
    let rowEls = [];
    let keyEls = Object.create(null);
    let keyNodes = [];              // letter keycaps only (glitch targets)
    let commitBtn = null;
    let msgEl = null;
    let rungPips = [];
    let rungChip = null;
    let rowChip = null;
    let clockChip = null;
    let wrap = null;

    const committed = [];           // [{guess, marks}]
    let cur = [];                   // current row letters
    let rowIndex = 0;
    let kbState = Object.create(null);
    let hintIndex = -1;
    let hintLetter = '';
    let solved = false;
    let finished = false;
    let ceremonyOpen = false;
    let revealing = false;
    let tasteUsed = false;
    let downKey = null;             // the keycap under the finger (glitch-exempt)
    let startMs = 0;
    let elapsedSec = 0;
    let rungAtSolve = 0;
    let jackpot = false;
    let paused = false;
    let suspended = false;
    let retake = false;             // already played this UTC day (see readMeta)

    const timers = new Set();
    const later = (ms, fn) => {
      const id = setTimeout(() => {
        timers.delete(id);
        try { fn(); } catch (e) { say('timer: ' + ((e && e.message) || e)); }
      }, Math.max(0, ms));
      timers.add(id);
      return id;
    };
    const clearTimers = () => { for (const id of Array.from(timers)) clearTimeout(id); timers.clear(); };

    /* Engine calls from this file are guarded the same way ladder.js guards its
     * own: a thrown effect must never kill the class. */
    function fx(kind, opts) {
      try { return ctx.engine ? ctx.engine.fire(kind, opts || {}) : null; }
      catch (e) { say('fire(' + kind + ') failed: ' + ((e && e.message) || e)); return null; }
    }
    function tick(name, level) {
      fx('audio_trigger', { name, level: level == null ? 0.3 : level, bus: 'fx' });
    }
    function beat(garnish) {
      try { if (ctx.engine && ctx.engine.beat) ctx.engine.beat({ garnish: !!garnish }); }
      catch (e) { say('beat failed: ' + ((e && e.message) || e)); }
    }

    /* ---------------------- settings ------------------------------------- */
    function readSettings() {
      const s = (ctx.settings && typeof ctx.settings === 'object') ? ctx.settings : {};
      hardMode = !!s.dt_hard_mode || tier >= 4;      // forced ON at Year 4 (dossier)
      typeToGuess = s.dt_type_to_guess !== false && !coarse;
      let layout = String(s.dt_keyboard_layout || 'auto');
      if (layout === 'auto') {
        let locale = '';
        try { locale = (navigator && (navigator.language || (navigator.languages || [])[0])) || ''; }
        catch (e) { locale = ''; }
        layout = autoLayout(locale);
      }
      return layout;
    }

    /* ---------------------- rendering ------------------------------------ */
    function msg(text, warn) {
      if (!msgEl) return;
      msgEl.textContent = String(text == null ? '' : text);
      msgEl.className = 'g-dt-msg' + (warn ? ' warn' : '');
    }

    function buildBoard(host) {
      const board = el('div', 'g-dt-board');
      cells = []; rowEls = [];
      for (let r = 0; r < ROWS; r++) {
        const row = el('div', 'g-dt-row');
        const rowCells = [];
        for (const cell of plan) {
          const c = el('span', 'g-dt-cell');
          c.setAttribute('role', 'img');
          c.setAttribute('aria-label', t('dt_cell_empty', 'empty'));
          if (entry.goldDay && cell.index === entry.goldIndex) c.classList.add('gold');
          row.appendChild(c);
          rowCells.push(c);
          if (cell.gapAfter) row.appendChild(el('span', 'g-dt-gap'));
        }
        board.appendChild(row);
        rowEls.push(row);
        cells.push(rowCells);
      }
      host.appendChild(board);
    }

    function buildKeyboard(host, layout) {
      const kb = el('div', 'g-dt-kb');
      const rows = layoutRows(layout);
      keyEls = Object.create(null);
      keyNodes = [];
      rows.forEach((letters, ri) => {
        const kr = el('div', 'g-dt-krow');
        if (ri === rows.length - 1 && !coarse) kr.appendChild(specialKey('enter', t('dt_enter', 'ENTER')));
        for (const ch of letters) {
          const k = el('button', 'g-dt-key', ch.toUpperCase());
          k.type = 'button';
          k.setAttribute('data-letter', ch);
          bindKey(k, () => addLetter(ch));
          kr.appendChild(k);
          keyEls[ch] = k;
          keyNodes.push(k);
        }
        if (ri === rows.length - 1) kr.appendChild(specialKey('back', '⌫'));
        kb.appendChild(kr);
      });
      host.appendChild(kb);
      if (coarse) {
        commitBtn = el('button', 'btn primary g-dt-commit', t('dt_commit', 'COMMIT ROW'));
        commitBtn.type = 'button';
        bindKey(commitBtn, commit);
        host.appendChild(commitBtn);
      }
    }

    function specialKey(kind, label) {
      const k = el('button', 'g-dt-key wide', label);
      k.type = 'button';
      k.setAttribute('data-key', kind);
      bindKey(k, kind === 'enter' ? commit : backspace);
      return k;
    }

    /** One place for the press wiring, so the glitch exemption can never drift. */
    function bindKey(node, fn) {
      node.addEventListener('click', () => { if (!blocked()) fn(); });
      // The key under the finger is exempt from glitch_swap WHILE pressed.
      node.addEventListener('pointerdown', () => { downKey = node; node.classList.add('down'); });
      node.addEventListener('pointerup', () => { downKey = null; node.classList.remove('down'); });
      node.addEventListener('mousedown', () => { downKey = node; });
      node.addEventListener('mouseup', () => { downKey = null; });
    }

    function buildHud(host) {
      const hud = el('div', 'g-dt-hud');
      hud.appendChild(el('span', 'chip', t('dt_ladder', 'Ladder')));
      const pips = el('span', 'g-dt-rung');
      rungPips = [];
      for (let i = 0; i < 5; i++) { const p = el('i'); pips.appendChild(p); rungPips.push(p); }
      hud.appendChild(pips);
      rungChip = el('span', 'chip num', 'rung 0/5');
      hud.appendChild(rungChip);
      rowChip = el('span', 'chip num', '1 / ' + ROWS);
      hud.appendChild(rowChip);
      clockChip = el('span', 'chip num', '0s');
      hud.appendChild(clockChip);
      if (hardMode) hud.appendChild(el('span', 'chip warn', t('dt_hard_chip', 'HARD')));
      if (entry.goldDay) hud.appendChild(el('span', 'chip warn', t('dt_gold_chip', 'GOLD ✨')));
      if (entry.kind === 'phrase') hud.appendChild(el('span', 'chip', t('dt_phrase_chip', 'PHRASE')));
      if (entry.revisionOf) hud.appendChild(el('span', 'chip', t('revision_day', 'Revision')));
      // One graded play per UTC day is the shell's rule to enforce; all this class
      // can honestly do is say so, and replay the identical seeded script.
      if (retake) hud.appendChild(el('span', 'chip', t('dt_retake', 'Retake')));
      host.appendChild(hud);
    }

    function paintHud() {
      const r = ladder ? ladder.rung : 0;
      rungPips.forEach((p, i) => { if (i < r) p.classList.add('on'); else p.classList.remove('on'); });
      if (rungChip) rungChip.textContent = 'rung ' + r + '/5';
      if (rowChip) rowChip.textContent = Math.min(rowIndex + 1, ROWS) + ' / ' + ROWS;
    }

    function paintCurrentRow() {
      const row = cells[rowIndex];
      if (!row) return;
      row.forEach((c, i) => {
        const ch = cur[i] || '';
        c.textContent = ch ? ch.toUpperCase() : '';
        c.classList.remove('typed', 'active');
        if (i === hintIndex && hintLetter) { c.classList.add('hinted'); return; }
        if (ch) c.classList.add('typed');
      });
      const next = nextEmpty();
      if (next >= 0 && row[next]) row[next].classList.add('active');
    }

    function paintKeys() {
      for (const ch of Object.keys(keyEls)) {
        const k = keyEls[ch];
        k.classList.remove('hit', 'near', 'miss');
        const st = kbState[ch];
        if (st) k.classList.add(st);
      }
    }

    /* ---------------------- input ---------------------------------------- */
    function blocked() { return finished || ceremonyOpen || revealing || paused || suspended; }

    function nextEmpty() {
      for (let i = 0; i < cur.length; i++) {
        if (i === hintIndex && hintLetter) continue;
        if (!cur[i]) return i;
      }
      return -1;
    }

    function addLetter(ch) {
      if (blocked()) return;
      const i = nextEmpty();
      if (i < 0) return;
      cur[i] = String(ch || '').toLowerCase();
      tick('blip', 0.18);
      msg('');
      paintCurrentRow();
    }

    function backspace() {
      if (blocked()) return;
      for (let i = cur.length - 1; i >= 0; i--) {
        if (i === hintIndex && hintLetter) continue;
        if (cur[i]) { cur[i] = ''; break; }
      }
      tick('blip', 0.12);
      paintCurrentRow();
    }

    /** Physical typing (desktop, per dt_type_to_guess). Deliberately narrow: it
     *  claims A-Z, Enter and Backspace ONLY, and never preventDefaults a letter,
     *  so it can not shadow a player who rebound the app's panic key to one. */
    function onKeyDown(e) {
      if (!typeToGuess || blocked()) return;
      if (!e || e.ctrlKey || e.altKey || e.metaKey) return;
      const k = String(e.key || '');
      if (k === 'Enter') { e.preventDefault(); commit(); return; }
      if (k === 'Backspace') { e.preventDefault(); backspace(); return; }
      if (/^[a-zA-Z]$/.test(k)) addLetter(k.toLowerCase());
    }

    /* ---------------------- the turn ------------------------------------- */
    function shake() {
      const row = rowEls[rowIndex];
      if (!row || reduced) return;
      row.classList.remove('shake');
      try { void row.offsetWidth; } catch (e) { /* noop */ }
      row.classList.add('shake');
      later(400, () => row.classList.remove('shake'));
    }

    function reject(reason) {
      const line = reason === REJECT.NOT_A_WORD
        ? t('dt_not_a_word', 'Not in the word list')
        : t('dt_not_enough', 'Not enough letters');
      msg(line, true);
      shake();
      tick('stamp_bad', 0.3);
    }

    function commit() {
      if (blocked()) return;
      const guess = cur.join('');
      if (guess.length !== entry.letters || cur.some((c) => !c)) { reject(REJECT.SHORT); return; }
      const acc = isAcceptable(guess, entry);
      if (!acc.ok) { reject(acc.reason); return; }
      if (hardMode) {
        const v = hardModeViolation(guess, committed);
        if (v) {
          msg(v.reason === 'hard_hit'
            ? t('dt_hard_hit', 'Hard mode: keep the revealed letters in place')
            : t('dt_hard_near', 'Hard mode: use every revealed letter'), true);
          shake();
          tick('stamp_bad', 0.3);
          return;
        }
      }
      const marks = markGuess(guess, entry.answer);
      committed.push({ guess, marks });
      msg('');
      revealRow(rowIndex, marks, () => afterReveal(guess, marks));
    }

    function revealRow(r, marks, done) {
      const row = cells[r] || [];
      revealing = true;
      const step = reduced ? 0 : 190;
      let chain = 0;
      marks.forEach((m, i) => {
        later(step * i, () => {
          const c = row[i];
          if (!c) return;
          c.classList.remove('typed', 'active');
          if (!reduced) c.classList.add('flip');
          c.classList.add(m);
          c.setAttribute('aria-label', t('mark_' + m, m));
          // the pitch ratchet degrades to a level ratchet: the sfx seam carries
          // name/level/bus only (see the build report's shared-layer asks)
          if (m === HIT) { chain += 1; tick('pop', Math.min(0.75, 0.3 + 0.08 * chain)); }
          else { chain = 0; tick('blip', 0.16); }
        });
      });
      later(step * marks.length + (reduced ? 0 : 120), () => {
        revealing = false;
        if (chain >= 3) tick('streak', 0.4);
        try { done(); } catch (e) { say('afterReveal: ' + ((e && e.message) || e)); }
      });
    }

    function afterReveal(guess, marks) {
      kbState = foldKeyboard(kbState, guess, marks);
      paintKeys();

      if (isSolved(marks)) { onSolve(); return; }

      /* a wrong row climbs the ladder - deterministic, never RNG */
      rowIndex += 1;
      cur = new Array(entry.letters).fill('');
      hintIndex = -1; hintLetter = '';
      if (ladder) ladder.miss();
      beat(tier >= 3);
      paintHud();

      const near = isNearMiss(marks);
      if (near) {
        msg(t('dt_near_miss', 'One letter away'), true);
        try { if (ctx.ceremonies) ctx.ceremonies.reward('near_miss', { target: wrap }); }
        catch (e) { say('near_miss ceremony: ' + ((e && e.message) || e)); }
        const off = marks.indexOf('near') >= 0 ? marks.indexOf('near') : marks.indexOf('miss');
        const c = (cells[rowIndex - 1] || [])[off];
        if (c && !reduced) { c.classList.add('wobble'); later(700, () => c.classList.remove('wobble')); }
      }

      /* Taste of the twist from Year 2: ONE telegraphed, gentle pollution flash,
       * captioned so the player meets our identity before forming an opinion.
       * It waits for a row that is not already saying "one letter away" - two
       * messages on one line means the player reads neither. */
      if (!tasteUsed && tier >= 2 && !near) {
        tasteUsed = true;
        if (ladder) ladder.tasteOfTwist();
        msg(t('dt_twist_telegraph', 'The whispers are real words - just not today\'s.'), true);
      }

      if (rowIndex >= ROWS) { onFail(marks); return; }
      paintCurrentRow();
    }

    /* ---------------------- solve / fail --------------------------------- */
    function onSolve() {
      solved = true;
      rungAtSolve = ladder ? ladder.rung : 0;
      jackpot = roll('vr') < (VR_BY_TIER[tier - 1] || 0.3);
      const word = entry.groups.join(' ').toUpperCase();

      if (ladder) ladder.absorbDressing(jackpot ? 'confetti' : 'petals');
      fx('sub_flash', { text: word, variant: 'centre' });
      tick(jackpot ? 'jackpot' : 'sting', 0.6);
      if (jackpot) {
        try { if (ctx.ceremonies) ctx.ceremonies.reward('jackpot', { target: wrap }); }
        catch (e) { say('jackpot ceremony: ' + ((e && e.message) || e)); }
      }
      absorbWord(entry.answer);

      const lines = [];
      lines.push(jackpot
        ? t('dt_absorb_jackpot', 'It sticks. Deeper than usual.')
        : t('dt_absorbed_line', 'Absorbed. It rides with you through the rest of today.'));
      if (entry.goldDay) lines.push(t('dt_gold_solved', 'Gilded: solved on a gold letter day.'));
      if (isThemeWord(entry.answer)) lines.push(t('dt_theme_word', 'One of your own words.'));

      ceremony({
        word,
        tone: 'good',
        line: lines.join(' '),
        stamp: t('absorbed', 'ABSORBED'),
        ms: reduced ? 1200 : 3800,
        onDone: finish,
      });
    }

    function onFail(lastMarks) {
      solved = false;
      rungAtSolve = ladder ? ladder.rung : 0;
      const word = entry.groups.join(' ').toUpperCase();
      const soClose = hitCount(lastMarks || []) >= Math.max(3, entry.letters - 1);

      /* The word is revealed anyway, inverted: instead of you absorbing it, it
       * flashes AT you. Detention is FLAVOUR ONLY - grade C, attendance intact. */
      if (ladder) ladder.detentionDressing();
      fx('sub_flash', { text: word, variant: 'stamp' });
      later(reduced ? 0 : 520, () => fx('sub_flash', { text: word, variant: 'centre' }));
      later(reduced ? 0 : 1100, () => fx('sub_flash', { text: word, variant: 'scatter' }));
      tick('stamp_bad', 0.5);
      mintStudyHint();

      ceremony({
        word,
        tone: 'bad',
        line: soClose
          ? t('detention_so_close', 'One letter. It kept the last one for itself.')
          : t('detention', 'The word learned you instead.'),
        stamp: t('dt_detention_stamp', 'DETENTION'),
        ms: reduced ? 1200 : 3600,
        onDone: finish,
      });
    }

    /** Page-session absorb only: NEVER a write into the persisted SubliminalPool
     *  (DECISIONS #10 / the ramp-never-writes precedent). If the shell ever grows
     *  a session word sink, this picks it up without a change here. */
    function absorbWord(word) {
      const w = String(word || '').toUpperCase();
      try {
        if (ctx.sessionWords && typeof ctx.sessionWords.add === 'function') {
          ctx.sessionWords.add(w);
          say('absorbed "' + w + '" into the page-session word pool');
          return true;
        }
      } catch (e) { say('absorb sink threw: ' + ((e && e.message) || e)); }
      say('absorbed "' + w + '" (class-local only: no shell session-word sink yet)');
      return false;
    }

    /** Comeback hook: failing mints a hint that pre-reveals one letter TOMORROW. */
    function mintStudyHint() {
      try {
        if (ctx.store && ctx.store.mergeGameMeta) {
          ctx.store.mergeGameMeta('daily_trigger', { studyHint: { mintedUtc: entry.dateUtc } });
        }
      } catch (e) { say('study hint write failed: ' + ((e && e.message) || e)); }
    }

    /** The per-game meta row, defensively (a dead store must not stop a class). */
    function readMeta() {
      try { return (ctx.store && ctx.store.gameMeta) ? (ctx.store.gameMeta('daily_trigger') || {}) : {}; }
      catch (e) { return {}; }
    }

    function consumeStudyHint() {
      const meta = readMeta();
      const hint = meta && meta.studyHint;
      if (!hint || !hint.mintedUtc || hint.mintedUtc === entry.dateUtc) return;
      hintIndex = Math.min(entry.letters - 1, Math.floor(roll('hintpos') * entry.letters));
      hintLetter = entry.answer[hintIndex] || '';
      if (hintLetter) cur[hintIndex] = hintLetter;
      try {
        if (ctx.store && ctx.store.mergeGameMeta) {
          ctx.store.mergeGameMeta('daily_trigger', { studyHint: null });
        }
      } catch (e) { /* noop */ }
      msg(t('dt_study_hint', 'Study hint: one letter is already in place. It costs you nothing.'));
    }

    /* ---------------------- ceremony overlay ----------------------------- */
    function ceremony({ word, tone, line, stamp, ms, onDone }) {
      ceremonyOpen = true;
      const over = el('div', 'g-dt-cer' + (tone === 'bad' ? ' bad' : ''));
      over.appendChild(el('div', 'g-dt-word', word));
      if (line) over.appendChild(el('p', 'g-dt-line', line));
      if (stamp) over.appendChild(el('div', 'g-dt-stamp', stamp));
      over.appendChild(el('p', 'g-dt-skip', t('dt_skip', 'Tap to continue')));

      let done = false;
      const close = () => {
        if (done) return;
        done = true;
        ceremonyOpen = false;
        try { over.remove(); } catch (e) { /* noop */ }
        try { if (typeof window !== 'undefined') window.removeEventListener('keydown', skipKey); }
        catch (e) { /* noop */ }
        try { onDone(); } catch (e) { say('ceremony onDone: ' + ((e && e.message) || e)); }
      };
      const skipKey = (e) => {
        // Escape belongs to the shell's ladder; every other key skips.
        if (e && String(e.key || '') === 'Escape') return;
        close();
      };
      over.addEventListener('click', close);
      try { if (typeof window !== 'undefined') window.addEventListener('keydown', skipKey); }
      catch (e) { /* noop */ }

      try {
        if (ctx.ceremonies) {
          ctx.ceremonies.stamp({ text: stamp, tone: tone === 'bad' ? undefined : 'pink', target: over });
        }
      } catch (e) { say('stamp: ' + ((e && e.message) || e)); }

      if (wrap) wrap.appendChild(over);
      later(Math.max(400, ms || 3200), close);
    }

    /* ---------------------- grading -------------------------------------- */
    /** Band from rows used, then the storm/hard-mode nudge (never above S). */
    function bandFor() {
      if (!solved) return 'C';
      const rows = committed.length;
      let band = rows <= 3 ? 'S' : rows === 4 ? 'A' : 'B';
      const stormed = rungAtSolve >= 4 || hardMode;
      if (stormed) {
        const i = BAND_ORDER.indexOf(band);
        band = BAND_ORDER[Math.max(0, i - 1)];
      }
      return band;
    }

    /**
     * composite = the band's range, positioned by speed. Time is a TIEBREAK
     * INSIDE a band only: it can never promote or demote a letter, which is the
     * dossier's rule and the reason this maths looks conservative.
     */
    function composite() {
      const band = bandFor();
      const [lo, hi] = BANDS[band] || BANDS.C;
      const budget = Math.max(30, Number(spec.timeBudgetSec) || 90);
      const speed = Math.max(0, Math.min(1, 1 - (elapsedSec / (budget * 2))));
      const span = (hi - lo) * 0.82;                     // stay clear of the next threshold
      return Math.max(0, Math.min(1, lo + span * (0.32 + 0.62 * speed)));
    }

    function sharePayload() {
      return {
        kind: 'emoji_grid',
        puzzleNumber: entry.puzzleNumber,
        attempts: solved ? committed.length : 'X',
        max: ROWS,
        solved,
        hardMode,
        // marks ONLY - never a letter, never the answer: the grid is spoiler-free
        rows: committed.map((r) => r.marks.slice()),
        storm: ladder ? ladder.stormBadges() : [],
      };
    }

    /**
     * The class's own corner of the meta store: which UTC day it last played (so a
     * replay can say "retake"), and the fluency counter the dossier's pips read
     * (solve in <= 3 rows). Attendance, streak and XP are NOT ours - the host mints
     * those from the class-ended frame and refuses a page write.
     */
    function recordPlay() {
      const gm = readMeta();
      const fluent = solved && committed.length <= 3;
      try {
        if (ctx.store && ctx.store.mergeGameMeta) {
          ctx.store.mergeGameMeta('daily_trigger', {
            lastPlayedUtc: entry.dateUtc,
            lastRows: solved ? committed.length : 0,
            lastSolved: !!solved,
            fluency: Math.max(0, Math.round(Number(gm.fluency) || 0)) + (fluent ? 1 : 0),
          });
        }
      } catch (e) { say('meta write failed (harmless): ' + ((e && e.message) || e)); }
    }

    function finish() {
      if (finished) return;
      finished = true;
      if (ladder) ladder.stopAll();
      const flavor = solved
        ? (5 + (jackpot ? 5 : 0) + (entry.goldDay ? 3 : 0))
        : 0;
      const payload = {
        metrics: { composite: composite() },
        flavorXp: flavor,
        share: sharePayload(),
      };
      recordPlay();
      say('day ' + entry.dateUtc + ' #' + entry.puzzleNumber
        + ' ' + (solved ? committed.length + '/' + ROWS : 'X/' + ROWS)
        + ' rung ' + rungAtSolve + (hardMode ? ' hard' : '')
        + ' -> band ' + bandFor() + ' composite ' + payload.metrics.composite.toFixed(3));
      try { ctx.endClass(payload); }
      catch (e) { say('endClass threw: ' + ((e && e.message) || e)); }
    }

    /* ---------------------- lifecycle ------------------------------------ */
    return {
      start(classSpec) {
        spec = classSpec || spec;
        tier = Math.max(1, Math.min(4, Math.round(Number(spec.gradeTier) || 1)));
        // THE DAILY WORD IS GLOBAL: the date half of the seed only - the tier
        // suffix must never reach the draw (bank.dateFromSeed).
        const dateUtc = dateFromSeed(spec.seed);
        entry = dailyEntry(dateUtc);
        roll = makeTaggedRoll('dt|' + dateUtc + '|t' + tier);
        plan = cellPlan(entry.groups);
        cur = new Array(entry.letters).fill('');
        const layout = readSettings();

        retake = readMeta().lastPlayedUtc === entry.dateUtc;

        injectStyles();
        ctx.root.textContent = '';
        wrap = el('div', 'g-dt');
        buildHud(wrap);
        buildBoard(wrap);
        msgEl = el('p', 'g-dt-msg');
        msgEl.setAttribute('role', 'status');
        wrap.appendChild(msgEl);
        buildKeyboard(wrap, layout);
        ctx.root.appendChild(wrap);

        ladder = createLadder({
          engine: ctx.engine,
          tier,
          log: say,
          reduced,
          roll,
          pollution: pollutionWords(dateUtc, 12, entry.answer),
          targets: {
            keyRows: () => keyNodes.slice(),
            keycaps: () => keyNodes.slice(),
            exempt: () => downKey,
            onGlitchSwap: swapGlyphs,
          },
        });
        ladder.open();

        consumeStudyHint();
        paintCurrentRow();
        paintKeys();
        paintHud();
        if (entry.kind === 'phrase') {
          msg(t('dt_phrase_hint', 'Two words today. The gap is free.'));
        } else if (entry.revisionOf && !hintLetter) {
          msg(t('revision_day_hint', 'Revision: you have met this one before.'));
        }

        startMs = Date.now();
        const clock = () => {
          if (finished) return;
          if (!paused && !suspended) {
            elapsedSec = Math.max(0, Math.round((Date.now() - startMs) / 1000));
            if (clockChip) {
              clockChip.textContent = elapsedSec + 's';
              const budget = Number(spec.timeBudgetSec) || 90;
              if (elapsedSec >= budget) clockChip.className = 'chip num warn';
            }
          }
          later(1000, clock);
        };
        later(1000, clock);

        try { if (typeof window !== 'undefined') window.addEventListener('keydown', onKeyDown); }
        catch (e) { say('keydown bind failed: ' + ((e && e.message) || e)); }

        say('board ' + entry.dateUtc + ' #' + entry.puzzleNumber + ' ' + entry.kind
          + ' letters ' + entry.letters + ' tier ' + tier
          + ' rung ' + tierStartFor(tier) + '-' + rungCapFor(tier)
          + (hardMode ? ' hard' : '') + (entry.goldDay ? ' gold' : ''));
      },

      pause() {
        paused = true;
        if (ladder) ladder.pause(true);
      },

      resume() {
        paused = false;
        if (ladder) ladder.pause(false);
      },

      suspend(on) {
        suspended = !!on;
        if (ladder) ladder.pause(suspended);
        // Board state is preserved on purpose: a panic stop or a mandatory video
        // must never cost the player their rows.
        if (wrap) wrap.setAttribute('aria-hidden', suspended ? 'true' : 'false');
      },

      destroy() {
        finished = true;
        clearTimers();
        if (ladder) ladder.stopAll();
        try { if (typeof window !== 'undefined') window.removeEventListener('keydown', onKeyDown); }
        catch (e) { /* noop */ }
        try { ctx.root.textContent = ''; } catch (e) { /* noop */ }
      },
    };

    /* ---------------------- keycap glyph swap ---------------------------- */
    /**
     * INPUT HONESTY, THE WHOLE POINT: this swaps the GLYPH a keycap shows and
     * nothing else. `data-letter` (what the click handler closes over) never
     * moves, the pressed key is exempt upstream in ladder.js, and the glyph is
     * restored on a timer. Friction, never a cheat.
     */
    function swapGlyphs(nodes, variant) {
      const list = (nodes || []).filter((n) => n && n.getAttribute && n.getAttribute('data-letter'));
      if (list.length < 1) return;
      const shown = list.map((n) => n.textContent);
      for (let i = 0; i < list.length; i++) {
        const n = list[i];
        if (n === downKey) continue;                   // never the key under the finger
        const swap = shown[(i + 1) % shown.length];
        n.textContent = String(swap == null ? '✧' : swap);
        n.classList.add('glitched');
      }
      later(reduced ? 260 : 900, () => {
        list.forEach((n, i) => {
          const real = n.getAttribute('data-letter') || '';
          n.textContent = real.toUpperCase();
          n.classList.remove('glitched');
        });
      });
      say('keycap glyphs glitched (' + list.length + ', ' + (variant || 'pooled') + ') - hitboxes unchanged');
    }
  },
};
