/* ============================================================================
 * chaosRun.js - the DtRH run brain, ported from ChaosModeService.cs.
 *
 * Owns the run lifecycle (countdown -> waves -> recap -> payout over the
 * bridge), the 0.25s RunTick folded into the rAF loop, the exact C# spawn
 * cadence, and the score stack:
 *
 *   TotalMult = BaseMult × ComboMult(min(1+combo·0.08, 6)) × DifficultyMult
 *               × HeatMult(1+heat)                       [boons arrive in M4]
 *   BasePoints = 40 + strength·1.6 ; treats pay ×0.4, snaps ×1.0
 *   Focus: +10/treat pop, +12/golden; a completed hold costs 30 (free frozen)
 *   payout: baseXp = min(score, 250·durMin·DiffMult) - C# owns it (run-ended)
 *
 * DtRH's RunIntensity (elapsed/duration) OVERRIDES the Fall's 360s ramp: the
 * director is demoted to a speed/boost presentation adapter fed from here.
 * No-lose: detonations cost streak/heat and fire a REAL native payload over
 * the bridge - the run itself never ends early.
 *
 * P1 3D-native enhancements live here too: detonation FOV punch + boost kill
 * (the fall stumbles), freeze = visible time dilation (the tunnel crawls),
 * the Ripple doubles as a tunnel shockwave, loop boundaries seed a fresh
 * mood zone ahead and flash the tube.
 * ==========================================================================*/

import { VARIANTS, pick, buildGolden, FREEZE_MAX_ON_SCREEN, SIDE_DRIFT_CHANCE, SIDE_DRIFT_GRACE_SPAWNS, MOTION } from './variants.js';
import { createChaosField } from './chaosField.js';
import { createChaosHud } from './chaosHud.js';
import { createOverlays } from './overlays.js';

// ---- economy tuning (ChaosTuning.cs / ChaosModeService.cs) ----
const FOCUS_MAX = 100, FOCUS_START = 50;
const FOCUS_PER_POP = 10, FOCUS_PER_GOLDEN = 12;
const DEFUSE_COST = 30;
const FREEZE_DURATION_SEC = 3.5;
const FREEZE_DURATION_MULT = 2.5;   // payloads fired while frozen linger
const FREEZE_BASE_POINTS = 140;
const GOLDEN_CHANCE = 0.005;
const RIPPLE_RECHARGE_SEC = 15;
const RIPPLE_RADIUS_PX = 260;
const RIPPLE_LIFE_MS = 520;
const RIPPLE_TRIGGER_GRACE_PX = 80;
const COMBO_BIG_THRESHOLDS = [25, 50, 100];
const TICK = 0.25;                  // the C# RunTick period

const NAME_OF = Object.fromEntries(VARIANTS.map((v) => [v.id, v.name]));
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));

export function createChaosGame({ bridge, runConfig, requestExit }) {
  const cfg = {
    difficulty: runConfig?.difficulty || 'Easy',
    difficultyMult: runConfig?.difficultyMult ?? 1.0,
    durationSec: clamp(runConfig?.durationSec ?? 180, 60, 900),
    waveCount: clamp(runConfig?.waveCount ?? 5, 1, 12),
    effectIntensity: clamp(runConfig?.effectIntensity ?? 0.85, 0.2, 1.5),
    enabledVariants: runConfig?.enabledVariants || null,
    motionOverride: runConfig?.motionOverride && MOTION[runConfig.motionOverride] ? runConfig.motionOverride : null,
    fuseTimeMult: runConfig?.fuseTimeMult ?? 1.0,
    baseMult: runConfig?.baseMult ?? 1.0,
    sparkGainMult: runConfig?.sparkGainMult ?? 1.0,
    spawnRateMult: clamp(runConfig?.spawnRateMult ?? 1.0, 0.1, 10.0),
    colorFlashes: runConfig?.colorFlashes !== false,
  };

  let ctx = null;      // { nav, fx, director, hud, canvas } from scene.start
  let field = null, hudUi = null, overlays = null;
  let state = 'boot';  // boot -> countdown -> running -> recap
  let paused = false, hidden = false, covered = false;
  const heldNow = () => paused || hidden || covered;

  // ---- run state (ChaosRunState) ----
  let st = null;
  function freshState() {
    return {
      score: 0, combo: 0, bestCombo: 0, heat: 0, focus: FOCUS_START,
      defused: 0, detonated: 0, effectsFired: 0, spawned: 0,
      elapsedSec: 0, runDurationSec: cfg.durationSec,
      waveIndex: 1, waveCount: cfg.waveCount, actIndex: 1,
      rippleCooldown: 0,
      freezeRemainingSec: 0,
      ordinarySpawns: 0, waveDetonations: 0,
      endingSoonFired: false, lastComboBig: 0,
      lastActFired: 1,
      totalMult: cfg.baseMult * cfg.difficultyMult,
    };
  }
  const comboMult = () => Math.min(1.0 + st.combo * 0.08, 6.0);
  const heatMult = () => 1.0 + st.heat;
  const totalMult = () => cfg.baseMult * comboMult() * cfg.difficultyMult * heatMult();
  const basePoints = (strength) => 40 + strength * 1.6;
  const intensity = () => clamp(st ? st.elapsedSec / st.runDurationSec : 0, 0, 1);

  let tickAcc = 0, spawnWait = 0.8;   // the WPF spawn timer opens at 800ms
  let lastNoFocusAnnounce = -10;
  const discovered = new Set();

  // ---- bridge helpers ----
  const sfx = (name, scale) => bridge.send({ type: 'sfx', name, scale });
  const firePayload = (spec) => {
    if (!spec.payload) return;
    bridge.send({
      type: 'fire-payload', ...spec.payload, strength: spec.strength,
      durationMult: st && st.freezeRemainingSec > 0 ? FREEZE_DURATION_MULT : 1.0,
    });
  };
  const bankGold = (amount) => bridge.send({ type: 'meta-command', op: 'add-gold', amount });
  const markDiscovered = (variantId) => {
    if (discovered.has(variantId)) return;
    discovered.add(variantId);
    bridge.send({ type: 'meta-command', op: 'add-to-set', set: 'discoveredCodexIds', id: 'bubble:' + variantId });
  };
  const pulse = (rgb, strength) => { if (cfg.colorFlashes) hudUi?.pulse(rgb, strength); };

  // ============================ field callbacks ============================

  function canChannel(spec) {
    if (!st) return false;
    return st.freezeRemainingSec > 0 || st.focus >= DEFUSE_COST;
  }

  function onChannelBroken(spec, reason) {
    if (!st) return;
    if (reason === 'nofocus') {
      sfx('focus_empty', 0.55);
      pulse('255,80,80', 0.22);
      hudUi.flashFocus();
      const now = st.elapsedSec;
      if (now - lastNoFocusAnnounce >= 1.5) {
        lastNoFocusAnnounce = now;
        hudUi.announce('✋ NO FOCUS TO DEFUSE', 'bad', 1300);
      }
      hudUi.toast('✋ no focus — it triggers in your grip');
    } else if (reason === 'click') {
      hudUi.toast('💥 a tap isn’t a hold');
    } else {
      hudUi.toast('💥 you let go');
    }
  }

  function onDetonated(spec) {
    if (!st || state !== 'running' || heldNow()) return;
    firePayload(spec);
    st.effectsFired++;
    st.detonated++;
    st.waveDetonations++;
    const s = spec.strength / 100;
    st.combo = 0;
    st.lastComboBig = 0;
    st.heat = 0;
    sfx('trigger', 0.55);
    pulse('255,50,50', 0.4 + s * 0.35);
    // P1: the fall STUMBLES - FOV punch + the speed boost dies on the spot.
    ctx.nav.fovKick(3 + s * 2.5);
    ctx.director.killBoost();
    hudUi.toast(`💥 ${NAME_OF[spec.variantId] || spec.variantId} triggered!`);
  }

  function onBenignPopped(spec, x, y) {
    if (!st || state !== 'running' || heldNow()) return;

    if (spec.kind === 'golden') {
      st.focus = clamp(st.focus + FOCUS_PER_GOLDEN, 0, FOCUS_MAX);
      const gold = 10 + Math.floor(Math.random() * 11);   // GoldenPayRange(0) = 10..20
      bankGold(gold);
      field.floatText(`+${gold} gold`, x, y + 30, 'cf-pop--gold');
      hudUi.announce(`🪙 +${gold} gold`, 'powerup', 1600);
      sfx('golden_pop', 0.6);
      pulse('255,215,0', 0.35);
      return;   // pure treasure - outside the score/combo economy
    }

    firePayload(spec);   // benign pop = a treat: the effect IS the reward
    st.effectsFired++;
    st.combo++;
    if (st.combo > st.bestCombo) st.bestCombo = st.combo;
    const focusWasLow = st.focus < DEFUSE_COST;
    st.focus = clamp(st.focus + FOCUS_PER_POP, 0, FOCUS_MAX);
    if (focusWasLow) field.floatText(`+${FOCUS_PER_POP} FOCUS`, x, y + 34, 'cf-pop--focus');
    st.heat = Math.min(1.0, st.heat + 0.04);
    st.score += basePoints(spec.strength) * 0.4 * totalMult();
    pulse('255,200,235', 0.10);
    ctx.director.notePop(spec.variantId, 0, st.combo);
    checkComboMilestone();
  }

  function onDefused(spec, fuseSecLeft, viaChannel) {
    if (!st || state !== 'running' || heldNow()) return;
    if (viaChannel && st.freezeRemainingSec <= 0) st.focus = clamp(st.focus - DEFUSE_COST, 0, FOCUS_MAX);
    st.defused++;
    st.combo++;
    if (st.combo > st.bestCombo) st.bestCombo = st.combo;
    st.heat = Math.min(1.0, st.heat + 0.07);
    st.score += basePoints(spec.strength) * 1.0 * totalMult();
    sfx('defuse_hiss', 0.5);
    pulse('90,255,150', 0.16);
    ctx.director.notePop(spec.variantId, 0, st.combo);
    checkComboMilestone();
  }

  function onFreezeCaught(spec) {
    if (!st || state !== 'running' || heldNow()) return;
    st.score += FREEZE_BASE_POINTS * totalMult();
    st.combo++;
    if (st.combo > st.bestCombo) st.bestCombo = st.combo;
    st.heat = Math.min(1.0, st.heat + 0.05);
    st.freezeRemainingSec = FREEZE_DURATION_SEC;
    field.setFrozen(true);
    ctx.director.setTimeFactor(0.06);   // P1: visible time dilation - the tunnel all but stops
    sfx('freeze_catch', 0.55);
    hudUi.announce('❄ FREEZE', 'freeze', 1800);
    pulse('150,210,255', 0.30);
    checkComboMilestone();
  }

  function endFreeze() {
    st.freezeRemainingSec = 0;
    field.setFrozen(false);
    ctx.director.setTimeFactor(1);
    sfx('freeze_shatter', 0.5);
  }

  function onTreatExpired(spec) {
    if (!st || state !== 'running' || heldNow()) return;
    const name = spec.kind === 'golden' ? 'lucky bubble' : NAME_OF[spec.variantId] || spec.variantId;
    if (st.combo > 1) {
      st.combo = Math.floor(st.combo / 2);
      hudUi.toast(`💨 ${name} faded… streak halved to ×${st.combo}`);
    } else {
      st.combo = 0;
      hudUi.toast(`💨 ${name} faded away`);
    }
    pulse('150,150,175', 0.10);
  }

  function checkComboMilestone() {
    const combo = st.combo;
    if (combo <= 0) return;
    for (const t of COMBO_BIG_THRESHOLDS) {
      if (combo >= t && st.lastComboBig < t) {
        st.lastComboBig = t;
        sfx('streak_milestone', 0.5);
        hudUi.announce(`STREAK ×${t}`, 'streak', 2000);
        pulse('255,230,120', 0.55);
      }
    }
    if (combo % 10 === 0) {
      pulse('255,200,60', 0.4);
      hudUi.toast(`🔥 streak ×${combo}!`);
    } else {
      const frac = (combo % 10) / 10;
      pulse('255,170,80', 0.04 + 0.08 * frac);
    }
  }

  // ============================ RunTick (0.25s) ============================

  function tick(dt) {
    st.elapsedSec += dt;
    st.heat = Math.max(0, st.heat - 0.0015);

    if (st.freezeRemainingSec > 0) {
      st.freezeRemainingSec -= dt;
      if (st.freezeRemainingSec <= 0) endFreeze();
    }

    // Empty-field rescue: a fast clear shouldn't leave dead air until the
    // spawn cadence's next beat - pull the next spawn forward.
    if (st.freezeRemainingSec <= 0 && field.count() === 0) spawnWait = 0;

    if (st.rippleCooldown > 0) {
      st.rippleCooldown -= dt;
      if (st.rippleCooldown <= 0) { st.rippleCooldown = 0; sfx('toy_ready', 0.3); }
    }

    if (!st.endingSoonFired && st.runDurationSec - st.elapsedSec <= 10) {
      st.endingSoonFired = true;
      hudUi.announce('the hole is closing…', 'depth', 2400);
      bridge.send({ type: 'bark', event: 'ending-soon' });
    }

    if (st.elapsedSec >= st.runDurationSec) { endRun(true); return; }

    const waveLen = st.runDurationSec / st.waveCount;
    const newWave = Math.min(st.waveCount, 1 + Math.floor(st.elapsedSec / waveLen));
    if (newWave > st.waveIndex) beginWaveTransition(newWave);

    hudUi.update(st);
  }

  function beginWaveTransition(newWave) {
    awardLoopTip();
    st.waveIndex = newWave;
    st.actIndex = 1 + Math.floor((newWave - 1) / 5);
    if (st.actIndex > st.lastActFired) {
      st.lastActFired = st.actIndex;
      sfx('depth_change', 0.55);
      hudUi.announce(`DEPTH ${['I', 'II', 'III', 'IV', 'V'][st.actIndex - 1] || st.actIndex}`, 'depth', 2400);
    } else {
      sfx('tunnel_zone', 0.5);
      hudUi.announce(`LOOP ${newWave}${newWave === st.waveCount ? ' — the last one' : ''}`, 'depth', 1800);
    }
    // P1: zone-shift wave transition - a fresh tunnel mood rolls in ahead
    // and the tube flashes on the boundary.
    ctx.fx.seedZoneAhead(intensity());
    ctx.fx.pulseFlash(0.6);
    pulse('150,200,255', 0.30);
  }

  /** Loop-clear tip: 3-6 · difficulty gold on every loop boundary, doubled
   * when the loop was clean (zero detonations). */
  function awardLoopTip() {
    const clean = st.waveDetonations === 0;
    st.waveDetonations = 0;
    let tip = Math.round((3 + Math.floor(Math.random() * 4)) * cfg.difficultyMult);
    if (clean) tip *= 2;
    bankGold(tip);
    hudUi.toast(clean ? `🪙 clean loop — she tips +${tip} gold` : `🪙 loop done — she tips +${tip} gold`);
  }

  // ============================ spawn cadence ============================

  function spawnTick() {
    if (st.freezeRemainingSec > 0) { spawnWait = 0.3; return; }   // frozen: hold the field
    const i = intensity();
    const effIntensity = clamp(i + (cfg.difficultyMult - 1.0) * 0.15, 0, 1);
    const maxConcurrent = Math.round((6 + i * 10) * Math.sqrt(cfg.difficultyMult));

    if (field.count() < maxConcurrent) {
      const sideDrift = st.ordinarySpawns < SIDE_DRIFT_GRACE_SPAWNS ? 0 : SIDE_DRIFT_CHANCE;
      const opts = {
        enabledIds: cfg.enabledVariants,
        fuseTimeMult: cfg.fuseTimeMult,
        motionOverride: cfg.motionOverride,
        effectIntensity: cfg.effectIntensity,
        sideDriftChance: sideDrift,
      };
      let spec = pick(effIntensity, opts);
      // Freeze cap: at most 2 freeze pickups live at once - re-pick without it.
      if (spec.kind === 'freeze' && field.freezeCount() >= FREEZE_MAX_ON_SCREEN) {
        const noFreeze = (cfg.enabledVariants || VARIANTS.map((v) => v.id)).filter((id) => id !== 'bambifreeze');
        spec = pick(effIntensity, { ...opts, enabledIds: noFreeze });
      }
      st.ordinarySpawns++;
      st.spawned++;
      markDiscovered(spec.variantId);
      field.spawn(spec);

      // Lucky golden bubble: a rare bonus roll riding every ordinary spawn.
      if (Math.random() < GOLDEN_CHANCE) {
        markDiscovered('golden');
        field.spawn(buildGolden());
        field.playChime(0.30);
      }
    }

    // Refill cadence: 1000ms early -> 320ms late (/difficulty), floor 280ms.
    let interval = (1000 - i * 680) / cfg.difficultyMult;
    interval /= cfg.spawnRateMult;
    spawnWait = Math.max(280, interval) / 1000;
  }

  // ============================ the Ripple (right-click) ============================

  function onPointerDownGlobal(e) {
    if (e.button !== 2) return;
    if (state !== 'running' || heldNow() || !st) return;
    if (st.freezeRemainingSec > 0) return;   // a frozen field is already a free-pop window
    const x = e.clientX, y = e.clientY;
    if (!field.nearAny(x, y, RIPPLE_RADIUS_PX + RIPPLE_TRIGGER_GRACE_PX)) return;
    if (st.rippleCooldown > 0) {
      sfx('toy_denied', 0.45);
      field.floatText(`gathering… ${Math.ceil(st.rippleCooldown)}s`, x, y - 30, 'cf-pop--focus');
      return;
    }
    st.rippleCooldown = RIPPLE_RECHARGE_SEC;
    sfx('ripple_cast', 0.6);
    field.castRipple(x, y, RIPPLE_RADIUS_PX, RIPPLE_LIFE_MS);
    // P1: the ripple is a tunnel shockwave too.
    ctx.nav.fovKick(2.5);
    ctx.fx.pulseFlash(0.5);
  }
  const onContextMenu = (e) => { e.preventDefault(); };

  // ============================ lifecycle ============================

  function beginCountdown(short) {
    state = 'countdown';
    st = freshState();
    tickAcc = 0;
    spawnWait = 0.8;
    lastNoFocusAnnounce = -10;
    hudUi.update(st);
    hudUi.setVisible(true);
    if (!short) sfx('fall_in', 0.28);
    overlays.showCountdown({
      short,
      onTick: (b) => sfx(b === 'GO!' ? 'sink' : 'countdown_tick', 0.5),
      onGo: () => {
        state = 'running';
        bridge.send({ type: 'run-started', difficulty: cfg.difficulty, mode: 'dtrh-web' });
      },
    });
  }

  function endRun(ranFullCourse) {
    if (state !== 'running') return;
    state = 'recap';
    if (st.freezeRemainingSec > 0) endFreeze();
    if (ranFullCourse) awardLoopTip();   // the final loop ends at the run clock
    field.clearAll();
    ctx.director.killBoost();
    ctx.director.setTimeFactor(0.3);     // idle crawl under the recap
    sfx('surface', 0.5);
    bridge.send({
      type: 'run-ended',
      score: st.score,
      durationSec: st.runDurationSec,
      elapsedSec: st.elapsedSec,
      difficulty: cfg.difficulty,
      difficultyMult: cfg.difficultyMult,
      sparkGainMult: cfg.sparkGainMult,
      bestCombo: st.bestCombo,
      defused: st.defused,
      detonated: st.detonated,
      trickleDrops: 0,
      dripFeedMaxed: false,
    });
    overlays.showRecap({
      score: st.score,
      difficulty: cfg.difficulty,
      waveCount: st.waveCount,
      depth: ctx.nav.getDepth(),
      bestCombo: st.bestCombo,
      defused: st.defused,
      detonated: st.detonated,
    }, {
      onAgain: () => {
        overlays.hideRecap();
        ctx.director.setTimeFactor(1);
        ctx.director.reset();
        ctx.nav.reset();          // fresh plunge
        beginCountdown(true);     // the WPF RunAgain uses the short GO flash
      },
      onSurface: () => {
        // Wind the whole page down (boot sends exit + exit-done; the host closes).
        if (requestExit) requestExit();
        else bridge.send({ type: 'exit' });
      },
    });
    hudUi.update(st);
  }

  // ============================ scene integration ============================

  return {
    /** scene.start calls this once the engine is live. */
    attach(sceneCtx) {
      ctx = sceneCtx;
      field = createChaosField({
        hud: ctx.hud,
        canChannel, onBenignPopped, onFreezeCaught,
        onDefused, onDetonated, onTreatExpired, onChannelBroken,
      });
      hudUi = createChaosHud(ctx.hud);
      overlays = createOverlays(ctx.hud);
      window.addEventListener('pointerdown', onPointerDownGlobal, true);
      window.addEventListener('contextmenu', onContextMenu);
      beginCountdown(false);
    },

    /** Called every frame from the scene loop (dt already clamped). */
    frame(dt) {
      if (!field) return;
      if (state === 'running' && !heldNow()) {
        tickAcc += dt;
        while (tickAcc >= TICK) {
          tickAcc -= TICK;
          tick(TICK);
          if (state !== 'running') break;
        }
        if (state === 'running' && st.freezeRemainingSec <= 0) {
          spawnWait -= dt;
          if (spawnWait <= 0) spawnTick();
        }
      }
      field.update(dt);
    },

    /** DtRH intensity feeds the director (demoted to a presentation adapter). */
    moodIntensity() { return state === 'running' ? intensity() : 0; },

    setPaused(v) {
      paused = !!v;
      field?.setHeld(heldNow());
      field?.setInputLocked(heldNow());
    },
    setHidden(v) {
      hidden = !!v;
      field?.setHeld(heldNow());
      field?.setInputLocked(heldNow());
    },
    /** A native payload window (mandatory video) fully covers the page. */
    setCovered(v) {
      covered = !!v;
      field?.setHeld(heldNow());
      field?.setInputLocked(heldNow());
    },

    /** "surface" from the pause menu: the descent ends early - recap still pays. */
    surface() {
      if (state === 'running') endRun(false);
    },

    onPayout(m) { overlays?.showPayout(m); },

    isRunning: () => state === 'running',

    dispose() {
      window.removeEventListener('pointerdown', onPointerDownGlobal, true);
      window.removeEventListener('contextmenu', onContextMenu);
      overlays?.dispose();
      hudUi?.dispose();
      field?.dispose();
      field = null; hudUi = null; overlays = null;
    },
  };
}
