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
import { GoonEndReason, GoonMatchPhase } from '../../core/contracts.js';

const COLLAPSE_AT = 6;
const GRACEFUL_MS = 8 * 60 * 1000;
const STONE_WALL_ENDURED = 4;

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

export function mount(container, ctx) {
  const ledger = createLedger();
  ledger.logger = ctx?.logger || null;
  assertStageClear(ctx?.logger);

  const { actions, audio, prefs, matchLog, getMatch } = ctx;
  const match = getMatch();
  const column = el('div', { class: 'gg-recap' });
  container.appendChild(column);

  let showAllPayloads = false;

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

  /* --------------------------------------------------------------- paint */

  function paint() {
    const result = match ? match.result : null;
    const v = verdictCopy(result);
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
  if (prefs) prefs.set('matchesPlayed', (prefs.get('matchesPlayed') | 0) + 1);

  paint();
  try { audio?.sfx?.('recap-reveal'); } catch (_e) { /* stub bus */ }
  try { audio?.music?.('recap'); } catch (_e) { /* stub bus */ }
  ledger.add(() => { try { audio?.stopMusic?.(); } catch (_e) { /* stub bus */ } });

  return { unmount() { ledger.dispose(); } };
}

export default { mount };
