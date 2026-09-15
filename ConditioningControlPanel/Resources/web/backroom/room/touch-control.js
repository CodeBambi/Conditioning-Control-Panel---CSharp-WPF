// One captured pointer owns walking; other fingers remain free to drag the room.
export function createTouchControl({ mount, onReset }) {
  const coarse = matchMedia('(any-pointer: coarse)');
  const style = document.createElement('link'); style.rel = 'stylesheet';
  style.href = new URL('./touch-control.css', import.meta.url).href; document.head.append(style);
  const base = document.createElement('div'); base.className = 'br-thumbstick'; base.hidden = true;
  base.setAttribute('aria-hidden', 'true');
  const knob = document.createElement('i'); base.append(knob); mount.append(base);
  const value = { x: 0, z: 0 };
  let enabled = false, pointer = null;
  function paint() { knob.style.transform = `translate(${value.x * 36}px, ${value.z * 36}px)`; }
  function reset() {
    const id = pointer; pointer = null; value.x = value.z = 0; paint();
    if (id != null) { try { base.releasePointerCapture(id); } catch { /* pointer already released */ } onReset?.(); }
  }
  function available() { return coarse.matches || navigator.maxTouchPoints > 0; }
  function refresh() { base.hidden = !enabled || !available(); if (base.hidden) reset(); }
  function move(e) {
    if (e.pointerId !== pointer) return;
    e.preventDefault(); e.stopPropagation();
    const r = base.getBoundingClientRect(), x = (e.clientX - r.left - r.width / 2) / 36, z = (e.clientY - r.top - r.height / 2) / 36;
    const length = Math.hypot(x, z), amount = Math.min(1, Math.max(0, (length - .12) / .88));
    value.x = length ? x / length * amount : 0; value.z = length ? z / length * amount : 0; paint();
  }
  base.addEventListener('pointerdown', e => {
    if (!enabled || base.hidden || pointer != null || e.button !== 0) return;
    pointer = e.pointerId; base.setPointerCapture(pointer); move(e);
  });
  base.addEventListener('pointermove', move);
  for (const event of ['pointerup', 'pointercancel', 'lostpointercapture']) base.addEventListener(event, e => {
    if (e.pointerId === pointer) { e.preventDefault(); e.stopPropagation(); reset(); }
  });
  base.addEventListener('contextmenu', e => e.preventDefault());
  coarse.addEventListener('change', refresh);
  return { value, reset, setEnabled(on) { enabled = !!on; refresh(); },
    dispose() { enabled = false; reset(); coarse.removeEventListener('change', refresh); base.remove(); style.remove(); },
    debug: () => ({ visible: !base.hidden, active: pointer != null, x: value.x, z: value.z }) };
}
