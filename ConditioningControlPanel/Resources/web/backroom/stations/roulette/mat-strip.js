// Supplemental bet picker for authored mat cells outside a narrow seated viewport.
export function createMatStrip({ root, spots, label, place }) {
  const strip = document.createElement('div'); strip.className = 'roul-mat-strip';
  const buttons = new Map();
  for (const spot of spots) {
    const button = document.createElement('button'); button.type = 'button';
    button.dataset.spot = spot; button.textContent = /^s\d+$/.test(spot) ? spot.slice(1) : label(spot);
    button.onclick = e => { place(spot, e.shiftKey); strip.hidden = true; };
    button.oncontextmenu = e => { e.preventDefault(); place(spot, true); };
    strip.append(button); buttons.set(spot, button);
  }
  root.append(strip);
  const rectOf = spot => { const r = buttons.get(spot)?.getBoundingClientRect(); return r ? { x:r.x, y:r.y, w:r.width, h:r.height } : null; };
  return { toggle() { strip.hidden = !strip.hidden; }, hide() { strip.hidden = true; }, layout() {}, hit() { return null; }, rectOf,
    draw(_, view) { for (const [spot, button] of buttons) { button.disabled = view.locked;
      button.dataset.chips = String(view.chips[spot] || 0); button.classList.toggle('is-hit', view.hits.includes(spot)); } },
    animate() {}, clearAnims() {}, dispose() { strip.remove(); },
    debug() { return { cells:buttons.size, anims:[], view:'strip' }; } };
}
