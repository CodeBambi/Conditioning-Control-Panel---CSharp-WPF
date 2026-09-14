import * as T from '../../vendor/three/three.module.min.js';

// Review copy only. No barks imply an outcome, ownership or a purchase.
export const EMI_BARKS = {
  counter: ['Just keeping the shiny things shiny.', 'I counted them. Then counted them again.', 'This shelf is my tiny kingdom.'],
  wheel: ['Round and round. Very on brand.', 'I am supervising the circle.', 'A little wave before the big wheel.'],
  cards: ['The cards are straight. My bow tie is trying.', 'Professional card straightener. Amateur magician.', 'Your seat has excellent taste.'],
  roulette: ['Keeping an eye on the little ball.', 'The velvet gets the full treatment.', 'Yes, I practise that bow.'],
};

/** Click/tap a visible NPC; dragging continues to belong to the room camera. */
export function createEmiInteraction({ canvas, camera, scene, emis, mount, isActive = () => true, label = (key, fallback) => fallback }) {
  const doc = canvas.ownerDocument, ray = new T.Raycaster(), pointer = new T.Vector2();
  const box = new T.Box3(), point = new T.Vector3(), counts = new Map();
  const entries = emis.map(emi => ({ emi, id: emi.id || emi.debug().id, root: emi.interactionRoot || emi.pivot || emi.root }));
  const bubble = doc.createElement('div');
  bubble.className = 'br-emi-bubble'; bubble.hidden = true;
  bubble.setAttribute('role', 'status'); bubble.setAttribute('aria-live', 'polite');
  Object.assign(bubble.style, { position: 'fixed', zIndex: '7', pointerEvents: 'none', maxWidth: '244px', boxSizing: 'border-box', padding: '12px 17px', border: '1px solid #d8a6cb', borderRadius: '18px 18px 18px 4px', background: 'rgba(39,20,51,.96)', color: '#ffebf8', font: '500 15px/1.4 system-ui, sans-serif', boxShadow: '0 6px 25px #08040b80, inset 0 0 16px #cc78c51c' });
  mount.appendChild(bubble);
  let down = null, selected = null, remaining = 0, disposed = false, enabled = true;
  const dismiss = () => { selected = null; remaining = 0; bubble.hidden = true; };
  function visible(object) {
    for (let node = object; node; node = node.parent) if (!node.visible) return false;
    return true;
  }
  function hitAt(x, y) {
    const rect = canvas.getBoundingClientRect();
    if (!rect.width || !rect.height) return null;
    pointer.set((x - rect.left) / rect.width * 2 - 1, -(y - rect.top) / rect.height * 2 + 1);
    scene.updateMatrixWorld(true); camera.updateMatrixWorld(); ray.setFromCamera(pointer, camera);
    const hits = ray.intersectObjects(entries.map(entry => entry.root), true).filter(hit => visible(hit.object));
    if (!hits.length) return null;
    const hit = hits[0];
    // Walls and booth bodies occlude NPCs; decorative transparent auras do not.
    const obstruction = ray.intersectObjects(scene.children, true).find(other => {
      if (!visible(other.object) || other.object === hit.object || !other.object.isMesh) return false;
      const materials = Array.isArray(other.object.material) ? other.object.material : [other.object.material];
      return materials.some(material => material && material.visible && (!material.transparent || material.opacity >= .9));
    });
    if (obstruction && obstruction.distance < hit.distance - .025) return null;
    return entries.find(entry => { for (let node = hit.object; node; node = node.parent) if (node === entry.root) return true; return false; });
  }
  function show(entry) {
    const index = counts.get(entry.id) || 0, lines = EMI_BARKS[entry.id] || ['Hello there.'];
    counts.set(entry.id, index + 1); selected = entry; remaining = 4.5;
    bubble.textContent = label('br_emi_' + entry.id + '_' + (index % lines.length + 1), lines[index % lines.length]);
    entry.emi.trigger?.(entry.id === 'counter' && index % 3 === 1 ? 'dust' : 'greet');
    update(0);
  }
  const onDown = event => {
    if (event.button !== 0 || !event.isPrimary || (!enabled || !isActive())) return;
    down = { id: event.pointerId, x: event.clientX, y: event.clientY, moved: false, time: performance.now() };
  };
  const onMove = event => { if (down?.id === event.pointerId && Math.hypot(event.clientX - down.x, event.clientY - down.y) > 7) down.moved = true; };
  const onUp = event => {
    const start = down; down = null;
    if (!start || start.id !== event.pointerId || start.moved || Math.hypot(event.clientX - start.x, event.clientY - start.y) > 7 || (!enabled || !isActive()) || performance.now() - start.time > 650) return;
    const entry = hitAt(event.clientX, event.clientY);
    if (entry) show(entry); else dismiss();
  };
  const cancel = () => { down = null; };
  const onKey = event => { if (event.key === 'Escape') dismiss(); };
  canvas.addEventListener('pointerdown', onDown); canvas.addEventListener('pointermove', onMove);
  canvas.addEventListener('pointerup', onUp); canvas.addEventListener('pointercancel', cancel);
  doc.addEventListener('keydown', onKey); doc.defaultView.addEventListener('blur', cancel);
  function update(dt = 0) {
    if (disposed) return;
    if ((!enabled || !isActive())) { dismiss(); cancel(); return; }
    if (!selected) return;
    remaining -= Math.max(0, Math.min(Number.isFinite(dt) ? dt : 0, .1));
    if (remaining <= 0 || !visible(selected.root)) { dismiss(); return; }
    box.setFromObject(selected.root); box.getCenter(point); point.y = box.max.y + .12;
    point.project(camera);
    if (point.z < -1 || point.z > 1 || Math.abs(point.x) > 1.05 || Math.abs(point.y) > 1.05) { bubble.hidden = true; return; }
    const rect = canvas.getBoundingClientRect();
    bubble.hidden = false;
    const width = Math.min(244, rect.width - 24); bubble.style.maxWidth = width + 'px';
    const x = rect.left + (point.x + 1) * rect.width / 2;
    const y = rect.top + (1 - point.y) * rect.height / 2;
    bubble.style.left = Math.max(rect.left + 12, Math.min(rect.right - bubble.offsetWidth - 12, x - bubble.offsetWidth / 2)) + 'px';
    bubble.style.top = Math.max(rect.top + 112, Math.min(rect.bottom - 130 - bubble.offsetHeight, y - bubble.offsetHeight)) + 'px';
  }
  return { update, dismiss, setEnabled(on) { enabled = !!on; if (!enabled) { dismiss(); cancel(); } }, debug: () => ({ id: selected?.id || null, text: bubble.hidden ? null : bubble.textContent }), dispose() {
    if (disposed) return; disposed = true; dismiss();
    canvas.removeEventListener('pointerdown', onDown); canvas.removeEventListener('pointermove', onMove);
    canvas.removeEventListener('pointerup', onUp); canvas.removeEventListener('pointercancel', cancel);
    doc.removeEventListener('keydown', onKey); doc.defaultView.removeEventListener('blur', cancel); bubble.remove();
  } };
}
