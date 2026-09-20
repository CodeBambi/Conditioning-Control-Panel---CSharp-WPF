import { kit } from '../shared/sound/kit.js';
import * as T from '../../vendor/three/three.module.min.js';

// No barks imply an outcome, ownership or a purchase.
export const EMI_BARKS = {
  counter: ['Just keeping the shiny things shiny.', 'I counted them. Then counted them again.', 'This shelf is my tiny kingdom.'],
  wheel: ['Round and round. Very on brand.', 'I am supervising the circle.', 'A little wave before the big wheel.'],
  cards: ['The cards are straight. My bow tie is trying.', 'Professional card straightener. Amateur magician.', 'Your seat has excellent taste.'],
  roulette: ['Keeping an eye on the little ball.', 'The velvet gets the full treatment.', 'Yes, I practise that bow.'],
};

/** A finger rolls a little between down and up: the same slop as the room's own tap (scene.js TAP_SLOP), or a tap on a mascot is neither bark nor visit. */
export const TAP_SLOP = 14;

/** Click/tap a visible NPC; dragging continues to belong to the room camera. */
export function createEmiInteraction({ canvas, camera, scene, emis, mount, isActive = () => true, canGesture = () => true, label = (key, fallback) => fallback }) {
  const doc = canvas.ownerDocument, ray = new T.Raycaster(), pointer = new T.Vector2();
  const box = new T.Box3(), point = new T.Vector3(), counts = new Map();
  const entries = emis.map(emi => ({ emi, id: emi.id || emi.debug().id, root: emi.interactionRoot || emi.pivot || emi.root }));
  const bubble = doc.createElement('div');
  bubble.className = 'br-emi-bubble'; bubble.hidden = true;
  bubble.setAttribute('role', 'status'); bubble.setAttribute('aria-live', 'polite');
  Object.assign(bubble.style, { position: 'fixed', zIndex: '25', pointerEvents: 'none', maxWidth: '244px', boxSizing: 'border-box', padding: '12px 17px', border: '1px solid #d8a6cb', borderRadius: '18px 18px 18px 4px', background: 'rgba(39,20,51,.96)', color: '#ffebf8', font: '500 15px/1.4 system-ui, sans-serif', boxShadow: '0 6px 25px #08040b80, inset 0 0 16px #cc78c51c' });
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
    const barkId = entry.id === 'slot' ? 'counter' : entry.id;
    const index = counts.get(entry.id) || 0, lines = (entry.id === 'slot' ? EMI_BARKS.counter.slice(0, 2) : EMI_BARKS[barkId]) || ['Hello there.'];
    counts.set(entry.id, index + 1); selected = entry; remaining = 4.5;
    bubble.textContent = label('br_emi_' + barkId + '_' + (index % lines.length + 1), lines[index % lines.length]);
    // Law VI: a still room holds every NPC at rest, so the bark speaks and the gesture is skipped.
    if (canGesture()) entry.emi.trigger?.(entry.id === 'counter' && index % 3 === 1 ? 'dust' : 'greet', { interrupt: true });
    kit.arm(); kit.stop('emi-bleep'); kit.play('emi-bleep', { variant: index % 3 });
    update(0);
  }
  const onDown = event => {
    if (event.button !== 0 || !event.isPrimary || (!enabled || !isActive())) return;
    const entry = hitAt(event.clientX, event.clientY);
    down = { id: event.pointerId, x: event.clientX, y: event.clientY, moved: false, time: performance.now(), entry };
    if (entry) { event.stopImmediatePropagation(); event.preventDefault(); try { canvas.setPointerCapture(event.pointerId); } catch {} }
  };
  const onMove = event => { if (down?.entry) event.stopImmediatePropagation(); if (down?.id === event.pointerId && Math.hypot(event.clientX - down.x, event.clientY - down.y) > TAP_SLOP) down.moved = true; };
  const onUp = event => {
    const start = down; down = null;
    if (start?.entry) { event.stopImmediatePropagation(); event.preventDefault(); }
    if (!start || start.id !== event.pointerId || start.moved || Math.hypot(event.clientX - start.x, event.clientY - start.y) > TAP_SLOP || (!enabled || !isActive()) || performance.now() - start.time > 650) return;
    const entry = hitAt(event.clientX, event.clientY);
    if (entry && entry === start.entry) show(entry); else dismiss();
  };
  const cancel = () => { down = null; };
  const onKey = event => { if (event.key === 'Escape') dismiss(); };
  canvas.addEventListener('pointerdown', onDown, true); canvas.addEventListener('pointermove', onMove, true);
  canvas.addEventListener('pointerup', onUp, true); canvas.addEventListener('pointercancel', cancel, true);
  doc.addEventListener('keydown', onKey); doc.defaultView.addEventListener('blur', cancel, true);
  function update(dt = 0) {
    if (disposed) return;
    if ((!enabled || !isActive())) { dismiss(); cancel(); return; }
    if (!selected) return;
    remaining -= Math.max(0, Math.min(Number.isFinite(dt) ? dt : 0, .1));
    if (remaining <= 0 || !visible(selected.root)) { dismiss(); return; }
    box.setFromObject(selected.root); box.getCenter(point); point.project(camera);
    if (point.z < -1 || point.z > 1 || Math.abs(point.x) > 1.2 || Math.abs(point.y) > 1.2) { bubble.hidden = true; return; }
    box.getCenter(point); point.y = box.max.y + .12; point.project(camera);
    const rect = canvas.getBoundingClientRect();
    bubble.hidden = false;
    const width = Math.min(244, rect.width - 24); bubble.style.maxWidth = width + 'px';
    const x = rect.left + (point.x + 1) * rect.width / 2;
    const y = rect.top + (1 - point.y) * rect.height / 2;
    let left = x - bubble.offsetWidth / 2, top = y - bubble.offsetHeight;
    if (top < rect.top + 18) {
      // A close-up can put the antenna at the top edge: speak beside the head, not over the face.
      let minX = Infinity, maxX = -Infinity;
      for (const bx of [box.min.x, box.max.x]) for (const by of [box.min.y, box.max.y]) for (const bz of [box.min.z, box.max.z]) {
        point.set(bx, by, bz).project(camera);
        const px = rect.left + (point.x + 1) * rect.width / 2;
        minX = Math.min(minX, px); maxX = Math.max(maxX, px);
      }
      left = maxX + 14 + bubble.offsetWidth < rect.right - 12 ? maxX + 14 : minX - bubble.offsetWidth - 14;
      box.getCenter(point); point.project(camera);
      top = Math.max(rect.top + 88, rect.top + (1 - point.y) * rect.height / 2 - bubble.offsetHeight / 2);
    }
    bubble.style.left = Math.max(rect.left + 12, Math.min(rect.right - bubble.offsetWidth - 12, left)) + 'px';
    bubble.style.top = Math.max(rect.top + 18, Math.min(rect.bottom - 18 - bubble.offsetHeight, top)) + 'px';
  }
  return { update, dismiss, setEnabled(on) { enabled = !!on; if (!enabled) { dismiss(); cancel(); } },
    /** The NPC id under a client point, or null: the room asks before treating a tap as a visit. */
    npcAt: (x, y) => hitAt(x, y)?.id || null, debug: () => ({ id: selected?.id || null, text: bubble.hidden ? null : bubble.textContent }), dispose() {
    if (disposed) return; disposed = true; dismiss();
    canvas.removeEventListener('pointerdown', onDown, true); canvas.removeEventListener('pointermove', onMove, true);
    canvas.removeEventListener('pointerup', onUp, true); canvas.removeEventListener('pointercancel', cancel, true);
    doc.removeEventListener('keydown', onKey); doc.defaultView.removeEventListener('blur', cancel, true); bubble.remove();
  } };
}
