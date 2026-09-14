/* ============================================================================
 * backroom/room/hud.js - the room's own chrome over the 3D view: the loading
 * veil, the walk hint, the Visit prompt, the Room view button and list, the
 * Motion button, the Options panel and the floor bell. Back and the SP chip
 * stay in index.html (Law VI: they exist before any of this loads).
 *
 * THE FLOOR BELL (CONTRACT 10.16.B). One line under the nav pills, role=status,
 * rotating through the entries every 8,000 ms, newest first, wrapping. No sound
 * at any intensity (Brake 1: it is somebody else's party). Hidden while a
 * station holds the screen (`br-visiting`) and in the room view (`br-overview`),
 * exactly as the Visit prompt is. Reduced motion and Calm keep the rotation (it
 * is text, not motion) and cross-fade in 0 ms instead of 200 ms.
 *
 * THE OPTIONS PANEL (CONTRACT 10.16.B, 10.14). A third pill in `br-nav` opening
 * `.br-options` beside the map list. Its first row is the floor bell opt-in;
 * the tunnel-vision toggle owed by the hypno v3 build survey adds a second.
 *
 * LEXICON KEYS this file shows, for the integration pass into en.json (Law VII;
 * every one has an English fallback here):
 *   br_loading, br_walk_hint, br_visit, br_back, br_room_view, br_room_walk,
 *   br_motion_still, br_motion_on
 *   br_options ("Options")
 *   br_bell_optin ("Show my name on the floor bell")
 *   br_bell_line ("{who} {what} {ago}"), br_bell_someone ("someone"),
 *   br_bell_slot_emi3, br_bell_slot_gif3same, br_bell_slot_sub3,
 *   br_bell_slot_spiral3, br_bell_wheel_jackpot, br_bell_wheel_slice,
 *   br_bell_cards_blackjack, br_bell_roulette_wake,
 *   br_bell_ago_now, br_bell_ago_min, br_bell_ago_hour, br_bell_ago_day
 *   br_wheel_must_hit ("MUST HIT"), br_wheel_must_hit_room ("The pot has to
 *   fall today") - 10.16.E, shown by the room on the bell line and on the
 *   wheel fixture's screen.
 * ==========================================================================*/

import { bellLines, ROTATE_MS } from './bell.js';

const el = (tag, cls, text) => { const n = document.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; };

/**
 * @param {Object} o  { root, lex(key, fallback), label(row), onVisit(row), onGo(row), onOverview(on),
 *                      onMotion(), onOptions(open), now() }
 */
export function createHud(o) {
  const L = o.lex;
  const now = typeof o.now === 'function' ? o.now : () => Date.now();
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
  const optionsBtn = el('button', 'br-pill'); optionsBtn.type = 'button';
  optionsBtn.textContent = L('br_options', 'Options');
  optionsBtn.setAttribute('aria-expanded', 'false');
  nav.append(viewBtn, motionBtn, optionsBtn);
  const bell = el('div', 'br-bell');
  bell.setAttribute('role', 'status'); bell.setAttribute('aria-live', 'polite'); bell.hidden = true;
  const bellText = el('span');
  bell.appendChild(bellText);
  const list = el('div', 'br-map-list'); list.hidden = true;
  const options = el('div', 'br-options'); options.hidden = true;
  o.root.append(veil, hint, cross, prompt, nav, bell, list, options);

  let nearest = null, overview = false, optionsOpen = false;
  prompt.addEventListener('click', () => { if (nearest) o.onVisit(nearest); });
  viewBtn.addEventListener('click', () => o.onOverview(!overview));
  motionBtn.addEventListener('click', () => o.onMotion());
  optionsBtn.addEventListener('click', () => setOptions(!optionsOpen));
  // Focus: main.js drops it from every HUD button on pointerup and eats Space/Enter on them while walking.

  function setOptions(open) {
    optionsOpen = !!open;
    options.hidden = !optionsOpen;
    optionsBtn.setAttribute('aria-expanded', String(optionsOpen));
    if (typeof o.onOptions === 'function') { try { o.onOptions(optionsOpen); } catch (e) { /* the panel still opened */ } }
  }

  /* ------------------------------------------------------------- the bell */
  // One line at a time. This timer is text only and never makes a request:
  // main.js fetches on room open and after each station close, never while seated.
  let entries = [], standing = null, lines = [], at = 0, spin = 0;
  function paintBell() {
    lines = bellLines(entries, now(), L, standing);
    if (at >= lines.length) at = 0;
    showLine();
  }
  function showLine() {
    const text = lines.length ? lines[at % lines.length] : '';
    bell.hidden = !text;
    if (bellText.textContent === text) return;
    bellText.textContent = text;
    bellText.classList.remove('is-in');
    void bellText.offsetWidth;          // restart the 200 ms cross-fade (0 ms while the room is still)
    bellText.classList.add('is-in');
  }
  function startBell() {
    if (spin) return;
    spin = setInterval(() => {
      if (lines.length > 1) { at = (at + 1) % lines.length; showLine(); } else { paintBell(); }
    }, ROTATE_MS);
  }

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
      document.documentElement.classList.toggle('br-still', !!still);   // the bell cross-fades in 0 ms while still
    },
    /* --------------------------------------------------- the Options panel */
    /**
     * Rows, in order. Each { key, label, fallback, checked, onChange(on) }. The
     * floor bell opt-in is the first; the tunnel-vision toggle joins it here.
     */
    options(rows) {
      options.textContent = '';
      for (const row of Array.isArray(rows) ? rows : []) {
        const line = el('label', 'br-option');
        const box = el('input'); box.type = 'checkbox'; box.checked = !!row.checked;
        if (row.key) box.dataset.option = String(row.key);
        box.addEventListener('change', () => { if (typeof row.onChange === 'function') row.onChange(box.checked); });
        line.append(box, el('span', null, L(row.label, row.fallback)));
        options.appendChild(line);
      }
    },
    /** The server's answer for one row (an optimistic tick is put back when it refuses). */
    option(key, checked) {
      const box = options.querySelector('input[data-option="' + String(key).replace(/["\\]/g, '') + '"]');
      if (box) box.checked = !!checked;
    },
    optionsOpen(on) { setOptions(on); },
    /* ------------------------------------------------------- the floor bell */
    /** The entries off `GET bell/state`, newest first. Starts the 8,000 ms rotation. */
    bell(rows) {
      entries = Array.isArray(rows) ? rows.slice() : [];
      at = 0;
      paintBell();
      startBell();
    },
    /** The standing first line while the wheel pot must fall today (10.16.E), or null. */
    bellStanding(text) {
      const next = typeof text === 'string' && text ? text : null;
      if (next === standing) return;
      standing = next;
      at = 0;
      paintBell();
      if (standing) startBell();
    },
    /** Test seam: what the ticker rotates through right now (never read by the room itself). */
    bellDebug() { return { lines: lines.slice(), at, rotateMs: ROTATE_MS, running: !!spin, text: bellText.textContent, hidden: bell.hidden }; },
    /** The room is leaving: the rotation stops with it. */
    stop() { if (spin) clearInterval(spin); spin = 0; },
    hideWhileVisiting(on) { document.documentElement.classList.toggle('br-visiting', !!on); if (on) prompt.hidden = true; else prompt.hidden = !nearest || overview; },
  };
}
