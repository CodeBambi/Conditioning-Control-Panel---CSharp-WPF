/* ============================================================================
 * ui/sd/quickdraw.js — the quick-draw lock card, centre stage.
 *
 * Prefers exec/lockCards.js's createLockCardView (the shared card the executor
 * also uses for LockCard payloads). If that module is not present at runtime we
 * render a minimal inline card instead so the round is still playable — the
 * sudden-death ladder must never be blocked on a sibling file.
 *
 * NO GHOST BAR. The peer's progress is not transmitted mid-round (only the
 * round_result at the end is), so anything drawn as "their progress" would be a
 * lie. What we show instead is honest: your own repeats as dots, and your slips.
 *
 * ESC IS NOT BOUND HERE. Esc belongs to MERCY for the whole match (the card's
 * own spec.strict is always false for exactly this reason), so abandoning a card
 * is an explicit, visible control instead of a keypress that could concede.
 * ==========================================================================*/

export function createQuickDraw(ctx) {
  const { el, add, cls, text, sfx } = ctx;
  let node = null;
  let view = null;
  let mistakes = 0;
  let done = 0;
  let usedFallback = false;

  function build() {
    if (node) return node;
    node = el('div', 'gg-sd-card');
    if (!node) return null;
    node._slot = add(node, el('div', 'gg-sd-card-slot'));
    node._dots = add(node, el('div', 'gg-sd-card-dots'));
    node._slips = add(node, el('div', 'gg-sd-card-slips', 'slips 0'));
    ctx.mountStage(node);
    return node;
  }

  function paintDots(repeats) {
    const host = node && node._dots;
    if (!host) return;
    try { host.replaceChildren(); } catch (_e) { host.textContent = ''; }
    for (let i = 0; i < repeats; i++) {
      const dot = add(host, el('i', 'gg-sd-dot'));
      cls(dot, 'is-on', i < done);
    }
    const label = add(host, el('span', 'gg-sd-dot-label', done + ' of ' + repeats));
    if (!label) return;
  }

  /** exec/lockCards.js passes the running count; the inline card passes nothing. */
  function onMistake(count) {
    mistakes = (typeof count === 'number' && count > mistakes) ? count : mistakes + 1;
    text(node && node._slips, 'slips ' + mistakes);
    cls(node, 'is-slip', true);
    setTimeout(() => cls(node, 'is-slip', false), 160);
  }

  function show(spec) {
    const n = build();
    if (!n) return;
    mistakes = 0;
    done = 0;
    const repeats = Math.max(1, (spec && spec.repeats) | 0);
    paintDots(repeats);
    text(n._slips, 'slips 0');
    n.hidden = false;
    cls(n, 'is-in', true);

    const factory = ctx.lockCardView();
    if (typeof factory === 'function') {
      try {
        view = factory(n._slot, {
          phrase: (spec && spec.phrase) || '',
          repeats,
          onSolved: (e) => {
            done = repeats;
            paintDots(repeats);
            const m = (e && typeof e.mistakes === 'number') ? e.mistakes : mistakes;
            ctx.raise.solved(m);
          },
          onAbandoned: () => ctx.raise.abandoned(),
          onMistake,
        });
        usedFallback = false;
        return;
      } catch (_e) {
        view = null;   // fall through to the inline card
      }
    }
    usedFallback = true;
    buildFallback(n._slot, spec, repeats);
  }

  /** The card we render when exec/lockCards.js is not there. Deliberately plain. */
  function buildFallback(slot, spec, repeats) {
    if (!slot) return;
    try { slot.replaceChildren(); } catch (_e) { slot.textContent = ''; }
    const phrase = (spec && spec.phrase) || '';

    add(slot, el('div', 'gg-lc-kicker', 'type it'));
    add(slot, el('div', 'gg-lc-phrase', phrase));
    const input = add(slot, el('input', 'gg-lc-input'));
    const hint = add(slot, el('div', 'gg-lc-hint', 'press enter for each line'));
    const give = add(slot, el('button', 'gg-lc-give', 'give up on this card'));
    if (give) give.type = 'button';
    if (input) {
      input.type = 'text';
      input.autocomplete = 'off';
      input.spellcheck = false;
      input.setAttribute && input.setAttribute('aria-label', 'type the phrase');
      const onKey = (e) => {
        if (!e || e.key !== 'Enter') return;
        const typed = String(input.value || '').trim().toLowerCase();
        if (typed === phrase.trim().toLowerCase()) {
          done++;
          input.value = '';
          paintDots(repeats);
          sfx('gg-check-ok');
          if (done >= repeats) ctx.raise.solved(mistakes);
        } else if (typed.length) {
          onMistake();
          sfx('gg-check');
        }
      };
      input.addEventListener('keydown', onKey);
      ctx.own(() => { try { input.removeEventListener('keydown', onKey); } catch (_e) { /* gone */ } });
      try { input.focus(); } catch (_e) { /* headless */ }
      if (hint) hint.hidden = repeats <= 1;
    }
    if (give) {
      const onGive = () => ctx.raise.abandoned();
      give.addEventListener('click', onGive);
      ctx.own(() => { try { give.removeEventListener('click', onGive); } catch (_e) { /* gone */ } });
    }
  }

  function hide() {
    if (view) {
      try {
        if (typeof view.destroy === 'function') view.destroy();
        else if (typeof view.dispose === 'function') view.dispose();
        else if (typeof view.unmount === 'function') view.unmount();
      } catch (_e) { /* gone */ }
      view = null;
    }
    if (!node) return;
    cls(node, 'is-in', false);
    node.hidden = true;
    if (node._slot) { try { node._slot.replaceChildren(); } catch (_e) { node._slot.textContent = ''; } }
  }

  return {
    show,
    hide,
    /** True when the sibling module was missing and we drew our own card. */
    usedFallback() { return usedFallback; },
    dispose() {
      hide();
      if (node) { try { node.remove(); } catch (_e) { /* gone */ } node = null; }
    },
  };
}
