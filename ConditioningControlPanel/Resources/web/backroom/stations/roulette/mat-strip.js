// Touch-sized betting surface; the authored mat remains available in the room.
export function createMatStrip({ root, spots, label, place, onToggle = () => {} }) {
  const strip = document.createElement('div'); strip.className = 'roul-mat-strip';
  const buttons = new Map(), mobile = matchMedia('(max-width: 600px), (max-height: 500px) and (min-aspect-ratio: 1/1)');
  let wasLocked = true;
  function show(on) { strip.hidden = !on; onToggle(on); }
  for (const spot of spots) {
    const button = document.createElement('button'); button.type = 'button';
    button.classList.toggle('is-number', /^s\d+$/.test(spot)); button.dataset.spot = spot; button.textContent = /^s\d+$/.test(spot) ? spot.slice(1) : label(spot);
    button.onclick = e => { place(spot, e.shiftKey); if(!mobile.matches)show(false); };
    button.oncontextmenu = e => { e.preventDefault(); place(spot, true); };
    strip.append(button); buttons.set(spot, button);
  }
  root.append(strip);
  const rectOf = spot => { const r = buttons.get(spot)?.getBoundingClientRect(); return r ? { x:r.x, y:r.y, w:r.width, h:r.height } : null; };
  return { toggle() { show(strip.hidden); }, hide() { show(false); }, layout() {}, hit() { return null; }, rectOf,
    draw(_, view) { if(view.locked)show(false); else if(wasLocked&&mobile.matches)show(true); wasLocked=view.locked; for (const [spot, button] of buttons) { button.disabled = view.locked;
      button.dataset.chips = String(view.chips[spot] || 0); button.classList.toggle('is-hit', view.hits.includes(spot)); } },
    animate() {}, clearAnims() {}, dispose() { strip.remove(); },
    debug() { return { cells:buttons.size, anims:[], view:'strip' }; } };
}
