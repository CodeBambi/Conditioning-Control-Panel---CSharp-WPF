import { drawFallbackFrame } from '../../arcademy/engine/loom/loomField.js';
import { LOOM_PRESETS } from '../shared/hypno/loom.js';
const node = document.getElementById('br-intro');
let finished = false;
let highWater = 0;
function visibility() { node?.classList.toggle('br-intro-still', document.hidden || node.dataset.still === 'true'); }
if (node) {
  try { const canvas = node.querySelector('canvas'); drawFallbackFrame(canvas.getContext('2d'), LOOM_PRESETS.hub, 0, 256, 256); } catch { /* The framed title remains usable without canvas. */ }
  document.addEventListener('visibilitychange', visibility);
  visibility();
}
export const intro = {
  configure(lex, still) {
    if (!node || finished) return;
    node.querySelector('h1').textContent = lex('br_room_title', 'The Back Room');
    node.querySelector('p').textContent = lex('br_loading', 'A little further in.');
    node.dataset.still = String(!!still); visibility();
  },
  progress(value) {
    if (!node || finished || !Number.isFinite(value)) return;
    highWater = Math.max(highWater, Math.min(1, value));
    node.querySelector('i').style.width = `${Math.round(highWater * 100)}%`;
  },
  finish(immediate = false) {
    if (!node || finished) return;
    this.progress(1); finished = true;
    document.removeEventListener('visibilitychange', visibility);
    node.classList.add('br-intro-out');
    if (immediate || matchMedia('(prefers-reduced-motion: reduce)').matches) node.remove();
    else setTimeout(() => node.remove(), 600);
  },
};
