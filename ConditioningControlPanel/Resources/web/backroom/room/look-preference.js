// Device-local preference shared by the desktop page and web room. Mouse-look stays the default.
const KEY = 'br.dragLook.v1';
export function readDragLook() {
  try { return localStorage.getItem(KEY) === 'true'; } catch { return false; }
}
export function saveDragLook(on) {
  try { localStorage.setItem(KEY, String(!!on)); } catch { /* The current visit still uses the choice. */ }
}
