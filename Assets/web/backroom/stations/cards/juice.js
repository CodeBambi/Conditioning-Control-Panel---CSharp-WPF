// Presentation only. No outcomes, clocks or server state are changed here.
export const LAND_MS = 240;
const clamp = (v) => Math.max(0, Math.min(1, v));
/** Small deterministic differences avoid consuming the game random source. */
export function cardTilt(owner, slot) {
  const seed = (owner === 'd' ? 7 : Number(owner) * 3) + slot * 5;
  return ((seed % 7) - 3) * .008;
}
/** A brief slide with one restrained overshoot, ending at the authored spot. */
export function landing(age, still = false) {
  const p = clamp(age / LAND_MS);
  return still || age < 0 || p === 1 ? 0 : Math.sin(p * Math.PI * 2) * (1 - p) ** 2;
}
/** Lift during a reveal, including its shadow. No lift at either endpoint. */
export const flipLift = (p, still = false) => still ? 0 : Math.sin(clamp(p) * Math.PI) * .22;
/** Stack parts briefly separate when a card leaves, then square themselves. */
export function deckRecoil(age, still = false) {
  const p = clamp(age / 280);
  return still || age < 0 || p === 1 ? 0 : Math.sin(p * Math.PI) * (1 - p);
}
/** Renderers own touchdown so sound cannot get ahead of a slow rendered frame. */
export function touchdown(card, now, still, onCue) {
  if (card.touchdown) return;
  card.touchdown = true;
  card.landAt = now;
  if (!card.quiet) onCue('card-land');
}
