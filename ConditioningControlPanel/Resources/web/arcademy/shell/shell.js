/* ============================================================================
 * shell/shell.js - the screen router and the class runner.
 *
 * THREE SCREENS, one mount: the split-flap timetable board -> a class -> the
 * report card (plus the settings page, which is a screen too). Nothing here
 * renders a game: it deals the board, builds the §11 ctx, hands the class its
 * root and gets out of the way.
 *
 * DEFENSIVE BY DEFAULT (intake's guarded-registry philosophy):
 *   - engine/ and provider/ are OPTIONAL imports. If either is missing, still
 *     loading, or throws at import, the shell substitutes a null object and the
 *     class still runs - silent, but playable. A missing Distraction Engine must
 *     cost you the distractions, not the school.
 *   - every game call (create/start/pause/suspend/destroy) is try/caught. A game
 *     that throws on start gets the class_suspended treatment instead of taking
 *     the shell down with it.
 *   - the engine handle a class receives is ALLOWLISTED to its manifest's
 *     effectsConsumed, and deliberately does NOT expose suspend()/dispose() -
 *     lifecycle belongs to the shell, so a game cannot un-suspend itself while a
 *     mandatory video is playing.
 *
 * WHO OWNS WHAT
 *   grade  -> core/grades.js (never a game)
 *   tier   -> games/registry.js + the meta store (never a game)
 *   XP     -> C# (the page only reads payout-result)
 *   dates  -> UTC seeds content, LOCAL date rolls attendance (regression #978)
 * ==========================================================================*/

import { t, setLexicon, tierLabel } from '../core/lexicon.js';
import { makeRng } from '../core/rng.js';
import { buildTimetable, dayAdd } from '../core/timetable.js';
import { gradeClass, capsRaised } from '../core/grades.js';
import { createStore } from '../core/store.js';
import { loadGames, descriptors, tierFor, advance, suspendedStub } from '../games/registry.js';
import { createBoard } from './splitflap.js';
import { createReportCard } from './reportcard.js';
import { createSettingsPage, boardSizeKey, SETTING_KEYS, isGlobalSettingKey } from './settings.js';
import { createCeremonies } from './ceremonies.js';
import { createPeek } from './peek.js';
import { createKeybinds } from './keybinds.js';

const FLAVOR_XP_CAP = 15;          // BUILD-CONTRACT §8 - the page clamps too
const MEATY_MAX_SEC = 300;
const QUICK_MAX_SEC = 180;

/** Palette keys we accept from init.palette, and the CSS token each one drives. */
const PALETTE_TOKENS = Object.freeze({
  ground: '--ground', navy: '--navy', panel: '--panel', ink: '--ink',
  accent: '--pink', accent2: '--lav', gold: '--gold',
  // tolerated aliases so a mod skin authored against the mockup's names works
  pink: '--pink', lav: '--lav', lavender: '--lav', line: '--line',
});

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

/* ----------------------------------------------------------------------------
 * OPTIONAL MODULES - intake's loadOptional, verbatim in spirit.
 * -------------------------------------------------------------------------- */
async function loadOptional(path, factoryName, fallback, say) {
  try {
    const mod = await import(path);
    const f = mod && (mod[factoryName] || (mod.default && typeof mod.default === 'function' ? mod.default : null));
    if (typeof f === 'function') return f;
    say('module ' + path + ': no ' + factoryName + '() export - using fallback');
  } catch (e) {
    say('module ' + path + ': import failed (' + ((e && e.message) || e) + ') - using fallback');
  }
  return fallback;
}

const NULL_ENGINE_FACTORY = () => ({
  setHeat() {}, fire() { return false; }, sustain() { return false; }, stop() {},
  setpiece() {}, beat() {}, ceremony() { return false; }, suspend() {}, dispose() {},
});

const NULL_ASSETS_FACTORY = () => ({
  claim() { return Promise.resolve({ next() { return null; }, release() {} }); },
});

/* ============================================================================
 * THE SHELL
 * ==========================================================================*/
/**
 * @param {Object} o
 * @param {Object} o.init      the init projection
 * @param {Object} o.bridge
 * @param {Object} o.dom       {topbar, screen, fx, ceremony}
 * @param {Function=} o.toast
 * @param {Function=} o.log
 */
export async function createShell({ init, bridge, dom, toast, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const shout = typeof toast === 'function' ? toast : () => {};
  const src = init || {};

  /* ---------------------- look & lexicon -------------------------------- */
  setLexicon(src.lexicon);
  applyPalette(src.palette, say);
  const reducedMotion = !!src.reducedMotion || src.motionLevel === 0;
  if (reducedMotion && document.documentElement) {
    document.documentElement.classList.add('arc-reduced');
  }

  /* ---------------------- state ----------------------------------------- */
  const utcDateSeed = String(src.utcDateSeed || '');
  const localDate = String(src.localDate || utcDateSeed);

  /* THE DAY'S WORD POOL. init.words is the floor (it may legally be EMPTY); a
   * class may ADD to it through ctx.absorb / ctx.sessionWords, and every engine
   * built after that gets the longer list - so a word absorbed in Homeroom rides
   * the rest of today's classes. SESSION-ONLY, both ways: nothing here is
   * persisted, nothing is sent to the host, and SubliminalPool is never written
   * (DECISIONS #10, the ramp-never-writes precedent). Reload and it is gone. */
  const ABSORB_MAX_LEN = 40;         // a subliminal card, not a paragraph
  const ABSORB_MAX_ADDED = 64;       // a runaway game cannot grow this unbounded
  const dayWords = (Array.isArray(src.words) ? src.words : [])
    .filter((w) => typeof w === 'string' && w.trim());
  let absorbed = 0;

  /** @returns {boolean} true when the word was taken (new, legal, under the cap). */
  function absorbWord(word) {
    if (typeof word !== 'string') return false;
    const w = word.trim();
    // Control characters would render as tofu in a sub_flash card; a newline
    // would break the one-line layout outright.
    if (!w || w.length > ABSORB_MAX_LEN || /[\u0000-\u001f\u007f]/.test(w)) return false;
    if (absorbed >= ABSORB_MAX_ADDED) return false;
    if (dayWords.some((x) => x.toUpperCase() === w.toUpperCase())) return false;
    dayWords.push(w);
    absorbed += 1;
    say('absorbed a session word (' + dayWords.length + ' in the day pool)');
    return true;
  }

  /** The sink a class reaches for. `add` is the verb Daily Trigger probes. */
  const sessionWords = {
    add: absorbWord,
    list: () => dayWords.slice(),
    get size() { return dayWords.length; },
  };

  const store = createStore({ bridge, initialMeta: src.meta, log: say });
  const keybinds = createKeybinds({ init: src, bridge, log: say });

  /** gameKey -> {grade, zen, composite, capped, tier, xp, levelUp} for TODAY. */
  const results = Object.create(null);
  /** gameKey -> share payload handed to the one share pipeline. */
  const shares = Object.create(null);

  let screen = 'board';            // 'board' | 'class' | 'report' | 'settings'
  let board = null;
  let settingsPage = null;
  let reportCard = null;
  let active = null;               // the running class (see startClass)
  let suspendedGlobally = false;
  let destroyed = false;

  /* ---------------------- registry + timetable -------------------------- */
  const games = await loadGames(say);
  const timetable = buildTimetable({
    dateSeed: utcDateSeed,
    registry: descriptors(games.list),
    overrideCalendar: src.overrideCalendar,
  });
  say('timetable ' + timetable.dateSeed + ' [' + timetable.source + ']: '
    + timetable.classes.map((c) => c.gameKey).join(', ')
    + (timetable.relaxed.length ? ' (relaxed: ' + timetable.relaxed.join(',') + ')' : ''));

  /* ---------------------- engine + provider ----------------------------- */
  const createEngine = await loadOptional('../engine/index.js', 'createEngine', NULL_ENGINE_FACTORY, say);
  const createAssets = await loadOptional('../provider/index.js', 'createAssets', NULL_ASSETS_FACTORY, say);

  let assets;
  try {
    assets = createAssets({
      bridge,
      remoteMediaEnabled: !!src.remoteMediaEnabled,
      remoteMediaRatio: src.remoteMediaRatio == null ? 0 : src.remoteMediaRatio,
      offlineMode: !!src.offlineMode,
      platform: src.platform || { isTouch: false, hasHaptics: false, host: 'desktop' },
      // The page cannot enumerate a virtual host, so the host ships the local
      // inventory as `localAssets` riding init.settings; the provider reads it
      // off this very object (provider/index.js resolveManifest).
      settings: src.settings,
      // Seeded so the same UTC day serves the same media order to everyone, and
      // logged straight to Serilog instead of the CustomEvent seam.
      rng: makeRng(utcDateSeed + '|assets'),
      log: (m) => say('[assets] ' + m),
    }) || NULL_ASSETS_FACTORY();
  } catch (e) {
    say('createAssets threw (' + ((e && e.message) || e) + ') - local-only null provider');
    assets = NULL_ASSETS_FACTORY();
  }

  const ceremonies = createCeremonies({
    engine: null,                  // rebound per class (the engine is per class)
    layer: dom && dom.ceremony,
    reducedMotion,
    log: say,
  });

  reportCard = createReportCard({ ceremonies, toast: shout, log: say });

  /* ---------------------- helpers --------------------------------------- */
  function gameName(key) {
    const entry = games.byKey[key];
    const title = entry && entry.mod && entry.mod.title;
    return t('game_' + key, title || String(key).replace(/_/g, ' '));
  }

  function maxTier() {
    let best = 1;
    for (const entry of games.list) {
      const tier = tierFor(store.gameMeta(entry.key));
      if (tier > best) best = tier;
    }
    return best;
  }

  function allDone() {
    const keys = timetable.classes.map((c) => c.gameKey);
    return keys.length > 0 && keys.every((k) => !!results[k]);
  }

  /**
   * Today's recorded row for a class, from this session OR from the meta store
   * (the player may have graded it, closed the Arcademy and come back). Its
   * presence is what makes the next run a RETAKE: still playable, still graded,
   * still stamped - but the host has already paid the day's XP for it and the
   * day's record keeps the FIRST grade.
   * @returns {?{grade:string, zen:boolean}}
   */
  function todaysRecord(gameKey) {
    if (results[gameKey]) return results[gameKey];
    try {
      const day = store.day(localDate);
      const row = day && day.classes ? day.classes[gameKey] : null;
      return (row && row.grade) ? row : null;
    } catch (e) { return null; }
  }

  function clearScreen() {
    if (!dom || !dom.screen) return;
    dom.screen.textContent = '';
  }

  /* ---------------------- top bar --------------------------------------- */
  function renderTopbar() {
    if (!dom || !dom.topbar) return;
    const bar = dom.topbar;
    bar.hidden = false;
    bar.textContent = '';
    bar.appendChild(el('span', 'arc-title', t('arcademy', 'The Arcademy')));
    bar.appendChild(el('span', 'chip year', tierLabel(maxTier())));

    const streak = store.streak();
    const flame = el('span', 'chip flame');
    flame.appendChild(el('span', null, '🔥 ' + t('attendance', 'Attendance') + ' '));
    flame.appendChild(el('b', null, String(streak.count | 0)));
    bar.appendChild(flame);
    if (streak.perfectDays) {
      bar.appendChild(el('span', 'chip', t('perfect_attendance', 'Perfect Attendance')
        + ' x' + (streak.perfectDays | 0)));
    }

    bar.appendChild(el('span', 'arc-spacer'));
    if (suspendedGlobally) bar.appendChild(el('span', 'chip warn', t('class_suspended', 'Class Suspended')));

    const gear = el('button', 'btn ghost', t('settings', 'Settings'));
    gear.type = 'button';
    gear.addEventListener('click', () => showSettings());
    bar.appendChild(gear);
  }

  /* ============================ SCREEN: BOARD =========================== */
  function showBoard(opts) {
    screen = 'board';
    teardownClass();
    clearScreen();
    renderTopbar();
    if (!dom || !dom.screen) return;

    const panel = el('div', 'arc-panel');
    panel.appendChild(el('p', 'arc-kicker', t('timetable', 'Timetable')));
    panel.appendChild(el('h1', 'arc-h1', t('arcademy', 'The Arcademy')));
    if (timetable.banner) panel.appendChild(el('p', 'arc-lede', timetable.banner));

    if (src.audioOnlySession) {
      // The host gate should have refused the launch (BUILD-CONTRACT §3); this is
      // the belt-and-braces path so a stale page can never start a visual class.
      panel.appendChild(el('p', 'arc-lede',
        'The Arcademy is closed during an audio-only session. Your attendance is safe.'));
      dom.screen.appendChild(panel);
      return;
    }

    const rows = timetable.classes.map((c) => {
      const entry = games.byKey[c.gameKey];
      const record = todaysRecord(c.gameKey);
      const done = !!record;
      const suspended = c.missing || !entry || !entry.ok;
      const chips = [];
      if (c.homeroom) chips.push({ text: t('homeroom', 'Homeroom'), kind: 'homeroom' });
      chips.push({ text: t('family_' + c.family, c.family) });
      chips.push({ text: c.timeBudgetSec + 's', kind: 'num' });
      if (suspended) chips.push({ text: t('class_suspended', 'Class Suspended'), kind: 'warn' });
      if (done) {
        chips.push({ text: t('grade', 'Grade') + ' ' + String(record.grade).toUpperCase() });
        // The row stays CLICKABLE on purpose - a graded class is replayable, it
        // just pays nothing (the host's per-UTC-day XP ledger) and keeps the
        // grade it already earned. The note is the whole warning.
        chips.push({ text: t('retake', 'Retake') });
      }
      return {
        id: c.gameKey,
        time: c.timeLabel,
        label: gameName(c.gameKey),
        chips,
        done,
        disabled: suspendedGlobally,
        ariaLabel: c.timeLabel + ' ' + gameName(c.gameKey),
      };
    });

    board = createBoard({
      rows,
      reducedMotion,
      animate: !(opts && opts.silent),
      onSelect: (gameKey) => {
        const cls = timetable.classes.find((c) => c.gameKey === gameKey);
        if (cls) startClass(cls);
      },
    });
    panel.appendChild(board.root);

    const bar = el('div', 'arc-classbar');
    const replay = el('button', 'btn ghost', t('replay_board', 'Flip the board again'));
    replay.type = 'button';
    replay.addEventListener('click', () => board && board.replay());
    bar.appendChild(replay);
    if (allDone()) {
      const rc = el('button', 'btn', t('report_card', 'Report Card'));
      rc.type = 'button';
      rc.addEventListener('click', () => showReport());
      bar.appendChild(rc);
    }
    panel.appendChild(bar);

    /* yesterday's strip (the mockup's report-card row) */
    const y = store.day(dayAdd(localDate, -1));
    const yClasses = y && y.classes ? Object.keys(y.classes) : [];
    if (yClasses.length) {
      const strip = el('div', 'reportcard');
      strip.appendChild(el('span', 'rlabel', 'Yesterday'));
      for (const key of yClasses.slice(0, 4)) {
        const r = y.classes[key] || {};
        const g = String(r.grade || '').toLowerCase();
        const cell = el('span', 'rcell');
        cell.appendChild(el('span', 'grade ' + (g === 'pass' ? 'pass' : g || 'none'),
          r.grade ? String(r.grade).toUpperCase() : '--'));
        cell.appendChild(el('span', null, gameName(key)));
        strip.appendChild(cell);
      }
      panel.appendChild(strip);
    }

    if (games.failed.length) {
      panel.appendChild(el('p', 'arc-note',
        games.failed.length + ' class(es) could not load and show as '
        + t('class_suspended', 'Class Suspended') + '.'));
    }

    dom.screen.appendChild(panel);
  }

  /* ============================ SCREEN: SETTINGS ======================== */
  function showSettings() {
    if (active) pauseClass(true);
    screen = 'settings';
    clearScreen();
    renderTopbar();
    settingsPage = createSettingsPage({
      init: src,
      bridge,
      games: games.list,
      keybinds,
      log: say,
      onClose: () => (active ? showClassScreen() : showBoard()),
    });
    dom.screen.appendChild(settingsPage.root);
  }

  /* ============================ SCREEN: REPORT ========================== */
  function showReport() {
    screen = 'report';
    clearScreen();
    renderTopbar();
    reportCard.render({
      timetable,
      results,
      shares,
      streak: store.streak(),
      perfect: allDone(),
      tier: maxTier(),
      title: t('report_card', 'Report Card'),
      dateLabel: localDate,
      onDone: () => showBoard(),
    });
    dom.screen.appendChild(reportCard.root);
  }

  /* ============================ THE CLASS RUNNER ======================== */
  function classScreenChrome(cls, gradeTier, retake) {
    const panel = el('div', 'arc-panel');
    const bar = el('div', 'arc-classbar');
    const back = el('button', 'btn ghost', t('leave_class', 'Leave class'));
    back.type = 'button';
    back.addEventListener('click', () => showBoard());
    bar.appendChild(back);
    bar.appendChild(el('span', 'arc-title', gameName(cls.gameKey)));
    bar.appendChild(el('span', 'chip year', tierLabel(gradeTier)));
    bar.appendChild(el('span', 'chip', t('family_' + cls.family, cls.family)));
    if (retake) bar.appendChild(el('span', 'chip', t('retake', 'Retake')));
    bar.appendChild(el('span', 'arc-spacer'));
    const clock = el('span', 'chip num', cls.timeBudgetSec + 's');
    bar.appendChild(clock);
    panel.appendChild(bar);

    const root = el('div', 'arc-classroot');
    panel.appendChild(root);
    return { panel, root, clock };
  }

  /** Re-show the running class's DOM (returning from settings). */
  function showClassScreen() {
    if (!active) { showBoard(); return; }
    screen = 'class';
    clearScreen();
    renderTopbar();
    dom.screen.appendChild(active.panel);
    pauseClass(false);
  }

  /** The allowlisted, lifecycle-free engine handle one class gets. */
  function engineHandleFor(engine, manifest, gameKey) {
    const allowed = new Set(Array.isArray(manifest && manifest.effectsConsumed)
      ? manifest.effectsConsumed : []);
    const warned = new Set();
    const refuse = (verb, kind) => {
      const id = verb + ':' + kind;
      if (!warned.has(id)) {
        warned.add(id);
        say('engine: ' + gameKey + ' called ' + verb + '("' + kind
          + '") outside its manifest - refused');
      }
      return false;
    };
    const guarded = (verb, fn) => (kind, opts) => {
      if (!allowed.has(kind)) return refuse(verb, kind);
      try { return fn(kind, opts); }
      catch (e) { say('engine.' + verb + '(' + kind + ') threw: ' + ((e && e.message) || e)); return false; }
    };
    const safe = (name) => (...args) => {
      try { return engine[name] ? engine[name].apply(engine, args) : undefined; }
      catch (e) { say('engine.' + name + ' threw: ' + ((e && e.message) || e)); return undefined; }
    };
    return {
      setHeat: safe('setHeat'),
      fire: guarded('fire', (k, o) => engine.fire(k, o)),
      sustain: guarded('sustain', (k, o) => engine.sustain(k, o)),
      stop: safe('stop'),
      setpiece: safe('setpiece'),
      beat: safe('beat'),
      ceremony: safe('ceremony'),

      /* The engine's additive helpers (engine/index.js header). None of them is
       * kind-addressed, so the manifest allowlist has nothing to say about them -
       * they only READ the clamped state or advance a director the class already
       * drives through beat()/setpiece(). Exposed for consistency: every game
       * currently reimplements a local fallback for the ones it wants, and the
       * next one should not have to.
       *   setPhase(phaseKeyOrIndex)      eligiblePhases + anti-clump bookkeeping
       *   armTail(on)                    arm forceTail for the class's last beat
       *   rewardRoll(opts) -> outcome    the variable-ratio canon (schedule.js)
       *   isPlainBeat(i01, floor, early) rolls the plain-share on the seeded rng
       *   plainShare(i01, floor, early)  the .80 -> .30 ramp itself
       *   cadenceMs(kind) -> ms          heat-scaled cadence (Infinity = silent)
       *   channels() -> {…}              the CLAMPED channel vector (a copy)
       *   diagnostics() -> {…}           live node counts, setpieces, refusals
       * A null engine answers undefined for all of them, which is why every game
       * keeps its own fallback: presence is not a promise of an effect. */
      setPhase: safe('setPhase'),
      armTail: safe('armTail'),
      rewardRoll: safe('rewardRoll'),
      isPlainBeat: safe('isPlainBeat'),
      plainShare: safe('plainShare'),
      cadenceMs: safe('cadenceMs'),
      channels: safe('channels'),
      diagnostics: safe('diagnostics'),
      // suspend()/dispose() are deliberately absent: the shell owns lifecycle.
    };
  }

  function startClass(cls) {
    if (suspendedGlobally) { shout(t('class_suspended', 'Class Suspended')); return; }
    if (active) teardownClass();

    const entry = games.byKey[cls.gameKey]
      || { key: cls.gameKey, ok: false, mod: suspendedStub(cls.gameKey, 'not in this build') };
    const mod = entry.mod;
    const manifest = (mod && mod.manifest) || {};
    const gameMeta = store.gameMeta(cls.gameKey);
    const gradeTier = tierFor(gameMeta);
    const timeBudgetSec = Math.min(
      cls.timeBudgetSec || QUICK_MAX_SEC,
      cls.meaty ? MEATY_MAX_SEC : QUICK_MAX_SEC
    );
    const seed = utcDateSeed + '|' + cls.gameKey + '|t' + gradeTier;
    // RETAKE: this class already has today's row. The seed is unchanged on
    // purpose (the day's script IS the day's script), so a game with an
    // identical-script replay needs nothing from this flag; it is here so a
    // game that wants to dress a replay differently can, and so the chrome can
    // say what is happening.
    const retake = !!todaysRecord(cls.gameKey);

    screen = 'class';
    clearScreen();
    renderTopbar();
    const chrome = classScreenChrome(Object.assign({}, cls, { timeBudgetSec }), gradeTier, retake);

    /* --- engine for this class --- */
    let engine;
    try {
      engine = createEngine({
        mount: (dom && dom.fx) || chrome.root,
        caps: src.caps || {},
        masterIntensity: src.masterIntensity == null ? 1 : src.masterIntensity,
        effectIntensity: src.effectIntensity == null ? 0.85 : src.effectIntensity,
        rng: makeRng(seed + '|engine'),
        // The DAY pool, not init.words: a word absorbed by an earlier class this
        // session is in here (ctx.absorb), and createEngine copies at construction.
        words: dayWords.slice(),
        assets,
        motionLevel: src.motionLevel == null ? 2 : src.motionLevel,
        reducedMotion,
        // No extra bus: the engine already emits its CustomEvents on `document`,
        // and boot.js listens there. Passing window as well would double every
        // arcademy-log line (engine/index.js emit() fires document AND bus).
        bus: null,
      }) || NULL_ENGINE_FACTORY();
    } catch (e) {
      say('createEngine threw (' + ((e && e.message) || e) + ') - class runs undistracted');
      engine = NULL_ENGINE_FACTORY();
    }

    /* --- shared verbs --- */
    keybinds.declare(cls.gameKey, manifest.keybinds);
    const keys = keybinds.runtime(cls.gameKey, window);
    const peek = createPeek({ log: say });
    const classCeremonies = createCeremonies({
      engine, layer: (dom && dom.ceremony) || null, reducedMotion, log: say,
    });

    /* --- per-game settings view (never a global) --- */
    const settingsView = (settingsPage
      ? settingsPage.gameSettingsFor(cls.gameKey, manifest)
      : gameSettingsFallback(cls.gameKey, manifest));

    /* --- below-par board detection (SYNTHESIS #7, shell-computed) --- */
    let belowPar = false;
    const bs = manifest.boardSizes;
    if (bs && bs.par && settingsView.boardSize != null) {
      const par = Number(bs.par[gradeTier]);
      const chosen = Number(settingsView.boardSize);
      if (Number.isFinite(par) && Number.isFinite(chosen) && chosen < par) belowPar = true;
    }

    let ended = false;

    const ctx = {
      root: chrome.root,
      engine: engineHandleFor(engine, manifest, cls.gameKey),
      assets,
      lexicon: t,
      caps: src.caps || {},
      rng: makeRng(seed),
      settings: settingsView,
      keys,
      peek,
      ceremonies: classCeremonies,
      store,

      /* ---- additive read-only projection (all from init) ----------------
       * A class that needs to know the shape of the machine it is running on
       * should not have to re-probe the browser for it: the host already told
       * us, and its answer outranks a media query (`performanceMode` and the
       * app's own motion policy have no CSS equivalent). */
      platform: src.platform || { isTouch: false, hasHaptics: false, host: 'desktop' },
      motion: {
        reducedMotion,
        motionLevel: src.motionLevel == null ? 2 : src.motionLevel,
      },
      // Resolved SubAudioAudible: FALSE means a cue will be MIXED but inaudible,
      // so a class that leans on audio has to carry a visual tell instead.
      audioAudible: !!src.audioAudible,

      /* The day's word pool, and the sink that adds to it. `words` is a COPY
       * (a game may not splice the shared array); `absorb` is the only way in
       * and is SESSION-ONLY - never persisted, never sent to the host, and
       * never a write into SubliminalPool. A word taken here reaches LATER
       * classes because their engines are built from the same day pool. */
      words: dayWords.slice(),
      absorb: absorbWord,
      sessionWords,

      log: (m) => say('[' + cls.gameKey + '] ' + m),
      endClass: (result) => {
        if (ended) { say('[' + cls.gameKey + '] endClass called twice - ignored'); return; }
        ended = true;
        finishClass(cls, gradeTier, result, { peek, belowPar });
      },
    };

    let instance = null;
    try {
      instance = mod.create(ctx);
      if (!instance || typeof instance.start !== 'function') throw new Error('create() returned no start()');
    } catch (e) {
      say('game ' + cls.gameKey + ' create() failed: ' + ((e && e.message) || e));
      instance = suspendedStub(cls.gameKey, 'create failed').create(ctx);
    }

    active = {
      cls, gradeTier, instance, engine, keys, peek, ceremonies: classCeremonies,
      panel: chrome.panel, root: chrome.root, paused: false, pauseEl: null,
      suspendEl: null, timeBudgetSec,
    };

    dom.screen.appendChild(chrome.panel);
    bridge.send({ type: 'class-started', gameKey: cls.gameKey, gradeTier });

    try {
      instance.start({ gradeTier, seed, timeBudgetSec, retake });
    } catch (e) {
      say('game ' + cls.gameKey + ' start() threw: ' + ((e && e.message) || e));
      showSuspendedOverlay('This class could not start. Your attendance is safe.');
    }
    if (suspendedGlobally) applySuspend(true, 'video');
  }

  /** Fallback per-game settings when the settings page has not been built yet. */
  function gameSettingsFallback(gameKey, manifest) {
    const bag = (src.settings && typeof src.settings === 'object') ? src.settings : {};
    const out = {};
    const pick = (key, dflt) => (
      Object.prototype.hasOwnProperty.call(bag, key) ? bag[key] : dflt
    );
    for (const s of (Array.isArray(manifest.settings) ? manifest.settings : [])) {
      if (s && s.key) out[s.key] = pick(s.key, s.default);
    }
    if (manifest.boardSizes && Array.isArray(manifest.boardSizes.values) && manifest.boardSizes.values.length) {
      const bk = boardSizeKey(gameKey);
      out[bk] = pick(bk, manifest.boardSizes.values[0]);
      out.boardSize = out[bk];
    }
    return out;
  }

  /* ---------------------- end of class ---------------------------------- */
  function finishClass(cls, gradeTier, result, shellState) {
    const r = result || {};
    const assists = Object.assign({}, r.assists || {});
    if (shellState.peek && shellState.peek.used) assists.peek = true;
    if (shellState.belowPar) assists.below_par_board = true;

    const input = {
      metrics: r.metrics || {},
      hardGates: r.hardGates,
      zen: !!r.zen,
      assists,
    };
    const graded = gradeClass(input);
    const flavorXp = Math.max(0, Math.min(FLAVOR_XP_CAP, Math.round(Number(r.flavorXp) || 0)));

    // THE FIRST GRADE OF THE DAY IS THE ONE THAT COUNTS. A retake is a free
    // replay: it re-runs, re-stamps and re-shares, but it does not overwrite the
    // row - otherwise a bad second attempt would erase an S, and the host has
    // already paid the day's XP for the first (payout-result carries retake:true
    // and xp 0, which is what the report card's XP line then shows).
    const priorRow = todaysRecord(cls.gameKey);
    const isRetake = !!priorRow;

    if (!isRetake) {
      results[cls.gameKey] = {
        grade: graded.grade,
        zen: graded.zen,
        composite: graded.composite,
        capped: capsRaised(input),
        tier: gradeTier,
        xp: null,                   // filled by payout-result; C# owns the table
      };
    } else {
      // Keep the recorded grade; note the replay so the card can explain the 0.
      const keep = results[cls.gameKey] || {
        grade: priorRow.grade, zen: !!priorRow.zen, composite: 0, capped: [], tier: gradeTier,
      };
      keep.retake = true;
      keep.xp = null;
      results[cls.gameKey] = keep;
    }
    // The share card always reflects what you just played - it is a transcript,
    // not a record, and a retake you are proud of is the one you want to paste.
    if (r.share && typeof r.share === 'object') shares[cls.gameKey] = r.share;

    say('class ended: ' + cls.gameKey + ' tier ' + gradeTier + ' -> ' + graded.grade
      + ' (composite ' + graded.composite.toFixed(3)
      + (graded.capped.length ? ', capped: ' + graded.capped.join(',') : '')
      + (isRetake ? ', RETAKE - the day keeps ' + String(priorRow.grade).toUpperCase() : '') + ')');

    bridge.send({
      type: 'class-ended',
      gameKey: cls.gameKey,
      gradeTier,
      grade: graded.grade,
      zen: graded.zen,
      flavorXp,
      dayUtc: utcDateSeed,
    });

    /* meta writes: per-game progression, the day's row, the streak */
    try {
      // Retakes advance nothing: a free replay that still fed `promotions`
      // would let grinding replays climb tiers (and tomorrow's XP base) for
      // free. The first attempt of the day is the only one that progresses.
      if (!isRetake) {
        const adv = advance(store.gameMeta(cls.gameKey), graded.grade);
        store.mergeGameMeta(cls.gameKey, {
          tier: adv.tier, promotions: adv.promotions, played: adv.played,
          best: adv.best, lastGrade: adv.lastGrade,
        });
        if (adv.promoted) shout(tierLabel(adv.tier) + ' unlocked');
      }
      // Not on a retake: the day's row is written once, by the first attempt.
      if (!isRetake) {
        store.recordClass(localDate, cls.gameKey, {
          grade: graded.grade, zen: graded.zen, at: Date.now(),
        });
      }
      // ATTENDANCE IS NOT WRITTEN HERE. ArcademyMetaStore mints the streak and
      // perfect-attendance count from this very `class-ended` frame (so a stale
      // page cannot forge one) and ships the numbers back on `payout-result`;
      // store.applyPayout folds them in. Writing them locally would be refused
      // by the host anyway - see core/store.js HOST_OWNED_KEYS.
      if (allDone()) {
        store.completeDay(localDate, { classCount: timetable.classes.length, perfect: true });
      }
    } catch (e) {
      say('meta write failed (screen unaffected): ' + ((e && e.message) || e));
    }

    teardownClass();
    showReport();
  }

  /* ---------------------- pause / suspend / teardown -------------------- */
  function pauseClass(on) {
    if (!active) return;
    active.paused = !!on;
    try { on ? active.instance.pause() : active.instance.resume(); }
    catch (e) { say('game pause/resume threw: ' + ((e && e.message) || e)); }
    if (on) {
      try { active.peek.forceHide(); } catch (e) { /* noop */ }
      if (!active.pauseEl) {
        const overlay = el('div', 'arc-suspended');
        overlay.appendChild(el('h2', 'arc-h2', 'Paused'));
        const bar = el('div', 'arc-classbar');
        const resume = el('button', 'btn primary', 'Resume');
        resume.type = 'button';
        resume.addEventListener('click', () => pauseClass(false));
        const leave = el('button', 'btn ghost', t('leave_class', 'Leave class'));
        leave.type = 'button';
        leave.addEventListener('click', () => showBoard());
        bar.appendChild(resume); bar.appendChild(leave);
        overlay.appendChild(bar);
        overlay.appendChild(el('p', 'arc-note', 'Hold Esc to leave the Arcademy.'));
        active.root.appendChild(overlay);
        active.pauseEl = overlay;
      }
    } else if (active.pauseEl) {
      active.pauseEl.remove();
      active.pauseEl = null;
    }
  }

  function showSuspendedOverlay(note, opts) {
    if (!active || active.suspendEl) return;
    const overlay = el('div', 'arc-suspended');
    overlay.appendChild(el('h2', 'arc-h2', t('class_suspended', 'Class Suspended')));
    if (note) overlay.appendChild(el('p', 'arc-note', note));
    const bar = el('div', 'arc-classbar');
    // THE PANIC RUNG'S WAY BACK. A video suspend un-suspends itself when the video
    // ends, and an audio-only suspend when the session does - but a PANIC suspend
    // has no natural end, so the page has to ask for it. The host owns the answer
    // (it replies with suspend:false, reason 'panic'), which keeps the one law that
    // the host is the only thing that may un-freeze a class.
    if (opts && opts.resumable) {
      const resume = el('button', 'btn primary', 'Resume');   // literal, like pauseClass's
      resume.type = 'button';
      resume.addEventListener('click', () => {
        try { bridge.send({ type: 'resume-request', reason: 'panic' }); }
        catch (e) { say('resume-request send failed: ' + ((e && e.message) || e)); }
      });
      bar.appendChild(resume);
    }
    const leave = el('button', 'btn ghost', t('leave_class', 'Leave class'));
    leave.type = 'button';
    leave.addEventListener('click', () => showBoard());
    bar.appendChild(leave);
    overlay.appendChild(bar);
    active.root.appendChild(overlay);
    active.suspendEl = overlay;
  }

  /**
   * The host says everything must stop NOW (mandatory video, audio-only session
   * starting mid-class, panic). Freeze the class, drop every effect, and show the
   * class_suspended treatment. Attendance for the day is preserved either way -
   * the streak has already been rolled or will be when a class actually ends.
   */
  function applySuspend(on, reason) {
    suspendedGlobally = !!on;
    if (active) {
      try { active.engine.suspend(!!on); } catch (e) { say('engine.suspend threw: ' + ((e && e.message) || e)); }
      try { active.peek.forceHide(); } catch (e) { /* noop */ }
      try { active.instance.suspend(!!on); } catch (e) { say('game.suspend threw: ' + ((e && e.message) || e)); }
      if (on) {
        pauseClass(true);
        showSuspendedOverlay(reason === 'audio-only'
          ? 'An audio-only session started. Your attendance is safe.'
          : reason === 'panic' ? 'Stopped. Press the panic key again to leave.' : 'Paused for a video.',
          { resumable: reason === 'panic' });
      } else if (active.suspendEl) {
        active.suspendEl.remove();
        active.suspendEl = null;
      }
    }
    renderTopbar();
    if (screen === 'board') showBoard({ silent: true });
  }

  function teardownClass() {
    if (!active) return;
    const a = active;
    active = null;
    // TELL THE HOST THE CLASS IS OVER. `class-started` has a closing bracket now:
    // without it the host's `_classActive` stayed true for the rest of the session
    // once you left a class with Esc (only `class-ended` ever cleared it), which
    // tightened the heartbeat watchdog to its mid-class 12s limit forever and made
    // the log lie about where the page was. Idempotent host-side, and deliberately
    // sent from the ONE funnel every leave path already goes through - including
    // the finished-class path, where `class-ended` has just cleared it anyway.
    try { bridge.send({ type: 'class-left', gameKey: a.cls && a.cls.gameKey }); }
    catch (e) { say('class-left send failed: ' + ((e && e.message) || e)); }
    try { a.instance.destroy(); } catch (e) { say('game destroy threw: ' + ((e && e.message) || e)); }
    try { a.peek.destroy(); } catch (e) { /* noop */ }
    try { a.keys.destroy(); } catch (e) { /* noop */ }
    try { a.ceremonies.destroy(); } catch (e) { /* noop */ }
    try { a.engine.dispose(); } catch (e) { say('engine dispose threw: ' + ((e && e.message) || e)); }
    try { a.panel.remove(); } catch (e) { /* noop */ }
    if (dom && dom.fx) dom.fx.textContent = '';
  }

  /* ---------------------- palette --------------------------------------- */
  function applyPalette(palette, sayFn) {
    if (!palette || typeof palette !== 'object' || !document.documentElement) return;
    const style = document.documentElement.style;
    const unknown = [];
    for (const key of Object.keys(palette)) {
      const token = PALETTE_TOKENS[key];
      const value = palette[key];
      if (!token) { unknown.push(key); continue; }
      if (typeof value === 'string' && /^#[0-9a-fA-F]{3,8}$/.test(value.trim())) {
        style.setProperty(token, value.trim());
      }
    }
    if (unknown.length) sayFn('palette: ignored unknown keys ' + unknown.join(','));
  }

  /* ---------------------- host frames ----------------------------------- */
  const api = {
    /** {type:'setting'} post-clamp echo. THE only path that moves a setting. */
    onSetting(m) {
      if (!m || typeof m.key !== 'string') return;
      // Keep the flat bag current so a class started later sees the new value.
      // Only per-game keys live in the bag; a global echo would shadow one.
      if (src.settings && typeof src.settings === 'object' && !isGlobalSettingKey(m.key)) {
        src.settings[m.key] = m.value;
      }
      if (settingsPage) { settingsPage.noteEcho(m.key, m.value); settingsPage.applyEcho(m.key, m.value); }
      if (m.key === SETTING_KEYS.keybinds) keybinds.applyEcho(m.value);
    },

    /** {type:'payout-result'} - the ONLY source of an XP number on this page. */
    onPayout(m) {
      if (!m || !m.gameKey) return;
      const r = results[m.gameKey];
      if (r) { r.xp = m.xp; r.levelUp = !!m.levelUp; }
      // The same frame carries the host's authoritative attendance figures.
      try { store.applyPayout(m); } catch (e) { say('applyPayout: ' + ((e && e.message) || e)); }
      if (m.levelUp) shout('Level up');
      renderTopbar();
      if (screen === 'report') showReport();
    },

    /** {type:'suspend'} */
    onSuspend(m) { applySuspend(!!(m && m.on), m && m.reason); },

    /** {type:'meta'} is consumed by the store; this just repaints the chrome. */
    onMeta() { renderTopbar(); if (screen === 'board') showBoard({ silent: true }); },

    /**
     * One rung of the Esc ladder. Returns true when the shell consumed it, so
     * boot.js only reaches for fullscreen/exit once the page has nothing to close.
     */
    escapeStep() {
      if (screen === 'settings') {
        if (settingsPage) { try { settingsPage.destroy(); } catch (e) { /* noop */ } settingsPage = null; }
        if (active) showClassScreen(); else showBoard();
        return true;
      }
      // A host suspend owns the screen. The panic ladder's second press (host
      // side) is the exit, and walking to the board here would destroy the
      // Resume card ~60ms after it appeared (both ladders fire on one Esc -
      // verified live 3/3). Consume the press and change nothing; the overlay's
      // own Resume / Leave class buttons remain the page-side way out.
      if (active && active.suspendEl) return true;
      if (active && !active.paused) { pauseClass(true); return true; }
      if (active && active.paused) { showBoard(); return true; }
      return false;
    },

    get screen() { return screen; },
    get inClass() { return !!active; },

    /** Read-only provider diagnostics (local/remote pool sizes, placeholderFloor).
     *  The one window onto the asset seam: `placeholderFloor:true` means the host
     *  shipped no `settings.localAssets` and every draw is a bundled tile. */
    assetStats() { try { return assets.stats(); } catch (e) { return null; } },

    destroy() {
      if (destroyed) return;
      destroyed = true;
      teardownClass();
      try { ceremonies.destroy(); } catch (e) { /* noop */ }
      if (settingsPage) { try { settingsPage.destroy(); } catch (e) { /* noop */ } }
    },
  };

  store.onChange(() => { if (screen === 'board' || screen === 'report') renderTopbar(); });

  showBoard();
  return api;
}

export default createShell;
