/* ============================================================================
 * ui/sd/verdict.js — the 2.2 s card that closes a sudden-death round.
 *
 * Says who took it, shows BOTH measurements side by side (the peer's numbers
 * only exist at this point — nothing about their round is transmitted while it
 * is being played), moves the net track, and badges a suspiciously fast press
 * without ever calling the player a cheat: the round scored it, and the chip
 * says so.
 * ==========================================================================*/

import { GoonRoundKind } from '../../core/contracts.js';
import { S } from '../strings.js';
import { GoonRoundVerdict } from '../../core/rounds/model.js';

const DWELL_MS = 2200;

function ms(v) { return (v == null) ? null : Math.max(0, v | 0); }

function secs(v) {
  const n = ms(v);
  return n == null ? '—' : (n / 1000).toFixed(1) + 's';
}

/** The one line that puts both players' work next to each other. */
export function comparisonLine(outcome) {
  const local = (outcome && outcome.local) || {};
  const peer = (outcome && outcome.peer) || {};
  switch (outcome && outcome.kind) {
    case GoonRoundKind.ReactionDuel: {
      const a = ms(local.reaction_ms);
      const b = ms(peer.reaction_ms);
      return S.sd.cmp(a == null ? S.sd.noPress : S.sd.ms(a), b == null ? S.sd.noPress : S.sd.ms(b));
    }
    case GoonRoundKind.BubbleRace:
      return S.sd.cmpPopped(local.progress | 0, peer.progress | 0);
    case GoonRoundKind.StaringContest:
      return S.sd.cmp(secs(local.elapsed_ms), secs(peer.elapsed_ms));
    default:
      return S.sd.cmp(secs(local.elapsed_ms), secs(peer.elapsed_ms));
  }
}

export function createVerdict(ctx) {
  const { el, add, cls, text, sfx } = ctx;
  let node = null;
  let timer = 0;

  function build() {
    if (node) return node;
    node = el('div', 'gg-sd-verdict');
    if (!node) return null;
    const card = add(node, el('div', 'gg-sd-verdict-card gg-plate'));
    node._head = add(card, el('h3', 'gg-sd-verdict-head', ''));
    node._line = add(card, el('p', 'gg-sd-verdict-line', ''));
    node._chip = add(card, el('span', 'gg-sd-suspect', S.sd.suspect));
    node._note = add(card, el('p', 'gg-sd-verdict-note', ''));
    ctx.mountOverlay(node);
    return node;
  }

  function show(outcome) {
    const n = build();
    if (!n) return;

    const v = outcome ? outcome.verdict : null;
    let head = S.sd.aborted;
    let tone = 'draw';
    if (v === GoonRoundVerdict.Win) { head = S.sd.toYou; tone = 'win'; }
    else if (v === GoonRoundVerdict.Loss) { head = S.sd.toThem; tone = 'loss'; }
    else if (v === GoonRoundVerdict.Draw) { head = S.sd.deadHeat; tone = 'draw'; }
    else { tone = 'abort'; }

    text(n._head, head);
    for (const t of ['win', 'loss', 'draw', 'abort']) cls(n, 'is-' + t, t === tone);
    text(n._line, outcome ? comparisonLine(outcome) : S.sd.nobody);

    const suspect = !!(outcome && (outcome.localSuspect || outcome.peerSuspect));
    if (n._chip) n._chip.hidden = !suspect;
    text(n._note, suspect ? S.sd.tooFast : '');

    if (outcome && typeof outcome.netScore === 'number') ctx.setNet(outcome.netScore);
    if (outcome && typeof outcome.roundNo === 'number') ctx.setRoundLabel(outcome.roundNo, outcome.kind);

    sfx(v === GoonRoundVerdict.Win ? 'gg-round-win' : v === GoonRoundVerdict.Loss ? 'gg-round-loss' : 'gg-tick');

    n.hidden = false;
    cls(n, 'is-in', true);
    try { clearTimeout(timer); } catch (_e) { /* gone */ }
    timer = setTimeout(hide, DWELL_MS);
  }

  function hide() {
    try { clearTimeout(timer); } catch (_e) { /* gone */ }
    if (!node) return;
    cls(node, 'is-in', false);
    node.hidden = true;
  }

  return {
    show,
    hide,
    dispose() {
      try { clearTimeout(timer); } catch (_e) { /* gone */ }
      if (node) { try { node.remove(); } catch (_e) { /* gone */ } node = null; }
    },
  };
}
