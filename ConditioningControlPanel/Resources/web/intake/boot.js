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
import { PRODUCT_NAME, Band, BAND_ORDER, Mechanic, themeOf, clamp01, bandIndex } from './core/contracts.js';

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
    const background = tryMake('background', () => createBg({ canvas: dom.bg }), stubBackground);
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

    const engine = createEngine({ bank, reward, ai, config, stats });

    // Real render if Agent C delivered it; else the inline stub renderer.
    const stubBeats = () => ({ render: (beat) => stubRenderBeat(beat, { effects, audio, steering, reward, caps: config.caps }) });
    const beats = createBeats
      ? tryMake('beats', () => createBeats({ root: dom.stage, effects, audio, steering, reward, caps: config.caps, background, theme, media: config.media || null, niche: config.niche }), stubBeats)
      : stubBeats();

    // --- fiction identity + feed-forward ----------------------------------
    const subjectId = resolveSubjectId(config);
    let priorRun = config.priorRun || null;
    if (!priorRun && !config.hosted && stats.feedForward) {
      try { priorRun = await stats.feedForward(); } catch (_e) { /* best-effort */ }
    }

    if (dom.loader) dom.loader.hidden = true;
    buildHud();

    // --- clinical briefing (skipped headless / in m2Test harness runs) ----
    const shellCtx = { config, theme, bank, background, subjectId, priorRun, audio };
    try { if (audio && typeof audio.sfx === 'function') audio.sfx('briefing-open'); } catch (_e) {} // run-start intake chime
    try { await runBriefing(shellCtx); } catch (e) { shim.log('briefing failed: ' + (e && e.message || e)); }

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
      step = await engine.next(ev);

      // CARD-SWAP BREATHER: hold ~1s on the normal answer -> next-card swap so the
      // outgoing card's EXIT animation (added in beats.finalize) plays out before
      // the next render clears the stage and mounts the next card — closes the
      // speedrun gap. Deliberately excluded from every path that already carries
      // its own pacing, so nothing double-dips:
      //   - !step.done          : the run is over -> the OUTRO owns its long pacing.
      //   - !ev.timedOut        : melt-skip (1-in-30) and genuine timeouts both
      //                           resolve timedOut=true; they already "went away" on
      //                           their own, so no extra hold is layered on them.
      //   - not Interlude       : loom / breathe pacing valleys are already slow.
      //   - next beat !bandNew   : a NEW band opens with a full interstitial ceremony
      //                           at the top of the next iteration; holding in front
      //                           of it would stack onto the band transition.
      // (The jumpscare aborts the host, so the loop never continues past it; the
      //  freeze gate resolves as a normal ~20s ceremony where +1s is negligible.)
      const nextBandNew = !!(step && !step.done && step.beat && step.beat.meta && step.beat.meta.bandNew === true);
      if (!step.done && !ev.timedOut && beat.mechanic !== Mechanic.Interlude && !nextBandNew) {
        await sleep(config.m2Test ? 0 : CARD_SWAP_EXTRA_MS);
      }

      // Bambi-Sparkle sprinkle: a small chance after each card resolves to play
      // one of the BS-mode giggle clips (random variant, ~30% via manifest gain).
      // Skip once the run is over — no giggle bleeds into the outro / certificate.
      if (!step.done && Math.random() < GIGGLE_CHANCE) {
        try { if (audio && typeof audio.sfx === 'function') audio.sfx('giggle'); } catch (_e) {}
      }
    }

    // Run over: retire the subliminal bed before the surface/outro (idempotent if already gone).
    safeCall(subliminals, 'dispose'); subliminals = null;

    // Recovery invariant: make sure we surface even if the engine ended abruptly.
    setDepthEverywhere(0, Band.Recovery, { effects, audio, background, subliminals });
    effects.recover && effects.recover(0);
    audio.emerge && audio.emerge();
    try { if (audio && typeof audio.sfx === 'function') audio.sfx('surface-bloom'); } catch (_e) {} // run-end emerge bloom

    const result = step.result;
    await (stats.record ? stats.record(result) : Promise.resolve());
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
    safeCall(subliminals, 'dispose'); subliminals = null;
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
 * -------------------------------------------------------------------------- */
const SVG_NS = 'http://www.w3.org/2000/svg';
const hud = { built: false, q: null, dial: null, needle: null, spiral: null, section: null };

function svgEl(name, attrs) {
  const n = doc.createElementNS(SVG_NS, name);
  for (const k in attrs) n.setAttribute(k, attrs[k]);
  return n;
}

function buildHud() {
  if (!dom.hud || hud.built) return;
  try {
    dom.hud.innerHTML = '';
    hud.q = el('span', 'intake-hud-q', '');

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
    const meta = beat.meta || {};
    hud.q.textContent = (typeof meta.qIndex === 'number' && typeof meta.qTotal === 'number' && meta.qTotal > 0)
      ? `Q ${meta.qIndex} / ~${meta.qTotal}` : '';
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
  const slotRecord = el('div', 'ix-recap-slot ix-recap-record');
  cer.append(slotLines, slotHead, slotStats, slotStmts, slotRecord);

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
  return { record: async () => {}, feedForward: async () => null, aggregates: async () => ({}), exportAll: async () => ({}), deleteAll: async () => {} };
}
function stubEffects({ root, caps, media, theme } = {}) {
  return { setDepth: () => {}, play: () => {}, recover: () => {} };
}
function stubAudio({ caps } = {}) { return { setDepth: () => {}, chime: () => {}, emerge: () => {} }; }
function stubBackground({ canvas } = {}) { return { setDepth: () => {}, setEnabled: () => {}, dispose: () => {} }; }
function stubSteering({ caps } = {}) { return { installSteering: () => ({ release: () => {} }) }; }
