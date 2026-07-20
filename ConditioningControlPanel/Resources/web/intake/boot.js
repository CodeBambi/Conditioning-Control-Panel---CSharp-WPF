/* ============================================================================
 * boot.js — entry point for the "Graded Intake" page (mirrors dtrh/boot.js).
 *
 * Boot order: wire shim handlers -> announceReady() -> shim resolves a
 * BootConfig (host `init` when hosted; URL+localStorage when standalone) ->
 * build ai / reward / engine / render -> run the beat loop -> emitResult.
 *
 * GRACEFUL SEAMS. Phase-1 modules (render/beats.js, render/effects.js,
 * render/audio.js, render/background.js, core/reward.js, core/stats.js) may not
 * exist yet. boot.js dynamically imports each and falls back to a null-object or
 * a tiny inline stub renderer, so Phase 0 runs end-to-end today and each agent's
 * file slots in with ZERO boot changes when it lands. This is the integration
 * contract Agent I copies on the C# side.
 * ==========================================================================*/

import * as shim from './web-shim.js';
import { createAI } from './core/ai.js';
import { createEngine } from './core/engine.js';
import { PRODUCT_NAME, Band } from './core/contracts.js';

const dom = {
  stage:  document.getElementById('intake-stage'),
  loader: document.getElementById('intake-loader'),
  hud:    document.getElementById('intake-hud'),
  bg:     document.getElementById('intake-bg'),
  title:  document.getElementById('intake-title'),
};
if (dom.title) dom.title.textContent = PRODUCT_NAME;

// Uncaught errors go to the host log — no devtools in the hosted page.
window.addEventListener('error', (e) => {
  const src = e.filename ? ` @ ${String(e.filename).split('/').pop()}:${e.lineno}` : '';
  shim.log('error: ' + (e.message || 'script error') + src);
});
window.addEventListener('unhandledrejection', (e) => {
  const r = e.reason;
  shim.log('promise: ' + ((r && (r.message || r.stack || r)) || 'unknown'));
});

// Optional-module loader: import a module + factory, or fall back to a stub.
async function loadOptional(path, factoryName, makeFallback) {
  try {
    const mod = await import(path);
    const f = mod && mod[factoryName];
    if (typeof f === 'function') return f;
  } catch (_e) { /* not delivered yet — use the fallback */ }
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

let running = false;

shim.onBoot(async (config) => {
  if (running) return;
  running = true;
  shim.log(`boot: niche=${config.niche} hosted=${config.hosted} endless=${config.endless}`);

  try {
    // --- build the stack (real module if present, else stub) --------------
    const ai = createAI(config.ai);

    const createReward = await loadOptional('./core/reward.js', 'createReward', stubReward);
    const createStats  = await loadOptional('./core/stats.js',  'createStats',  stubStats);
    const createBeats  = await loadOptional('./render/beats.js', 'createBeats',  null); // null -> inline stub render
    const createEffects= await loadOptional('./render/effects.js','createEffects', stubEffects);
    const createAudio  = await loadOptional('./render/audio.js', 'createAudio',  stubAudio);
    const createBg     = await loadOptional('./render/background.js','createBackground', stubBackground);

    const reward   = createReward({ config });
    const stats    = createStats();
    const effects  = createEffects({ root: dom.stage, caps: config.caps });
    const audio    = createAudio({ caps: config.caps });
    const background = createBg({ canvas: dom.bg });
    background.setEnabled && background.setEnabled(true);

    // Steering is optional too; beats.js pulls it in itself in the real build,
    // but we resolve it here so the stub render can use it symmetrically.
    const createSteering = await loadOptional('./render/steering.js', 'createSteering', stubSteering);
    const steering = createSteering({ caps: config.caps });

    // Load the niche's prompt bank (banks/<niche>.json). The engine falls back
    // to its own placeholder bank if this is null, so a fetch failure never
    // wedges the run (harness.html runs bankless the same way).
    const bank = config.bank || await loadBank(config.niche);

    const engine = createEngine({ bank, reward, ai, config, stats });

    // Real render if Agent C delivered it; else the inline stub renderer.
    const beats = createBeats
      ? createBeats({ root: dom.stage, effects, audio, steering, reward, caps: config.caps })
      : { render: (beat) => stubRenderBeat(beat, { effects, audio, steering, reward, caps: config.caps }) };

    if (dom.loader) dom.loader.hidden = true;

    // --- the canonical beat loop -----------------------------------------
    let step = await engine.next();
    while (!step.done) {
      const beat = step.beat;
      setDepthEverywhere(beat.depth, beat.band, { effects, audio, background });
      updateHud(beat);
      const ev = await beats.render(beat);
      step = await engine.next(ev);
    }

    // Recovery invariant: make sure we surface even if the engine ended abruptly.
    setDepthEverywhere(0, Band.Recovery, { effects, audio, background });
    effects.recover && effects.recover(0);
    audio.emerge && audio.emerge();

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
    showDone(result, ack);

  } catch (err) {
    shim.log('boot/run failed: ' + (err && (err.stack || err.message) || err));
    shim.bootError(String(err && err.message || err));
    if (dom.loader) dom.loader.hidden = true;
    if (dom.stage) dom.stage.innerHTML = `<div class="intake-fatal">Something went wrong starting ${PRODUCT_NAME}.</div>`;
  }
});

shim.announceReady();
shim.log('boot: ready posted');

/* ----------------------------------------------------------------------------
 * depth fan-out + HUD (shared by real + stub render)
 * -------------------------------------------------------------------------- */
function setDepthEverywhere(depth, band, { effects, audio, background }) {
  try { effects.setDepth && effects.setDepth(depth); } catch (_e) {}
  try { audio.setDepth && audio.setDepth(depth); } catch (_e) {}
  try { background.setDepth && background.setDepth(depth); } catch (_e) {}
}
function updateHud(beat) {
  if (!dom.hud) return;
  dom.hud.textContent = `${beat.band} · depth ${beat.depth.toFixed(2)}`;
}
function showDone(result, ack) {
  if (!dom.stage) return;
  const where = ack.delivered === 'host' ? 'Session drafting…' : 'Saved locally.';
  dom.stage.innerHTML = `<div class="intake-done">
    <h2>${PRODUCT_NAME} complete</h2>
    <p>Route: ${result.route.primaryArchetypeId || result.niche}</p>
    <p>Peak depth ${(result.peakDepth * 100) | 0}% · deepest band ${result.deepestBand}</p>
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
function stubEffects({ root, caps } = {}) {
  return { setDepth: () => {}, play: () => {}, recover: () => {} };
}
function stubAudio({ caps } = {}) { return { setDepth: () => {}, chime: () => {}, emerge: () => {} }; }
function stubBackground({ canvas } = {}) { return { setDepth: () => {}, setEnabled: () => {}, dispose: () => {} }; }
function stubSteering({ caps } = {}) { return { installSteering: () => ({ release: () => {} }) }; }
