/* ============================================================================
 * engine/oneshots.js — the fire() primitives.
 *
 *   glitch_swap · sub_flash · flash_burst · gif_burst · audio_trigger
 *
 * Laws enforced here (GROUND-RULES §6, DECISIONS #9):
 *   - strengths come from ctx.channels() (heat -> clampToCaps -> master); the
 *     strobe-class terms (glitch shudder, burst alpha, sub alpha) are additionally
 *     multiplied by effectIntensity (the photosensitivity guard);
 *   - node budgets: flash_burst 20 live (3 on a coarse/low-motion device),
 *     gif_burst 10 (4 coarse/low-motion), sub_flash 6 (3 coarse/low-motion);
 *   - INPUT TRUST: over a click/tap-precision surface the game passes
 *     clickSafe:true and every burst node renders pointer-events:none;
 *   - a clickable burst is a blocking effect, so it carries an ESCAPE GUARD
 *     (6 interactions / 5s -> forceComplete clears the burst);
 *   - words may be EMPTY: sub_flash then falls back to image-only, and with no
 *     image either it skips SILENTLY (never crashes, never invents a word);
 *   - reduced motion / motionLevel 0 -> static, dim, single-node degrades.
 *
 * ADDITIVE (2026-08-23): `fullBleed: true` on flash_burst / gif_burst renders
 * ONE node covering the whole effects layer (`.ae-burst-cover`, object-fit:
 * cover, no transform) instead of a placed card - CCP's own "fullscreen GIF",
 * which a width-only burst node could never be. Opt-in and count-forcing: a
 * caller that never passes it sees byte-identical behaviour, so no existing
 * class moves. Every other law still applies (the node cap, the decoder budget
 * through budgetedKind, the alpha ceiling, clickSafe, the timer registry).
 *
 * ADDITIVE (2026-08-23, Instant Recall's variety pass): `holdMs` on sub_flash
 * LENGTHENS the blip without touching its alpha. `ae-sub-blip`'s plateau is
 * 22%-70% of the duration, so a 320-400ms spec word sits at full alpha for only
 * ~170ms - unreadable over a bright moving wall. A class that QUIZZES the player
 * on the word needs it legible, and the honest lever is TIME, not intensity:
 * alpha still comes from the clamped `subDensity` channel and nothing here may
 * raise it (THE CEILING RULE). `dur = clamp(holdMs, spec.durMs, SUB_HOLD_MAX_MS)`
 * - it can only ever make the word last LONGER than the spec, never shorter, and
 * never longer than 1400ms. The release timer moves with it (`dur + 320`) and the
 * handle answers `holdMs: dur`. Absent -> byte-identical: no field on the handle,
 * `--ae-dur` is `spec.durMs` and the release is `spec.durMs + 320`, exactly as
 * before. A caller that lengthens a word must also widen its own cadence, or two
 * words overlap (Instant Recall raised `CADENCE.subliminal.min` to 1400 for this).
 *
 * ADDITIVE (2026-08-25, the voiced word): `voice: true` on sub_flash SAYS the
 * word as it paints it. OPT-IN per call and inert unless three things line up -
 * the caller asked, the shell handed this engine a `wordAudio` entry for the
 * word it drew, and the latch below is open. The engine holds no audio element
 * here either: it routes through this file's own `audio_trigger` (the clip door
 * on `shell/audio.js`), so the clip rides the bus graph, the duck hierarchy, the
 * mute and the master volume like every other sound the school makes.
 *   - `voiceKey` (default 'ae-sub') is the mixer's voice SLOT: a re-fire on the
 *     same key cuts the clip already in it, which is what keeps a class from
 *     stacking its own whispers. A class that wants its own slot passes
 *     '<game>-whisper'.
 *   - The synthesised whisper (`sfx`) is the FALLBACK, not a layer: a call that
 *     passes both plays the clip and skips the oscillator for that tick.
 *   - `wordAudio` is EMPTY on a day the app-wide whisper mute is on, so no game
 *     has to gate its own flag on `ctx.audioAudible` - the flag simply does
 *     nothing. Neither side alone opens the tap, exactly as the host describes.
 * ==========================================================================*/

import { clamp01 } from '../core/caps.js';
import {
  NODE_CAPS, DUCK, subFlashSpec, glitchSpec, gifBurstSpec,
  burstCountForHeat, burstOpacityForHeat,
} from './curves.js';
import { rand, pickFrom, hasDom, mediaEl, budgetedKind, isVideoUrl } from './util.js';
import { createEscapeGuard } from './escape.js';

/** The ceiling on a lengthened sub_flash. A word that outlives this stops being
 *  a subliminal and becomes a caption; the class asking about it still has to
 *  read it off the wall, not off a billboard. */
export const SUB_HOLD_MAX_MS = 1400;

/** The one-at-a-time latch on a VOICED sub_flash. The stream can tick at
 *  SUB_MS.fast (360ms) and six clips a second would empty CLIP_VOICES in one
 *  breath, so a voiced word waits this long behind the last one and the tick
 *  that arrives early simply lands silent - the WORD still paints. 1400 is
 *  SUB_HOLD_MAX_MS: no clip may start while the last word could still be up. */
const VOICE_MIN_GAP_MS = SUB_HOLD_MAX_MS;

/** W3 P1-17: the floor between two SYNTHESISED sub_flash whispers. The stream
 *  ticks at 360ms at the top of its range and a whisper on every tick is a
 *  hiss, not a subliminal; two seconds is slow enough that the ear hears each
 *  one arrive. The VOICED path has its own, longer latch (VOICE_MIN_GAP_MS). */
const SUB_CUE_GAP_MS = 2000;

export function createOneshots(ctx) {
  const live = { flash: 0, gifBurst: 0, sub: 0 };
  /** When the last voiced word's clip was sent. Per engine, not per class. */
  let lastVoiceAt = 0;
  /** ...and when the last synthesised whisper went out (W3 P1-17). */
  let lastSubCueAt = 0;

  /* The lite twins ride the exact seam flashBurstLite always has: ctx.lite()
   * (coarse pointer OR motionLevel <= 1), evaluated at spend time. A desktop
   * with a fine pointer on motionLevel 2 never sees the lite number. */
  const flashCap = () => (ctx.lite() ? NODE_CAPS.flashBurstLite : NODE_CAPS.flashBurst);
  const gifBurstCap = () => (ctx.lite() ? NODE_CAPS.gifBurstLite : NODE_CAPS.gifBurst);
  const subCap = () => (ctx.lite() ? NODE_CAPS.subFlashLite : NODE_CAPS.subFlash);

  /* ---- glitch_swap ------------------------------------------------------- */
  /**
   * The engine NEVER moves a hitbox: it dresses the transition and calls back at
   * the midpoint so the GAME swaps its own content (input-honest by construction).
   */
  function glitchSwap(opts = {}) {
    const ceiling = ctx.ceiling('bgIntensity', opts.strength);
    if (ceiling <= 0.001) return null;        // the player capped this channel off
    const strobe = ctx.strobe(ceiling);
    const variant = ctx.variant('glitch_swap', strobe, ctx.reduced() ? 'crossfade' : opts.variant);
    const spec = glitchSpec(strobe, opts.seconds == null ? 0.6 : opts.seconds, opts.durationMult || 1);
    const targets = ctx.nodes(opts.targets);
    const exempt = opts.exempt || null;

    let done = false;
    const applied = [];
    for (const t of targets) {
      if (!t || t === exempt || (exempt && exempt.contains && exempt.contains(t))) continue;
      try {
        t.classList.add('ae-glitch', 'ae-glitch-' + (variant.name || 'crossfade'));
        t.style.setProperty('--ae-dur', spec.durMs + 'ms');
        applied.push(t);
      } catch { /* a detached node is not our problem */ }
    }
    ctx.fx('glitch_swap', variant.name);
    if (opts.sfx !== false) ctx.sfx(opts.sfxName || 'glitch', 0.35 + 0.4 * strobe);

    const clear = () => {
      if (done) return;
      done = true;
      for (const t of applied) {
        try {
          t.classList.remove('ae-glitch', 'ae-glitch-' + (variant.name || 'crossfade'));
          t.style.removeProperty('--ae-dur');
        } catch { /* ignore */ }
      }
    };
    // midpoint = where the game swaps content under cover of the transition
    if (typeof opts.onSwap === 'function') ctx.timers.after(spec.midpointMs, () => { try { opts.onSwap(variant.name); } catch { /* game's problem */ } });
    ctx.timers.after(spec.durMs + 40, clear);
    return { kind: 'glitch_swap', variant: variant.name, durMs: spec.durMs, cancel: clear };
  }

  /* ---- sub_flash --------------------------------------------------------- */
  function subFlash(opts = {}) {
    if (!hasDom()) return null;
    if (live.sub >= subCap()) return null;
    const strength = ctx.ceiling('subDensity', opts.strength);
    if (strength <= 0.001) return null;       // subDensity capped off -> silent
    const spec = subFlashSpec(ctx.strobe(strength));
    // an explicit alpha may only ever ASK FOR LESS than the ceiling-derived alpha
    const alpha = Math.min(spec.alpha, clamp01(opts.alpha == null ? spec.alpha : ctx.pct(opts.alpha))) * (ctx.reduced() ? 0.6 : 1);
    if (alpha <= 0.001) return null;

    const words = ctx.words();
    const wantImage = opts.image === true || (opts.image !== false && !words.length) || (opts.image !== false && ctx.rng() < 0.5);
    const word = opts.text || (words.length ? pickFrom(ctx.rng, words) : null);

    const variant = ctx.variant('sub_flash', strength, ctx.reduced() ? 'centre' : opts.variant);
    const node = (() => {
      if (wantImage) {
        const url = opts.url || ctx.assetUrlSync('still');
        if (url) {
          const img = document.createElement('img');
          img.src = url;
          img.className = 'ae-sub ae-sub-' + variant.name;
          return img;
        }
      }
      if (!word) return null;                  // may-be-empty contract: skip silently
      const div = document.createElement('div');
      div.className = 'ae-sub ae-sub-word ae-sub-' + variant.name;
      div.textContent = String(word);
      return div;
    })();
    if (!node) return null;

    /* ADDITIVE: an opt-in LONGER hold. Never shorter than the spec, never over
     * SUB_HOLD_MAX_MS, and never a word on the alpha - see the header. */
    const wantHold = Number(opts.holdMs);
    const held = Number.isFinite(wantHold);
    const durMs = held
      ? Math.round(Math.max(spec.durMs, Math.min(SUB_HOLD_MAX_MS, wantHold)))
      : spec.durMs;

    node.style.setProperty('--ae-dur', durMs + 'ms');
    node.style.setProperty('--ae-alpha', String(alpha));
    if (variant.name === 'scatter') {
      node.style.setProperty('--ae-x', Math.round(rand(ctx.rng, 18, 82)) + '%');
      node.style.setProperty('--ae-y', Math.round(rand(ctx.rng, 20, 80)) + '%');
    }
    if (ctx.reduced()) node.style.opacity = String(alpha);
    const host = opts.anchor && opts.anchor.appendChild ? opts.anchor : ctx.layers.front;
    host.appendChild(node);
    live.sub += 1;
    ctx.fx('sub_flash', variant.name);
    ctx.timers.after(durMs + 320, () => { ctx.timers.release(node); live.sub = Math.max(0, live.sub - 1); });
    ctx.timers.own(node);

    /* THE VOICED WORD (opt-in, see the header). Only a caller that asked, only a
     * word the shell handed a clip for, and only when the latch is open. */
    let voiced = false;
    if (opts.voice === true && word) {
      const clip = (typeof ctx.wordAudio === 'function') ? ctx.wordAudio(String(word)) : null;
      const now = Date.now();
      if (clip && (now - lastVoiceAt) >= VOICE_MIN_GAP_MS) {
        lastVoiceAt = now;
        voiced = true;
        audioTrigger({
          name: 'whisper',                      // the recipe a host without the file falls to
          url: clip,
          key: opts.voiceKey ? String(opts.voiceKey) : 'ae-sub',
          maxMs: durMs + 400,
          level: 0.28 + 0.2 * strength,
          duck: 'voice',
        });
      }
    }
    /* The SYNTHESISED whisper is the fallback, never a layer on top: a caller
     * that passes both `sfx` and `voice` (deja-vu's preview flash does) would
     * otherwise play an oscillator impression of a whisper over the real one.
     *
     * W3 P1-17: `opts.sfx` was never set by anything in the school, so the word
     * that lands in the middle of the screen landed in silence. The CENTRE
     * variant now whispers by default, quietly and throttled; scatter stays
     * mute (it is peripheral dressing, and a cue would drag the eye to it).
     * `sfx:false` is still the way out and a string still names the cue, and a
     * caller that asked keeps the louder level it always had. */
    const autoSfx = opts.sfx == null && variant.name === 'centre';
    const wantSfx = opts.sfx == null ? autoSfx : opts.sfx;
    if (wantSfx && !voiced) {
      const nowMs = Date.now();
      if (!autoSfx || (nowMs - lastSubCueAt) >= SUB_CUE_GAP_MS) {
        if (autoSfx) lastSubCueAt = nowMs;
        ctx.sfx(typeof wantSfx === 'string' ? wantSfx : 'whisper',
          autoSfx ? 0.18 : 0.25 + 0.35 * strength, { duck: 'voice' });
      }
    }

    const handle = { kind: 'sub_flash', variant: variant.name, text: word || null, durMs };
    if (held) handle.holdMs = durMs;
    if (voiced) handle.voiced = true;
    return handle;
  }

  /* ---- burst core (flash_burst + gif_burst share the node builder) -------- */
  function spawnBurst(kind, opts) {
    if (!hasDom()) return null;
    const heat = ctx.heat();
    const intensity = ctx.ceiling('flashOpacity', opts.strength);
    if (intensity <= 0.001) return null;      // flashOpacity capped off -> silent
    const strobe = ctx.strobe(intensity);
    const spec = gifBurstSpec(strobe);
    const variant = ctx.variant(kind, strobe, ctx.reduced() ? (kind === 'flash_burst' ? 'single' : 'pop') : opts.variant);
    const ceilAlpha = burstOpacityForHeat(heat) * (0.6 + 0.4 * strobe);
    const alpha = Math.min(ceilAlpha, clamp01(opts.alpha == null ? ceilAlpha : ctx.pct(opts.alpha)));
    const cap = kind === 'flash_burst' ? flashCap() : gifBurstCap();
    const counter = kind === 'flash_burst' ? 'flash' : 'gifBurst';

    /* FULL BLEED (additive, 2026-08-23). One node covering the whole layer -
     * CCP's "fullscreen GIF", which a width-only burst node cannot be. Opt-in:
     * without the flag nothing about a burst changes, so no other class moves. */
    const fullBleed = opts.fullBleed === true;

    let count = Number.isFinite(opts.count) ? Math.max(1, opts.count | 0)
      : burstCountForHeat(heat, ctx.rng());
    if (ctx.reduced() || ctx.motion() <= 0) count = 1;
    if (fullBleed) count = 1;
    count = Math.min(count, Math.max(0, cap - live[counter]));
    if (count <= 0) return null;

    // INPUT TRUST: a click-precision surface gets decoration only.
    const clickSafe = !!opts.clickSafe || ctx.reduced();
    const clickable = !clickSafe && opts.clickable !== false && kind === 'flash_burst';
    const nodes = [];
    let guard = null;
    let cleared = false;

    function clearAll(why) {
      if (cleared) return;
      cleared = true;
      for (const n of nodes.slice()) killNode(n);
      if (guard) guard.cancel();
      if (why) ctx.log(kind + ' cleared (' + why + ')');
    }
    function killNode(n) {
      const i = nodes.indexOf(n);
      if (i >= 0) nodes.splice(i, 1);
      ctx.timers.release(n);
      live[counter] = Math.max(0, live[counter] - 1);
    }

    function makeNode(genLeft, atX, atY) {
      if (cleared || live[counter] >= cap) return null;
      /* THE DECODER BUDGET (util.js): a gif_burst asks for a loop, but once the
       * shared budget is spent it is handed a still instead - the burst is the
       * same size, the same count and the same hold, it just stops minting a
       * 854x480 decoder per node. An explicit opts.url is the caller's own
       * choice and is never second-guessed. */
      let url = opts.url || null;
      if (!url) {
        const drawn = ctx.assetUrlSync(budgetedKind(opts.assetKind || (kind === 'gif_burst' ? 'loop' : 'still')));
        /* WARM-MEDIA SEAM (opt-in, 2026-08-25; twin of gif_rain's): pick() may
         * substitute a url the game knows is warm, but the original pool draw
         * above always happens (shared-rng law - pool.next consumes the
         * provider's own rng) and stands in when pick answers nothing. A picked
         * VIDEO only rides a slot budgetedKind already granted. An explicit
         * opts.url still short-circuits everything, exactly as before. */
        url = drawn;
        if (typeof opts.pick === 'function') {
          let picked = null;
          try { picked = opts.pick() || null; } catch { picked = null; }
          if (picked && (!isVideoUrl(picked) || isVideoUrl(drawn))) url = picked;
        }
      }
      // mediaEl: <img>, or a muted looping <video> when the pool handed us a
      // webm/mp4 loop (the only animated shape a remote provider has)
      const node = (url && mediaEl(url)) || document.createElement('div');
      node.className = 'ae-burst ae-burst-' + variant.name + (clickable ? ' ae-burst-clickable' : '')
        + (fullBleed ? ' ae-burst-cover' : '');
      node.style.setProperty('--ae-x', (atX == null ? Math.round(rand(ctx.rng, 12, 88)) : atX) + '%');
      node.style.setProperty('--ae-y', (atY == null ? Math.round(rand(ctx.rng, 14, 86)) : atY) + '%');
      node.style.setProperty('--ae-rot', fullBleed ? '0deg' : (Math.round(rand(ctx.rng, -8, 8)) + 'deg'));
      node.style.setProperty('--ae-size', Math.round((opts.sizePx || spec.sizePx) * (0.8 + 0.4 * ctx.rng())) + 'px');
      node.style.setProperty('--ae-dur', (opts.holdMs || spec.holdMs) + 'ms');
      node.style.setProperty('--ae-alpha', String(alpha));
      if (ctx.reduced()) node.style.opacity = String(alpha * 0.8);
      if (clickable) {
        node.addEventListener('click', (ev) => {
          try { ev.stopPropagation(); } catch { /* ignore */ }
          if (guard) guard.note();
          if (typeof opts.onPop === 'function') { try { opts.onPop(node); } catch { /* game's problem */ } }
          ctx.sfx('pop', 0.4 + 0.3 * strobe);
          killNode(node);
          // the hydra: popping one spawns two more, up to hydraGen generations
          const gens = Number.isFinite(opts.hydraGen) ? opts.hydraGen : (ctx.lite() ? 0 : 1);
          if (genLeft > 0 && gens > 0 && !cleared) {
            makeNode(genLeft - 1); makeNode(genLeft - 1);
            /* W3 P1-17: the hydra's children arrived in the same silence as the
             * pop that made them, so the one moment the effect FIGHTS BACK read
             * as an ordinary clear. One bright pop behind the plain one, on the
             * children not on each child. */
            ctx.timers.after(80, () => ctx.sfx('pop', 0.25, { pitch: 1.4 }));
          }
        }, { once: true });
      }
      ctx.layers.front.appendChild(node);
      ctx.timers.own(node);
      nodes.push(node);
      live[counter] += 1;
      ctx.timers.after((opts.holdMs || spec.holdMs) + 700, () => killNode(node));
      return node;
    }

    /* W3 P1-17: ONE cue used to fire for the whole burst, before any node was
     * on screen, so a four-node stagger sounded exactly like a single flash.
     * The cue now rides the nodes: one each, capped at three (trap 111 - bursts
     * are the per-instance exception, and the cap is what keeps them from being
     * a machine gun), each quieter than the last so the burst reads as one
     * gesture arriving rather than as three separate events. */
    let cued = 0;
    const CUE_FALL = [1, 0.7, 0.5];
    const burstName = opts.sfxName || (kind === 'gif_burst' ? 'burst' : 'flash');
    const burstLevel = 0.3 + 0.45 * strobe;
    function burstCue() {
      if (opts.sfx === false || cued >= CUE_FALL.length) return;
      ctx.sfx(burstName, burstLevel * CUE_FALL[cued]);
      cued += 1;
    }

    const stagger = ctx.reduced() ? 0 : Math.round((opts.holdMs || spec.holdMs) * 0.32 / Math.max(1, count));
    for (let i = 0; i < count; i++) {
      const gens = Number.isFinite(opts.hydraGen) ? opts.hydraGen : (ctx.lite() ? 0 : 1);
      if (i === 0 || stagger === 0) { makeNode(gens, opts.x, opts.y); burstCue(); }
      else ctx.timers.after(stagger * i, () => { makeNode(gens); burstCue(); });
    }

    if (clickable) {
      // W3 P0-24: sfx threaded so the guard can sound its own release.
      guard = createEscapeGuard({ timers: ctx.timers, sfx: ctx.sfx, onComplete: (why) => { clearAll('escape:' + why); if (typeof opts.onForceComplete === 'function') { try { opts.onForceComplete(why); } catch { /* ignore */ } } } });
      guard.arm();
    }

    ctx.fx(kind, variant.name);
    return {
      kind, variant: variant.name, count, clickSafe, clickable, fullBleed,
      get live() { return nodes.length; },
      cancel: () => clearAll('cancel'),
      escape: guard,
    };
  }

  const flashBurst = (opts = {}) => spawnBurst('flash_burst', opts);
  const gifBurst = (opts = {}) => spawnBurst('gif_burst', Object.assign({ clickable: false }, opts));

  /* ---- audio_trigger ----------------------------------------------------- */
  /**
   * The engine holds NO audio element. It emits `arcademy-sfx` and the shell/host
   * route it; duck requests follow DTRH's .4/.25/.15 hierarchy.
   */
  function audioTrigger(opts = {}) {
    const chans = ctx.channels();
    const name = opts.name || opts.id || 'sting';
    const base = opts.level == null ? 0.6 : ctx.pct(opts.level);
    // THE BINAURAL FLOOR (2026-08-25, Echo's silence). binauralDepth is
    // smoothstep(heat) x caps x master, so a low-heat class sees ~0.03-0.05 and
    // the old (0.55 + 0.45*depth) term halved every tone before the mixer ever
    // saw it - on a phone speaker that WAS the difference between a game with
    // sound and one without. The effects dial may COLOUR loudness, never
    // near-silence it: mute/masterVolume/the bus law is the honest volume
    // control. Floor 0.8, depth buys the last 20%.
    const level = ctx.magnitude(base * (0.8 + 0.2 * clamp01(chans.binauralDepth)));
    const duckKind = opts.duck === true ? 'voice' : (opts.duck || null);
    const detail = { name, level, bus: opts.bus || 'fx' };
    // The pitch ratchet (shell/audio.js clamps 0.5..2): three games send it and
    // the seam dropped it on the floor until 0822 - every descending chime and
    // reveal tick played at pitch 1. Pass-through only; audio owns the clamp.
    if (opts.pitch != null && Number.isFinite(Number(opts.pitch))) detail.pitch = Number(opts.pitch);
    // A CLIP: a same-origin ccp.* url the mixer plays through the bus instead of
    // synthesising the recipe (shell/audio.js). Pass-through only, exactly like
    // pitch - the level above is still the engine's clamped one, so a clip can
    // never be louder than the channel allows, and `key` is the voice slot the
    // mixer cuts on a re-fire. A host that cannot play it falls back to `name`.
    if (typeof opts.url === 'string' && opts.url) {
      detail.url = opts.url;
      if (opts.key != null) detail.key = String(opts.key);
      if (Number.isFinite(Number(opts.maxMs))) detail.maxMs = Number(opts.maxMs);
      if (Number.isFinite(Number(opts.fadeMs))) detail.fadeMs = Number(opts.fadeMs);
    }
    // THE CASCADE (2026-08-26, deep-end choreography): `steps` are follow-up
    // blips pre-scheduled on the mixer's own timeline INSIDE this one dispatch
    // ({atMs, name?, pitch?, level?} each) - one graph build for a whole run of
    // pops instead of one per pop. Every step level is clamped exactly like the
    // main cue's; the mixer clamps pitch. Pass-through otherwise, like pitch.
    if (Array.isArray(opts.steps) && opts.steps.length) {
      const steps = [];
      for (const s of opts.steps.slice(0, 16)) {
        if (!s) continue;
        const st = { atMs: Math.max(0, Number(s.atMs) || 0) };
        if (s.name != null) st.name = String(s.name);
        if (s.pitch != null && Number.isFinite(Number(s.pitch))) st.pitch = Number(s.pitch);
        if (s.level != null) st.level = ctx.magnitude(ctx.pct(s.level) * (0.8 + 0.2 * clamp01(chans.binauralDepth)));
        steps.push(st);
      }
      if (steps.length) detail.steps = steps;
    }
    if (duckKind && DUCK[duckKind] != null) {
      // depth of the duck is the player's duckDepth channel against the policy
      const policy = DUCK[duckKind];
      const depth = 1 - (1 - policy) * clamp01(chans.duckDepth);
      detail.duck = { target: duckKind, mult: depth, ms: opts.duckMs || 250 };
    }
    ctx.sfxRaw(detail);
    ctx.fx('audio_trigger', name);
    return { kind: 'audio_trigger', name, level, duck: detail.duck || null };
  }

  return {
    glitch_swap: glitchSwap,
    sub_flash: subFlash,
    flash_burst: flashBurst,
    gif_burst: gifBurst,
    audio_trigger: audioTrigger,
    live,
    reset() { live.flash = 0; live.gifBurst = 0; live.sub = 0; lastVoiceAt = 0; },
  };
}

export default createOneshots;
