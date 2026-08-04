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

const COLLAPSE_AT = 6;
const GRACEFUL_MS = 8 * 60 * 1000;
const STONE_WALL_ENDURED = 4;

/** How many artifacts the report shortlist ever shows. Flagged ones come first. */
export const MAX_REPORT_ITEMS = 6;

/** One retry on a failed submit, then the card stops offering false hope. */
export const REPORT_MAX_RETRIES = 1;

const KIND_NAMES = Object.freeze({
  0: 'flash burst', 1: 'subliminal storm', 2: 'bubble swarm',
  3: 'video', 4: 'lock card', 5: 'toy pattern', 6: 'brain drain',
});

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

    // COLLAPSED. A filed report is a closed subject: the shortlist, the picker
    // and the button all go, so the card cannot be used to file a second one by
    // accident and does not sit there looking unfinished.
    if (reportPhase === 'done' || reportPhase === 'deduped') {
      card.appendChild(el('p', {
        class: 'gg-report-status is-done',
        text: reportPhase === 'deduped' ? S.report.deduped : S.report.done(reportId),
      }));
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
      el('span', { class: 'gg-plrow-kind', text: KIND_NAMES[entry.kind] || ('kind ' + entry.kind) }),
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
      const risk = match?.scoring?.riskMultiplier || 1;
      column.appendChild(el('section', { class: 'gg-card gg-recap-score' }, [
        el('h2', { class: 'gg-recap-h', text: S.recap.scoreline }),
        el('div', { class: 'gg-scoreline' }, [
          el('span', { class: 'gg-scorenum is-you', text: String(result.localScore) }),
          el('span', { class: 'gg-scoredash', text: '·' }),
          el('span', { class: 'gg-scorenum is-them', text: String(result.remoteScore) }),
        ]),
        el('p', { class: 'gg-recap-fine', text: S.recap.scoreFineprint(risk) }),
        el('p', { class: 'gg-recap-fine', text: S.recap.survived(result.survivedMs) }),
      ]));
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

    /* --- actions --- */
    // Rematch needs a fresh room (the old one is spent) — that is v2. It ships
    // visible and disabled rather than absent, so the shape of the screen does
    // not move when it arrives.
    const rematch = button(ledger, S.recap.rematch, () => {}, { variant: 'ghost', audio });
    rematch.disabled = true;
    // gg-menu-item carries `position: relative` — without it the absolutely
    // positioned note escapes to the nearest positioned ancestor and lands at
    // the bottom of the page. (It did.)
    rematch.classList.add('gg-menu-item', 'has-note');
    rematch.appendChild(el('span', { class: 'gg-menu-note', text: S.recap.rematchSoon }));
    const back = button(ledger, S.recap.back, () => actions.leave('recap'), { variant: 'primary', audio, sfx: 'ui-back' });
    column.appendChild(el('div', { class: 'gg-recap-actions' }, [rematch, back]));
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

  paint();
  try { audio?.sfx?.('recap-reveal'); } catch (_e) { /* stub bus */ }
  try { audio?.music?.('recap'); } catch (_e) { /* stub bus */ }
  ledger.add(() => { try { audio?.stopMusic?.(); } catch (_e) { /* stub bus */ } });

  return { unmount() { ledger.dispose(); } };
}

export default { mount };
