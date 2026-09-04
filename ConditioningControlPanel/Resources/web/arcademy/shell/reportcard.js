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
 * the clipboard as TEXT. Every other game's `share` payload is IGNORED with one
 * log line (once per session, not once per class - a shell that logs 60 times a
 * day is a shell nobody reads).
 *
 * SHARE v2, THE SLIP (community ask, 2026-09-02 - "grades posted somewhere,
 * maybe in image format"): a SECOND delivery of the SAME night, beside the text
 * one and never in place of it. The whole report - the day's classes with their
 * grades, the attendance meter, the seal when it was earned - drawn onto a
 * canvas as a paper slip by `shell/sharecard.js` and handed over as a PNG. v1's
 * text share is untouched down to the byte: `SHARE_ALLOWED` and
 * `buildShareText` emit exactly what they always emitted, because two players
 * comparing grids across a year of pastes is a contract.
 *
 * The share header is deliberately MOD-ANONYMOUS: the literal string "The
 * Arcademy", not t('arcademy'). A skinned header would out the player's mod in a
 * Discord paste, which is the one thing a share card must never do. The IMAGE
 * obeys the same law twice over - the crest is the school's own art, and every
 * class on it is lettered from sharecard.js's neutral table rather than from a
 * `game_<key>` row. It is also ANONYMOUS BY DEFAULT: your name and student
 * number reach the slip only when you have ticked the box beside the button,
 * and that tick is remembered in the meta bag so it sticks.
 * ==========================================================================*/

import { t, gradeLabel, tierLabel } from '../core/lexicon.js';
import { gradeKey } from '../core/grades.js';
import { exitBar, sign as signExit, campusPillRow } from './exits.js';
import { canRenderCard, renderShareCard } from './sharepaint.js';
import { deliverShareCard, DELIVERED } from './sharedeliver.js';

/** Mod-anonymous, name-only, no URL. Do not "improve" this. */
export const SHARE_HEADER = 'The Arcademy';
/** The only game whose share payload v1 renders. */
export const SHARE_ALLOWED = Object.freeze(['daily_trigger']);
/** Mod-anonymous display names for share text only (never for the UI). */
const SHARE_NAMES = Object.freeze({ daily_trigger: 'Daily Trigger' });
/** The meta key the "add my name" tick lives under. Page-owned. */
export const SHARE_NAME_KEY = 'shareNamed';

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
 * SHARE IMAGE - THE SLIP
 * -------------------------------------------------------------------------- */

/** Reduced motion, either signal - the shell's own class or the OS preference. */
function reducedMotion() {
  try {
    const root = (typeof document !== 'undefined') && document.documentElement;
    if (root && root.classList && typeof root.classList.contains === 'function'
      && root.classList.contains('arc-reduced')) return true;
  } catch (e) { /* noop */ }
  try {
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      const m = window.matchMedia('(prefers-reduced-motion: reduce)');
      if (m && m.matches) return true;
    }
  } catch (e) { /* noop */ }
  return false;
}

/** The file name the slip is saved under. Mod-anonymous, like everything else. */
export function shareFileName(dateLabel) {
  const d = String(dateLabel || '').replace(/[^0-9A-Za-z-]/g, '') || 'today';
  return 'arcademy-report-' + d + '.png';
}

/**
 * DELIVERY, RUNG 1: hand the player the file.
 *
 * A `blob:` url on an `<a download>`, clicked once and revoked. This is the
 * LAST fallback in the finished ladder and the only one in this wave, and it is
 * also the one that a host can refuse outright: the Discord Activity iframe
 * sandbox drops navigations it did not initiate, so the click can be a silent
 * no-op. There is no event for that refusal, which is why the ladder above this
 * one exists at all - and why a host that has NEITHER is told so out loud
 * rather than left looking like it worked.
 *
 * @returns {boolean} whether the browser accepted the attempt
 */
export function deliverDownload(blob, fileName) {
  let url = null;
  try {
    if (!blob || typeof document === 'undefined') return false;
    const U = (typeof URL !== 'undefined') ? URL : null;
    if (!U || typeof U.createObjectURL !== 'function') return false;
    const a = document.createElement('a');
    /* `download` on an anchor is the feature test AND the verb: a host that
     * does not implement it simply never has the property. */
    if (!('download' in a)) return false;
    url = U.createObjectURL(blob);
    a.href = url;
    a.download = String(fileName || 'report.png');
    a.rel = 'noopener';
    a.style.position = 'fixed';
    a.style.opacity = '0';
    document.body.appendChild(a);
    a.click();
    a.remove();
    return true;
  } catch (e) {
    return false;
  } finally {
    /* revoked on the next turn - revoking inside the same tick has raced the
     * download in every browser this school runs on */
    if (url && typeof setTimeout === 'function') {
      setTimeout(() => { try { URL.revokeObjectURL(url); } catch (e2) { /* noop */ } }, 4000);
    }
  }
}

/**
 * THE PRINT BEAT. Pressing Share is a committed action, so the button does not
 * simply go grey - a slip of paper slides out from under it while the canvas
 * draws, and one `paper` cue rides the same frame (Law X: sound and sight land
 * together). Under reduced motion the slip is still MINTED and still says the
 * same thing, it just fades in place, which is the House Book's own
 * "<=120ms opacity change" collapse rather than a beat that vanishes.
 */
function printBeat(host, reduced) {
  try {
    if (!host || typeof document === 'undefined') return () => {};
    const slip = el('span', 'arc-shareslip' + (reduced ? ' still' : ''));
    slip.setAttribute('aria-hidden', 'true');
    host.appendChild(slip);
    return () => { try { slip.remove(); } catch (e) { /* noop */ } };
  } catch (e) { return () => {}; }
}

/* ----------------------------------------------------------------------------
 * THE CARD
 * -------------------------------------------------------------------------- */
/**
 * @param {Object} o
 * @param {Object=} o.ceremonies   createCeremonies() handle
 * @param {Object=} o.seep         THE SEEP's director (shell/seep.js), or null.
 *                                 The card asks it ONE question - `stampGhost()`
 *                                 - as the grade stamp comes down, and draws
 *                                 whatever it is handed. Absent = the card the
 *                                 shell always rendered.
 * @param {Function=} o.toast
 * @param {Function=} o.log
 * @param {Function=} o.onCounter  THE TILL IS A DOOR (counter shortcut wave,
 *                                 2026-08-30). Absent = the card the shell
 *                                 always rendered, down to the byte: the
 *                                 ticket chip stays the inert <span> it has
 *                                 always been. Present = that same chip is a
 *                                 <button> and this is what it presses. The
 *                                 card never decides whether there IS a
 *                                 counter to walk to - the shell owns the
 *                                 catalog, the shutter and the walk, and this
 *                                 is one callback out.
 * @param {Function=} o.identity   () -> {name, number} - who the player is, for
 *                                 the ONE case they asked to be named on the
 *                                 slip. Absent, or answering null, and the
 *                                 "add my name" tick is not even drawn: a
 *                                 checkbox that cannot do anything is worse
 *                                 than no checkbox.
 * @param {Object=} o.shareNamed   {get(), set(v)} over the page-owned meta key
 *                                 SHARE_NAME_KEY. Absent = the tick still
 *                                 works for this sitting and forgets after it,
 *                                 which is the honest degradation for a
 *                                 preference with nowhere to live.
 */
export function createReportCard({
  ceremonies, seep, toast, log, onCounter, identity, shareNamed, shareToHost,
} = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const shout = typeof toast === 'function' ? toast : () => {};
  /* THE ONE DOOR THIS CARD OFFERS BESIDES 'done'. Captured once, asked at
   * render: a card built without it is not a card with a dead button on it,
   * it is the card that has no button at all (see the chip below). */
  const toCounter = typeof onCounter === 'function' ? onCounter : null;
  /* WHO YOU ARE, AND WHETHER YOU SAID SO. Both are captured once and asked at
   * render, the same discipline `onCounter` above set. Neither is ever read
   * unless the player has ticked the box - `readIdentity()` is not called on
   * the anonymous path at all, so a card that nobody named never so much as
   * looks up a display name. */
  const readIdentity = typeof identity === 'function' ? identity : null;
  const namedGet = (shareNamed && typeof shareNamed.get === 'function') ? shareNamed.get : null;
  const namedSet = (shareNamed && typeof shareNamed.set === 'function') ? shareNamed.set : null;
  /* The desktop app's clipboard, reached the only way the page is allowed to
   * reach anything: a round trip the SHELL owns. A web build hands in nothing
   * and that rung of the ladder simply is not there. */
  const toHost = typeof shareToHost === 'function' ? shareToHost : null;
  let namedLocal = false;    // the sitting's answer when there is nowhere to save one
  const isNamed = () => (namedGet ? namedGet() === true : namedLocal);
  const setNamed = (v) => {
    namedLocal = !!v;
    if (namedSet) { try { namedSet(!!v); } catch (e) { say('share name pref failed to save'); } }
  };
  // The report is a fullscreen beat now (shell marks <html> arc-report-on):
  // the stage is the night ground under a desk lamp, and everything the card
  // used to box sits on one graded PAPER laid on it. Same DOM order, same
  // class names on the rows/grades/share block - only the frame changed.
  const root = el('div', 'arc-reportstage');

  /* EVERY HOLD HAS AN OWNER. The till's count-up is the card's first timer, and
   * a timer that outlives its paper writes into a node the shell has already
   * dropped - so they are all swept here, on a repaint AND on destroy. */
  const timers = new Set();
  function later(fn, ms) {
    if (typeof setTimeout !== 'function') { try { fn(); } catch (e) { /* noop */ } return 0; }
    const id = setTimeout(() => {
      timers.delete(id);
      try { fn(); } catch (e) { /* a beat must never be the thing that throws */ }
    }, Math.max(0, Number(ms) || 0));
    timers.add(id);
    return id;
  }
  function sweep() {
    for (const id of Array.from(timers)) { try { clearTimeout(id); } catch (e) { /* noop */ } }
    timers.clear();
  }

  /** Shut a chip's door, or open it again. `disabled` is the real property on a
   *  real <button> and the class is only what the sheet paints; both are
   *  wrapped because the node double carries neither. */
  function setBusy(node, on) {
    if (!node) return;
    try { node.disabled = !!on; } catch (e) { /* noop */ }
    try {
      if (node.classList && typeof node.classList.toggle === 'function') {
        node.classList.toggle('is-busy', !!on);
      }
    } catch (e) { /* noop */ }
  }

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
    sweep();
    root.textContent = '';
    if (s.arrived) sfx('paper', 0.3);

    /* the paper under the lamp - every block below lands on it */
    const paper = el('div', 'arc-report-paper');
    root.appendChild(paper);
    const put = (n) => paper.appendChild(n);

    /* THE CREST, PINNED TO THE TOP OF THE PAPER. Done is still the lit sign at
     * the bottom of the stage; this is the same journey with the school's name
     * on it, and it sticks so a day with four classes, a share block and a
     * stamp can never scroll the way home off the top. Same verb as Done. */
    put(campusPillRow({
      onActivate: () => { try { if (s.onDone) s.onDone(); } catch (e) { /* noop */ } },
    }));

    put(el('p', 'arc-kicker', s.title || t('report_card', 'Report Card')));
    put(el('h2', 'arc-h2', s.dateLabel || (s.timetable && s.timetable.dateSeed) || ''));

    /* --- the day's classes (CLASSES_PER_DAY of them) --- */
    const strip = el('div', 'reportcard');
    for (const c of classes) {
      const r = results[c.gameKey];
      const cell = el('span', 'rcell' + (r ? '' : ' pending'));
      const g = r ? String(r.grade || '') : '';
      /* 'S+' LOWERCASES TO A CLASS NOBODY CAN STYLE. `.grade.s+` is not a
       * selector CSS will honour without escaping, so the honours letter wears
       * `splus` here and in every other badge on the page - the LABEL is still
       * the lexicon's own 'S+' one line down. */
      const gk = gradeKey(g);
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

    /* W3 P0-29: THE STRIP COUNTS ITSELF OUT. Four grades and the day's XP used
     * to land in one silent paint, which is the one moment of the night the
     * player is actually reading numbers. One `pip` per graded class, 160ms
     * apart and a step higher each time so a run of four reads as a tally, then
     * a short `clock_tick` roll under the XP total - the adding machine, quiet
     * enough to stay under the pips. The whole thing is ONE dispatch: `steps`
     * are scheduled on the mixer's own timeline, so nothing here owns a timer.
     * Drops bus (this is the payout), and never on a repaint (see `arrived`). */
    if (s.arrived) {
      const graded = classes.filter((c) => results[c.gameKey]).length;
      const xpTotal = classes.reduce((sum, c) => {
        const r = results[c.gameKey];
        return sum + ((r && r.xp) ? Math.round(r.xp) : 0);
      }, 0);
      if (graded > 0) {
        const steps = [];
        for (let i = 1; i < graded; i += 1) {
          steps.push({ atMs: 160 * i, pitch: 1 + 0.08 * i });
        }
        if (xpTotal > 0) {
          // The roll starts after the last pip and never runs past 600ms.
          const rollFrom = 160 * (graded - 1) + 240;
          for (let k = 0; k < 6; k += 1) {
            steps.push({ atMs: rollFrom + 90 * k, name: 'clock_tick', level: 0.12, pitch: 1 + 0.03 * k });
          }
        }
        sfx('pip', 0.3, { bus: 'drops', steps });
      }
    }

    /* --- THE TILL (economy wave, 2026-08-26) -------------------------------
     * The night's tickets, counted out onto the paper, and - on the rare night
     * that earned one - the token dropping into the tray. Both numbers are the
     * HOST'S: they arrive on `payout-result` and the shell parks them on the
     * result row, so nothing here adds anything up that C# has not already
     * added up. The card only knows how to count TO a number.
     *
     * TICKETS ARE THE SMALL BEAT AND THE TOKEN IS THE BIG ONE, on purpose. A
     * ticket payout happens every single graded class; if it rang like a
     * jackpot the jackpot would stop meaning anything by Wednesday. So tickets
     * get the adding machine (the same `pip` ladder the strip above uses, one
     * bus, one dispatch) and the token gets the actual ceremony.
     * --------------------------------------------------------------------- */
    const till = { tickets: 0, token: false };
    for (const c of classes) {
      const p = results[c.gameKey] && results[c.gameKey].payout;
      if (!p) continue;
      till.tickets += Math.max(0, Math.round(Number(p.tickets) || 0));
      if (p.token === true) till.token = true;
    }
    if (till.tickets > 0 || till.token) {
      const row = el('div', 'arc-classbar arc-till');
      /* THE TILL IS A DOOR (counter shortcut wave, 2026-08-30). This is the one
       * screen in the school where you are TOLD you just earned tickets and had
       * no road to spend them on: the campus chip is two screens back behind a
       * Done. So the ticket chip is the same press the campus chip already is -
       * `campus.js`'s pattern copied, not a second one invented. Same label off
       * the same key, glyph still `aria-hidden`, and the count-up below writes
       * `tNum.textContent` exactly as it did: the parent changing tag changes
       * nothing about the number inside it.
       *
       * NO DOOR, NO BUTTON. Without `onCounter` the chip is emitted as the inert
       * <span> it has always been, byte for byte, so every other caller of this
       * card and every suite that reads it are untouched.
       *
       * NO `returnTo`. report -> prizebooth is a lateral swap `screenCue()`
       * already knows how to sound; the card's Back and Esc still mean the
       * campus, and a variable exit on a screen with three call sites is trap
       * 48's warning with the serial numbers filed off. */
      const tChip = toCounter
        ? el('button', 'chip arc-till-t arc-till-go')
        : el('span', 'chip arc-till-t');
      if (toCounter) {
        const lbl = t('campus_room_prizes', 'Prize Counter');
        try {
          tChip.setAttribute('type', 'button');
          tChip.setAttribute('aria-label', lbl);
          tChip.setAttribute('title', lbl);
        } catch (e) { /* the node double may not carry attributes */ }
        try {
          if (typeof tChip.addEventListener === 'function') {
            tChip.addEventListener('click', () => {
              try { toCounter(); }
              catch (e2) { say('the till door threw: ' + ((e2 && e2.message) || e2)); }
            });
          }
        } catch (e) { /* a chip that cannot be wired is the chip it was */ }
      }
      const tIco = el('i', 'arc-tick');
      try { tIco.setAttribute('aria-hidden', 'true'); } catch (e) { /* noop */ }
      tChip.appendChild(tIco);
      const tNum = el('b', 'arc-till-n', String(s.arrived ? 0 : till.tickets));
      tChip.appendChild(tNum);
      tChip.appendChild(el('span', null, ' ' + t('payout_tickets', 'Tickets')));
      row.appendChild(tChip);
      put(row);

      if (s.arrived && till.tickets > 0) {
        /* THE COUNT-UP. Twelve steps and out, eased so the last few land slow -
         * a counter that ticks evenly reads as a progress bar, and a counter
         * that slows down reads as money being counted. A repaint (`arrived`
         * false) skips straight to the total: the number is not news twice. */
        const STEPS = 12;
        const dur = 720;
        for (let i = 1; i <= STEPS; i += 1) {
          const frac = i / STEPS;
          const eased = 1 - Math.pow(1 - frac, 2.2);
          const at = Math.round(dur * frac);
          const shown = Math.round(till.tickets * eased);
          later(() => { try { tNum.textContent = String(shown); } catch (e) { /* noop */ } }, at);
        }
        sfx('pip', 0.26, {
          bus: 'drops',
          steps: [
            { atMs: 0, pitch: 0.94 }, { atMs: 150, pitch: 1.02 },
            { atMs: 320, pitch: 1.1 }, { atMs: 520, pitch: 1.18 },
            { atMs: 700, name: 'chime', level: 0.34, pitch: 1 },
          ],
        });
      }

      if (till.token) {
        const kChip = el('span', 'chip arc-till-k');
        const kIco = el('i', 'arc-tok', '◉');
        try { kIco.setAttribute('aria-hidden', 'true'); } catch (e) { /* noop */ }
        kChip.appendChild(kIco);
        kChip.appendChild(el('span', null,
          t('payout_token_minted', 'A token dropped in the tray. That is your one for today.')));
        row.appendChild(kChip);
        /* THE ONE BIG BEAT OF THE NIGHT, and it is borrowed rather than built:
         * ceremonies.reward('jackpot') is the engine's own jackpot with the CSS
         * floor behind it, which is exactly what a once-a-day mint should be
         * spending. It lands AFTER the ticket count-up so the two do not step on
         * each other - the small number finishes, then the rare thing happens. */
        if (s.arrived && ceremonies) {
          later(() => {
            try {
              ceremonies.reward('jackpot', {
                target: kChip,
                text: t('wallet_tokens', 'Tokens'),
              });
            } catch (e) { say('token mint beat failed: ' + ((e && e.message) || e)); }
          }, till.tickets > 0 ? 820 : 120);
        }
      }

      /* THE DOOR IS SHUT WHILE THE MONEY IS STILL BEING COUNTED. Walking to the
       * counter tears this screen down (`showPrizeBooth` calls dismissEndCard,
       * dismissPunchStage and dismissAnnexStage on its way in), so a press
       * landing mid-beat stomps the very beat that is telling you what you have
       * to spend. The count-up runs 720ms and the jackpot lands at 820; the
       * chip wakes up on the far side of whichever of the two is playing. A
       * REPAINT has no beats at all, so it never shuts.
       *
       * The shell keeps its own rung on the same question - a punch card or the
       * annex reveal can still be up over this paper - and refuses there. Two
       * guards because they are two different facts. */
      if (toCounter && s.arrived) {
        const ends = till.token
          ? (till.tickets > 0 ? 820 : 120) + 1400
          : (till.tickets > 0 ? 780 : 0);
        if (ends > 0) {
          setBusy(tChip, true);
          later(() => setBusy(tChip, false), ends);
        }
      }
    }

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
      /* THE OTHER STAMP (THE SEEP, tell 08). The stamp comes down, and for a
       * single frame the mark it leaves is not a grade. Then the ink settles and
       * it always was one.
       *
       * It rides a beat the PLAYER caused and is already watching, and it costs
       * the card nothing: the ghost lives in a zero-height relatively-positioned
       * box laid before the ceremony's own target, so no row on this paper ever
       * moves, and it is gone before the stamp keyframe starts. `seep` answering
       * null is the overwhelmingly common case and the whole block is skipped. */
      if (seep && typeof seep.stampGhost === 'function') {
        let spec = null;
        try { spec = seep.stampGhost(); } catch (e) { spec = null; }
        if (spec) {
          const box = el('div', 'arc-seep-stampbox');
          const mark = el('span', 'arc-seep-stamp', String(spec.text || ''));
          box.setAttribute('aria-hidden', 'true');
          box.appendChild(mark);
          stampHost.appendChild(box);
          const clear = () => {
            try { box.remove(); } catch (e) { /* noop */ }
            try { if (spec.done) spec.done(); } catch (e) { /* noop */ }
          };
          if (typeof setTimeout === 'function') setTimeout(clear, Math.max(40, Number(spec.ms) || 80));
          else clear();
        }
      }
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

    /* --- THE SLIP: the same night, as a picture ---------------------------
     * Offered on EVERY night, not only a Daily Trigger one - the text grid is
     * one game's spoiler-free puzzle and this is the whole report card, so the
     * two answer different asks. Drawn only where a canvas exists: the headless
     * DOM double the suites drive answers `canRenderCard()` false and this
     * whole block is skipped, which is why the suites can still build a report.
     */
    if (canRenderCard()) {
      const imgBox = el('div', 'arc-sharebox arc-shareimg');
      const shareBtn = el('button', 'btn', t('share_image', 'Share report card'));
      shareBtn.type = 'button';
      imgBox.appendChild(shareBtn);

      /* THE TICK. Anonymous is the default and the default is not a nag: the
       * box is drawn unticked, it is remembered once ticked, and it is not
       * drawn at all when there is no identity to print. */
      let nameBox = null;
      if (readIdentity) {
        const lbl = el('label', 'arc-sharename');
        nameBox = el('input');
        try {
          nameBox.type = 'checkbox';
          nameBox.checked = isNamed();
        } catch (e) { /* the node double carries neither */ }
        if (typeof nameBox.addEventListener === 'function') {
          nameBox.addEventListener('change', () => { setNamed(nameBox.checked === true); });
        }
        lbl.appendChild(nameBox);
        lbl.appendChild(el('span', null, t('share_add_name', 'Add my name')));
        imgBox.appendChild(lbl);
      }

      let busy = false;
      shareBtn.addEventListener('click', () => {
        if (busy) return;
        busy = true;
        setBusy(shareBtn, true);
        const reduced = reducedMotion();
        const endBeat = printBeat(imgBox, reduced);
        sfx('paper', 0.4);
        const dateLabel = s.dateLabel || (s.timetable && s.timetable.dateSeed) || '';
        const ident = (nameBox && nameBox.checked === true && readIdentity)
          ? (() => { try { return readIdentity(); } catch (e) { return null; } })()
          : null;
        renderShareCard({
          dateLabel,
          classes: classes.map((c) => ({
            gameKey: c.gameKey,
            grade: results[c.gameKey] ? results[c.gameKey].grade : null,
          })),
          streak: streak.count | 0,
          perfect: !!s.perfect,
          identity: ident,
        }).then((out) => {
          if (!out || !out.blob) {
            shout(t('share_unavailable', 'Sharing is not available here'));
            say('share image: the canvas would not export');
            return null;
          }
          return deliverShareCard(out.blob, shareFileName(dateLabel), {
            toHost, download: deliverDownload,
          }).then((how) => {
            if (how === DELIVERED.SHARED) { shout(t('share_shared', 'Report card shared')); return; }
            if (how === DELIVERED.COPIED) { shout(t('share_copied', 'Report card copied')); return; }
            if (how === DELIVERED.SAVED) { shout(t('share_saved', 'Report card saved')); return; }
            /* EVERY RUNG REFUSED. Say so. The Activity iframe can take the
             * download without taking it, and a beat that looks like it
             * worked is the one outcome worse than not sharing. */
            shout(t('share_unavailable', 'Sharing is not available here'));
            say('share image: no delivery this host will accept');
          });
        }).catch((e) => {
          shout(t('share_unavailable', 'Sharing is not available here'));
          say('share image threw: ' + ((e && e.message) || e));
        }).then(() => {
          /* ONE owner for the latch, on every path. The ladder is two awaits
           * deep now and a beat left running because a rung threw is a button
           * nobody can press again tonight. */
          endBeat();
          setBusy(shareBtn, false);
          busy = false;
        });
      });
      put(imgBox);
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

  return { root, render, destroy() { sweep(); root.remove(); } };
}

export default createReportCard;
