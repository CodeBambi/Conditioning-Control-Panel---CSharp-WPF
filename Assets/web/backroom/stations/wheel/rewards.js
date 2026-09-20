/* rewards.js - the wheel's reward receipts, and THE PRIZE MOMENT.
 *
 * Server rewards are revealed only when the pointer lands. No payout is calculated here (Law I).
 *
 * THE PRIZE MOMENT (CONTRACT 10.22, Law IX + Law XIII). A decoration and Seeing Double are the only rewards
 * in the whole room that are a THING and not a number, and until the reward pass they had the smallest party
 * in the building: a cloche, a sentence, and a lid that slid off on a linear ramp. They now arrive on THE
 * REVEAL's own curve - 620 ms, one overshoot, cubic-bezier(.2,1.35,.35,1) - the cloche growing into place
 * while the lid lifts on the same curve (station.css, and room-reward.js for the 3D prop), with the sparkle
 * burst the plan allows firing underneath it.
 *
 * `plan` is the spine's (shared/win/plan.js) and it is the only budget this file spends. It never decides a
 * rung, never re-derives a brake and never fires a burst the plan did not pay for (10.22.D: a station may
 * always spend LESS than its plan; it may never spend more).
 *
 * Law VI / Brake 9: reduced motion and Calm keep the SETTLED cloche and the sentence. The words are the
 * value and they survive motion level 0; `is-still` kills every animation on the box, so a `still` reveal is
 * the prize sitting there, never a faster version of its arrival. */

import { sparkBurst } from '../../../arcademy/shell/counterfx.js';

export function rewardOf(result) {
  const r = result?.reward;
  if (!r || !['decoration', 'double', 'nothing', 'sp'].includes(r.kind)) return null;
  return { kind: r.kind, decorationId: typeof r.decorationId === 'string' ? r.decorationId : null,
    fallback: r.fallback === true, until: Number.isFinite(Number(r.until)) ? Number(r.until) : 0 };
}
export function rewardText(result, t) {
  const r = rewardOf(result);
  if (!r || r.kind === 'sp') return null;
  if (r.kind === 'nothing') return t('br_daze_empty', 'Not a thought. Not a sparkle.');
  if (r.kind === 'double') return t('br_daze_double_won', 'Seeing Double. Winnings doubled for 24 hours.');
  if (r.fallback) return t('br_daze_complete', 'Collection complete. +{n} SP.', { n: result.credited ?? result.total ?? result.pay ?? 75 });
  const names = { monstera: ['br_custom_monstera', 'Monstera'], ivy: ['br_custom_ivy', 'Hanging ivy'],
    terrarium: ['br_custom_terrarium', 'Terrarium'], gallery: ['br_custom_gallery', 'Gallery frame'],
    portraits: ['br_custom_portraits', 'Portrait pair'], billboard: ['br_custom_billboard', 'Wide billboard'] };
  const name = names[r.decorationId];
  return t('br_daze_delivered', '{name} delivered. Place it at Room Service.', { name: name ? t(...name) : t('br_daze_gift', 'A decoration') });
}
export function sliceText(s, t, fmt = String) {
  const names = {
    pocket_sparkles: ['br_wheel_slice_pocket_sparkles', 'Pocket Sparkles'], good_behaviour: ['br_wheel_slice_good_behaviour', 'Good Behaviour'],
    keep_the_change: ['br_wheel_slice_keep_the_change', 'Keep the Change'], spoiled_rotten: ['br_wheel_slice_spoiled_rotten', 'Spoiled Rotten'],
    room_service: ['br_wheel_slice_room_service', 'Room Service'], seeing_double: ['br_wheel_slice_seeing_double', 'Seeing Double'], head_empty: ['br_wheel_slice_head_empty', 'Head Empty'] };
  const name = names[s.id] ? t(...names[s.id]) : t(`br_wheel_slice_${s.id.replace(/_[a-z]$/, '')}`, s.label);
  const big = s.kind === 'decoration' ? '\u2667' : s.kind === 'double' ? '\u00d72' : s.kind === 'nothing' ? '\u00b7'
    : s.kind === 'jackpot' ? `\u2605 ${fmt(s.pay)}` : s.kind === 'malus' ? 'Zz' : fmt(s.pay);
  return { big, small: name };
}
export function rewardOdds(s, t, fmt) {
  if (s.kind === 'decoration') return t('br_daze_gift_odds', 'Unowned decoration, or 75 SP if complete');
  if (s.kind === 'double') return t('br_daze_double_odds', 'Double winnings for 24 hours');
  if (s.kind === 'nothing') return t('br_daze_nothing', 'Nothing');
  return s.kind === 'malus' ? t('br_wheel_odds_snooze', '+{n} next spin', { n: 2 }) : `${fmt(s.pay)} SP`;
}

/** THE REVEAL: 620 ms, one overshoot, the declared hero curve (feel.js FEEL.REVEAL_MS / REVEAL_EASE). */
const REVEAL_MS = 620, REVEAL_EASE = 'cubic-bezier(.2,1.35,.35,1)';

export function createRewardReveal(root, t) {
  const box = document.createElement('div'); box.className = 'daze-delivery'; box.hidden = true;
  box.innerHTML = '<div class="daze-cloche" aria-hidden="true"><i></i><b></b><span></span></div><p></p>';
  root.append(box);
  let anim = null;
  /** THE REVEAL on the cloche itself: it grows into place past 1 and settles back, once. Never under `still`
   *  (Law VI takes the settled box), never on a board with no WAAPI - the text has already been written. */
  function arrive() {
    if (typeof box.animate !== 'function') return;
    try { anim = box.animate([{ transform: 'translateX(-50%) scale(.72)', opacity: 0 }, { transform: 'translateX(-50%) scale(1)', opacity: 1 }],
      { duration: REVEAL_MS, easing: REVEAL_EASE, fill: 'none' }); } catch (e) { anim = null; }
  }
  return {
    /**
     * `opts` is `{ still, plan }`; a bare boolean is still read as `still` (the checks and dev.html call it
     * that way). `plan.sparkle` is the only thing that fires THE SPARKLE BURST - 0 means the rung did not
     * buy one, and a burst accompanies the arrival, it is never the arrival itself (10.22.D).
     */
    show(result, opts = false) {
      const o = opts && typeof opts === 'object' ? opts : { still: !!opts };
      const still = !!o.still, plan = o.plan || null;
      const r = rewardOf(result); box.hidden = !r || r.kind === 'sp';
      if (box.hidden) return;
      box.querySelector('.daze-cloche').hidden = false;
      box.dataset.kind = r.kind; box.classList.toggle('is-still', still);
      box.querySelector('p').textContent = rewardText(result, t);
      box.querySelector('span').textContent = r.kind === 'double' ? '\u00d72' : r.kind === 'nothing' ? '\u00b7' : r.fallback ? `\u2726 ${result.credited ?? result.total ?? result.pay ?? 75}` : '\u2667';
      if (anim) { try { anim.cancel(); } catch (e) { /* noop */ } anim = null; }
      if (still) return;
      arrive();
      if (plan && plan.sparkle > 0) sparkBurst(box, { count: plan.sparkle });
    },
    useModel(value) { box.querySelector('.daze-cloche').hidden = !!value; },
    /** A live MotionLevel drop mid-arrival (Law VI): the box takes its settled state, it does not re-run. */
    setStill(value) { if (value) { box.classList.add('is-still'); if (anim) { try { anim.finish(); } catch (e) { /* noop */ } anim = null; } } },
    hide() { box.hidden = true; },
    dispose() { if (anim) { try { anim.cancel(); } catch (e) { /* noop */ } anim = null; } box.remove(); },
  };
}
