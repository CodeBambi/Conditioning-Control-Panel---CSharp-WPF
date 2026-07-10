/* ============================================================================
 * hudTips.js - hover tooltips for the run HUD, without touching the input
 * surface.
 *
 * The HUD strip and pick ribbon are pointer-events:none ON PURPOSE (the field
 * below owns holds, right-click ripples and the vibe sweep), so native title
 * tooltips can never fire there and we must not flip pointer-events back on.
 * Instead: one window-level pointermove listener (passive observer) rect-tests
 * the registered elements each frame the mouse moves, and after a short hover
 * dwell shows a single shared bubble. The bubble itself is pointer-events:none,
 * so nothing about game input changes.
 *
 * Content is getter-based - `attach(el, () => ({...}) | null)` - and the getter
 * runs at show time, so live values (ripple cost, cooldowns, streak) are always
 * fresh and the HUD's update() never has to think about tooltips.
 * ==========================================================================*/

const SHOW_DELAY_MS = 300;
const EDGE_PAD = 8;

export function createHudTips(hud, { isSuppressed } = {}) {
  const bubble = document.createElement('div');
  bubble.className = 'cf-tip';
  const rows = {};
  for (const part of ['glyph', 'kicker', 'name', 'desc', 'flavor', 'extra']) {
    const d = document.createElement('div');
    d.className = `cf-tip-${part}`;
    bubble.appendChild(d);
    rows[part] = d;
  }
  hud.appendChild(bubble);

  const targets = new Map();   // Element -> content getter
  let hoverEl = null;          // element under the pointer right now
  let shownEl = null;          // element the visible bubble belongs to
  let dwellTimer = 0;
  let rafPending = false;
  let px = 0, py = 0;

  const suppressed = () => !!(isSuppressed && isSuppressed());

  function hideNow() {
    clearTimeout(dwellTimer);
    dwellTimer = 0;
    hoverEl = null;
    shownEl = null;
    bubble.classList.remove('is-on');
  }

  function fill(c) {
    rows.glyph.textContent = c.glyph || '';
    rows.kicker.textContent = c.kicker || '';
    rows.name.textContent = c.name || '';
    rows.desc.textContent = c.desc || '';
    rows.flavor.textContent = c.flavor || '';
    rows.extra.textContent = c.extra || '';
    for (const k of Object.keys(rows)) rows[k].style.display = rows[k].textContent ? '' : 'none';
    bubble.style.setProperty('--tipA', c.accent || '255,105,180');
  }

  function place(el) {
    const r = el.getBoundingClientRect();
    // measure while invisible, then anchor below (top half) or above (bottom half)
    bubble.classList.add('is-measure');
    const bw = bubble.offsetWidth, bh = bubble.offsetHeight;
    bubble.classList.remove('is-measure');
    const below = r.top + r.height / 2 < window.innerHeight / 2;
    const x = Math.min(Math.max(r.left + r.width / 2 - bw / 2, EDGE_PAD), window.innerWidth - bw - EDGE_PAD);
    const y = below ? Math.min(r.bottom + 10, window.innerHeight - bh - EDGE_PAD)
                    : Math.max(r.top - bh - 10, EDGE_PAD);
    bubble.style.left = `${Math.round(x)}px`;
    bubble.style.top = `${Math.round(y)}px`;
    bubble.classList.toggle('cf-tip--above', !below);
    bubble.classList.toggle('cf-tip--below', below);
  }

  function show(el) {
    const getter = targets.get(el);
    if (!getter || !el.isConnected || suppressed()) return;
    let content = null;
    try { content = getter(); } catch (e) { content = null; }
    if (!content) return;
    fill(content);
    place(el);
    shownEl = el;
    bubble.classList.add('is-on');
  }

  function hitTest() {
    rafPending = false;
    if (suppressed()) { if (shownEl || dwellTimer) hideNow(); return; }
    let best = null, bestArea = Infinity;
    for (const el of targets.keys()) {
      if (!el.isConnected) { targets.delete(el); continue; }
      if (el.offsetParent === null && el.getClientRects().length === 0) continue;   // display:none
      const r = el.getBoundingClientRect();
      if (px < r.left || px > r.right || py < r.top || py > r.bottom) continue;
      const area = r.width * r.height;
      if (area > 0 && area < bestArea) { best = el; bestArea = area; }
    }
    if (best === hoverEl) return;   // dwell timer (or shown bubble) keeps riding
    clearTimeout(dwellTimer);
    dwellTimer = 0;
    hoverEl = best;
    if (shownEl) { shownEl = null; bubble.classList.remove('is-on'); }
    if (best) dwellTimer = window.setTimeout(() => { dwellTimer = 0; show(best); }, SHOW_DELAY_MS);
  }

  function onMove(e) {
    if (e.pointerType && e.pointerType !== 'mouse') return;   // a stray tap must never pin a bubble
    px = e.clientX; py = e.clientY;
    if (!rafPending) { rafPending = true; requestAnimationFrame(hitTest); }
  }
  const onDown = () => hideNow();   // observer only - never preventDefault/stopPropagation
  const onBlur = () => hideNow();
  const onLeave = (e) => { if (e.target === document.documentElement) hideNow(); };

  window.addEventListener('pointermove', onMove, { passive: true });
  window.addEventListener('pointerdown', onDown, { capture: true, passive: true });
  window.addEventListener('blur', onBlur);
  document.documentElement.addEventListener('pointerleave', onLeave);

  return {
    /** Register a hover target. getter() runs at show time; return null for "no tip right now". */
    attach(el, getter) { targets.set(el, getter); },
    hideNow,
    dispose() {
      hideNow();
      targets.clear();
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerdown', onDown, { capture: true });
      window.removeEventListener('blur', onBlur);
      document.documentElement.removeEventListener('pointerleave', onLeave);
      bubble.remove();
    },
  };
}
