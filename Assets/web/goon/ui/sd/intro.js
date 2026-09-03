/* ============================================================================
 * ui/sd/intro.js — the 3-2-1 card that opens every sudden-death round.
 *
 * One kind title, ONE rule line, one difficulty chip, one countdown. The round
 * is scheduled on the shared clock, so when a clock is available the countdown
 * is synced to fireAtMatchMs rather than to whenever this presenter happened to
 * be called; without one it counts down from the call, which is what the runner
 * gives us anyway (MinScheduleBuffer + CountdownMs).
 * ==========================================================================*/

import { GoonRoundKind } from '../../core/contracts.js';

/** kind -> {title, rule}. The rule line is the whole tutorial. */
export const ROUND_COPY = Object.freeze({
  [GoonRoundKind.QuickDrawLockCard]: { title: 'quick draw', rule: 'type the card first.' },
  [GoonRoundKind.StaringContest]: { title: 'staring contest', rule: "don't blink." },
  [GoonRoundKind.ReactionDuel]: { title: 'reaction duel', rule: 'hit it when it turns pink. not before.' },
  [GoonRoundKind.BubbleRace]: { title: 'bubble race', rule: 'pop them all. faster than them.' },
});

const MAX_COUNTDOWN_MS = 10000;

export function createIntro(ctx) {
  const { el, add, cls, text, sfx } = ctx;
  let node = null;
  let timer = 0;
  let lastNumber = -1;

  function build() {
    if (node) return node;
    node = el('div', 'gg-sd-intro');
    if (!node) return null;
    add(node, el('div', 'gg-scrim'));
    const card = add(node, el('div', 'gg-sd-intro-card'));
    node._title = add(card, el('h2', 'gg-sd-intro-title gg-grad', ''));
    node._rule = add(card, el('p', 'gg-sd-intro-rule', ''));
    node._chip = add(card, el('span', 'gg-sd-chip', ''));
    node._count = add(card, el('div', 'gg-sd-count', ''));
    ctx.mountOverlay(node);
    return node;
  }

  function remainingMs(intro) {
    const clock = ctx.clock();
    if (clock && typeof clock.nowMatchMs === 'function' && intro && intro.fireAtMatchMs) {
      try {
        const left = intro.fireAtMatchMs - clock.nowMatchMs();
        if (Number.isFinite(left)) return Math.max(0, Math.min(MAX_COUNTDOWN_MS, left));
      } catch (_e) { /* fall through to the local countdown */ }
    }
    return 3000;
  }

  function show(intro) {
    const n = build();
    if (!n) return;
    const copy = ROUND_COPY[intro && intro.kind] || { title: 'round', rule: 'hold on.' };
    text(n._title, copy.title);
    text(n._rule, copy.rule);
    text(n._chip, 'level ' + Math.max(1, (intro && intro.difficulty) | 0));
    n.hidden = false;
    cls(n, 'is-in', true);
    lastNumber = -1;

    const endsAt = Date.now() + remainingMs(intro);
    stopTimer();
    const step = () => {
      const left = endsAt - Date.now();
      const num = Math.ceil(left / 1000);
      if (left <= 0) { text(n._count, 'go'); sfx('gg-go'); hideSoon(); return; }
      if (num !== lastNumber && num <= 3) { lastNumber = num; sfx('gg-tick'); }
      text(n._count, num <= 3 ? String(num) : 'get ready');
      cls(n._count, 'is-beat', num <= 3);
    };
    step();
    timer = setInterval(step, 100);
  }

  function hideSoon() {
    stopTimer();
    timer = setTimeout(hide, 450);
  }

  function stopTimer() {
    try { clearInterval(timer); } catch (_e) { /* gone */ }
    try { clearTimeout(timer); } catch (_e) { /* gone */ }
    timer = 0;
  }

  function hide() {
    stopTimer();
    if (!node) return;
    cls(node, 'is-in', false);
    node.hidden = true;
  }

  return {
    show,
    hide,
    dispose() {
      stopTimer();
      if (node) { try { node.remove(); } catch (_e) { /* gone */ } node = null; }
    },
  };
}
