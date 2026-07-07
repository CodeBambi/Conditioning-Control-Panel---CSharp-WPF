/* ============================================================================
 * chaosRun.js - the DtRH run brain, ported from ChaosModeService.cs (M4: the
 * full roguelite). Owns the run lifecycle (countdown -> waves -> boon drafts ->
 * recap -> payout over the bridge), the 0.25s RunTick folded into the rAF
 * loop, the exact C# spawn cadence, and the full score stack:
 *
 *   TotalMult = BaseMult x ComboMult(min(1+combo*0.08, 6)) x DifficultyMult
 *               x HeatMult(1+heat) x BoonMult x UrgeMult
 *   benign pts = BasePoints * benignBaseline * payMult * pendulum * coinFlip
 *                * TotalMult * blindfoldPayMult
 *   snap pts   = BasePoints * lastBreath * slowburn * pendulum * coinFlip
 *                * TotalMult * blindfoldPayMult
 *
 * The LOADOUT (trained habits + equipped charms/accessories/toys) arrives
 * pre-applied from C# in runConfig.loadout - the host builds a ChaosRunState,
 * runs ChaosMeta.ApplyLifetimeBoons over it and snapshots the knobs, so the
 * boon math can never drift from the WPF game. Drafted mantras/sins live in
 * boons.js and mutate this state mid-run exactly like the C# Apply lambdas.
 *
 * DtRH's RunIntensity (elapsed/duration) OVERRIDES the Fall's 360s ramp; the
 * director is a speed/boost presentation adapter. No-lose: detonations cost
 * streak/heat/resistance and fire REAL native payloads over the bridge.
 *
 * NOT ported (deliberately, for now): the happy-path scripted first runs,
 * lessons/first-times, and the Madam narrative - they belong to the Warren
 * milestone (M5). Web runs play as normal unscripted descents.
 * ==========================================================================*/

import { VARIANTS, ALL_IDS, NAME_OF, pick, build, buildGolden, buildHeart, buildGoldDroplet,
  buildHeavy, buildPrism, buildBrittle, buildEcho, buildEchoChild, buildTease,
  buildBoundPair, buildChaperonePair, buildDarter, rollDarter,
  FREEZE_MAX_ON_SCREEN, SIDE_DRIFT_CHANCE, SIDE_DRIFT_GRACE_SPAWNS, MOTION,
  DEBUT_FUSE_MULT, ECHO_SPAWN_CHANCE, CHAPERONE_SPAWN_CHANCE, TEASE_SPAWN_CHANCE,
  BOUND_SPAWN_CHANCE, BRITTLE_SPAWN_CHANCE,
  TEASE_GOLD_MIN, TEASE_GOLD_MAX, TEASE_DENIED_SCORE,
  DARTER_BASE_POINTS, DARTER_QUICK_BONUS } from './variants.js';
import { draft as dealDraft, boonById } from './boons.js';
import { createChaosField } from './chaosField.js';
import { createFieldFx } from './fieldFx.js';
import { createChaosHud } from './chaosHud.js';
import { createOverlays } from './overlays.js';

// ---- economy tuning (ChaosTuning.cs / ChaosModeService.cs) ----
const FOCUS_MAX = 100, FOCUS_START = 50;
const FOCUS_PER_POP = 10, FOCUS_PER_GOLDEN = 12, FOCUS_PER_DROPLET = 4;
const FOCUS_PER_HEART = 10, FOCUS_PER_RABBIT = 15, FOCUS_PER_PRISM = 10;
const FOCUS_PER_HEAVY = 15, FOCUS_PER_DENIED = 10;
const DEFUSE_COST = 30, DEFUSE_COST_BOUND = 15;
const FOCUS_LOW_BARK_SEC = 8;
const FREEZE_DURATION_SEC = 3.5;
const FREEZE_DURATION_MULT = 2.5;   // payloads fired while frozen linger
const FREEZE_BASE_POINTS = 140;
const SLOWMO_FACTOR = 0.12;
const SLOWMO_DURATION_SEC = 6.0;
const RIPPLE_TRIGGER_GRACE_PX = 80;
const RIPPLE_WAVE_GAP_MS = 1000;
const COMBO_BIG_THRESHOLDS = [25, 50, 100];
const TICK = 0.25;                  // the C# RunTick period
const VIDEO_HEAVY_SEC = 18;         // 15s slice + open/close slack
const CASCADE_BASE_SEC = 6;         // GifCascadePayload.DURATION_SEC (+5 ride-out)
const RANK = { Curious: 0, Tempted: 1, Slipping: 2, Entranced: 3, Devoted: 4, Claimed: 5 };

const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const randInt = (a, b) => a + Math.floor(Math.random() * (b - a + 1));   // inclusive

export function createChaosGame({ bridge, runConfig, requestExit }) {
  const rc = runConfig || {};
  const lo = rc.loadout || {};
  const cfg = {
    difficulty: rc.difficulty || 'Easy',
    difficultyMult: rc.difficultyMult ?? 1.0,
    durationSec: clamp(rc.durationSec ?? 180, 60, 900),
    waveCount: clamp(rc.waveCount ?? 5, 1, 12),
    effectIntensity: clamp(rc.effectIntensity ?? 0.85, 0.2, 1.5),
    enabledVariants: rc.enabledVariants || null,
    motionOverride: rc.motionOverride && MOTION[rc.motionOverride] ? rc.motionOverride : null,
    baseMult: rc.baseMult ?? 1.0,
    sparkGainMult: rc.sparkGainMult ?? 1.0,
    spawnRateMult: clamp(rc.spawnRateMult ?? 1.0, 0.1, 10.0),
    colorFlashes: rc.colorFlashes !== false,
    boonDraftEnabled: rc.boonDraftEnabled !== false,
    allowCurses: rc.allowCurses !== false,
    dartersEnabled: rc.dartersEnabled !== false,
    draftChoices: clamp(rc.draftChoices ?? 3, 2, 4),
    draftAutoResumeSec: rc.draftAutoResumeSec ?? 15,
    sinChance: clamp(rc.sinChance ?? 0.5, 0, 1),
    pendulumSwing: !!rc.pendulumSwing,
    popupHeart: !!rc.popupHeartEnabled,
    rankIndex: rc.rankIndex ?? 0,
    equipment: rc.equipment || [],
    equippedStartBoon: rc.equippedStartBoon || null,
    toys: rc.toys || [],
    toyKeys: rc.toyKeys || ['Q', 'E'],
    thoughtTexts: (rc.thoughtTexts && rc.thoughtTexts.length ? rc.thoughtTexts : ['GIVE IN']),
  };
  // Seen-once flags: a local mutable mirror of chaos_meta (persisted one-way
  // over the bridge the moment each flips).
  const flags = Object.assign({
    seenDefuseTutorial: false, seenFocusTip: false, seenHeatTeach: false, seenRippleTeach: false,
    seenEcho: false, seenChaperone: false, seenTease: false, seenBound: false, seenBrittle: false,
    seenGoldFirst: false, seenBarkDefuseFirst: false, seenBarkDefuseNoFocus: false,
    seenBarkDefuseRelease: false, seenBarkClickDetonate: false,
  }, rc.flags || {});

  let ctx = null;      // { nav, fx, director, hud, canvas } from scene.start
  let field = null, ffx = null, hudUi = null, overlays = null;
  let state = 'boot';  // boot -> countdown -> running -> drafting -> recap
  let paused = false, hidden = false, covered = false, drafting = false;
  const heldNow = () => paused || hidden || covered || drafting;

  // ---- run state (ChaosRunState + the boon knobs) ----
  let st = null;
  function freshState() {
    return {
      score: 0, combo: 0, bestCombo: 0, heat: 0, focus: FOCUS_START,
      defused: 0, detonated: 0, effectsFired: 0, spawned: 0,
      elapsedSec: 0, runDurationSec: cfg.durationSec,
      waveIndex: 1, waveCount: cfg.waveCount, actIndex: 1,
      rippleCooldown: 0,
      freezeRemainingSec: 0, slowMoRemainingSec: 0,
      ordinarySpawns: 0, spawnSerial: 0, waveDetonations: 0, runDetonations: 0,
      endingSoonFired: false, finalLoopAnnounced: false,
      lastComboBig: 0, lastActFired: 1,

      // ---- resistance / streak protection ----
      shields: lo.shields ?? 0,
      startShields: lo.shields ?? 0,
      startingShields: lo.startingShields ?? (lo.shields ?? 0),   // hollow-heart row cap
      collarSaves: lo.collarSaves ?? 0,
      regenPops: 0,
      invulnUntil: 0,          // Snap Chain window end (performance.now ms)

      // ---- loadout knobs (pre-applied by C# ChaosMeta.ApplyLifetimeBoons) ----
      baseMult: cfg.baseMult,
      fuseTimeMult: lo.fuseTimeMult ?? rc.fuseTimeMult ?? 1.0,
      benignBaseline: lo.benignBaseline ?? 0.4,
      blindfoldPayMult: lo.blindfoldPayMult ?? 1.0,
      lastBreathWindowSec: lo.lastBreathWindowSec ?? 0,
      lastBreathPayMult: lo.lastBreathPayMult ?? 1.0,
      chanceDoubleOdds: lo.chanceDoubleOdds ?? 0,
      rerollsLeft: lo.rerollsLeft ?? 0,
      sinExtraMult: lo.sinExtraMult ?? 0,
      goldenChance: lo.goldenChance ?? 0.005,
      goldenPay: lo.goldenPayRange || [10, 20],
      dropPerPop: lo.dropPerPop ?? 0,
      dripFeedCap: lo.dripFeedCap ?? 0,
      trickleDrops: 0,
      shieldRegenPops: lo.shieldRegenPops ?? 0,
      showPopScores: !!lo.showPopScores,
      showWaveTimer: !!lo.showWaveTimer,
      rippleRechargeSec: lo.rippleRechargeSec ?? 15,
      rippleRadiusPx: lo.rippleRadiusPx ?? 260,
      rippleLifeMs: lo.rippleLifeMs ?? 520,
      rabbitRateMult: lo.rabbitRateMult ?? 1.0,
      intrusiveThoughtsSec: lo.intrusiveThoughtsSec ?? 0,
      slowMoBonusSec: lo.slowMoBonusSec ?? 0,
      bubbleScale: lo.bubbleScale ?? 1.0,
      maxedBoons: new Set(lo.maxedBoons || []),

      // ---- drafted mantra/sin knobs (boons.js apply() writes these) ----
      boonMult: 1.0, urgeMult: 1.0,
      defuseInvulnMs: 0,
      goldDigger: false, welcomeShower: false, heavyDropEvery: 0,
      ggRabbitChance: 0, snapRing: false, aftermath: false,
      pendulumPayMult: 1.0, estimChargeMult: 1, afterglowSec: 0,
      dvdSplitBounces: 0, rabbitTrailSec: 0, unleashed: false,
      electrifiedRabbits: false, estimShockwaveChance: 0,
      detonationDurationMult: 1.0, lastSecondGold: false,
      prismChance: 0, prismTreatOnly: false,
      camGirlFlee: 0, camGirlTipChance: 0,
      activesDisabled: false,
      relapseArmed: false, relapseActive: false,
      surrenderShieldUsed: false,
      takenBoonIds: new Set(),
      runPicks: [],            // ribbon tiles: { id, name, curse }
    };
  }
  const comboMult = () => Math.min(1.0 + st.combo * 0.08, 6.0);
  const heatMult = () => 1.0 + st.heat;
  const totalMult = () => st.baseMult * comboMult() * cfg.difficultyMult * heatMult() * st.boonMult * st.urgeMult;
  const basePoints = (strength) => 40 + strength * 1.6;
  const intensity = () => clamp(st ? st.elapsedSec / st.runDurationSec : 0, 0, 1);
  const chanceFlip = () => st.chanceDoubleOdds > 0 ? (Math.random() < st.chanceDoubleOdds ? 2.0 : 0.5) : 1.0;
  const pendulumFactor = () => (pendulumSlowActive && st.pendulumPayMult > 1) ? st.pendulumPayMult : 1.0;
  const goldScaled = (g) => st.relapseActive ? g * 2 : g;

  let tickAcc = 0, spawnWait = 0.8;   // the WPF spawn timer opens at 800ms
  let lastNoFocusAnnounce = -10;
  let focusLowAccum = 0, focusLowBarked = false;
  let pendingWave = 1;
  let heavyUntil = 0;                 // one heavy effect (video/cascade) at a time
  let pendulumRolledWave = 0, pendulumFireAt = 0, pendulumFired = false, pendulumSlowActive = false;
  let heartRolledWave = 0, heartFireAt = 0, heartArmed = false;
  let slowMoCueOn = false, freezeCueOn = false;
  let vibeRemainingSec = 0, afterglowApplied = false;
  let estimCharges = 0, estimMaxed = false;
  let rabbitCallPending = 0, rabbitCallMaxed = false;
  let rabbitStormSec = 0, rabbitStormAccum = 0;
  let thoughtAccum = 0;
  let heartbeatCd = 0;
  let dvdWasActive = false;
  const discovered = new Set();
  let toys = [];

  // ---- bridge helpers ----
  const sfx = (name, scale) => bridge.send({ type: 'sfx', name, scale });
  const bark = (event, data) => bridge.send({ type: 'bark', event, ...(data || {}) });
  const setFlag = (key) => {
    if (flags[key]) return false;
    flags[key] = true;
    bridge.send({ type: 'meta-command', op: 'set-flag', key });
    return true;
  };
  const firePayload = (spec, isDetonation = false) => {
    if (!spec.payload) return;
    let durationMult = st.freezeRemainingSec > 0 ? FREEZE_DURATION_MULT
      : st.slowMoRemainingSec > 0 ? 1 / SLOWMO_FACTOR : 1.0;
    if (isDetonation) durationMult *= st.detonationDurationMult;
    const kind = spec.payload.kind;
    if (isDetonation && (kind === 'video' || kind === 'gifCascade')) {
      // ONE heavy effect at a time: a heavy detonating into a running heavy is dropped.
      if (heavyActive()) {
        hudUi.toast(`▶ the deep is busy — ${NAME_OF[spec.variantId] || spec.variantId} fizzles`);
        return;
      }
      heavyUntil = performance.now() + (kind === 'video'
        ? VIDEO_HEAVY_SEC * 1000
        : (CASCADE_BASE_SEC * durationMult + 5) * 1000);
      sfx(kind === 'video' ? 'trigger' : 'fx_rain_start', 0.45);
    }
    if (isDetonation && spec.variantId === 'braindrain') sfx('fx_drain', 0.45);
    bridge.send({ type: 'fire-payload', ...spec.payload, strength: spec.strength, durationMult });
  };
  const heavyActive = () => covered || performance.now() < heavyUntil;
  const bankGold = (amount, x, y) => {
    if (amount <= 0) return;
    bridge.send({ type: 'meta-command', op: 'add-gold', amount });
    if (x != null) field.floatText(`+${amount} 🪙`, x, y + 30, 'cf-pop--gold');
    if (setFlag('seenGoldFirst')) {
      hudUi.toast('🪙 gold. she takes it at her bench.');
      bark('gold-first');
    }
  };
  const markDiscovered = (codexId) => {
    if (discovered.has(codexId)) return;
    discovered.add(codexId);
    bridge.send({ type: 'meta-command', op: 'add-to-set', set: 'discoveredCodexIds', id: codexId });
  };
  const pulse = (rgb, strength) => { if (cfg.colorFlashes) hudUi?.pulse(rgb, strength); };
  const showPopScore = (pts, x, y) => {
    if (!st.showPopScores || pts <= 0 || x == null) return;
    field.floatText('+' + Math.floor(pts).toLocaleString(), x, y + 30, 'cf-pop--score');
  };
  const bankDripFeed = () => {
    if (st.dropPerPop <= 0) return;
    const per = st.dropPerPop * (st.relapseActive ? 2 : 1);
    st.trickleDrops = Math.min(st.dripFeedCap || Infinity, st.trickleDrops + per);
  };
  const rollCamGirlTip = (x, y) => {
    if (st.camGirlTipChance <= 0 || Math.random() >= st.camGirlTipChance) return;
    bankGold(goldScaled(randInt(2, 4)), x, y);
  };
  const countRegenPop = () => {
    if (st.shieldRegenPops <= 0) return;
    if (st.shields >= st.startShields) { st.regenPops = 0; return; }
    if (++st.regenPops < st.shieldRegenPops) return;
    st.regenPops = 0;
    st.shields += 1;
    hudUi.toast('♥ resistance regrows');
    pulse('120,220,160', 0.18);
  };

  /** Cursor pull physics: The Pull minus Cam Girl, DIPs/frame -> px/s (x31). */
  const syncPhys = () => {
    field.phys.cursorPull = ((lo.cursorPullStrength ?? 0) - st.camGirlFlee) * 31;
    field.phys.rabbitHoming = (lo.cursorPullStrength ?? 0) > 0;
    field.phys.spanker = !!lo.spankerActive;
    field.phys.spankGrow = lo.spankGrowFactor ?? 1.0;
    field.phys.chainReach = lo.chainReactionReach ?? 1.0;
    field.phys.hitboxScale = lo.hitboxScale ?? rc.hitboxScale ?? 1.0;
    field.phys.magnet = !!(lo.magnetEnabled ?? rc.magnetEnabled);
    field.phys.dimOpacity = lo.blindfoldActive ? (lo.blindfoldOpacity ?? 1.0) : 1.0;
    field.phys.residueZones = st.aftermath;
    field.phys.rabbitTrailSec = st.rabbitTrailSec;
  };

  // ============================ field callbacks ============================

  function canChannel(spec) {
    if (!st) return false;
    const cost = spec.boundPairId != null ? DEFUSE_COST_BOUND : DEFUSE_COST;
    return st.freezeRemainingSec > 0 || st.focus >= cost;
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
      if (setFlag('seenBarkDefuseNoFocus')) bark('defuse-nofocus');
    } else if (reason === 'click') {
      hudUi.toast('💥 a tap isn’t a hold');
      if (setFlag('seenBarkClickDetonate')) bark('click-detonate');
    } else {
      hudUi.toast('💥 you let go');
      if (setFlag('seenBarkDefuseRelease')) bark('defuse-release');
    }
  }

  function spawnEchoChildren(spec, x, y) {
    for (let i = 0; i < 2; i++) {
      field.spawn(buildEchoChild(spec.sizePx, x + randInt(-70, 70), y + randInt(-50, 50), cfg.effectIntensity));
    }
    hudUi.toast('◌ it splits — two more');
    pulse('201,196,232', 0.30);
  }

  function onDetonated(spec, x, y) {
    if (!st || state !== 'running' || heldNow()) return;
    if (spec.isEcho) {
      spawnEchoChildren(spec, x, y);   // the Echo fires NO payload - it SPLITS
    } else {
      firePayload(spec, true);
      st.effectsFired++;
    }
    st.detonated++;
    st.runDetonations++;
    st.waveDetonations++;
    const s = spec.strength / 100;
    const diff = cfg.difficulty;

    // Snap Chain: inside the post-snap window a trigger can't take anything.
    if (performance.now() < st.invulnUntil) {
      hudUi.toast(`⛓ snap chain holds (${NAME_OF[spec.variantId] || spec.variantId})`);
      pulse('122,224,255', 0.22);
      bark('detonated-absorbed', { variant: spec.variantId, strength: spec.strength, runDetonations: st.runDetonations, combo: st.combo, difficulty: diff, shields: st.shields });
      return;
    }
    if (st.shields >= 1) {
      st.shields -= 1;
      st.heat = Math.max(0, st.heat - 0.2);
      hudUi.toast(`♥ resistance crumbles (${NAME_OF[spec.variantId] || spec.variantId})`);
      sfx(st.shields === 0 ? 'resist_crumble' : 'resist_absorb', 0.6);
      pulse('80,160,255', 0.28);
      bark('detonated-absorbed', { variant: spec.variantId, strength: spec.strength, runDetonations: st.runDetonations, combo: st.combo, difficulty: diff, shields: st.shields });
      return;
    }
    if (st.collarSaves > 0) {
      // The collar protects the chain, not the screen.
      st.collarSaves--;
      hudUi.toast(`📿 the collar holds (${st.collarSaves} left)`);
      sfx('collar_save', 0.6);
      pulse('255,215,0', 0.32);
      if (st.unleashed) {
        field.snapAll(false);
        hudUi.announce('📿 UNLEASHED', 'powerup', 2000, { artKey: 'unleashed' });
        pulse('255,215,0', 0.50);
      }
      bark('detonated-absorbed', { variant: spec.variantId, strength: spec.strength, runDetonations: st.runDetonations, combo: st.combo, difficulty: diff, shields: st.shields });
      return;
    }
    const comboBefore = st.combo;
    st.combo = 0;
    st.lastComboBig = 0;
    st.heat = 0;
    sfx('trigger', 0.55);
    pulse('255,50,50', 0.4 + s * 0.35);
    // P1: the fall STUMBLES - FOV punch + the speed boost dies on the spot.
    ctx.nav.fovKick(3 + s * 2.5);
    ctx.director.killBoost();
    hudUi.toast(`💥 ${NAME_OF[spec.variantId] || spec.variantId} triggered!`);
    hudUi.flashShields();
    bark('detonated', { variant: spec.variantId, strength: spec.strength, runDetonations: st.runDetonations, combo: comboBefore, difficulty: diff });
  }

  function onBenignPopped(spec, x, y, src) {
    if (!st || state !== 'running' || heldNow()) return;
    countRegenPop();

    if (spec.kind === 'heart') {
      st.shields += 1;
      st.focus = clamp(st.focus + FOCUS_PER_HEART, 0, FOCUS_MAX);
      hudUi.announce('💖 +1 resistance', 'powerup', 1600, { artKey: 'resistance' });
      sfx('resist_absorb', 0.55);
      pulse('120,220,160', 0.25);
      return;
    }
    if (spec.kind === 'droplet') {
      st.focus = clamp(st.focus + FOCUS_PER_DROPLET, 0, FOCUS_MAX);
      bankGold(goldScaled(randInt(3, 7)), x, y);
      sfx('golden_pop', 0.35);
      pulse('255,215,0', 0.12);
      return;
    }
    if (spec.kind === 'golden') {
      st.focus = clamp(st.focus + FOCUS_PER_GOLDEN, 0, FOCUS_MAX);
      const gold = goldScaled(randInt(st.goldenPay[0], st.goldenPay[1]));
      bankGold(gold, x, y);
      hudUi.announce(`🪙 +${gold} gold`, 'powerup', 1600);
      sfx('golden_pop', 0.6);
      pulse('255,215,0', 0.35);
      if (st.goldDigger) {
        for (let i = 0; i < 3; i++) {
          markDiscovered('bubble:gold_droplet');
          field.spawn(buildGoldDroplet(x + randInt(-50, 50), y + randInt(-20, 20)));
        }
        hudUi.toast('⛏ gold digger — it spills');
      }
      return;
    }
    if (spec.kind === 'prism') {
      st.focus = clamp(st.focus + FOCUS_PER_PRISM, 0, FOCUS_MAX);
      firePayload(spec, true);   // the sin: the copied effect fires at full strength
      st.effectsFired++;
      st.combo++;
      if (st.combo > st.bestCombo) st.bestCombo = st.combo;
      st.heat = Math.min(1.0, st.heat + 0.05);
      const prismPts = basePoints(spec.strength) * 10.0 * totalMult() * st.blindfoldPayMult;
      st.score += prismPts;
      showPopScore(prismPts, x, y);
      hudUi.announce(`🔮 the colors! 10x — it was ${NAME_OF[spec.mimicId] || spec.mimicId}`, 'bad', 2200, { artKey: 'bright_colors' });
      pulse('200,168,255', 0.40);
      // P2: the prism scrambles the tube's palette for a few seconds.
      try { ctx.fx.flashRandomTheme?.(6000); } catch (err) { /* engine build without it */ }
      rollCamGirlTip(x, y);
      checkComboMilestone();
      return;
    }

    // ---- standard treat (incl. heavies and chaperone escorts) ----
    firePayload(spec);   // benign pop = a treat: the effect IS the reward
    st.effectsFired++;
    st.combo++;
    if (st.combo > st.bestCombo) st.bestCombo = st.combo;
    const focusWasLow = st.focus < DEFUSE_COST;
    const focusGain = spec.payMult > 1 ? FOCUS_PER_HEAVY : FOCUS_PER_POP;
    st.focus = clamp(st.focus + focusGain, 0, FOCUS_MAX);
    if (focusWasLow) field.floatText(`+${focusGain} FOCUS`, x, y + 34, 'cf-pop--focus');
    st.heat = Math.min(1.0, st.heat + 0.04);
    const pts = basePoints(spec.strength) * st.benignBaseline * spec.payMult
      * pendulumFactor() * chanceFlip() * totalMult() * st.blindfoldPayMult;
    st.score += pts;
    bankDripFeed();
    showPopScore(pts, x, y);
    pulse('255,200,235', 0.10);
    if (spec.payMult > 1) hudUi.toast('🪨 heavy drop! x3');
    rollCamGirlTip(x, y);

    // E-Stim: a charged click discharges into the neighbours (nothing in reach = the charge keeps).
    if (estimCharges > 0 && src === 'pointer') {
      const struck = field.dischargeAt(x, y, { maxTargets: 3, reach: 600, chain: estimMaxed });
      if (struck > 0) {
        estimCharges--;
        sfx('estim_zap', 0.6);
        pulse('156,92,255', 0.25);
        hudUi.toast(estimCharges > 0 ? `⚡ the current arcs (${estimCharges} left)` : '⚡ the charge is spent');
      }
    }
    // Electrified Rabbits: a mowed bubble discharges free arcs.
    if (st.electrifiedRabbits && src === 'sweep') {
      field.dischargeAt(x, y, { maxTargets: 3, reach: 620 });
      sfx('estim_zap', 0.45);
    }
    // Body Buzz: one pop in eight detonates an electric shockwave.
    if (st.estimShockwaveChance > 0 && Math.random() < st.estimShockwaveChance) {
      field.dischargeAt(x, y, { maxTargets: 8, reach: 440 });
      sfx('estim_zap', 0.5);
      pulse('122,224,255', 0.25);
      hudUi.toast('⚡ body buzz — the current spreads');
    }
    // GG make more GG: sometimes a popped treat bursts into 3 wild sweeper rabbits.
    if (st.ggRabbitChance > 0 && Math.random() < st.ggRabbitChance) {
      for (let i = 0; i < 3; i++) {
        field.spawn(buildDarter(intensity(), { atX: x + randInt(-40, 40), atY: y + randInt(-40, 40), sweeper: true }));
      }
      sfx('rabbit_spawn', 0.5);
      hudUi.toast('🐇 GG! they multiply');
    }
    ctx.director.notePop(spec.variantId, 0, st.combo);
    bark('benign-popped', { variant: spec.variantId, payload: NAME_OF[spec.variantId] || spec.variantId, combo: st.combo });
    checkComboMilestone();
  }

  function onDefused(spec, fuseSecLeft, viaChannel, x, y) {
    if (!st || state !== 'running' || heldNow()) return;
    // The player's hand pays for its defuses; toys, chains and zones never do.
    // A channel completed during a freeze is free (that's the freeze's reward).
    if (viaChannel) {
      if (st.freezeRemainingSec <= 0) {
        const cost = spec.boundPairId != null ? DEFUSE_COST_BOUND : DEFUSE_COST;
        st.focus = clamp(st.focus - cost, 0, FOCUS_MAX);
      }
      if (setFlag('seenBarkDefuseFirst')) bark('defuse-first');
    }
    if (st.defuseInvulnMs > 0) st.invulnUntil = performance.now() + st.defuseInvulnMs;
    countRegenPop();
    st.defused++;
    st.combo++;
    if (st.combo > st.bestCombo) st.bestCombo = st.combo;
    st.heat = Math.min(1.0, st.heat + 0.07);
    const lastBreath = st.lastBreathWindowSec > 0 && fuseSecLeft <= st.lastBreathWindowSec
      ? st.lastBreathPayMult : 1.0;
    const slowburn = fuseSecLeft <= 1.5 && st.maxedBoons.has('slowburner') ? 3.0 : 1.0;
    const pts = basePoints(spec.strength) * 1.0 * lastBreath * slowburn
      * pendulumFactor() * chanceFlip() * totalMult() * st.blindfoldPayMult;
    st.score += pts;
    bankDripFeed();
    showPopScore(pts, x, y);
    rollCamGirlTip(x, y);
    // Playing with fire: a snap inside the final second pays gold on the spot.
    if (st.lastSecondGold && fuseSecLeft <= 1.0) {
      bankGold(goldScaled(randInt(5, 9)), x, y);
      pulse('255,140,60', 0.30);
    }
    // Aftermath: a brink-snap leaves 2s of crackling residue at the pop point.
    if (st.aftermath && fuseSecLeft <= 1.5) {
      ffx.addResidue(x, y);
      hudUi.toast('⚡ aftermath — the air still crackles');
    }
    // Size Queen: every snap rings outward and pops the treats it touches.
    if (st.snapRing) field.castRipple(x, y, 215, 420, { treatsOnly: true });
    sfx('defuse_hiss', 0.5);
    if (lastBreath > 1) {
      hudUi.announce(`⏱ last breath x${Math.round(lastBreath)}`, 'powerup', 1800);
      pulse('255,215,0', 0.40);
    } else {
      pulse('90,255,150', 0.16);
    }
    if (slowburn > 1) hudUi.toast('🐌 slow burn! x3');
    ctx.director.notePop(spec.variantId, 0, st.combo);
    bark('defused', { combo: st.combo, variant: spec.variantId, difficulty: cfg.difficulty });
    checkComboMilestone();
  }

  function onDarterCaught(spec, quick) {
    if (!st || state !== 'running' || heldNow()) return;
    firePayload(spec);   // the micro-flash (strength 8)
    const pts = (DARTER_BASE_POINTS + (quick ? DARTER_QUICK_BONUS : 0)) * totalMult();
    st.score += pts;
    st.focus = clamp(st.focus + FOCUS_PER_RABBIT, 0, FOCUS_MAX);
    st.combo++;
    if (st.combo > st.bestCombo) st.bestCombo = st.combo;
    st.heat = Math.min(1.0, st.heat + 0.05);
    // Chained catches top the window up by +0.8s instead of re-arming it.
    const extended = st.slowMoRemainingSec > 0;
    if (extended) { st.slowMoRemainingSec += 0.8; pendulumSlowActive = false; }
    else activateSlowMo();
    pulse('120,200,255', quick ? 0.32 : 0.24);
    hudUi.toast(extended ? '🐇 caught in the slow! +0.8s'
      : quick ? '⚡ quick catch! time slows' : '🐇 white rabbit caught! time slows');
    bark('darter-caught', { points: Math.round(pts), combo: st.combo, quick });
    checkComboMilestone();
  }

  function onFreezeCaught(spec) {
    if (!st || state !== 'running' || heldNow()) return;
    st.score += FREEZE_BASE_POINTS * totalMult();
    st.combo++;
    if (st.combo > st.bestCombo) st.bestCombo = st.combo;
    st.heat = Math.min(1.0, st.heat + 0.05);
    activateFreeze();
    bark('freeze-caught', { points: Math.round(FREEZE_BASE_POINTS * totalMult()), combo: st.combo });
    checkComboMilestone();
  }

  function onTeaseTouched(spec) {
    if (!st || state !== 'running' || heldNow()) return;
    st.detonated++;
    st.runDetonations++;
    st.waveDetonations++;
    const s = spec.strength / 100;
    if (st.shields >= 1) {
      // Resistance prevents only the payload - the streak still pays below.
      st.shields -= 1;
      st.heat = Math.max(0, st.heat - 0.2);
      sfx(st.shields === 0 ? 'resist_crumble' : 'resist_absorb', 0.6);
      hudUi.toast('♥ resistance takes the sting');
    } else {
      firePayload(spec, true);
      st.effectsFired++;
      sfx('trigger', 0.55);
      ctx.nav.fovKick(2 + s * 2);
      hudUi.flashShields();
    }
    st.combo = st.combo > 1 ? Math.floor(st.combo / 2) : 0;
    st.lastComboBig = 0;
    hudUi.toast(`✖ you touched it. it laughs — streak halves to ×${st.combo}`);
    pulse('255,61,90', 0.38);
    bark('tease-clicked');
  }

  let teaseDeniedThisRun = 0, teaseDeniedStreakBarked = false;

  function onTeaseDenied(spec) {
    if (!st || state !== 'running' || heldNow()) return;
    const gold = goldScaled(randInt(TEASE_GOLD_MIN, TEASE_GOLD_MAX));
    bankGold(gold);
    const pts = TEASE_DENIED_SCORE * totalMult() * st.blindfoldPayMult;
    st.score += pts;
    st.focus = clamp(st.focus + FOCUS_PER_DENIED, 0, FOCUS_MAX);
    hudUi.announce(`DENIED. +${gold} 🪙 gold`, 'powerup', 2000, { artKey: 'denied', subText: `+${gold} gold` });
    pulse('255,215,0', 0.25);
    bark('tease-denied', { count: ++teaseDeniedThisRun });
    if (teaseDeniedThisRun >= 5 && !teaseDeniedStreakBarked) {
      teaseDeniedStreakBarked = true;
      bark('tease-denied-streak', { count: teaseDeniedThisRun });
    }
  }

  function onBrittleShattered(spec) {
    if (!st || state !== 'running' || heldNow()) return;
    sfx('glass_shatter', 0.55);
    if (st.shields >= 1) {
      st.shields -= 1;
      st.heat = Math.max(0, st.heat - 0.2);
      sfx(st.shields === 0 ? 'resist_crumble' : 'resist_absorb', 0.6);
      hudUi.toast('♥ resistance takes the shards');
    } else {
      firePayload(spec, true);
      st.effectsFired++;
      hudUi.flashShields();
      hudUi.toast(`◇ it shatters — ${NAME_OF[spec.mimicId] || spec.mimicId} was inside`);
    }
    pulse('191,230,255', 0.32);
  }

  function onBoundEnraged(spec) {
    if (!st) return;
    sfx('toy_denied', 0.5);
    pulse('255,74,74', 0.30);
    hudUi.toast('⛓ the tether snaps — it enrages');
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
        hudUi.announce(`STREAK ×${t}`, 'streak', 2000, { artKey: 'streak' });
        pulse('255,230,120', 0.55);
        bark('combo-big', { combo, threshold: t });
      }
    }
    if (combo % 10 === 0) {
      pulse('255,200,60', 0.4);
      hudUi.toast(`🔥 streak ×${combo}!`);
      bark('combo-milestone', { combo, difficulty: cfg.difficulty });
    } else {
      const frac = (combo % 10) / 10;
      pulse('255,170,80', 0.04 + 0.08 * frac);
    }
  }

  // ============================ power-ups ============================

  function activateSlowMo(durationSec = null, viaPendulum = false) {
    st.slowMoRemainingSec = durationSec ?? (SLOWMO_DURATION_SEC + st.slowMoBonusSec);
    pendulumSlowActive = viaPendulum;   // "Focus here..." pays ONLY on the pendulum's own swing
    field.setTimeScale(SLOWMO_FACTOR);
    if (st.freezeRemainingSec <= 0) ctx.director.setTimeFactor(0.35);
    if (!slowMoCueOn) sfx('time_slow_in', 0.5);
    slowMoCueOn = true;
  }

  function endSlowMo() {
    st.slowMoRemainingSec = 0;
    pendulumSlowActive = false;
    field.setTimeScale(1.0);
    if (st.freezeRemainingSec <= 0 && state === 'running') ctx.director.setTimeFactor(1);
    if (slowMoCueOn) sfx('time_slow_out', 0.45);
    slowMoCueOn = false;
  }

  function activateFreeze() {
    st.freezeRemainingSec = FREEZE_DURATION_SEC;
    freezeCueOn = true;
    field.setFrozen(true);
    ctx.director.setTimeFactor(0.06);   // P1: visible time dilation - the tunnel all but stops
    sfx('freeze_catch', 0.55);
    hudUi.announce('❄ FREEZE', 'freeze', 1800, { artKey: 'freeze' });
    pulse('150,210,255', 0.30);
  }

  function endFreeze() {
    st.freezeRemainingSec = 0;
    field.setFrozen(false);
    if (state === 'running') ctx.director.setTimeFactor(st.slowMoRemainingSec > 0 ? 0.35 : 1);
    if (freezeCueOn) sfx('freeze_shatter', 0.5);
    freezeCueOn = false;
  }

  // ============================ toys (active skills) ============================

  function buildToys() {
    toys = (cfg.toys || []).map((t, i) => ({
      id: t.id, name: t.name || t.id, glyph: t.glyph || '◈', desc: t.desc || '',
      key: (t.key || cfg.toyKeys[i] || '').toUpperCase(),
      cooldownSec: t.cooldownSec ?? 0,
      power: t.power ?? 0,
      level: t.level ?? 1,
      maxed: !!t.maxed,
      chargesLeft: (t.cooldownSec ?? 0) <= 0 ? Math.round(t.power ?? 0) : -1,
      cooldownLeft: 0,
      effectActive: false,
    }));
  }

  function useToy(t) {
    if (!st || state !== 'running' || heldNow()) return;
    if (st.activesDisabled) {
      sfx('toy_denied', 0.4);
      hudUi.toast('🫦 the urge holds your hands — no toys');
      return;
    }
    if (t.cooldownLeft > 0 || t.chargesLeft === 0) { sfx('toy_denied', 0.4); return; }

    switch (t.id) {
      case 'vibe_popping':
        field.setVibe(true, t.maxed);
        vibeRemainingSec = Math.max(1, t.power);
        afterglowApplied = false;
        t.cooldownLeft = t.cooldownSec;
        sfx('vibe_buzz', 0.5);
        hudUi.toast('🔸 it buzzes. hold and sweep');
        break;
      case 'freeze_trigger':
        if (t.chargesLeft <= 0) { sfx('toy_denied', 0.4); return; }
        t.chargesLeft--;
        sfx('freeze_trigger', 0.6);
        activateFreeze();
        if (t.maxed) field.snapAll(false);
        t.cooldownLeft = 3;   // anti-doubletap between charges
        hudUi.toast('❄ everything holds still');
        break;
      case 'porn_dvd': {
        const speed = t.level === 1 ? 0.7 : t.level === 2 ? 0.85 : 1.0;
        const scale = t.level === 1 ? 0.8 : t.level === 2 ? 0.9 : 1.0;
        field.launchDvd({ durationSec: Math.max(5, t.power), speed, scale,
          count: t.maxed ? 2 : 1, splitBounces: st.dvdSplitBounces });
        sfx('dvd_launch', 0.55);
        t.cooldownLeft = t.cooldownSec;
        dvdWasActive = true;
        hudUi.toast('📀 now loading');
        break;
      }
      case 'snap_field':
        field.snapAll(t.maxed);
        sfx('freeze_trigger', 0.5);
        t.cooldownLeft = Math.max(5, t.power);   // the level value IS the cooldown
        pulse('150,220,255', 0.35);
        hudUi.toast(t.maxed ? '✋ snapped. all of it.' : '✋ snapped — every live one let go');
        break;
      case 'rabbit_caller':
        rabbitCallPending = Math.max(1, Math.round(t.power));
        rabbitCallMaxed = t.maxed;
        sfx('toy_ready', 0.5);
        t.cooldownLeft = t.cooldownSec;
        hudUi.toast('🐇 the whistle hangs — your next click calls them');
        break;
      case 'e_stim':
        estimCharges = Math.max(1, Math.round(t.power)) * Math.max(1, st.estimChargeMult);
        estimMaxed = t.maxed;
        sfx('toy_ready', 0.5);
        t.cooldownLeft = t.cooldownSec;
        pulse('156,92,255', 0.25);
        hudUi.toast(t.maxed
          ? `⚡ charged — your next ${estimCharges} pops chain-react`
          : `⚡ charged — your next ${estimCharges} pops conduct`);
        break;
    }
    hudUi.updateToys(toys, toyStatus());
  }

  const toyStatus = () => ({
    vibe: vibeRemainingSec > 0,
    freeze: st ? st.freezeRemainingSec > 0 : false,
    dvd: field ? field.dvdActive() : false,
    estim: estimCharges > 0,
    rabbits: rabbitCallPending > 0 || rabbitStormSec > 0,
  });

  function onToyKey(e) {
    if (e.repeat || !toys.length) return;
    const name = e.key.length === 1 ? e.key.toUpperCase() : e.key.toUpperCase();
    for (const t of toys) {
      if (t.key && t.key === name) { useToy(t); return; }
    }
  }

  /** Rabbit Caller: the armed whistle spends itself on the next field click. */
  function onGlobalPointerDownCapture(e) {
    if (e.button !== 0) return;
    if (rabbitCallPending <= 0 || state !== 'running' || heldNow()) return;
    const n = rabbitCallPending;
    const maxed = rabbitCallMaxed;
    rabbitCallPending = 0;
    for (let i = 0; i < n; i++) {
      markDiscovered('bubble:darter');
      field.spawn(buildDarter(intensity(), { atX: e.clientX + randInt(-60, 60), atY: e.clientY + randInt(-60, 60) }));
    }
    if (maxed) { rabbitStormSec = 10.0; rabbitStormAccum = 0; }
    sfx('rabbit_spawn', 0.5);
    hudUi.toast(maxed ? `🐇 ${n} at your fingertip… and the burrow is emptying`
      : `🐇 ${n} answered at your fingertip`);
  }

  function tickToys(dt) {
    if (vibeRemainingSec > 0) {
      vibeRemainingSec -= dt;
      if (vibeRemainingSec <= 0) {
        if (st.afterglowSec > 0 && !afterglowApplied) {
          afterglowApplied = true;
          vibeRemainingSec = st.afterglowSec;
          field.setVibe(true, true);   // the lingering window pops on hover alone
          hudUi.toast('🔸 afterglow — it lingers');
        } else {
          field.setVibe(false);
        }
      }
    }
    if (rabbitStormSec > 0) {
      rabbitStormSec -= dt;
      rabbitStormAccum += dt;
      if (rabbitStormAccum >= 1.25) {
        rabbitStormAccum = 0;
        markDiscovered('bubble:darter');
        field.spawn(buildDarter(intensity()));
      }
    }
    // Intrusive Thoughts: every 5s a stray thought races across on its own.
    if (st.intrusiveThoughtsSec > 0 && st.freezeRemainingSec <= 0) {
      thoughtAccum += dt;
      if (thoughtAccum >= 5.0) {
        thoughtAccum = 0;
        field.launchDvd({ durationSec: st.intrusiveThoughtsSec, speed: 1.3, scale: 0.8,
          count: 1, text: cfg.thoughtTexts[(Math.random() * cfg.thoughtTexts.length) | 0] });
      }
    }
    let changed = false;
    for (const t of toys) {
      if (t.cooldownLeft > 0) {
        t.cooldownLeft = Math.max(0, t.cooldownLeft - dt);
        changed = true;
        if (t.cooldownLeft <= 0 && t.chargesLeft !== 0) sfx('toy_ready', 0.45);
      }
      const on = t.id === 'vibe_popping' ? vibeRemainingSec > 0
        : t.id === 'freeze_trigger' ? st.freezeRemainingSec > 0
        : t.id === 'porn_dvd' ? field.dvdActive()
        : t.id === 'rabbit_caller' ? (rabbitCallPending > 0 || rabbitStormSec > 0)
        : t.id === 'e_stim' ? estimCharges > 0
        : false;
      if (on !== t.effectActive) { t.effectActive = on; changed = true; }
    }
    if (dvdWasActive && !field.dvdActive()) { dvdWasActive = false; changed = true; }
    if (changed || toys.length) hudUi.updateToys(toys, toyStatus());
  }

  // ============================ RunTick (0.25s) ============================

  function tick(dt) {
    st.elapsedSec += dt;
    st.heat = Math.max(0, st.heat - 0.0015);

    if (st.slowMoRemainingSec > 0) {
      st.slowMoRemainingSec -= dt;
      if (st.slowMoRemainingSec <= 0) endSlowMo();
    }
    if (st.freezeRemainingSec > 0) {
      st.freezeRemainingSec -= dt;
      if (st.freezeRemainingSec <= 0) endFreeze();
    }

    // Empty-field rescue: a fast clear shouldn't leave dead air.
    if (st.freezeRemainingSec <= 0 && field.count() === 0) spawnWait = 0;

    if (st.rippleCooldown > 0) {
      st.rippleCooldown -= dt;
      if (st.rippleCooldown <= 0) { st.rippleCooldown = 0; sfx('toy_ready', 0.3); }
    }
    // The Ripple's once-ever teach line: offered on the ready charge until the first cast.
    if (!flags.seenRippleTeach && !rippleTeachOffered && st.elapsedSec > 6 && st.rippleCooldown <= 0) {
      rippleTeachOffered = true;
      hudUi.announce('🌊 THE RIPPLE — right-click near the bubbles', 'powerup', 3200, { artKey: 'ripple_teach' });
    }
    // Once-ever gentle teach the first time focus dips under a snap's price.
    if (!flags.seenFocusTip && st.focus < DEFUSE_COST) {
      setFlag('seenFocusTip');
      hudUi.announce('focus runs low. treats refill it. snaps spend it.', 'depth', 3200);
    }
    // Once-ever heat teach: name the burn the first time it visibly climbs.
    if (!flags.seenHeatTeach && st.heat >= 0.15) {
      setFlag('seenHeatTeach');
      hudUi.announce('lust climbs while you perform. it pays up to x2', 'depth', 3200);
    }
    // rh_focus_low: a dry spell with live threats hanging -> one bark per run.
    if (!focusLowBarked) {
      if (st.focus < DEFUSE_COST && field.minFuseSec() != null) {
        focusLowAccum += dt;
        if (focusLowAccum >= FOCUS_LOW_BARK_SEC) { focusLowBarked = true; bark('focus-low'); }
      } else focusLowAccum = 0;
    }

    // Pendulum habit: once per loop, at a random beat, the world dips into slow-mo.
    if (cfg.pendulumSwing && st.waveIndex !== pendulumRolledWave) {
      pendulumRolledWave = st.waveIndex;
      pendulumFireAt = 0.15 + Math.random() * 0.65;
      pendulumFired = false;
    }
    const waveLen0 = st.runDurationSec / st.waveCount;
    const waveProgress = (st.elapsedSec % waveLen0) / waveLen0;
    if (cfg.pendulumSwing && !pendulumFired && waveProgress >= pendulumFireAt
        && st.freezeRemainingSec <= 0 && st.slowMoRemainingSec <= 0) {
      pendulumFired = true;
      activateSlowMo(2.5, true);
      sfx('ticktock', 0.45);
      if (st.pendulumPayMult > 1) hudUi.announce('🕰 FOCUS HERE — everything pays x3', 'powerup', 2400, { artKey: 'focus_here' });
      else hudUi.announce('🕰 the pendulum swings', 'powerup', 2000, { artKey: 'pendulum' });
    }
    // Pop-up Notification habit: once per loop, sometimes, a heart drifts down.
    if (cfg.popupHeart && st.waveIndex !== heartRolledWave) {
      heartRolledWave = st.waveIndex;
      heartArmed = Math.random() < 0.60;
      heartFireAt = 0.20 + Math.random() * 0.60;
    }
    if (heartArmed && waveProgress >= heartFireAt) {
      heartArmed = false;
      markDiscovered('bubble:heart');
      field.spawn(buildHeart());
      field.playChime(0.22);
    }

    tickToys(dt);
    tickHeartbeat(dt);

    if (!st.endingSoonFired && st.runDurationSec - st.elapsedSec <= 10) {
      st.endingSoonFired = true;
      hudUi.announce('the hole is closing…', 'depth', 2400, { artKey: 'ending_soon', subText: 'ten seconds' });
      bark('ending-soon');
    }

    if (st.elapsedSec >= st.runDurationSec) {
      // Relapse: the hole isn't done with you - one more loop, everything drips double.
      if (st.relapseArmed && !st.relapseActive) {
        st.relapseArmed = false;
        st.relapseActive = true;
        const waveLen = Math.round(st.runDurationSec / Math.max(1, st.waveCount));
        st.waveCount += 1;
        st.runDurationSec += waveLen;
        hudUi.announce('☠ RELAPSE — one more loop', 'bad', 2600, { artKey: 'relapse', subText: 'one more loop' });
        sfx('sin_accept', 0.6);
        bark('wave-escalated', { wave: st.waveIndex + 1 });
      } else { endRun(true); return; }
    }

    const waveLen = st.runDurationSec / st.waveCount;
    const newWave = Math.min(st.waveCount, 1 + Math.floor(st.elapsedSec / waveLen));
    if (newWave > st.waveIndex) beginWaveTransition(newWave);

    hudUi.update(hudState());
  }

  /** Blindfold capstone: a heartbeat tracks the field's closest fuse. */
  function tickHeartbeat(dt) {
    if (!st.maxedBoons.has('blindfold')) return;
    heartbeatCd -= dt;
    if (heartbeatCd > 0) return;
    const fuse = field.minFuseSec();
    if (fuse == null || fuse > 4.0) { heartbeatCd = 0; return; }
    heartbeatCd = clamp(fuse / 4.0, 0.33, 1.0);
    sfx('heartbeat', 0.5);
  }

  const hudState = () => ({
    score: st.score, totalMult: totalMult(), combo: st.combo,
    focus: st.focus, elapsedSec: st.elapsedSec, runDurationSec: st.runDurationSec,
    waveIndex: st.waveIndex, waveCount: st.waveCount,
    rippleCooldown: st.rippleCooldown,
    shields: st.shields, startingShields: st.startingShields,
    collarSaves: st.collarSaves,
    heat: st.heat,
    showClock: true,
  });

  let rippleTeachOffered = false;

  // ============================ waves + the draft table ============================

  function beginWaveTransition(newWave) {
    awardLoopTip();
    bark('wave-escalated', { wave: newWave });

    if (!cfg.boonDraftEnabled) {
      commitWave(newWave);
      return;
    }

    // The draft table: hold the field, clear it with a burst, deal the cards.
    drafting = true;
    state = 'drafting';
    syncHeld();
    field.clearAll(true);
    sfx('wave_clear', 0.6);
    pulse('150,200,255', 0.30);
    bark('wave-cleared', { wave: st.waveIndex });
    pendingWave = newWave;

    const options = dealOptions();
    overlays.showDraft({
      wave: st.waveIndex,
      options,
      autoResumeSec: cfg.draftAutoResumeSec,
      rerollsLeft: st.rerollsLeft,
      onPick: (boon) => resolveDraft(boon, false),
      onSkip: (auto) => { if (auto) bark('draft-autopick'); resolveDraft(null, auto); },
      onReroll: () => {
        if (st.rerollsLeft <= 0) return null;
        st.rerollsLeft--;
        hudUi.toast('🎲 tempted fate again');
        return { options: dealOptions(), rerollsLeft: st.rerollsLeft };
      },
    });
  }

  function dealOptions() {
    const options = dealDraft({
      allowCurses: cfg.allowCurses,
      choices: cfg.draftChoices,
      guaranteeCurse: st.maxedBoons.has('surrender'),
      takenIds: st.takenBoonIds,
      sinChance: cfg.sinChance,
      equipment: cfg.equipment,
    });
    for (const o of options) markDiscovered('boon:' + o.id);
    return options;
  }

  function applyBoon(boon, shieldDrawback = false) {
    if (shieldDrawback) { if (boon.applyShielded) boon.applyShielded(st); }
    else boon.apply(st);
    st.boonMult += boon.mult;
    st.takenBoonIds.add(boon.id);
    st.runPicks.push({ id: boon.id, name: boon.name, curse: boon.curse });
    hudUi.setPicks(st.runPicks);
    syncPhys();   // drafted knobs that live in field physics (tail-plug, aftermath, cam girl)
  }

  function resolveDraft(boon, auto) {
    if (boon) {
      const sinShielded = boon.curse && st.maxedBoons.has('surrender') && !st.surrenderShieldUsed;
      if (sinShielded) st.surrenderShieldUsed = true;
      applyBoon(boon, sinShielded);
      if (boon.curse) {
        if (st.sinExtraMult > 0) {
          st.boonMult += st.sinExtraMult;
          hudUi.toast(`🕯 sin embraced (+${st.sinExtraMult.toFixed(2)}x)`);
        }
        if (sinShielded) hudUi.toast('🕯 the candle took the sting (no drawback)');
        if (st.maxedBoons.has('surrender')) {
          st.shields += 1;
          hudUi.toast('🕯 you said yes. it gave back (+1 resistance)');
        }
        sfx('sin_accept', 0.6);
        hudUi.announce(`☠ ${boon.name}`, 'bad', 2200, { artKey: boon.id });
        bark('curse-picked', { name: boon.name, rarity: boon.rarity, mult: boon.mult });
      } else {
        sfx('boon_pick', 0.55);
        hudUi.announce(`◈ ${boon.name}`, 'powerup', 2200, { artKey: boon.id });
        bark('boon-picked', { name: boon.name });
      }
    } else {
      st.shields += 1;
      pulse('120,220,160', 0.22);
      hudUi.announce('+1 RESISTANCE', 'freeze', 2000, { artKey: 'resistance' });
      bark('boon-skipped', { shields: st.shields });
    }
    commitWave(pendingWave);
    // A brief "Ready? -> GO!" beat before the next loop resumes.
    overlays.showReadyGo(() => {
      drafting = false;
      state = 'running';
      syncHeld();
      if (st.welcomeShower) spawnWelcomeShower();
      announceFinalLoopIfEntering();
    }, { onTick: (b) => sfx(b === 'GO!' ? 'sink' : 'countdown_tick', 0.45) });
  }

  function commitWave(newWave) {
    st.waveIndex = newWave;
    st.actIndex = 1 + Math.floor((newWave - 1) / 5);
    if (st.actIndex > st.lastActFired) {
      st.lastActFired = st.actIndex;
      sfx('depth_change', 0.55);
      hudUi.announce(`DEPTH ${['I', 'II', 'III', 'IV', 'V'][st.actIndex - 1] || st.actIndex}`, 'depth', 2400, { artKey: 'depth' });
      bark('act-changed', { act: st.actIndex, wave: newWave });
    } else if (!cfg.boonDraftEnabled) {
      sfx('tunnel_zone', 0.5);
      hudUi.announce(`LOOP ${newWave}${newWave === st.waveCount ? ' — the last one' : ''}`, 'depth', 1800);
    } else {
      sfx('tunnel_zone', 0.5);
    }
    // P1: zone-shift wave transition - a fresh tunnel mood rolls in ahead.
    ctx.fx.seedZoneAhead(intensity());
    ctx.fx.pulseFlash(0.6);
    pulse('150,200,255', 0.30);
    if (!cfg.boonDraftEnabled) announceFinalLoopIfEntering();
  }

  function announceFinalLoopIfEntering() {
    if (st.finalLoopAnnounced) return;
    if (st.waveCount <= 1 || st.waveIndex < st.waveCount) return;
    st.finalLoopAnnounced = true;
    hudUi.announce('THE LAST LOOP', 'depth', 2400, { artKey: 'final_loop', subText: 'nothing after this one' });
  }

  /** Loop-clear tip: 3-6 x difficulty gold, doubled when the loop was clean. */
  function awardLoopTip() {
    const clean = st.waveDetonations === 0;
    st.waveDetonations = 0;
    let tip = Math.round(randInt(3, 6) * cfg.difficultyMult);
    if (clean) tip *= 2;
    tip = goldScaled(tip);
    bankGold(tip);
    hudUi.toast(clean ? `🪙 clean loop — she tips +${tip} gold` : `🪙 loop done — she tips +${tip} gold`);
  }

  /** Welcome Shower: a quick rain of treats from the top on every GO!. */
  function spawnWelcomeShower() {
    for (let i = 0; i < 6; i++) {
      const variant = VARIANTS[Math.random() < 0.5 ? 0 : 1];
      field.spawn(build(variant, intensity(), {
        fuseTimeMult: st.fuseTimeMult,
        motionOverride: MOTION.RainDown,
        effectIntensity: cfg.effectIntensity,
        sizeScale: st.bubbleScale,
      }));
    }
    field.playChime(0.25);
    hudUi.toast('🚿 welcome shower — treats from above');
  }

  // ============================ spawn cadence ============================

  /** Behavioral bubbles: each rolls to REPLACE the ordinary spawn slot. Gating
   * is by RANK, not difficulty; Gentle halves every roll. Debuts spawn alone
   * with a gentler trance and announce themselves. */
  function trySpawnBehavioral(effIntensity) {
    const gentleMult = cfg.difficulty === 'Easy' ? 0.5 : 1.0;
    const rank = cfg.rankIndex;

    if (rank >= RANK.Tempted && Math.random() < ECHO_SPAWN_CHANCE * gentleMult) {
      const debut = !flags.seenEcho;
      if (debut) {
        setFlag('seenEcho');
        hudUi.announce('◌ THE ECHO — hold it down, or it multiplies', 'powerup', 2800, { artKey: 'echo', subText: 'hold it down, or it multiplies' });
      }
      markDiscovered('bubble:echo');
      field.spawn(buildEcho(effIntensity, st.fuseTimeMult, st.bubbleScale, debut ? DEBUT_FUSE_MULT : 1.0));
      return true;
    }
    if (rank >= RANK.Tempted && Math.random() < CHAPERONE_SPAWN_CHANCE * gentleMult) {
      const debut = !flags.seenChaperone;
      if (debut) {
        setFlag('seenChaperone');
        hudUi.announce('💞 THE CHAPERONE — its little escort first', 'powerup', 2800, { artKey: 'chaperone', subText: 'its little escort first' });
      }
      const [liveSpec, escortSpec] = buildChaperonePair(effIntensity, st.fuseTimeMult,
        cfg.effectIntensity, st.bubbleScale, debut ? DEBUT_FUSE_MULT : 1.0);
      markDiscovered('bubble:chaperone');
      const liveB = field.spawn(liveSpec);
      const escortB = field.spawn(escortSpec);
      field.linkChaperone(liveB, escortB);
      return true;
    }
    if ((cfg.difficulty === 'Hard' || cfg.difficulty === 'Extreme' || rank >= RANK.Entranced)
        && Math.random() < BOUND_SPAWN_CHANCE * gentleMult) {
      const debut = !flags.seenBound;
      if (debut) {
        setFlag('seenBound');
        hudUi.announce('⛓ THE BOUND — both, and quickly', 'powerup', 2800, { artKey: 'bound', subText: 'both, and quickly' });
      }
      const [a, b] = buildBoundPair(effIntensity, st.fuseTimeMult, cfg.effectIntensity,
        st.bubbleScale, debut ? DEBUT_FUSE_MULT : 1.0);
      markDiscovered('bubble:bound');
      field.spawn(a);
      field.spawn(b);
      return true;
    }
    if (rank >= RANK.Slipping && Math.random() < TEASE_SPAWN_CHANCE * gentleMult) {
      const debut = !flags.seenTease;
      if (debut) {
        setFlag('seenTease');
        hudUi.announce('✖ THE TEASE — whatever you do, don\'t', 'bad', 2800, { artKey: 'tease', subText: 'whatever you do, don\'t' });
        bark('tease-debut');
      }
      markDiscovered('bubble:tease');
      field.spawn(buildTease(effIntensity, cfg.effectIntensity, st.bubbleScale));
      return true;
    }
    return false;
  }

  function spawnTick() {
    if (st.freezeRemainingSec > 0) { spawnWait = 0.3; return; }   // frozen: hold the field
    const i = intensity();
    const effIntensity = clamp(i + (cfg.difficultyMult - 1.0) * 0.15, 0, 1);
    const maxConcurrent = Math.round((6 + i * 10) * Math.sqrt(cfg.difficultyMult));

    const room = field.count() < maxConcurrent;
    const behavioralSpawned = room && trySpawnBehavioral(effIntensity);

    if (!behavioralSpawned && room) {
      // Be gentle with the tape: no video bubble while a heavy effect runs, and
      // none when the loop/run is too close to its end for the fuse + 15s slice.
      let enabled = cfg.enabledVariants;
      const waveLen = st.runDurationSec / st.waveCount;
      const waveLeft = waveLen - (st.elapsedSec % waveLen);
      const runLeft = st.runDurationSec - st.elapsedSec;
      if ((!enabled || enabled.includes('video')) && (heavyActive() || waveLeft < 14 || runLeft < 18)) {
        enabled = (enabled || ALL_IDS).filter((id) => id !== 'video');
      }
      if ((!enabled || enabled.includes('htlink')) && heavyActive()) {
        enabled = (enabled || ALL_IDS).filter((id) => id !== 'htlink');
      }

      let spec;
      if (st.heavyDropEvery > 0 && ++st.spawnSerial % st.heavyDropEvery === 0) {
        spec = buildHeavy(effIntensity, cfg.effectIntensity, st.bubbleScale);
      } else {
        const sideDrift = st.ordinarySpawns < SIDE_DRIFT_GRACE_SPAWNS ? 0 : SIDE_DRIFT_CHANCE;
        const opts = {
          enabledIds: enabled,
          fuseTimeMult: st.fuseTimeMult,
          motionOverride: cfg.motionOverride,
          effectIntensity: cfg.effectIntensity,
          sizeScale: st.bubbleScale,
          sideDriftChance: sideDrift,
        };
        spec = pick(effIntensity, opts);
        // Freeze cap: at most 2 freeze pickups live at once - re-pick without it.
        if (spec.kind === 'freeze' && field.freezeCount() >= FREEZE_MAX_ON_SCREEN) {
          const noFreeze = (enabled || ALL_IDS).filter((id) => id !== 'bambifreeze');
          spec = pick(effIntensity, { ...opts, enabledIds: noFreeze });
        }
      }
      st.ordinarySpawns++;
      st.spawned++;
      markDiscovered('bubble:' + spec.variantId);
      field.spawn(spec);

      // Lucky golden bubble: a rare bonus roll riding every ordinary spawn.
      if (Math.random() < st.goldenChance) {
        markDiscovered('bubble:golden');
        field.spawn(buildGolden());
        field.playChime(0.30);
      }
      // "Look at the bright colors..." sin: sometimes a mimic prism drifts in.
      if (st.prismChance > 0 && Math.random() < st.prismChance) {
        markDiscovered('bubble:prism');
        field.spawn(buildPrism(effIntensity, cfg.effectIntensity, st.prismTreatOnly));
      }
      // The Brittle (Tempted+, half odds on Gentle): a glass mine rides in alongside.
      if (cfg.rankIndex >= RANK.Tempted
          && Math.random() < BRITTLE_SPAWN_CHANCE * (cfg.difficulty === 'Easy' ? 0.5 : 1.0)) {
        if (!flags.seenBrittle) {
          setFlag('seenBrittle');
          hudUi.announce('◇ THE BRITTLE — don\'t even hover', 'bad', 2800, { artKey: 'brittle', subText: 'don\'t even hover' });
        }
        markDiscovered('bubble:brittle');
        field.spawn(buildBrittle(effIntensity, cfg.effectIntensity, st.bubbleScale, cfg.enabledVariants));
      }
    }

    // Darters spawn on their own intensity-scaled roll, independent of the cap.
    if (cfg.dartersEnabled) {
      const darter = rollDarter(effIntensity, st.rabbitRateMult);
      if (darter) {
        markDiscovered('bubble:darter');
        field.spawn(darter);
      }
    }

    // Refill cadence: 1000ms early -> 320ms late (/difficulty), floor 280ms.
    let interval = (1000 - i * 680) / cfg.difficultyMult;
    interval /= cfg.spawnRateMult;
    if (st.slowMoRemainingSec > 0) interval /= SLOWMO_FACTOR;   // slow-mo stretches the cadence
    spawnWait = Math.max(280, interval) / 1000;
  }

  // ============================ the Ripple (right-click) ============================

  function onPointerDownGlobal(e) {
    if (e.button !== 2) return;
    if (state !== 'running' || heldNow() || !st) return;
    if (st.freezeRemainingSec > 0) return;   // a frozen field is already a free-pop window
    const x = e.clientX, y = e.clientY;
    if (!field.nearAny(x, y, st.rippleRadiusPx + RIPPLE_TRIGGER_GRACE_PX)) return;
    if (st.rippleCooldown > 0) {
      sfx('toy_denied', 0.45);
      field.floatText(`gathering… ${Math.ceil(st.rippleCooldown)}s`, x, y - 30, 'cf-pop--focus');
      return;
    }
    st.rippleCooldown = st.rippleRechargeSec;
    if (setFlag('seenRippleTeach')) hudUi.announce('🌊 that\'s the ripple. it gathers back on its own', 'powerup', 2400);
    const skips = st.maxedBoons.has('skipping_stone');
    castRippleWave(x, y);
    if (skips) {
      for (let i = 1; i <= 2; i++) {
        window.setTimeout(() => { if (state === 'running' && !heldNow()) castRippleWave(x, y); }, i * RIPPLE_WAVE_GAP_MS);
      }
      hudUi.toast('🌊 the stone skips — three waves');
    }
  }

  function castRippleWave(x, y) {
    sfx('ripple_cast', 0.6);
    field.castRipple(x, y, st.rippleRadiusPx, st.rippleLifeMs);
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
    focusLowAccum = 0; focusLowBarked = false;
    teaseDeniedThisRun = 0; teaseDeniedStreakBarked = false;
    rippleTeachOffered = false;
    heavyUntil = 0;
    pendulumRolledWave = 0; heartRolledWave = 0; heartArmed = false;
    vibeRemainingSec = 0; afterglowApplied = false;
    estimCharges = 0; rabbitCallPending = 0; rabbitStormSec = 0; thoughtAccum = 0;
    heartbeatCd = 0;
    buildToys();
    syncPhys();

    // A pre-equipped start boon enters the run already active (before wave 1).
    if (cfg.equippedStartBoon) {
      const boon = boonById(cfg.equippedStartBoon);
      if (boon) {
        applyBoon(boon);
        markDiscovered('boon:' + boon.id);
        hudUi.announce(`◈ ${boon.name}`, 'powerup', 2200, { artKey: boon.id });
      }
    }

    hudUi.update(hudState());
    hudUi.updateToys(toys, toyStatus());
    hudUi.setPicks(st.runPicks);
    hudUi.setVisible(true);
    if (!short) sfx('fall_in', 0.28);
    overlays.showCountdown({
      short,
      onTick: (b) => sfx(b === 'GO!' ? 'sink' : 'countdown_tick', 0.5),
      onGo: () => {
        state = 'running';
        bridge.send({ type: 'run-started', difficulty: cfg.difficulty, mode: 'dtrh-web' });
        // First descent since the verb changed: one quiet line so the hold isn't a mystery.
        if (!flags.seenDefuseTutorial) {
          setFlag('seenDefuseTutorial');
          hudUi.announce('press and HOLD a live one to snap it', 'freeze', 3200);
        }
        if (st.welcomeShower) spawnWelcomeShower();
      },
    });
  }

  function endRun(ranFullCourse) {
    if (state !== 'running') return;
    state = 'recap';
    if (st.freezeRemainingSec > 0) endFreeze();
    if (st.slowMoRemainingSec > 0) endSlowMo();
    field.setVibe(false);
    if (ranFullCourse) awardLoopTip();   // the final loop ends at the run clock
    field.clearAll();
    ctx.director.killBoost();
    ctx.director.setTimeFactor(0.3);     // idle crawl under the recap
    sfx('surface', 0.5);
    const channelSec = field.channelSeconds();
    if (channelSec > 0.5) bridge.send({ type: 'meta-command', op: 'add-channel-seconds', seconds: channelSec });
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
      trickleDrops: st.trickleDrops,
      dripFeedMaxed: st.maxedBoons.has('drip_feed'),
    });
    overlays.showRecap({
      score: st.score,
      difficulty: cfg.difficulty,
      waveCount: st.waveCount,
      depth: ctx.nav.getDepth(),
      bestCombo: st.bestCombo,
      defused: st.defused,
      detonated: st.detonated,
      trickleDrops: st.trickleDrops,
    }, {
      onAgain: () => {
        overlays.hideRecap();
        ctx.director.setTimeFactor(1);
        ctx.director.reset();
        ctx.nav.reset();          // fresh plunge
        beginCountdown(true);     // the WPF RunAgain uses the short GO flash
      },
      onSurface: () => {
        if (requestExit) requestExit();
        else bridge.send({ type: 'exit' });
      },
    });
    hudUi.update(hudState());
  }

  function syncHeld() {
    field?.setHeld(heldNow());
    field?.setInputLocked(heldNow());
  }

  // ============================ scene integration ============================

  return {
    /** scene.start calls this once the engine is live. */
    attach(sceneCtx) {
      ctx = sceneCtx;
      ffx = createFieldFx(ctx.hud);
      field = createChaosField({
        hud: ctx.hud, fx: ffx,
        canChannel, onBenignPopped, onFreezeCaught,
        onDefused, onDetonated, onTreatExpired, onChannelBroken,
        onDarterCaught, onTeaseTouched, onTeaseDenied, onBrittleShattered, onBoundEnraged,
      });
      hudUi = createChaosHud(ctx.hud, { onToyUse: (id) => { const t = toys.find((x) => x.id === id); if (t) useToy(t); } });
      overlays = createOverlays(ctx.hud);
      window.addEventListener('pointerdown', onPointerDownGlobal, true);
      window.addEventListener('pointerdown', onGlobalPointerDownCapture, true);
      window.addEventListener('contextmenu', onContextMenu);
      window.addEventListener('keydown', onToyKey);
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

    setPaused(v) { paused = !!v; syncHeld(); },
    setHidden(v) { hidden = !!v; syncHeld(); },
    /** A native payload window (mandatory video) fully covers the page. */
    setCovered(v) { covered = !!v; syncHeld(); },

    /** M4: tunnel veils fire their wash only while the run is actually falling. */
    allowVeil() { return state === 'running' && !heldNow(); },

    /** "surface" from the pause menu: the descent ends early - recap still pays. */
    surface() {
      if (state === 'running') endRun(false);
    },

    onPayout(m) {
      // A rank-up card was shown - persist it so the next recap doesn't repeat it
      // (the WPF results card does the same via LastRankSeen).
      if (m.rankUp && RANK[m.rankUp] != null) {
        bridge.send({ type: 'meta-command', op: 'set-num', key: 'lastRankSeen', value: RANK[m.rankUp] });
      }
      overlays?.showPayout(m);
    },

    isRunning: () => state === 'running',

    dispose() {
      window.removeEventListener('pointerdown', onPointerDownGlobal, true);
      window.removeEventListener('pointerdown', onGlobalPointerDownCapture, true);
      window.removeEventListener('contextmenu', onContextMenu);
      window.removeEventListener('keydown', onToyKey);
      overlays?.dispose();
      hudUi?.dispose();
      field?.dispose();
      ffx?.dispose();
      field = null; hudUi = null; overlays = null; ffx = null;
    },
  };
}
