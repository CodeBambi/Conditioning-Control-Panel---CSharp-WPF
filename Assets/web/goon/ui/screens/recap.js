/* ============================================================================
 * ui/screens/recap.js — what just happened, in the order it mattered.
 *
 * Reads TWO sources and nothing else:
 *   match.result   — the countersigned outcome (core/match.js GoonMatchResult).
 *                    It can arrive AFTER this screen mounts: the result
 *                    handshake has a 10 s deadline, so we subscribe to
 *                    onResultFinalized and repaint rather than rendering a
 *                    provisional verdict as if it were final.
 *   matchLog       — boot's collector (payload traffic, phases, emotes).
 *
 * DISPUTED IS NOT AN ERROR. When the two clients disagree the engine records
 * BOTH claims and still grants the uncontested cosmetics. The amber badge says
 * so plainly instead of picking a winner the engine refused to pick.
 *
 * Titles are computed HERE, from local data, and are cosmetic only. Nothing on
 * this screen is sent anywhere.
 * ==========================================================================*/

import { createLedger, el, button } from '../router.js';
import { S, mmss } from '../strings.js';
import { avatarSlot, emitAva } from '../avatar.js';
import { GoonEndReason, GoonMatchPhase } from '../../core/contracts.js';
import { evidenceFor, submitReport, NOTE_MAX, REPORT_REASONS } from '../report.js';
import { noteMatchFinished } from '../nightProgress.js';
import { settleOnce, formatRecord, outcomeOf } from '../rivalry.js';

/** Matches already counted by noteMatchFinished (ui/nightProgress.js). */
const countedMatches = new WeakSet();
import { duelSummary } from '../duel/duelController.js';
import { DUEL_COPY } from '../duel/copy.js';
import { burst, centreOf, countUp, isCalm, play, popIn, squash, staggerIn } from '../juiceDom.js';
import { buildShareData, cardKey, FLAVOUR_TINTS } from '../shareWords.js';
import { renderCard, copyCard, saveCard, canvasBlob, cardFileName } from '../shareCard.js';
import { THUD_EASE, staggerDelays } from '../juice.js';

const COLLAPSE_AT = 6;
const GRACEFUL_MS = 8 * 60 * 1000;
const STONE_WALL_ENDURED = 4;

/** How many artifacts the report shortlist ever shows. Flagged ones come first. */
export const MAX_REPORT_ITEMS = 6;

/**
 * Where the standalone CTA points. `.html` is NOT decoration: the site is
 * plain static hosting with clean URLs OFF, so /explore is a 404 and
 * /explore.html is the page every link on cclabs.app itself uses.
 */
export const EXPLORE_URL = 'https://cclabs.app/explore.html';

/** One retry on a failed submit, then the card stops offering false hope. */
export const REPORT_MAX_RETRIES = 1;

/* Payload names, by GoonPayloadKind code: S.payloads in ui/strings.js. */
const KIND_NAMES = S.payloads;

/**
 * DEFENCE IN DEPTH, NOT THE FIX. boot.js clearForRecap() empties #gg-stage when
 * the phase turns Recap; this is the screen refusing to mount underneath a husk
 * regardless of who forgot. #gg-stage is z20 and full-bleed over the z10 screen
 * stack, and ui/screens.css only makes it click-through while it is :empty — one
 * leftover node and every button on this card is unreachable (it shipped that
 * way: "clicking any button does nothing, esc does nothing too").
 *
 * It logs at WARN when it finds something, because finding something means the
 * teardown regressed and that is worth a line in the log, not a silent patch.
 */
export function assertStageClear(logger) {
  if (typeof document === 'undefined') return 0;
  let stage = null;
  try { stage = document.getElementById('gg-stage'); } catch (_e) { return 0; }
  const n = stage ? (stage.childElementCount | 0) : 0;
  if (!n) return 0;
  try { logger?.warn?.('recap mounted with ' + n + ' node(s) still on #gg-stage — clearing (they would eat every click)'); }
  catch (_e) { /* logger is optional */ }
  try { stage.replaceChildren(); } catch (_e) { /* ignore */ }
  return n;
}

/**
 * THE REPORT SHORTLIST — which peer artifacts this card offers, and in what
 * order. Pure, so test/selftest-report.js can prove the gating without a DOM.
 *
 * TWO SOURCES, BECAUSE NEITHER ONE IS THE WHOLE ANSWER:
 *
 *   `rendered` (exec/videos.js peerRenderLog) is the only record of what
 *   actually went ON SCREEN this match, and the only place a FLAG can come
 *   from — but it carries hashes, not rows, and it does not know about
 *   flash bursts (excluded from in-match flagging in v1).
 *
 *   `landed` (receivedStore.thisMatch) is every artifact that arrived during
 *   this page's life, with the mime/bytes/url the report body and the thumbnail
 *   both need — but an artifact the store already HAD from a previous session
 *   (the `decline:'have'` reuse win) never appears in it, even when it rendered.
 *
 * So: flagged first, then everything else that rendered, then the rest of what
 * landed. `held` (receivedStore.list) is the lookup table that turns a rendered
 * hash into a row. A hash with no row anywhere is dropped — there is nothing to
 * show a thumbnail of and nothing to put in `mime`/`bytes`.
 *
 * AN EMPTY RESULT IS THE GATE. No peer media, no card.
 *
 * @returns {Array<{sha,kind,mime,bytes,url,flagged:boolean,rendered:boolean,at:number}>}
 */
export function reportCandidates({ landed = [], held = [], rendered = [], max = MAX_REPORT_ITEMS } = {}) {
  const rows = new Map();
  for (const r of (Array.isArray(held) ? held : [])) if (r && r.sha) rows.set(r.sha, r);
  for (const r of (Array.isArray(landed) ? landed : [])) if (r && r.sha) rows.set(r.sha, r);

  const marks = new Map();
  for (const e of (Array.isArray(rendered) ? rendered : [])) {
    if (e && typeof e.sha === 'string' && e.sha) marks.set(e.sha, e);
  }

  const order = [];
  const seen = new Set();
  const push = (sha) => {
    if (!sha || seen.has(sha) || !rows.has(sha)) return;
    seen.add(sha);
    order.push(sha);
  };

  for (const [sha, m] of marks) if (m.flagged) push(sha);
  for (const sha of marks.keys()) push(sha);
  for (const r of (Array.isArray(landed) ? landed : [])) push(r && r.sha);

  const cap = Math.max(0, Math.floor(Number(max) || 0));
  return order.slice(0, cap).map((sha) => {
    const row = rows.get(sha);
    const m = marks.get(sha) || null;
    return {
      sha,
      kind: row.kind || '',
      mime: row.mime || '',
      bytes: Math.max(0, Number(row.bytes) || 0),
      url: row.url || '',
      flagged: !!(m && m.flagged),
      rendered: !!m,
      at: m ? (Number(m.at) || 0) : 0,
    };
  });
}

export function mount(container, ctx) {
  const ledger = createLedger();
  ledger.logger = ctx?.logger || null;
  assertStageClear(ctx?.logger);

  const { actions, audio, prefs, matchLog, getMatch } = ctx;
  const match = getMatch();
  const column = el('div', { class: 'gg-recap' });
  container.appendChild(column);

  let showAllPayloads = false;

  /* ------------------------------------------------------ the report card
   * State lives HERE, not in the DOM: paint() replaces the whole column every
   * time the countersignature lands (onResultFinalized), and a half-typed note
   * that vanished because the peer answered would be its own bug report. */
  const session = ctx.session || null;
  const receivedStore = ctx.receivedStore || null;
  const getPeerRenders = typeof ctx.getPeerRenders === 'function' ? ctx.getPeerRenders : null;
  const mountedAt = Date.now();

  /**
   * THE OPPONENT'S DISPLAY NAME, or '' when this match never learned one.
   *
   * For the report card's email line (S.recap.reportEmail), which asks a player
   * to tell support WHO they were with — the one field a mail an hour later
   * cannot reconstruct, and the one this screen already knows. It falls back to
   * the Discord card's name the way resultPlates() does, and to '' rather than to
   * a placeholder: "the name of the player you were with (they)" would be worse
   * than not naming anybody, so the string has a nameless variant instead.
   * Trimmed and length-clamped because it is a peer-supplied string being read
   * back to the player as an instruction.
   */
  function peerName() {
    try {
      const card = ctx.discord ? ctx.discord.peer : null;
      const raw = (match && match.opponent && match.opponent.displayName)
        || (card && card.name) || '';
      return String(raw).trim().slice(0, 64);
    } catch (_e) { return ''; }
  }

  let reportPick = '';        // sha
  let reportReason = '';      // a WIRE code from REPORT_REASONS
  let reportNote = '';
  let reportPhase = 'idle';   // idle | submitting | done | deduped | failed
  let reportId = '';
  let reportTries = 0;

  /** The shortlist, recomputed on every paint — the store can still be settling. */
  function reportItems() {
    if (!receivedStore || !session || !session.room) return [];
    let landed = [];
    let held = [];
    try { landed = receivedStore.thisMatch ? receivedStore.thisMatch() : []; } catch (_e) { landed = []; }
    try { held = receivedStore.list ? receivedStore.list() : []; } catch (_e) { held = []; }
    let rendered = [];
    if (getPeerRenders) {
      try { rendered = (getPeerRenders() || {}).rendered || []; } catch (_e) { rendered = []; }
    }
    return reportCandidates({ landed, held, rendered });
  }

  /**
   * `at_match_ms` — WHERE in the match it appeared, for triage context only.
   *
   * It is an APPROXIMATION and the field is optional server-side. The render log
   * stamps wall-clock time (exec/videos.js is a leaf and has no match clock),
   * and the only match-relative anchor this screen has is `result.survivedMs`,
   * so the origin is taken as "mount minus the run's length". A recap that
   * mounted late (the RECAP_FALLBACK_MS path) skews it by that delay; it is
   * clamped to the run and reads 0 when there is nothing to anchor to, which is
   * exactly what the route treats as "not supplied".
   */
  function atMatchMsFor(item) {
    const at = Number(item && item.at) || 0;
    const result = match ? match.result : null;
    const survived = result ? (Number(result.survivedMs) || 0) : 0;
    if (!at || !survived) return 0;
    return Math.max(0, Math.min(survived, at - (mountedAt - survived)));
  }

  /** True while the reason picker cannot produce a report a moderator can act on. */
  function reportBlocked() {
    if (!reportPick || REPORT_REASONS.indexOf(reportReason) < 0) return true;
    // 'other' is the one reason that carries no meaning on its own.
    return reportReason === 'other' && !reportNote.trim();
  }

  async function fileReport(item) {
    if (reportPhase === 'submitting' || !item) return;
    reportPhase = 'submitting';
    paint();

    // Evidence generation is allowed to take a second (nothing is running) and
    // is allowed to FAIL. `null` is a normal outcome and the route accepts a
    // body without it — an unencodable thumbnail must never eat a report.
    let evidence = null;
    try { evidence = await evidenceFor(item); } catch (_e) { evidence = null; }
    if (ledger.isDisposed) return;

    const res = await submitReport({
      session,
      artifact: { sha: item.sha, mime: item.mime, bytes: item.bytes },
      reason: reportReason,
      note: reportNote,
      atMatchMs: atMatchMsFor(item),
      evidence,
    });
    if (ledger.isDisposed) return;

    if (res.ok && res.deduped) { reportPhase = 'deduped'; reportId = res.id; }
    else if (res.ok) { reportPhase = 'done'; reportId = res.id; }
    else { reportPhase = 'failed'; reportTries++; }
    paint();
  }

  function thumbFor(item, index) {
    const label = S.report.thumbLabel(item.kind, index + 1);
    const media = item.kind === 'video'
      ? el('video', { src: item.url, muted: true, playsinline: true, preload: 'metadata', 'aria-hidden': 'true' })
      : el('img', { src: item.url, alt: '', loading: 'lazy' });
    // A <video> element ignores the `muted` ATTRIBUTE for autoplay purposes in
    // Chromium; the property is what actually silences it, and these never play.
    try { media.muted = true; } catch (_e) { /* stub DOM */ }

    const btn = el('button', {
      type: 'button',
      class: 'gg-report-thumb' + (item.flagged ? ' is-flagged' : ''),
      'aria-pressed': item.sha === reportPick ? 'true' : 'false',
    }, [
      media,
      el('span', {
        class: 'gg-report-thumb-cap',
        text: item.flagged ? S.recap.reportFlagged : label,
      }),
    ]);
    ledger.listen(btn, 'click', (e) => {
      e.preventDefault();
      if (reportPhase === 'submitting') return;
      reportPick = (reportPick === item.sha) ? '' : item.sha;
      if (reportPhase === 'failed') { reportPhase = 'idle'; reportTries = 0; }
      try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub bus */ }
      paint();
    });
    return btn;
  }

  function reportCard() {
    const items = reportItems();
    if (!items.length) return null;              // THE GATE: no peer media, no card

    const card = el('section', { class: 'gg-card gg-recap-report' }, [
      el('h2', { class: 'gg-recap-h', text: S.recap.reportTitle }),
    ]);

    /* THE EMAIL ROAD (owner ask, 2026-08-06), and it is built here — before the
     * collapse branch below — so it appears in BOTH states. The card above files
     * against ONE ARTIFACT; serious abuse is very often not a file at all, and a
     * player who has just filed a picture-report may be exactly the one who still
     * needs a human. The name is the opponent's DISPLAY NAME when the recap has
     * one, because it is the field a stranger cannot reconstruct later and it is
     * on this screen right now; the string has a nameless variant for a match
     * that never learned one (an abandon before the hello). */
    const emailLine = () => el('p', {
      class: 'gg-report-note-line gg-report-email',
      text: S.recap.reportEmail(peerName()),
    });

    // COLLAPSED. A filed report is a closed subject: the shortlist, the picker
    // and the button all go, so the card cannot be used to file a second one by
    // accident and does not sit there looking unfinished.
    if (reportPhase === 'done' || reportPhase === 'deduped') {
      card.appendChild(el('p', {
        class: 'gg-report-status is-done',
        text: reportPhase === 'deduped' ? S.report.deduped : S.report.done(reportId),
      }));
      card.appendChild(emailLine());
      return card;
    }

    card.appendChild(el('p', { class: 'gg-report-lead', text: S.recap.reportLead }));
    card.appendChild(el('p', { class: 'gg-recap-fine', text: S.recap.reportPick }));
    card.appendChild(el('div', { class: 'gg-report-thumbs' }, items.map(thumbFor)));

    const picked = items.find((i) => i.sha === reportPick) || null;
    if (picked) {
      card.appendChild(el('p', { class: 'gg-recap-fine', text: S.report.reasonHead }));
      card.appendChild(el('div', { class: 'gg-report-reasons' }, S.report.reasons.map((r) => {
        const b = el('button', {
          type: 'button',
          class: 'gg-report-reason',
          'aria-pressed': r.code === reportReason ? 'true' : 'false',
          text: r.label,
        });
        ledger.listen(b, 'click', (e) => {
          e.preventDefault();
          if (reportPhase === 'submitting') return;
          reportReason = r.code;
          if (reportPhase === 'failed') { reportPhase = 'idle'; reportTries = 0; }
          try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub bus */ }
          paint();
        });
        return b;
      })));

      // The note is OPTIONAL everywhere except 'other', where it is the only
      // thing that says what happened.
      if (reportReason) {
        card.appendChild(el('p', { class: 'gg-recap-fine', text: S.report.noteHead }));
        const ta = el('textarea', {
          class: 'gg-report-note',
          maxlength: String(NOTE_MAX),
          placeholder: S.report.notePlaceholder,
        });
        try { ta.value = reportNote; } catch (_e) { /* stub DOM */ }
        ledger.listen(ta, 'input', () => {
          try { reportNote = String(ta.value || '').slice(0, NOTE_MAX); } catch (_e) { /* ignore */ }
        });
        card.appendChild(ta);
        card.appendChild(el('p', {
          class: 'gg-report-note-line',
          text: reportReason === 'other' && !reportNote.trim()
            ? S.report.noteNeeded
            : S.report.noteHint(NOTE_MAX),
        }));
      }

      const submitting = reportPhase === 'submitting';
      const spent = reportPhase === 'failed' && reportTries > REPORT_MAX_RETRIES;
      const send = button(
        ledger,
        submitting ? S.report.submitting : (reportPhase === 'failed' ? S.report.retry : S.report.submit),
        () => { void fileReport(picked); },
        { variant: 'primary', audio },
      );
      send.disabled = submitting || spent || reportBlocked();
      // A visible way back out. Re-clicking the tile does the same thing, but
      // "click the picture again" is not discoverable and this control must not
      // feel like a trap once it is open.
      const cancel = button(ledger, S.report.cancel, () => {
        if (reportPhase === 'submitting') return;
        reportPick = '';
        reportReason = '';
        reportNote = '';
        reportPhase = 'idle';
        reportTries = 0;
        paint();
      }, { variant: 'ghost', audio, sfx: 'ui-back' });
      cancel.disabled = submitting;
      card.appendChild(el('div', { class: 'gg-report-actions' }, [send, cancel]));

      if (reportPhase === 'failed') {
        card.appendChild(el('p', {
          class: 'gg-report-status is-failed',
          text: spent ? S.report.givenUp : S.report.failed,
        }));
      } else if (!submitting) {
        card.appendChild(el('p', { class: 'gg-report-note-line', text: S.report.evidenceNote }));
      }
    }

    card.appendChild(el('p', { class: 'gg-report-note-line', text: S.recap.reportPrivacy }));
    // LAST, and unconditional: it must be readable before anything is picked,
    // before a reason is chosen and before anything is submitted. The one thing
    // it may never be is a receipt that only exists after a successful file.
    card.appendChild(emailLine());
    return card;
  }

  /* ------------------------------------------------------------- verdict */

  function verdictCopy(result) {
    if (!result) return { hero: S.recap.draw, line: '' };
    const oppName = match?.opponent?.displayName || 'they';

    if (result.endReason === GoonEndReason.Draw) {
      return { hero: S.recap.draw, line: S.recap.drawLine, tone: 'draw' };
    }
    if (result.endReason === GoonEndReason.Abandon) {
      return { hero: S.recap.vanished, line: S.recap.abandonLine, tone: 'abandon' };
    }
    if (result.endReason === GoonEndReason.SuddenDeathLoss) {
      return {
        hero: result.localWon ? S.recap.held : S.recap.broke,
        line: S.recap.sdLine(result.localScore, result.remoteScore),
        tone: result.localWon ? 'won' : 'lost',
      };
    }
    // Mercy: whoever pressed it is the one who broke.
    const theyMercied = result.localWon;
    return {
      hero: theyMercied ? S.recap.held : S.recap.broke,
      line: S.recap.mercyLine(theyMercied ? oppName : 'you', result.survivedMs),
      tone: theyMercied ? 'won' : 'lost',
    };
  }

  /* -------------------------------------------------------------- titles */

  function computeTitles(result) {
    const out = [];
    const stats = matchLog ? matchLog.stats() : { landedOnYou: 0, enduredByYou: 0 };
    const reachedSd = !!matchLog && matchLog.sawPhase(GoonMatchPhase.SuddenDeath);
    const survived = result ? result.survivedMs : 0;
    const mercied = !!result && result.endReason === GoonEndReason.Mercy && !result.localWon;

    if (mercied && survived >= GRACEFUL_MS) out.push(S.titles.graceful);
    if (reachedSd) out.push(S.titles.ironEdge);
    if (stats.enduredByYou >= STONE_WALL_ENDURED) out.push(S.titles.stoneWall);
    if (stats.landedOnYou === 0 && survived > 0) out.push(S.titles.untouchable);
    const first = !prefs || (prefs.get('matchesPlayed') | 0) <= 1;
    if (first || (result && result.endReason === GoonEndReason.Draw)) out.push(S.titles.gg);
    return out;
  }

  /* ------------------------------------------------------------ payloads */

  function payloadRow(entry) {
    const chipFor = (status) => {
      switch (status) {
        case 'endured': return { text: S.recap.chipEndured, cls: 'is-endured', note: S.recap.chipEnduredNote };
        case 'blocked': return { text: S.recap.chipBlocked, cls: 'is-blocked' };
        case 'too_soon': return { text: S.recap.chipTooSoon, cls: 'is-toosoon' };
        default: return { text: S.recap.chipLanded, cls: 'is-landed' };
      }
    };
    const chip = chipFor(entry.status);
    return el('li', { class: 'gg-plrow gg-plrow--' + entry.dir }, [
      el('span', { class: 'gg-plrow-t', text: mmss(entry.atMs) }),
      el('span', { class: 'gg-plrow-dir', text: entry.dir === 'in' ? S.recap.dirIn : S.recap.dirOut }),
      el('span', { class: 'gg-plrow-kind', text: (typeof KIND_NAMES[entry.kind] === 'string' && KIND_NAMES[entry.kind]) || S.payloads.unknown(entry.kind) }),
      el('span', { class: 'gg-chip ' + chip.cls, text: chip.text }),
      chip.note && entry.dir === 'in' ? el('span', { class: 'gg-plrow-note', text: chip.note }) : null,
    ]);
  }

  /* --------------------------------------------------------------- sting
   * ONE sting per mount, on the FIRST paint that knows a tone — paint() runs
   * again when the countersignature lands (onResultFinalized), and hearing the
   * verdict twice would read as a second verdict. 'abandon' stays deliberately
   * silent: nobody won that, and a fanfare over a peer who vanished is a lie.
   * The generic 'recap-reveal' fires under all four at the bottom of mount(),
   * so a toneless recap is never mute. */
  const STING = { won: 'recap-won', lost: 'recap-lost', draw: 'recap-draw' };
  /** The FX beat that goes with each sting. 'abandon' has none — see below. */
  const AVA_BEAT = { won: 'win', lost: 'lose', draw: 'draw' };
  let stung = false;
  function stingFor(tone) {
    if (stung || !tone) return;
    const id = STING[tone];
    if (!id) { stung = true; return; }   // 'abandon' spends the one shot on silence
    stung = true;
    try { audio?.sfx?.(id); } catch (_e) { /* stub bus */ }
    /* THE TERMINAL BEAT, on the same one-shot latch as the sting and for the
     * same reason: paint() runs again when the countersignature lands, and a
     * second victory bounce would read as a second verdict. E latches the state
     * class, so the plates keep the result after the motion is over.
     *
     * WHO GETS TOLD IS NOT SYMMETRIC, and mirroring the wrong one is a double
     * animation. ui/avatarFx.js applies `draw` to BOTH bubbles off a single
     * event (as it does `cue`, and as it mirrors fire->alarm and mercy->bow),
     * so a draw is emitted ONCE. win/lose are not mirrored — they are two
     * different reactions and both have to be asked for by name. */
    const beat = AVA_BEAT[tone];
    if (!beat) return;
    if (beat === 'draw') { emitAva('draw', 'you'); return; }
    emitAva(beat, 'you');
    emitAva(beat === 'win' ? 'lose' : 'win', 'opp');
  }

  /* --------------------------------------------------------- result plates
   * Both avatars, side by side, under the verdict — and the ONE place a
   * "Message them" button belongs. It appears only when THEY shared DMs and
   * only after the match, because a Message button mid-duel would be a way to
   * interrupt one, and interruptions are exactly what an opponent would
   * weaponise (the same reasoning that keeps the report card off the HUD).
   *
   * The button never carries an id. It posts `discord-open-dm {which:'peer'}`
   * and the host resolves the snowflake from its own store (and un-fullscreens
   * first, §4).
   *
   * NO CONFIRM HERE, unlike the identical affordance on the HUD, and the
   * difference is the whole rule: a confirm exists to catch a surprise, and
   * mid-duel a browser opening IS one. On the end card there is no duel left to
   * interrupt and the button says exactly what it will do, so a second dialog
   * would only be a dialog. It is also a hard constraint — NOTHING on this
   * screen may open the z70 chrome (test/selftest-report.js pins it): a modal
   * over the recap is what made the end card unreachable once already. */
  const discord = ctx.discord || null;

  function resultPlates() {
    const card = discord ? discord.peer : null;
    const showOpp = !discord || discord.showOpponentAvatars;
    const youName = (match && match.localDisplayName)
      || (session && session.identity && session.identity.displayName) || S.discord.you;
    const theirName = (match && match.opponent && match.opponent.displayName)
      || (card && card.name) || S.lobby.them;

    const st = discord ? discord.state : null;
    const mine = avatarSlot({
      side: 'you',
      name: youName,
      dataUri: (discord && discord.sharingAvatar && st) ? st.avatarDataUri : null,
      size: 'plate',
    });
    const theirs = avatarSlot({
      side: 'opp',
      name: theirName,
      dataUri: (showOpp && card) ? card.avatarDataUri : null,
      size: 'plate',
    });
    if (!mine.node || !theirs.node) return null;

    const row = el('div', { class: 'gg-recap-plates' }, [mine.node, theirs.node]);
    if (showOpp && card && card.dm) {
      const dm = button(ledger, S.discord.ggMessage(theirName), () => {
        try { discord.openDm('peer'); } catch (_e) { /* the host is allowed to be gone */ }
      }, { variant: 'discord', audio });
      return el('div', { class: 'gg-recap-platewrap' }, [row, dm]);
    }
    return row;
  }

  /* ------------------------------------------------------------- the CTA
   * "what was throwing all that at you" — the one piece of marketing on this
   * page, and it is aimed at exactly one person: the STANDALONE joiner who
   * followed an invite link, endured a duel on their phone and has never seen
   * the app the payloads came out of. `session.hosted` is the same flag
   * title.js reads to decide which menu item is primary.
   *
   * HOSTED IT IS ABSENT, not merely quiet: inside WebView2 the player is
   * already in the Conditioning Control Panel, so the card would be selling
   * them the desk they are sitting at — and a target=_blank in the host opens
   * nothing useful anyway.
   *
   * It is appended BELOW the actions on purpose. The rule the report card
   * follows applies here twice over: nothing this screen adds may sit between
   * the player and "Back to menu".
   *
   * A plain <a>, not a button — a real link is middle-clickable, copyable and
   * needs no host round-trip. `rel="noopener"` because target=_blank without
   * it hands the new tab a window.opener back into this page.
   */
  const standalone = !(session && session.hosted);

  function ctaCard() {
    if (!standalone) return null;
    const link = el('a', {
      class: 'gg-btn gg-btn--primary gg-recap-cta-link',
      href: EXPLORE_URL,
      target: '_blank',
      rel: 'noopener',
      text: S.recap.ctaLink,
    });
    ledger.listen(link, 'click', () => {
      try { audio?.sfx?.('ui-select'); } catch (_e) { /* stub bus */ }
    });
    return el('section', { class: 'gg-card gg-recap-cta' }, [
      el('h2', { class: 'gg-recap-h', text: S.recap.ctaTitle }),
      el('p', { class: 'gg-recap-cta-lead', text: S.recap.ctaLead }),
      link,
      el('p', { class: 'gg-recap-fine', text: S.recap.ctaFine }),
    ]);
  }

  /* ------------------------------------------------------------- rivalry
   * Booked ONCE per match (settleOnce latches on the match object), on the
   * first paint that has a result. Practice never books. The line reads the
   * stored record back, so it already includes this match. */
  const rivalry = ctx.rivalry || null;
  const practice = typeof ctx.isPractice === 'function' ? !!ctx.isPractice() : false;
  function rivalLine() {
    if (!rivalry || practice || !match) return '';
    try {
      settleOnce(match, rivalry, { practice });
      const name = match.opponent ? match.opponent.displayName : '';
      return formatRecord(rivalry.recordFor(name), name);
    } catch (_e) { return ''; }
  }

  /* ---------------------------------------------------------- share card
   * THE CARD PLAYERS POST (2026-09-24). One persistent node: paint() re-appends
   * it rather than rebuilding it, so a countersignature landing mid-copy does not
   * throw the picture away. It redraws only when what it shows changed (cardKey).
   * No match picture ever goes on it (ui/shareCard.js): it is made for public
   * channels. Copy and Save never open a sheet or a modal here; a hosted Save is
   * the host's own file dialog. */
  const shareNode = el('section', { class: 'gg-card gg-recap-share' });
  let shareKey = '';
  let shareCanvas = null;
  let shareWord = '';
  let shareUrl = '';
  let shareBusy = false;
  let shareShown = false;
  ledger.add(() => { if (shareUrl) { try { URL.revokeObjectURL(shareUrl); } catch (_e) { /* gone */ } } });

  function shareData(result) {
    const st = discord ? discord.state : null;
    const card = discord ? discord.peer : null;
    const showOpp = !discord || discord.showOpponentAvatars;
    let flavour = '';
    try { const m = ctx.mediaFlavour && ctx.mediaFlavour.get ? ctx.mediaFlavour.get() : null; flavour = (m && m.flavour) || ''; } catch (_e) { flavour = ''; }
    let highlights = [];
    try { highlights = computeTitles(result).map((t) => t.name); } catch (_e) { highlights = []; }
    let outcome = null;
    try { outcome = outcomeOf(result); } catch (_e) { outcome = null; }
    return buildShareData({
      result,
      outcome,
      log: matchLog,
      duels: duelSummary(match),
      /* Per-player stats from the points model (match.matchStats), in shareWords.statsFromScoring's
       * shape; null in an old-score match, which keeps the match-log numbers. */
      scoring: (match && match.scoreCard) || scoreCardOf(match),
      youName: (match && match.localDisplayName) || (session && session.identity && session.identity.displayName) || '',
      themName: peerName(),
      youAvatar: (discord && discord.sharingAvatar && st) ? st.avatarDataUri : '',
      themAvatar: (showOpp && card) ? card.avatarDataUri : '',
      flavour,
      seed: match ? match.matchSeed : 0,
      highlights,
    });
  }

  function shareToast(ok, good, bad) {
    try { if (ok) ctx.toasts?.good?.(good); else ctx.toasts?.warn?.(bad); } catch (_e) { /* toasts are optional */ }
  }

  function paintShare(tint) {
    shareNode.replaceChildren(
      el('h2', { class: 'gg-recap-h', text: S.share.title }),
      el('p', { class: 'gg-recap-fine', text: S.share.lead }),
    );
    if (!shareUrl) {
      shareNode.appendChild(el('div', { class: 'gg-share-preview is-pending', text: S.share.preparing }));
      return;
    }
    const img = el('img', { class: 'gg-share-preview', src: shareUrl, alt: S.share.alt(shareWord) });
    shareNode.appendChild(img);
    const copy = button(ledger, S.share.copy, async () => {
      if (shareBusy || !shareCanvas) return;
      shareBusy = true;
      squash(copy);
      const r = await copyCard(shareCanvas);
      shareBusy = false;
      if (ledger.isDisposed) return;
      shareToast(r.ok, S.share.copied, S.share.copyFailed);
      if (r.ok) { const c = centreOf(copy); if (c && c.w) burst(c.x, c.y, { count: 14, dist: 60, color: '255, 212, 94' }); }
    }, { variant: 'primary', audio });
    const save = button(ledger, S.share.save, async () => {
      if (shareBusy || !shareCanvas) return;
      shareBusy = true;
      squash(save);
      const r = await saveCard(shareCanvas, cardFileName(Date.now()));
      shareBusy = false;
      if (ledger.isDisposed || r.error === 'cancelled') return;
      shareToast(r.ok, S.share.saved, S.share.saveFailed);
    }, { variant: 'ghost', audio });
    shareNode.appendChild(el('div', { class: 'gg-share-actions' }, [copy, save]));
    if (!shareShown) {
      shareShown = true;
      popIn(img, { from: 0.86, over: 1.03, ms: 420, delay: 120 });
      if (!isCalm()) {
        ledger.timer(() => {
          const c = centreOf(img);
          if (c && c.w) burst(c.x, c.y, { count: 18, dist: 110, spread: 70, color: hexRgb(tint) });
        }, 260);
      }
    }
  }

  function hexRgb(hex) {
    const n = parseInt(String(hex || '#ff69b4').slice(1), 16) || 0;
    return ((n >> 16) & 255) + ', ' + ((n >> 8) & 255) + ', ' + (n & 255);
  }

  function refreshShare(result) {
    let data = null;
    try { data = shareData(result); } catch (_e) { data = null; }
    if (!data) return;
    const key = cardKey(data);
    const tint = FLAVOUR_TINTS[data.flavour] || FLAVOUR_TINTS.plain;
    if (key === shareKey) return;
    shareKey = key;
    if (!shareUrl) paintShare(tint);
    void (async () => {
      const r = await renderCard(data);
      if (ledger.isDisposed || key !== shareKey || !r) return;
      let blob = null;
      try { blob = await canvasBlob(r.canvas); } catch (_e) { blob = null; }
      if (ledger.isDisposed || key !== shareKey || !blob) return;
      if (shareUrl) { try { URL.revokeObjectURL(shareUrl); } catch (_e) { /* gone */ } }
      shareCanvas = r.canvas;
      shareWord = r.word;
      shareUrl = URL.createObjectURL(blob);
      paintShare(tint);
    })();
  }

  /* --------------------------------------------------------------- paint */

  function paint() {
    const result = match ? match.result : null;
    const v = verdictCopy(result);
    stingFor(v.tone);
    column.replaceChildren();

    /* --- hero ---
     * Three states, not two. The verdict is painted from the LOCAL result the
     * moment the match ends, because that is when this screen mounts — the
     * countersignature can be up to the engine's 10 s handshake behind it, and
     * a peer that vanished may never send one at all. Rather than stall on a
     * blank screen (or, worse, leave the player behind an interstitial waiting
     * for a frame that is not coming), say plainly that it is unconfirmed and
     * repaint when onResultFinalized lands. */
    const badge = !result ? null
      : result.disputed ? { cls: 'gg-badge--disputed', text: S.recap.disputed }
        : !result.agreed ? { cls: 'gg-badge--unconfirmed', text: S.recap.unconfirmed }
          : null;
    const hero = el('section', { class: 'gg-card gg-recap-hero is-' + (v.tone || 'draw') }, [
      el('h1', { class: 'gg-recap-verdict gg-grad', text: v.hero }),
      v.line ? el('p', { class: 'gg-recap-reason', text: v.line }) : null,
      badge ? el('span', { class: 'gg-badge ' + badge.cls, text: badge.text }) : null,
    ]);
    /* --- THE PLATES: two faces under the verdict.
     * This is also the moment the HOST writes the last-opponent record (it fires
     * on `match-result` and already holds the peer card) — the page does nothing
     * for that, sends nothing, and must not try to help. All that happens here
     * is that the two bubbles the whole match was drawn around get their last
     * frame, and E latches the win/lose state onto them. */
    try {
      const plates = resultPlates();
      if (plates) hero.appendChild(plates);
    } catch (e) {
      try { ctx?.logger?.warn?.('recap: plates failed to build: ' + ((e && e.message) || e)); }
      catch (_e2) { /* logger is optional */ }
    }
    column.appendChild(hero);

    /* --- scoreline --- */
    if (result) {
      /* THE MULTIPLIER IS NOT READ HERE ANY MORE (2026-08-05). This block used
       * to pull `match.scoring.riskMultiplier` — the engine's frozen name for
       * the pool's score bonus — and print it in the fine print. It was the
       * recap's copy of the live HUD's risk readout and left with it; the
       * scoreline is the multiplier's whole effect, already added up. The
       * engine value is untouched, it simply has no reader on any screen. */
      column.appendChild(el('section', { class: 'gg-card gg-recap-score' }, [
        el('h2', { class: 'gg-recap-h', text: S.recap.scoreline }),
        el('div', { class: 'gg-scoreline' }, [
          el('span', { class: 'gg-scorenum is-you', text: String(result.localScore) }),
          el('span', { class: 'gg-scoredash', text: '·' }),
          el('span', { class: 'gg-scorenum is-them', text: String(result.remoteScore) }),
        ]),
        el('p', { class: 'gg-recap-fine', text: S.recap.scoreFineprint }),
        el('p', { class: 'gg-recap-fine', text: S.recap.survived(result.survivedMs) }),
        el('p', { class: 'gg-rival-line', text: rivalLine() }),
        duelSummary(match).won > 0 && el('p', { class: 'gg-recap-fine', text: DUEL_COPY.recapLine(duelSummary(match).won) }),
      ]));
    }

    /* --- the share card: right under the numbers it is made of --- */
    if (result) {
      try {
        refreshShare(result);
        column.appendChild(shareNode);
      } catch (e) {
        try { ctx?.logger?.warn?.('recap: share card failed to build: ' + ((e && e.message) || e)); }
        catch (_e2) { /* logger is optional */ }
      }
    }

    /* --- payload log --- */
    const entries = matchLog ? matchLog.payloads() : [];
    const shown = showAllPayloads ? entries : entries.slice(0, COLLAPSE_AT);
    const list = el('ul', { class: 'gg-pllist' }, shown.map(payloadRow));
    const logCard = el('section', { class: 'gg-card gg-recap-log' }, [
      el('h2', { class: 'gg-recap-h', text: S.recap.payloads }),
      entries.length ? list : el('p', { class: 'gg-recap-fine', text: S.recap.noPayloads }),
    ]);
    if (!showAllPayloads && entries.length > COLLAPSE_AT) {
      logCard.appendChild(button(ledger, S.recap.showAll(entries.length), () => {
        showAllPayloads = true;
        paint();
      }, { variant: 'ghost', audio }));
    }
    column.appendChild(logCard);

    /* --- titles --- */
    const titles = computeTitles(result);
    if (titles.length) {
      column.appendChild(el('section', { class: 'gg-card gg-recap-titles' }, [
        el('h2', { class: 'gg-recap-h', text: S.recap.titles }),
        el('div', { class: 'gg-titlestrip' }, titles.map((t) => el('div', { class: 'gg-title-chip' }, [
          el('span', { class: 'gg-title-chip-name', text: t.name }),
          el('span', { class: 'gg-title-chip-why', text: t.why }),
        ]))),
      ]));
    }

    /* --- report what they sent ---
     * Below the log and above the actions on purpose: it is a consequence of
     * what the log describes, and it must never sit between the player and
     * "Back to menu". It renders at all only when a duel partner's own media
     * reached this machine (reportCandidates is the gate). */
    try {
      const rc = reportCard();
      if (rc) column.appendChild(rc);
    } catch (e) {
      // A card that cannot build must never take the recap down with it —
      // the recap is the screen the player has to be able to LEAVE from.
      try { ctx?.logger?.warn?.('recap: report card failed to build: ' + ((e && e.message) || e)); }
      catch (_e2) { /* logger is optional */ }
    }

    /* --- actions ---
     * REMATCH IS THE SMALLEST HONEST VERSION. The room is spent the moment the
     * match ends, so a rematch is a fresh room: the host's button opens one (a
     * new link to send), the guest's lands on the join screen ready for it, and
     * practice simply goes again. No new wire frame, so an old peer cannot be
     * confused by it. Disabled only when the page gave us no road to take. */
    const canRematch = !!(actions && typeof actions.rematch === 'function');
    const rematch = button(ledger, S.recap.rematch, () => {
      if (canRematch) void actions.rematch();
    }, { variant: 'ghost', audio });
    rematch.disabled = !canRematch;
    // gg-menu-item carries `position: relative` - without it the absolutely
    // positioned note escapes to the nearest positioned ancestor and lands at
    // the bottom of the page. (It did.)
    rematch.classList.add('gg-menu-item', 'has-note');
    rematch.appendChild(el('span', {
      class: 'gg-menu-note',
      text: !canRematch ? S.recap.rematchSoon
        : practice ? S.recap.rematchPractice
          : (match && match.isHost) ? S.recap.rematchHost : S.recap.rematchGuest,
    }));
    const back = button(ledger, S.recap.back, () => actions.leave('recap'), { variant: 'primary', audio, sfx: 'ui-back' });
    column.appendChild(el('div', { class: 'gg-recap-actions' }, [rematch, back]));

    /* --- discover the app (standalone only, and last) --- */
    try {
      const cta = ctaCard();
      if (cta) column.appendChild(cta);
    } catch (e) {
      // An ad must never be the reason a player cannot leave the end card.
      try { ctx?.logger?.warn?.('recap: cta failed to build: ' + ((e && e.message) || e)); }
      catch (_e2) { /* logger is optional */ }
    }
  }

  /**
   * THE REVEAL (juice pass 2026-09-23), first paint only; a repaint (the
   * countersignature landing) just swaps the numbers. The router cascades the
   * cards in; on top of that the verdict THUDs, each score counts up from zero
   * with a climbing pentatonic tick, the payload rows cascade inside their card
   * and the title chips pop one by one. Reduced motion: numbers land at once,
   * everything else is the router's fade.
   */
  function reveal() {
    try {
      const q = (sel) => Array.from(column.querySelectorAll ? column.querySelectorAll(sel) : []);
      const calm = isCalm();
      const verdict = q('.gg-recap-verdict')[0];
      if (verdict && !calm) {
        play(verdict, [
          { opacity: 0, transform: 'scale(1.8) rotate(-4deg)' },
          { opacity: 1, transform: 'scale(1) rotate(0deg)' },
        ], { duration: 340, delay: 140, easing: THUD_EASE, fill: 'backwards' });
      }
      let rung = 0;
      q('.gg-scorenum').forEach((node, i) => {
        const final = parseInt(node.textContent, 10);
        if (!Number.isFinite(final)) return;
        const stop = countUp(node, 0, final, {
          delay: 360 + i * 180,
          onStep: () => { if (!calm) { try { audio?.tone?.(rung++ % 10, { ms: 90 }); } catch (_e) { /* stub bus */ } } },
          onDone: () => {
            if (calm) return;
            popIn(node, { from: 0.8, over: 1.25, ms: 300 });
            const c = centreOf(node);
            if (c && c.w && node.classList.contains('is-you')) burst(c.x, c.y, { count: 10, dist: 50, spread: 40, color: '255, 212, 94' });
          },
        });
        ledger.add(stop);
      });
      staggerIn(q('.gg-pllist > li').slice(0, 14), { start: 420, step: 55 });
      const chips = q('.gg-title-chip');
      const delays = staggerDelays(chips.length, { start: 700, step: 120, max: 600 });
      chips.forEach((chip, i) => {
        popIn(chip, { delay: delays[i], from: 0.5, over: 1.12, ms: 340 });
        if (!calm) ledger.timer(() => { try { audio?.tone?.(4 + i, { ms: 160 }); } catch (_e) { /* stub bus */ } }, delays[i] + 80);
      });
    } catch (_e) { /* the reveal is decoration: the recap stands without it */ }
  }

  if (match) {
    ledger.sub(match.onResultFinalized(() => { if (!ledger.isDisposed) paint(); }));
    ledger.sub(match.onMatchEnded(() => { if (!ledger.isDisposed) paint(); }));
  }
  // A peer card is fetched fire-and-forget and can land AFTER the end card is
  // already up — that is the design (§7: it never gates anything), so the plate
  // has to be able to grow a face late rather than the screen waiting for one.
  if (discord && typeof discord.subscribe === 'function') {
    ledger.add(discord.subscribe(() => { if (!ledger.isDisposed) paint(); }));
  }
  if (prefs) prefs.set('matchesPlayed', (prefs.get('matchesPlayed') | 0) + 1);
  // Game Night's own count (ui/nightProgress.js): game cards unlock from the second one.
  // Once per match object (the recap can be shown again for the same match), and only for a
  // REAL result: an early abandon or a result that never finalized counts for nothing. The
  // result can land after this screen mounts, so the check rides the same repaint hooks.
  function countFinished() {
    if (practice || !match || countedMatches.has(match)) return;
    let real = false;
    try { real = outcomeOf(match.result) != null; } catch (_e) { real = false; }
    if (!real) return;
    countedMatches.add(match);
    try { noteMatchFinished(); } catch (_e) { /* never breaks the recap */ }
  }
  countFinished();
  if (match) ledger.sub(match.onResultFinalized(() => countFinished()));

  paint();
  reveal();
  try { audio?.sfx?.('recap-reveal'); } catch (_e) { /* stub bus */ }
  try { audio?.music?.('recap'); } catch (_e) { /* stub bus */ }
  ledger.add(() => { try { audio?.stopMusic?.(); } catch (_e) { /* stub bus */ } });

  return { unmount() { ledger.dispose(); } };
}

export default { mount };

/** The scoring lane's ledger in the card's { you, them } shape; null outside the points model
 *  (an old-score match keeps the card's match-log numbers). */
function scoreCardOf(match) {
  try {
    if (!match || typeof match.matchStats !== 'function') return null;
    const s = match.matchStats();
    if (!s || !s.pointsModel || !s.me) return null;
    return { you: s.me, them: s.them || null };
  } catch (_e) { return null; }
}
