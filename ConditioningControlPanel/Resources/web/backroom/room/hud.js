/* ============================================================================
 * backroom/room/hud.js - the room's own chrome over the 3D view: the loading
 * veil, the walk hint, the Visit prompt, the Room view button and list, the
 * Motion button and the room's Options (CONTRACT 10.14: effects intensity, tunnel
 * vision, melt). Back and the SP chip stay in index.html (Law VI: they exist
 * before any of this loads).
 * ==========================================================================*/

const el = (tag, cls, text) => { const n = document.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; };

/**
 * @param {Object} o  { root, lex(key, fallback), label(row), onVisit(row), onGo(row), onOverview(on), onMotion(),
 *                      onOption(key, value) }
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
  const optBtn = el('button', 'br-pill', L('br_opt_title', 'Options')); optBtn.type = 'button';
  optBtn.setAttribute('aria-expanded', 'false');
  nav.append(viewBtn, motionBtn, optBtn);
  const list = el('div', 'br-map-list'); list.hidden = true;

  // THE ROOM'S OPTIONS (10.14). Every press goes to the host as `room-option`; the host's settings frame paints it back.
  const panel = el('div', 'br-options'); panel.hidden = true; panel.setAttribute('role', 'group');
  panel.setAttribute('aria-label', L('br_opt_title', 'Options'));
  const segs = [['calm', 'Calm'], ['normal', 'Normal'], ['full', 'Full']].map(([v, name]) => {
    const b = el('button', 'br-seg', L('br_opt_' + v, name)); b.type = 'button';
    b.dataset.value = v;
    b.addEventListener('click', () => o.onOption('intensity', v));
    return b;
  });
  const segRow = el('div', 'br-seg-row'); segRow.append(...segs);
  const forcedNote = el('p', 'br-opt-note', L('br_opt_calm_forced', 'Calm while Motion is not Full'));
  const switchRow = (key, name) => {
    const row = el('div', 'br-opt-row'); const b = el('button', 'br-switch'); b.type = 'button';
    b.dataset.option = key;
    b.addEventListener('click', () => o.onOption(key, b.getAttribute('aria-pressed') !== 'true'));
    row.append(el('span', 'br-opt-name', name), b);
    return { row, b };
  };
  const tunnel = switchRow('tunnel', L('br_opt_tunnel', 'Tunnel vision'));
  const melt = switchRow('melt', L('br_opt_melt', 'Melt'));
  panel.append(el('span', 'br-opt-name', L('br_opt_effects', 'Effects')), segRow, forcedNote, tunnel.row, melt.row);
  o.root.append(veil, hint, cross, prompt, nav, list, panel);
  function setOptions(open) { panel.hidden = !open; optBtn.setAttribute('aria-expanded', String(!!open)); }
  optBtn.addEventListener('click', () => setOptions(panel.hidden));

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
    /** { intensityChoice: 'calm'|'normal'|'full', forcedCalm, tunnel, melt } from init / settings. */
    options(v) {
      for (const b of segs) b.setAttribute('aria-pressed', String(b.dataset.value === v.intensityChoice));
      forcedNote.hidden = !v.forcedCalm;
      for (const [s, on] of [[tunnel.b, v.tunnel], [melt.b, v.melt]]) {
        s.setAttribute('aria-pressed', String(!!on));
        s.textContent = on ? L('br_opt_on', 'On') : L('br_opt_off', 'Off');
      }
    },
    get optionsOpen() { return !panel.hidden; },
    closeOptions() { setOptions(false); },
    hideWhileVisiting(on) {
      if (on) setOptions(false);
      document.documentElement.classList.toggle('br-visiting', !!on);
      if (on) prompt.hidden = true; else prompt.hidden = !nearest || overview;
    },
  };
}
