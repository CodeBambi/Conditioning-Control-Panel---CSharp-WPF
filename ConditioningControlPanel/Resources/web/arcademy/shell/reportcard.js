/* ============================================================================
 * shell/reportcard.js - the day summary + the ONE share pipeline (§9).
 *
 * Renders: the day's four classes with their grades and the XP the HOST paid
 * (never a locally computed number - the XP table lives in C#), the attendance
 * streak on the shared 10-segment meter, and the perfect-attendance stamp
 * through the shared ceremony.
 *
 * SHARE, v1 (DECISIONS #6, SYNTHESIS #5): exactly one renderer, and exactly one
 * payload shape reaches it - Daily Trigger's spoiler-free emoji grid, copied to
 * the clipboard as TEXT. No canvas, no image export, no URL. Every other game's
 * `share` payload is IGNORED with one log line (once per session, not once per
 * class - a shell that logs 60 times a day is a shell nobody reads).
 *
 * The share header is deliberately MOD-ANONYMOUS: the literal string "The
 * Arcademy", not t('arcademy'). A skinned header would out the player's mod in a
 * Discord paste, which is the one thing a share card must never do.
 * ==========================================================================*/

import { t, gradeLabel, tierLabel } from '../core/lexicon.js';
import { exitBar, sign as signExit } from './exits.js';

/** Mod-anonymous, name-only, no URL. Do not "improve" this. */
export const SHARE_HEADER = 'The Arcademy';
/** The only game whose share payload v1 renders. */
export const SHARE_ALLOWED = Object.freeze(['daily_trigger']);
/** Mod-anonymous display names for share text only (never for the UI). */
const SHARE_NAMES = Object.freeze({ daily_trigger: 'Daily Trigger' });

let ignoredShareLogged = false;

/* ----------------------------------------------------------------------------
 * THE PAPER LANDS ON THE DESK.
 * shell/audio.js holds the only audio node on the page (trap 18), so this is a
 * REQUEST on `document` and never a sound - the exact defensive shape
 * shell/ceremonies.js sfx() set. A dropped cue is not an error.
 * -------------------------------------------------------------------------- */
/* ONE cue only. The card's other beats already ring through shell/ceremonies.js
 * - the streak meter requests its own chime ladder and the perfect-attendance
 * seal is `ceremonies.stamp()`, which dispatches 'stamp' itself (trap 67) - so
 * there is deliberately no 'commit' at the seal. It is already sealed, audibly.
 */
function sfx(name, level, extra) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    document.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: Object.assign(
        { name: String(name || 'blip'), level: Number(level) || 0.5, bus: 'fx' },
        extra || {}
      ),
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

/* ----------------------------------------------------------------------------
 * SHARE TEXT
 * -------------------------------------------------------------------------- */
/** Mark key -> emoji, through the lexicon so each mod ships its own set. */
function markEmoji(mark) {
  const m = String(mark || '').toLowerCase();
  if (m === 'hit') return t('share_hit', '💗');
  if (m === 'near') return t('share_near', '🌀');
  return t('share_miss', '🖤');
}

/**
 * Build the spoiler-free grid.
 *
 * @param {Object} payload  {kind:'emoji_grid', puzzleNumber, attempts, max,
 *                           rows:[['hit','miss',...]], storm:[emoji], hardMode,
 *                           solved}
 * @param {Object=} ctx     {grade, streak}
 * @returns {string|null}   null when the payload is not renderable
 */
export function buildShareText(payload, ctx) {
  if (!payload || typeof payload !== 'object') return null;
  const rows = Array.isArray(payload.rows) ? payload.rows : [];
  if (!rows.length) return null;
  const o = ctx || {};

  const name = SHARE_NAMES[payload.gameKey] || SHARE_NAMES.daily_trigger;
  const num = payload.puzzleNumber != null ? ' #' + payload.puzzleNumber : '';
  const max = payload.max || rows.length;
  const attempts = payload.solved === false ? 'X' : (payload.attempts || rows.length);
  const grade = o.grade ? ' - ' + String(o.grade).toUpperCase() : '';
  const hard = payload.hardMode ? '*' : '';

  const lines = [SHARE_HEADER];
  lines.push(name + num + hard + ' - ' + attempts + '/' + max + grade);
  if (o.streak) lines.push('🔥 ' + o.streak);
  lines.push('');
  for (const row of rows.slice(0, 12)) {
    const cells = Array.isArray(row) ? row : [];
    lines.push(cells.slice(0, 12).map(markEmoji).join(''));
  }
  // The storm line is ours, not Wordle's: one badge per effect family that was
  // live on the solving row, so two players with the same 4/6 can compare what
  // they solved through.
  const storm = Array.isArray(payload.storm) ? payload.storm.filter((s) => typeof s === 'string') : [];
  if (storm.length) { lines.push(''); lines.push(storm.slice(0, 8).join('')); }
  return lines.join('\n');
}

/** Clipboard with a synchronous fallback (WebView2 without the async API). */
async function copyText(text) {
  try {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch (e) { /* fall through to the textarea trick */ }
  try {
    const ta = document.createElement('textarea');
    ta.value = text;
    ta.setAttribute('readonly', '');
    ta.style.position = 'fixed';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    const ok = document.execCommand && document.execCommand('copy');
    ta.remove();
    return !!ok;
  } catch (e) { return false; }
}

/* ----------------------------------------------------------------------------
 * THE CARD
 * -------------------------------------------------------------------------- */
/**
 * @param {Object} o
 * @param {Object=} o.ceremonies   createCeremonies() handle
 * @param {Function=} o.toast
 * @param {Function=} o.log
 */
export function createReportCard({ ceremonies, toast, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const shout = typeof toast === 'function' ? toast : () => {};
  // The report is a fullscreen beat now (shell marks <html> arc-report-on):
  // the stage is the night ground under a desk lamp, and everything the card
  // used to box sits on one graded PAPER laid on it. Same DOM order, same
  // class names on the rows/grades/share block - only the frame changed.
  const root = el('div', 'arc-reportstage');

  /**
   * @param {Object} state
   * @param {Object} state.timetable   buildTimetable() result
   * @param {Object} state.results     gameKey -> {grade, xp, zen, capped, tier, detail}
   * @param {Object} state.shares      gameKey -> share payload
   * @param {Object} state.streak      {count, perfectDays}
   * @param {boolean} state.perfect    all classes done today
   * @param {string} state.title       heading (today / yesterday)
   * @param {Function=} state.onDone
   * @param {boolean=} state.arrived  this render is an ARRIVAL at the report,
   *   not a repaint. `onPayout` re-renders this very screen the instant the
   *   host pays out (trap 50's neighbour), and a paper that lands twice is a
   *   paper nobody believes. The shell passes its own `wasScreen` answer.
   */
  function render(state) {
    const s = state || {};
    const results = s.results || {};
    const classes = (s.timetable && s.timetable.classes) || [];
    root.textContent = '';
    if (s.arrived) sfx('paper', 0.3);

    /* the paper under the lamp - every block below lands on it */
    const paper = el('div', 'arc-report-paper');
    root.appendChild(paper);
    const put = (n) => paper.appendChild(n);

    put(el('p', 'arc-kicker', s.title || t('report_card', 'Report Card')));
    put(el('h2', 'arc-h2', s.dateLabel || (s.timetable && s.timetable.dateSeed) || ''));

    /* --- the day's classes (CLASSES_PER_DAY of them) --- */
    const strip = el('div', 'reportcard');
    for (const c of classes) {
      const r = results[c.gameKey];
      const cell = el('span', 'rcell' + (r ? '' : ' pending'));
      const g = r ? String(r.grade || '') : '';
      const gk = g.toLowerCase();
      const badge = el('span', 'grade ' + (gk === 'pass' ? 'pass' : gk || 'none'),
        r ? gradeLabel(r.grade) : '--');
      cell.appendChild(badge);
      cell.appendChild(el('span', null, t('game_' + c.gameKey, c.gameKey.replace(/_/g, ' '))));
      if (r && r.xp != null) cell.appendChild(el('span', 'num', '+' + Math.round(r.xp) + ' ' + t('xp', 'XP')));
      if (r && r.capped && r.capped.length) {
        cell.appendChild(el('span', 'arc-hint', 'capped at A (' + r.capped.join(', ').replace(/_/g, ' ') + ')'));
      }
      strip.appendChild(cell);
    }
    put(strip);

    /* --- attendance --- */
    const att = el('div', 'arc-classbar');
    const streak = s.streak || { count: 0, perfectDays: 0 };
    const chip = el('span', 'chip flame');
    chip.appendChild(el('span', null, '🔥 ' + t('attendance', 'Attendance') + ' '));
    chip.appendChild(el('b', null, String(streak.count | 0)));
    att.appendChild(chip);

    const meterHost = el('span');
    att.appendChild(meterHost);
    if (ceremonies) {
      try { ceremonies.streakMeter({ target: meterHost, filled: streak.count | 0 }); }
      catch (e) { say('streak meter failed: ' + ((e && e.message) || e)); }
    }
    if (streak.perfectDays) {
      att.appendChild(el('span', 'chip', t('perfect_attendance', 'Perfect Attendance') + ' x' + (streak.perfectDays | 0)));
    }
    put(att);

    /* --- perfect attendance stamp (shared ceremony) --- */
    if (s.perfect) {
      const stampHost = el('div', 'arc-classbar');
      put(stampHost);
      if (ceremonies) {
        try {
          ceremonies.stamp({
            text: t('perfect_attendance', 'Perfect Attendance'),
            target: stampHost,
            hold: 60000,           // it is a record, not a flash - it stays on the card
          });
        } catch (e) { say('perfect stamp failed: ' + ((e && e.message) || e)); }
      }
    }

    /* --- the one share pipeline --- */
    const shares = s.shares || {};
    let sharePayload = null;
    for (const key of Object.keys(shares)) {
      if (SHARE_ALLOWED.indexOf(key) >= 0) {
        sharePayload = Object.assign({ gameKey: key }, shares[key]);
      } else if (!ignoredShareLogged) {
        ignoredShareLogged = true;
        say('share: v1 renders daily_trigger only - ignoring payload from ' + key
          + ' (and any others this session)');
      }
    }

    if (sharePayload) {
      const r = results[sharePayload.gameKey] || {};
      const text = buildShareText(sharePayload, { grade: r.grade, streak: streak.count | 0 });
      if (text) {
        const box = el('div', 'arc-sharebox');
        const btn = el('button', 'btn', t('share', 'Copy share card'));
        btn.type = 'button';
        btn.addEventListener('click', () => {
          copyText(text).then((ok) => {
            shout(ok ? t('shared', 'Copied to clipboard') : 'Clipboard refused the copy');
            if (!ok) say('share: clipboard write failed');
          });
        });
        box.appendChild(btn);
        box.appendChild(el('pre', 'arc-sharepreview', text));
        put(box);
      }
    }

    /* --- out ---
     * A STICKY LIT SIGN, not a button at the bottom of a page. The paper
     * scrolls inside the stage, and a day with four classes, a share block and
     * a stamp is taller than a short window - so Done used to be somewhere
     * below the fold with nothing on screen saying so. It now rides the bottom
     * of the stage for as long as the report is up, and it wears the arrow
     * board (shell/exits.js) because this is a terminal screen: there is
     * nothing here to out-shout. */
    const done = el('button', 'btn primary', t('done', 'Done'));
    done.type = 'button';
    done.addEventListener('click', () => { try { if (s.onDone) s.onDone(); } catch (e) { /* noop */ } });
    signExit(done, { dir: 'back' });
    const foot = exitBar([done, s.tier != null ? el('span', 'chip year', tierLabel(s.tier)) : null]);
    put(foot);

    return root;
  }

  return { root, render, destroy() { root.remove(); } };
}

export default createReportCard;
