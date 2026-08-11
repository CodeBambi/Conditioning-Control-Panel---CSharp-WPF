/* ============================================================================
 * ui/sd/reaction.js — the universal round: one big plate, one honest instant.
 *
 * States: `wait` (neutral violet-grey) -> feints (a 180 ms amber-white blink
 * with a decoy glyph) -> the real one (full pink fill + glyph + cue). Pressing
 * during `wait` or on a feint is a false start; the ROUND decides that, we just
 * report the press.
 *
 * MEASUREMENT HONESTY: the round takes its baseline in the same turn as
 * fireReactionStimulus, then re-bases onto inputs.reaction.onStimulusRendered if
 * the presenter can say when the pixels landed. We can: the raise goes out on
 * the second rAF after the style flip — i.e. after the frame carrying the pink
 * fill has been presented — so our own paint latency is not charged to the
 * player.
 *
 * NOTE vs rounds/model.js: that file suggests rendering feints
 * indistinguishably from the real stimulus. The duel-desk design deliberately
 * makes the feint a DIFFERENT colour and glyph — an unmissable feint is not a
 * test of nerve, it is a coin flip — and the round's false-start rule is what
 * keeps the feint dangerous.
 * ==========================================================================*/

import { GoonStimulusKind } from '../../core/rounds/model.js';

const DECOY_MS = 180;

export function createReaction(ctx) {
  const { el, add, cls, text, sfx } = ctx;
  let node = null;
  let armed = false;
  let decoyTimer = 0;
  const own = [];

  function build() {
    if (node) return node;
    node = el('div', 'gg-sd-react');
    if (!node) return null;
    node._glyph = add(node, el('div', 'gg-sd-react-glyph', ''));
    node._word = add(node, el('div', 'gg-sd-react-word', 'wait'));
    ctx.mountStage(node);

    const onDown = (e) => { if (!armed) return; if (e && e.preventDefault) e.preventDefault(); press(); };
    node.addEventListener('pointerdown', onDown);
    own.push(() => { try { node.removeEventListener('pointerdown', onDown); } catch (_e) { /* gone */ } });

    const win = typeof window !== 'undefined' ? window : null;
    const onKey = (e) => {
      if (!armed || !e || e.repeat) return;
      if (e.key !== ' ' && e.key !== 'Spacebar' && e.key !== 'Enter') return;
      if (e.preventDefault) e.preventDefault();
      press();
    };
    if (win && win.addEventListener) {
      win.addEventListener('keydown', onKey);
      own.push(() => { try { win.removeEventListener('keydown', onKey); } catch (_e) { /* gone */ } });
    }
    return node;
  }

  function press() {
    ctx.raise.press();
  }

  function arm(spec) {
    const n = build();
    if (!n) return;
    armed = true;
    n.hidden = false;
    cls(n, 'is-in', true);
    cls(n, 'is-real', false);
    cls(n, 'is-decoy', false);
    text(n._word, 'wait');
    text(n._glyph, '');
    if (n.setAttribute) n.setAttribute('data-gg-level', String(Math.max(1, (spec && spec.difficulty) | 0)));
  }

  function fire(kind) {
    const n = node;
    if (!n) return;
    if (kind === GoonStimulusKind.Decoy) {
      cls(n, 'is-decoy', true);
      text(n._glyph, '✳');
      text(n._word, '');
      try { clearTimeout(decoyTimer); } catch (_e) { /* gone */ }
      decoyTimer = setTimeout(() => {
        cls(n, 'is-decoy', false);
        text(n._glyph, '');
        text(n._word, 'wait');
      }, DECOY_MS);
      return;
    }

    cls(n, 'is-decoy', false);
    cls(n, 'is-real', true);
    text(n._glyph, '◆');
    text(n._word, 'now');
    sfx('gg-go');

    // Two frames: the first is the one that carries the fill, the second runs
    // after it has been presented. That is the instant the player could see it.
    if (typeof requestAnimationFrame === 'function') {
      requestAnimationFrame(() => requestAnimationFrame(() => ctx.raise.rendered()));
    } else {
      ctx.raise.rendered();
    }
  }

  function end() {
    armed = false;
    try { clearTimeout(decoyTimer); } catch (_e) { /* gone */ }
    if (!node) return;
    cls(node, 'is-in', false);
    cls(node, 'is-real', false);
    cls(node, 'is-decoy', false);
    node.hidden = true;
  }

  return {
    arm,
    fire,
    end,
    dispose() {
      end();
      while (own.length) { const fn = own.pop(); try { fn(); } catch (_e) { /* gone */ } }
      if (node) { try { node.remove(); } catch (_e) { /* gone */ } node = null; }
    },
  };
}
