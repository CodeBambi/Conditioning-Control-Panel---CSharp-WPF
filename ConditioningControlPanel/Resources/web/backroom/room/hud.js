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
  // THREE LEVELS (10.14). The room used to have one Music slider and nothing else, which meant the
  // only way to turn the whisper down was the app's own SubAudioVolume - and that moved the whisper
  // while leaving every lever, reel and win exactly where it was. The room owns its own mix now.
  // o.levels is main.js's adapter over the kit's buses and the music element; the host persists it.
  const levelRows = [];
  let stopQuality = () => {};
  if (!window.__brOptions && o.levels) {
    for (const [key, fallback] of [['sub', 'Subliminal'], ['sfx', 'Game sounds'], ['music', 'Music and room']]) {
      const row = el('div', 'br-opt-row');
      const name = L('br_opt_vol_' + key, fallback);
      const slider = el('input'); slider.type = 'range'; slider.min = '0'; slider.max = '1'; slider.step = '.01';
      slider.value = String(o.levels[key]); slider.style.width = '100px'; slider.setAttribute('aria-label', name);
      slider.dataset.level = key;
      const amount = el('span', 'br-opt-name', Math.round(o.levels[key] * 100) + '%'); amount.style.minWidth = '30px';
      // input paints and sounds; change is where it goes to the host, so dragging does not post 80 times.
      slider.addEventListener('input', () => {
        const v = Number(slider.value);
        amount.textContent = Math.round(v * 100) + '%';
        o.levels.preview(key, v);
      });
      slider.addEventListener('change', () => o.levels.commit(key, Number(slider.value)));
      row.append(el('span', 'br-opt-name', name), slider, amount);
      levelRows.push({ key, slider, amount });
      panel.append(row);
    }
  }
  if (!window.__brOptions && o.quality) {
    const qualityRow = el('div', 'br-opt-row'), choice = el('select');
    const qualityLabel = L('br_opt_quality', 'Quality'); choice.setAttribute('aria-label', qualityLabel);
    choice.style.cssText = 'max-width:140px;min-height:30px;color:#f6ecff;background:#2a1838;border:1px solid #6b4a78;border-radius:6px';
    for (const [value, key, name] of [['auto', 'br_opt_quality_auto', 'Auto'], ['full', 'br_opt_quality_full', 'Full'], ['performance', 'br_opt_quality_performance', 'Performance']]) {
      const option = el('option', '', L(key, name)); option.value = value; choice.append(option);
    }
    choice.value = o.quality.mode;
    choice.addEventListener('change', () => o.quality.setMode(choice.value));
    stopQuality = o.quality.subscribe(() => { choice.value = o.quality.mode; });
    qualityRow.append(el('span', 'br-opt-name', qualityLabel), choice);
    panel.append(qualityRow);
  }

  /* ------------------------------------------------- the picture picker (10.13.C)
   * Its own card rather than more rows on the Options one: the niche editor is a text field and a
   * growing pill list, and that does not belong in a card people open to nudge a slider. The pill
   * that opens it used to be dead on desktop - it was gated ON window.__brOptions, which only the
   * web playtest's shell ever provides, so the desktop never saw it.
   *
   * The gate is INVERTED now rather than deleted. The web shell's own options sheet already carries
   * a media block - it is where this picker's layout came from - and it is the half of that sheet
   * that actually reaches the web host. An ungated pill would put a SECOND picker on that page whose
   * presses go nowhere, because the shell answers no mediaSource / mediaSub* option. Same guard the
   * levels and quality rows above use, for the same reason. */
  const onWebShell = !!window.__brOptions;
  const mediaBtn = el('button', 'br-pill', L('br_media_title', 'Pictures and GIFs')); mediaBtn.type = 'button';
  mediaBtn.setAttribute('aria-expanded', 'false');
  if (!onWebShell) panel.append(mediaBtn);

  const media = el('div', 'br-options br-media'); media.hidden = true; media.setAttribute('role', 'group');
  media.setAttribute('aria-label', L('br_media_title', 'Pictures and GIFs'));
  const MEDIA_SOURCES = [['auto', 'Auto'], ['local', 'My files'], ['online', 'Scrolller'],
                         ['mixed', 'Both'], ['bundled', 'Built-in']];
  const mediaSegs = MEDIA_SOURCES.map(([v, name]) => {
    const b = el('button', 'br-seg', L('br_media_' + v, name)); b.type = 'button';
    b.dataset.value = v;
    b.addEventListener('click', () => o.onOption('mediaSource', v));
    return b;
  });
  const mediaSegRow = el('div', 'br-seg-row'); mediaSegRow.append(...mediaSegs);
  const mediaNote = el('p', 'br-opt-note'); mediaNote.setAttribute('role', 'status');
  const nicheWrap = el('div', 'br-niches');
  const nicheForm = el('form', 'br-niche-entry');
  const nicheField = el('input'); nicheField.type = 'text'; nicheField.className = 'br-niche-field';
  nicheField.placeholder = L('br_media_placeholder', 'Community name');
  nicheField.setAttribute('aria-label', L('br_media_add', 'Add a niche'));
  nicheField.autocomplete = 'off'; nicheField.spellcheck = false;
  const nicheAdd = el('button', 'br-seg', L('br_media_add_btn', 'Add')); nicheAdd.type = 'submit';
  nicheForm.append(el('span', 'br-niche-prefix', 'r/'), nicheField, nicheAdd);
  const nicheList = el('div', 'br-niche-list');
  nicheList.setAttribute('aria-label', L('br_media_niches', 'Your niches'));
  const mediaTiming = el('p', 'br-opt-note', L('br_media_timing',
    'Walls and new flashes change now. Game artwork changes on your next visit, so your current hand and prepaid spins are kept.'));
  nicheWrap.append(nicheForm, nicheList);
  media.append(el('span', 'br-opt-name', L('br_media_source', 'Source')), mediaSegRow, mediaNote, nicheWrap, mediaTiming);
  if (!onWebShell) nav.append(media);

  // The room never trusts this field: the host validates the name again before it stores it. This is
  // only here so a typo is answered in the room instead of silently dropped over the bridge.
  const NICHE_OK = /^[A-Za-z0-9_]{2,40}$/;
  const cleanNiche = (raw) => String(raw || '')
    .trim()
    .replace(/^https?:\/\/(?:www\.)?(?:old\.)?(?:reddit\.com|scrolller\.com)\//i, '')
    .replace(/^r\//i, '')
    .replace(/\/.*$/, '')
    .trim();
  let mediaState = { source: 'auto', effective: 'local', subs: [], off: [], cap: 8, consented: false };
  let mediaMessage = '';

  function setMedia(open) {
    media.hidden = !open;
    mediaBtn.setAttribute('aria-expanded', String(!!open));
    if (open) { setOptions(false); paintMedia(); }
  }
  mediaBtn.addEventListener('click', () => setMedia(media.hidden));
  nicheForm.addEventListener('submit', (e) => {
    e.preventDefault();
    const name = cleanNiche(nicheField.value);
    if (!NICHE_OK.test(name)) { mediaMessage = L('br_media_bad', 'Enter one community name.'); paintMedia(); return; }
    if (mediaState.subs.some((s) => s.toLowerCase() === name.toLowerCase())) {
      mediaMessage = L('br_media_dupe', 'That niche is already added.'); paintMedia(); return;
    }
    if (mediaState.subs.length >= mediaState.cap) {
      mediaMessage = L('br_media_cap', 'You can keep up to {n} niches. Remove one to add another.')
        .replace('{n}', String(mediaState.cap));
      paintMedia(); return;
    }
    mediaMessage = '';
    nicheField.value = '';
    o.onOption('mediaSubAdd', name);
  });
  // A text field inside the room must not walk it. The room's keys are read on the document.
  for (const type of ['keydown', 'keyup', 'keypress']) {
    nicheField.addEventListener(type, (e) => e.stopPropagation());
  }

  function paintMedia() {
    for (const b of mediaSegs) {
      b.setAttribute('aria-pressed', String(b.dataset.value === mediaState.source));
      // Scrolller and Both need consent, which lives in Assets. Show them refused, not missing.
      const needsOnline = b.dataset.value === 'online' || b.dataset.value === 'mixed';
      b.disabled = needsOnline && !mediaState.consented;
    }
    const online = mediaState.effective === 'online' || mediaState.effective === 'mixed';
    nicheWrap.hidden = !online;
    mediaTiming.hidden = false;
    if (mediaMessage) mediaNote.textContent = mediaMessage;
    else if (!mediaState.consented && (mediaState.source === 'online' || mediaState.source === 'mixed'))
      mediaNote.textContent = L('br_media_no_consent', 'Turn on online media in Assets to use Scrolller here.');
    else mediaNote.textContent = L('br_media_note_' + mediaState.effective, {
      local: 'Your assets folder, minus anything you deselected in Assets.',
      online: 'Tap a niche to turn it on or off.',
      mixed: 'Your files and Scrolller together, blended by the mix you set in Assets.',
      bundled: "The room's built-in art. No online feed needed.",
    }[mediaState.effective] || '');
    nicheList.textContent = '';
    for (const name of mediaState.subs) {
      const off = mediaState.off.some((s) => s.toLowerCase() === name.toLowerCase());
      const pill = el('span', 'br-niche-pill');
      const toggle = el('button', 'br-seg', 'r/' + name); toggle.type = 'button';
      toggle.setAttribute('aria-pressed', String(!off));
      toggle.addEventListener('click', () => { mediaMessage = ''; o.onOption('mediaSubToggle', name); });
      const remove = el('button', 'br-niche-remove', '\u00d7'); remove.type = 'button';
      remove.setAttribute('aria-label', L('br_media_remove', 'Remove {n}').replace('{n}', 'r/' + name));
      remove.addEventListener('click', () => { mediaMessage = ''; o.onOption('mediaSubRemove', name); });
      pill.append(toggle, remove);
      nicheList.append(pill);
    }
  }
  paintMedia();

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
      // The host's frame has the last word on all three of these, exactly like the switches above.
      if (v.media) { mediaState = { ...mediaState, ...v.media }; mediaMessage = ''; paintMedia(); }
      if (v.levels) for (const row of levelRows) {
        const level = v.levels[row.key];
        if (!Number.isFinite(level) || document.activeElement === row.slider) continue;
        row.slider.value = String(level);
        row.amount.textContent = Math.round(level * 100) + '%';
      }
    },
    /** The floor bell opt-in row (10.16.B): the server's answer, an optimistic tick put back when it refuses. */
    bellOptIn(checked) { paintSwitch(bellOpt.b, checked); },
    get optionsOpen() { return !panel.hidden; },
    closeOptions() { setOptions(false); setMedia(false); },
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
    stop() { if (spin) clearInterval(spin); spin = 0; stopQuality(); },
  };
}
