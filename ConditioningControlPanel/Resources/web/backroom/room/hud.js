/* ============================================================================
 * backroom/room/hud.js - the room's own chrome over the 3D view: the loading
 * veil, the walk hint, the Visit prompt, the Room view button and list, and the
 * Motion button. Back and the SP chip stay in index.html (Law VI: they exist
 * before any of this loads).
 * ==========================================================================*/

const el = (tag, cls, text) => { const n = document.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; };

/**
 * @param {Object} o  { root, lex(key, fallback), label(row), onVisit(row), onGo(row), onOverview(on), onMotion() }
 */
export function createHud(o) {
  const L = o.lex;
  const veil = el('div', 'br-loading');
  const veilText = el('p', 'br-loading-text', L('br_loading', 'A little further in.'));
  const bar = el('div', 'br-loading-bar'); const fill = el('i');
  bar.appendChild(fill);
  veil.append(el('span', 'br-spark', '✦'), veilText, bar);

  const hint = el('div', 'br-hint', L('br_walk_hint', 'WASD to walk, drag to look, E to visit'));
  const cross = el('div', 'br-crosshair'); cross.setAttribute('aria-hidden', 'true');
  const prompt = el('button', 'br-visit'); prompt.type = 'button'; prompt.hidden = true;
  const nav = el('nav', 'br-nav');
  const viewBtn = el('button', 'br-pill'); viewBtn.type = 'button';
  const motionBtn = el('button', 'br-pill'); motionBtn.type = 'button';
  nav.append(viewBtn, motionBtn);
  const list = el('div', 'br-map-list'); list.hidden = true;
  o.root.append(veil, hint, cross, prompt, nav, list);

  let nearest = null, overview = false;
  prompt.addEventListener('click', () => { if (nearest) o.onVisit(nearest); });
  viewBtn.addEventListener('click', () => o.onOverview(!overview));
  motionBtn.addEventListener('click', () => o.onMotion());
  // Focus: main.js drops it from every HUD button on pointerup and eats Space/Enter on them while walking.

  function paintView() {
    viewBtn.textContent = overview ? L('br_room_walk', 'Back to walking') : L('br_room_view', 'Room view');
    viewBtn.setAttribute('aria-pressed', String(overview));
    list.hidden = !overview;
    cross.hidden = overview;
    document.documentElement.classList.toggle('br-overview', overview);
  }

  return {
    progress(f) { fill.style.width = Math.round(Math.max(0, Math.min(1, f)) * 100) + '%'; },
    ready() { veil.hidden = true; document.documentElement.classList.add('br-walking'); paintView(); },
    failed(text) { veilText.textContent = text; bar.hidden = true; },
    stations(rows) {
      list.textContent = '';
      for (const row of rows) {
        const b = el('button', 'br-pill', o.label(row)); b.type = 'button';
        b.dataset.station = row.key;
        b.addEventListener('click', () => o.onGo(row));
        list.appendChild(b);
      }
    },
    nearest(row) {
      nearest = row;
      prompt.hidden = !row || overview;
      if (row) prompt.textContent = L('br_visit', 'Visit {0}').replace('{0}', o.label(row)) + '  (E)';
    },
    overview(on) { overview = !!on; if (overview) prompt.hidden = true; paintView(); },
    motion(still, forced) {
      motionBtn.textContent = still ? L('br_motion_still', 'Motion still') : L('br_motion_on', 'Motion on');
      motionBtn.setAttribute('aria-pressed', String(still));
      motionBtn.disabled = !!forced;
    },
    hideWhileVisiting(on) { document.documentElement.classList.toggle('br-visiting', !!on); if (on) prompt.hidden = true; else prompt.hidden = !nearest || overview; },
  };
}
