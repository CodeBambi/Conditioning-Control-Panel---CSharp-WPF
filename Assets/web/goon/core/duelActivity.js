// Per-match field ownership shared by the duel controller and effect renderers.
const states = new WeakMap();
function state(match) {
  if (!match || typeof match !== 'object') return null;
  if (!states.has(match)) states.set(match, { active: false, listeners: new Set() });
  return states.get(match);
}
export function setDuelFieldActive(match, active) {
  const s = state(match);
  if (!s || s.active === !!active) return;
  s.active = !!active;
  for (const fn of Array.from(s.listeners)) fn(s.active);
}
export function observeDuelField(match, fn) {
  const s = state(match);
  fn(s?.active || false);
  if (!s) return () => {};
  s.listeners.add(fn);
  return () => s.listeners.delete(fn);
}
