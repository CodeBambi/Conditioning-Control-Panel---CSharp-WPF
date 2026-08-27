/* ============================================================================
 * shell/settings.js - THE settings page. One page, three tiers (GROUND-RULES §5).
 *
 * Precedence is absolute and this page renders it in that order:
 *   1. safety / consent ceilings - app-level, READ-ONLY here. The page shows the
 *      resolved state and points at the app; it NEVER re-exposes a consent toggle
 *      (remote-media consent, mic, webcam, MediaSource). Those live in the app's
 *      own settings and the host projects them already-resolved.
 *   2. global tier - master intensity, the 7 channel caps, the photosensitivity
 *      guard, the 5-group audio mixer + mute, tutorial policy. Global values are
 *      CEILINGS: a game may use less, never more.
 *   3. per-game tier - only knobs meaningless globally, rendered from each game's
 *      manifest, plus the two promoted mechanisms (board-size row and keybind
 *      slots, SYNTHESIS #7). No game may re-expose a global setting; a manifest
 *      that tries gets logged and skipped.
 *
 * C# OWNS SETTINGS. Every change posts `set-setting` and the UI applies ONLY the
 * value the host echoes back (`setting`). That is the whole anti-drift rule: if
 * the host clamps 0.9 to 0.6, the slider ends at 0.6 because the echo said so,
 * not because we guessed the clamp. A row stays visibly "pending" until its echo
 * lands, so a host that silently drops a write is visible instead of invisible.
 *
 * SETTING_KEYS below is the ONE list of keys this page writes, and they are the
 * PROTOCOL's names (camelCase, the init projection's own field names), not C#
 * property names - the host maps them to AppSettings. Nothing else in the page
 * invents a key.
 * ==========================================================================*/

import { t } from '../core/lexicon.js';
import { keyLabel, keyGlyph, keyGlyphWide } from './keybinds.js';
import { exitBar, sign as signExit } from './exits.js';
/* THE LANDSCAPE RAIL (owner brief 2026-08-27). The page reads the device's two
 * marks off <html> (`arc-mobile` + `data-arc-orient`, core/device.js's one
 * decision) and re-reads them when the phone turns. It never decides mobile-
 * ness itself; it only subscribes. */
import { onDeviceChange } from '../core/device.js';

/* ----------------------------------------------------------------------------
 * THE KEY MAP (cross-agent contract - keep in sync with ArcademyHostService's
 * ApplySetting switch).
 *
 * `set-setting` keys are the INIT PROJECTION's own field names, flattened - the
 * protocol is camelCase end to end (BUILD-CONTRACT §4), NOT the C# property
 * names. The host accepts either the dotted or the bare form (`caps.flashRate`
 * or `flashRate`); we always send the dotted one because it says what it is.
 *
 * Anything the host does not recognise is treated as a PER-GAME knob and lands
 * in the flat bag (AppSettings.ArcademySettingsJson) under the key verbatim -
 * which is why GLOBAL_RESERVED below is load-bearing, not decoration: a game
 * declaring `flashRate` would otherwise write the global ceiling.
 * -------------------------------------------------------------------------- */
export const SETTING_KEYS = Object.freeze({
  masterIntensity: 'masterIntensity',
  /** REUSED, never duplicated (BUILD-CONTRACT §4): the ONE photosensitivity guard. */
  effectIntensity: 'effectIntensity',
  caps: Object.freeze({
    flashRate: 'caps.flashRate',
    flashOpacity: 'caps.flashOpacity',
    subDensity: 'caps.subDensity',
    duckDepth: 'caps.duckDepth',
    bubbleRate: 'caps.bubbleRate',
    binauralDepth: 'caps.binauralDepth',
    bgIntensity: 'caps.bgIntensity',
  }),
  audio: Object.freeze({
    fx: 'audioLevels.fx',
    voice: 'audioLevels.voice',
    tutorial: 'audioLevels.tutorial',
    drops: 'audioLevels.drops',
    music: 'audioLevels.music',
  }),
  audioMute: 'audioMute',
  hideTutorial: 'hideTutorial',
  /** CAMPUS PRESENCE (PRESENCE.md §3): the ONE consent flag this page may write.
   *  `off` | `anon` | `username` | `discord`, and the host re-clamps every one of
   *  them - an unknown value lands on `off`, never on the nearest rung. */
  presenceShare: 'presenceShare',
  /** The whole keybind map, as an OBJECT (the host stores it as JSON itself). */
  keybinds: 'keybinds',
});

/* ----------------------------------------------------------------------------
 * THE MEDIA COUNTER'S KEYS - WEB ONLY (MEDIA-CONTRACT v1 §1, §3).
 *
 * The browser host shim owns media on the web the way ArcademyHostService owns
 * it in the app, and it announces itself with exactly one flag:
 * `init.settings.mediaControls === true`. These five keys are the whole
 * surface, and they are gated on that flag STRICTLY, not truthily, because the
 * C# host never sets it and a `media.*` key posted at the app would fall
 * straight through ApplySetting's switch into the per-game scalar bag and sit
 * there as junk under a name no game will ever read. Absent flag = this page
 * renders exactly what it rendered before any of this existed, read-only
 * "Asset source" row included.
 *
 * Two of the five are ACTIONS rather than values (`pickLocal`, `clearLocal`):
 * they store nothing, the host echoes `value: null`, and the result of the work
 * arrives separately as a `local-media` push.
 * -------------------------------------------------------------------------- */
export const MEDIA_KEYS = Object.freeze({
  remoteConsent: 'media.remoteConsent',
  niches: 'media.niches',
  librarySelect: 'media.librarySelect',
  pickLocal: 'media.pickLocal',
  clearLocal: 'media.clearLocal',
});

/** True for anything on the web media counter. Nothing here is a per-game knob. */
export function isMediaSettingKey(key) {
  return typeof key === 'string' && key.slice(0, 6) === 'media.';
}

/** Every global key this page can write - used to tell an echo apart from a game's. */
const GLOBAL_KEYS = new Set([
  SETTING_KEYS.masterIntensity, SETTING_KEYS.effectIntensity,
  SETTING_KEYS.audioMute, SETTING_KEYS.hideTutorial, SETTING_KEYS.keybinds,
  SETTING_KEYS.presenceShare,
  'masterVolume', 'remoteMediaRatio',
  // The web's motion control (host/init.js echoes it; shell.js turns it into
  // html.arc-reduced). A global, so it never lands in the per-game bag.
  'motionLevel',
  // WHOLE-OBJECT echoes are globals too (W0, 2026-08-24): the host answers the
  // mixer as one `{key:'audioLevels', value:{...}}` frame, and the undotted key
  // used to slip this fence and land the whole object in the per-game flat bag.
  'audioLevels', 'caps',
].concat(Object.keys(SETTING_KEYS.caps).map((k) => SETTING_KEYS.caps[k]))
  .concat(Object.keys(SETTING_KEYS.audio).map((k) => SETTING_KEYS.audio[k])));

/** True when a `setting` echo is a global, not a per-game knob. */
export function isGlobalSettingKey(key) {
  const k = String(key || '');
  // A `media.*` echo is the host's, never a game's: without this fence
  // shell.js's onSetting would park `media.niches` in the per-game flat bag.
  if (isMediaSettingKey(k)) return true;
  if (GLOBAL_KEYS.has(k)) return true;
  // the host also accepts the bare forms, so an echo may come back undotted
  const bare = k.replace(/^(caps|audioLevels)\./, '');
  return GLOBAL_KEYS.has('caps.' + bare) || GLOBAL_KEYS.has('audioLevels.' + bare) || GLOBAL_KEYS.has(bare);
}

/** Caps channel canon = Intake's names verbatim (SYNTHESIS #9: binauralDepth). */
export const CAP_CHANNELS = Object.freeze([
  { ch: 'flashRate', label: 'Flash rate' },
  { ch: 'flashOpacity', label: 'Flash opacity' },
  { ch: 'subDensity', label: 'Subliminal density' },
  { ch: 'duckDepth', label: 'Audio ducking' },
  { ch: 'bubbleRate', label: 'Bubble rate' },
  { ch: 'binauralDepth', label: 'Binaural depth' },
  { ch: 'bgIntensity', label: 'Background intensity' },
]);

export const AUDIO_GROUPS = Object.freeze([
  { g: 'fx', label: 'Effects' },
  { g: 'voice', label: 'Voice' },
  { g: 'tutorial', label: 'Tutorial' },
  { g: 'drops', label: 'Drops' },
  { g: 'music', label: 'Music' },
]);

/* ----------------------------------------------------------------------------
 * THE AUDIBLE MIXER (AV CLUB, 2026-08-24). Five sliders that moved in complete
 * silence - a player set "Drops" to 40% by reading a number and found out what
 * 40% meant three screens later, in the middle of a class. So each bus gets ONE
 * representative cue, fired when its new level lands.
 *
 * WHEN, and this is the whole trick: NOT on the drag. The model on this page
 * only ever moves on the host's echo (see the header), and `shell/audio.js`
 * subscribes to that same echo for the mixer's own gains - so a cue fired while
 * the thumb is still moving would play at the OLD level and tell the player a
 * lie about the slider they are looking at. The preview therefore hangs off
 * `applyEcho`, behind a short debounce, and the debounce does a second job:
 * boot.js subscribes the shell to `setting` BEFORE audio.js exists, so on any
 * one frame this page hears the echo FIRST. A timer puts the cue after
 * audio.js's own handler in that same dispatch, whichever order they sit in.
 * -------------------------------------------------------------------------- */
const PREVIEW_CUE = Object.freeze({
  fx: 'chime', voice: 'emi_blip', tutorial: 'blip', drops: 'pop', music: 'wash',
});
/** Loud enough to judge, quiet enough to tune a whole mixer with. */
const PREVIEW_LEVEL = 0.7;
const PREVIEW_DEBOUNCE_MS = 150;

/** CAMPUS PRESENCE's consent ladder, weakest first and `off` at the bottom. The
 *  ORDER is the ladder's, so the select reads as a ramp rather than as a menu. */
export const PRESENCE_RUNGS = Object.freeze(['off', 'anon', 'username', 'discord']);

/** English floors for the four rungs, used only when the lexicon has no row. */
const PRESENCE_FALLBACK = Object.freeze({
  off: 'Off - room head counts only',
  anon: 'Anonymous - a ghost with no name or picture',
  username: 'Username - your display name over the ghost',
  discord: 'Discord - your display name and profile picture',
});

/** A per-game manifest may not name any of these (they are global). */
const GLOBAL_RESERVED = new Set(
  ['masterIntensity', 'effectIntensity', 'audioMute', 'hideTutorial', 'mediaSource',
    'remoteMediaRatio', 'offlineMode', 'masterVolume', 'motionLevel', 'reducedMotion',
    'presenceShare']
    .concat(CAP_CHANNELS.map((c) => c.ch))
    .concat(AUDIO_GROUPS.map((a) => a.g))
);

/** Per-game board size rides the flat bag under one derived key. */
export function boardSizeKey(gameKey) { return String(gameKey) + '_board_size'; }

/* ----------------------------------------------------------------------------
 * DOM HELPERS
 * -------------------------------------------------------------------------- */
function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}
function pct(v) { return Math.round((Number(v) || 0) * 100) + '%'; }
function mult(v) { return (Math.round((Number(v) || 0) * 100) / 100).toFixed(2) + 'x'; }

/**
 * @param {Object} o
 * @param {Object} o.init       the init projection
 * @param {Object} o.bridge
 * @param {Array} o.games       registry entries [{key, mod, ok, ...}]
 * @param {Object=} o.assets     createAssets() handle. The MEDIA COUNTER borrows
 *                              exactly two of the DOOR's verbs from it,
 *                              `probeSub` and `removeLibrarySub`, so the add
 *                              and remove buttons ride the `probe-sub` /
 *                              `library-remove` frames SORT's door already
 *                              minted rather than a second copy of them.
 *                              Absent (or the null provider) and those two
 *                              buttons are simply not offered.
 * @param {Object} o.keybinds   createKeybinds() handle
 * @param {Function=} o.onClose
 * @param {Function=} o.log
 * @param {string=} o.gameKey   THE SPLIT (owner ruling 2026-08-24): set, the
 *                              page is SCOPED - tiers 1 + 2 unchanged, then
 *                              exactly ONE game group (the running class),
 *                              never the other eight. The campus / Front Office
 *                              keep calling with no key and get the full sheet.
 *                              An unknown key falls back to the full page (a
 *                              missing group would hide real knobs; too many is
 *                              the lesser bug).
 * @param {{count:number, source:'host'|'house'}=} o.vocab  THE RESOLVED word
 *                              pool from the shell (core/vocab.js), not
 *                              `init.words`: on a day the player's own list is
 *                              empty the school lends its own vocabulary, and
 *                              the row has to name the list that is actually
 *                              flashing. Absent -> the row falls back to
 *                              `init.words`, which is what any caller that
 *                              predates the floor already meant.
 */
export function createSettingsPage({ init, bridge, games, keybinds, assets, store, onClose, log, gameKey, vocab, emi, themes } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const src = init || {};
  const root = el('div', 'arc-settings');
  /** setting key -> {apply(value), node} so an echo can find its row. */
  const rows = new Map();
  const flatBag = (src.settings && typeof src.settings === 'object') ? src.settings : {};
  /** Host-frame unsubscribers (the media counter's two pushes). destroy() runs them. */
  const offs = [];

  /* MEDIA-CONTRACT §1. STRICTLY true. `undefined` is the app, and the app must
   * see this page exactly as it saw it yesterday. */
  const mediaControls = flatBag.mediaControls === true;

  /* WHICH HOST (owner bug, 2026-08-25). `init.platform.host` is the one signal:
   * the C# host says 'desktop', the browser shim says 'web'. Anything that is
   * not 'web' is the app, so a host that predates the field keeps the desktop
   * sheet exactly. On the app the ceilings are READ-ONLY (the app owns them);
   * on the web there is no app to point at, so the rows that mean nothing in a
   * browser (subliminal audio, the word pool, the panic key) are not drawn, and
   * the two the shim can actually honour - master volume (shell/audio.js reads
   * the `masterVolume` echo) and motion (`motionLevel` echo -> html.arc-reduced)
   * - become real controls. Only what the shim echoes is exposed: a control
   * whose echo never comes back wears `pending` forever. */
  const host = (src.platform && src.platform.host === 'web') ? 'web' : 'app';
  const mobile = !!(typeof document !== 'undefined' && document.documentElement
    && document.documentElement.classList
    && document.documentElement.classList.contains('arc-mobile'));

  function send(key, value) {
    if (!bridge || typeof bridge.send !== 'function') return;
    bridge.send({ type: 'set-setting', key: String(key), value });
  }

  /** Register + mark pending; the echo clears it. */
  function write(key, value, row) {
    if (row) row.classList.add('pending');
    send(key, value);
  }

  /* THE PREVIEW. One pending cue at a time: sweeping a slider lands several
   * echoes and the player wants to hear the LAST one, not a burst. The request
   * is `arcademy-sfx` on `document` like every other cue in the school -
   * settings owns no audio node either (trap 18), and the copy of the defensive
   * dispatch is ceremonies.js's, deliberately, so there is one shape to fix. */
  let previewTimer = 0;
  let previewBus = null;
  function previewBusLevel(bus) {
    if (!PREVIEW_CUE[bus]) return;
    previewBus = bus;
    if (previewTimer) { try { clearTimeout(previewTimer); } catch (e) { /* noop */ } }
    previewTimer = setTimeout(() => {
      previewTimer = 0;
      const b = previewBus;
      previewBus = null;
      try {
        if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
        const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
        if (!Ctor) return;
        document.dispatchEvent(new Ctor('arcademy-sfx', {
          detail: { name: PREVIEW_CUE[b], level: PREVIEW_LEVEL, bus: b },
        }));
      } catch (e) { /* a cue must never be the thing that throws */ }
    }, PREVIEW_DEBOUNCE_MS);
  }

  /* THE SHEET'S OWN VOICE (W3 P0-34 / P1-23). Same defensive dispatch as the
   * preview above, minus the debounce: these are one-per-press answers, not a
   * slider being swept. TUTORIAL bus, because a settings page is the school
   * explaining itself, and quiet by construction - chrome is never the loudest
   * thing on a screen. A dropped cue is not an error. */
  function sfx(name, level, extra) {
    try {
      if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
      const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
      if (!Ctor) return;
      document.dispatchEvent(new Ctor('arcademy-sfx', {
        detail: Object.assign(
          { name: String(name || 'blip'), level: Number(level) || 0.5, bus: 'tutorial' },
          extra || {}
        ),
      }));
    } catch (e) { /* a cue must never be the thing that throws */ }
  }

  /* ---------------------- row builders --------------------------------- */
  function sliderRow({ key, label, hint, value, min, max, step, fmt }) {
    const row = el('div', 'arc-row');
    const lab = el('label', null, label);
    const input = el('input');
    input.type = 'range';
    input.min = String(min); input.max = String(max); input.step = String(step);
    input.value = String(value);
    const out = el('span', 'arc-val', (fmt || pct)(value));
    lab.htmlFor = input.id = 'arc-' + String(key).replace(/[^\w]+/g, '-');
    if (hint) lab.appendChild(el('span', 'arc-hint', hint));

    // Paint the drag live (an input that fights your thumb feels broken) but the
    // MODEL only moves on the echo - see applyEcho.
    input.addEventListener('input', () => { out.textContent = (fmt || pct)(input.value); });
    input.addEventListener('change', () => write(key, Number(input.value), row));

    row.appendChild(lab); row.appendChild(input); row.appendChild(out);
    rows.set(key, {
      node: row,
      value,
      apply(v) {
        this.value = v;
        input.value = String(v);
        out.textContent = (fmt || pct)(v);
        row.classList.remove('pending');
        refreshSummaries();
      },
    });
    return row;
  }

  function switchRow({ key, label, hint, value }) {
    const row = el('div', 'arc-row');
    const lab = el('label', null, label);
    if (hint) lab.appendChild(el('span', 'arc-hint', hint));
    const wrap = el('span', 'arc-switch');
    const input = el('input');
    input.type = 'checkbox';
    input.checked = !!value;
    lab.htmlFor = input.id = 'arc-' + String(key).replace(/[^\w]+/g, '-');
    wrap.appendChild(input); wrap.appendChild(el('span'));
    input.addEventListener('change', () => write(key, !!input.checked, row));
    row.appendChild(lab); row.appendChild(wrap);
    rows.set(key, {
      node: row,
      value: !!value,
      apply(v) { this.value = !!v; input.checked = !!v; row.classList.remove('pending'); refreshSummaries(); },
    });
    return row;
  }

  function selectRow({ key, label, hint, value, options, format }) {
    const row = el('div', 'arc-row');
    const lab = el('label', null, label);
    if (hint) lab.appendChild(el('span', 'arc-hint', hint));
    const sel = el('select');
    for (const o of options) {
      const opt = el('option', null, format ? format(o) : String(o));
      opt.value = String(o);
      if (String(o) === String(value)) opt.selected = true;
      sel.appendChild(opt);
    }
    lab.htmlFor = sel.id = 'arc-' + String(key).replace(/[^\w]+/g, '-');
    sel.addEventListener('change', () => {
      const raw = sel.value;
      const n = Number(raw);
      write(key, Number.isFinite(n) && String(n) === raw ? n : raw, row);
    });
    row.appendChild(lab); row.appendChild(sel);
    rows.set(key, {
      node: row,
      value,
      apply(v) { this.value = v; sel.value = String(v); row.classList.remove('pending'); refreshSummaries(); },
    });
    return row;
  }

  /* ---------------------- THE CAMPUS LOOK ROW ---------------------------
   * COUNTER STOCK. The one row on this sheet that is NOT a host setting: the
   * pick is a page-owned meta key (`campusTheme`), the shell owns it, and this
   * page is handed four narrow functions and nothing else (shell.js themeCaps).
   *
   * THREE THINGS THAT MAKE IT DIFFERENT FROM selectRow, and each is deliberate:
   *   - NO `pending`. There is no host echo to wait for; `select` returns what
   *     stuck and the row repaints from that, so the look changes under your
   *     thumb rather than a beat later.
   *   - NO UNOWNED ROW. `list()` contains House Standard plus what the player
   *     actually owns. A theme they have not bought is ABSENT - not dimmed, not
   *     padlocked, not named. A restock should appear, not be spoiled.
   *   - RADIO, NOT A <select>. Two or three options that each repaint the whole
   *     school want to be visible at once, and the swatch is the point.
   * -------------------------------------------------------------------- */
  function themeRow(caps) {
    const row = el('div', 'arc-row arc-themerow');
    const picks = el('div', 'arc-themepicks');
    picks.setAttribute('role', 'radiogroup');
    picks.setAttribute('aria-label', t('opt_theme_head', 'Campus look'));

    let list = [];
    try { list = caps.list() || []; } catch (e) { list = []; }
    const buttons = [];

    function paint(current) {
      for (const b of buttons) {
        b.setAttribute('aria-checked', b.__themeId === current ? 'true' : 'false');
      }
    }

    for (const entry of list) {
      if (!entry || !entry.id) continue;
      const b = el('button', 'arc-themepick');
      b.type = 'button';
      b.setAttribute('role', 'radio');
      b.__themeId = entry.id;
      /* THE SWATCH. Three dots off the theme's OWN palette, so a button shows
       * what it does before it is pressed. This is the one place in the school
       * a colour is allowed to travel as a value: it is DESCRIBING a palette,
       * not using one, and the row would otherwise be three identical words. */
      let sw = null;
      try { sw = caps.swatch ? caps.swatch(entry.id) : null; } catch (e) { sw = null; }
      if (sw) {
        const dots = el('span', 'arc-themedots');
        dots.setAttribute('aria-hidden', 'true');
        for (const hue of [sw.panel, sw.accent, sw.ink]) {
          const d = el('span', 'arc-themedot');
          if (typeof hue === 'string') d.style.setProperty('--dot', hue);
          dots.appendChild(d);
        }
        b.appendChild(dots);
      }
      b.appendChild(el('span', null, t(entry.nameKey || '', entry.nameEn || entry.id)));
      b.addEventListener('click', () => {
        let landed = entry.id;
        try { landed = caps.select(entry.id); } catch (e) { /* the pick is the shell's */ }
        paint(landed);
        refreshSummaries();
        // Same one-per-press answer every other control on this sheet gives.
        sfx('tell', 0.22, { pitch: landed === 'standard' ? 0.92 : 1.08 });
      });
      buttons.push(b);
      picks.appendChild(b);
    }

    let current = 'standard';
    try { current = caps.current() || 'standard'; } catch (e) { current = 'standard'; }
    paint(current);

    row.appendChild(picks);
    return row;
  }

  /** Is there a look worth offering? House Standard alone is not a choice, so
   *  the whole group stays away until the player owns at least one theme. */
  function hasThemeChoice() {
    if (!themes || typeof themes.list !== 'function') return false;
    try { return (themes.list() || []).length > 1; } catch (e) { return false; }
  }

  /** The name of the pick, for the fold's one-line summary. */
  function themeSummary() {
    try {
      const current = themes.current();
      for (const entry of themes.list() || []) {
        if (entry && entry.id === current) return t(entry.nameKey || '', entry.nameEn || entry.id);
      }
    } catch (e) { /* a fold summary is never worth a throw */ }
    return '';
  }

  /** THE GROUP. Its own fold, headed with the one key the contract mints
   *  (`opt_theme_head`), so the row inside is nothing but the choices - a group
   *  and a row both reading "Campus look" would be the same word twice. */
  function buildLook() {
    const g = group(t('opt_theme_head', 'Campus look'), 'look', themeSummary);
    g.body.appendChild(themeRow(themes));
    return g;
  }

  function readonlyRow(label, value, hint) {
    const row = el('div', 'arc-row readonly');
    const lab = el('span', 'arc-rowlabel', label);
    if (hint) lab.appendChild(el('span', 'arc-hint', hint));
    row.appendChild(lab);
    row.appendChild(el('span', 'arc-val', String(value)));
    return row;
  }

  /** Keybind capture row - the shared framework's only UI (SYNTHESIS #7).
   *
   * DECK VI (VERB GLYPHS): the cap is DRAWN - a CSS keycap wearing the key's
   * glyph, not its name. The label string is not deleted, it MOVES: the verb's
   * own label stays in this sheet on the left (that is the settings row), and
   * the key's human name lives on the cap's `title` and `aria-label`, so a
   * screen reader and a hover both still say "Space" while the eye reads ␣. */
  function keyRow(gameKey, slot) {
    const row = el('div', 'arc-row');
    const label = slot.labelKey ? t(slot.labelKey, slot.verb) : slot.verb;
    const lab = el('span', 'arc-rowlabel', label);
    const msg = el('span', 'arc-conflict', '');
    lab.appendChild(msg);
    const cap = el('button', 'arc-keycap arc-glyphcap');
    cap.type = 'button';

    /** Paint the drawn cap for the CURRENT binding (glyph on the face, name in
     *  the tooltip + aria). One place, so capture and cancel cannot drift. */
    function paintCap() {
      const bound = keybinds.get(gameKey, slot.verb);
      const name = keyLabel(bound);
      cap.textContent = keyGlyph(bound);
      if (keyGlyphWide(bound)) cap.classList.add('wide'); else cap.classList.remove('wide');
      // The label survives HERE - the glyph is for the eye, this is for
      // everything else (SYNTHESIS #7's rebind UI must stay announceable).
      cap.setAttribute('title', label + ': ' + name);
      cap.setAttribute('aria-label', label + ': ' + name);
    }
    paintCap();

    let capturing = false;
    const stop = () => {
      capturing = false;
      cap.classList.remove('capturing');
      window.removeEventListener('keydown', grab, true);
    };
    const grab = (e) => {
      e.preventDefault();
      e.stopPropagation();
      if (e.key === 'Escape') { stop(); paintCap(); return; }
      const res = keybinds.bind(gameKey, slot.verb, e.code || e.key);
      if (res.ok) {
        msg.textContent = '';
        sfx('commit', 0.25);        // W3 P1-23: the key is yours now
        paintCap();
      } else {
        // W3 P1-23: refused, and refusals are quiet (owner's standing rule).
        sfx('bump', 0.08);
        const r = res.conflict && res.conflict.reason;
        msg.textContent = r === 'panic' ? ' - that is the panic key'
          : r === 'taken' ? ' - already bound to ' + res.conflict.with
            : ' - reserved key';
        paintCap();
      }
      stop();
    };
    cap.addEventListener('click', () => {
      if (capturing) { stop(); paintCap(); return; }
      capturing = true;
      // W3 P1-23: ARMED. The cap is listening, and a cap that is listening has
      // to say so - the glyph swaps to "press a key" and nothing else changes.
      sfx('tell', 0.2, { pitch: 1.1 });
      cap.classList.add('capturing');
      cap.classList.add('wide');
      cap.textContent = 'press a key';
      msg.textContent = '';
      window.addEventListener('keydown', grab, true);
    });

    row.appendChild(lab);
    row.appendChild(cap);
    return row;
  }

  /* ---------------------- THE DISCLOSURE ------------------------------
   * Every section on this page is a fold (owner brief 2026-08-25: the sheet
   * was one long cramped scroll on a phone). A header row carries the title,
   * a one-line summary of the section's current state, and a chevron; the body
   * folds under it. The header is a real <button> (Enter / Space toggle for
   * free, aria-expanded says which way it is), the summary repaints on every
   * echo, and the open state is banked per section in the meta store under
   * `optionsOpen.<id>` so a second trip through the front office opens the way
   * it was left. Defaults: desktop all open; a phone opens the first section
   * and folds the rest. Height animates on grid rows; .arc-reduced cuts it.
   * -------------------------------------------------------------------- */
  const sections = [];
  const OPEN_KEY = 'optionsOpen';

  /* ----------------------------------------------------------------------
   * THE RAIL (phone, landscape). A landscape phone is ~390px tall: the folded
   * list showed two rows per screen and the sheet was an endless scroll. In
   * landscape the SAME sections become a left rail of tabs and the active
   * section's rows fill the right pane; the classes share one rail entry
   * ("Classes") with a chip strip across the top of the pane, so the rail
   * stays nine names long however many classes the school runs. Portrait and
   * desktop keep the folds exactly - the rail and the strip are in the DOM
   * always and styles.css shows them only under
   * `html.arc-mobile[data-arc-orient="landscape"]`, so a rotate is a class
   * flip and a repaint, never a rebuild. Fold state is not touched by the
   * rail: it is banked in portrait and honoured again on the way back.
   * -------------------------------------------------------------------- */
  let railMode = false;
  let activeId = null;
  let activeClassId = null;
  let rail = null;
  let strip = null;
  let pane = null;
  let classesTab = null;
  const CLASS_PREFIX = 'game.';
  const isClassId = (id) => String(id || '').indexOf(CLASS_PREFIX) === 0;

  function isRailViewport() {
    try {
      const html = document.documentElement;
      return !!(html && html.classList && html.classList.contains('arc-mobile')
        && html.getAttribute('data-arc-orient') === 'landscape');
    } catch (e) { return false; }
  }

  /** One section lit, in the rail and in the pane. Never banks anything. */
  function selectSection(id) {
    const sid = String(id || '');
    if (!sections.some((s) => s.id === sid)) return;
    activeId = sid;
    if (isClassId(sid)) activeClassId = sid;
    const cls = isClassId(sid);
    for (const s of sections) {
      const on = s.id === sid;
      s.node.classList.toggle('arc-set-active', on);
      s.tab.setAttribute('aria-selected', on ? 'true' : 'false');
      s.tab.tabIndex = on ? 0 : -1;
    }
    if (classesTab) {
      classesTab.setAttribute('aria-selected', cls ? 'true' : 'false');
      classesTab.tabIndex = cls ? 0 : -1;
    }
    if (strip) strip.hidden = !cls;
    if (railMode && pane) { try { pane.scrollTop = 0; } catch (e) { /* noop */ } }
  }

  /** Arrow keys walk a tablist; the walked-to tab is selected as it is focused. */
  function arrowNav(list, prevKeys, nextKeys) {
    list.addEventListener('keydown', (e) => {
      const dir = nextKeys.indexOf(e.key) >= 0 ? 1 : prevKeys.indexOf(e.key) >= 0 ? -1 : 0;
      if (!dir) return;
      const tabs = Array.from(list.querySelectorAll('[role="tab"]')).filter((n) => !n.hidden && n.offsetParent !== null);
      if (!tabs.length) return;
      const at = Math.max(0, tabs.indexOf(document.activeElement));
      const next = tabs[(at + dir + tabs.length) % tabs.length];
      e.preventDefault();
      try { next.focus(); next.click(); } catch (err) { /* noop */ }
    });
  }

  /** Read the viewport, flip the mode, repaint the folds. Idempotent. */
  function layout() {
    const want = isRailViewport();
    if (want) {
      /* The rail box is the viewport minus the topbar, which is sticky and
       * its own height (it wraps on a narrow phone). Measured, never guessed;
       * styles.css falls back to 52px if this never ran. */
      let top = 0;
      try {
        const tb = document.getElementById('arc-topbar');
        if (tb && !tb.hidden) top = Math.round(tb.getBoundingClientRect().height);
      } catch (e) { /* noop */ }
      root.style.setProperty('--arc-set-top', top + 'px');
    }
    if (want === railMode) return;
    railMode = want;
    root.classList.toggle('arc-set-rail', want);
    for (const s of sections) s.paint();
    if (want && !activeId) selectSection(defaultSection());
  }

  /** The scoped page opens on its class; the front office on its first sheet. */
  function defaultSection() {
    const scoped = sections.find((s) => isClassId(s.id) && s.scoped);
    return scoped ? scoped.id : (sections[0] ? sections[0].id : null);
  }

  function storedOpen(id) {
    try {
      const all = store && typeof store.get === 'function' ? store.get(OPEN_KEY, null) : null;
      if (all && typeof all === 'object' && typeof all[id] === 'boolean') return all[id];
    } catch (e) { /* an unreadable store is an unset one */ }
    return null;
  }
  function bankOpen(id, open) {
    try { if (store && typeof store.merge === 'function') store.merge(OPEN_KEY, { [id]: !!open }); }
    catch (e) { say('settings: could not bank fold state for ' + id); }
  }

  function group(title, id, summarize) {
    const sec = el('section', 'arc-group arc-disc');
    const sid = String(id || title).replace(/[^\w.-]+/g, '-');
    const head = el('button', 'arc-disc-head');
    head.type = 'button';
    head.appendChild(el('h3', null, title));
    const sum = el('span', 'arc-disc-sum', '');
    head.appendChild(sum);
    const chev = el('span', 'arc-disc-chev', '\u203A');
    chev.setAttribute('aria-hidden', 'true');
    head.appendChild(chev);
    const body = el('div', 'arc-disc-body');
    const inner = el('div', 'arc-disc-inner');
    const bodyId = 'arc-disc-' + sid;
    body.id = bodyId;
    head.setAttribute('aria-controls', bodyId);
    body.appendChild(inner);
    sec.appendChild(head);
    sec.appendChild(body);

    const fallback = mobile ? sections.length === 0 : true;
    const stored = storedOpen(sid);
    let open = stored == null ? fallback : stored;

    /* THE RAIL TAB. Built with the section, placed by build(): tier sections
     * go in the rail, class sections in the strip. Hidden outside landscape. */
    const tab = el('button', 'arc-set-tab');
    tab.type = 'button';
    tab.setAttribute('role', 'tab');
    tab.setAttribute('aria-selected', 'false');
    tab.setAttribute('aria-controls', bodyId);
    tab.tabIndex = -1;
    tab.appendChild(el('span', 'arc-set-tab-t', title));
    tab.addEventListener('click', () => selectSection(sid));

    function paint() {
      if (railMode) {
        /* The rail owns visibility: every body is unfolded (the pane shows one
         * section, styles.css hides the rest) and the head is a title line. */
        sec.classList.add('open');
        head.setAttribute('aria-expanded', 'true');
        body.hidden = false;
        return;
      }
      sec.classList.toggle('open', open);
      head.setAttribute('aria-expanded', open ? 'true' : 'false');
      // hidden is what keeps a folded section out of the tab order; the height
      // transition rides the class, so the flag lands after the fold on close.
      if (open) { body.hidden = false; }
      else if (reducedMotion()) { body.hidden = true; }
      else {
        // ...unless the phone turned meanwhile and the rail now owns the body.
        setTimeout(() => { if (!open && !railMode) body.hidden = true; }, 240);
      }
    }
    function refresh() {
      let text = '';
      try { text = summarize ? String(summarize() || '') : ''; }
      catch (e) { text = ''; }
      sum.textContent = text;
    }
    head.addEventListener('click', () => {
      if (railMode) return;           // a title line, not a fold, in the rail
      open = !open;
      paint();
      bankOpen(sid, open);
    });
    paint();
    sections.push({ id: sid, title, node: sec, tab, refresh, paint, isOpen: () => open, scoped: false });
    sec.body = inner;
    sec.refresh = refresh;
    return sec;
  }

  function reducedMotion() {
    try { return !!(document.documentElement && document.documentElement.classList.contains('arc-reduced')); }
    catch (e) { return false; }
  }

  function refreshSummaries() {
    for (const s of sections) s.refresh();
  }

  /** A row's live value (the echo's, once one has landed). */
  function val(key, fallback) {
    const r = rows.get(key);
    return r && r.value !== undefined ? r.value : fallback;
  }
  const SEP = ' \u00B7 ';
  function fill(text, vars) {
    let out = String(text);
    for (const k of Object.keys(vars || {})) out = out.split('{' + k + '}').join(String(vars[k]));
    return out;
  }

  /* ---------------------- 1. ceilings -------------------------------------
   * TWO SHEETS, ONE FUNCTION. The app's: read-only, the app owns them, the note
   * says so. The web's: 'This device' - master volume and motion as live
   * controls (both echoed by the shim and honoured on this page), nothing that
   * a browser cannot mean. */
  function motionName(v) {
    const n = Number(v);
    return t('settings_motion_' + (n === 0 ? 'off' : n === 1 ? 'reduced' : 'full'),
      n === 0 ? 'Off' : n === 1 ? 'Reduced' : 'Full');
  }

  function buildCeilings() {
    if (host === 'web') return buildDevice();
    const g = group(t('settings_ceilings_head', 'App ceilings'), 'ceilings', () => [
      fill(t('settings_sum_volume', 'Volume {v}'), { v: pct(src.masterVolume == null ? 1 : src.masterVolume) }),
      fill(t('settings_sum_motion', 'Motion {v}'), {
        v: String(src.motionLevel == null ? 2 : src.motionLevel) + (src.reducedMotion ? ' (reduced)' : ''),
      }),
    ].join(SEP));
    g.body.appendChild(el('p', 'arc-note', t('settings_ceilings_note_app',
      'Set in the app and shown here so you know what the school has to work with.')));

    /* THE SWAP (MEDIA-CONTRACT §9). On the app this row is the only thing the
     * page says about media, and it is read-only because the app owns the
     * setting. On the web the shim owns it and hands the player the real
     * controls, so a row pointing at a settings screen that does not exist
     * there steps aside for the Media group rather than doubling it. */
    if (!mediaControls) {
      const source = src.offlineMode ? 'local only (offline mode)'
        : (src.remoteMediaEnabled ? 'local + online' : 'local only');
      g.body.appendChild(readonlyRow('Asset source', source,
        'Online media follows the app\u2019s media source and consent.'));
    }
    if (src.remoteMediaEnabled) {
      g.body.appendChild(readonlyRow('Online media share', pct(src.remoteMediaRatio)));
    }
    g.body.appendChild(readonlyRow(t('settings_master_volume', 'Master volume'),
      pct(src.masterVolume == null ? 1 : src.masterVolume)));
    g.body.appendChild(readonlyRow('Subliminal audio', src.audioAudible ? 'audible' : 'silent'));
    g.body.appendChild(readonlyRow(t('settings_motion', 'Motion'), String(src.motionLevel == null ? 2 : src.motionLevel)
      + (src.reducedMotion ? ' (reduced)' : '')));
    if (src.performanceMode) g.body.appendChild(readonlyRow('Performance mode', 'on'));
    /* THE ROW MUST NAME THE LIST THAT IS FLASHING. `init.words` is the player's
     * own pool; when it is empty the shell deals the school's vocabulary in its
     * place, and a row still reading "0 words" would describe a silence the
     * player is not getting. */
    const house = !!(vocab && vocab.source === 'house');
    const words = (vocab && Number.isFinite(+vocab.count)) ? Math.max(0, +vocab.count | 0)
      : (Array.isArray(src.words) ? src.words.length : 0);
    g.body.appendChild(readonlyRow('Subliminal vocabulary',
      words + ' word' + (words === 1 ? '' : 's') + (house ? ' (the school’s own)' : ''),
      house ? 'Your own list is empty, so the school lends you its vocabulary.'
        : (words ? null : 'Empty is legal - word effects simply skip.')));
    if (keybinds && keybinds.panicKey) {
      g.body.appendChild(readonlyRow('Panic key', keyLabel(keybinds.panicKey),
        'Never bindable to a class verb.'));
    }
    return g;
  }

  /** The web's 'This device' sheet. Only keys the shim echoes (host/init.js
   *  applySetting: masterVolume, motionLevel) - see the host note above. */
  function buildDevice() {
    const g = group(t('settings_device_head', 'This device'), 'device', () => [
      fill(t('settings_sum_volume', 'Volume {v}'), { v: pct(val('masterVolume', src.masterVolume == null ? 1 : src.masterVolume)) }),
      fill(t('settings_sum_motion', 'Motion {v}'), { v: motionName(val('motionLevel', src.motionLevel == null ? 2 : src.motionLevel)) }),
    ].join(SEP));
    g.body.appendChild(el('p', 'arc-note', t('settings_device_note',
      'Sound and motion for this browser, on this phone or PC. Nothing here leaves the device.')));
    g.body.appendChild(sliderRow({
      key: 'masterVolume',
      label: t('settings_master_volume', 'Master volume'),
      hint: t('settings_master_volume_hint', 'One dial over every sound the school makes.'),
      value: src.masterVolume == null ? 1 : src.masterVolume,
      min: 0, max: 1, step: 0.05,
    }));
    g.body.appendChild(selectRow({
      key: 'motionLevel',
      label: t('settings_motion', 'Motion'),
      hint: t('settings_motion_hint', 'Reduced keeps the room still. Off cuts every animation the school can cut.'),
      value: src.motionLevel == null ? 2 : src.motionLevel,
      options: [2, 1, 0],
      format: motionName,
    }));
    return g;
  }

  /* ---------------------- 2. global tier -------------------------------- */
  function buildGlobal() {
    const g = group(t('settings_distraction_head', 'Distraction'), 'distraction', () => [
      fill(t('settings_sum_intensity', 'Intensity {v}'), { v: pct(val(SETTING_KEYS.masterIntensity, 1)) }),
      fill(t('settings_sum_guard', 'Guard {v}'), { v: mult(val(SETTING_KEYS.effectIntensity, 0.85)) }),
    ].join(SEP));
    g.body.appendChild(sliderRow({
      key: SETTING_KEYS.masterIntensity,
      label: 'Master intensity',
      hint: 'One dial over every channel below.',
      value: src.masterIntensity == null ? 1 : src.masterIntensity,
      min: 0, max: 1, step: 0.05,
    }));
    g.body.appendChild(sliderRow({
      key: SETTING_KEYS.effectIntensity,
      label: 'Flash strength guard',
      hint: 'Photosensitivity guard - every strobe-class effect routes through it.',
      value: src.effectIntensity == null ? 0.85 : src.effectIntensity,
      min: 0.2, max: 1.5, step: 0.05, fmt: mult,
    }));

    const caps = (src.caps && typeof src.caps === 'object') ? src.caps : {};
    const gc = group(t('settings_channels_head', 'Channel ceilings'), 'channels', () => {
      let low = null;
      for (const c of CAP_CHANNELS) {
        const v = Number(val(SETTING_KEYS.caps[c.ch], 1));
        if (low == null || v < low.v) low = { v, label: c.label };
      }
      if (!low || low.v >= 1) return t('settings_sum_caps_all', 'All at 100%');
      return fill(t('settings_sum_caps_low', 'Lowest: {name} {v}'), { name: low.label, v: pct(low.v) });
    });
    gc.body.appendChild(el('p', 'arc-note', t('settings_channels_note', 'A class may use less than these. Never more.')));
    for (const c of CAP_CHANNELS) {
      gc.body.appendChild(sliderRow({
        key: SETTING_KEYS.caps[c.ch],
        label: c.label,
        value: caps[c.ch] == null ? 1 : caps[c.ch],
        min: 0, max: 1, step: 0.05,
      }));
    }

    const levels = (src.audioLevels && typeof src.audioLevels === 'object') ? src.audioLevels : {};
    const ga = group(t('settings_sound_head', 'Sound'), 'sound', () => (val(SETTING_KEYS.audioMute, false)
      ? t('settings_sum_muted', 'Muted')
      : fill(t('settings_sum_sound', 'On{sep}Music {v}'), { sep: SEP, v: pct(val(SETTING_KEYS.audio.music, 1)) })));
    ga.body.appendChild(switchRow({
      key: SETTING_KEYS.audioMute, label: 'Mute the Arcademy', value: !!src.audioMute,
    }));
    for (const a of AUDIO_GROUPS) {
      ga.body.appendChild(sliderRow({
        key: SETTING_KEYS.audio[a.g],
        label: a.label,
        value: levels[a.g] == null ? 1 : levels[a.g],
        min: 0, max: 1, step: 0.05,
      }));
    }

    const gt = group(t('settings_lessons_head', 'Lessons'), 'lessons', () => (val(SETTING_KEYS.hideTutorial, false)
      ? t('settings_sum_tutorials_off', 'Tutorials skipped')
      : t('settings_sum_tutorials_on', 'Tutorials on')));
    /* TODO (EMI): a "Show EMI" switch would sit here, but it is NOT a one-liner
     * and it would be a SECOND source of truth. The player already has an on/off
     * that persists - the x on EMI herself, which docks her (`emi.hidden` in the
     * meta blob). A row here would need its own protocol key, the host's
     * unknown-key bag, an echo path AND a wire down to the mounted controller
     * (`emi/index.js setEnabled`), and the two states would then have to be kept
     * in step. Wire it only if the owner wants EMI gone including the dock. */
    gt.body.appendChild(switchRow({
      key: SETTING_KEYS.hideTutorial,
      label: 'Skip class tutorials',
      hint: 'Skips the class rules sheet, even the first time you meet a class.',
      value: !!src.hideTutorial,
    }));

    /* THE MASCOT (owner, 2026-08-25: "make the bark bubble permanence time an
     * option in the options"). This is the page's ONE local group: the value
     * lives on the emi blob in the meta store and the mounted controller is
     * the writer, so there is no protocol key, no host bag and no echo to
     * wait for - the row applies on the spot and never wears `pending`.
     * `emi` is a GETTER (shell hands over `getEmi`): the controller mounts
     * async, and a page opened before the mascot resolves still renders the
     * row - it reads the stored blob and banks the choice there for the next
     * boot. The "Show EMI" switch stays deliberately unbuilt (see the note on
     * the Lessons group). */
    const BUBBLE_HOLDS = [
      { v: 0.7, key: 'quick', label: 'Quick' },
      { v: 1, key: 'normal', label: 'Normal' },
      { v: 1.5, key: 'long', label: 'Long' },
      { v: 2, key: 'extra', label: 'Extra long' },
    ];
    function mascotCtl() {
      try { return typeof emi === 'function' ? emi() : (emi || null); }
      catch (e) { return null; }
    }
    function bubbleHoldNow() {
      const ctl = mascotCtl();
      let v = ctl && typeof ctl.bubbleHold === 'number' ? ctl.bubbleHold : null;
      if (v == null) {
        try {
          const blob = store && typeof store.get === 'function' ? store.get('emi') : null;
          if (blob && typeof blob.holdScale === 'number' && isFinite(blob.holdScale)) v = blob.holdScale;
        } catch (e) { /* an unreadable blob is the default */ }
      }
      if (v == null || !isFinite(v)) v = 1;
      let best = BUBBLE_HOLDS[1];
      for (const o of BUBBLE_HOLDS) { if (Math.abs(o.v - v) < Math.abs(best.v - v)) best = o; }
      return best;
    }
    const gm = group(t('settings_mascot_head', 'Mascot'), 'mascot',
      () => t('emi_bubble_hold_' + bubbleHoldNow().key, bubbleHoldNow().label));
    {
      const row = el('div', 'arc-row');
      const lab = el('label', null, t('emi_bubble_hold_label', 'Speech bubble time'));
      lab.appendChild(el('span', 'arc-hint', t('emi_bubble_hold_hint',
        'How long her lines stay up before she lets them go. Questions always wait for an answer.')));
      const sel = el('select');
      for (const o of BUBBLE_HOLDS) {
        const opt = el('option', null, t('emi_bubble_hold_' + o.key, o.label));
        opt.value = String(o.v);
        if (o.v === bubbleHoldNow().v) opt.selected = true;
        sel.appendChild(opt);
      }
      lab.htmlFor = sel.id = 'arc-emi-bubble-hold';
      sel.addEventListener('change', () => {
        const n = Number(sel.value);
        if (!isFinite(n) || n <= 0) return;
        const ctl = mascotCtl();
        let applied = false;
        try { if (ctl && typeof ctl.setBubbleHold === 'function') { ctl.setBubbleHold(n); applied = true; } }
        catch (e) { /* the store write below still lands */ }
        if (!applied) {
          /* No controller on this page (a class screen, a mount that failed):
           * bank it straight on the blob, the same field the widget reads at
           * boot. `merge` keeps the rest of the blob honest. */
          try { if (store && typeof store.merge === 'function') store.merge('emi', { holdScale: n }); }
          catch (e) { /* nothing to do; the row simply did not take */ }
        }
        refreshSummaries();
      });
      row.appendChild(lab); row.appendChild(sel);
      gm.body.appendChild(row);
    }

    /* CAMPUS PRESENCE - the consent row (PRESENCE.md §3). It sits in the GLOBAL
     * tier and not in the read-only ceilings above, because it is the one thing
     * on this page the player grants rather than inherits: the app has no
     * surface for it, so this IS the surface.
     *
     * FOUR RUNGS AND EVERY LABEL SAYS WHAT IT SHOWS. A row called only
     * "Anonymous" is not consent copy; "a ghost with no name or picture" is.
     * Everything here goes through t(), the shell's whole string law, so a mod
     * can re-voice the copy - the C# NeutralLexicon mirrors all seven rows.
     *
     * The value is a STRING and selectRow sends it verbatim (its Number() path
     * only takes over for a value that round-trips as a number), which is what
     * the host's `presenceShare` clamp expects. Only the echo moves the model,
     * trap 1, exactly like every other row on this page. */
    const gp = group(t('presence_student_body', 'Student Body'), 'presence', () => {
      const rung = String(val(SETTING_KEYS.presenceShare, 'off'));
      const line = t('presence_share_' + rung, PRESENCE_FALLBACK[rung] || rung);
      return String(line).split(' - ')[0];
    });
    gp.body.appendChild(selectRow({
      key: SETTING_KEYS.presenceShare,
      label: t('presence_share_label', 'Show yourself on campus'),
      hint: t('presence_share_hint',
        'Your last 24 hours replay as a ghost. Room head counts include you at every rung.'),
      value: PRESENCE_RUNGS.indexOf(String(src.presenceShare)) >= 0 ? String(src.presenceShare) : 'off',
      options: PRESENCE_RUNGS,
      format: (o) => t('presence_share_' + o, PRESENCE_FALLBACK[o] || String(o)),
    }));
    gp.body.appendChild(el('p', 'arc-note', t('presence_share_discord_note',
      'Discord needs a linked account. Without one the school shows your name instead.')));

    const frag = document.createDocumentFragment();
    frag.appendChild(g); frag.appendChild(gc); frag.appendChild(ga); frag.appendChild(gt);
    frag.appendChild(gm);
    frag.appendChild(gp);
    return frag;
  }

  /* ---------------------- 2b. THE MEDIA COUNTER (web only) --------------
   * MEDIA-CONTRACT v1. Everything below is dead code on the app: `build()`
   * only calls buildMedia() when `mediaControls` is strictly true, so no
   * `media.*` frame can ever leave a WebView2 window.
   *
   * THE LAW OF THIS GROUP IS TRAP 1, and it bites harder here than anywhere
   * else on the page because the host REFUSES some of what it is asked. A
   * niche list that sanitizes to nothing is refused outright and the echo
   * carries the list the host still holds, so a control that painted itself
   * on the click would end up showing a state the host never stored. So every
   * control here posts, wears `pending`, and repaints from the echo.
   *
   * The two pushes (`library`, `local-media`) ride `bridge.on`, which is the
   * same loose seam provider/remote.js's `subscribe` wraps and is
   * multi-subscriber by design (trap 11) - listening here never steals the
   * provider's own `library` frames.
   * -------------------------------------------------------------------- */

  /** Keep only ids the catalog actually knows, deduped, in the order given. */
  function cleanNiches(list, known) {
    const out = [];
    const seen = new Set();
    for (const v of (Array.isArray(list) ? list : [])) {
      const id = String(v == null ? '' : v);
      if (!id || seen.has(id)) continue;
      if (known && known.length && known.indexOf(id) < 0) continue;
      seen.add(id);
      out.push(id);
    }
    return out;
  }

  /** The library, as PLAIN rows we may write `selected` on (the provider's are
   *  frozen, and its sanitizer predates the field). `selected` absent reads as
   *  in play: a host with no opinion about a sub is not hiding it. */
  function cleanLibrary(list) {
    const out = [];
    const seen = new Set();
    for (const e of (Array.isArray(list) ? list : [])) {
      const name = typeof e === 'string' ? e : (e && typeof e.name === 'string' ? e.name : '');
      if (!name) continue;
      const k = name.toLowerCase();
      if (seen.has(k)) continue;
      seen.add(k);
      const o = (e && typeof e === 'object') ? e : {};
      out.push({
        name,
        ok: o.ok != null ? !!o.ok : true,
        videoCount: Math.max(0, o.videoCount | 0),
        stillOnly: !!o.stillOnly,
        selected: o.selected != null ? !!o.selected : true,
      });
    }
    return out;
  }

  /** `{images, videos, skipped, active}`, whatever the frame actually carried. */
  function cleanPile(m) {
    const o = (m && typeof m === 'object') ? m : {};
    const images = Math.max(0, o.images | 0);
    const videos = Math.max(0, o.videos | 0);
    return {
      images,
      videos,
      skipped: Math.max(0, o.skipped | 0),
      active: o.active != null ? !!o.active : (images + videos > 0),
    };
  }

  /** Subscribe to a host frame for as long as this page is up. */
  function listen(type, fn) {
    if (!bridge || typeof bridge.on !== 'function') return;
    try {
      const off = bridge.on(type, fn);
      if (typeof off === 'function') offs.push(off);
    } catch (e) { say('media: no subscription for ' + type); }
  }

  /** The label + hint + trap-1 marker every media row wears. */
  function mediaLabel(text, hint) {
    const lab = el('span', 'arc-rowlabel', text);
    if (hint) lab.appendChild(el('span', 'arc-hint', hint));
    lab.appendChild(el('span', 'arc-media-wait', t('media_pending', 'writing it down')));
    return lab;
  }

  /* --- the niches ------------------------------------------------------- */
  function nicheRow() {
    const catalog = Array.isArray(flatBag.remoteCatalog) ? flatBag.remoteCatalog : [];
    const known = catalog.map((c) => String((c && c.id) || '')).filter(Boolean);
    const row = el('div', 'arc-row arc-media-row');
    row.appendChild(mediaLabel(
      t('media_niches_head', 'What we pull'),
      t('media_niches_hint', 'Tick as many as you like. The desk hangs on to the last one, since an'
        + ' empty board leaves the rooms with nothing to work with.')));
    const box = el('div', 'arc-media-chips');
    const note = el('p', 'arc-note arc-media-say', '');
    row.appendChild(box);
    row.appendChild(note);

    let selected = cleanNiches(flatBag.niches, known);
    let asked = null;              // what the last click posted, so a refusal is readable

    function paint() {
      box.textContent = '';
      if (!catalog.length) {
        box.appendChild(el('p', 'arc-note', t('media_niches_none', 'The desk has no list to offer tonight.')));
        return;
      }
      for (const c of catalog) {
        const id = String((c && c.id) || '');
        if (!id) continue;
        const on = selected.indexOf(id) >= 0;
        const label = el('label', 'arc-check' + (on ? ' on' : ''));
        const input = el('input');
        input.type = 'checkbox';
        input.checked = on;
        input.addEventListener('change', () => {
          const next = input.checked ? selected.concat([id]) : selected.filter((x) => x !== id);
          asked = next.slice();
          note.textContent = '';
          row.classList.add('pending');
          // TRAP 1: the box may stand where the thumb left it, but `selected`
          // does not move until applyEcho repaints from what is STORED.
          send(MEDIA_KEYS.niches, next);
        });
        label.appendChild(input);
        label.appendChild(el('span', null, String((c && c.label) || id)));
        box.appendChild(label);
      }
    }
    paint();

    rows.set(MEDIA_KEYS.niches, {
      node: row,
      apply(v) {
        const stored = cleanNiches(v, known);
        /* THE REFUSAL (contract §3): a list that sanitizes to empty is refused
         * and the host echoes what it still holds, so the box the player just
         * cleared comes straight back up. Say why, or it reads as a dead tap. */
        const refused = !!asked && asked.length === 0 && stored.length > 0;
        selected = stored;
        asked = null;
        // The page is rebuilt on every visit, so bank the host's answer or a
        // second trip through the front office repaints yesterday's ticks.
        flatBag.niches = stored.slice();
        paint();
        row.classList.remove('pending');
        refreshSummaries();
        note.textContent = refused
          ? t('media_niches_snapback', 'That was the last one ticked, so it went straight back up.'
            + ' Tick another and then you can drop it.')
          : '';
      },
    });
    return row;
  }

  /* --- the sub library -------------------------------------------------- */
  function libraryRow() {
    const canProbe = !!(assets && typeof assets.probeSub === 'function');
    const canRemove = !!(assets && typeof assets.removeLibrarySub === 'function');
    const row = el('div', 'arc-row arc-media-row');
    row.appendChild(mediaLabel(
      t('media_lib_head', 'Subs on your list'),
      t('media_lib_hint', 'Untick one to sit it out for a while, or use the X and it comes off'
        + ' the list everywhere.')));
    const list = el('div', 'arc-media-lib');
    const note = el('p', 'arc-note arc-media-say', '');
    row.appendChild(list);

    let library = cleanLibrary(flatBag.subLibrary);

    function paint() {
      list.textContent = '';
      if (!library.length) {
        list.appendChild(el('p', 'arc-note',
          t('media_lib_empty', 'Nothing on your list yet, so type a name below and we will go'
            + ' and see if it is really there.')));
        return;
      }
      for (const r of library) {
        const line = el('div', 'arc-media-libline');
        const label = el('label', 'arc-check' + (r.selected ? ' on' : ''));
        const input = el('input');
        input.type = 'checkbox';
        input.checked = !!r.selected;
        input.disabled = r.ok === false;
        input.addEventListener('change', () => {
          row.classList.add('pending');
          // Trap 1 again: the host flips the row and pushes a fresh `library`.
          send(MEDIA_KEYS.librarySelect, { name: r.name, selected: !!input.checked });
        });
        label.appendChild(input);
        label.appendChild(el('span', null, 'r/' + r.name));
        line.appendChild(label);
        const meta = r.stillOnly ? t('media_lib_stills', 'pictures only')
          : (r.videoCount ? r.videoCount + ' ' + t('media_lib_clips', 'clips') : '');
        if (meta) line.appendChild(el('span', 'arc-media-meta', meta));
        if (canRemove) {
          const x = el('button', 'arc-media-x', '✕');
          x.type = 'button';
          x.setAttribute('title', t('media_lib_remove', 'Take it off the list'));
          x.setAttribute('aria-label', t('media_lib_remove', 'Take it off the list') + ' r/' + r.name);
          x.addEventListener('click', () => {
            row.classList.add('pending');
            /* The provider drops its own copy optimistically; THIS list waits
             * for the host's `library` push, which is the only thing that
             * moves it. A pill that vanishes on a write the host refused is
             * exactly the drift trap 1 exists to stop. */
            try { Promise.resolve(assets.removeLibrarySub(r.name)).catch(() => {}); }
            catch (e) { say('media: removeLibrarySub threw'); }
          });
          line.appendChild(x);
        }
        list.appendChild(line);
      }
    }
    paint();

    /* THE ADD BOX. `probe-sub` is the existing frame and the provider owns the
     * round trip, its 15s backstop included, so there is no second probe here. */
    if (canProbe) {
      const add = el('div', 'arc-media-add');
      const input = el('input');
      input.type = 'text';
      input.placeholder = t('media_lib_add_ph', 'name of a sub');
      input.setAttribute('aria-label', t('media_lib_add_head', 'Add one'));
      const btn = el('button', 'btn ghost', t('media_lib_add_btn', 'Add'));
      btn.type = 'button';
      let busy = false;

      const submit = () => {
        if (busy) return;
        const name = String(input.value || '').trim().replace(/^\/?r\//i, '').trim();
        if (!name) return;
        if (library.some((r) => r.name.toLowerCase() === name.toLowerCase())) {
          note.textContent = 'r/' + name + ' ' + t('media_probe_dupe', 'is already on your list.');
          return;
        }
        busy = true;
        btn.disabled = true;
        note.textContent = t('media_probe_checking', 'Having a look for') + ' r/' + name;
        let p = null;
        try { p = assets.probeSub(name); } catch (e) { p = null; }
        Promise.resolve(p).then((res) => {
          busy = false;
          btn.disabled = false;
          const answered = String((res && res.name) || name);
          if (res && res.ok) {
            input.value = '';
            note.textContent = 'r/' + answered + ' ' + t('media_probe_ok', 'is on your list now.');
            // The row itself arrives on the host's `library` push, never here.
          } else {
            note.textContent = 'r/' + answered + ' '
              + t('media_probe_missing', 'came back empty, so give the spelling another go.');
          }
        }).catch(() => {
          busy = false;
          btn.disabled = false;
          note.textContent = 'r/' + name + ' '
            + t('media_probe_missing', 'came back empty, so give the spelling another go.');
        });
      };

      btn.addEventListener('click', submit);
      input.addEventListener('keydown', (ev) => {
        if (!ev || ev.key !== 'Enter') return;
        if (typeof ev.preventDefault === 'function') ev.preventDefault();
        submit();
      });
      add.appendChild(input);
      add.appendChild(btn);
      row.appendChild(add);
    }
    row.appendChild(note);

    rows.set(MEDIA_KEYS.librarySelect, {
      node: row,
      apply(v) {
        row.classList.remove('pending');
        /* `null` = the host did not know the name, so nothing moved and the
         * repaint below puts the tick back where the host still has it. */
        if (v && typeof v === 'object' && typeof v.name === 'string') {
          const k = v.name.toLowerCase();
          for (const r of library) if (r.name.toLowerCase() === k) r.selected = !!v.selected;
        }
        paint();
      },
    });

    /* THE PUSH IS THE TRUTH. Any surface can cause one (this box, SORT's door,
     * the shim's own housekeeping), so the list only ever repaints from here. */
    listen('library', (m) => {
      const payload = (m && m.detail) || m;
      if (!payload || !Array.isArray(payload.subLibrary)) return;
      library = cleanLibrary(payload.subLibrary);
      flatBag.subLibrary = payload.subLibrary;
      row.classList.remove('pending');
      paint();
    });

    return row;
  }

  /* --- the player's own pile -------------------------------------------- */
  function localRow() {
    const row = el('div', 'arc-row arc-media-row');
    row.appendChild(mediaLabel(
      t('media_local_head', 'Your own media'),
      t('media_local_hint', 'Hand over a folder, a zip, or a few things off your camera roll, and'
        + ' the rooms will deal them out like anything else. It stays on this device and it goes'
        + ' when you close the page.')));
    const btns = el('div', 'arc-media-btns');
    const counts = el('p', 'arc-note arc-media-counts', '');
    const bar = el('div', 'arc-media-bar');
    const fill = el('i');
    const phase = el('span', 'arc-media-phase', '');
    bar.appendChild(fill);
    bar.hidden = true;
    row.appendChild(btns);
    row.appendChild(counts);
    row.appendChild(bar);
    row.appendChild(phase);

    let pile = cleanPile(flatBag.localMedia);

    function paintCounts() {
      if (!pile.active) {
        counts.textContent = t('media_local_empty', 'Nothing of yours in the pile yet.');
        return;
      }
      let line = String(t('media_local_counts', '{images} pictures and {videos} clips in the pile'))
        .replace('{images}', String(pile.images))
        .replace('{videos}', String(pile.videos));
      if (pile.skipped) {
        line += ', ' + String(t('media_local_skipped', '{n} we could not read'))
          .replace('{n}', String(pile.skipped));
      }
      counts.textContent = line;
    }
    paintCounts();

    /* THE THREE PICKERS. `what` is the contract's own string, verbatim. */
    const PICKERS = [
      ['folder', 'media_local_folder', 'A folder'],
      ['zip', 'media_local_zip', 'A zip'],
      ['gallery', 'media_local_gallery', 'Some files'],
    ];
    for (const spec of PICKERS) {
      const btn = el('button', 'btn ghost', t(spec[1], spec[2]));
      btn.type = 'button';
      btn.addEventListener('click', () => {
        /* THE GESTURE RULE (MEDIA-CONTRACT §6) AND IT IS THE WHOLE REASON THIS
         * LINE IS FIRST. A file picker opens only while the browser's transient
         * user activation is still standing, and the shim's transport delivers
         * postMessage SYNCHRONOUSLY into its router - so the frame must be
         * posted from inside this handler with nothing awaited, promised or
         * timed out in front of it. Put ANY `await` above this line and the
         * picker silently never opens, with no error anywhere to read. */
        send(MEDIA_KEYS.pickLocal, spec[0]);
        row.classList.add('pending');
        counts.textContent = t('media_local_waiting', 'Waiting on your picker.');
      });
      btns.appendChild(btn);
    }

    const clear = el('button', 'btn ghost', t('media_local_clear', 'Clear the pile'));
    clear.type = 'button';
    clear.addEventListener('click', () => {
      send(MEDIA_KEYS.clearLocal, true);
      row.classList.add('pending');
    });
    btns.appendChild(clear);

    /* Both actions store nothing and echo `null`; the pile itself arrives on
     * the `local-media` push, so the echo only takes the marker down. */
    const clearPending = { node: row, apply() { row.classList.remove('pending'); } };
    rows.set(MEDIA_KEYS.pickLocal, clearPending);
    rows.set(MEDIA_KEYS.clearLocal, clearPending);

    listen('local-media', (m) => {
      const payload = (m && m.detail) || m;
      if (!payload) return;
      pile = cleanPile(payload);
      flatBag.localMedia = { images: pile.images, videos: pile.videos, skipped: pile.skipped, active: pile.active };
      bar.hidden = true;
      phase.textContent = '';
      row.classList.remove('pending');
      paintCounts();
    });

    /* Zip ingest only, and it may be ignored - a bar is just kinder on a phone. */
    listen('local-media-progress', (m) => {
      const payload = (m && m.detail) || m;
      if (!payload) return;
      const n = Number(payload.frac);
      const frac = Number.isFinite(n) ? Math.max(0, Math.min(1, n)) : 0;
      bar.hidden = false;
      fill.style.width = Math.round(frac * 100) + '%';
      phase.textContent = payload.phase === 'unpacking'
        ? t('media_progress_unpacking', 'Unpacking')
        : t('media_progress_reading', 'Reading');
    });

    return row;
  }

  /** The group itself. Only ever called behind the strict flag. */
  function buildMedia() {
    const g = group(t('media_head', 'Media'), 'media', () => {
      const on = !!val(MEDIA_KEYS.remoteConsent, !!flatBag.remoteConsent);
      const parts = [on ? t('settings_sum_online_on', 'Online on') : t('settings_sum_online_off', 'Online off')];
      if (on) {
        const catalog = Array.isArray(flatBag.remoteCatalog) ? flatBag.remoteCatalog : [];
        const picked = Array.isArray(flatBag.niches) ? flatBag.niches : [];
        const names = picked.map((id) => {
          const c = catalog.find((x) => x && String(x.id) === String(id));
          return c && c.label ? String(c.label) : String(id);
        });
        if (names.length) parts.push(names.slice(0, 3).join(', ') + (names.length > 3 ? ' +' + (names.length - 3) : ''));
      }
      return parts.join(SEP);
    });
    g.body.appendChild(el('p', 'arc-note',
      t('media_note', 'This is the counter where you say what the rooms are allowed to pull from.'
        + ' Anything you change is in play from your next class on, and whatever is running right'
        + ' now keeps the pile it already has.')));

    g.body.appendChild(switchRow({
      key: MEDIA_KEYS.remoteConsent,
      label: t('media_consent_label', 'Pull from online'),
      hint: t('media_consent_hint', 'With this off nothing goes out to the network at all, and the'
        + ' rooms run on whatever you have handed over yourself.'),
      value: !!flatBag.remoteConsent,
    }));
    /* switchRow is the shared builder and stays that way; the media counter
     * needs one extra thing from the echo, which is to bank the host's answer
     * into the local view of init.settings so a second visit paints it. */
    const consent = rows.get(MEDIA_KEYS.remoteConsent);
    if (consent) {
      const base = consent.apply;
      consent.apply = (v) => { flatBag.remoteConsent = !!v; base(v); };
    }

    g.body.appendChild(nicheRow());
    g.body.appendChild(libraryRow());
    g.body.appendChild(localRow());
    return g;
  }

  /* ---------------------- 3. per-game tier ------------------------------ */
  function gameValue(gameKey, key, fallback) {
    return Object.prototype.hasOwnProperty.call(flatBag, key) ? flatBag[key] : fallback;
  }

  function buildGame(entry) {
    const mod = entry && entry.mod;
    const manifest = (mod && mod.manifest) || {};
    const name = t('game_' + entry.key, (mod && mod.title) || entry.key);
    const bsKey = boardSizeKey(entry.key);
    /** The knobs this group draws, for the header line: [{key, kind, label, fmt}]. */
    const knobs = [];
    const g = group(name, 'game.' + entry.key, () => {
      if (!entry.ok) return t('class_suspended', 'Class Suspended');
      const parts = [];
      if (rows.has(bsKey)) parts.push(fill(t('settings_sum_board', 'Board {v}'), { v: val(bsKey, '') }));
      for (const k of knobs) {
        const v = val(k.key, undefined);
        if (v === undefined || v === null) continue;
        if (k.kind === 'bool') { if (v) parts.push(k.label); continue; }
        parts.push(k.kind === 'range' ? k.fmt(v) : String(v));
      }
      const n = keybinds ? keybinds.slotsFor(entry.key).length : 0;
      if (n) parts.push(n === 1 ? t('settings_sum_key_one', '1 key') : fill(t('settings_sum_keys', '{n} keys'), { n }));
      if (!parts.length) return t('settings_sum_nothing', 'Nothing to set');
      const shown = parts.slice(0, 4);
      return shown.join(SEP) + (parts.length > 4 ? ' +' + (parts.length - 4) : '');
    });

    if (!entry.ok) {
      g.body.appendChild(el('p', 'arc-note', t('class_suspended', 'Class Suspended')
        + ' — this class failed to load, so its options are hidden.'));
      return g;
    }

    let any = false;

    /* board-size row (promoted mechanism) */
    const bs = manifest.boardSizes;
    if (bs && Array.isArray(bs.values) && bs.values.length) {
      const key = boardSizeKey(entry.key);
      const value = gameValue(entry.key, boardSizeKey(entry.key), bs.values[0]);
      const par = bs.par || {};
      const parText = [1, 2, 3, 4].map((tier) => par[tier]).filter((v) => v != null).join(' / ');
      g.body.appendChild(selectRow({
        key,
        label: 'Board size',
        hint: 'Playing below your tier’s par caps the class at A.'
          + (parText ? ' Par by tier: ' + parText + '.' : ''),
        value,
        options: bs.values,
      }));
      any = true;
    }

    /* the game's own knobs */
    for (const s of (Array.isArray(manifest.settings) ? manifest.settings : [])) {
      if (!s || !s.key) continue;
      if (GLOBAL_RESERVED.has(s.key)) {
        say('settings: ' + entry.key + ' tried to re-expose global "' + s.key + '" - skipped');
        continue;
      }
      const key = s.key;
      const label = s.label_key ? t(s.label_key, s.key) : s.key;
      const value = gameValue(entry.key, s.key, s.default);
      if (s.kind === 'bool') {
        g.body.appendChild(switchRow({ key, label, hint: s.hint_key ? t(s.hint_key, '') : null, value: !!value }));
        knobs.push({ key, kind: 'bool', label });
      } else if (s.kind === 'enum' && Array.isArray(s.values)) {
        g.body.appendChild(selectRow({ key, label, value, options: s.values }));
        knobs.push({ key, kind: 'enum', label });
      } else if (s.kind === 'range') {
        const fmt = s.fmt === 'mult' ? mult : pct;
        g.body.appendChild(sliderRow({
          key, label, value: value == null ? 0 : value,
          min: s.min == null ? 0 : s.min,
          max: s.max == null ? 1 : s.max,
          step: s.step == null ? 0.05 : s.step,
          fmt,
        }));
        knobs.push({ key, kind: 'range', label, fmt });
      } else {
        say('settings: ' + entry.key + ' setting "' + s.key + '" has unknown kind "' + s.kind + '"');
        continue;
      }
      any = true;
    }

    /* keybind slots */
    const slots = keybinds ? keybinds.slotsFor(entry.key) : [];
    if (slots.length) {
      for (const slot of slots) g.body.appendChild(keyRow(entry.key, slot));
      any = true;
    }

    if (!any) g.body.appendChild(el('p', 'arc-note', t('settings_game_nothing', 'Nothing to configure - this class runs on the globals.')));
    return g;
  }

  /* ---------------------- assemble -------------------------------------- */
  function build() {
    root.textContent = '';
    const close = () => { try { if (onClose) onClose(); } catch (e) { /* noop */ } };

    /* THE SPLIT. A scoped page shows the one game the player is actually in;
     * the full list stays the campus/Front Office's. Scope resolves ONCE, here:
     * a gameKey that matches nothing (a retired class, a typo'd caller) falls
     * back to the full sheet rather than silently hiding every knob. */
    const list = Array.isArray(games) ? games : [];
    const scopedEntry = gameKey
      ? list.find((e) => e && e.key === String(gameKey)) || null
      : null;
    if (gameKey && !scopedEntry) say('settings scope "' + gameKey + '" unknown - full page');
    const shown = scopedEntry ? [scopedEntry] : list;

    const head = el('div', 'arc-classbar');
    const back = el('button', 'btn ghost', t('back', 'Back'));
    back.type = 'button';
    back.addEventListener('click', close);
    head.appendChild(back);
    const title = scopedEntry
      ? t('game_' + scopedEntry.key, (scopedEntry.mod && scopedEntry.mod.title) || scopedEntry.key)
        + ' - ' + t('settings', 'Settings')
      : t('settings', 'Settings');
    head.appendChild(el('span', 'arc-title', title));
    root.appendChild(head);

    /* THE RAIL AND THE PANE. Every section lives in `pane`; in portrait and on
     * a desktop the pane is `display:contents` and the rail is not drawn, so
     * the grid of folds is the one that has always been here. */
    rail = el('nav', 'arc-set-railnav');
    rail.setAttribute('role', 'tablist');
    rail.setAttribute('aria-orientation', 'vertical');
    rail.setAttribute('aria-label', t('settings', 'Settings'));
    rail.appendChild(el('span', 'arc-set-railtitle', title));
    arrowNav(rail, ['ArrowUp', 'ArrowLeft'], ['ArrowDown', 'ArrowRight']);
    root.appendChild(rail);
    pane = el('div', 'arc-set-pane');
    /* THE WHISPER, WHOLE. In the rail the hint under a label is one ellipsised
     * line; a tap on the hint itself unclamps it for that row. The tap is
     * swallowed here because the hint lives inside the <label>, and a label's
     * click is the control's click - a switch would flip, a slider would
     * focus. Outside the rail the hint is already whole and the tap is the
     * label's as before. */
    pane.addEventListener('click', (e) => {
      if (!railMode) return;
      const hint = e.target && e.target.closest ? e.target.closest('.arc-hint') : null;
      if (!hint || !pane.contains(hint)) return;
      e.preventDefault();
      const row = hint.closest('.arc-row');
      if (row) row.classList.toggle('arc-hint-on');
    });
    root.appendChild(pane);
    strip = el('div', 'arc-set-classes');
    strip.setAttribute('role', 'tablist');
    strip.setAttribute('aria-label', t('settings_classes_head', 'Classes'));
    strip.hidden = true;
    arrowNav(strip, ['ArrowLeft', 'ArrowUp'], ['ArrowRight', 'ArrowDown']);
    pane.appendChild(strip);

    /* ORDER. The app keeps ceilings first, exactly as before. On the web the
     * Media counter goes first - it is the thing a player opens this page for,
     * and on a phone it is the one section that starts open - with the device
     * sheet under it. Strict flag on Media either way. */
    if (host === 'web' && mediaControls) {
      pane.appendChild(buildMedia());
      pane.appendChild(buildCeilings());
    } else {
      pane.appendChild(buildCeilings());
      if (mediaControls) pane.appendChild(buildMedia());
    }
    /* CAMPUS LOOK sits under the ceilings and above Distraction: it is the one
     * group on this sheet the player BOUGHT, and burying a prize at the bottom
     * of a ten-class page is the same as not shipping it. Absent entirely until
     * a theme is owned, and absent on the scoped (mid-class) sheet, which is
     * about one game and not about the school. */
    if (!scopedEntry && hasThemeChoice()) pane.appendChild(buildLook());
    pane.appendChild(buildGlobal());
    for (const entry of shown) {
      if (!entry || !entry.key) continue;
      // Declare the game's slots so the keybind rows (and conflict checks) exist
      // even if the class has not been played yet this session.
      if (keybinds && entry.mod && entry.mod.manifest) {
        keybinds.declare(entry.key, entry.mod.manifest.keybinds);
      }
      pane.appendChild(buildGame(entry));
    }
    /* The scoped page is only reachable mid-class, and ctx.settings is a
     * snapshot taken at startClass - so a knob moved here lands NEXT run.
     * One honest line beats a silent surprise (owner ruling 2026-08-24). */
    if (scopedEntry) {
      pane.appendChild(el('p', 'arc-note',
        t('applies_next_class', 'Class option changes take effect next class.')));
    }

    /* PLACING THE TABS. Tier sections go in the rail in page order. The
     * classes share ONE rail entry and a chip strip in the pane - unless the
     * page is scoped to a single class, which then gets its own rail entry
     * (it is the reason the player is here) and no strip. */
    const classSections = sections.filter((s) => isClassId(s.id));
    if (scopedEntry && classSections.length === 1) classSections[0].scoped = true;
    const useStrip = !scopedEntry && classSections.length > 0;
    if (useStrip) {
      classesTab = el('button', 'arc-set-tab');
      classesTab.type = 'button';
      classesTab.setAttribute('role', 'tab');
      classesTab.setAttribute('aria-selected', 'false');
      classesTab.tabIndex = -1;
      classesTab.appendChild(el('span', 'arc-set-tab-t', t('settings_classes_head', 'Classes')));
      classesTab.addEventListener('click', () => selectSection(activeClassId || classSections[0].id));
    }
    for (const s of sections) {
      if (isClassId(s.id) && useStrip) {
        s.tab.classList.add('arc-set-chip');
        strip.appendChild(s.tab);
        if (classesTab && !classesTab.parentNode) rail.appendChild(classesTab);
      } else {
        rail.appendChild(s.tab);
      }
    }

    /* THE STICKY WAY OUT. This page is ten classes long - three ceiling rows,
     * the whole global tier, then a group per class with its keybind slots - so
     * the Back at the top scrolls out of reach inside a screen or two and the
     * only way back to it is to scroll all the way up again. The lit sign below
     * rides the bottom of the viewport for as long as the page is up, which is
     * the same guarantee every other Arcademy screen now makes. */
    const out = el('button', 'btn primary', t('back', 'Back'));
    out.type = 'button';
    out.addEventListener('click', close);
    signExit(out, { dir: 'back' });
    const bar = exitBar([out]);
    bar.className += ' arc-settings-exit';
    root.appendChild(bar);
    refreshSummaries();

    /* ARM THE RAIL. Now, for the page as mounted; again on every real device
     * change (a rotate flips the class on <html> before the seam fires) and on
     * a plain resize, which is what a topbar wrapping onto two lines is. */
    layout();
    try { offs.push(onDeviceChange(() => layout())); } catch (e) { /* noop */ }
    try {
      const onResize = () => layout();
      window.addEventListener('resize', onResize);
      offs.push(() => window.removeEventListener('resize', onResize));
    } catch (e) { /* noop */ }
    return root;
  }

  return {
    root: build(),
    /** Apply the host's post-clamp echo. THE only path that moves the model. */
    applyEcho(key, value) {
      if (key === SETTING_KEYS.keybinds) { if (keybinds) keybinds.applyEcho(value); return; }
      const row = rows.get(key);
      if (!row) return;
      /* Was this echo the answer to OUR write? `pending` is the only marker of
       * that, and `row.apply` clears it - so the question has to be asked here,
       * one line early. Without it a host-initiated push (the app's own volume
       * mixer moving, a mod applying a profile) would make this page beep at a
       * player who never touched a slider. */
      const mine = !!(row.node && row.node.classList && row.node.classList.contains('pending'));
      try { row.apply(value); }
      catch (e) { say('settings echo ' + key + ' failed: ' + ((e && e.message) || e)); }
      if (mine && typeof key === 'string' && key.indexOf('audioLevels.') === 0) {
        previewBusLevel(key.slice('audioLevels.'.length));
      } else if (mine) {
        /* W3 P0-34: EVERY OTHER CONTROL LANDS TOO. The mixer sliders have had
         * their preview since AV CLUB and every other row on the sheet - the
         * toggles, the enums, the per-game dials - moved in total silence, so
         * the one page in the school that is nothing but input answered none
         * of it. Same law as the preview and for the same reason (trap 88): it
         * rides the ECHO, and only OUR echo, so a host-initiated push never
         * beeps at a player who touched nothing. Up for on, down for off. */
        sfx('tell', 0.22, { pitch: value ? 1.08 : 0.92 });
      }
    },
    /** Current per-game values as a game sees them (see shell.js ctx.settings). */
    gameSettingsFor(gameKey, manifest) {
      const out = {};
      const m = manifest || {};
      for (const s of (Array.isArray(m.settings) ? m.settings : [])) {
        if (!s || !s.key || GLOBAL_RESERVED.has(s.key)) continue;
        out[s.key] = gameValue(gameKey, s.key, s.default);
      }
      if (m.boardSizes && Array.isArray(m.boardSizes.values) && m.boardSizes.values.length) {
        const bk = boardSizeKey(gameKey);
        out[bk] = gameValue(gameKey, bk, m.boardSizes.values[0]);
        out.boardSize = out[bk];
      }
      return out;
    },
    /** Keep the local view of the flat bag current when a PER-GAME echo arrives. */
    noteEcho(key, value) {
      if (typeof key === 'string' && key && !isGlobalSettingKey(key)) flatBag[key] = value;
    },
    /** The folds, for a test rig: [{id, isOpen(), node}]. */
    sections: () => sections.map((x) => ({ id: x.id, isOpen: x.isOpen, node: x.node })),
    /** The landscape rail, for a test rig: mode, the lit section, select(id). */
    rail: () => ({ on: railMode, active: activeId, select: selectSection }),
    host,
    destroy() {
      for (const off of offs.splice(0)) { try { off(); } catch (e) { /* noop */ } }
      // A preview owed to a page that is already gone is a beep from nowhere.
      if (previewTimer) { try { clearTimeout(previewTimer); } catch (e) { /* noop */ } }
      previewTimer = 0; previewBus = null;
      rows.clear();
      root.remove();
    },
  };
}

export default createSettingsPage;
