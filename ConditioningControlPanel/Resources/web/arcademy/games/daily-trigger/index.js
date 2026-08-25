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
 * ladder.js (rung -> engine), styles.js (.g-dt-* stylesheet), words-*.js (bank),
 * casino.js (House Rules Deck II: marquee / tonight-only room / near-miss
 * staging), trickster.js (Deck III: crooked clock / stat flicker / chalk
 * whisper - the ladder's upper rungs, presentation-only by law).
 * ==========================================================================*/

import { injectStyles } from './styles.js';
import {
  dailyEntry, dateFromSeed, isAcceptable, pollutionWords, isThemeWord, REJECT,
} from './bank.js';
import {
  markGuess, isSolved, isNearMiss, hitCount, foldKeyboard, hardModeViolation,
  layoutRows, autoLayout, cellPlan, HIT, NEAR,
} from './board.js';
import { createLadder, tierStartFor, rungCapFor } from './ladder.js';
import { createDtCasino } from './casino.js';
import { createDtTrickster } from './trickster.js';
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

/** THE TIER AUDIO CEILING (House Book): every cue homeroom requests is clamped
 *  to its grade tier's ceiling, indexed by gradeTier-1. The clamp lives inside
 *  tick() so no call site - this file's, ladder.js's, casino.js's or
 *  trickster.js's - can route around it. Same discipline as
 *  games/anomaly/index.js cue() against plan.audioCeil. */
const AUDIO_CEIL = Object.freeze([0.45, 0.6, 0.75, 0.9]);

/** A refused press (a letter into a full row, a backspace over an empty one, a
 *  key while the ceremony holds the room) answers with ONE muted bump, and a
 *  mashed keyboard must not machine-gun it. */
const BUMP_MIN_MS = 250;

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
  orientation: 'portrait',   // phone only; see games/registry.js ORIENTATIONS
  /* CLOCKLESS (owner ruling, the class-length wave). Homeroom keeps its budget
   * - the bell is real and still ends the class - but the SECONDS are never
   * drawn: no chip on the departure board, none on the campus room card, none
   * in the proctor strip, and no time bar over the stage. A wordle is a board
   * and six rows, and a draining hairline made a one-minute ritual read like a
   * timed exam. It is a DESCRIPTOR flag: core/timetable.js carries it onto the
   * dealt class and shell/shell.js + shell/campus.js are the only readers, so
   * nothing in this file branches on it. games/registry.js GAME_META mirrors it
   * (the parachute is read for a suspended class too). */
  clockless: true,
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

    /* EMI COMMENTARY SEAMS (the heartbeat wave). note() names a moment the
     * mascot may react to - the shell prefixes 'game:' and its own voice engine
     * decides whether the moment is worth a face, a line or nothing at all.
     * Homeroom is clockless and has no timing-critical input, so there is no
     * hold() window here. Additive, one-way and fully guarded: an older shell
     * has no note() at all, and a mascot may never break a class. */
    const note = (id, extra) => {
      try { if (ctx.mood && typeof ctx.mood.note === 'function') ctx.mood.note(id, extra); }
      catch (e) { /* a mascot may never break a class */ }
    };

    const reduced = probeReduced();
    const coarse = probeCoarse();

    /* ---------------------- state ---------------------------------------- */
    let spec = { gradeTier: 1, seed: '', timeBudgetSec: 90 };
    let tier = 1;
    let entry = null;               // dailyEntry() - the day's board
    let roll = makeTaggedRoll('dt');
    let ladder = null;
    let casino = null;              // House Rules Deck II (marquee / tonight-only / almost)
    let trickster = null;           // House Rules Deck III (crooked clock / flicker / whisper)
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

    /* EMI seam bookkeeping only - never read by game logic. The two keystroke
     * seams live on a per-keypress path, so they are latched (once per class /
     * once per row); the reject counter is the `n` the shell throttles on. */
    let emiFirstLetterDone = false;
    let emiRowFilledAt = -1;
    let emiRejects = 0;

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
    /** The deck modules' timer registry: the game's own timers, adapted. */
    const deckTimers = {
      after: later,
      cancel: (id) => { clearTimeout(id); timers.delete(id); },
    };

    /* Engine calls from this file are guarded the same way ladder.js guards its
     * own: a thrown effect must never kill the class. */
    function fx(kind, opts) {
      try { return ctx.engine ? ctx.engine.fire(kind, opts || {}) : null; }
      catch (e) { say('fire(' + kind + ') failed: ' + ((e && e.message) || e)); return null; }
    }
    /**
     * THE ONE ROAD. Every cue - homeroom's own, and every deck's - lands here
     * and is clamped to the grade tier's audio ceiling, so a level is never
     * louder than the year allows. The third argument takes either a bare
     * pitch (every call site that predates W2) or an `extra` object, which is
     * the shape the decks are handed as opts.cue.
     */
    function tick(name, level, pitch) {
      const extra = (pitch && typeof pitch === 'object') ? pitch : null;
      const p = extra ? extra.pitch : pitch;
      const o = Object.assign({ bus: 'fx' }, extra || {});
      o.name = name;
      const ceil = AUDIO_CEIL[tier - 1] || AUDIO_CEIL[0];
      o.level = Math.min(ceil, level == null ? 0.3 : level);
      // the chime ladder climbs in PITCH, not tempo (shell/audio.js seam)
      if (Number.isFinite(p)) o.pitch = Math.max(0.5, Math.min(2, p));
      else delete o.pitch;
      fx('audio_trigger', o);
    }
    /** The refused-input bump, throttled (BUMP_MIN_MS). */
    let lastBumpAt = 0;
    function refused() {
      const now = Date.now();
      if (now - lastBumpAt < BUMP_MIN_MS) return;
      lastBumpAt = now;
      tick('bump', 0.15);   /* owner 2026-08-24: error cues -50% */
    }
    /**
     * blocked() bundles four very different states and only ONE of them is a
     * refusal we own. Mid-reveal the room is genuinely saying no. Paused and
     * suspended belong to the shell (its own pause/resume cues, W1) and a key
     * there must not knock; `finished` is the report card walking in; and
     * while the ceremony is up EVERY key is a verb - it skips - so answering
     * it with a refusal would call a working button broken.
     */
    function refusedByRoom() { if (revealing) refused(); }
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
    /** The room: lamp pools, door-neon bleed, floor line, dust motes, vignette.
     *  DECORATION ONLY - pointer-events:none, aria-hidden, occludes nothing. */
    function roomLayer() {
      const room = el('div', 'g-dt-room');
      room.setAttribute('aria-hidden', 'true');
      room.appendChild(el('div', 'g-dt-neonbleed'));
      const lampA = el('div', 'g-dt-lamp');
      lampA.style.setProperty('left', '4%');
      lampA.style.setProperty('top', '-8%');
      room.appendChild(lampA);
      const lampB = el('div', 'g-dt-lamp b');
      lampB.style.setProperty('right', '2%');
      lampB.style.setProperty('top', '-6%');
      room.appendChild(lampB);
      room.appendChild(el('div', 'g-dt-floor'));
      if (!reduced) {
        // fixed table, not rng: ambience must not consume a seeded stream
        const motes = [
          ['12%', '68%', '9s', '0s'], ['22%', '80%', '11s', '2s'], ['36%', '58%', '10s', '4s'],
          ['48%', '74%', '8.5s', '1s'], ['58%', '62%', '12s', '3s'], ['68%', '82%', '9.5s', '5s'],
          ['78%', '56%', '11s', '6s'], ['88%', '72%', '10s', '2.5s'], ['30%', '38%', '13s', '7s'],
          ['70%', '34%', '12.5s', '1.5s'], ['8%', '44%', '10.5s', '3.5s'], ['92%', '46%', '9s', '6.5s'],
        ];
        for (const [x, y, tt, dl] of motes) {
          const m = el('span', 'g-dt-mote');
          m.style.setProperty('--x', x);
          m.style.setProperty('--y', y);
          m.style.setProperty('--t', tt);
          m.style.setProperty('--dl', dl);
          room.appendChild(m);
        }
      }
      room.appendChild(el('div', 'g-dt-vignette'));
      return room;
    }

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
      if (entry.goldDay) {
        hud.appendChild(el('span', 'chip warn', t('dt_gold_chip', 'GOLD ✨')));
        note('dt.goldDay', { kind: 'curiosity', n: Number(entry.goldIndex) | 0 });
      }
      if (entry.kind === 'phrase') hud.appendChild(el('span', 'chip', t('dt_phrase_chip', 'PHRASE')));
      if (entry.revisionOf) {
        hud.appendChild(el('span', 'chip', t('revision_day', 'Revision')));
        note('dt.revisionDay', { kind: 'curiosity', word: String(entry.revisionOf) });
      }
      // One graded play per UTC day is the shell's rule to enforce; all this class
      // can honestly do is say so, and replay the identical seeded script.
      if (retake) {
        hud.appendChild(el('span', 'chip', t('dt_retake', 'Retake')));
        note('dt.retakeSpotted', { kind: 'tease', n: Number(readMeta().lastRows) | 0 });
      }
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
      // A key pressed into a held room (ceremony / reveal / pause) or into a
      // row that is already full is a REFUSED press, not a silent one.
      if (blocked()) { refusedByRoom(); return; }
      const i = nextEmpty();
      if (i < 0) { refused(); return; }
      cur[i] = String(ch || '').toLowerCase();
      tick('blip', 0.18);
      msg('');
      paintCurrentRow();
      // latched: the opener fires once a class, the full row once a row
      if (!emiFirstLetterDone) {
        emiFirstLetterDone = true;
        note('dt.firstLetterTyped', { kind: 'ambient', tile: cur[i], n: Number(entry.letters) | 0 });
      }
      if (emiRowFilledAt !== rowIndex && nextEmpty() < 0) {
        emiRowFilledAt = rowIndex;
        note('dt.rowFilled', { kind: 'tension', n: rowIndex + 1, word: cur.join('') });
      }
    }

    function backspace() {
      if (blocked()) { refusedByRoom(); return; }
      let took = false;
      for (let i = cur.length - 1; i >= 0; i--) {
        if (i === hintIndex && hintLetter) continue;
        if (cur[i]) { cur[i] = ''; took = true; break; }
      }
      // A backspace over an empty row used to answer with a BRIGHT tick for a
      // letter it never deleted. Nothing moved: it is a refusal.
      if (!took) { refused(); return; }
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
      tick('stamp_bad', 0.15);
      // only the made-up word, never the short row - and `n` is the count so far
      // this class, which is what the shell throttles a mashy player on
      if (reason === REJECT.NOT_A_WORD) {
        emiRejects += 1;
        note('dt.notAWord', { kind: 'tease', n: emiRejects, word: cur.join('') });
      }
    }

    function commit() {
      if (blocked()) { refusedByRoom(); return; }
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
          tick('stamp_bad', 0.15);
          return;
        }
      }
      const marks = markGuess(guess, entry.answer);
      committed.push({ guess, marks });
      msg('');
      /* W3 P0-6: the guess is ACCEPTED. Until now only the refusal had a
       * sound, so the room answered a bad word and ignored a good one. */
      tick('commit', 0.45);
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
          if (m === HIT) { chain += 1; tick('pop', Math.min(0.75, 0.3 + 0.08 * chain), 1 + 0.07 * chain); }
          /* W3 P1-9: a NEAR is the letter you have in the wrong place - the
           * hit's own sound dropped a third, never the dead miss tick. */
          else if (m === NEAR) { chain = 0; tick('pop', 0.22, 0.7); }
          else { chain = 0; tick('blip', 0.16); }
        });
      });
      later(step * marks.length + (reduced ? 0 : 120), () => {
        revealing = false;
        if (chain >= 3) {
          tick('streak', 0.4, 1 + 0.06 * chain);
          if (casino) casino.payout(chain);      // a strong row pays light
          // a solved row is all hits by definition - that one belongs to dt.solvedRow
          if (!isSolved(marks)) note('dt.hitChainRow', { kind: 'celebrate', streak: chain, n: hitCount(marks) });
        }
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
      if (casino && ladder) casino.setHeat(ladder.heat);   // the marquee climbs with the storm
      /* EMI COLOR: row five is the lean-in, the last row the wide eyes; the
       * third wrong row is one small >_<. All face-side, shell-rationed. */
      try {
        if (ctx.mood && rowIndex === ROWS - 1) ctx.mood.clutch();
        else if (ctx.mood && rowIndex === ROWS - 2) ctx.mood.tense();
        else if (ctx.mood && rowIndex === 3) ctx.mood.stumble();
      } catch (e) { /* noop */ }
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
        // near-miss staging: the solved underline starts to draw, dies partway
        if (casino) casino.almost(rowEls[rowIndex - 1]);
        note('dt.nearMissRow', {
          kind: (ROWS - rowIndex) <= 1 ? 'tension' : 'commiserate',
          n: hitCount(marks), left: ROWS - rowIndex, word: guess,
        });
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

      // the solved row earns its chalk underline (rowIndex is still this row -
      // afterReveal only advances it on a miss)
      const solvedRow = rowEls[rowIndex];
      if (solvedRow) solvedRow.classList.add('solved');
      // rows used is the whole brag, and the band is settled by here
      note('dt.solvedRow', {
        kind: 'celebrate', n: committed.length, grade: bandFor(), word: entry.answer,
      });

      if (ladder) ladder.absorbDressing(jackpot ? 'confetti' : 'petals');
      if (casino) { casino.gold(true); casino.payout(5); }   // the frame goes gold for the absorb
      fx('sub_flash', { text: word, variant: 'centre', voice: true, voiceKey: 'daily-trigger-whisper' });
      /* W3 P0-5: solving the word is a GUARANTEED win and must always sound
       * like one. The plain solve gets the same arp a shade under and a hair
       * flat; the variable-ratio roll then stacks its ceremony over it. */
      tick('jackpot', jackpot ? 0.6 : 0.55, jackpot ? 1 : 0.95);
      if (jackpot) {
        try { if (ctx.ceremonies) ctx.ceremonies.reward('jackpot', { target: wrap }); }
        catch (e) { say('jackpot ceremony: ' + ((e && e.message) || e)); }
        note('dt.jackpotAbsorb', { kind: 'celebrate', n: committed.length, grade: bandFor() });
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
      if (casino) casino.dimOut();                 // a loss is never silence: the frame sighs out
      /* VOICE: the stamp carries it. The two echoes land at +520ms and +1100ms,
         inside the 1400ms voiced-gap floor, so they stay silent - one word said
         once over three visual beats, not three clips stacked. */
      fx('sub_flash', { text: word, variant: 'stamp', voice: true, voiceKey: 'daily-trigger-whisper' });
      /* W3 P1-9: three flashes AT you, and two of them were mute. Each echo
       * gets its own stamp, quieter and lower than the one before - a third
       * loss in a row is the quietest (owner's error rule). */
      later(reduced ? 0 : 520, () => {
        fx('sub_flash', { text: word, variant: 'centre' });
        tick('stamp_bad', 0.22, 0.92);
      });
      later(reduced ? 0 : 1100, () => {
        fx('sub_flash', { text: word, variant: 'scatter' });
        tick('stamp_bad', 0.18, 0.85);
      });
      tick('stamp_bad', 0.25);
      mintStudyHint();
      // n is the hits on the row that ran out, so "one letter" is legible to her
      note('dt.detention', {
        kind: 'commiserate', n: hitCount(lastMarks || []), grade: bandFor(), word: entry.answer,
      });

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
      /* W3 P1-9: yesterday's failure handing today a letter is a GIFT, and a
       * gift arrives out loud. */
      tick('chime', 0.3, 1.15);
      msg(t('dt_study_hint', 'Study hint: one letter is already in place. It costs you nothing.'));
      // only reachable on a day that actually inherited yesterday's failure
      note('dt.studyHintConsumed', {
        kind: 'curiosity', tile: String(hintLetter || '').toUpperCase(), n: hintIndex,
      });
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
        /* W3 P1-9: the press that puts the card away. */
        tick('slide', 0.28);
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
      if (trickster) trickster.stop();
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

    /* ---------------------- class rules sheet ---------------------------- */
    /**
     * THE SHEET (Deck VI, Law IV - the rules are DRAWN, the words only caption).
     * Three vignettes in this class's own language: the letterboard taking a
     * typed word, the three marks a committed row wears, and the six-row budget
     * with the rung strip that climbs beside it. Every figure is CSS - cells,
     * glyphs and pips - so it costs no media and reads at any size.
     */
    let howtoEl = null;

    /** Tiers this player has already had the rules sheet for (persisted). */
    function howtoSeenTiers() {
      const m = readMeta();
      return Array.isArray(m.howtoTiers) ? m.howtoTiers.slice() : [];
    }

    function hideHowto() {
      if (howtoEl) { try { howtoEl.remove(); } catch (e) { /* noop */ } }
      howtoEl = null;
    }

    function buildHowto(onGo) {
      const sheet = el('div', 'g-dt-howto');
      sheet.appendChild(el('h2', 'g-dt-hw-title', t('dt_howto_title', 'Class rules')));

      const row = (build, caption) => {
        const r = el('div', 'g-dt-hw-row');
        const fig = el('span', 'g-dt-hw-fig');
        fig.setAttribute('aria-hidden', 'true');
        try { build(fig); } catch (e) { /* a caption alone still teaches */ }
        r.appendChild(fig);
        r.appendChild(el('p', 'g-dt-hw-cap', caption));
        sheet.appendChild(r);
        return r;
      };

      /* 1 - THE ROW. Letters land in the board one cell at a time and the last
         cell holds the caret; the commit key sits beside it, pressing itself. */
      row((fig) => {
        const line = el('span', 'g-dt-hw-line');
        const glyphs = ['G', 'O', 'O', 'D'];
        for (let i = 0; i < glyphs.length; i++) {
          const c = el('span', 'g-dt-hw-cell typed', glyphs[i]);
          c.style.setProperty('--dt-hw-i', String(i));
          line.appendChild(c);
        }
        const caret = el('span', 'g-dt-hw-cell caret');
        caret.style.setProperty('--dt-hw-i', String(glyphs.length));
        line.appendChild(caret);
        fig.appendChild(line);
        fig.appendChild(el('span', 'g-dt-hw-key' + (coarse ? ' wide' : ''),
          coarse ? t('dt_commit', 'COMMIT ROW') : t('dt_enter', 'ENTER')));
      }, t('dt_howto_type', 'Type a word into the row, then Enter. One answer a day, the same for everyone.'));

      /* 2 - THE MARKS. The three faces a committed cell can wear, in the order
         the caption names them. Same tokens as the board, same glyph badges. */
      row((fig) => {
        const line = el('span', 'g-dt-hw-line');
        const marks = ['hit', 'near', 'miss'];
        const glyphs = ['G', 'O', 'O'];
        for (let i = 0; i < marks.length; i++) {
          const c = el('span', 'g-dt-hw-cell flip ' + marks[i], glyphs[i]);
          c.style.setProperty('--dt-hw-i', String(i));
          line.appendChild(c);
        }
        fig.appendChild(line);
      }, t('dt_howto_marks', 'Star: right letter, right place. Half: right letter, elsewhere. Cross: not in it.'));

      /* 3 - THE BUDGET. Six slabs, two already spent, and the rung strip that
         climbs one notch with each of them. */
      row((fig) => {
        const stack = el('span', 'g-dt-hw-stack');
        for (let i = 0; i < ROWS; i++) {
          const slab = el('span', 'g-dt-hw-slab' + (i < 2 ? ' spent' : i === 2 ? ' next' : ''));
          slab.style.setProperty('--dt-hw-i', String(i));
          stack.appendChild(slab);
        }
        fig.appendChild(stack);
        const rungs = el('span', 'g-dt-hw-rungs');
        for (let i = 0; i < 5; i++) {
          const pip = el('i', i < 2 ? 'on' : null);
          pip.style.setProperty('--dt-hw-i', String(i));
          rungs.appendChild(pip);
        }
        fig.appendChild(rungs);
      }, t('dt_howto_rows', 'Six rows is the whole budget. Every wrong row turns the room up one notch.'));

      const go = el('button', 'g-dt-hw-go', t('dt_howto_go', 'Start homeroom'));
      go.type = 'button';
      go.addEventListener('click', () => {
        // THE START PRESS. This one button dismisses the sheet AND opens
        // homeroom, so it wears the school's start cue, not a page-turn
        // slide - the sheet is a single page and has no turns.
        tick('lift', 0.5);
        try { onGo(); } catch (e) { say('howto go: ' + ((e && e.message) || e)); }
      });
      sheet.appendChild(go);
      try { if (typeof go.focus === 'function') go.focus(); } catch (e) { /* noop */ }
      return sheet;
    }

    /**
     * THE LAW, uniform across every open class (owner ruling 2026-08-24): the
     * sheet SHOWS the first time this player meets homeroom at this grade tier
     * (the tier is what changes the room) and AUTO-SKIPS every later class at
     * that tier, whatever the setting says. The shell's "Skip class tutorials"
     * switch (ctx.hideTutorial) means "skip even the first showing". No meta =
     * no memory = the sheet shows. Dismissal is the sheet's own button and
     * nothing else - every letter key is already a verb here, so a keyboard
     * shortcut would type into the board it is covering. The sheet is also
     * free of the clock: startMs is taken in beginClass, past GO.
     */
    function howto(onDone) {
      if (ctx.hideTutorial === true || howtoSeenTiers().indexOf(tier) >= 0) { onDone(); return; }
      if (!wrap) { onDone(); return; }
      let done = false;
      let sheet = null;
      try {
        sheet = buildHowto(() => {
          if (done || finished) return;
          done = true;
          try {
            const seen = howtoSeenTiers();
            if (seen.indexOf(tier) < 0) {
              seen.push(tier);
              if (ctx.store && ctx.store.mergeGameMeta) {
                ctx.store.mergeGameMeta('daily_trigger', { howtoTiers: seen });
              }
            }
          } catch (e) { /* best effort - the sheet just shows again next time */ }
          hideHowto();
          onDone();
        });
      } catch (e) { say('rules sheet refused: ' + ((e && e.message) || e)); sheet = null; }
      if (!sheet) { onDone(); return; }
      howtoEl = sheet;
      wrap.appendChild(sheet);
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
        // the room first, so every light pool sits under the play surfaces
        wrap.appendChild(roomLayer());
        buildHud(wrap);
        // the lesson wall: header + chalkboard slab + chalk-note message line,
        // centered in the flexible zone; the desk (keyboard) anchors the bottom
        const zone = el('div', 'g-dt-stagezone');
        zone.appendChild(el('p', 'g-dt-lesson', t('dt_lesson_header', "Today's Lesson")));
        const slab = el('div', 'g-dt-slab');
        const doodleL = el('span', 'g-dt-doodle l');
        doodleL.setAttribute('aria-hidden', 'true');
        slab.appendChild(doodleL);
        const doodleR = el('span', 'g-dt-doodle r');
        doodleR.setAttribute('aria-hidden', 'true');
        slab.appendChild(doodleR);
        buildBoard(slab);
        zone.appendChild(slab);
        msgEl = el('p', 'g-dt-msg');
        msgEl.setAttribute('role', 'status');
        zone.appendChild(msgEl);
        wrap.appendChild(zone);
        buildKeyboard(wrap, layout);
        ctx.root.appendChild(wrap);

        ladder = createLadder({
          engine: ctx.engine,
          tier,
          log: say,
          reduced,
          roll,
          cue: tick,                     // THE CUE ROAD - clamped by the game
          pollution: pollutionWords(dateUtc, 12, entry.answer),
          targets: {
            keyRows: () => keyNodes.slice(),
            keycaps: () => keyNodes.slice(),
            exempt: () => downKey,
            onGlitchSwap: swapGlyphs,
          },
        });
        /* The House decks (House Rules floor map: homeroom's three cards +
         * the lighting rig). Both disarm themselves when bgIntensity is
         * capped to 0; both replay identically on a retake. */
        const capsOk = !(ctx.caps && Number(ctx.caps.bgIntensity) === 0);
        casino = createDtCasino({
          dateUtc,                       // DATE ONLY: tonight is everyone's night
          slab, wrap,
          timers: deckTimers,
          reduced, capsOk,
          cue: tick,                     // THE CUE ROAD - clamped, never capsOk-gated
          log: say,
        });
        trickster = createDtTrickster({
          seed: String(spec.seed || ('dt|' + dateUtc)),
          tier,
          budgetSec: Number(spec.timeBudgetSec) || 90,
          timers: deckTimers,
          reduced, capsOk,
          cue: tick,                     // THE CUE ROAD - clamped, never capsOk-gated
          getRung: () => (ladder ? ladder.rung : 0),
          isHalted: blocked,
          stats: () => ({
            rung: ladder ? ladder.rung : 0, cap: 5,
            row: Math.min(rowIndex + 1, ROWS), rows: ROWS,
          }),
          paintTruth: paintHud,
          chipEl: (which) => (which === 'rung' ? rungChip : rowChip),
          canWhisper: () => !(msgEl && msgEl.textContent),
          whisperHost: () => zone,
          t,
          log: say,
        });
        consumeStudyHint();
        paintCurrentRow();
        paintKeys();
        paintHud();
        if (entry.kind === 'phrase') {
          msg(t('dt_phrase_hint', 'Two words today. The gap is free.'));
        } else if (entry.revisionOf && !hintLetter) {
          msg(t('revision_day_hint', 'Revision: you have met this one before.'));
        }

        /* THE SHEET FIRST (Deck VI). Nothing that measures the player runs
           until GO: the ladder is still shut, the decks are dark, no key is
           bound and startMs has not been taken, so a class read at leisure
           grades exactly like one that skipped the sheet. */
        const beginClass = () => {
          if (finished) return;
          ladder.open();
          casino.start();
          trickster.start();
          casino.setHeat(ladder.heat);
          paintHud();                      // the opening rung, now that it exists

          startMs = Date.now();
          let budgetWarned = false;      // W3 P2-2: the warn chip lands once
          const clock = () => {
            if (finished) return;
            if (!paused && !suspended) {
              elapsedSec = Math.max(0, Math.round((Date.now() - startMs) / 1000));
              if (clockChip) {
                const budget = Number(spec.timeBudgetSec) || 90;
                // CROOKED CLOCK (House Rules): the FACE may bend from rung 3;
                // elapsedSec itself - the composite input, the warn state, the
                // log line - is exact and never routes through the trickster.
                const face = trickster ? trickster.clockFace(elapsedSec, budget) : elapsedSec;
                clockChip.textContent = face + 's';
                if (elapsedSec >= budget) {
                  /* W3 P2-2: the chip going warn is the ONE time signal this
                   * class has, so it lands once, low, on the transition. */
                  if (!budgetWarned) { budgetWarned = true; tick('clock_tick', 0.16, 0.9); }
                  clockChip.className = 'chip num warn';
                }
              }
            }
            later(1000, clock);
          };
          later(1000, clock);

          try { if (typeof window !== 'undefined') window.addEventListener('keydown', onKeyDown); }
          catch (e) { say('keydown bind failed: ' + ((e && e.message) || e)); }
        };
        howto(beginClass);

        say('board ' + entry.dateUtc + ' #' + entry.puzzleNumber + ' ' + entry.kind
          + ' letters ' + entry.letters + ' tier ' + tier
          + ' rung ' + tierStartFor(tier) + '-' + rungCapFor(tier)
          + (hardMode ? ' hard' : '') + (entry.goldDay ? ' gold' : ''));
      },

      pause() {
        paused = true;
        if (ladder) ladder.pause(true);
        if (casino) casino.freeze(true);
      },

      resume() {
        paused = false;
        if (ladder) ladder.pause(false);
        if (casino) casino.freeze(false);
      },

      suspend(on) {
        suspended = !!on;
        if (ladder) ladder.pause(suspended);
        if (casino) casino.freeze(suspended);
        // Board state is preserved on purpose: a panic stop or a mandatory video
        // must never cost the player their rows.
        if (wrap) wrap.setAttribute('aria-hidden', suspended ? 'true' : 'false');
      },

      destroy() {
        finished = true;
        hideHowto();
        clearTimers();
        if (ladder) ladder.stopAll();
        try { if (trickster) trickster.destroy(); } catch (e) { /* ignore */ }
        trickster = null;
        try { if (casino) casino.destroy(); } catch (e) { /* ignore */ }
        casino = null;
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
      /* W3 P1-9: ONE glitch per scramble, not one per keycap. Without it the
       * glyphs moving under the finger read as a render fault rather than as
       * something the room did on purpose. */
      tick('glitch', 0.28);
      say('keycap glyphs glitched (' + list.length + ', ' + (variant || 'pooled') + ') - hitboxes unchanged');
    }
  },
};
