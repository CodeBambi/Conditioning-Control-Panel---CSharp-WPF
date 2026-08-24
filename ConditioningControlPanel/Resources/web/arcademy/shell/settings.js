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
  /** The whole keybind map, as an OBJECT (the host stores it as JSON itself). */
  keybinds: 'keybinds',
});

/** Every global key this page can write - used to tell an echo apart from a game's. */
const GLOBAL_KEYS = new Set([
  SETTING_KEYS.masterIntensity, SETTING_KEYS.effectIntensity,
  SETTING_KEYS.audioMute, SETTING_KEYS.hideTutorial, SETTING_KEYS.keybinds,
  'masterVolume', 'remoteMediaRatio',
  // WHOLE-OBJECT echoes are globals too (W0, 2026-08-24): the host answers the
  // mixer as one `{key:'audioLevels', value:{...}}` frame, and the undotted key
  // used to slip this fence and land the whole object in the per-game flat bag.
  'audioLevels', 'caps',
].concat(Object.keys(SETTING_KEYS.caps).map((k) => SETTING_KEYS.caps[k]))
  .concat(Object.keys(SETTING_KEYS.audio).map((k) => SETTING_KEYS.audio[k])));

/** True when a `setting` echo is a global, not a per-game knob. */
export function isGlobalSettingKey(key) {
  const k = String(key || '');
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

/** A per-game manifest may not name any of these (they are global). */
const GLOBAL_RESERVED = new Set(
  ['masterIntensity', 'effectIntensity', 'audioMute', 'hideTutorial', 'mediaSource',
    'remoteMediaRatio', 'offlineMode', 'masterVolume', 'motionLevel', 'reducedMotion']
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
 */
export function createSettingsPage({ init, bridge, games, keybinds, onClose, log, gameKey } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const src = init || {};
  const root = el('div', 'arc-settings');
  /** setting key -> {apply(value), node} so an echo can find its row. */
  const rows = new Map();
  const flatBag = (src.settings && typeof src.settings === 'object') ? src.settings : {};

  function send(key, value) {
    if (!bridge || typeof bridge.send !== 'function') return;
    bridge.send({ type: 'set-setting', key: String(key), value });
  }

  /** Register + mark pending; the echo clears it. */
  function write(key, value, row) {
    if (row) row.classList.add('pending');
    send(key, value);
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
      apply(v) {
        input.value = String(v);
        out.textContent = (fmt || pct)(v);
        row.classList.remove('pending');
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
      apply(v) { input.checked = !!v; row.classList.remove('pending'); },
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
      apply(v) { sel.value = String(v); row.classList.remove('pending'); },
    });
    return row;
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
        paintCap();
      } else {
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

  function group(title) {
    const g = el('div', 'arc-group');
    g.appendChild(el('h3', null, title));
    return g;
  }

  /* ---------------------- 1. app ceilings (read-only) ------------------- */
  function buildCeilings() {
    const g = group('App ceilings');
    g.appendChild(el('p', 'arc-note',
      'Owned by the app, shown here so you know what the school is working with. '
      + 'Change these in the app’s own settings.'));

    const source = src.offlineMode ? 'local only (offline mode)'
      : (src.remoteMediaEnabled ? 'local + online' : 'local only');
    g.appendChild(readonlyRow('Asset source', source,
      'Online media follows the app’s media source and consent.'));
    if (src.remoteMediaEnabled) {
      g.appendChild(readonlyRow('Online media share', pct(src.remoteMediaRatio)));
    }
    g.appendChild(readonlyRow('Master volume', pct(src.masterVolume == null ? 1 : src.masterVolume)));
    g.appendChild(readonlyRow('Subliminal audio', src.audioAudible ? 'audible' : 'silent'));
    g.appendChild(readonlyRow('Motion', String(src.motionLevel == null ? 2 : src.motionLevel)
      + (src.reducedMotion ? ' (reduced)' : '')));
    if (src.performanceMode) g.appendChild(readonlyRow('Performance mode', 'on'));
    const words = Array.isArray(src.words) ? src.words.length : 0;
    g.appendChild(readonlyRow('Subliminal vocabulary', words + ' word' + (words === 1 ? '' : 's'),
      words ? null : 'Empty is legal - word effects simply skip.'));
    if (keybinds && keybinds.panicKey) {
      g.appendChild(readonlyRow('Panic key', keyLabel(keybinds.panicKey),
        'Never bindable to a class verb.'));
    }
    return g;
  }

  /* ---------------------- 2. global tier -------------------------------- */
  function buildGlobal() {
    const g = group('Distraction');
    g.appendChild(sliderRow({
      key: SETTING_KEYS.masterIntensity,
      label: 'Master intensity',
      hint: 'One dial over every channel below.',
      value: src.masterIntensity == null ? 1 : src.masterIntensity,
      min: 0, max: 1, step: 0.05,
    }));
    g.appendChild(sliderRow({
      key: SETTING_KEYS.effectIntensity,
      label: 'Flash strength guard',
      hint: 'Photosensitivity guard - every strobe-class effect routes through it.',
      value: src.effectIntensity == null ? 0.85 : src.effectIntensity,
      min: 0.2, max: 1.5, step: 0.05, fmt: mult,
    }));

    const caps = (src.caps && typeof src.caps === 'object') ? src.caps : {};
    const gc = group('Channel ceilings');
    gc.appendChild(el('p', 'arc-note', 'A class may use less than these. Never more.'));
    for (const c of CAP_CHANNELS) {
      gc.appendChild(sliderRow({
        key: SETTING_KEYS.caps[c.ch],
        label: c.label,
        value: caps[c.ch] == null ? 1 : caps[c.ch],
        min: 0, max: 1, step: 0.05,
      }));
    }

    const levels = (src.audioLevels && typeof src.audioLevels === 'object') ? src.audioLevels : {};
    const ga = group('Sound');
    ga.appendChild(switchRow({
      key: SETTING_KEYS.audioMute, label: 'Mute the Arcademy', value: !!src.audioMute,
    }));
    for (const a of AUDIO_GROUPS) {
      ga.appendChild(sliderRow({
        key: SETTING_KEYS.audio[a.g],
        label: a.label,
        value: levels[a.g] == null ? 1 : levels[a.g],
        min: 0, max: 1, step: 0.05,
      }));
    }

    const gt = group('Lessons');
    /* TODO (EMI): a "Show EMI" switch would sit here, but it is NOT a one-liner
     * and it would be a SECOND source of truth. The player already has an on/off
     * that persists - the x on EMI herself, which docks her (`emi.hidden` in the
     * meta blob). A row here would need its own protocol key, the host's
     * unknown-key bag, an echo path AND a wire down to the mounted controller
     * (`emi/index.js setEnabled`), and the two states would then have to be kept
     * in step. Wire it only if the owner wants EMI gone including the dock. */
    gt.appendChild(switchRow({
      key: SETTING_KEYS.hideTutorial,
      label: 'Skip class tutorials',
      hint: 'Skips the class rules sheet, even the first time you meet a class.',
      value: !!src.hideTutorial,
    }));

    const frag = document.createDocumentFragment();
    frag.appendChild(g); frag.appendChild(gc); frag.appendChild(ga); frag.appendChild(gt);
    return frag;
  }

  /* ---------------------- 3. per-game tier ------------------------------ */
  function gameValue(gameKey, key, fallback) {
    return Object.prototype.hasOwnProperty.call(flatBag, key) ? flatBag[key] : fallback;
  }

  function buildGame(entry) {
    const mod = entry && entry.mod;
    const manifest = (mod && mod.manifest) || {};
    const name = t('game_' + entry.key, (mod && mod.title) || entry.key);
    const g = group(name);

    if (!entry.ok) {
      g.appendChild(el('p', 'arc-note', t('class_suspended', 'Class Suspended')
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
      g.appendChild(selectRow({
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
        g.appendChild(switchRow({ key, label, hint: s.hint_key ? t(s.hint_key, '') : null, value: !!value }));
      } else if (s.kind === 'enum' && Array.isArray(s.values)) {
        g.appendChild(selectRow({ key, label, value, options: s.values }));
      } else if (s.kind === 'range') {
        g.appendChild(sliderRow({
          key, label, value: value == null ? 0 : value,
          min: s.min == null ? 0 : s.min,
          max: s.max == null ? 1 : s.max,
          step: s.step == null ? 0.05 : s.step,
          fmt: s.fmt === 'mult' ? mult : pct,
        }));
      } else {
        say('settings: ' + entry.key + ' setting "' + s.key + '" has unknown kind "' + s.kind + '"');
        continue;
      }
      any = true;
    }

    /* keybind slots */
    const slots = keybinds ? keybinds.slotsFor(entry.key) : [];
    if (slots.length) {
      for (const slot of slots) g.appendChild(keyRow(entry.key, slot));
      any = true;
    }

    if (!any) g.appendChild(el('p', 'arc-note', 'Nothing to configure - this class runs on the globals.'));
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

    root.appendChild(buildCeilings());
    root.appendChild(buildGlobal());
    for (const entry of shown) {
      if (!entry || !entry.key) continue;
      // Declare the game's slots so the keybind rows (and conflict checks) exist
      // even if the class has not been played yet this session.
      if (keybinds && entry.mod && entry.mod.manifest) {
        keybinds.declare(entry.key, entry.mod.manifest.keybinds);
      }
      root.appendChild(buildGame(entry));
    }
    /* The scoped page is only reachable mid-class, and ctx.settings is a
     * snapshot taken at startClass - so a knob moved here lands NEXT run.
     * One honest line beats a silent surprise (owner ruling 2026-08-24). */
    if (scopedEntry) {
      root.appendChild(el('p', 'arc-note',
        t('applies_next_class', 'Class option changes take effect next class.')));
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
    return root;
  }

  return {
    root: build(),
    /** Apply the host's post-clamp echo. THE only path that moves the model. */
    applyEcho(key, value) {
      if (key === SETTING_KEYS.keybinds) { if (keybinds) keybinds.applyEcho(value); return; }
      const row = rows.get(key);
      if (!row) return;
      try { row.apply(value); }
      catch (e) { say('settings echo ' + key + ' failed: ' + ((e && e.message) || e)); }
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
    destroy() { rows.clear(); root.remove(); },
  };
}

export default createSettingsPage;
