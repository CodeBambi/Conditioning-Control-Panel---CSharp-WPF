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

export function mount(container, ctx) {
  const ledger = createLedger();
  ledger.logger = ctx?.logger || null;

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

    /* --- hero --- */
    const hero = el('section', { class: 'gg-card gg-recap-hero is-' + (v.tone || 'draw') }, [
      el('h1', { class: 'gg-recap-verdict gg-grad', text: v.hero }),
      v.line ? el('p', { class: 'gg-recap-reason', text: v.line }) : null,
      result && result.disputed ? el('span', { class: 'gg-badge gg-badge--disputed', text: S.recap.disputed }) : null,
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
