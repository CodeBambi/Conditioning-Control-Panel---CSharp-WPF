/* ============================================================================
 * backroom/room/hud.js - the room's own chrome over the 3D view: the loading
 * veil, the walk hint, the Room view button and list, the
 * Motion button and the room's Options (CONTRACT 10.14: effects intensity, tunnel
 * vision, melt). Back and the SP chip stay in index.html (Law VI: they exist
 * before any of this loads).
 *
 * THE FLOOR BELL (CONTRACT 10.16.B). One line under the nav pills, role=status,
 * rotating through the entries every 8,000 ms, newest first, wrapping. No sound
 * at any intensity (Brake 1: it is somebody else's party). Hidden while a
 * station holds the screen (`br-visiting`) and in the room view (`br-overview`),
 * Reduced motion and Calm keep the rotation (it
 * is text, not motion) and cross-fade in 0 ms instead of 200 ms. Its opt-in is
 * one more switch row inside the 10.14 Options panel, after Melt: it is the
 * user's own setting, so that press goes to `onBellOpt`, never to the host as
 * `room-option`.
 *
 * LEXICON KEYS this file shows, for the integration pass into en.json (Law VII;
 * every one has an English fallback here):
 *   br_loading, br_walk_hint, br_visit, br_back, br_room_view, br_room_walk,
 *   br_motion_still, br_motion_on
 *   br_opt_title ("Options"), br_opt_effects, br_opt_calm, br_opt_normal,
 *   br_opt_full, br_opt_calm_forced, br_opt_tunnel, br_opt_melt,
 *   br_opt_on, br_opt_off
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
 * @param {Object} o  { root, lex(key, fallback), label(row), onVisit(row), onGo(row), onOverview(on), onMotion(),
 *                      onOption(key, value), onBellOpt(on), now() }
 */
export function createHud(o) {
  const L = o.lex;
  const now = typeof o.now === 'function' ? o.now : () => Date.now();
  const veil = el('div', 'br-loading');
  const veilText = el('p', 'br-loading-text', L('br_loading', 'A little further in.'));
  const bar = el('div', 'br-loading-bar'); const fill = el('i');
  bar.appendChild(fill);
  veil.append(el('span', 'br-spark', '✦'), veilText, bar);

  const hint = el('div', 'br-hint', L('br_walk_hint', 'W/S or up/down to walk, A/D or left/right to move sideways, drag to look, E to visit'));
  const cross = el('div', 'br-crosshair'); cross.setAttribute('aria-hidden', 'true');

  const nav = el('nav', 'br-nav');
  const viewBtn = el('button', 'br-pill'); viewBtn.type = 'button';
  const motionBtn = el('button', 'br-pill'); motionBtn.type = 'button';
  const optBtn = el('button', 'br-pill', L('br_opt_title', 'Options')); optBtn.type = 'button';
  optBtn.setAttribute('aria-expanded', 'false');
  nav.append(viewBtn, motionBtn, optBtn);
  const bell = el('div', 'br-bell');
  bell.setAttribute('role', 'status'); bell.setAttribute('aria-live', 'polite'); bell.hidden = true;
  const bellText = el('span');
  bell.appendChild(bellText);
  const list = el('div', 'br-map-list'); list.hidden = true;

  // THE ROOM'S OPTIONS (10.14). Every press goes to the host as `room-option`; the host's settings frame paints it back.
  // The floor bell opt-in (10.16.B) is the one row that does not: it is the user's own, and goes to `onBellOpt`.
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
  const paintSwitch = (b, on) => {
    b.setAttribute('aria-pressed', String(!!on));
    b.textContent = on ? L('br_opt_on', 'On') : L('br_opt_off', 'Off');
  };
  const switchRow = (key, name, press) => {
    const row = el('div', 'br-opt-row'); const b = el('button', 'br-switch'); b.type = 'button';
    b.dataset.option = key;
    const fire = typeof press === 'function' ? press : ((on) => o.onOption(key, on));
    b.addEventListener('click', () => fire(b.getAttribute('aria-pressed') !== 'true'));
    row.append(el('span', 'br-opt-name', name), b);
    return { row, b };
  };
  const tunnel = switchRow('tunnel', L('br_opt_tunnel', 'Tunnel vision'));
  const melt = switchRow('melt', L('br_opt_melt', 'Melt'));
  const bellOpt = switchRow('bellOptIn', L('br_bell_optin', 'Show my name on the floor bell'),
    (on) => { if (typeof o.onBellOpt === 'function') o.onBellOpt(on); });
  paintSwitch(bellOpt.b, false);
  panel.append(el('span', 'br-opt-name', L('br_opt_effects', 'Effects')), segRow, forcedNote, tunnel.row, melt.row, bellOpt.row);
  if (typeof window.__brOptions?.openMedia === 'function') {
    const sources = el('button', 'br-pill', 'Pictures and GIFs'); sources.type='button';
    sources.addEventListener('click', () => { setOptions(false); window.__brOptions.openMedia(); });
    panel.append(sources);
  }
  nav.append(panel);   // anchored under the Options pill, whatever the nav's own offset
  o.root.append(veil, hint, cross, nav, bell, list);
  function setOptions(open) { panel.hidden = !open; optBtn.setAttribute('aria-expanded', String(!!open)); }
  optBtn.addEventListener('click', () => setOptions(panel.hidden));
  // A press anywhere outside the card (and outside its pill, which toggles it) closes it.
  document.addEventListener('pointerdown', (e) => {
    if (!panel.hidden && !panel.contains(e.target) && !optBtn.contains(e.target)) setOptions(false);
  }, true);

  let overview = false;
  viewBtn.addEventListener('click', () => o.onOverview(!overview));
  motionBtn.addEventListener('click', () => o.onMotion());
  // Focus: main.js drops it from every HUD button on pointerup and eats Space/Enter on them while walking.

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
      hint.title = row ? L('br_visit', 'Visit {0}').replace('{0}', o.label(row)) : '';
    },
    overview(on) { overview = !!on; paintView(); },
    motion(still, forced) {
      motionBtn.textContent = still ? L('br_motion_still', 'Motion still') : L('br_motion_on', 'Motion on');
      motionBtn.setAttribute('aria-pressed', String(still));
      motionBtn.disabled = !!forced;
      document.documentElement.classList.toggle('br-still', !!still);   // the bell cross-fades in 0 ms while still
    },
    /** { intensityChoice: 'calm'|'normal'|'full', forcedCalm, tunnel, melt } from init / settings. */
    options(v) {
      for (const b of segs) b.setAttribute('aria-pressed', String(b.dataset.value === v.intensityChoice));
      forcedNote.hidden = !v.forcedCalm;
      paintSwitch(tunnel.b, v.tunnel);
      paintSwitch(melt.b, v.melt);
    },
    /** The floor bell opt-in row (10.16.B): the server's answer, an optimistic tick put back when it refuses. */
    bellOptIn(checked) { paintSwitch(bellOpt.b, checked); },
    get optionsOpen() { return !panel.hidden; },
    closeOptions() { setOptions(false); },
    seated(on) {
      document.documentElement.classList.toggle('br-seated', !!on);
      viewBtn.disabled = !!on;
    },
    hideWhileVisiting(on) {
      if (on) setOptions(false);
      document.documentElement.classList.toggle('br-visiting', !!on);
    },
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
  };
}
