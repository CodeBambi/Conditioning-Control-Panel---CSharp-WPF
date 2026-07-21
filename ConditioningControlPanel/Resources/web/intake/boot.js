/* ============================================================================
 * boot.js — entry point for the "Graded Intake" page (mirrors dtrh/boot.js).
 *
 * Boot order: wire shim handlers -> announceReady() -> shim resolves a
 * BootConfig (host `init` when hosted; URL+localStorage when standalone) ->
 * load bank + resolve theme -> build ai / reward / engine / render -> CLINICAL
 * BRIEFING intro -> run the beat loop (band interstitials, in-fiction HUD,
 * interviewer asides, Climax glitch) -> emitResult -> OUTRO CEREMONY.
 *
 * GRACEFUL SEAMS. Phase-1 modules (render/beats.js, render/effects.js,
 * render/audio.js, render/background.js, core/reward.js, core/stats.js) may not
 * exist yet. boot.js dynamically imports each and falls back to a null-object or
 * a tiny inline stub renderer, so Phase 0 runs end-to-end today and each agent's
 * file slots in with ZERO boot changes when it lands. Every OPTIONAL surface
 * (Background reactive API, BeatMeta fields, theme blocks) is feature-detected —
 * an older engine/background still runs, just without the dressing. This is the
 * integration contract Agent I copies on the C# side.
 *
 * IMPORT SAFETY: nothing here may throw at module import time — all DOM/window
 * access is guarded so `node -e "import('./boot.js')"` succeeds headless.
 * ==========================================================================*/

import * as shim from './web-shim.js';
import { createAI } from './core/ai.js';
import { createEngine } from './core/engine.js';
import { PRODUCT_NAME, Band, BAND_ORDER, themeOf, clamp01, bandIndex } from './core/contracts.js';
import { resetPalette, noteColorPick, deepen, COLOR_TAG, harvestedColors, COLOR_SWATCHES } from './core/palette.js';
import { resetSpiralLog, recordedSpirals, spiralCount } from './core/spiralLog.js';
import { resetMediaLog, recordedMedia, mediaShownCount } from './core/mediaLog.js';

/* ----------------------------------------------------------------------------
 * DOM handles — guarded so importing this module in node (no `document`) is
 * safe. Every consumer already null-checks its element.
 * -------------------------------------------------------------------------- */
const doc = (typeof document !== 'undefined') ? document : null;
const dom = {
  stage:   doc && doc.getElementById('intake-stage'),
  loader:  doc && doc.getElementById('intake-loader'),
  hud:     doc && doc.getElementById('intake-hud'),
  bg:      doc && doc.getElementById('intake-bg'),
  title:   doc && doc.getElementById('intake-title'),
  overlay: doc && doc.getElementById('intake-overlay'),
  aside:   doc && doc.getElementById('intake-aside'),
};
if (dom.title) dom.title.textContent = PRODUCT_NAME;

// Uncaught errors go to the host log — no devtools in the hosted page.
if (typeof window !== 'undefined') {
  window.addEventListener('error', (e) => {
    const src = e.filename ? ` @ ${String(e.filename).split('/').pop()}:${e.lineno}` : '';
    shim.log('error: ' + (e.message || 'script error') + src);
  });
  window.addEventListener('unhandledrejection', (e) => {
    const r = e.reason;
    shim.log('promise: ' + ((r && (r.message || r.stack || r)) || 'unknown'));
  });
  // Render-layer diagnostics (effects.js emits these instead of holding the shim).
  window.addEventListener('intake-log', (e) => {
    try { shim.log('[fx] ' + (e && e.detail && e.detail.msg || '?')); } catch (_e) { /* never fatal */ }
  });
}

// Optional-module loader: import a module + factory, or fall back to a stub.
async function loadOptional(path, factoryName, makeFallback) {
  try {
    const mod = await import(path);
    const f = mod && mod[factoryName];
    if (typeof f === 'function') return f;
    shim.log(`module ${path}: no '${factoryName}' export — using fallback`);
  } catch (e) {
    // A real import failure (bad path / throw at import / missing dep) silently
    // fell back to a no-op stub before — e.g. a dead background = a flat void with
    // no trace. Record it so the seam is never invisible again.
    shim.log(`module ${path}: import failed (${(e && e.message) || e}) — using fallback`);
  }
  return makeFallback;
}

// Fetch a niche's prompt bank. Returns null on any failure (engine placeholder
// takes over) so a missing/malformed bank degrades gracefully instead of wedging.
async function loadBank(niche) {
  try {
    const res = await fetch(`./banks/${niche}.json`, { cache: 'no-cache' });
    if (!res.ok) { shim.log(`bank ${niche}: HTTP ${res.status} — using placeholder`); return null; }
    const bank = await res.json();
    if (!bank || !Array.isArray(bank.prompts) || !bank.prompts.length) {
      shim.log(`bank ${niche}: empty/invalid — using placeholder`); return null;
    }
    shim.log(`bank ${niche}: ${bank.prompts.length} prompts, ${(bank.archetypes||[]).length} archetypes`);
    return bank;
  } catch (e) {
    shim.log(`bank ${niche}: load failed (${e && e.message || e}) — using placeholder`);
    return null;
  }
}

shim.startHeartbeat();

/** Chance, per card resolution, to sprinkle a Bambi-Sparkle giggle cue. */
const GIGGLE_CHANCE = 0.03;

/**
 * Deliberate breather (ms) held on the normal answer -> next-card swap, so a
 * player can't speedrun card-to-card by answering instantly. The just-resolved
 * card is still on the stage playing its band EXIT animation (beats.finalize
 * adds the exit class right after it resolves), so this reads as that animation
 * breathing out before the next card mounts — pacing, not lag. Applied ONLY to
 * the active-answer fast path (see the gate below); ceremony-heavy paths are
 * excluded so nothing double-dips.
 */
const CARD_SWAP_EXTRA_MS = 1000;

let running = false;

shim.onBoot(async (config) => {
  if (running) return;
  running = true;
  if (!doc) { shim.log('boot: no DOM — shell inert (import-safety mode)'); return; }
  shim.log(`boot: niche=${config.niche} hosted=${config.hosted} endless=${config.endless}`);

  // Declared out here so the recovery/outro paths (and the catch) can dispose it.
  let subliminals = null;

  try {
    // --- build the stack (real module if present, else stub) --------------
    const ai = createAI(config.ai);

    const createReward = await loadOptional('./core/reward.js', 'createReward', stubReward);
    const createStats  = await loadOptional('./core/stats.js',  'createStats',  stubStats);
    const createBeats  = await loadOptional('./render/beats.js', 'createBeats',  null); // null -> inline stub render
    const createEffects= await loadOptional('./render/effects.js','createEffects', stubEffects);
    const createAudio  = await loadOptional('./render/audio.js', 'createAudio',  stubAudio);
    const createBg     = await loadOptional('./render/background.js','createBackground', stubBackground);
    const createSubs   = await loadOptional('./render/subliminals.js','createSubliminals', null);
    // COLOUR FLASH: the fullscreen tint pulse fired when a Calibration colour
    // question commits (core/palette.js). Optional like every render module —
    // a missing file just means no flash, never a broken run.
    const flashColor   = await loadOptional('./render/colorFlash.js', 'flashColor', () => false);

    const reward   = createReward({ config });
    const stats    = createStats();

    // Bank FIRST — the resolved theme feeds effects/beats and re-tints the page.
    const bank  = config.bank || await loadBank(config.niche);
    const theme = themeOf(bank);
    applyTheme(theme);

    // A real factory that throws (e.g. no DOM in a headless run) degrades to
    // its stub instead of killing the boot — same seam, one level deeper.
    const effects  = tryMake('effects', () => createEffects({ root: dom.stage, caps: config.caps, media: config.media || null, theme }), stubEffects);
    const audio    = tryMake('audio', () => createAudio({ caps: config.caps }), stubAudio);
    // `media` is optional and only feeds the tube's wall dressing (gif-plastered
    // wall sections); an older background that ignores it behaves exactly as before.
    const background = tryMake('background', () => createBg({ canvas: dom.bg, media: config.media || null }), stubBackground);
    safeCall(background, 'setEnabled', true);

    // Steering is optional too; beats.js pulls it in itself in the real build,
    // but we resolve it here so the stub render can use it symmetrically.
    const createSteering = await loadOptional('./render/steering.js', 'createSteering', stubSteering);
    const steering = tryMake('steering', () => createSteering({ caps: config.caps, media: config.media || null }), stubSteering);

    // The user's subliminal pool, replicated in-page (render/subliminals.js). SECOND-PHASE
    // layer: the bed belongs to the descent, NOT the honest warm-up — so it is NOT mounted
    // here. We resolve the pool now (empty pool -> no layer, ever) but defer construction to
    // the first non-Calibration beat below, so no subliminal text/audio (and no layer, timer
    // or AudioContext) exists while the run is still in Calibration. It starts from
    // Establishing onward and is retired again as Recovery opens.
    const subPool = Array.isArray(config.subliminals)
      ? config.subliminals.filter((s) => s && typeof s.text === 'string' && s.text.trim().length > 0) : [];
    subliminals = null; // mounted lazily in the beat loop once the run leaves Calibration

    // COLOUR HARVEST: wipe last run's picks BEFORE the engine is built — the
    // engine arms the harvest from the bank inside createEngine().
    resetPalette();
    // ...and the spirals those picks will weave, so the outro's recap gallery
    // only ever shows THIS run's spirals (core/spiralLog.js).
    resetSpiralLog();
    // ...and the payloads the run will pay out, so the archive's file for THIS
    // run lists only what THIS run actually showed (core/mediaLog.js).
    resetMediaLog();

    const engine = createEngine({ bank, reward, ai, config, stats });

    // Real render if Agent C delivered it; else the inline stub renderer.
    const stubBeats = () => ({ render: (beat) => stubRenderBeat(beat, { effects, audio, steering, reward, caps: config.caps }) });
    const beats = createBeats
      // micEnabled: the host's MicConsentGiven. Absent (standalone/harness) => true,
      // where the browser's own permission prompt is the gate and no WPF setting exists.
      ? tryMake('beats', () => createBeats({ root: dom.stage, effects, audio, steering, reward, caps: config.caps, background, theme, media: config.media || null, niche: config.niche, micEnabled: config.micEnabled !== false }), stubBeats)
      : stubBeats();

    // --- fiction identity + feed-forward ----------------------------------
    const subjectId = resolveSubjectId(config);
    let priorRun = config.priorRun || null;
    if (!priorRun && !config.hosted && stats.feedForward) {
      try { priorRun = await stats.feedForward(); } catch (_e) { /* best-effort */ }
    }

    if (dom.loader) dom.loader.hidden = true;

    // --- kawaii MAIN MENU (ui/menu.js) ------------------------------------
    // The storefront goes up the instant the loader drops and BEFORE buildHud /
    // the clinical briefing, so the run's HUD never sits behind the title screen
    // and the tonal handoff lands in the intended order: bubbly welcome, then
    // cold clinic. Optional module — if ui/menu.js is missing or throws, the
    // boot proceeds straight into the briefing exactly as it did before.
    //   'quit' -> hand the window back to the host WITHOUT emitting a result,
    //             so no session is drafted (shim.closeHost = `intake-close`).
    if (!config.m2Test) {                          // headless harness: zero pacing
      let menuChoice = 'begin';
      try {
        const menuMod = await import('./ui/menu.js');
        if (menuMod && typeof menuMod.showMainMenu === 'function') {
          // `stats` rides along so the menu can offer the Archive (ui/history.js)
          // once there is at least one completed run to consult. Optional field:
          // an older menu.js ignores it and shows exactly the buttons it always did.
          menuChoice = await menuMod.showMainMenu({ theme, config, audio, background, stats, bank });
        }
      } catch (e) {
        shim.log('main menu failed — entering briefing: ' + (e && e.message || e));
      }
      if (menuChoice === 'quit') {
        shim.log('main menu: quit — closing without a run');
        try { if (typeof shim.closeHost === 'function') shim.closeHost(); } catch (_e) {}
        return;
      }
    }

    buildHud(config.caps);

    // --- clinical briefing (skipped headless / in m2Test harness runs) ----
    const shellCtx = { config, theme, bank, background, subjectId, priorRun, audio };
    try { if (audio && typeof audio.sfx === 'function') audio.sfx('briefing-open'); } catch (_e) {} // run-start intake chime
    try { await runBriefing(shellCtx); } catch (e) { shim.log('briefing failed: ' + (e && e.message || e)); }

    // --- in-game pause menu (ui/pause.js) ---------------------------------
    // Installed AFTER the briefing and BEFORE the first beat, because its timing
    // shim only governs timers created after it is installed — which is exactly
    // the set that matters (every countdown beats.js arms per card). Optional
    // like every other render module; a missing file just means no pause.
    // Skipped in the m2Test harness: that path must stay zero-pacing/headless.
    let pause = null;
    if (!config.m2Test) {
      try {
        const pmod = await import('./ui/pause.js');
        if (pmod && typeof pmod.installPause === 'function') {
          pause = pmod.installPause({ audio, background, effects, caps: config.caps, config });
        }
      } catch (e) { shim.log('pause: install failed (' + (e && e.message || e) + ')'); }
    }

    // --- the canonical beat loop -----------------------------------------
    const seenBands = new Set([Band.Calibration]); // the briefing covers Section 1
    let step = await engine.next();
    while (!step.done) {
      const beat = step.beat;
      const meta = beat.meta || {}; // feature-detect: older engine = no meta

      if (meta.bandNew === true && !seenBands.has(beat.band)) {
        seenBands.add(beat.band);
        try { await showInterstitial(beat.band, shellCtx); }
        catch (e) { shim.log('interstitial failed: ' + (e && e.message || e)); }
      }

      // The subliminal bed belongs to the descent — retire it as the run winds down.
      if (beat.band === Band.Recovery && subliminals) { safeCall(subliminals, 'dispose'); subliminals = null; }

      // ...and it does NOT belong to the honest warm-up: mount it lazily the FIRST time the
      // run leaves Calibration (Establishing onward), so the first flash can only land once
      // the second phase begins. Idempotent (only builds once) and never re-armed after
      // Recovery has disposed it (guarded on band + the null check above).
      if (!subliminals && createSubs && subPool.length &&
          beat.band !== Band.Calibration && beat.band !== Band.Recovery) {
        subliminals = tryMake('subliminals', () => createSubs({ pool: subPool, caps: config.caps, theme }), null);
      }

      setDepthEverywhere(beat.depth, beat.band, { effects, audio, background, subliminals });
      // ...and tell the SHELL how deep we are. ui/pause.js forwards it to
      // ui/corruption.js, which rots the kawaii palette + the shell song from
      // band 3 onward. Cosmetic and optional: an older pause handle has no
      // setBand and simply never corrupts.
      if (pause && typeof pause.setBand === 'function') {
        try { pause.setBand(beat.band, beat.depth); } catch (_e) { /* never fatal */ }
      }
      // Living background: rotate to a fresh DtRH biome dress on every beat
      // (depth is set just above, so the pick is biased by it). Optional method —
      // safeCall no-ops on the stub/fallback background.
      safeCall(background, 'nextBiome');
      applyRouteTint(engine, bank, background);
      updateHud(beat, theme);
      if (meta.interviewerLine) {
        showAside(meta.interviewerLine);
        await sleep(config.m2Test ? 0 : 450); // never block input longer than ~600ms
      }

      const ev = await beats.render(beat);

      // COLOUR HARVEST + FLASH. beats.finalize() resolves the render promise the
      // instant the answer commits (the card's exit animation runs after), so
      // this lands ON the pick: the screen washes with a deep version of the
      // colour, over the exiting card and the swap breather below. Harvesting
      // BEFORE engine.next(ev) matters — the engine reads the harvest count to
      // decide whether the next Calibration beat is another colour question.
      harvestColorPick(beat, ev, flashColor, config.caps);

      step = await engine.next(ev);

      // CARD-SWAP BREATHER: hold ~1s on the normal answer -> next-card swap so the
      // outgoing card's EXIT animation (added in beats.finalize) plays out before
      // the next render clears the stage and mounts the next card — closes the
      // speedrun gap. Deliberately excluded from every path that already carries
      // its own pacing, so nothing double-dips:
      //   - !step.done          : the run is over -> the OUTRO owns its long pacing.
      // This is a GUARANTEED FLOOR: every beat -> beat transition holds, with no
      // per-path exclusions. The earlier version skipped the hold on timeouts /
      // melt-skips, interludes and band-new transitions, which left enough instant
      // swaps that the run still read as speedrunnable. The hold is unskippable by
      // construction — the outgoing card is already gone and the next one has not
      // mounted, so there is nothing on the stage to click through.
      //   - !step.done : the run is over -> the OUTRO owns its long pacing.
      //   - m2Test     : the headless harness runs with zero pacing.
      // (The jumpscare aborts the host, so the loop never continues past it; the
      //  freeze gate resolves as a normal ~20s ceremony where +1s is negligible.)
      if (!step.done) {
        await sleep(config.m2Test ? 0 : CARD_SWAP_EXTRA_MS);
        // PAUSE GATE. sleep() already freezes on its own (pause.js shims
        // setTimeout), but hold explicitly here too, so a resume can never land
        // between the breather ending and the next card mounting.
        if (pause) { try { await pause.gate(); } catch (_e) { /* never block the run */ } }
      }

      // Bambi-Sparkle sprinkle: a small chance after each card resolves to play
      // one of the BS-mode giggle clips (random variant, ~30% via manifest gain).
      // Skip once the run is over — no giggle bleeds into the outro / certificate.
      if (!step.done && Math.random() < GIGGLE_CHANCE) {
        try { if (audio && typeof audio.sfx === 'function') audio.sfx('giggle'); } catch (_e) {}
      }
    }

    // Run over: the pause menu belongs to the RUN, not the outro ceremony.
    if (pause) { try { pause.dispose(); } catch (_e) {} pause = null; }
    // Run over: retire the subliminal bed before the surface/outro (idempotent if already gone).
    safeCall(subliminals, 'dispose'); subliminals = null;
    teardownCounter();   // no held garble / falling clone may reach the outro

    // Recovery invariant: make sure we surface even if the engine ended abruptly.
    setDepthEverywhere(0, Band.Recovery, { effects, audio, background, subliminals });
    effects.recover && effects.recover(0);
    audio.emerge && audio.emerge();
    try { if (audio && typeof audio.sfx === 'function') audio.sfx('surface-bloom'); } catch (_e) {} // run-end emerge bloom

    const result = step.result;
    // The archive's copy of the closing card (ui/history.js reads this back).
    // Assembled HERE because boot is the only module that can see the resolved
    // bank/theme, the fiction identity and both run ledgers at once. Wrapped:
    // a failure to build the recap must never cost the run its stats record.
    let recapExtras = null;
    try { recapExtras = buildRecapExtras(result, shellCtx); }
    catch (e) { shim.log('recap extras failed: ' + (e && e.message || e)); }
    statsRef = stats;   // so the outro's weave actions can annotate this record
    await (stats.record ? stats.record(result, recapExtras) : Promise.resolve());
    // Close the feed-forward loop: seed the NEXT run's BootConfig.priorRun.
    // Standalone persists it locally; hosted, the C# host reads stats/feedForward
    // for the next `init` (Agent I). Never let this throw past the run.
    try {
      if (stats.feedForward && !config.hosted) {
        shim.saveStandalonePrefs({ priorRun: await stats.feedForward() });
      }
    } catch (_e) { /* feed-forward is best-effort */ }
    const ack = shim.emitResult(result);
    shim.log(`run complete: peakDepth=${result.peakDepth.toFixed(2)} score=${result.totalScore}/${result.maxScore} -> ${ack.delivered}`);
    try { await runOutro(result, ack, shellCtx); }
    catch (e) { shim.log('outro failed: ' + (e && e.message || e)); showDoneFallback(result, ack); }

  } catch (err) {
    disposePauseIfAny();   // `pause` is scoped to the try; reach it via its handle
    safeCall(subliminals, 'dispose'); subliminals = null;
    teardownCounter();
    shim.log('boot/run failed: ' + (err && (err.stack || err.message) || err));
    shim.bootError(String(err && err.message || err));
    if (dom.loader) dom.loader.hidden = true;
    if (dom.stage) dom.stage.innerHTML = `<div class="intake-fatal">Something went wrong starting ${PRODUCT_NAME}.</div>`;
  }
});

shim.announceReady();
shim.log('boot: ready posted');

/* ----------------------------------------------------------------------------
 * SMALL SHELL HELPERS — DOM sugar, pacing, guarded optional calls.
 * -------------------------------------------------------------------------- */
function sleep(ms) { return new Promise((r) => setTimeout(r, Math.max(0, ms | 0))); }

/**
 * Tear down the pause menu from a path that cannot see the beat loop's `pause`
 * binding (the fatal catch). ui/pause.js publishes its live handle on window for
 * exactly this; no import needed, so it is safe on the error path.
 */
function disposePauseIfAny() {
  try {
    const p = (typeof window !== 'undefined') ? window.__intakePauseHandle : null;
    if (p && typeof p.dispose === 'function') p.dispose();
  } catch (_e) { /* never fatal */ }
}

function el(tag, cls, text) {
  const n = doc.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

/** Construct via a real factory; fall back to the stub if it throws. */
function tryMake(name, make, fallback) {
  try { return make(); }
  catch (e) { shim.log(`${name} factory failed — using stub (${e && e.message || e})`); return fallback(); }
}

/** Call an optional method on an optional object; log + swallow failures. */
function safeCall(obj, fn, ...args) {
  try { if (obj && typeof obj[fn] === 'function') return obj[fn](...args); }
  catch (e) { shim.log(`${fn} failed: ` + (e && e.message || e)); }
  return undefined;
}

/** Await a Background.transition(kind) but never wedge the loop on it. */
function safeTransition(background, kind) {
  try {
    if (background && typeof background.transition === 'function' && kind) {
      return Promise.race([
        Promise.resolve(background.transition(kind)).catch(() => {}),
        sleep(2500),
      ]);
    }
  } catch (_e) { /* fall through */ }
  return Promise.resolve();
}

/** Wait ~ms, but a click on `target` skips ahead immediately. */
function clickSkippableWait(ms, target) {
  return new Promise((resolve) => {
    let done = false;
    let t = 0;
    const finish = () => {
      if (done) return;
      done = true;
      clearTimeout(t);
      if (target) { try { target.removeEventListener('click', finish); } catch (_e) {} }
      resolve();
    };
    t = setTimeout(finish, Math.max(0, ms | 0));
    if (target) { try { target.addEventListener('click', finish); } catch (_e) {} }
  });
}

/**
 * Typewriter: reveal lines one <p> at a time. `state.fast` (flipped by a click)
 * instantly completes everything — repeat players click straight through.
 */
async function typeLines(container, lines, opts = {}) {
  const cps = opts.cps || 42;
  const state = opts.state || { fast: false };
  for (const line of lines || []) {
    const p = el('p', 'intake-typeline');
    container.appendChild(p);
    for (let i = 0; i < line.length; i++) {
      if (state.fast) break;
      p.textContent = line.slice(0, i + 1);
      await sleep(1000 / cps);
    }
    p.textContent = line;
    if (!state.fast) await sleep(opts.lineDelay == null ? 240 : opts.lineDelay);
  }
}

function showOverlay() { if (dom.overlay) { dom.overlay.innerHTML = ''; dom.overlay.hidden = false; } }
function hideOverlay() { if (dom.overlay) { dom.overlay.hidden = true; dom.overlay.innerHTML = ''; } }

/* ----------------------------------------------------------------------------
 * THEME + FICTION IDENTITY
 * -------------------------------------------------------------------------- */
function applyTheme(theme) {
  if (!doc || !theme) return;
  try {
    const r = doc.documentElement.style;
    if (theme.accent)  r.setProperty('--intake-accent', theme.accent);
    if (theme.accent2) r.setProperty('--intake-accent-2', theme.accent2);
  } catch (_e) { /* cosmetic only */ }
}

function resolveSubjectId(config) {
  if (config.subjectId) return String(config.subjectId);
  if (!config.hosted) {
    try { const id = shim.ensureStandaloneSubjectId && shim.ensureStandaloneSubjectId(); if (id) return id; }
    catch (_e) { /* fall through */ }
  }
  // Hosted without an id (or storage-less standalone): ephemeral, fiction holds.
  return String(1 + Math.floor(Math.random() * 9998)).padStart(4, '0');
}

/** "0417" -> "Subject #0417"; a host-supplied full string passes through. */
function formatSubject(theme, id) {
  if (!id) return `${theme.subjectNoun} #????`;
  const s = String(id);
  if (s.includes('#')) return s;
  return `${theme.subjectNoun} #${s.padStart(4, '0')}`;
}

function findArchetype(bank, id) {
  if (!bank || !Array.isArray(bank.archetypes) || !id) return null;
  return bank.archetypes.find((a) => a && a.id === id) || null;
}

function prettyId(id) {
  return id ? String(id).replace(/[-_]+/g, ' ').trim() : '';
}

/** "trance-dabbler" / archetype -> "THE TRANCE DABBLER" (feed-forward voice). */
function classificationName(bank, archetypeId) {
  const a = findArchetype(bank, archetypeId);
  const name = (a && a.name) || prettyId(archetypeId);
  if (!name) return '';
  const up = name.toUpperCase();
  return up.startsWith('THE ') ? up : 'THE ' + up;
}

/* ----------------------------------------------------------------------------
 * 1+2. CLINICAL BRIEFING — the intro screen. Typewriter intro lines, Subject #,
 * feed-forward greeting for returning subjects, an in-fiction consent card, and
 * a Begin button. A click fast-completes the typewriter for repeat players.
 * -------------------------------------------------------------------------- */
async function runBriefing(ctx) {
  const { config, theme, bank, background, subjectId, priorRun, audio } = ctx;
  if (!dom.overlay || config.m2Test) { safeCall(background, 'setBand', Band.Calibration); return; }

  showOverlay();
  const screen = el('div', 'intake-screen intake-briefing');
  dom.overlay.appendChild(screen);

  // VOICEOVER: the interviewer's spoken intro plays over the briefing — intro_1 ->
  // intro_2 -> intro_3 sequentially (each waits for the previous to end; a missing
  // clip / stub audio just skips straight to the next). Runs concurrently with the
  // typewriter. Leaving the briefing (Begin, or any teardown) stops the chain.
  let introVoiceCancelled = false;
  const stopIntroVoice = () => {
    if (introVoiceCancelled) return;
    introVoiceCancelled = true;
    try { if (audio && typeof audio.voiceStop === 'function') audio.voiceStop(0.25); } catch (_e) {}
  };
  (async () => {
    for (const id of ['intro_1', 'intro_2', 'intro_3']) {
      if (introVoiceCancelled) return;
      await new Promise((res) => {
        let done = false;
        const fin = () => { if (done) return; done = true; res(); };
        let handle = null;
        try {
          if (audio && typeof audio.voice === 'function') handle = audio.voice(id, { onEnd: fin });
        } catch (_e) { handle = null; }
        if (!handle) fin(); // stub / muted / headless -> advance without waiting
      });
    }
  })();

  const priorArch = priorRun && priorRun.route && priorRun.route.primaryArchetypeId;
  screen.appendChild(el('div', 'intake-brief-eyebrow', `${PRODUCT_NAME} · Form CRA-7`));
  screen.appendChild(el('h1', 'intake-brief-title', 'Cognitive Response Assessment'));
  screen.appendChild(el('div', 'intake-brief-subject',
    formatSubject(theme, subjectId) + (priorArch ? ' · returning' : '')));
  if (priorArch) {
    const cls = classificationName(bank, priorArch);
    if (cls) {
      screen.appendChild(el('p', 'intake-brief-prior',
        `${formatSubject(theme, subjectId)}, returning. Previous classification: ${cls}.`));
    }
  }

  const linesBox = el('div', 'intake-brief-lines');
  screen.appendChild(linesBox);

  // Consent card + Begin (revealed once the typewriter finishes or is skipped).
  const consentCard = el('div', 'intake-consent-card');
  const consentLabel = el('label', 'intake-consent');
  const checkbox = doc.createElement('input');
  checkbox.type = 'checkbox';
  consentLabel.appendChild(checkbox);
  consentLabel.appendChild(el('span', null, 'I consent to honest responses.'));
  consentCard.appendChild(consentLabel);
  const begin = el('button', 'intake-begin', 'Begin assessment');
  begin.disabled = true;
  screen.appendChild(consentCard);
  screen.appendChild(begin);

  const state = { fast: false };
  const fastForward = () => { state.fast = true; };
  dom.overlay.addEventListener('click', fastForward);

  await typeLines(linesBox, theme.introLines, { state, cps: 46 });
  screen.classList.add('is-ready');

  checkbox.addEventListener('change', () => { begin.disabled = !checkbox.checked; });
  await new Promise((resolve) => {
    begin.addEventListener('click', (e) => {
      e.stopPropagation();
      if (!begin.disabled) resolve();
    });
  });

  try { dom.overlay.removeEventListener('click', fastForward); } catch (_e) {}
  stopIntroVoice(); // advancing past the briefing cuts any still-playing intro line
  safeCall(background, 'setBand', Band.Calibration);
  hideOverlay();
}

/* ----------------------------------------------------------------------------
 * 3+4. BAND INTERSTITIALS + THE CLIMAX GLITCH — section cards shown when the
 * engine flags beat.meta.bandNew. Text presentation degrades with depth
 * (crisp -> drifting -> melted); Climax entry fakes a malfunction first, then
 * the real voice comes through. Background transitions ride under the card.
 * -------------------------------------------------------------------------- */
const BAND_TRANSITION = {
  [Band.Establishing]: 'iris',
  [Band.Deepening]:    'fork',
  [Band.Climax]:       'plunge',
  [Band.Recovery]:     'surface',
};
const BAND_TONE = {
  [Band.Calibration]:  'crisp',
  [Band.Establishing]: 'crisp',
  [Band.Deepening]:    'drift',
  [Band.Climax]:       'melt',
  [Band.Recovery]:     'crisp',
};
const SECTION_FLAVOR = {
  [Band.Calibration]:  'Plain questions. Honest answers. This establishes your baseline.',
  [Band.Establishing]: 'Response mapping begins. Answer with the first thing that surfaces.',
  [Band.Deepening]:    'You may notice the questions answering themselves. Allow it.',
  [Band.Climax]:       'stop holding on. the survey is holding you now.',
  [Band.Recovery]:     'The instruments are powering down. Breathe out. Come back up.',
};

async function showInterstitial(band, ctx) {
  const { theme, background, config } = ctx;
  if (!dom.overlay || config.m2Test) { safeCall(background, 'setBand', band); return; }

  showOverlay();
  if (band === Band.Climax) {
    try { await playGlitch(theme); } catch (e) { shim.log('glitch failed: ' + (e && e.message || e)); }
  }
  safeCall(background, 'setBand', band);
  const trans = safeTransition(background, BAND_TRANSITION[band]);

  const idx = bandIndex(band);
  const card = el('div', 'intake-screen intake-section tone-' + (BAND_TONE[band] || 'crisp'));
  card.appendChild(el('div', 'intake-section-count',
    `Section ${idx >= 0 ? idx + 1 : '?'} of ${BAND_ORDER.length}`));
  card.appendChild(el('h2', 'intake-section-title', theme.sectionTitles[band] || ''));
  card.appendChild(el('p', 'intake-section-flavor', SECTION_FLAVOR[band] || ''));
  dom.overlay.appendChild(card);

  // Generous read time (player feedback: the cards flipped too fast); a click
  // still skips ahead, so the patient read and the impatient tap both win.
  await clickSkippableWait(band === Band.Climax ? 5300 : 4300, dom.overlay);
  await trans;
  hideOverlay();
}

/** ~2.5s fake malfunction: flicker + garbled title + scanlines, a beat of
 *  black, then the real voice comes through. Pure CSS/JS, no assets. */
async function playGlitch(theme) {
  const wrap = el('div', 'intake-glitch');
  const title = el('div', 'intake-glitch-title', theme.sectionTitles[Band.Climax] || 'Section 4 · Deep Compliance Survey');
  wrap.appendChild(title);
  wrap.appendChild(el('div', 'intake-glitch-scan'));
  dom.overlay.appendChild(wrap);
  dom.overlay.classList.add('intake-glitching');

  const src = title.textContent;
  const GL = '█▓▒░<>/\\|#@%&$!?';
  const scramble = setInterval(() => {
    try {
      title.textContent = src.split('')
        .map((ch) => (ch !== ' ' && Math.random() < 0.28) ? GL[(Math.random() * GL.length) | 0] : ch)
        .join('');
    } catch (_e) { /* keep flickering */ }
  }, 55);

  await sleep(1150);
  clearInterval(scramble);
  dom.overlay.classList.remove('intake-glitching');
  wrap.remove();

  const black = el('div', 'intake-glitch-black');
  dom.overlay.appendChild(black);
  await sleep(420);

  const voice = el('div', 'intake-glitch-voice', 'there you are.');
  black.appendChild(voice);
  await sleep(30); // let it mount before the transition class lands
  voice.classList.add('is-on');
  await sleep(950);
  black.remove();
}

/* ----------------------------------------------------------------------------
 * 5. IN-FICTION HUD — question counter, a calibration dial whose needle morphs
 * into a slow spiral as depth rises, and the current section name. NEVER the
 * raw depth number or band id.
 *
 * THE COUNTER LIES. The engine's meta.qTotal is the honest estimate and stays
 * authoritative for every internal system (pacing, bands, reward planning); the
 * HUD never shows it. What the HUD shows is a DISPLAY LIE that opens at
 * COUNTER_START_TOTAL (20), creeps upward in glitching revisions, and only ever
 * catches the real number when the run is genuinely nearly over. See the
 * COUNTER_* block for the schedule; the glitch borrows the CORRUPT_* datamosh
 * vocabulary from render/beats.js (scramble pool + RGB-split ghosts + steps()
 * jitter) so it reads as the same feed breaking, not a new effect.
 * -------------------------------------------------------------------------- */
const SVG_NS = 'http://www.w3.org/2000/svg';
const hud = { built: false, q: null, dial: null, needle: null, spiral: null, section: null };

/* ---- COUNTER LIE: creep schedule -----------------------------------------
 * The schedule is written in terms of the LEAD (displayed total minus the
 * question you are on) rather than the total itself, because the lead is what
 * a player actually feels ("about a dozen to go"). It holds flat, then closes:
 *   qIndex <= HOLD_Q                   -> exactly START_TOTAL (the run looks short)
 *   after that, lead ramps linearly    -> from (START_TOTAL - HOLD_Q) down to
 *                                         (real - REVEAL_P * real) at the reveal
 * so the displayed total rises ~0.9 per question — always moving, never fast
 * enough to be caught. Three invariants hold at every beat, in this order:
 *   1. never below qIndex + MIN_LEAD  (a total under the question you are on is
 *      an obvious tell — it would read as a bug, not as a lie);
 *   2. never equal to the truth before REVEAL_P of the way through (capped at
 *      real-2), so the real ~65 can't be inferred early;
 *   3. never DECREASES (the displayed value is a running max), which also makes
 *      endless mode — where real keeps growing — behave for free.
 * Reference run (real=65, quantized by the bump gate below): 20 held to q9, then
 * 22, 25, 29, 32, 36, 40, 43, 47, 50, 54, 57, 61, 63 and finally the true 65 at
 * q60 — 92% of the way in, with five questions left. Fifteen revisions, each
 * +2..+4, and the lead never leaves the 5..19 window. */
const COUNTER_START_TOTAL = 20;   // the opening understatement
const COUNTER_HOLD_Q      = 8;    // questions the opening total holds before creeping
const COUNTER_REVEAL_P    = 0.9;  // fraction of the real run before the truth may show
const COUNTER_MIN_LEAD    = 4;    // hard floor: displayed total >= qIndex + this
/** A revision must be worth at least this much, and beats since the last one,
 *  before it bumps — otherwise the number would tick +1 nearly every card and
 *  stop reading as a glitchy re-estimate. */
const COUNTER_BUMP_MIN    = 2;
const COUNTER_BUMP_GAP_Q  = 4;
/** How long the bump's scramble runs before the new number lands (ms). */
const COUNTER_BUMP_MS     = 620;
/** Per-band chance that the TOTAL is replaced by a fourth-wall phrase instead
 *  (mirrors CORRUPT_CHANCE in beats.js: nothing before Deepening, escalating
 *  into Climax). Recovery is the walk-down and never breaks the wall. */
const COUNTER_PHRASE_CHANCE = {
  [Band.Deepening]: 0.12,
  [Band.Climax]:    0.22,
};
/** How long a phrase garbles before the coin flip resolves it (ms). */
const COUNTER_PHRASE_MS   = 2000;
/** Coin flip: roll < this => snap back to the number, else keep glitching until
 *  the next card's render replaces it. */
const COUNTER_PHRASE_SNAP = 0.5;
/** Bands where the counter is loose enough to knock off (and to lie in words).
 *  Latched: once live it stays live, including through Recovery. */
const COUNTER_LIVE_BANDS  = [Band.Deepening, Band.Climax];
/** Chance that a click actually detaches the counter. The other 3/4 wobble. */
const COUNTER_FALL_CHANCE = 0.25;
/** Glyph pool the counter scrambles into — same set as beats.js CORRUPT_SCRAMBLE. */
const COUNTER_SCRAMBLE = '#@%&*!/|<>_=+$?~^';

/** Whole-run counter state. Reset by resetCounter() at buildHud time. */
const counter = {
  shown: 0,        // displayed total, monotone (0 = nothing painted yet)
  lastBumpQ: 0,    // qIndex of the last revision
  live: false,     // latched: clickable + allowed to break the wall
  fallen: false,   // knocked off — permanently gone for this run
  master: 1,       // caps.masterIntensity
  idx: null,       // "Q 23 /" span
  tot: null,       // total-or-phrase span (the part that lies)
  fx: [],          // live timers owned by a glitch/phrase
  mem: [],         // last two phrase indices (no-repeat)
  fall: null,      // body-parented falling clone
  raf: 0,          // fall animation handle
  fallT: 0,        // reduced-motion fade timer
  wobbleT: 0,      // failed-click wobble timer
};

function svgEl(name, attrs) {
  const n = doc.createElementNS(SVG_NS, name);
  for (const k in attrs) n.setAttribute(k, attrs[k]);
  return n;
}

function buildHud(caps) {
  if (!dom.hud || hud.built) return;
  try {
    dom.hud.innerHTML = '';
    resetCounter(caps);
    ensureCounterCss();
    // The counter is two spans: an honest index and a total that lies. Only the
    // total is ever scrambled/replaced, so "Q 23 /" stays readable throughout.
    hud.q = el('span', 'intake-hud-q');
    counter.idx = el('span', 'ixq-idx', '');
    counter.tot = el('span', 'ixq-tot', '');
    hud.q.appendChild(counter.idx);
    hud.q.appendChild(counter.tot);
    hud.q.addEventListener('click', onCounterClick);

    const svg = svgEl('svg', { viewBox: '0 0 40 40', class: 'intake-hud-dial', 'aria-hidden': 'true' });
    // gauge arc ticks (-120° .. +120°, 0° = straight up)
    for (let i = 0; i <= 8; i++) {
      const a = ((-120 + i * 30) - 90) * Math.PI / 180;
      svg.appendChild(svgEl('line', {
        x1: (20 + Math.cos(a) * 13.5).toFixed(2), y1: (20 + Math.sin(a) * 13.5).toFixed(2),
        x2: (20 + Math.cos(a) * 16.5).toFixed(2), y2: (20 + Math.sin(a) * 16.5).toFixed(2),
        class: 'intake-dial-tick',
      }));
    }
    svg.appendChild(svgEl('circle', { cx: 20, cy: 20, r: 17.5, class: 'intake-dial-ring' }));
    // needle (clinical gauge face)
    hud.needle = svgEl('g', { class: 'intake-dial-needleg' });
    hud.needle.appendChild(svgEl('line', { x1: 20, y1: 20, x2: 20, y2: 7, class: 'intake-dial-needle' }));
    hud.needle.appendChild(svgEl('circle', { cx: 20, cy: 20, r: 1.6, class: 'intake-dial-hub' }));
    svg.appendChild(hud.needle);
    // spiral glyph (crossfades in as depth rises; CSS rotates it slowly)
    let d = '';
    for (let i = 0; i <= 110; i++) {
      const a = i * 0.24, r = 0.6 + i * 0.115;
      d += (i ? ' L' : 'M') + (20 + Math.cos(a) * r).toFixed(2) + ' ' + (20 + Math.sin(a) * r).toFixed(2);
    }
    hud.spiral = svgEl('g', { class: 'intake-dial-spiralg' });
    hud.spiral.appendChild(svgEl('path', { d, class: 'intake-dial-spiral' }));
    svg.appendChild(hud.spiral);
    hud.dial = svg;

    hud.section = el('span', 'intake-hud-section', '');
    dom.hud.appendChild(hud.q);
    dom.hud.appendChild(svg);
    dom.hud.appendChild(hud.section);
    hud.built = true;
  } catch (e) { shim.log('hud build failed: ' + (e && e.message || e)); }
}

/** Section title minus its "Section N ·" prefix — the HUD's small label. */
function sectionShortName(theme, band) {
  const t = (theme.sectionTitles && theme.sectionTitles[band]) || '';
  const cut = t.split('·');
  return (cut.length > 1 ? cut.slice(1).join('·') : t).trim();
}

function updateHud(beat, theme) {
  if (!dom.hud || !hud.built) return;
  try {
    updateCounter(beat, theme);
    const dep = clamp01(beat.depth || 0);
    const morph = clamp01((dep - 0.2) / 0.55); // gauge -> spiral crossfade window
    if (hud.needle) {
      hud.needle.style.transform = `rotate(${(-120 + 240 * dep).toFixed(1)}deg)`;
      hud.needle.style.opacity = String(1 - 0.85 * morph);
    }
    if (hud.spiral) {
      hud.spiral.style.opacity = String(0.95 * morph);
      hud.dial.style.setProperty('--dial-spin', (14 - 9 * dep).toFixed(1) + 's');
    }
    hud.section.textContent = sectionShortName(theme, beat.band);
  } catch (_e) { /* HUD is cosmetic */ }
}

/* ----------------------------------------------------------------------------
 * 5b. THE UNRELIABLE COUNTER — creep schedule, glitch bumps, the fourth-wall
 * break and the knock-it-off interaction. Everything here is DISPLAY ONLY:
 * beat.meta.qTotal is never shown and never modified.
 * -------------------------------------------------------------------------- */

/** Wipe all per-run counter state (called once per run, from buildHud). */
function resetCounter(caps) {
  teardownCounter();
  counter.shown = 0;
  counter.lastBumpQ = 0;
  counter.live = false;
  counter.fallen = false;
  counter.mem.length = 0;
  counter.master = (caps && caps.masterIntensity != null) ? caps.masterIntensity : 1;
}

/** Full-strength datamosh, or the soft path? Mirrors boot's gallery gate. */
function counterAnimates() {
  return !prefersReducedMotion() && counter.master >= 0.35;
}

/** Scramble a fraction of a string's non-space chars (beats.js scrambleText,
 *  inlined here so the HUD never depends on the optional render module). */
function counterScramble(src, amount) {
  const s = String(src == null ? '' : src);
  let out = '';
  for (let i = 0; i < s.length; i++) {
    const ch = s[i];
    if (ch === ' ' || Math.random() > amount) out += ch;
    else out += COUNTER_SCRAMBLE[(Math.random() * COUNTER_SCRAMBLE.length) | 0];
  }
  return out;
}

/** The lie. Real qTotal in, displayed total out — see the COUNTER_* header. */
function displayedTotal(qi, real) {
  if (!(real > 0)) return counter.shown;
  const start = Math.min(COUNTER_START_TOTAL, real);
  const qR = Math.max(COUNTER_HOLD_Q + 1, COUNTER_REVEAL_P * real);   // reveal question
  let t;
  if (qi <= COUNTER_HOLD_Q) {
    t = start;                                           // the opening understatement
  } else {
    const u = Math.min(1, (qi - COUNTER_HOLD_Q) / (qR - COUNTER_HOLD_Q));
    const lead0 = start - COUNTER_HOLD_Q;                // lead as the creep begins
    const lead1 = real - qR;                             // lead at the reveal point
    t = Math.round(qi + lead0 + (lead1 - lead0) * u);
  }
  t = Math.max(t, qi + COUNTER_MIN_LEAD);                // never at/under the index
  t = Math.min(t, (qi / real) < COUNTER_REVEAL_P ? real - 2 : real);  // truth stays hidden
  return Math.max(t, counter.shown);                     // monotone: never counts down
}

/** Repaint both spans from state (no animation). */
function paintCounter(qi) {
  if (!counter.idx || !counter.tot) return;
  counter.idx.textContent = `Q ${qi} / `;
  counter.tot.textContent = `~${counter.shown}`;
}

/** Kill every timer a glitch/phrase owns and restore the plain number. Called
 *  at the top of each render (so a held phrase can never survive into the next
 *  beat) and by teardownCounter. Safe to call at any time. */
function clearCounterFx() {
  for (const stop of counter.fx) { try { stop(); } catch (_e) {} }
  counter.fx.length = 0;
  const tot = counter.tot;
  if (!tot) return;
  try {
    tot.classList.remove('ixq-glitch', 'ixq-reduced');
    delete tot.dataset.ixq;
  } catch (_e) {}
}

/**
 * Garble the TOTAL span for `ms`, settling on `text`.
 * opts.keepChance — probability the garble is NOT resolved (it keeps running
 *   until the next render clears it); opts.snapTo — what to paint instead of
 *   `text` when it does resolve. Both optional (a bump passes neither).
 */
function garbleTotal(text, ms, opts) {
  const tot = counter.tot;
  if (!tot) return;
  const keepChance = (opts && opts.keepChance) || 0;
  const snapTo = (opts && opts.snapTo != null) ? opts.snapTo : text;
  const soft = !counterAnimates();
  const paint = (s) => { try { tot.textContent = s; tot.dataset.ixq = s; } catch (_e) {} };
  let spin = 0;

  try { tot.classList.add('ixq-glitch'); if (soft) tot.classList.add('ixq-reduced'); } catch (_e) {}
  // Soft path (reduced motion / low master): the text lands immediately and just
  // sits there — legible, no jitter, no RGB ghosts. The lie still happens.
  paint(soft ? text : counterScramble(text, 0.9));
  if (!soft) {
    const t0 = Date.now();
    spin = setInterval(() => {
      const p = Math.min(1, (Date.now() - t0) / ms);
      paint(counterScramble(text, 0.7 * (1 - p) + 0.08));   // heavy, then settling
    }, 62);
    counter.fx.push(() => { try { clearInterval(spin); } catch (_e) {} });
  }

  const settle = setTimeout(() => {
    if (keepChance > 0 && Math.random() < keepChance) return;  // KEEP GLITCHING
    if (spin) { try { clearInterval(spin); } catch (_e) {} spin = 0; }
    try {
      tot.classList.remove('ixq-glitch', 'ixq-reduced');
      delete tot.dataset.ixq;
      tot.textContent = snapTo;
    } catch (_e) {}
  }, ms);
  counter.fx.push(() => { try { clearTimeout(settle); } catch (_e) {} });
}

/** Pick a fourth-wall phrase, avoiding the last two used. */
function pickCountPhrase(theme) {
  const pool = (theme && Array.isArray(theme.countPhrases) && theme.countPhrases.length)
    ? theme.countPhrases : null;
  if (!pool) return null;                                  // bank without the key
  let idxs = pool.map((_, i) => i).filter((i) => !counter.mem.includes(i));
  if (!idxs.length) idxs = pool.map((_, i) => i);
  const idx = idxs[Math.floor(Math.random() * idxs.length)];
  counter.mem.push(idx); if (counter.mem.length > 2) counter.mem.shift();
  return pool[idx];
}

/** Per-beat counter pass: revise the lie, then roll for the wall break. */
function updateCounter(beat, theme) {
  if (counter.fallen || !counter.idx || !counter.tot) return;   // knocked off = gone
  clearCounterFx();                                             // nothing leaks across beats
  const meta = beat.meta || {};
  const qi = (typeof meta.qIndex === 'number') ? meta.qIndex : 0;
  const real = (typeof meta.qTotal === 'number') ? meta.qTotal : 0;
  if (!qi || !(real > 0)) { counter.idx.textContent = ''; counter.tot.textContent = ''; return; }

  // Once the run reaches Deepening the counter comes loose — clickable, and
  // allowed to answer in words instead of numbers. Latched for the rest of it.
  if (!counter.live && COUNTER_LIVE_BANDS.indexOf(beat.band) >= 0) {
    counter.live = true;
    try { hud.q.classList.add('ixq-live'); } catch (_e) {}
  }

  const target = displayedTotal(qi, real);
  const first = !counter.shown;
  // A revision must be worth showing: >= COUNTER_BUMP_MIN and >= COUNTER_BUMP_GAP_Q
  // beats since the last one — UNLESS the index is closing on the displayed total,
  // in which case it revises immediately (an honest-looking total can never be
  // caught up to by the question number).
  const forced = target > counter.shown && counter.shown < qi + COUNTER_MIN_LEAD;
  const due = target - counter.shown >= COUNTER_BUMP_MIN && qi - counter.lastBumpQ >= COUNTER_BUMP_GAP_Q;
  const bump = !first && (forced || due);
  if (first || bump) { counter.shown = target; counter.lastBumpQ = qi; }
  paintCounter(qi);
  if (first) return;                                    // the opening 20 lands quietly

  if (bump) { garbleTotal(`~${counter.shown}`, COUNTER_BUMP_MS, null); return; }

  // FOURTH WALL (Deepening onward, escalating into Climax). Mutually exclusive
  // with a bump, exactly like beats.js lets one set-piece own a beat.
  const chance = COUNTER_PHRASE_CHANCE[beat.band] || 0;
  if (!chance || !counter.live || Math.random() >= chance) return;
  const phrase = pickCountPhrase(theme);
  if (!phrase) return;
  garbleTotal(phrase, COUNTER_PHRASE_MS, {
    keepChance: 1 - COUNTER_PHRASE_SNAP,                // coin flip: snap back, or keep going
    snapTo: `~${counter.shown}`,
  });
}

/** Click on a loose counter: 1-in-4 it comes off for good, else it wobbles. */
function onCounterClick() {
  if (!counter.live || counter.fallen || !hud.q) return;
  if (Math.random() < COUNTER_FALL_CHANCE) { knockCounterOff(); return; }
  // The other three: a loose-fitting wobble that settles back — the tell that
  // teaches the player it can be knocked off, so they keep poking it.
  try {
    hud.q.classList.remove('ixq-wobble');
    void hud.q.offsetWidth;                             // restart the animation
    hud.q.classList.add('ixq-wobble');
    clearTimeout(counter.wobbleT);
    counter.wobbleT = setTimeout(() => {
      try { hud.q.classList.remove('ixq-wobble'); } catch (_e) {}
    }, 560);
  } catch (_e) { /* cosmetic */ }
}

/** Detach the counter: a body-parented clone falls out of frame and the real
 *  span goes invisible but keeps its box, so the HUD row never reflows. There
 *  is no respawn — the player has lost their sense of progress for good. */
function knockCounterOff() {
  counter.fallen = true;
  counter.live = false;
  clearCounterFx();
  try {
    hud.q.classList.remove('ixq-live', 'ixq-wobble');
    hud.q.classList.add('ixq-gone');
  } catch (_e) {}
  try {
    const r = hud.q.getBoundingClientRect();
    const cs = (typeof getComputedStyle === 'function') ? getComputedStyle(hud.q) : null;
    const clone = el('div', 'ixq-fall', hud.q.textContent || '');
    clone.style.left = r.left + 'px';
    clone.style.top = r.top + 'px';
    if (cs) {
      clone.style.fontFamily = cs.fontFamily; clone.style.fontSize = cs.fontSize;
      clone.style.fontWeight = cs.fontWeight; clone.style.letterSpacing = cs.letterSpacing;
      clone.style.textTransform = cs.textTransform; clone.style.color = cs.color;
    }
    // <body>, never the stage: beats.js wipes the stage on every render and the
    // fall must outlive the card it started on (same reasoning as effects.js
    // bodyHost()). The HUD itself is fixed-position, so the clone matches.
    (doc.body || doc.documentElement).appendChild(clone);
    counter.fall = clone;
    if (counterAnimates()) startCounterFall(clone, r);
    else {
      clone.classList.add('ixq-fall-fade');              // reduced motion: it just goes
      setTimeout(() => { try { clone.style.opacity = '0'; } catch (_e) {} }, 20);
      counter.fallT = setTimeout(removeCounterFall, 620);
    }
  } catch (_e) { removeCounterFall(); }
}

/** Gravity + tumble + drift, on rAF, until it clears the bottom of the window. */
function startCounterFall(node, rect) {
  const G = 0.0024;                                     // px per ms^2
  let x = 0, y = 0, rot = 0;
  let vy = -0.05 - Math.random() * 0.05;                 // small hop as it detaches
  const vx = (Math.random() < 0.5 ? -1 : 1) * (0.02 + Math.random() * 0.06);
  const spin = (Math.random() < 0.5 ? -1 : 1) * (0.05 + Math.random() * 0.12); // deg/ms
  const floor = ((typeof window !== 'undefined' && window.innerHeight) || 800) + 160;
  let last = Date.now();
  const tick = () => {
    const t = Date.now(); const dt = Math.min(48, t - last); last = t;
    vy += G * dt; y += vy * dt; x += vx * dt; rot += spin * dt;
    try {
      node.style.transform = `translate(${x.toFixed(1)}px, ${y.toFixed(1)}px) rotate(${rot.toFixed(1)}deg)`;
    } catch (_e) {}
    if (rect.top + y > floor) { removeCounterFall(); return; }
    counter.raf = requestAnimationFrame(tick);
  };
  counter.raf = requestAnimationFrame(tick);
}

/** Drop the falling clone + its clock. Idempotent. */
function removeCounterFall() {
  if (counter.raf) { try { cancelAnimationFrame(counter.raf); } catch (_e) {} counter.raf = 0; }
  if (counter.fallT) { try { clearTimeout(counter.fallT); } catch (_e) {} counter.fallT = 0; }
  if (counter.fall) { try { counter.fall.remove(); } catch (_e) {} counter.fall = null; }
}

/** Run-end / failure teardown: no timer, no garble and no falling clone may
 *  survive into the outro. Idempotent. */
function teardownCounter() {
  clearCounterFx();
  removeCounterFall();
  if (counter.wobbleT) { try { clearTimeout(counter.wobbleT); } catch (_e) {} counter.wobbleT = 0; }
  try { if (hud.q) hud.q.classList.remove('ixq-wobble', 'ixq-live'); } catch (_e) {}
}

/* Counter styles — a deliberate echo of beats.js's IXCR_CSS (same steps() jitter,
 * same pink/cyan RGB-split ghosts read off a data attribute, same reduced-motion
 * escape hatch), scoped to the HUD so it can never touch a card.
 * (No stray backtick may ever appear inside this template literal.) */
const IX_COUNTER_CSS = `
.intake-hud-q.ixq-live { pointer-events: auto; cursor: pointer; }
.intake-hud-q.ixq-gone { visibility: hidden; }
.ixq-tot { display: inline-block; white-space: nowrap; }
.ixq-glitch {
  position: relative;
  animation: ixq-jitter .16s steps(2, end) infinite;
  text-shadow: 0 0 12px rgba(255,43,208,.35), 0 0 3px rgba(32,232,255,.25);
}
.ixq-glitch::before, .ixq-glitch::after {
  content: attr(data-ixq);
  position: absolute; left: 0; top: 0; white-space: nowrap;
  pointer-events: none; opacity: .7; mix-blend-mode: screen;
  font: inherit; letter-spacing: inherit;
}
.ixq-glitch::before { color: #ff2bd0; animation: ixq-tearA .27s steps(3, end) infinite; }
.ixq-glitch::after  { color: #20e8ff; animation: ixq-tearB .34s steps(3, end) infinite; }
@keyframes ixq-jitter {
  0%   { transform: translate(0,0) skewX(0deg); }
  33%  { transform: translate(-1px,1px) skewX(-.6deg); }
  66%  { transform: translate(1px,-1px) skewX(.5deg); }
  100% { transform: translate(0,0) skewX(0deg); }
}
@keyframes ixq-tearA {
  0%   { clip-path: inset(0 0 62% 0); transform: translate(-3px,-1px); }
  50%  { clip-path: inset(12% 0 46% 0); transform: translate(3px,0); }
  100% { clip-path: inset(0 0 57% 0); transform: translate(-1px,2px); }
}
@keyframes ixq-tearB {
  0%   { clip-path: inset(46% 0 0 0); transform: translate(3px,1px); }
  50%  { clip-path: inset(58% 0 8% 0); transform: translate(-3px,0); }
  100% { clip-path: inset(52% 0 0 0); transform: translate(2px,-2px); }
}
/* loose-fitting wobble on a click that did NOT knock it off */
.ixq-wobble { display: inline-block; animation: ixq-wobble .52s cubic-bezier(.3,.9,.4,1) 1; }
@keyframes ixq-wobble {
  0%   { transform: rotate(0deg) translateY(0); }
  18%  { transform: rotate(-4.5deg) translateY(2px); }
  40%  { transform: rotate(3deg) translateY(-1px); }
  62%  { transform: rotate(-1.8deg) translateY(1px); }
  82%  { transform: rotate(.8deg) translateY(0); }
  100% { transform: rotate(0deg) translateY(0); }
}
@keyframes ixq-blip { 0%, 100% { opacity: 1; } 50% { opacity: .35; } }
/* the detached clone, body-parented so the stage wipe can't take it */
.ixq-fall {
  position: fixed; z-index: 2147480000; pointer-events: none; white-space: nowrap;
  will-change: transform, opacity;
}
.ixq-fall-fade { transition: opacity .5s ease; }
/* soft path: legible number, no datamosh */
.ixq-glitch.ixq-reduced { animation: none; text-shadow: none; }
.ixq-glitch.ixq-reduced::before, .ixq-glitch.ixq-reduced::after { display: none; }
@media (prefers-reduced-motion: reduce) {
  .ixq-glitch { animation: none; text-shadow: none; }
  .ixq-glitch::before, .ixq-glitch::after { display: none; }
  .ixq-wobble { animation: ixq-blip .3s linear 1; }
}
`;

/** Inject the counter CSS once. No-op headlessly (guarded on `doc`). */
function ensureCounterCss() {
  if (!doc || doc.getElementById('ix-counter-css')) return;
  const s = doc.createElement('style');
  s.id = 'ix-counter-css';
  s.textContent = IX_COUNTER_CSS;
  (doc.head || doc.body || doc.documentElement).appendChild(s);
}

/* ----------------------------------------------------------------------------
 * 6. INTERVIEWER ASIDES — a brief narrator line that fades in near the card
 * and back out. Non-blocking beyond a short pacing pause in the loop.
 * -------------------------------------------------------------------------- */
let asideTimer = 0;
function showAside(line) {
  if (!dom.aside || !line) return;
  try {
    clearTimeout(asideTimer);
    dom.aside.textContent = String(line);
    dom.aside.classList.add('is-on');
    asideTimer = setTimeout(() => { dom.aside.classList.remove('is-on'); }, 1600);
  } catch (_e) { /* cosmetic */ }
}

/* ----------------------------------------------------------------------------
 * 9. ROUTE TINT — feed the leading archetype's signature hue to the tube as
 * the route solidifies. Fully optional (hue-less banks / stub background skip).
 * -------------------------------------------------------------------------- */
function applyRouteTint(engine, bank, background) {
  try {
    const r = engine && engine.route;
    if (!r || !r.primaryArchetypeId) return;
    if (!background || typeof background.setRouteTint !== 'function') return;
    const a = findArchetype(bank, r.primaryArchetypeId);
    if (!a || typeof a.hue !== 'number') return;
    background.setRouteTint({ hue: a.hue, strength: clamp01(r.primaryShare || 0) });
  } catch (_e) { /* tint is cosmetic */ }
}

/* ----------------------------------------------------------------------------
 * 9b. COLOUR HARVEST — a Calibration colour-preference beat just committed.
 * Resolve the chosen option's label to a swatch (core/palette.js), bank it for
 * the spiral, and wash the screen with a deep version of it. A no-op on every
 * other beat, on a timeout, and on a label the swatch table doesn't know.
 * Cosmetic + additive: it can never fail the run.
 * -------------------------------------------------------------------------- */
function harvestColorPick(beat, ev, flashColor, caps) {
  try {
    const tags = (beat && beat.prompt && beat.prompt.tags) || [];
    if (!Array.isArray(tags) || tags.indexOf(COLOR_TAG) < 0) return;
    if (!ev || ev.timedOut || typeof ev.chosenIndex !== 'number') return;
    const opt = beat.options && beat.options[ev.chosenIndex];
    if (!opt || !opt.label) return;
    const hex = noteColorPick(opt.label);
    if (!hex) return;
    shim.log(`colour pick: ${opt.label} -> ${hex}`);
    if (typeof flashColor === 'function') flashColor(deepen(hex), { caps });
  } catch (_e) { /* the flash is garnish; never break the loop */ }
}

/* ----------------------------------------------------------------------------
 * depth fan-out (shared by real + stub render)
 * -------------------------------------------------------------------------- */
function setDepthEverywhere(depth, band, { effects, audio, background, subliminals }) {
  try { effects.setDepth && effects.setDepth(depth); } catch (_e) {}
  try { audio.setDepth && audio.setDepth(depth); } catch (_e) {}
  try { background.setDepth && background.setDepth(depth); } catch (_e) {}
  try { subliminals && subliminals.setDepth && subliminals.setDepth(depth); } catch (_e) {}
}

/* ----------------------------------------------------------------------------
 * 7. OUTRO CEREMONY — staged results: outro lines -> grade card -> route
 * reveal -> recorded statements -> stats -> the report-card artifact. Stages
 * auto-advance; a click skips ahead. Replaces the old 3-line showDone.
 * -------------------------------------------------------------------------- */
function gradeFor(result) {
  const ratio = (result.maxScore > 0) ? clamp01(result.totalScore / result.maxScore) : 0;
  if (ratio >= 0.92) return 'S';
  if (ratio >= 0.80) return 'A';
  if (ratio >= 0.65) return 'B';
  if (ratio >= 0.50) return 'C';
  return 'D';
}

/* ----------------------------------------------------------------------------
 * THE ARCHIVE'S COPY OF THE CLOSING CARD  (feeds core/stats.js -> ui/history.js)
 *
 * ui/history.js re-shows a past run the way the outro showed it: grade, primary
 * classification, the numbers, the colours, the spirals, the payloads. All of
 * that is resolved HERE, at run end, because it needs the bank + theme + the two
 * run ledgers, none of which stats.js can see. Everything below is read from
 * data that demonstrably exists — nothing is guessed except the drafted session
 * NAME, which is flagged `derived` and explained at deriveSessionName().
 * -------------------------------------------------------------------------- */

/** Live stats handle, so the outro's weave actions can annotate the record they
 *  belong to. Set at run end (just before stats.record); null until then. */
let statsRef = null;

/** Note a late addition to the run's stored record. Never throws, never waits. */
function noteKeepsake(keepsake) {
  try {
    if (statsRef && typeof statsRef.annotateLast === 'function') {
      statsRef.annotateLast({ keepsake });
    }
  } catch (_e) { /* the archive is never allowed to interrupt the ceremony */ }
}

/**
 * The name C# will give the session drafted from this run.
 *
 * MIRROR, NOT A READ-BACK. The host does not tell the page what it drafted: the
 * `quiz-result` message is one-way and IntakeHostService replies with nothing.
 * The name is however fully deterministic from data the page already holds —
 * QuizSessionGenerator.GetFallbackContent(niche, scorePercent) picks a prefix
 * from the score and a noun from the niche, and GenerateSession only fills the
 * name in when the content block left it blank (it always does, absent AI text).
 * So this recomputes the same two inputs rather than inventing a placeholder. If
 * the C# naming rule ever changes, this drifts — hence `derived: true`, and the
 * archive prints it as "drafted as", never as a filename.
 */
const SESSION_NOUNS = Object.freeze({
  sissy: 'Feminization',
  bambi: 'Bambi Training',
  circe: 'Keyholding',
  obedience: 'Obedience Training',
  mindlessness: 'Mindless Bliss',
  submission: 'Submission',
});
function deriveSessionName(result) {
  const niche = String((result && result.niche) || '').trim().toLowerCase();
  const maxScore = (result && typeof result.maxScore === 'number') ? result.maxScore : 0;
  const pct = maxScore > 0
    ? (result.totalScore / maxScore) * 100
    : clamp01((result && result.peakDepth) || 0) * 100;
  const prefix = pct <= 25 ? 'Gentle' : pct <= 50 ? 'Guided' : pct <= 75 ? 'Intense' : 'Extreme';
  return `${prefix} ${SESSION_NOUNS[niche] || 'Conditioning'}`;
}

/** Assemble the recap extras for stats.record(). Pure read — no side effects. */
function buildRecapExtras(result, ctx) {
  const { theme, bank, subjectId } = ctx || {};
  const route = (result && result.route) || {};
  const primary = findArchetype(bank, route.primaryArchetypeId);
  const secondary = findArchetype(bank, route.secondaryArchetypeId);
  return {
    grade: gradeFor(result),
    subject: formatSubject(theme || {}, subjectId),
    sectionTitle: sectionShortName(theme || {}, result.deepestBand) || prettyId(result.deepestBand),
    classification: {
      primaryName: (primary && primary.name) || prettyId(route.primaryArchetypeId) || result.niche || '',
      primaryShare: clamp01(route.primaryShare || 0),
      primaryBlurb: (primary && primary.blurb) || '',
      secondaryName: route.secondaryArchetypeId
        ? ((secondary && secondary.name) || prettyId(route.secondaryArchetypeId)) : '',
      secondaryShare: clamp01(route.secondaryShare || 0),
      hue: (primary && typeof primary.hue === 'number') ? primary.hue : null,
    },
    session: {
      // shim.isHosted is exactly what emitResult's ack reports, minus the ordering
      // dependency (this is assembled before the emit).
      delivered: shim.isHosted ? 'host' : 'local',
      name: shim.isHosted ? deriveSessionName(result) : '',
      derived: true,
    },
    colors: harvestedColors(),
    spirals: recordedSpirals(),
    spiralsRolled: spiralCount(),
    media: recordedMedia(),
    mediaShown: mediaShownCount(),
  };
}

/* Scoped outro-recap CSS, injected once at run-end (see ensureOutroCss). Kept as
 * a string constant so importing boot.js headlessly stays inert — nothing here
 * touches the DOM until runOutro runs. Selectors are scoped under `.intake-outro`
 * / `.ix-recap` so they layer cleanly over styles.css (this <style> is appended
 * after the linked sheet, so equal-specificity rules here win). The recap is a
 * compact certificate that fits ~100vh, with an internally-scrollable frame as a
 * hard safety net so nothing is ever silently clipped on short windows. */
const IX_OUTRO_CSS = `
/* wider column + the scroll frame that guarantees nothing clips */
.intake-outro { position: relative; width: min(780px, 94vw); }
.ix-outro-scroll {
  max-height: calc(100dvh - 28px);
  overflow-y: auto; overflow-x: hidden;
  padding: 6px 14px 34px;
  scrollbar-width: thin;
  scrollbar-color: rgba(176,108,255,.5) transparent;
}
.ix-outro-scroll::-webkit-scrollbar { width: 10px; }
.ix-outro-scroll::-webkit-scrollbar-track { background: transparent; }
.ix-outro-scroll::-webkit-scrollbar-thumb {
  background: rgba(176,108,255,.45); border-radius: 6px;
  border: 2px solid transparent; background-clip: padding-box;
}
.ix-outro-scroll::-webkit-scrollbar-thumb:hover {
  background: rgba(176,108,255,.72); background-clip: padding-box;
}

/* certificate = single fitted document; gap owns the rhythm, empty slots vanish
   so the staged reveal never leaves phantom gaps */
.ix-recap { display: flex; flex-direction: column; gap: clamp(12px, 2.2vh, 20px); }
.ix-recap .intake-outro-block { margin: 0; }
.ix-recap-slot:empty, .ix-recap-headrow:empty, .ix-recap-lines:empty { display: none; }
.ix-recap .intake-outro-lines { margin: 0; }
.ix-recap .intake-outro-lines .intake-typeline { font-size: clamp(16px, 2.4vh, 22px); }

/* grade + classification: two columns on wide viewports, stacked when narrow */
.ix-recap-headrow {
  display: grid; grid-template-columns: auto minmax(0, 1fr);
  align-items: center; gap: clamp(16px, 3vw, 34px); text-align: left;
}
.ix-recap-headrow .intake-gradeblock { text-align: center; }
.ix-recap-headrow .intake-grade-letter { font-size: clamp(64px, 15vh, 118px); }
.ix-recap-headrow .intake-routeblock { margin: 0; }
.ix-recap .intake-route-name { font-size: clamp(26px, 4vh, 38px); line-height: 1.05; }
.ix-recap .intake-route-blurb { font-size: clamp(14px, 2vh, 18px); margin-top: 8px; }

/* stats: tight 3-up */
.ix-recap .intake-statsblock { gap: clamp(14px, 3vw, 30px); padding: 2px 0; }
.ix-recap .intake-stat-value { font-size: clamp(20px, 3.2vh, 28px); }

/* recorded statements: compact italic lines */
.ix-recap-stmts { text-align: center; }
.ix-recap .intake-mantra { font-size: clamp(15px, 2.2vh, 20px); margin: 3px 0; }

/* record card: translucent doc, paperwork rows in two columns to save height */
.ix-recap-record { display: flex; justify-content: center; }
.ix-recap .intake-cert {
  margin: 0; width: min(600px, 100%);
  padding: clamp(14px, 2.4vh, 22px) clamp(16px, 3vw, 26px);
  background: rgba(37, 37, 66, 0.5);
}
.ix-recap .intake-cert-head, .ix-recap .intake-cert-title { padding-right: 76px; }
.ix-recap .intake-cert-title { font-size: clamp(18px, 3vh, 24px); margin-bottom: 10px; }
.ix-cert-rows { display: grid; grid-template-columns: 1fr 1fr; gap: 0 clamp(18px, 3vw, 30px); }
.ix-cert-rows .intake-cert-row { font-size: clamp(14px, 2vh, 18px); }
.ix-recap .intake-cert-seal {
  top: clamp(14px, 2.2vh, 20px); bottom: auto; right: clamp(16px, 3vw, 22px);
  width: 64px; height: 64px;
}

/* THE WEAVE — the run's own spirals, re-rendered from their recorded params,
   plus the colour harvest that fed them. Reads as an inlay strip on the record,
   not a control panel: tiles are small, the actions only appear on hover/focus. */
.ix-weaveblock { text-align: center; }
.ix-palette-row {
  display: flex; flex-wrap: wrap; justify-content: center;
  gap: 8px clamp(10px, 2vw, 18px); margin: 6px 0 4px;
}
.ix-palette-chip {
  display: inline-flex; align-items: center; gap: 7px;
  font-size: clamp(11px, 1.6vh, 13px); letter-spacing: .12em; text-transform: uppercase;
  color: rgba(238, 232, 255, .78);
}
.ix-palette-dot {
  width: 13px; height: 13px; border-radius: 50%;
  border: 1px solid rgba(255, 255, 255, .3);
  box-shadow: 0 0 9px var(--ix-chip, rgba(255,105,180,.7));
  background: var(--ix-chip, #ff69b4);
}
.ix-weave-grid {
  display: grid; justify-content: center; gap: clamp(10px, 1.8vw, 16px);
  grid-template-columns: repeat(auto-fit, minmax(104px, 118px));
  margin-top: clamp(8px, 1.6vh, 14px);
}
.ix-weave-tile {
  position: relative; margin: 0; border-radius: 10px; overflow: hidden;
  background: rgba(20, 16, 31, .72);
  border: 1px solid rgba(176, 108, 255, .28);
  box-shadow: 0 0 16px rgba(176, 108, 255, .12);
  opacity: 0; transform: translateY(6px);
  transition: opacity .5s ease, transform .5s ease, box-shadow .3s ease;
}
.ix-weave-tile.is-shown { opacity: 1; transform: none; }
.ix-weave-tile:hover, .ix-weave-tile:focus-within {
  box-shadow: 0 0 22px rgba(255, 105, 180, .3);
}
.ix-weave-canvas { display: block; width: 100%; height: auto; aspect-ratio: 1 / 1; }
.ix-weave-cap {
  font-size: 10px; letter-spacing: .18em; text-transform: uppercase;
  color: rgba(238, 232, 255, .55); padding: 5px 0 6px;
}
/* actions: hidden until the tile is hovered/focused so the strip stays calm */
.ix-weave-acts {
  position: absolute; left: 0; right: 0; bottom: 0;
  display: flex; opacity: 0; transition: opacity .22s ease;
  background: linear-gradient(to top, rgba(12, 9, 20, .95), rgba(12, 9, 20, .72));
}
.ix-weave-tile:hover .ix-weave-acts,
.ix-weave-tile:focus-within .ix-weave-acts { opacity: 1; }
.ix-weave-act {
  flex: 1 1 0; min-width: 0; cursor: pointer;
  font: inherit; font-size: 10px; letter-spacing: .1em; text-transform: uppercase;
  padding: 7px 2px; color: rgba(238, 232, 255, .9);
  background: none; border: 0; border-top: 1px solid rgba(176, 108, 255, .3);
}
.ix-weave-act + .ix-weave-act { border-left: 1px solid rgba(176, 108, 255, .3); }
.ix-weave-act:hover:not(:disabled) { color: var(--intake-accent, #ff69b4); }
.ix-weave-act:disabled { opacity: .45; cursor: default; }
.ix-weave-status {
  min-height: 1.2em; margin-top: clamp(6px, 1.2vh, 10px);
  font-size: clamp(12px, 1.8vh, 14px); color: rgba(238, 232, 255, .72);
}
.ix-weave-status.is-bad { color: #ff8ba0; }
.ix-weave-note {
  font-size: clamp(11px, 1.6vh, 13px); letter-spacing: .08em;
  color: rgba(238, 232, 255, .45); margin-top: 4px;
}
@media (prefers-reduced-motion: reduce) {
  .ix-weave-tile { transition: none !important; opacity: 1; transform: none; }
  .ix-weave-acts { transition: none !important; opacity: 1; }
}

/* scroll affordances — only while overflowing; no motion under reduced-motion */
.ix-scroll-fade, .ix-scroll-hint { opacity: 0; pointer-events: none; }
.intake-outro.is-scrollable .ix-scroll-fade {
  opacity: 1; position: absolute; left: 0; right: 0; bottom: 0; height: 56px;
  border-radius: 0 0 14px 14px;
  background: linear-gradient(to bottom, rgba(20,16,31,0), rgba(20,16,31,.92) 92%);
  transition: opacity .3s ease;
}
.intake-outro.is-scrollable .ix-scroll-hint {
  opacity: .82; position: absolute; left: 50%; bottom: 8px; transform: translateX(-50%);
  font-size: 22px; line-height: 1; color: var(--intake-accent-2);
  text-shadow: 0 0 10px rgba(176,108,255,.6);
}
.intake-outro.is-scrollable.at-bottom .ix-scroll-fade,
.intake-outro.is-scrollable.at-bottom .ix-scroll-hint { opacity: 0; }

@media (max-width: 640px) {
  .ix-recap-headrow { grid-template-columns: 1fr; text-align: center; }
  .ix-cert-rows { grid-template-columns: 1fr; }
  .ix-recap .intake-cert-head, .ix-recap .intake-cert-title { padding-right: 0; }
  .ix-recap .intake-cert-seal { display: none; }
}
@media (prefers-reduced-motion: reduce) {
  .ix-scroll-fade { transition: none !important; }
}`;

/** Inject the scoped outro CSS once. No-op headlessly (guarded on `doc`). */
function ensureOutroCss() {
  if (!doc || doc.getElementById('ix-outro-css')) return;
  const s = doc.createElement('style');
  s.id = 'ix-outro-css';
  s.textContent = IX_OUTRO_CSS;
  (doc.head || doc.body || doc.documentElement).appendChild(s);
}

async function runOutro(result, ack, ctx) {
  const { theme, bank, config, subjectId } = ctx;
  if (!dom.stage) return;
  ensureOutroCss();
  hideOverlay();
  teardownCounter();                    // idempotent; the HUD fades out below
  if (dom.hud) dom.hud.classList.add('is-gone');
  if (dom.aside) dom.aside.classList.remove('is-on');

  dom.stage.innerHTML = '';
  const root = el('div', 'intake-outro');
  dom.stage.appendChild(root);

  // The whole recap lives in an internally-scrollable frame so it NEVER silently
  // clips on short windows; the compact certificate below fits ~100vh on normal
  // viewports and only scrolls when it can't.
  const scroll = el('div', 'ix-outro-scroll');
  root.appendChild(scroll);
  const cer = el('div', 'intake-ceremony ix-recap');
  scroll.appendChild(cer);

  // Pre-built layout slots decouple reveal *timing* (staged) from visual *order*
  // (compact document): grade + classification share a row, stats are a tight
  // 3-up, statements + the record card follow. Empty slots collapse (CSS) so the
  // staged reveal leaves no phantom gaps.
  const slotLines  = el('div', 'ix-recap-lines');
  const slotHead   = el('div', 'ix-recap-headrow');
  const slotStats  = el('div', 'ix-recap-slot ix-recap-stats');
  const slotStmts  = el('div', 'ix-recap-slot ix-recap-stmts');
  const slotWeave  = el('div', 'ix-recap-slot ix-recap-weave');
  const slotRecord = el('div', 'ix-recap-slot ix-recap-record');
  cer.append(slotLines, slotHead, slotStats, slotStmts, slotWeave, slotRecord);

  // Scroll affordances: bottom fade + chevron, shown only when there's more below
  // (toggled via .is-scrollable / .at-bottom). Purely CSS opacity; RM-safe.
  const fade = el('div', 'ix-scroll-fade');
  const hint = el('div', 'ix-scroll-hint', '⌄');
  root.append(fade, hint);
  const syncScrollHint = () => {
    const more = scroll.scrollHeight - scroll.clientHeight;
    root.classList.toggle('is-scrollable', more > 6);
    root.classList.toggle('at-bottom', scroll.scrollTop >= (more - 8));
  };
  scroll.addEventListener('scroll', syncScrollHint);

  const fastMul = config.m2Test ? 0.05 : 1;
  const wait = (ms) => clickSkippableWait(ms * fastMul, root);

  // -- a. closing lines ----------------------------------------------------
  const state = { fast: false };
  root.addEventListener('click', () => { state.fast = true; }, { once: true });
  const linesBox = el('div', 'intake-outro-lines');
  slotLines.appendChild(linesBox);
  let lines = theme.outroLines.slice();
  if (!/assessment complete/i.test(lines[0] || '')) lines.unshift('Assessment complete.');
  await typeLines(linesBox, lines, { state, cps: 40 });
  linesBox.classList.add('is-ready'); // retire the caret
  await wait(700);

  // -- b. grade card -------------------------------------------------------
  const letter = gradeFor(result);
  const gradeBlock = el('div', 'intake-outro-block intake-gradeblock');
  gradeBlock.appendChild(el('div', 'intake-outro-label', 'Composite grade'));
  gradeBlock.appendChild(el('div', 'intake-grade-letter grade-' + letter, letter));
  slotHead.appendChild(gradeBlock);
  syncScrollHint();
  await wait(1700);

  // -- c. route reveal -----------------------------------------------------
  const route = result.route || {};
  const primary = findArchetype(bank, route.primaryArchetypeId);
  const secondary = findArchetype(bank, route.secondaryArchetypeId);
  const primaryName = (primary && primary.name) || prettyId(route.primaryArchetypeId) || result.niche || '—';
  const routeBlock = el('div', 'intake-outro-block intake-routeblock');
  if (primary && typeof primary.hue === 'number') {
    routeBlock.classList.add('is-hued');
    routeBlock.style.setProperty('--outro-hue', String(primary.hue));
  }
  routeBlock.appendChild(el('div', 'intake-outro-label', 'Primary classification'));
  routeBlock.appendChild(el('div', 'intake-route-name', primaryName));
  const share = Math.round(clamp01(route.primaryShare || 0) * 100);
  if (share > 0) routeBlock.appendChild(el('div', 'intake-route-share', `${share}% expression`));
  if (primary && primary.blurb) routeBlock.appendChild(el('p', 'intake-route-blurb', primary.blurb));
  if (route.secondaryArchetypeId) {
    const secName = (secondary && secondary.name) || prettyId(route.secondaryArchetypeId);
    const secShare = Math.round(clamp01(route.secondaryShare || 0) * 100);
    routeBlock.appendChild(el('div', 'intake-route-secondary',
      `Secondary: ${secName}` + (secShare > 0 ? ` · ${secShare}%` : '')));
  }
  slotHead.appendChild(routeBlock);
  syncScrollHint();
  await wait(2000);

  // -- d. recorded statements ---------------------------------------------
  const mantras = Array.isArray(result.affirmedMantras) ? result.affirmedMantras.filter(Boolean) : [];
  if (mantras.length) {
    const mb = el('div', 'intake-outro-block intake-mantrablock');
    mb.appendChild(el('div', 'intake-outro-label', 'Recorded statements'));
    const ul = el('ul', 'intake-mantras');
    mb.appendChild(ul);
    slotStmts.appendChild(mb);
    for (const m of mantras.slice(0, 8)) {
      const li = el('li', 'intake-mantra', '“' + String(m) + '”');
      ul.appendChild(li);
      await sleep(30);
      li.classList.add('is-shown');
      await wait(520);
    }
    await wait(600);
  }

  // -- e. stats ------------------------------------------------------------
  const qCount = Array.isArray(result.trajectory) ? result.trajectory.length : 0;
  const statsBlock = el('div', 'intake-outro-block intake-statsblock');
  const addStat = (label, value) => {
    const cell = el('div', 'intake-stat');
    cell.appendChild(el('div', 'intake-stat-value', value));
    cell.appendChild(el('div', 'intake-stat-label', label));
    statsBlock.appendChild(cell);
  };
  addStat('Susceptibility index', `${Math.round(clamp01(result.peakDepth || 0) * 100)}%`);
  addStat('Deepest section', sectionShortName(theme, result.deepestBand) || prettyId(result.deepestBand) || '—');
  addStat('Questions answered', String(qCount));
  slotStats.appendChild(statsBlock);
  syncScrollHint();
  await wait(1800);

  // -- e2. THE WEAVE: this run's own spirals + the colours that made them ---
  // Never allowed to cost the ceremony: a failure here logs and moves on.
  try { await mountSpiralRecap(slotWeave, { config, wait, syncScrollHint }); }
  catch (e) { shim.log('spiral recap failed: ' + (e && e.message || e)); }

  // -- f. the report-card artifact (folded in as the fitted record footer) --
  const cert = el('div', 'intake-cert');
  if (primary && typeof primary.hue === 'number') {
    cert.classList.add('is-hued');
    cert.style.setProperty('--outro-hue', String(primary.hue));
  }
  cert.appendChild(el('div', 'intake-cert-head', `${PRODUCT_NAME} · Assessment Record`));
  cert.appendChild(el('h3', 'intake-cert-title', 'Cognitive Response Assessment'));
  const rowsWrap = el('div', 'ix-cert-rows');
  const row = (label, value) => {
    const r = el('div', 'intake-cert-row');
    r.appendChild(el('span', 'intake-cert-k', label));
    r.appendChild(el('span', 'intake-cert-v', value));
    rowsWrap.appendChild(r);
  };
  row('Subject', formatSubject(theme, subjectId));
  row('Classification', primaryName + (share > 0 ? ` (${share}%)` : ''));
  row('Grade', letter);
  row('Susceptibility', `${Math.round(clamp01(result.peakDepth || 0) * 100)}%`);
  row('Date', new Date().toLocaleDateString());
  cert.appendChild(rowsWrap);
  const seal = el('div', 'intake-cert-seal');
  seal.appendChild(el('span', null, 'CRA'));
  seal.appendChild(el('span', 'intake-cert-seal-sub', 'CERTIFIED'));
  cert.appendChild(seal);
  const handoff = ack.delivered === 'host' ? 'Session drafting…' : 'Saved locally.';
  cert.appendChild(el('div', 'intake-cert-handoff', handoff));
  if (config.hosted) {
    const exit = el('button', 'intake-begin intake-cert-exit', 'Return to the Lab');
    exit.addEventListener('click', () => { try { shim.send({ type: 'exit' }); } catch (_e) {} });
    cert.appendChild(exit);
  }
  slotRecord.appendChild(cert);
  await sleep(30);
  cert.classList.add('is-shown');
  syncScrollHint();
}

/* ----------------------------------------------------------------------------
 * THE WEAVE — the outro's spiral recap.
 *
 * core/spiralLog.js kept the params of every spiral the run mounted, and those
 * spirals were woven from the colours the player named in Calibration
 * (core/palette.js). Here they come back: the harvested swatches as chips, then
 * the spirals themselves re-rendered live from their recorded params, with two
 * host-backed actions per tile (keep it in THE LOOM · save it as a PNG).
 *
 * PERF CALL — ONE WebGL context, ONE rAF, ~12fps, 128px tiles.
 *   Eight independent live spirals the way beats.js mounts them would be eight
 *   WebGL contexts (the browser caps ~16 and evicts the oldest) on top of eight
 *   rAF loops at viewport scale — a guaranteed WebView2 stall on the one screen
 *   the player is meant to linger on. Instead a SINGLE 128x128 offscreen field
 *   renderer is shared: the loop wakes at ~12fps, renders each tile's params
 *   into that one field and blits it into the tile's 2D canvas. That is ~131k
 *   shaded pixels per wake for the whole gallery (one fullscreen spiral is ~1M
 *   per frame at 60fps), so the gallery costs a fraction of a single in-run
 *   spiral while still moving. Under reduced motion / a low masterIntensity cap
 *   / no WebGL, each tile is painted ONCE as a static frame and no loop starts.
 *   The loop self-stops the moment the gallery leaves the DOM.
 * -------------------------------------------------------------------------- */

/** Live prefers-reduced-motion probe (called at render time, never at import). */
function prefersReducedMotion() {
  try {
    return typeof window !== 'undefined' && !!window.matchMedia
      && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  } catch (_e) { return false; }
}

/** hex -> the bank's own colour word ('#ff69b4' -> 'pink'), or the hex itself. */
function swatchName(hex) {
  const h = String(hex || '').toLowerCase();
  for (const k of Object.keys(COLOR_SWATCHES)) if (COLOR_SWATCHES[k] === h) return k;
  return h;
}

const WEAVE_TILE_PX = 128;   // shared field + tile backing store (CSS upscales)
const WEAVE_FPS = 12;        // gallery wake rate (see the perf call above)

async function mountSpiralRecap(slot, ctx) {
  const { config, wait, syncScrollHint } = ctx;
  const spirals = recordedSpirals();
  const colors = harvestedColors();
  // Nothing woven (a very shallow run, or no colour beats at all) -> no block;
  // the empty slot collapses via CSS and the ceremony reads exactly as before.
  if (!spirals.length) return;

  const block = el('div', 'intake-outro-block ix-weaveblock');
  block.appendChild(el('div', 'intake-outro-label', 'Woven from your colours'));

  if (colors.length) {
    const row = el('div', 'ix-palette-row');
    for (const hex of colors) {
      const chip = el('span', 'ix-palette-chip');
      const dot = el('span', 'ix-palette-dot');
      dot.style.setProperty('--ix-chip', hex);
      chip.append(dot, el('span', null, swatchName(hex)));
      row.appendChild(chip);
    }
    block.appendChild(row);
  }

  const grid = el('div', 'ix-weave-grid');
  block.appendChild(grid);
  const status = el('div', 'ix-weave-status');
  block.appendChild(status);
  const total = spiralCount();
  if (total > spirals.length) {
    block.appendChild(el('div', 'ix-weave-note', `${spirals.length} of ${total} kept from your descent.`));
  }
  slot.appendChild(block);

  // The loom module is the same one the in-run spirals render through. A failed
  // import (no such thing in the hosted build, but the harness can be served
  // from anywhere) simply retires the whole block.
  let m = null;
  try { m = await import('../dtrh/shared/loomField.js'); }
  catch (e) {
    shim.log('weave: loom import failed: ' + (e && e.message || e));
    try { block.remove(); } catch (_e) {}
    return;
  }

  const reduced = prefersReducedMotion();
  const master = (config.caps && config.caps.masterIntensity != null) ? config.caps.masterIntensity : 1;
  const animate = !reduced && master >= 0.35 && !config.m2Test;

  // ONE shared offscreen field for every tile (the perf call). Null = no WebGL,
  // in which case each tile paints the v1 2D fallback the in-run spirals use.
  let field = null;
  try {
    const off = doc.createElement('canvas');
    off.width = WEAVE_TILE_PX; off.height = WEAVE_TILE_PX;
    field = m.createFieldRenderer(off);
  } catch (_e) { field = null; }

  const tiles = [];
  spirals.forEach((entry, i) => {
    let q;
    try { q = m.normalizeParams2(entry.params); } catch (_e) { return; }
    const fig = el('figure', 'ix-weave-tile');
    const canvas = doc.createElement('canvas');
    canvas.className = 'ix-weave-canvas';
    canvas.width = WEAVE_TILE_PX; canvas.height = WEAVE_TILE_PX;
    const c2d = canvas.getContext('2d');
    if (!c2d) return;
    fig.appendChild(canvas);
    fig.appendChild(el('figcaption', 'ix-weave-cap', 'No. ' + (i + 1)));
    // Stagger the starting phases so the strip never pulses in lockstep.
    const tile = { q, ctx: c2d, span: Math.max(200, m.loopMs2(q)), offset: i / spirals.length };
    tiles.push(tile);
    if (shim.isHosted) fig.appendChild(buildWeaveActions(m, q, i, status, grid));
    grid.appendChild(fig);
    setTimeout(() => { try { fig.classList.add('is-shown'); } catch (_e) {} }, 60 + i * 70);
  });

  if (!tiles.length) { try { block.remove(); } catch (_e) {} return; }

  const paint = (tile, phase) => {
    try {
      if (field) m.composeFrame(tile.ctx, field, tile.q, phase, WEAVE_TILE_PX, WEAVE_TILE_PX);
      else m.drawFallbackFrame(tile.ctx, tile.q, phase, WEAVE_TILE_PX, WEAVE_TILE_PX);
    } catch (_e) { /* one bad tile must not stop the gallery */ }
  };

  // Static first frame either way, so a stopped/reduced gallery is never blank.
  for (const t of tiles) paint(t, t.offset);

  if (animate && typeof requestAnimationFrame === 'function') {
    const t0 = performance.now();
    let last = -1e9;
    const step = (now) => {
      // Self-stop: the outro is the last thing on the stage, but an abort/close
      // can pull it out from under us mid-loop.
      if (!grid.isConnected) {
        // release the one shared context rather than leave it pinned
        try { if (field) { const lose = field.gl.getExtension('WEBGL_lose_context'); if (lose) lose.loseContext(); } }
        catch (_e) { /* best-effort */ }
        return;
      }
      if (now - last >= 1000 / WEAVE_FPS) {
        last = now;
        for (const t of tiles) paint(t, (((now - t0) / t.span) + t.offset) % 1);
      }
      requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  }

  if (typeof syncScrollHint === 'function') syncScrollHint();
  await wait(1400);
}

/* ----------------------------------------------------------------------------
 * THE TWO ACTIONS (hosted only — the web layer cannot write files).
 *
 *   KEEP IT  -> weave a GIF off-thread with the Loom's own encoder
 *               (/dtrh/engine/loomWorker.js) and hand it to the host as a
 *               `loom-save`, byte-for-byte the message THE LOOM sends. The
 *               recorded params ARE schema-v2 loom params (they came out of
 *               loomField.randomParams2), so the sidecar round-trips: the saved
 *               spiral opens in the Loom editor exactly as it looked here.
 *   SAVE PNG -> render one crisp frame at PNG_PX and hand the bytes to the host
 *               as `intake-save-image`; C# writes it under the user data folder.
 *
 * Only ONE job runs at a time (a GIF encode is seconds of worker CPU): every
 * button on the strip disables for the duration and the shared status line
 * narrates. Standalone (harness.html / a browser) `shim.isHosted` is false and
 * these buttons are never built at all.
 * -------------------------------------------------------------------------- */
const PNG_PX = 1024;

let weaveWorker = null;    // lazily built loomWorker (GIF encoder)
let weaveJobId = 0;
let weavePending = null;   // { id, resolve } for the in-flight encode
let weaveBusy = false;
let weaveHostWired = false;
let hostReply = null;      // { type, resolve } for the in-flight host round-trip

/** Register the two host replies once. shim.on keeps ONE handler per type. */
function ensureWeaveHostWiring() {
  if (weaveHostWired || !shim.isHosted) return;
  weaveHostWired = true;
  const settle = (type, msg) => {
    if (hostReply && hostReply.type === type) { const r = hostReply; hostReply = null; r.resolve(msg); }
  };
  shim.on('loom-result', (m) => settle('loom-result', m));
  shim.on('intake-save-image-result', (m) => settle('intake-save-image-result', m));
}

/** Send `msg` and await the host's `replyType` (or time out). */
function askHost(msg, replyType, timeoutMs) {
  ensureWeaveHostWiring();
  return new Promise((resolve) => {
    let done = false;
    const finish = (m) => { if (!done) { done = true; resolve(m); } };
    hostReply = { type: replyType, resolve: finish };
    setTimeout(() => { if (hostReply && hostReply.resolve === finish) hostReply = null; finish(null); },
      Math.max(1000, timeoutMs | 0));
    try { shim.send(msg); } catch (_e) { finish(null); }
  });
}

/** ArrayBuffer -> base64, chunked so a multi-MB GIF can't blow the arg limit. */
function b64FromBuffer(buf) {
  const bytes = new Uint8Array(buf);
  let s = '';
  for (let i = 0; i < bytes.length; i += 0x8000) {
    s += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
  }
  return btoa(s);
}

/** Encode `q` to a GIF with the Loom's own worker. Resolves null on failure. */
function weaveGif(q, onProgress) {
  return new Promise((resolve) => {
    try {
      if (!weaveWorker) {
        weaveWorker = new Worker('/dtrh/engine/loomWorker.js', { type: 'module' });
        weaveWorker.onmessage = (e) => {
          const msg = e.data || {};
          if (!weavePending || msg.id !== weavePending.id) return;
          if (msg.progress != null) { try { weavePending.onProgress(msg.progress); } catch (_e) {} return; }
          const p = weavePending; weavePending = null;
          p.resolve(msg.gif ? b64FromBuffer(msg.gif) : null);
        };
        weaveWorker.onerror = () => {
          const p = weavePending; weavePending = null;
          if (p) p.resolve(null);
        };
      }
      const id = ++weaveJobId;
      weavePending = { id, resolve, onProgress: onProgress || (() => {}) };
      weaveWorker.postMessage({ id, params: q });
    } catch (_e) { resolve(null); }
  });
}

/** The two buttons for one tile. `status` is the block's shared status line. */
function buildWeaveActions(m, q, index, status, grid) {
  const acts = el('div', 'ix-weave-acts');
  const keep = el('button', 'ix-weave-act', 'Keep it');
  const save = el('button', 'ix-weave-act', 'Save PNG');
  keep.type = 'button'; save.type = 'button';
  acts.append(keep, save);

  const say = (text, bad) => {
    status.textContent = text;
    status.classList.toggle('is-bad', !!bad);
  };
  const setBusy = (on) => {
    weaveBusy = on;
    const all = grid.querySelectorAll('.ix-weave-act');
    for (const b of all) b.disabled = on;
  };

  keep.addEventListener('click', async () => {
    if (weaveBusy) return;
    setBusy(true);
    say('weaving it into the loom…');
    try {
      const gifBase64 = await weaveGif(q, (p) => say(`weaving it into the loom… ${Math.round(p * 100)}%`));
      if (!gifBase64) { say('the thread snapped. try another.', true); return; }
      // Slug rules are C#-authoritative (DtrhLoomStore): [a-z0-9_-]{1,24}.
      const name = `intake-${Date.now().toString(36).slice(-6)}-${index + 1}`;
      const res = await askHost({ type: 'loom-save', name, params: q, gifBase64, overwrite: false },
        'loom-result', 15000);
      if (res && res.ok) {
        say(`kept as "${res.slug}" — it's in your Spirals library now.`);
        // ...and the archive's file for this run says which one you kept.
        noteKeepsake({ kind: 'loom', index: index + 1, label: String(res.slug || name) });
      }
      else if (res && res.error === 'cap-reached') say('the loom rack is full — forget one in The Loom first.', true);
      else if (res && res.error === 'too-big') say('that weave was too heavy to hang. try a simpler one.', true);
      else say('the loom refused it' + (res && res.error ? ' (' + res.error + ')' : '') + '.', true);
    } catch (e) {
      say('the loom refused it.', true);
      shim.log('weave keep failed: ' + (e && e.message || e));
    } finally { setBusy(false); }
  });

  save.addEventListener('click', async () => {
    if (weaveBusy) return;
    setBusy(true);
    say('printing it…');
    try {
      const png = renderSpiralPng(m, q);
      if (!png) { say('could not print that one.', true); return; }
      const res = await askHost({ type: 'intake-save-image', pngBase64: png, index: index + 1 },
        'intake-save-image-result', 15000);
      if (res && res.ok) {
        say('saved to ' + (res.path || 'your user data folder') + '.');
        noteKeepsake({ kind: 'png', index: index + 1, label: String(res.path || '') });
      }
      else say('could not save it' + (res && res.error ? ' (' + res.error + ')' : '') + '.', true);
    } catch (e) {
      say('could not print that one.', true);
      shim.log('weave save failed: ' + (e && e.message || e));
    } finally { setBusy(false); }
  });

  return acts;
}

/**
 * One crisp still of `q` at PNG_PX, base64 (no data: prefix). Its own throwaway
 * WebGL context — the gallery's shared field is only WEAVE_TILE_PX, and this
 * runs once per click. The context is released immediately so the gallery's
 * long-lived one is never the browser evicts.
 */
function renderSpiralPng(m, q) {
  let out = null, gl = null;
  try {
    const target = doc.createElement('canvas');
    target.width = PNG_PX; target.height = PNG_PX;
    const c2d = target.getContext('2d');
    if (!c2d) return null;
    let big = null;
    try {
      const off = doc.createElement('canvas');
      off.width = PNG_PX; off.height = PNG_PX;
      big = m.createFieldRenderer(off);
    } catch (_e) { big = null; }
    if (big) { gl = big.gl; m.composeFrame(c2d, big, q, 0, PNG_PX, PNG_PX); }
    else m.drawFallbackFrame(c2d, q, 0, PNG_PX, PNG_PX);
    out = target.toDataURL('image/png').replace(/^data:image\/png;base64,/, '');
  } catch (_e) { out = null; }
  try { if (gl) { const lose = gl.getExtension('WEBGL_lose_context'); if (lose) lose.loseContext(); } }
  catch (_e) { /* best-effort */ }
  return out;
}

/** Last-ditch plain results card if the ceremony itself throws. */
function showDoneFallback(result, ack) {
  if (!dom.stage) return;
  const where = ack.delivered === 'host' ? 'Session drafting…' : 'Saved locally.';
  dom.stage.innerHTML = `<div class="intake-done">
    <h2>${PRODUCT_NAME} complete</h2>
    <p>Route: ${result.route.primaryArchetypeId || result.niche}</p>
    <p>${where}</p></div>`;
}

/* ----------------------------------------------------------------------------
 * PHASE-0 STUB RENDERER + null-object modules.
 * Replaced the instant each Agent's real module lands (no boot change needed).
 * The stub render is intentionally plain — it proves the loop, not the feel.
 * -------------------------------------------------------------------------- */
function stubRenderBeat(beat, ctx) {
  return new Promise((resolve) => {
    const t0 = performance.now();
    const stage = dom.stage;
    stage.innerHTML = '';
    const card = document.createElement('div');
    card.className = 'intake-beat';
    const q = document.createElement('p');
    q.className = 'intake-q';
    q.textContent = beat.prompt.text + (beat.prompt.flavor ? ` (${beat.prompt.flavor})` : '');
    card.appendChild(q);

    const commit = (chosenIndex, value, steered) => {
      const ev = { latencyMs: (performance.now() - t0) | 0, mechanic: beat.mechanic, timedOut: false, steered: !!steered };
      if (typeof chosenIndex === 'number') ev.chosenIndex = chosenIndex;
      if (value !== undefined) ev.value = value;
      // fire a reward through reward->effects/audio so the depth stack is exercised
      const opt = beat.options[chosenIndex];
      const scoreDelta = opt ? opt.score : (value === true ? 1 : 0);
      const rew = ctx.reward && ctx.reward.resolve
        ? ctx.reward.resolve(beat.rewardPlan, ev, scoreDelta)
        : { fire: beat.rewardPlan.baseChance > 0.5, intensity: beat.depth, kind: beat.rewardPlan.kind, decoupled: beat.rewardPlan.mode !== 'honest' };
      try { ctx.effects.play && ctx.effects.play(rew, beat.depth); } catch (_e) {}
      try { ctx.audio.chime && ctx.audio.chime(rew); } catch (_e) {}
      resolve(ev);
    };

    if (beat.options && beat.options.length) {
      const opts = document.createElement('div');
      opts.className = 'intake-opts';
      beat.options.forEach((o) => {
        const b = document.createElement('button');
        b.className = 'intake-opt' + (o.isCorrect ? ' is-correct' : '');
        b.textContent = o.label;
        b.addEventListener('click', () => commit(o.index, undefined, false));
        opts.appendChild(b);
      });
      card.appendChild(opts);
      // hand options to steering so Agent D's module can be exercised via the stub too
      exerciseSteering(ctx, beat, card, opts);
    } else if (beat.mechanic === 'checkin') {
      const slider = document.createElement('input');
      slider.type = 'range'; slider.min = '0'; slider.max = '100'; slider.value = '50';
      const go = document.createElement('button'); go.textContent = 'OK'; go.className = 'intake-opt';
      go.addEventListener('click', () => commit(undefined, +slider.value / 100, false));
      card.appendChild(slider); card.appendChild(go);
    } else {
      // mantra / free input: a text box
      const inp = document.createElement('input');
      inp.type = 'text'; inp.placeholder = beat.prompt.answer ? 'type it…' : '';
      const go = document.createElement('button'); go.textContent = 'Say it'; go.className = 'intake-opt';
      go.addEventListener('click', () => commit(undefined, inp.value, false));
      card.appendChild(inp); card.appendChild(go);
    }
    stage.appendChild(card);
  });
}

function exerciseSteering(ctx, beat, root, optsWrap) {
  if (!ctx.steering || !ctx.steering.installSteering) return;
  const options = Array.from(optsWrap.querySelectorAll('.intake-opt')).map((el, index) => ({
    index, el, isCorrect: !!beat.options[index] && beat.options[index].isCorrect, score: beat.options[index] ? beat.options[index].score : 0,
  }));
  let done = false;
  const commitViaForce = (idx) => { if (!done && options[idx]) { done = true; options[idx].el.click(); } };
  const steerCtx = {
    root, options, mechanic: beat.mechanic, band: beat.band, depth: beat.depth,
    roll: beat.steerRoll, caps: ctx.caps, escapeEffort: 5, escapeMs: 4000,
    onCommit: () => () => {}, forceComplete: commitViaForce, markProgress: () => {},
  };
  try {
    const handle = ctx.steering.installSteering(steerCtx);
    // release the steer once any option is chosen
    optsWrap.addEventListener('click', () => { try { handle && handle.release && handle.release(); } catch (_e) {} }, { once: true });
  } catch (_e) { /* steering stub might no-op */ }
}

/* --- null-object fallbacks (each mirrors its contracts.js factory) --------- */
function stubReward() {
  return {
    planFor: (band, depth, prompt) => ({ mode: 'honest', baseChance: band === 'recovery' ? 0 : 0.6, baseIntensity: depth, kind: 'chime' }),
    resolve: (plan, ev, scoreDelta) => ({ fire: (scoreDelta > 0) || plan.mode !== 'honest', intensity: plan.baseIntensity, kind: plan.kind, decoupled: plan.mode !== 'honest' }),
    channelsFor: () => ({}),
  };
}
function stubStats() {
  return {
    record: async () => {}, feedForward: async () => null, aggregates: async () => ({}),
    exportAll: async () => ({}), deleteAll: async () => {},
    recentRuns: async () => [], annotateLast: async () => {},
  };
}
function stubEffects({ root, caps, media, theme } = {}) {
  return { setDepth: () => {}, play: () => {}, recover: () => {} };
}
function stubAudio({ caps } = {}) { return { setDepth: () => {}, chime: () => {}, emerge: () => {} }; }
function stubBackground({ canvas } = {}) { return { setDepth: () => {}, setEnabled: () => {}, dispose: () => {} }; }
function stubSteering({ caps } = {}) { return { installSteering: () => ({ release: () => {} }) }; }
