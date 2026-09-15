/* Server rewards are revealed only when the pointer lands. No payout is calculated here. */
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
export function createRewardReveal(root, t) {
  const box = document.createElement('div'); box.className = 'daze-delivery'; box.hidden = true;
  box.innerHTML = '<div class="daze-cloche" aria-hidden="true"><i></i><b></b><span></span></div><p></p>';
  root.append(box);
  return { show(result, still = false) {
    const r = rewardOf(result); box.hidden = !r || r.kind === 'sp';
    if (box.hidden) return;
    box.dataset.kind = r.kind; box.classList.toggle('is-still', still);
    box.querySelector('p').textContent = rewardText(result, t);
    box.querySelector('span').textContent = r.kind === 'double' ? '\u00d72' : r.kind === 'nothing' ? '\u00b7' : r.fallback ? `\u2726 ${result.credited ?? result.total ?? result.pay ?? 75}` : '\u2667';
  }, setStill(value) { if (value) box.classList.add('is-still'); }, hide() { box.hidden = true; }, dispose() { box.remove(); } };
}
