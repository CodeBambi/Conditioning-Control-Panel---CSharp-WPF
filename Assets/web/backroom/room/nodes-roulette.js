// The renderer measures authored anchors. Missing nodes refuse the seated view.
export const REQUIRED = Object.freeze(['roulette_rotor', 'roulette_ball', 'ball_track', 'center_disc',
  ...Array.from({length:37}, (_,n) => `pocket_${n}`),
  ...Array.from({length:37}, (_,n) => `bet_hit_s${n}`),
  ...['rose','plum','sip','sink','deep'].map(spot => `bet_hit_${spot}`)]);
export function checkNodes(names) {
  const found = new Set(names);
  return { missingRequired: REQUIRED.filter(name => !found.has(name)) };
}
