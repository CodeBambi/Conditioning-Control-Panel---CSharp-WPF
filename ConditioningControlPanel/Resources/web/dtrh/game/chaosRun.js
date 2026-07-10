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
 * Grab-in-the-tube rework: there is no pre-run loadout. Trained HABITS still
 * arrive as cfg.* knobs; toys/accessories/charms are DISCOVERED and grabbed in
 * the fall (engine/powerupDrops.js) and applied live - consumables dock (useToy),
 * passives run game/boonPassives.js apply(st) exactly like the drafted mantras in
 * boons.js. runConfig ships only each item's LEVEL + the consumable-slot count.
 *
 * DtRH's RunIntensity (elapsed/duration) OVERRIDES the Fall's 360s ramp; the
 * director is a speed/boost presentation adapter. No-lose: detonations cost
 * streak/heat/resistance and fire REAL native payloads over the bridge.
 *
 * M5: the page boots into the WARREN (warren.js - the hub over the idling
 * tunnel). A descent is dealt per-run: request-run -> the host persists the
 * setup + answers run-config -> countdown. The recap's "wake up" returns to
 * the Warren. Lessons/first-times (lessons.js) and the happy-path scripted
 * first descents (happyPath.js) ride the run's seams. The Madam narrative is
 * NOT ported - Story mode is disabled in the WPF game too (placeholder only).
 * ==========================================================================*/

import { VARIANTS, ALL_IDS, NAME_OF, pick, build, buildGolden, buildPlain, buildHeart, buildGoldDroplet,
  buildHeavy, buildPrism, buildBrittle, buildEcho, buildEchoChild, buildTease,
  buildBoundPair, buildChaperonePair, buildDarter, rollDarter,
  FREEZE_MAX_ON_SCREEN, SIDE_DRIFT_CHANCE, SIDE_DRIFT_GRACE_SPAWNS, MOTION,
  DEBUT_FUSE_MULT, ECHO_SPAWN_CHANCE, CHAPERONE_SPAWN_CHANCE, TEASE_SPAWN_CHANCE,
  BOUND_SPAWN_CHANCE, BRITTLE_SPAWN_CHANCE,
  TEASE_GOLD_MIN, TEASE_GOLD_MAX, TEASE_DENIED_SCORE,
  DARTER_BASE_POINTS, DARTER_QUICK_BONUS } from './variants.js';
import { draft as dealDraft, boonById, boonTheme, duoPartnerScore } from './boons.js';
import { PASSIVE_APPLY, isGrabbablePassive } from './boonPassives.js';
import { WEATHER_BY_ID, rollWeather } from './weather.js';
import { REGIONS, REGION_COUNT, regionForWave, profileForWave, PROFILE_NEUTRAL } from './regions.js';
import { rollBiomeIds, biomeForWave, biomeById } from './biomes.js';
import { createBiomeMech } from './biomeMech.js';
import { setAudioColor } from '../engine/audioBus.js';
import { createChaosField } from './chaosField.js';
import { createFieldFx } from './fieldFx.js';
import { createPayloadFx } from './payloadFx.js';
import { createChaosHud } from './chaosHud.js';
import { createOverlays } from './overlays.js';
import { createWarren } from './warren.js';
import { createLessonTracker } from './lessons.js';
import { createSessionMetrics } from '../engine/sessionMetrics.js';
import { createHappyPath } from './happyPath.js';
import { createVnPortrait } from './vnPortrait.js';
import { createLessonCard } from './lessonCard.js';
import { createHubGuide } from './hubGuide.js';
import { setDucked } from '../shared/audioMute.js';
import { lessonById, boonDefById, DIARY_CODEX, DIARY_VERBS, RANKS } from './catalog.js';

// ---- first-discovery lesson copy (catalog is the single source of truth) ----
// bubbles/pickups/weather: keyed by the codex id markDiscovered() stamps.
const CODEX_COPY = {};
for (const c of DIARY_CODEX) CODEX_COPY[c.codex] = { glyph: c.glyph, name: c.name, desc: c.desc, accent: c.tint };
// core verbs: keyed by a teach point, resolved by the diary line's name.
const VERB_BY_NAME = {};
for (const v of DIARY_VERBS) VERB_BY_NAME[v.name] = v;
const VERB_COPY = {
  snap:   VERB_BY_NAME['hold to snap'],
  treats: VERB_BY_NAME['click the treats'],
  ripple: VERB_BY_NAME['right-click · the ripple'],
  focus:  VERB_BY_NAME['focus'],
  panic:  VERB_BY_NAME['your panic key'],
};

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
const RIPPLE_FOCUS_COST = 30;   // BASE ripple focus cost (no cooldown; chain while focus lasts). Skipping Stone lowers the per-run cost -> st.rippleFocusCost. FOCUS_MAX 100 = 3 casts at base, more when cheaper.
const COMBO_BIG_THRESHOLDS = [25, 50, 100];
const TICK = 0.25;                  // the C# RunTick period
const PLAIN_BUBBLE_CHANCE = 0.30;   // share of ordinary spawns that are plain, effect-free soap bubbles (deep-run baseline)
const PLAIN_BUBBLE_CHANCE_EARLY = 0.80; // at the top of the run: mostly plain bubbles, effects rare - ramps down to PLAIN_BUBBLE_CHANCE as intensity peaks
const VIDEO_HEAVY_SEC = 17;         // the stuck POV video holds 15s (payloadFx VIDEO_HOLD_SEC) + slack
const CASCADE_BASE_SEC = 6;         // GifCascadePayload.DURATION_SEC (+5 ride-out)
const RANK = { Curious: 0, Tempted: 1, Slipping: 2, Entranced: 3, Devoted: 4, Claimed: 5 };

// Difficulty PACE profiles (recalibrated 2026-07). difficultyMult used to drive
// the spawner directly, which pinned Gentle at what Inescapable should feel like;
// it is now a PAYOUT scalar only (x1.0/1.3/1.7/2.2) and pace lives here.
//   spawn   - divides the refill interval (Inescapable ~= the old Gentle pace)
//   density - scales the concurrent-bubble ceiling
//   surge   - flat offset on effIntensity (fuse length / variant strength deep in the run)
//   strange - scales every behavioral roll (echo/chaperone/bound/tease/brittle)
//   fuse    - baseline trance length (multiplies rc.fuseTimeMult; >1 = longer, kinder)
const DIFF_PACE = {
  Easy:    { spawn: 0.55, density: 0.65, surge: -0.15, strange: 0.35, fuse: 1.35 },
  Medium:  { spawn: 0.75, density: 0.82, surge: -0.07, strange: 0.60, fuse: 1.15 },
  Hard:    { spawn: 0.90, density: 0.92, surge: 0.00,  strange: 0.85, fuse: 1.05 },
  Extreme: { spawn: 1.05, density: 1.00, surge: 0.05,  strange: 1.10, fuse: 0.95 },
};

const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const randInt = (a, b) => a + Math.floor(Math.random() * (b - a + 1));   // inclusive
const smoothstep = (t) => { t = clamp(t, 0, 1); return t * t * (3 - 2 * t); }; // ease-in-out 0..1

export function createChaosGame({ bridge, hostState, runSetup, requestExit, modId }) {
  // Active persona (Bambi/Sissy/Circe) - drives the VN portrait set + tint.
  const activeModId = modId || 'builtin-sissyhypno';
  // rc/lo/cfg/flags are dealt PER RUN by applyRunConfig (the host's run-config
  // answer to request-run) - the Warren can change the loadout between runs.
  let rc = {}, cfg = null, flags = {}, levels = {};

  function applyRunConfig(runConfig) {
    rc = runConfig || {};
    cfg = {
      difficulty: rc.difficulty || 'Easy',
      difficultyMult: rc.difficultyMult ?? 1.0,   // payout scalar ONLY - pace comes from DIFF_PACE
      pace: DIFF_PACE[rc.difficulty] || DIFF_PACE.Easy,
      durationSec: clamp(rc.durationSec ?? 960, 60, 1200),
      waveCount: clamp(rc.waveCount ?? REGION_COUNT, 1, 12),
      // The Four Chambers: a fixed I->IV descent, each ending in a boon Landing.
      // On by default; a legacy non-4-loop run (or an explicit opt-out) falls
      // back to the old random-weather / monotonic-intensity path.
      regionMode: rc.regionMode !== false && (rc.waveCount ?? REGION_COUNT) === REGION_COUNT,
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
      runsCompleted: rc.runsCompleted ?? 0,
      scriptedFirstRun: !!rc.scriptedFirstRun,
      equippedStartBoon: rc.equippedStartBoon || null,
      // Grab-in-the-tube rework: no pre-run loadout. Consumables (toys) are grabbed live,
      // so the toy dock starts empty; each item's dollhouse level (a grab applies at that
      // level, min 1) and the consumable-slot count come from the run-config.
      toyKeys: rc.toyKeys || ['Q', 'E'],
      consumableSlots: clamp(rc.consumableSlots ?? 1, 1, 9),
      thoughtTexts: (rc.thoughtTexts && rc.thoughtTexts.length ? rc.thoughtTexts : ['GIVE IN']),
    };
    // Per-item dollhouse levels: id -> level (>=1 discovered). A grab reads this to know
    // the strength to apply; absent/0 means "never discovered" (first grab unlocks at L1).
    levels = rc.levels || {};
    // First-discovery lessons fire once EVER. Seed the in-run `discovered` ledger from
    // the persisted set so a returning player doesn't re-see every card (the ledger is
    // otherwise fresh per app-session and markDiscovered would re-fire on each launch).
    discovered.clear();
    (rc.discoveredCodexIds || []).forEach((id) => discovered.add(id));
    rippleCastCount = 0;
    bridge.log(`runcfg: diff=${cfg.difficulty} scripted=${cfg.scriptedFirstRun} runs=${cfg.runsCompleted}`
      + ` variants=${JSON.stringify(cfg.enabledVariants)} draft=${cfg.boonDraftEnabled} sin=${cfg.sinChance}`);
    // Seen-once flags: a local mutable mirror of chaos_meta (persisted one-way
    // over the bridge the moment each flips).
    flags = Object.assign({
      seenDefuseTutorial: false, seenFocusTip: false, seenHeatTeach: false, seenRippleTeach: false,
      seenEcho: false, seenChaperone: false, seenTease: false, seenBound: false, seenBrittle: false,
      seenGoldFirst: false, seenBarkDefuseFirst: false, seenBarkDefuseNoFocus: false,
      seenBarkDefuseRelease: false, seenBarkClickDetonate: false,
      seenBraindrain: false, seenFirstSin: false, seenDuoDemo: false,
    }, rc.flags || {});
  }

  let ctx = null;      // { nav, fx, director, hud, canvas } from scene.start
  let field = null, ffx = null, hudUi = null, overlays = null, payloadFx = null;
  let bMech = null;    // the biome mechanic controller (game/biomeMech.js), built in attach
  let warren = null, lessons = null, happy = null, vn = null, lessonCardUi = null, hubGuide = null;
  let rippleCastCount = 0;   // per-run: feeds the classroom's "try the ripple" prompt
  const metrics = createSessionMetrics();   // per-run engagement counters (local-only telemetry)
  let state = 'boot';  // boot -> warren -> requesting -> countdown -> running -> drafting -> recap
  let paused = false, hidden = false, covered = false, drafting = false;
  let vnHold = false;               // the persona is mid-line: freeze the field (no spawn, no drift, no pop) until she's done
  let shortNextCountdown = false;   // "fall again" re-enters with the lone GO flash
  let scriptedDraftActive = false;  // run 1's mid-run draft: no wave commit
  const heldNow = () => paused || hidden || covered || drafting || vnHold;

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
      endingSoonFired: false, finalLoopAnnounced: false, finalLandingDone: false,
      lastComboBig: 0, lastActFired: 1,

      // ---- THE BIOMES: one rolled id per room (null on legacy/scripted runs) ----
      biomes: null,

      // ---- branching paths (junctions.js) ----
      nextJunctionSec: 45,     // first fork ~45s into the descent
      branchTint: null,        // { color, strength } - a chosen branch's decaying tube blush
      brands: {},              // tally of which trigger-word branches you keep taking

      // ---- resistance / streak protection ----
      // Grab-in-the-tube rework: the run starts NAKED. Every accessory/charm knob below
      // seeds to its bare-handed base; a grabbed passive (game/boonPassives.js) mutates st
      // mid-fall. Habit knobs still arrive at cfg level (rc.*) since habits stay always-on.
      shields: 0,
      startShields: 0,
      startingShields: 0,      // hollow-heart row cap (grows when start_resistance is grabbed)
      collarSaves: 0,
      regenPops: 0,
      invulnUntil: 0,          // Snap Chain window end (performance.now ms)

      // ---- accessory/charm knobs (base defaults; boonPassives.js writes these on grab) ----
      baseMult: cfg.baseMult,
      fuseTimeMult: (rc.fuseTimeMult ?? 1.0) * cfg.pace.fuse,       // slow_fuses habit (rc.*) x difficulty trance length
      fuseTimeMultBase: (rc.fuseTimeMult ?? 1.0) * cfg.pace.fuse,   // weather re-derives fuseTimeMult from this
      benignBaseline: 0.4,
      blindfoldPayMult: 1.0,
      lastBreathWindowSec: 0,
      lastBreathPayMult: 1.0,
      chanceDoubleOdds: 0,
      rerollsLeft: 0,
      sinExtraMult: 0,
      goldenChance: 0.005,
      goldenChanceBase: 0.005, // weather re-derives goldenChance from this
      goldenPay: [10, 20],
      moodRingLevel: 0,        // 💍 forecast / x1.5 weather / reroll
      stickyFingersLevel: 0,   // 🍯 held-card paddle pay tier
      dropPerPop: 0,
      dripFeedCap: 0,
      trickleDrops: 0,
      shieldRegenPops: 0,
      showPopScores: false,
      showWaveTimer: false,
      rippleRechargeSec: 15,   // vestigial (no cooldown) - reused below as the focus-cost dial
      rippleRadiusPx: 260,
      rippleLifeMs: 520,
      // Skipping Stone makes the ripple CHEAPER: the boon's recharge number (15 bare-handed
      // -> 13/11/9/8 by level) sets the focus cost per cast at rechargeSec*2 -> 30 -> 26/22/18/16.
      // Bare-handed base is 30, clamped to floor/ceiling.
      rippleFocusCost: clamp(Math.round(15 * 2), 15, RIPPLE_FOCUS_COST),
      rabbitRateMult: 1.0,
      intrusiveThoughtsSec: 0,
      slowMoBonusSec: 0,
      bubbleScale: 1.0,
      maxedBoons: new Set(),

      // ---- physics passives (syncPhys pushes these into field.phys; grabs mutate st) ----
      cursorPullStrength: 0,   // The Pull
      spankerActive: false,    // The Spanker
      spankGrowFactor: 1.0,
      chainReactionReach: 1.0, // Poppers (1.0 = no chain)
      blindfoldActive: false,  // Blindfold
      blindfoldOpacity: 1.0,
      hitboxScale: rc.hitboxScale ?? 1.0,   // silk_touch habit is cfg-level -> keep rc.*
      magnetEnabled: !!rc.magnetEnabled,    // cfg-level

      // ---- grab-in-the-tube run kit (grown live as you fall) ----
      takenPassiveIds: new Set(),   // double-grab guard (multiplicative passives esp.)
      // every id grabbed this run; feeds duo/trio draft gating. The pendulum is a
      // HABIT (cfg flag), not a grabbable - seed it so "Focus here..." can draft.
      runEquipment: new Set(rc.pendulumSwing ? ['pendulum_swing'] : []),
      consumableSlots: cfg.consumableSlots,   // dock cap; live dock is the module `toys` array

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
      stormChaser: false,      // the sky locks to Static + detonations arc
      liveWire: false,         // Live Wire duo: the wand's ink runs current - trail pops arc onward
      wandBounce: false,       // Hopscotch duo: rabbits ricochet off the wand's ink (each bounce = a smack)
      superconductor: false,   // Superconductor duo: pops during a freeze chain-discharge across the field
      autoplay: false,         // Autoplay duo: a DVD corner hit fires a full-screen shockwave + treat rain
      trustExercise: false,    // Trust Exercise duo: pops sonar-ping the blindfold's dimmed bubbles
      milkingMachine: false,   // Milking Machine duo: pump suction holds the ink fresh; pump end detonates it
      chargedRipple: false,    // Skinny Dipping duo: ripple-wave pops arc lightning onward
      thoughtOrbit: false,     // One-Track Mind duo: intrusive thoughts orbit the cursor
      weatherGirl: false,      // Weather Girl duo: every new sky fires a themed entrance event
      midas: false,            // Midas Ricochet duo: lucky pops gild nearby treats (gilded pops tip gold)
      loadedDice: false,       // Loaded Dice duo: 3 doubled coin-flips in a row = gold-droplet jackpot
      echoChamber: false,      // Echo Chamber duo: card pops sometimes bloom two more treats
      freefallActive: false, freefallCadence: false,  // Freefall sin: 2x fall (+25% spawns)
      spunRollRate: 0,         // Spun sin: deg/s of continuous camera roll
      privateShowPending: false, privateShowAt: 0, privateShowPayMult: 1.0,
      activesDisabled: false,
      relapseArmed: false, relapseActive: false,
      surrenderShieldUsed: false,
      takenBoonIds: new Set(),
      runPicks: [],            // ribbon tiles: { id, name, curse }
    };
  }
  const comboMult = () => Math.min(1.0 + st.combo * 0.08, 6.0);
  const heatMult = () => 1.0 + st.heat;
  const totalMult = () => st.baseMult * comboMult() * cfg.difficultyMult * heatMult() * st.boonMult * st.urgeMult * wxPay();

  // ---- THE BIOMES (game/biomes.js): the rolled place-variant of each room ----
  // A biome is the LAND under whatever sky is up: its style replaces the room's
  // classic dress, its profile replaces the spawn feel, and its wx scalars fold
  // into the same math as the weather modifiers below. Legacy/scripted runs
  // roll no biomes (st.biomes null) and everything resolves to the classics.
  const activeBiome = () => (st && cfg && cfg.regionMode ? biomeForWave(st.waveIndex, st.biomes) : null);
  const biomeAt = (waveIndex) => (st && cfg && cfg.regionMode ? biomeForWave(waveIndex, st.biomes) : null);
  /** The chamber's effective style/profile/weather: biome override or classic. */
  const styleForWaveNow = (waveIndex) => {
    const b = biomeAt(waveIndex);
    return (b && b.style) || regionForWave(waveIndex).style || null;
  };
  const profileForWaveNow = (waveIndex) => {
    const b = biomeAt(waveIndex);
    return (b && b.profile) || profileForWave(waveIndex);
  };
  const weatherIdForWave = (waveIndex) => {
    const b = biomeAt(waveIndex);
    return b && b.weatherId !== undefined ? b.weatherId : regionForWave(waveIndex).weatherId;
  };
  const bx = (key) => { const b = activeBiome(); return (b && b.wx && b.wx[key]) || null; };
  const bxMult = (key) => { const v = bx(key); return v ? v : 1; };

  // ---- Wave 2 weather math (weather.js) ----
  // Mood Ring L2 amplifies every weather effect x1.5 (bonus part only).
  const wxAmp = () => (st && st.moodRingLevel >= 2 ? 1.5 : 1.0);
  const wxMult = (v) => (weatherNow && v ? 1 + (v - 1) * wxAmp() : 1);
  const wxPay = () => wxMult(weatherNow && weatherNow.payMult) * bxMult('payMult');
  const wxHeatGain = () => wxMult(weatherNow && weatherNow.heatGain) * bxMult('heatGain');
  const wxFuse = () => wxMult(weatherNow && weatherNow.fuseMult) * bxMult('fuseMult');
  const wxSpeed = () => wxMult(weatherNow && weatherNow.speedMult);
  const wxSpawnRate = () => wxMult(weatherNow && weatherNow.spawnRate);
  const wxGolden = () => (weatherNow && weatherNow.goldenBonus ? weatherNow.goldenBonus * wxAmp() : 0) + (bx('goldenBonus') || 0);
  /** The run's resting time factor (freeze/slow-mo restore to THIS, not 1).
   * Overstim weather and the Freefall sin both live here. */
  const baseTime = () => wxSpeed() * (st && st.freefallActive ? 2 : 1);
  /** All LUST gains flow through here so Her Perfume can sweeten them. */
  const addHeat = (x) => { st.heat = Math.min(1.0, st.heat + x * wxHeatGain()); };
  const basePoints = (strength) => 40 + strength * 1.6;
  /** The descent's "how deep are we" signal (0..1), read by spawn cadence, the
   * bubble build strength, the director mood and the FX ceilings.
   *
   * Legacy: a flat monotonic ramp across the whole run.
   * Four Chambers: a per-region SAWTOOTH. Within a chamber the value eases from
   * its band.start up to band.peak; the next chamber resets to its (higher)
   * start, so the descent BREATHES instead of sliding. Bands rise region to
   * region, so the overall trend still climbs and the Court (IV) is deepest. */
  const intensity = () => {
    if (!st) return 0;
    if (!cfg || !cfg.regionMode || st.waveCount <= 1) {
      return clamp(st.elapsedSec / st.runDurationSec, 0, 1);
    }
    const regionLen = st.runDurationSec / st.waveCount;
    const region = regionForWave(st.waveIndex);
    const local = smoothstep((st.elapsedSec - (st.waveIndex - 1) * regionLen) / regionLen);
    return clamp(region.band.start + (region.band.peak - region.band.start) * local, 0, 1);
  };
  const chanceFlip = () => {
    if (st.chanceDoubleOdds <= 0) return 1.0;
    const win = Math.random() < st.chanceDoubleOdds;
    noteDiceFlip(win);   // Loaded Dice: three doubles in a row = jackpot
    return win ? 2.0 : 0.5;
  };
  const pendulumFactor = () => (pendulumSlowActive && st.pendulumPayMult > 1) ? st.pendulumPayMult : 1.0;
  const goldScaled = (g) => st.relapseActive ? g * 2 : g;

  let finalLandingActive = false;     // the Court's terminal boon draft is up
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
  const STATS_FLUSH_SEC = 15;   // how often the per-asset engagement delta is posted home
  let statsFlushCd = STATS_FLUSH_SEC;
  let dvdWasActive = false;
  const discovered = new Set();
  let toys = [];

  // ---- Wave 2: tunnel reactivity state ----
  let tiltSins = 0;          // The Tilt: unshielded sins accepted this run (max 3)
  let lustFullSeen = false;  // Lust Bleed: pink-fog hold while the bar stays high
  let weatherZone = null;    // W3 weather: the zone held for the current loop
  let comboWarp = 0;         // Heat Warp: latest combo-driven warp 0..1
  let weatherNow = null;     // the loop's rolled weather (weather.js record)
  let weatherNext = null;    // pre-rolled for the Mood Ring's forecast
  let weatherRerollUsed = false; // Mood Ring L3: one reroll per descent
  let staticBoltIn = 0;      // Static weather: seconds until the next bolt
  const weatherSeen = new Set(); // per-run: which skies already explained themselves
  let condensationIn = 0;    // W4 pickups: seconds until the next wall droplet
  let rabbitPickupWave = 0;  // W4 pickups: last loop the white rabbit rolled on
  let wandDrawSec = 0, wandArcCd = 0; // W5: the Wand's draw window + Live Wire arc pacing
  let pumpRemainingSec = 0;  // W5: the Pump's suction window
  let showActive = false, showDetonationsAt = 0; // W5: Private Show stage tracking
  let diceStreak = 0;        // Loaded Dice: consecutive doubled coin-flips
  let sonarCd = 0;           // Trust Exercise: paces the sonar rings
  let rippleArcCd = 0;       // Skinny Dipping: paces the ripple's discharges
  let autoplayCdUntil = 0;   // Autoplay: corner-shot cooldown (performance.now ms)
  let weatherGirlPending = null; // Weather Girl: sky id whose entrance fires on the next live tick

  // ---- bridge helpers ----
  // Drain the spawner's per-asset engagement delta (weighted attention + paddle
  // interactions) and post it home, where C# sums it into a cumulative store so
  // future features can bias toward the images the user actually engages with.
  function flushAssetStats() {
    if (!ctx.spawner || !ctx.spawner.drainAssetStats) return;
    const stats = ctx.spawner.drainAssetStats();
    if (stats && stats.length) bridge.send({ type: 'asset-stats', stats });
  }
  const sfx = (name, scale) => bridge.send({ type: 'sfx', name, scale });
  const bark = (event, data) => bridge.send({ type: 'bark', event, ...(data || {}) });
  // The boon draft plays IN the tube (engine/boonPick.js): the fall parks, themed
  // cards hang ahead, click one to shatter-and-drop-through. Same callback
  // contract as the old DOM overlay (overlays.showDraft), which stays as a
  // fallback if the engine didn't attach a boonPick. `sfx` gives the pick "the Pow".
  // NOTE: region-mode wave drafts now prefer the draft ROOM (openDraftRoom -
  // one tube per boon); this presenter serves the scripted run-1 draft, the
  // Court's Landing, and 4-choice drafts.
  const presentDraft = (o) => {
    if (ctx && ctx.boonPick) ctx.boonPick.open({ ...o, sfx });
    else overlays.showDraft(o);
  };
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
    // Hard cutover (2026-07): every effect renders IN-WORLD (payloadFx) instead
    // of firing a native WPF layered window over the bridge - those topmost
    // WS_EX_LAYERED surfaces were the freeze/OOM cluster's root cause, and the
    // desktop is never visible under the fullscreen game anyway. Even video is
    // in-world now (a slide-down card in front of the POV, not a native cover).
    // Only AUDIO still rides the native sfx bridge.
    if (kind === 'audio') {
      bridge.send({ type: 'fire-payload', ...spec.payload, strength: spec.strength, durationMult });
    } else {
      payloadFx?.applyPayload(spec, { isDetonation, durationMult });
    }
    lessons.onPayloadFired(kind);   // blindfold's screen-busy window
    metrics.noteEffect(kind);       // session telemetry: effects shown + est. on-screen seconds
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
  // Queue one first-discovery explainer. `copy` = { glyph, name, desc, flavor?, accent? }.
  // No-op if the card layer isn't up yet or the copy is empty. Extra = { kicker?, onDismiss? }.
  const teach = (copy, extra) => {
    if (!lessonCardUi || !copy || (!copy.name && !copy.desc)) return;
    lessonCardUi.enqueue({
      glyph: copy.glyph, name: copy.name, desc: copy.desc,
      flavor: copy.flavor || '', accent: copy.accent || copy.tint || null,
      kicker: extra && extra.kicker, onDismiss: extra && extra.onDismiss,
    });
  };
  const markDiscovered = (codexId) => {
    if (discovered.has(codexId)) return;
    discovered.add(codexId);
    bridge.send({ type: 'meta-command', op: 'add-to-set', set: 'discoveredCodexIds', id: codexId });
    // First time ever: pause + explain (bubbles/pickups/weather). Draft boons are
    // excluded - the in-tube draft already parks the fall and shows the card + desc.
    if (!codexId.startsWith('boon:') && CODEX_COPY[codexId]) teach(CODEX_COPY[codexId]);
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
  /** Loaded Dice duo: three doubled coin-flips in a row rain a droplet jackpot. */
  function noteDiceFlip(win) {
    if (!st.loadedDice) { diceStreak = 0; return; }
    diceStreak = win ? diceStreak + 1 : 0;
    if (diceStreak < 3) return;
    diceStreak = 0;
    const n = randInt(8, 12);
    for (let i = 0; i < n; i++) {
      markDiscovered('bubble:gold_droplet');
      field.spawn(buildGoldDroplet(randInt(40, Math.max(80, window.innerWidth - 40)), randInt(10, 60)));
    }
    sfx('streak_milestone', 0.6);
    hudUi.announce('🎰 JACKPOT', 'powerup', 2200, { subText: 'the dice came loaded' });
    pulse('255,215,0', 0.55);
  }
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
    field.phys.cursorPull = ((st.cursorPullStrength || 0) - st.camGirlFlee) * 31;
    field.phys.rabbitHoming = (st.cursorPullStrength || 0) > 0;
    field.phys.spanker = !!st.spankerActive;
    field.phys.spankGrow = st.spankGrowFactor ?? 1.0;
    field.phys.chainReach = st.chainReactionReach ?? 1.0;
    field.phys.hitboxScale = st.hitboxScale ?? 1.0;
    field.phys.magnet = !!st.magnetEnabled;
    field.phys.dimOpacity = st.blindfoldActive ? (st.blindfoldOpacity ?? 1.0) : 1.0;
    field.phys.residueZones = st.aftermath;
    field.phys.rabbitTrailSec = st.rabbitTrailSec;
    field.phys.trailBounce = !!st.wandBounce;
    field.phys.thoughtOrbit = !!st.thoughtOrbit;
  };

  // Sticky Fingers capstone: releasing a held card hurls it down the tube; the impact
  // rains a treat shower. Wired at run-start AND on a mid-fall grab of a maxed sticky_fingers.
  function wireStickyFingers() {
    if (!ctx || !ctx.spawner || !st) return;
    if (st.stickyFingersLevel > 0 && st.maxedBoons.has('sticky_fingers')) {
      ctx.spawner.setThrowOnRelease(() => {
        if (state !== 'running') return;
        ctx.fx.pulseFlash(0.7);
        sfx('wave_clear', 0.5);
        hudUi.toast('🍯 delivered — treats follow');
        spawnWelcomeShower();
      });
    }
  }

  // ============================ field callbacks ============================

  function canChannel(spec) {
    // Defusing a live bubble is always allowed now - the hold is free. Focus is
    // spent only by the ripple; low focus never leaves you unable to snap a live one.
    return !!st;
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
    lessons.onDetonation();   // silk_touch: this loop is no longer clean
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
    // THE BIOMES: an UNSHIELDED detonation truly landed (Fool's Casino's pot burns).
    if (bMech) bMech.onDetonated(spec, x, y);
    const comboBefore = st.combo;
    st.combo = 0;
    st.lastComboBig = 0;
    st.heat = 0;
    sfx('trigger', 0.55);
    pulse('255,50,50', 0.4 + s * 0.35);
    // P1: the fall STUMBLES - a brightness punch + the speed boost dies on the spot.
    ctx.fx.pulseFlash(0.5 + s * 0.3);
    ctx.director.killBoost();
    // Heat Warp: a big streak dying while the tube runs hot cracks real lightning
    if (comboWarp >= 0.6 || comboBefore >= 15) ctx.fx.strikeNow();
    // Storm Chaser: your detonations arc into the field (Body Buzz's reach)
    if (st.stormChaser) {
      field.dischargeAt(x, y, { maxTargets: 4, reach: 440 });
      ctx.fx.strikeNow();
      sfx('estim_zap', 0.5);
    }
    hudUi.toast(`💥 ${NAME_OF[spec.variantId] || spec.variantId} triggered!`);
    hudUi.flashShields();
    bark('detonated', { variant: spec.variantId, strength: spec.strength, runDetonations: st.runDetonations, combo: comboBefore, difficulty: diff });
    hudNow();
  }

  function onBenignPopped(spec, x, y, src) {
    if (!st || state !== 'running' || heldNow()) return;
    countRegenPop();
    metrics.noteBubblePopped();   // session telemetry: every treat/special popped

    // Trust Exercise: every pop sonar-pings the dark - dimmed bubbles light back up.
    if (st.trustExercise && st.blindfoldActive && sonarCd <= 0) {
      sonarCd = 0.9;
      field.sonarAt(x, y, 330);
    }

    if (spec.kind === 'heart') {
      lessons.onSpecialPopped();
      st.shields += 1;
      st.focus = clamp(st.focus + FOCUS_PER_HEART, 0, FOCUS_MAX);
      hudUi.announce('💖 +1 resistance', 'powerup', 1600, { artKey: 'resistance' });
      sfx('resist_absorb', 0.55);
      pulse('120,220,160', 0.25);
      return;
    }
    if (spec.kind === 'droplet') {
      lessons.onSpecialPopped();
      st.focus = clamp(st.focus + FOCUS_PER_DROPLET, 0, FOCUS_MAX);
      bankGold(goldScaled(randInt(3, 7)), x, y);
      sfx('golden_pop', 0.35);
      pulse('255,215,0', 0.12);
      return;
    }
    if (spec.kind === 'golden') {
      lessons.onSpecialPopped();
      st.focus = clamp(st.focus + FOCUS_PER_GOLDEN, 0, FOCUS_MAX);
      const gold = goldScaled(randInt(st.goldenPay[0], st.goldenPay[1]));
      // THE BIOMES: Fool's Casino stakes the payout instead of banking it -
      // when the mech owns the gold, nothing lands until the pot resolves.
      if (bMech && bMech.onGoldenPop(gold, x, y)) {
        sfx('golden_pop', 0.6);
        pulse('255,215,0', 0.35);
      } else {
        bankGold(gold, x, y);
        hudUi.announce(`🪙 +${gold} gold`, 'powerup', 1600);
        sfx('golden_pop', 0.6);
        pulse('255,215,0', 0.35);
      }
      if (st.goldDigger) {
        for (let i = 0; i < 3; i++) {
          markDiscovered('bubble:gold_droplet');
          field.spawn(buildGoldDroplet(x + randInt(-50, 50), y + randInt(-20, 20)));
        }
        hudUi.toast('⛏ gold digger — it spills');
      }
      // Midas Ricochet: the luck spreads - nearby treats gild, gilded pops tip gold.
      if (st.midas) {
        const gilded = field.gildNear(x, y, 340);
        if (gilded > 0) {
          hudUi.toast(`✨ midas — ${gilded} gilded`);
          pulse('255,215,0', 0.30);
        }
      }
      return;
    }
    if (spec.kind === 'prism') {
      lessons.onSpecialPopped();
      lessons.onPrismPopped();
      st.focus = clamp(st.focus + FOCUS_PER_PRISM, 0, FOCUS_MAX);
      firePayload(spec, true);   // the sin: the copied effect fires at full strength
      st.effectsFired++;
      st.combo++;
      if (st.combo > st.bestCombo) st.bestCombo = st.combo;
      addHeat(0.05);
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
    lessons.onTreatPopped(spec, x, y);   // vibe window / chains / the_pull / whispers
    if (spec.variantId === 'subliminal') metrics.noteSubliminalShown();
    firePayload(spec);   // benign pop = a treat: the effect IS the reward
    st.effectsFired++;
    st.combo++;
    if (st.combo > st.bestCombo) st.bestCombo = st.combo;
    const focusWasLow = st.focus < DEFUSE_COST;
    const focusGain = spec.payMult > 1 ? FOCUS_PER_HEAVY : FOCUS_PER_POP;
    st.focus = clamp(st.focus + focusGain, 0, FOCUS_MAX);
    if (focusWasLow) field.floatText(`+${focusGain} FOCUS`, x, y + 34, 'cf-pop--focus');
    addHeat(0.04);
    let pts = basePoints(spec.strength) * st.benignBaseline * spec.payMult
      * pendulumFactor() * chanceFlip() * totalMult() * st.blindfoldPayMult;
    // Sticky Fingers: pops taken by the held card pay a premium (L3 leaks gold)
    if (src === 'card' && st.stickyFingersLevel > 0) {
      pts *= st.stickyFingersLevel >= 2 ? 1.4 : 1.2;
      if (st.stickyFingersLevel >= 3 && Math.random() < 0.25) {
        markDiscovered('bubble:gold_droplet');
        field.spawn(buildGoldDroplet(x + randInt(-30, 30), y + randInt(-15, 15)));
      }
    }
    // Private Show (shielded half): pops pay x3 while she holds the stage
    if (showActive && st.privateShowPayMult > 1) pts *= st.privateShowPayMult;
    // THE BIOMES: the place weighs in - positional pay (Keyhole beams, Gallery
    // restraint) plus the mechanic's own pop reaction (Toybox mimic flips, the
    // Grey Ward's color touch, Mirror Moment chains).
    if (bMech) {
      pts *= bMech.payMultAt(x, y, spec);
      const tp = bMech.treatPop(spec, x, y, src);
      if (tp && tp.payMult) pts *= tp.payMult;
    }
    st.score += pts;
    bankDripFeed();
    showPopScore(pts, x, y);
    pulse('255,200,235', 0.10);
    if (spec.payMult > 1) hudUi.toast('🪨 heavy drop! x3');
    rollCamGirlTip(x, y);
    // Midas Ricochet: a gilded treat tips real gold on the spot.
    if (spec.gilded) bankGold(goldScaled(randInt(2, 4)), x, y);

    // E-Stim: a charged click discharges into the neighbours (nothing in reach = the charge keeps).
    let estimArced = false;
    if (estimCharges > 0 && src === 'pointer') {
      const struck = field.dischargeAt(x, y, { maxTargets: 3, reach: 600, chain: estimMaxed });
      if (struck > 0) {
        estimArced = true;
        estimCharges--;
        sfx('estim_zap', 0.6);
        pulse('156,92,255', 0.25);
        ctx.fx.strikeNow(); // Chain Mirror: the tunnel feels the current too
        hudUi.toast(estimCharges > 0 ? `⚡ the current arcs (${estimCharges} left)` : '⚡ the charge is spent');
      }
    }
    // E-Stim passive: even uncharged, the toy sometimes arcs on its own when you pop.
    // Chance scales with its level (10/15/20%) and jumps to 30% once maxed. Only your
    // own clicks roll it - automated pops (estim/chain/dvd/ink/...) would loop or spam.
    if (!estimArced && src === 'pointer') {
      const est = toys.find((t) => t.id === 'e_stim');
      if (est) {
        const chance = est.maxed ? 0.30 : [0.10, 0.15, 0.20][Math.min(est.level, 3) - 1] || 0.10;
        if (Math.random() < chance) {
          field.dischargeAt(x, y, { maxTargets: 3, reach: 600, chain: est.maxed });
          sfx('estim_zap', 0.5);
          pulse('156,92,255', 0.20);
          ctx.fx.strikeNow();
        }
      }
    }
    // Electrified Rabbits: a mowed bubble discharges free arcs.
    if (st.electrifiedRabbits && src === 'sweep') {
      field.dischargeAt(x, y, { maxTargets: 3, reach: 620 });
      sfx('estim_zap', 0.45);
      ctx.fx.strikeNow();
    }
    // Live Wire: the wand's ink runs current - a trail pop discharges onward.
    if (st.liveWire && src === 'ink' && wandArcCd <= 0) {
      wandArcCd = 0.45;
      field.dischargeAt(x, y, { maxTargets: 3, reach: 400 });
      sfx('estim_zap', 0.4);
      ctx.fx.strikeNow();
    }
    // Superconductor: a frozen field has zero resistance - your pop chains a
    // crawling bolt-cascade through everything the freeze is holding.
    if (st.superconductor && st.freezeRemainingSec > 0 && src === 'pointer') {
      const struck = field.dischargeAt(x, y, { maxTargets: 5, reach: 800, chain: true });
      if (struck > 0) {
        sfx('estim_zap', 0.6);
        pulse('150,210,255', 0.45);
        ctx.fx.strikeNow();
        hudUi.announce('⚡ SUPERCONDUCTOR', 'powerup', 1800);
      }
    }
    // Skinny Dipping: the ripple runs charged - a wave pop arcs lightning onward.
    if (st.chargedRipple && src === 'ripple' && rippleArcCd <= 0) {
      rippleArcCd = 0.4;
      field.dischargeAt(x, y, { maxTargets: 2, reach: 420 });
      sfx('estim_zap', 0.4);
      ctx.fx.strikeNow();
    }
    // Echo Chamber: treats the held card takes sometimes bloom two more.
    if (st.echoChamber && src === 'card' && spec.kind === 'treat' && Math.random() < 0.35) {
      for (let i = 0; i < 2; i++) {
        const echo = buildPlain(intensity(), { sizeScale: st.bubbleScale });
        echo.spawnAt = { x: x + randInt(-70, 70), y: y + randInt(-50, 50) };
        echo.paddleGraceMs = 900;   // let the bloom be seen before the card can take it
        field.spawn(echo);
      }
      pulse('255,200,235', 0.15);
    }
    // Body Buzz: one pop in eight detonates an electric shockwave.
    if (st.estimShockwaveChance > 0 && Math.random() < st.estimShockwaveChance) {
      field.dischargeAt(x, y, { maxTargets: 8, reach: 440 });
      sfx('estim_zap', 0.5);
      pulse('122,224,255', 0.25);
      ctx.fx.strikeNow();
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
    // Defusing is free now - the hold costs no focus (focus fuels only the ripple).
    if (viaChannel) {
      if (setFlag('seenBarkDefuseFirst')) bark('defuse-first');
    }
    if (st.defuseInvulnMs > 0) st.invulnUntil = performance.now() + st.defuseInvulnMs;
    lessons.onDefuseCompleted(fuseSecLeft, viaChannel);   // last_breath / snap_field / blindfold
    happy.onDefuseCompleted(hpIo);                        // run 1: the whitelist opens
    countRegenPop();
    // Trust Exercise: a snap pings the dark too.
    if (st.trustExercise && st.blindfoldActive && sonarCd <= 0) {
      sonarCd = 0.9;
      field.sonarAt(x, y, 330);
    }
    st.defused++;
    st.combo++;
    if (st.combo > st.bestCombo) st.bestCombo = st.combo;
    addHeat(0.07);
    const lastBreath = st.lastBreathWindowSec > 0 && fuseSecLeft <= st.lastBreathWindowSec
      ? st.lastBreathPayMult : 1.0;
    const slowburn = fuseSecLeft <= 1.5 && st.maxedBoons.has('slowburner') ? 3.0 : 1.0;
    // THE BIOMES: the chamber may weigh in on the snap (Vertigo's mid-flip
    // double, the Undertow's beaten current, the Chain Court's kept bargains).
    const bd = bMech ? bMech.onDefused(spec, fuseSecLeft, viaChannel, x, y) : null;
    const pts = basePoints(spec.strength) * 1.0 * lastBreath * slowburn
      * pendulumFactor() * chanceFlip() * totalMult() * st.blindfoldPayMult
      * (bd && bd.payMult ? bd.payMult : 1);
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
    sfx('defuse_hiss', 0.28);
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
    lessons.onRabbitCaught();
    firePayload(spec);   // the micro-flash (strength 8)
    const pts = (DARTER_BASE_POINTS + (quick ? DARTER_QUICK_BONUS : 0)) * totalMult();
    st.score += pts;
    st.focus = clamp(st.focus + FOCUS_PER_RABBIT, 0, FOCUS_MAX);
    st.combo++;
    if (st.combo > st.bestCombo) st.bestCombo = st.combo;
    addHeat(0.05);
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
    lessons.onFreezeCaught();
    st.score += FREEZE_BASE_POINTS * totalMult();
    st.combo++;
    if (st.combo > st.bestCombo) st.bestCombo = st.combo;
    addHeat(0.05);
    activateFreeze();
    bark('freeze-caught', { points: Math.round(FREEZE_BASE_POINTS * totalMult()), combo: st.combo });
    checkComboMilestone();
  }

  function onTeaseTouched(spec) {
    if (!st || state !== 'running' || heldNow()) return;
    lessons.onDetonation();   // a touched tease dirties the loop too
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
      ctx.fx.pulseFlash(0.45 + s * 0.25);
      hudUi.flashShields();
    }
    st.combo = st.combo > 1 ? Math.floor(st.combo / 2) : 0;
    st.lastComboBig = 0;
    hudUi.toast(`✖ you touched it. it laughs — streak halves to ×${st.combo}`);
    pulse('255,61,90', 0.38);
    bark('tease-clicked');
    hudNow();
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
    lessons.onDetonation();
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
    hudNow();
  }

  function checkComboMilestone() {
    hudNow(); // the badge punch should land with the pop SFX, not on the next 250ms tick
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
    if (ctx.spawner) ctx.spawner.setPickupTimeScale(SLOWMO_FACTOR);
    if (st.freezeRemainingSec <= 0) ctx.director.setTimeFactor(0.35);
    if (!slowMoCueOn) sfx('time_slow_in', 0.5);
    slowMoCueOn = true;
  }

  function endSlowMo() {
    st.slowMoRemainingSec = 0;
    pendulumSlowActive = false;
    field.setTimeScale(1.0);
    if (ctx.spawner) ctx.spawner.setPickupTimeScale(st.freezeRemainingSec > 0 ? 0 : 1);
    if (st.freezeRemainingSec <= 0 && state === 'running') ctx.director.setTimeFactor(baseTime());
    if (slowMoCueOn) sfx('time_slow_out', 0.45);
    slowMoCueOn = false;
  }

  function activateFreeze() {
    st.freezeRemainingSec = FREEZE_DURATION_SEC;
    freezeCueOn = true;
    field.setFrozen(true);
    if (ctx.spawner) ctx.spawner.setPickupTimeScale(0);
    ctx.director.setTimeFactor(0.06);   // P1: visible time dilation - the tunnel all but stops
    sfx('freeze_catch', 0.55);
    hudUi.announce('❄ FREEZE', 'freeze', 1800, { artKey: 'freeze' });
    pulse('150,210,255', 0.30);
    // The world stops with the field: ask the host to pause any native voiceline +
    // covering video for the freeze window (resumed on endFreeze). Idempotent host-side.
    try { bridge.send({ type: 'freeze-state', on: true }); } catch (e) {}
  }

  function endFreeze() {
    st.freezeRemainingSec = 0;
    field.setFrozen(false);
    if (ctx.spawner) ctx.spawner.setPickupTimeScale(st.slowMoRemainingSec > 0 ? SLOWMO_FACTOR : 1);
    if (state === 'running') ctx.director.setTimeFactor(st.slowMoRemainingSec > 0 ? 0.35 : baseTime());
    if (freezeCueOn) sfx('freeze_shatter', 0.5);
    freezeCueOn = false;
    try { bridge.send({ type: 'freeze-state', on: false }); } catch (e) {}
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
      case 'the_wand':
        // Reworked: 2s of DRAWING - the cursor lays a shimmering ink trail that
        // lingers 4 more seconds and pops/defuses whatever touches it. The tube
        // glares while the hand draws. Charge-based like the freeze.
        if (t.chargesLeft <= 0) { sfx('toy_denied', 0.4); return; }
        t.chargesLeft--;
        wandDrawSec = 2.0;
        field.wandInkStart(t.maxed);
        ctx.fx.holdFlash(true, 0.5);
        sfx('vibe_buzz', 0.6);
        t.cooldownLeft = 3;   // anti-doubletap between charges
        pulse('255,180,240', 0.25);
        hudUi.toast('🪄 draw — the line lingers');
        break;
      case 'the_pump':
        // Wave 2: seconds of hard suction (sweet kinds only), then whatever
        // arrived at the cursor bursts at once.
        pumpRemainingSec = Math.max(1, t.power);
        field.phys.pumpPull = 480;
        // Milking Machine: while the suction runs, the wand's ink refuses to fade.
        if (st.milkingMachine) {
          field.phys.inkFreeze = true;
          if (field.wandInkActive()) hudUi.toast('🫧 the ink holds its breath');
        }
        sfx('vibe_buzz', 0.5);
        t.cooldownLeft = t.cooldownSec;
        hudUi.toast('🫧 the pump draws them in');
        break;
    }
    lessons.onToyFired();   // first_play
    // Grab-in-the-tube: a grabbed toy is single-use. Consume it and renumber the dock
    // so the remaining consumables keep contiguous 1..N slot keys.
    if (t.consumable) {
      const i = toys.indexOf(t);
      if (i >= 0) toys.splice(i, 1);
      toys.forEach((x, k) => { x.key = String(k + 1); });
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
        : t.id === 'the_wand' ? (wandDrawSec > 0 || field.wandInkActive())
        : t.id === 'the_pump' ? pumpRemainingSec > 0
        : false;
      if (on !== t.effectActive) { t.effectActive = on; changed = true; }
    }
    if (dvdWasActive && !field.dvdActive()) { dvdWasActive = false; changed = true; }
    if (changed || toys.length) hudUi.updateToys(toys, toyStatus());
  }

  // ============================ Wave 2: the tunnel reacts ============================

  /** One holder of fx.forceZone: a full LUST bar outranks the loop's weather. */
  function syncZone() {
    if (!ctx) return;
    ctx.fx.forceZone(lustFullSeen ? 'pinkfog' : weatherZone);
  }

  /** Reset every tunnel-reactivity verb (run end / fresh run / back to hub). */
  function clearAmbient() {
    tiltSins = 0; lustFullSeen = false; weatherZone = null; comboWarp = 0;
    weatherNow = null; weatherNext = null; weatherRerollUsed = false; staticBoltIn = 0;
    weatherSeen.clear();
    condensationIn = 6 + Math.random() * 6;   // first droplet lands early in the run
    rabbitPickupWave = 1;                     // no rabbit on the opening loop
    wandDrawSec = 0; wandArcCd = 0; pumpRemainingSec = 0; showActive = false;
    diceStreak = 0; sonarCd = 0; rippleArcCd = 0; autoplayCdUntil = 0; weatherGirlPending = null;
    if (field) { field.phys.pumpPull = 0; field.phys.inkFreeze = false; }
    if (hudUi) hudUi.setWeather(null);
    if (!ctx) return;
    if (ctx.spawner) {
      ctx.spawner.clearPickups();
      ctx.spawner.setPickupTimeScale(1);
      ctx.spawner.setThrowOnRelease(null);
    }
    ctx.nav.setRollOffset(0);
    ctx.nav.setRollRate(0);
    ctx.nav.setSpeedCapMult(1);
    ctx.fx.holdFlash(false);   // a run ended mid-draw must not leave the glare stuck on
    ctx.fx.setTint(null);
    ctx.fx.setDensity(null, { snap: true }); // hub cadence lands whole, behind the recap/hub transition
    ctx.fx.setRushOverride(0);
    ctx.fx.holdFlash(false);
    ctx.fx.forceZone(null);
    // surface from the chambers: the tube hands the palette back to the user's
    // theme (short fade), knobs glide to 0, bloom and field accents reset
    if (ctx.fx.applyRegionGrade) ctx.fx.applyRegionGrade(null, 1.5);
    if (ctx.fx.setRegionProgress) ctx.fx.setRegionProgress(0);
    if (ffx && ffx.setRegionPalette) ffx.setRegionPalette(null);
    if (ctx.setBloomStrength) ctx.setBloomStrength(1);
  }

  // ---- Wave 2: THE WEATHER (weather.js data; one sky per loop) --------------
  function updateWeatherHud() {
    if (!hudUi) return;
    if (!weatherNow) { hudUi.setWeather(null); return; }
    hudUi.setWeather({
      glyph: weatherNow.glyph,
      name: weatherNow.name,
      desc: weatherNow.desc,
      forecast: (st.moodRingLevel >= 1 && weatherNext && !st.stormChaser)
        ? `${weatherNext.glyph} ${weatherNext.name}` : null,
      rerollable: st.moodRingLevel >= 3 && !weatherRerollUsed && !st.stormChaser && state === 'running',
    });
  }

  /** Make w the loop's sky: force the matching tunnel zone, re-derive the
   * weather-touched knobs, announce a debut. */
  function applyWeather(w, announceIt = true) {
    weatherNow = w;
    staticBoltIn = w && w.boltEverySec ? 2.5 : 0;
    st.fuseTimeMult = st.fuseTimeMultBase * wxFuse();
    st.goldenChance = st.goldenChanceBase + wxGolden();
    weatherZone = w ? w.zone : null;
    syncZone();
    if (state === 'running' && st.freezeRemainingSec <= 0 && st.slowMoRemainingSec <= 0) {
      ctx.director.setTimeFactor(baseTime());
    }
    updateWeatherHud();
    // Weather Girl duo: every new sky makes an entrance. Deferred to the next
    // live tick - a chamber boundary applies its sky while the field is still
    // held/cleared by the draft, so the fanfare waits for the fall to resume.
    if (st && st.weatherGirl && w) weatherGirlPending = w.id;
    if (w && announceIt) {
      hudUi.announce(`${w.glyph} ${w.name}`, 'depth', 2000);
      if (!weatherSeen.has(w.id)) {
        weatherSeen.add(w.id);
        hudUi.toast(`${w.glyph} ${w.desc}`);
      }
      markDiscovered('weather:' + w.id);
    }
  }

  /** Advance the sky at a loop boundary (Storm Chaser holds it at Static). */
  function advanceWeather() {
    if (st.stormChaser) {
      applyWeather(WEATHER_BY_ID.static, !weatherNow || weatherNow.id !== 'static');
      return;
    }
    // Four Chambers: the sky is DETERMINISTIC - it's whatever this region wears.
    if (cfg.regionMode) { applyRegionSky(st.waveIndex); return; }
    const w = weatherNext || rollWeather(weatherNow && weatherNow.id);
    weatherNext = rollWeather(w.id);
    applyWeather(w);
  }

  /** Dress the tube in region N's fixed sky (null = Region I's open fall) and
   * pre-set the Mood Ring forecast to the NEXT chamber. Storm Chaser still wins
   * (handled by advanceWeather before this is reached). */
  function applyRegionSky(regionIndex) {
    const region = regionForWave(regionIndex);
    // THE BIOMES: the rolled place-variant owns the chamber's sky + dress +
    // mechanic; the room keeps its band/numeral/title. No biome = the classics.
    const biome = biomeAt(regionIndex);
    const wid = weatherIdForWave(regionIndex);
    const w = wid ? WEATHER_BY_ID[wid] : null;
    const nextWid = regionIndex < REGION_COUNT ? weatherIdForWave(regionIndex + 1) : null;
    weatherNext = nextWid ? WEATHER_BY_ID[nextWid] : null;
    // The chamber banner (announceRegion) is the headline, so the sky lands
    // quietly here - just its persistent HUD chip + a first-sight desc toast,
    // never its own competing banner.
    applyWeather(w, false);
    if (w) {
      if (!weatherSeen.has(w.id)) { weatherSeen.add(w.id); hudUi.toast(`${w.glyph} ${w.desc}`); }
      markDiscovered('weather:' + w.id);
    }
    // Four Chambers wall dressing: the chamber sets how plastered the tube wall
    // is (I bare -> IV almost wall-to-wall). Scales with the descent.
    if (ctx && ctx.wall) ctx.wall.setRegion(regionIndex);
    // Four Chambers voiceover: the drift chain draws this chamber's region-tagged
    // lines (universal backbone still plays underneath). Escalates I->IV.
    // THE BIOMES: biome-tagged lines join in only while their place is up.
    if (ctx && ctx.drift) {
      ctx.drift.setRegion(cfg.regionMode ? regionIndex : 0);
      if (ctx.drift.setBiome) ctx.drift.setBiome(biome ? biome.id : null);
      // Biome lines are dual-voiced: Circe's Lock speaks her own set, every
      // other persona shares the sissy set (Bambi Sleep included).
      if (ctx.drift.setVoice) ctx.drift.setVoice(activeModId === 'builtin-locked' ? 'circe' : 'sissy');
    }
    // Four Chambers visual identity: the chamber OWNS the tube. Palette grade
    // crossfades in (~3.2s, landing as the ready-GO beat clears); the ring/
    // spiral/arm cadence snaps ONCE right here, hidden under commitWave's flash
    // (eased count changes read as a visible respace); bloom + the 2D field
    // accents finish the dress. The strobe knob is scaled by the player's
    // effect-intensity dial (photosensitivity guard).
    const style = (biome && biome.style) || region.style || null;
    ctx.fx.applyRegionGrade(style, 3.2, { strobeScale: cfg.effectIntensity });
    if (style && style.density) ctx.fx.setDensity(style.density, { snap: true });
    if (ffx && ffx.setRegionPalette) ffx.setRegionPalette(style ? style.field : null);
    if (ctx.setBloomStrength) ctx.setBloomStrength(style && style.bloom ? style.bloom : 1);
    // THE BIOMES: swap the chamber's mechanic in (exits the old one cleanly).
    if (bMech) bMech.setBiome(regionIndex, biome);
  }

  // ---- Wave 2: tunnel pickups (spawner.spawnPickup - clickable 3D objects) ----

  /** Condensation: a golden droplet beads on the tube wall and streaks past;
   * click it for +2-5 drops into the run's trickle (paid when you surface). */
  function spawnCondensation() {
    ctx.spawner.spawnPickup({
      kind: 'condensation',
      spriteUrl: 'https://ccp.art/bubbles/gold_droplet.png',
      w: 1.1, aheadDepth: 45, ttlSec: 14, glowColor: 0xffd700,
      onClick: () => {
        if (!st || state !== 'running') return;
        const n = randInt(2, 5);
        st.trickleDrops += n;
        sfx('golden_pop', 0.45);
        hudUi.toast(`💧 +${n}✦ condensed off the wall`);
        markDiscovered('pickup:condensation');
      },
    });
  }

  /** The White Rabbit: dashes down the wall faster than you fall; click him
   * before he outruns the POV for 10-20 gold. Rabbit's Foot raises his odds. */
  function spawnWhiteRabbit() {
    sfx('rabbit_spawn', 0.4);
    ctx.spawner.spawnPickup({
      kind: 'whiterabbit',
      spriteUrl: 'https://ccp.art/bubbles/darter.png',
      w: 1.7, aheadDepth: 26, ttlSec: 9,
      speed: ctx.nav.getSpeed() + 5,   // always a little quicker than you
      glowColor: 0xffffff,
      onClick: () => {
        if (!st || state !== 'running') return;
        const gold = goldScaled(randInt(10, 20));
        bankGold(gold);
        sfx('golden_pop', 0.55);
        hudUi.toast(`🐇 caught him — +${gold} 🪙. he's late. you're not.`);
        bark('rabbit-caught', { gold });
        markDiscovered('pickup:whiterabbit');
      },
      onGone: () => { if (state === 'running') hudUi.toast('🐇 he got away. late, as always'); },
    });
  }

  /** Weather Girl duo: the new sky's themed entrance event (fired from tick,
   * so the field is live and unheld when the fanfare lands). */
  function weatherGirlEntrance(id) {
    hudUi.toast('💍 weather girl — the sky shows off');
    if (id === 'static') {
      for (let i = 0; i < 5; i++) {
        window.setTimeout(() => {
          if (state !== 'running' || heldNow()) return;
          const hit = field.stormStrike(false);
          ffx.addBolt(randInt(0, window.innerWidth), 0,
            hit ? hit.x : randInt(0, window.innerWidth),
            hit ? hit.y : randInt(120, Math.max(200, window.innerHeight - 120)));
          ctx.fx.strikeNow();
          sfx('estim_zap', 0.35);
        }, i * 200);
      }
    } else if (id === 'perfume') {
      for (let i = 0; i < 2; i++) { markDiscovered('bubble:heart'); field.spawn(buildHeart()); }
      field.playChime(0.25);
      pulse('255,160,215', 0.35);
    } else if (id === 'foolsgold') {
      for (let i = 0; i < 2; i++) { markDiscovered('bubble:golden'); field.spawn(buildGolden()); }
      field.playChime(0.30);
      pulse('255,215,0', 0.35);
    } else if (id === 'stillness') {
      activateSlowMo(2.5);
      pulse('190,220,255', 0.30);
    } else if (id === 'overstim') {
      spawnWelcomeShower();
    }
  }

  /** Mood Ring L3: the HUD chip rerolls the current sky, once per descent. */
  function rerollWeather() {
    if (!st || state !== 'running' || !weatherNow) return;
    if (st.moodRingLevel < 3 || weatherRerollUsed || st.stormChaser) return;
    weatherRerollUsed = true;
    sfx('boon_pick', 0.4);
    hudUi.toast('💍 she changes her mind');
    applyWeather(rollWeather(weatherNow.id));
  }

  // ---- Branching paths ("The Junction", engine/junctions.js) ----------------
  // Mid-chamber forks: two branded mouths drift out of the fog; the player CLICKS
  // a doorway card to dive (looking around / grabbing stays free at the fork).
  // Each open doorway wears a power-up prize - the road IS the reward. The
  // surrender fork is an EASTER EGG (~1 in 100): it auto-commits at 5s and even
  // overrides a click ("the tube chose for you"). Deeper chambers pre-seal the
  // resisting mouth more often (the "closing path" - the choice quietly leaves).
  const JUNCTION_LEAD = 120;          // world units of telegraph before commit
  const JUNCTION_EVERY = 55;          // rough seconds between forks (+ jitter)
  const PRESEAL_BY_WAVE = [0, 0, 0.12, 0.4, 0.62]; // indexed by waveIndex (I..IV)
  // Each fork pairs a coaxed (deeper/surrender) mouth with a resisting one.
  const JUNCTION_PAIRS = [
    { coax: { word: 'DEEPER', color: '#c86bff', brand: 'deep' }, resist: { word: 'SLOW',  color: '#7fe0ff', brand: 'slow' } },
    { coax: { word: 'SINK',   color: '#ff6bd0', brand: 'sink' }, resist: { word: 'FLOAT', color: '#9effc0', brand: 'float' } },
    { coax: { word: 'OBEY',   color: '#ff5c8a', brand: 'obey' }, resist: { word: 'THINK', color: '#bfc6ff', brand: 'think' } },
    { coax: { word: 'MELT',   color: '#ff9a5c', brand: 'melt' }, resist: { word: 'HOLD',  color: '#a0ffe6', brand: 'hold' } },
    { coax: { word: 'GOOD',   color: '#ff6bb0', brand: 'good' }, resist: { word: 'WAIT',  color: '#8fbaff', brand: 'wait' } },
  ];

  function maybeScheduleJunction() {
    if (!cfg || !cfg.regionMode || cfg.scriptedFirstRun) return;
    if (!ctx || !ctx.junctions || ctx.junctions.isBusy()) return;
    if (heldNow() || covered) return;
    if (ctx.spawner && ctx.spawner.spotlightActive()) return;
    if (st.elapsedSec < st.nextJunctionSec) return;
    // don't collide with the region-boundary boon draft: skip near the edges
    const waveLen = st.runDurationSec / st.waveCount;
    const intoRegion = st.elapsedSec - (st.waveIndex - 1) * waveLen;
    if (intoRegion < 12 || waveLen - intoRegion < 24) { st.nextJunctionSec = st.elapsedSec + 6; return; }
    st.nextJunctionSec = st.elapsedSec + JUNCTION_EVERY + Math.random() * 16;
    fireJunction();
  }

  /** Blend a doorway's brand color 35% toward the current chamber's line hue so
   * the portal veins sit in-chamber while the brand word stays legible. */
  function chamberBlend(hex) {
    if (!cfg.regionMode) return hex;
    const style = styleForWaveNow(st.waveIndex);
    if (!style || !style.palette) return hex;
    const a = parseInt(hex.slice(1), 16), b = style.palette.colLine, t = 0.35;
    const mix = (sh) => Math.round(((a >> sh) & 255) + (((b >> sh) & 255) - ((a >> sh) & 255)) * t);
    return `#${((1 << 24) | (mix(16) << 16) | (mix(8) << 8) | mix(0)).toString(16).slice(1)}`;
  }

  function fireJunction() {
    const pair = JUNCTION_PAIRS[Math.floor(Math.random() * JUNCTION_PAIRS.length)];
    const coaxSide = Math.random() < 0.5 ? 'left' : 'right';
    // clone the branded descs - rewards are per-fork and must never stick to the
    // shared JUNCTION_PAIRS catalog entries
    const left = { ...(coaxSide === 'left' ? pair.coax : pair.resist) };
    const right = { ...(coaxSide === 'left' ? pair.resist : pair.coax) };
    left.color = chamberBlend(left.color);
    right.color = chamberBlend(right.color);
    // each doorway carries a PRIZE, overlaid on its card (a nameplate chip): the
    // road you take is the power-up you get, the other one is gone - so choosing
    // matters. Same offer pool + veto as the drifting drops; two distinct items.
    // A starved pool (low rank / everything grabbed this run / dock full) falls
    // back to a flat 100-gold pouch so an open doorway is never prizeless.
    // Per-door try/catch (2026-07): the old single try around BOTH assignments
    // meant one bad offer silently stripped BOTH doors - the "empty room" bug.
    // junctions.buildVein carries the same 100-gold fallback as the last line
    // of defense, so even a failure HERE can't produce a bare entrance.
    const goldPrize = () => ({ kind: 'gold', amount: 100, name: '100 Gold', glyph: '🪙',
      desc: 'a pouch of gold, no questions asked. spend it at the dollhouse.',
      frameCol: 0xf2c14e, capCol: '#f2c14e' });   // warm gold card, not the relic violet
    const offer = (excludeIds) => {
      try {
        return ctx.powerupDrops && ctx.powerupDrops.pickOffer ? ctx.powerupDrops.pickOffer(excludeIds) : null;
      } catch (e) {
        console.warn('[dtrh] junction prize offer failed - gold pouch takes the doorway:', e);
        return null;
      }
    };
    const a = offer(null);
    const b = offer(a ? [a.def.id] : null);
    left.reward = a ? { id: a.def.id, kind: a.kind, name: a.def.name, glyph: a.def.glyph || '◈', desc: a.def.desc || '' } : goldPrize();
    right.reward = b ? { id: b.def.id, kind: b.kind, name: b.def.name, glyph: b.def.glyph || '◈', desc: b.def.desc || '' } : goldPrize();
    const presealChance = PRESEAL_BY_WAVE[Math.min(4, st.waveIndex)] || 0;
    let presealIndex = null;
    if (Math.random() < presealChance) presealIndex = (coaxSide === 'left') ? 1 : 0; // seal the resisting mouth
    ctx.junctions.schedule({
      atDepth: ctx.nav.getDepth() + JUNCTION_LEAD,
      branches: [left, right],
      coaxIndex: coaxSide === 'left' ? 0 : 1,
      presealIndex,
      mode: 'fork',
    });
    bark('junction-near');
  }

  /** The fork committed (engine reports the winning branch). Tally the brand,
   * blush the tube in the branch's color for a stretch, and mark the choice. */
  function onJunctionChosen(choice) {
    if (!st || !choice || !choice.branch) return;
    const b = choice.branch;
    st.brands[b.brand] = (st.brands[b.brand] || 0) + 1;
    st.branchTint = { color: b.color, strength: 0.6 };
    ctx.fx.pulseFlash(0.5);
    sfx(choice.forced || choice.overridden ? 'sink' : 'boon_pick', 0.4);
    // overridden = the 1-in-100 surrender fork snatched the choice OVER a click
    hudUi.announce(b.word, 'depth', 1500, {
      subText: choice.forced ? 'the only way left'
        : choice.overridden ? 'the tube chose for you'
          : (choice.passive ? 'you let it choose' : ''),
    });
    hudUi.toast(choice.forced ? `↳ ${b.word} — the only way left`
      : choice.overridden ? `↳ ${b.word} — the tube chose for you`
        : choice.passive ? `↳ ${b.word} — you let it choose` : `↳ ${b.word}`);
    bark(choice.forced || choice.overridden ? 'junction-forced' : 'junction-chosen', { word: b.word, brand: b.brand });
    metrics.noteJunction({ forced: choice.forced || choice.overridden, passive: choice.passive });
    markDiscovered('junction:' + b.brand);
    // the chosen doorway pays its prize: same discovery/dock/apply path as a
    // grabbed drop (first-ever discovery still fires its pause+explain card).
    // The card shattered mid-screen, so the HUD art flies from the center.
    // Gold pouches (the starved-pool fallback) just bank on the spot.
    if (b.reward && state === 'running') {
      if (b.reward.kind === 'gold') {
        const gold = goldScaled(b.reward.amount || 100);
        bankGold(gold, window.innerWidth / 2, window.innerHeight / 2);
        hudUi.announce(`🪙 +${gold} gold`, 'powerup', 1600);
        sfx('golden_pop', 0.55);
      } else {
        handlePowerupGrab(b.reward.id, b.reward.kind, { x: window.innerWidth / 2, y: window.innerHeight / 2 });
      }
    }
  }

  /** The faller has reached the fork mouth: crawl the tunnel so the branch can be
   * read + chosen (the engine owns the linger; only ~1 fork in 10 auto-commits at
   * 5s, the rest wait for the click with a 30s failsafe), then hand the speed
   * back on commit/abort. Respects a concurrent freeze/slow-mo on release. */
  function onJunctionLinger(on) {
    if (!ctx || !ctx.director) return;
    if (on) {
      // reaching a room ends the heavies early: the stuck POV video recedes and
      // any running gif rain stops, so neither plays over the door read
      try { if (payloadFx && payloadFx.cancelHeavy && payloadFx.cancelHeavy()) heavyUntil = 0; } catch (e) { /* ignore */ }
      ctx.director.setTimeFactor(0.16);   // near-hover at the Y so both mouths can be read
      try { if (hudUi) hudUi.toast('click a card to choose ...'); } catch (e) { /* ignore */ }
    } else if (state === 'running' || state === 'drafting') {
      // restore whatever the world time-factor should currently be. 'drafting'
      // is the boon room: its commit lands while the draft is still held, and
      // without this branch the 0.16 hover stuck to the whole next chamber.
      ctx.director.setTimeFactor(
        st.freezeRemainingSec > 0 ? 0.06
          : st.slowMoRemainingSec > 0 ? 0.35
            : baseTime()
      );
    }
  }

  // ---- The boon draft as a ROOM ("choose your door") -------------------------
  // Four Chambers: leaving a chamber, the draft is no longer a parked card row -
  // the fall arrives at a draft ANTECHAMBER with one tube per dealt boon, each
  // doorway gated by that boon's card. Click a card = take the boon + dive its
  // tube (the next chamber rebases at the vein exit, so the door you take IS
  // the chamber you land in). The resist button / timeout dives the coaxed door
  // with no boon (+1 resistance, same as the old table). Reroll re-deals the
  // door cards in place. The old in-tube card row (engine/boonPick.js) stays
  // for 4-choice drafts (three doors is the room's geometric max), the run-1
  // scripted draft (fires at an arbitrary mid-loop beat) and the Court's
  // terminal Landing (no next chamber to dive into).
  const DRAFT_ROOM_LEAD = 70;   // shorter telegraph than a fork: the field is already held+cleared
  // door tint per boon theme (mirrors boonPick's THEMES palette)
  const DRAFT_DOOR_COLORS = {
    sin: '#ff2b4a', electric: '#8fdcff', rabbit: '#c9a2ff', duo: '#ffd76a',
    rare: '#c178ff', uncommon: '#66e0d0', common: '#ccd6e6',
  };
  let draftRoomActive = false;   // a draft room is scheduled/lingering (drafting stays true throughout)
  let draftRoomSkip = false;     // the resist button fired; the commit resolves boonless
  let draftRoomDoors = 3;        // dealt door count (draftChoices can be 2)
  let draftDom = null;           // lazy DOM chrome: caption + reroll/resist (reuses boonPick's CSS)
  let draftCapTimer = null;      // 250ms caption countdown while the room lingers

  function boonBranch(boon) {
    const color = DRAFT_DOOR_COLORS[boonTheme(boon)] || DRAFT_DOOR_COLORS.common;
    return {
      word: boon.name, color, boon,
      reward: {
        id: boon.id, kind: boon.curse ? 'sin' : 'boon',
        name: boon.name, glyph: boon.curse ? '☠' : '◈', desc: boon.desc || '',
        frameCol: parseInt(color.slice(1), 16), capCol: color,
      },
    };
  }

  function ensureDraftDom() {
    if (draftDom) return draftDom;
    const root = document.createElement('div');
    root.className = 'cf-boonpick';
    root.hidden = true;
    const cap = document.createElement('div');
    cap.className = 'cf-boonpick-cap';
    const btns = document.createElement('div');
    btns.className = 'cf-boonpick-btns';
    const reroll = document.createElement('button');
    reroll.type = 'button'; reroll.className = 'sf-btn cf-boonpick-reroll';
    const resist = document.createElement('button');
    resist.type = 'button'; resist.className = 'sf-btn cf-boonpick-resist';
    resist.textContent = '♥ resist (+1)';
    btns.append(reroll, resist);
    root.append(cap, btns);
    ((ctx && ctx.hud) || document.body).appendChild(root);
    reroll.addEventListener('click', onDraftReroll);
    resist.addEventListener('click', onDraftResist);
    draftDom = { root, cap, reroll, resist };
    return draftDom;
  }

  function syncDraftButtons() {
    if (!draftDom) return;
    draftDom.reroll.textContent = `🎲 reroll (${st.rerollsLeft})`;
    draftDom.reroll.hidden = !(st.rerollsLeft > 0);
    draftDom.resist.hidden = !(cfg.runsCompleted >= 2);   // same reveal gate as the card row
  }

  function draftRoomCaption() {
    const left = ctx && ctx.junctions && ctx.junctions.getDraftSecondsLeft
      ? ctx.junctions.getDraftSecondsLeft() : null;
    const words = draftRoomDoors === 2 ? 'two doors' : 'three doors';
    return `${words}, one keeps you — click a card to take it${left != null ? ` · ${left}s` : ''}`;
  }

  function showDraftRoomChrome() {
    const d = ensureDraftDom();
    d.cap.textContent = draftRoomCaption();
    syncDraftButtons();
    d.root.hidden = false;
    if (draftCapTimer) clearInterval(draftCapTimer);
    draftCapTimer = setInterval(() => { if (draftDom && !draftDom.root.hidden) draftDom.cap.textContent = draftRoomCaption(); }, 250);
  }

  function hideDraftRoomChrome() {
    if (draftCapTimer) { clearInterval(draftCapTimer); draftCapTimer = null; }
    if (draftDom) draftDom.root.hidden = true;
  }

  function onDraftRoomLinger(on) {
    onJunctionLinger(on);   // the same near-hover crawl while the doors are read
    if (on && draftRoomActive) showDraftRoomChrome();
    else hideDraftRoomChrome();
  }

  function onDraftReroll() {
    if (!draftRoomActive || st.rerollsLeft <= 0 || !ctx || !ctx.junctions) return;
    st.rerollsLeft--;
    hudUi.toast('🎲 tempted fate again');
    ctx.junctions.setDraftCards(dealOptions().map(boonBranch));
    syncDraftButtons();
  }

  function onDraftResist() {
    if (!draftRoomActive || !ctx || !ctx.junctions) return;
    draftRoomSkip = true;
    if (!ctx.junctions.skipDraft()) draftRoomSkip = false;   // not lingering yet: ignore the click
  }

  function openDraftRoom(options) {
    draftRoomActive = true;
    draftRoomSkip = false;
    draftRoomDoors = options.length;
    // The Journey Rooms: the doors lead INTO the next room, so its title hangs
    // over them - big writing above the doorway mouths (junctions buildTitle).
    // The room theme only; the rolled biome stays a surprise until arrival.
    const next = regionForWave(pendingWave);
    const nextStyle = next.style;   // classic room palette - never the biome's (no early hint)
    ctx.junctions.schedule({
      atDepth: ctx.nav.getDepth() + DRAFT_ROOM_LEAD,
      branches: options.map(boonBranch),
      coaxIndex: Math.floor(Math.random() * options.length),
      mode: 'draft',
      lingerSec: cfg.draftAutoResumeSec,
      lead: DRAFT_ROOM_LEAD,
      title: { numeral: next.numeral, text: next.name,
               color: (nextStyle && nextStyle.palette && nextStyle.palette.colLine) || 0xff69b4 },
    });
  }

  /** A draft-room door committed: the dive is already running engine-side;
   * resolve the draft exactly like the card row did (boon / skip / timeout). */
  function onDraftDoorChosen(choice) {
    hideDraftRoomChrome();
    if (!draftRoomActive) return;   // aborted (run ended): teardown fired without us
    draftRoomActive = false;
    const b = (choice && choice.branch) || {};
    if (b.color) st.branchTint = { color: b.color, strength: 0.6 };   // the tube blushes in the boon's color
    ctx.fx.pulseFlash(0.5);
    const skipped = draftRoomSkip || (choice && choice.skipped);
    draftRoomSkip = false;
    if (skipped) { resolveDraft(null, false); return; }
    if (choice && choice.timedOut) { bark('draft-autopick'); resolveDraft(null, true); return; }
    // the engine's empty-entrance fallback (junctions.buildVein): a door that
    // couldn't carry a boon card carries a 100-gold pouch instead - bank it
    if (!b.boon && b.reward && b.reward.kind === 'gold') {
      const gold = goldScaled(b.reward.amount || 100);
      bankGold(gold, window.innerWidth / 2, window.innerHeight / 2);
      hudUi.announce(`🪙 +${gold} gold`, 'powerup', 1600);
      sfx('golden_pop', 0.55);
    }
    resolveDraft(b.boon || null, false);
  }

  /** Per-RunTick: Lust Bleed (the tube blushes with the LUST bar; a held full
   * bar forces pink fog) + Heat Warp (the combo streak re-patterns the rings
   * and burns the speed-lines hotter). Engine-side easing smooths all of it. */
  function ambientTick() {
    // A committed branch tints the tube in its color for ~18s (decaying), floored
    // by / blended with the LUST blush so the fork's identity lingers on the wall.
    const bt = st.branchTint;
    if (bt && bt.strength > 0.01) {
      bt.strength *= 0.965; // ~18s to fade at the 0.25s RunTick
      ctx.fx.setTint(bt.color, Math.max(st.heat * 0.6, bt.strength));
    } else {
      ctx.fx.setTint('#ff4fae', st.heat * 0.6);
    }
    if (!lustFullSeen && st.heat >= 0.98) { lustFullSeen = true; syncZone(); }
    else if (lustFullSeen && st.heat < 0.5) { lustFullSeen = false; syncZone(); }
    // comboWarp still drives the GLOW heat (speed-line burn below + lightning),
    // but NOT the ring/spiral COUNT anymore. Those counts must stay integers for
    // the seam-free loop, so the old combo->count re-pattern snapped uRings by 1 on
    // almost every pop and re-spaced every ring in a single frame - the tube
    // "teleported" on each bubble pop (rings and spiral ticking at different combos
    // made it look random). Heat now reads through color/glow, never geometry, so
    // the line pattern holds its position. Base counts (125 / 45) stay put.
    comboWarp = Math.min(1, st.combo / 25);
    // speed-line heat: Heat Warp's streak burn, outranked by Freefall's howl
    const heatRush = comboWarp >= 0.6 ? ((comboWarp - 0.6) / 0.4) * 0.8 : 0;
    ctx.fx.setRushOverride(st.freefallActive ? Math.max(0.95, heatRush) : heatRush);
    // W5 sins that ride the camera: Freefall raises the speed ceiling, Spun rolls
    ctx.nav.setSpeedCapMult(st.freefallActive ? 2 : 1);
    ctx.nav.setRollRate(st.spunRollRate || 0);
    // Four Chambers escalation: where intensity() sits inside this chamber's
    // band drives the pattern knobs' enter->peak lerp (fx eases it further).
    if (ctx.fx.setRegionProgress) {
      if (cfg.regionMode) {
        const r = regionForWave(st.waveIndex);
        const span = Math.max(0.001, r.band.peak - r.band.start);
        ctx.fx.setRegionProgress(clamp((intensity() - r.band.start) / span, 0, 1));
      } else {
        ctx.fx.setRegionProgress(0);
      }
    }
  }

  // ============================ RunTick (0.25s) ============================

  function tick(dt) {
    st.elapsedSec += dt;
    st.heat = Math.max(0, st.heat - 0.0015);
    ambientTick();
    // THE BIOMES: the chamber's mechanic breathes AFTER ambientTick so its
    // rush/speed writes win the frame (Terminal Velocity outruns Heat Warp).
    if (bMech) bMech.tick(dt);
    maybeScheduleJunction();

    // Weather Girl: the sky that arrived while the field was held makes its entrance now.
    if (weatherGirlPending) {
      const skyId = weatherGirlPending;
      weatherGirlPending = null;
      weatherGirlEntrance(skyId);
    }

    // W4 pickups: condensation beads on the wall (rank Tempted+); once per
    // loop the white rabbit may dash by (Rabbit's Foot raises his odds).
    if (ctx.spawner && !covered && st.freezeRemainingSec <= 0) {
      if (cfg.rankIndex >= RANK.Tempted) {
        condensationIn -= dt;
        if (condensationIn <= 0) {
          condensationIn = 12 + Math.random() * 8;
          spawnCondensation();
        }
      }
      if (st.waveIndex !== rabbitPickupWave) {
        rabbitPickupWave = st.waveIndex;
        const foot = st.goldenChanceBase > 0.005 ? 1.5 : 1.0; // Rabbit's Foot worn
        if (Math.random() < 0.15 * foot) spawnWhiteRabbit();
      }
    }

    // Private Show sin: once mid-descent, one of your videos takes the stage
    // at full volume while the bubbles keep coming.
    if (st.privateShowPending && intensity() >= st.privateShowAt && !covered
        && ctx.spawner && !ctx.spawner.spotlightActive()) {
      st.privateShowPending = false;
      showDetonationsAt = st.detonated;
      ctx.spawner.forceSpotlight(null, 15).then((ok) => {
        if (!ok || state !== 'running') return;
        showActive = true;
        hudUi.announce('💋 PRIVATE SHOW', 'bad', 2400, { artKey: 'private_show' });
        sfx('sin_accept', 0.5);
      }).catch(() => {});
    }
    // ...and when she leaves the stage: a streak kept through the show doubles.
    if (showActive && ctx.spawner && !ctx.spawner.spotlightActive()) {
      showActive = false;
      if (st.detonated <= showDetonationsAt && st.combo > 0) {
        st.combo = Math.min(999, st.combo * 2);
        if (st.combo > st.bestCombo) st.bestCombo = st.combo;
        hudUi.announce('💋 eyes stayed on her — STREAK DOUBLED', 'powerup', 2600);
        sfx('streak_milestone', 0.6);
        hudNow();
      } else {
        hudUi.toast('💋 the show cost you the streak');
      }
    }

    // Static weather: stray current pops a treat FOR you every few seconds;
    // one bolt in ten bites a live fuse down to half instead.
    if (weatherNow && weatherNow.boltEverySec && st.freezeRemainingSec <= 0) {
      staticBoltIn -= dt;
      if (staticBoltIn <= 0) {
        staticBoltIn = (weatherNow.boltEverySec * (0.7 + Math.random() * 0.6)) / wxAmp();
        const bite = Math.random() < 0.10;
        const hit = field.stormStrike(bite);
        if (hit) {
          ctx.fx.strikeNow();
          sfx('estim_zap', 0.35);
          if (hit.kind === 'life') hudUi.toast('⚡ static bit a fuse — it burns faster now');
        }
      }
    }

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

    // The Ripple's once-ever teach: a pause+explain card the moment a cast is affordable.
    if (!flags.seenRippleTeach && st.elapsedSec > 6 && st.focus >= st.rippleFocusCost) {
      setFlag('seenRippleTeach');   // also short-circuits the post-cast confirm announce below
      teach(VERB_COPY.ripple, { kicker: 'a new move' });
    }
    // Once-ever teach the first time focus dips under a ripple's price.
    if (!flags.seenFocusTip && st.focus < st.rippleFocusCost) {
      setFlag('seenFocusTip');
      teach(VERB_COPY.focus);
    }
    // Once-ever heat teach: name the burn the first time it visibly climbs.
    if (!flags.seenHeatTeach && st.heat >= 0.15) {
      setFlag('seenHeatTeach');
      hudUi.announce('lust climbs while you perform. it pays up to x2', 'depth', 3200);
    }
    // rh_focus_low: a sustained dry spell with no ripple in the tank -> one bark per run.
    if (!focusLowBarked) {
      if (st.focus < st.rippleFocusCost) {
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
    lessons.sampleCursor();                          // the_pull's rest witness
    lessons.noteChannelTotal(field.channelSeconds()); // slow_fuses
    happy.tickRun(hpIo);                             // scripted first-descent beats

    if (!st.endingSoonFired && st.runDurationSec - st.elapsedSec <= 10) {
      st.endingSoonFired = true;
      hudUi.announce('the hole is closing…', 'depth', 2400, { artKey: 'ending_soon', subText: 'ten seconds' });
      bark('ending-soon');
    }

    // A live fork owns the tube: the Landing's parked draft (or the recap) must
    // not fire mid-antechamber / mid-dive. The clock runs on a few extra seconds
    // until the fork tears down, then this block fires normally.
    if (st.elapsedSec >= st.runDurationSec && !(ctx && ctx.junctions && ctx.junctions.isBusy())) {
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
      } else if (cfg.regionMode && cfg.boonDraftEnabled && !st.finalLandingDone) {
        // Four Chambers: the Court (IV) earns its Landing too. The run's other
        // three drafts fire as you LEAVE a chamber; region IV has no chamber
        // after it, so its boon is a terminal draft, then the descent ends.
        st.finalLandingDone = true;
        beginFinalLanding();
        return;
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

  // A combo just moved - repaint the HUD immediately so the badge punch/break
  // lands with the moment instead of up to TICK (250ms) later.
  const hudNow = () => { if (hudUi && st) hudUi.update(hudState()); };

  const hudState = () => ({
    score: st.score, totalMult: totalMult(), combo: st.combo,
    focus: st.focus, elapsedSec: st.elapsedSec, runDurationSec: st.runDurationSec,
    waveIndex: st.waveIndex, waveCount: st.waveCount,
    rippleCost: st.rippleFocusCost,
    shields: st.shields, startingShields: st.startingShields,
    collarSaves: st.collarSaves,
    heat: st.heat,
    showClock: true,
  });

  // ============================ waves + the draft table ============================

  function beginWaveTransition(newWave) {
    // A live fork owns the tube (antechamber / vein dive / rebase): starting the
    // draft now parks the fall INSIDE the junction room (the overlap bug). Defer -
    // tick re-calls every 0.25s while newWave > st.waveIndex, so the transition
    // fires the moment the fork tears down.
    if (ctx && ctx.junctions && ctx.junctions.isBusy()) return;
    lessons.onLoopCompleted();   // silk_touch judged + progress flushed per loop
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
    // Four Chambers: the draft is a ROOM with one tube per boon (2-3 doors).
    // The clock is frozen while drafting, so the approach + linger cost no run
    // time. 4-choice drafts keep the in-tube card row (three doors max).
    if (cfg.regionMode && ctx && ctx.junctions && options.length >= 2 && options.length <= 3) {
      openDraftRoom(options);
      if (ctx.junctions.isBusy()) return;   // room armed: onCommit resolves the draft
      draftRoomActive = false;              // engine refused (inactive/torn down): card row, not a wedge
    }
    presentDraft({
      wave: st.waveIndex,
      options,
      autoResumeSec: cfg.draftAutoResumeSec,
      rerollsLeft: st.rerollsLeft,
      // The SKIP affordance is reveal-gated until run 3 (draft_skip): before
      // that the table auto-PICKS on timeout ("she chooses for you").
      allowSkip: cfg.runsCompleted >= 2,
      onPick: (boon, auto) => { if (auto) bark('draft-autopick'); resolveDraft(boon, auto); },
      onSkip: (auto) => { if (auto) bark('draft-autopick'); resolveDraft(null, auto); },
      onReroll: () => {
        if (st.rerollsLeft <= 0) return null;
        st.rerollsLeft--;
        hudUi.toast('🎲 tempted fate again');
        return { options: dealOptions(), rerollsLeft: st.rerollsLeft };
      },
    });
  }

  /** The Court's Landing: region IV's terminal boon draft. Same table as a wave
   * transition, but it resolves into endRun (no next chamber to advance to).
   * The loop tip is left to endRun so it isn't double-paid. */
  function beginFinalLanding() {
    lessons.onLoopCompleted();
    bark('wave-escalated', { wave: st.waveIndex });
    finalLandingActive = true;
    drafting = true;
    state = 'drafting';
    syncHeld();
    field.clearAll(true);
    sfx('wave_clear', 0.6);
    pulse('150,200,255', 0.30);
    bark('wave-cleared', { wave: st.waveIndex });

    const options = dealOptions();
    presentDraft({
      wave: st.waveIndex,
      options,
      autoResumeSec: cfg.draftAutoResumeSec,
      rerollsLeft: st.rerollsLeft,
      allowSkip: cfg.runsCompleted >= 2,
      onPick: (boon, auto) => { if (auto) bark('draft-autopick'); resolveDraft(boon, auto); },
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
      // Grab-in-the-tube rework: synergy duo/trio boons gate on what you've grabbed THIS
      // run (grown live), not a pre-run loadout - a partner card unlocks its synergy draft.
      equipment: [...st.runEquipment],
      hasVideo: !!(hostState && hostState.mediaStats && hostState.mediaStats.videos > 0),
    });
    happy.rigDraft(options, hpIo);   // run 4's defanged first sin / the duo demo
    for (const o of options) markDiscovered('boon:' + o.id);
    return options;
  }

  function applyBoon(boon, shieldDrawback = false) {
    if (shieldDrawback) { if (boon.applyShielded) boon.applyShielded(st); }
    else boon.apply(st);
    st.boonMult += boon.mult;
    st.takenBoonIds.add(boon.id);
    st.runPicks.push({ id: boon.id, name: boon.name, curse: boon.curse, desc: boon.desc, flavor: boon.flavor, rarity: boon.rarity });
    hudUi.setPicks(st.runPicks);
    syncPhys();   // drafted knobs that live in field physics (tail-plug, aftermath, cam girl)
  }

  // Grab-in-the-tube rework: a grabbed accessory/charm applies its effect ONCE, live, at its
  // current dollhouse level (min 1). Mirrors applyBoon but reads the ported PASSIVE_APPLY table
  // (game/boonPassives.js) and the C#-authored levelValues. Returns the applied level (0 = no-op).
  function levelFor(id) { return Math.max(1, (levels && levels[id]) | 0); }

  function applyGrabbedPassive(id) {
    if (!st || !isGrabbablePassive(id)) return 0;
    if (st.takenPassiveIds.has(id)) return 0;   // one grab per id per run (esp. multiplicative)
    const def = boonDefById(id);
    if (!def || !def.levelValues || !def.levelValues.length) return 0;
    const level = levelFor(id);
    const v = def.levelValues[Math.min(level, def.levelValues.length) - 1];
    st.takenPassiveIds.add(id);
    st.runEquipment.add(id);
    try { PASSIVE_APPLY[id](st, level, v, { wireStickyFingers }); }
    catch (e) { bridge.log('grab-passive ' + id + ' failed: ' + e.message); }
    if (level >= def.levelValues.length) st.maxedBoons.add(id);   // arm capstone gates
    st.runPicks.push({ id, name: def.name, curse: false, desc: def.desc, flavor: def.flavor, glyph: def.glyph, relic: true });
    hudUi.setPicks(st.runPicks);
    syncPhys();
    return level;
  }

  // A grabbed consumable (active toy) docks as a single-use record in the module `toys`
  // array (the HUD's bottom dock), at its dollhouse level. Number-keyed by slot position.
  function addConsumable(id, level) {
    const def = boonDefById(id);
    if (!def || !def.levelValues || !def.levelValues.length) return;
    if (toys.length >= st.consumableSlots) return;   // dock full (canOfferPowerup should have vetoed)
    const lvl = Math.max(1, level | 0);
    const power = def.levelValues[Math.min(lvl, def.levelValues.length) - 1];
    toys.push({
      id, name: def.name, glyph: def.glyph || '◈', desc: def.desc || '',
      key: String(toys.length + 1),                       // slot 1 -> "1", etc.
      cooldownSec: def.cooldownSec ?? 0,
      power, level: lvl, maxed: lvl >= def.levelValues.length,
      chargesLeft: 1,                                     // consumable: one use, whatever the native model
      cooldownLeft: 0, effectActive: false, consumable: true,
    });
    st.runEquipment.add(id);
    hudUi.updateToys(toys, toyStatus());
  }

  // A power-up card was grabbed mid-fall: unlock-on-first-discovery (free), then dock it
  // (consumable) or apply it (passive), flying the art from the tube point to its HUD slot.
  // The FIRST time you ever grab an item, a pause+explain card fires and the dock/apply is
  // DEFERRED until you dismiss it - so the art flies into the HUD after you've read what it is.
  function handlePowerupGrab(id, kind, screenPos) {
    const def = boonDefById(id);
    if (!def) return;
    const level = levelFor(id);
    const meta = hostState && hostState.meta;
    const prev = (meta && meta.lifetimeBoonLevels && (meta.lifetimeBoonLevels[id] | 0)) || 0;
    const first = prev < 1;
    if (first) {
      // free discovery unlock (the bridge accepts level==cur+1 && Sparks>=cost; cost 0)
      bridge.send({ type: 'meta-command', op: 'set-lifetime-boon', id, level: 1, cost: 0 });
      try { if (meta) { meta.lifetimeBoonLevels = meta.lifetimeBoonLevels || {}; meta.lifetimeBoonLevels[id] = 1; } } catch (e) { /* mirror only */ }
    }
    sfx('dive', 0.5);
    try { ctx.fx && ctx.fx.pulseFlash && ctx.fx.pulseFlash(0.4); } catch (e) { /* ignore */ }

    const acquire = () => {
      if (state !== 'running') return;   // card dismissed after the run ended: skip the apply/anim
      if (kind === 'consumable') {
        addConsumable(id, level);
        try { hudUi.flyArtToSlot(id, screenPos, 'consumable'); } catch (e) { /* ignore */ }
      } else {
        applyGrabbedPassive(id);
        try { hudUi.flyArtToSlot(id, screenPos, 'relic'); } catch (e) { /* ignore */ }
      }
      hudUi.toast('◈ ' + def.name);
    };

    if (first) {
      teach(
        { glyph: def.glyph || '◈', name: def.name, desc: def.desc || '', flavor: def.flavor || '' },
        { kicker: kind === 'consumable' ? 'new toy · discovered' : 'new relic · discovered', onDismiss: acquire }
      );
    } else {
      acquire();
    }
  }

  function resolveDraft(boon, auto) {
    // session telemetry: what the draft yielded + whether the timer had to decide
    if (auto) metrics.noteAutopick();
    if (boon) { if (boon.curse) metrics.noteCurse(); else metrics.noteBoon(); }
    else metrics.noteSkip();
    if (boon) {
      lessons.onDraftCardTaken(!!boon.curse);   // draft4 / surrender / first-times
      // The happy-path rig shields the run-4 demo sin WITHOUT spending
      // Surrender's own once-per-run candle.
      const rigShield = boon.curse && happy.shouldShieldSin(boon.id);
      const surrShield = boon.curse && !rigShield && st.maxedBoons.has('surrender') && !st.surrenderShieldUsed;
      if (surrShield) st.surrenderShieldUsed = true;
      const sinShielded = rigShield || surrShield;
      applyBoon(boon, sinShielded);
      if (boon.curse) {
        // The Tilt: the world tips a little every time you say yes (a shielded
        // sin keeps the horizon - the candle takes the tilt too)
        if (!sinShielded && tiltSins < 3) {
          tiltSins++;
          ctx.nav.setRollOffset(tiltSins * 2.5);
        }
        if (st.sinExtraMult > 0) {
          st.boonMult += st.sinExtraMult;
          hudUi.toast(`🕯 sin embraced (+${st.sinExtraMult.toFixed(2)}x)`);
        }
        if (rigShield) hudUi.toast('☠ the first taste is free — no sting, this once');
        else if (sinShielded) hudUi.toast('🕯 the candle took the sting (no drawback)');
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
      // fly a shield from screen-centre into the HUD shields slot so the bank reads
      try { hudUi.flyShieldToSlot && hudUi.flyShieldToSlot(); } catch (e) { /* ignore */ }
      bark('boon-skipped', { shields: st.shields });
    }
    happy.onDraftResolved(hpIo);   // the run-4 first-sin beat is spent either way
    // The Court's Landing resolves into the end of the descent, not a next loop.
    if (finalLandingActive) {
      finalLandingActive = false;
      drafting = false;
      syncHeld();
      // endRun's reentry guard only admits 'running' - the Landing left us in
      // 'drafting', which used to no-op the call and strand the run forever
      // (clock frozen at 11:59, tube flying with nothing left to spawn).
      state = 'running';
      endRun(true);
      return;
    }
    const scripted = scriptedDraftActive;
    scriptedDraftActive = false;
    if (!scripted) commitWave(pendingWave);   // run 1's mid-run table advances no loop
    // A brief "Ready? -> GO!" beat before the next loop resumes.
    overlays.showReadyGo(() => {
      drafting = false;
      state = 'running';
      syncHeld();
      if (!scripted && st.welcomeShower) spawnWelcomeShower();
      if (!scripted) announceFinalLoopIfEntering();
    }, { onTick: (b) => sfx(b === 'GO!' ? 'sink' : 'countdown_tick', 0.45) });
  }

  function commitWave(newWave) {
    st.waveIndex = newWave;
    // Four Chambers: every loop IS a new chamber, so announce it by name.
    if (cfg.regionMode) {
      st.actIndex = Math.min(newWave, REGION_COUNT);
      st.lastActFired = st.actIndex;
      announceRegion(newWave);
      advanceWeather();
      ctx.fx.pulseFlash(0.6);
      pulse('150,200,255', 0.30);
      return;
    }
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
    // Wave 2: THE WEATHER - a named sky rolls in with every loop (it forces
    // the matching tunnel zone, so the old seedZoneAhead ride is subsumed).
    advanceWeather();
    ctx.fx.pulseFlash(0.6);
    pulse('150,200,255', 0.30);
    if (!cfg.boonDraftEnabled) announceFinalLoopIfEntering();
  }

  /** The Journey Rooms arrival card: room title (the emotional beat) headlining,
   * the rolled BIOME naming the place underneath - the "surprise on entry"
   * reveal. Fired on every chamber entry (and Room I at the opening GO). */
  function announceRegion(regionIndex) {
    const r = regionForWave(regionIndex);
    const b = biomeAt(regionIndex);
    sfx('depth_change', 0.55);
    hudUi.announce(`${r.numeral} · ${r.name.toUpperCase()}`, 'depth', 3000, {
      artKey: 'depth',
      subText: b ? `${b.glyph} ${b.name} — ${b.tagline}` : r.subtitle,
    });
    bark('act-changed', { act: Math.min(regionIndex, REGION_COUNT), wave: regionIndex });
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
   * is by RANK, not difficulty; the difficulty's `strange` pace scalar rides
   * every roll (Gentle barely sees them, Inescapable gets the full menagerie).
   * Debuts spawn alone with a gentler trance and announce themselves. */
  function trySpawnBehavioral(effIntensity) {
    const gentleMult = cfg.pace.strange;
    const rank = cfg.rankIndex;
    // Four Chambers: the region (or its rolled BIOME) biases WHICH mechanics
    // show up. `behavioral` is a global scale; the per-mechanic keys ride on
    // top. Neutral (all 1.0) off-region.
    const prof = cfg.regionMode ? profileForWaveNow(st.waveIndex) : PROFILE_NEUTRAL;
    const bMult = gentleMult * prof.behavioral;

    if (rank >= RANK.Tempted && Math.random() < ECHO_SPAWN_CHANCE * bMult * prof.echo) {
      const debut = !flags.seenEcho;
      if (debut) setFlag('seenEcho');   // gates the debut fuse-slowdown; the teach is markDiscovered's card
      markDiscovered('bubble:echo');
      field.spawn(buildEcho(effIntensity, st.fuseTimeMult, st.bubbleScale, debut ? DEBUT_FUSE_MULT : 1.0));
      return true;
    }
    if (rank >= RANK.Tempted && Math.random() < CHAPERONE_SPAWN_CHANCE * bMult * prof.chaperone) {
      const debut = !flags.seenChaperone;
      if (debut) setFlag('seenChaperone');   // gates the debut fuse-slowdown; the teach is markDiscovered's card
      const [liveSpec, escortSpec] = buildChaperonePair(effIntensity, st.fuseTimeMult,
        cfg.effectIntensity, st.bubbleScale, debut ? DEBUT_FUSE_MULT : 1.0);
      markDiscovered('bubble:chaperone');
      const liveB = field.spawn(liveSpec);
      const escortB = field.spawn(escortSpec);
      field.linkChaperone(liveB, escortB);
      return true;
    }
    if ((cfg.difficulty === 'Hard' || cfg.difficulty === 'Extreme' || rank >= RANK.Entranced)
        && Math.random() < BOUND_SPAWN_CHANCE * bMult * prof.bound) {
      const debut = !flags.seenBound;
      if (debut) setFlag('seenBound');   // gates the debut fuse-slowdown; the teach is markDiscovered's card
      const [a, b] = buildBoundPair(effIntensity, st.fuseTimeMult, cfg.effectIntensity,
        st.bubbleScale, debut ? DEBUT_FUSE_MULT : 1.0);
      markDiscovered('bubble:bound');
      field.spawn(a);
      field.spawn(b);
      return true;
    }
    if (rank >= RANK.Slipping && Math.random() < TEASE_SPAWN_CHANCE * bMult * prof.tease) {
      const debut = !flags.seenTease;
      if (debut) { setFlag('seenTease'); bark('tease-debut'); }   // fuse-slowdown gate + bark; the teach is markDiscovered's card
      markDiscovered('bubble:tease');
      field.spawn(buildTease(effIntensity, cfg.effectIntensity, st.bubbleScale));
      return true;
    }
    return false;
  }

  /** The giants live in the deep (replaces the old Entranced rank clamp):
   * chambers I-II never deal video/gif rain, chamber III deals them at half
   * presence (an independent roll per giant per spawn), chamber IV at the full
   * (still rare) pool weight. Feeds both the main spawn roll and the Brittle,
   * so a giant can't hide in the glass shallower than it's allowed to swim. */
  function gateGiants(enabled) {
    if (!cfg.regionMode) return enabled;
    const giantShare = st.waveIndex >= 4 ? 1 : st.waveIndex === 3 ? 0.5 : 0;
    if (giantShare >= 1) return enabled;
    for (const id of ['video', 'htlink']) {
      if ((!enabled || enabled.includes(id)) && (giantShare === 0 || Math.random() >= giantShare)) {
        enabled = (enabled || ALL_IDS).filter((x) => x !== id);
      }
    }
    return enabled;
  }

  function spawnTick() {
    // Held (paused / covered / drafting / the persona mid-line): hold the field and
    // retry shortly. Guards the same-frame case where the empty-field rescue zeroes
    // spawnWait in tick() just as a VN beat sets the hold.
    if (heldNow()) { spawnWait = 0.3; return; }
    if (st.freezeRemainingSec > 0) { spawnWait = 0.3; return; }   // frozen: hold the field
    const i = intensity();
    const effIntensity = clamp(i + cfg.pace.surge, 0, 1);
    // Four Chambers: the chamber's (biome-aware) `density` scales how full the
    // field runs - sparse and open up top, an overgrown tangle in the deep.
    const prof = cfg.regionMode ? profileForWaveNow(st.waveIndex) : PROFILE_NEUTRAL;
    // Population ceiling: kept low through most of the fall and only climbing
    // hard as intensity peaks in the deep chambers, so the run RAMPS instead of
    // firing a full field the whole way down. (Was 6 + i*10.)
    const maxConcurrent = Math.max(3, Math.round((4 + i * 8) * cfg.pace.density * prof.density));

    const room = field.count() < maxConcurrent;
    // The scripted first descent: the behavioral menagerie stands down entirely
    // (the happy path spawns its own scripted threat/darter beats).
    const behavioralSpawned = room && !cfg.scriptedFirstRun && trySpawnBehavioral(effIntensity);

    if (!behavioralSpawned && room) {
      // Be gentle with the tape: no video bubble while a heavy effect runs, and
      // none when the loop/run is too close to its end for the fuse + 15s slice.
      let enabled = gateGiants(cfg.enabledVariants);
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
      const sideDrift = st.ordinarySpawns < SIDE_DRIFT_GRACE_SPAWNS ? 0 : SIDE_DRIFT_CHANCE;
      if (st.heavyDropEvery > 0 && ++st.spawnSerial % st.heavyDropEvery === 0) {
        spec = buildHeavy(effIntensity, cfg.effectIntensity, st.bubbleScale);
      } else if (Math.random() < PLAIN_BUBBLE_CHANCE_EARLY + (PLAIN_BUBBLE_CHANCE - PLAIN_BUBBLE_CHANCE_EARLY) * i) {
        // Plain-bubble share RAMPS with intensity: ~80% plain at the top of the
        // run (effects stay rare, the fall reads as calm) easing to ~30% deep, so
        // the stimulus builds gradually instead of firing wall-to-wall up front.
        spec = buildPlain(effIntensity, { sizeScale: st.bubbleScale, sideDriftChance: sideDrift });
      } else {
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
      // The Brittle (Tempted+, scaled by the difficulty's strange rate): a glass mine rides in alongside.
      if (!cfg.scriptedFirstRun && cfg.rankIndex >= RANK.Tempted
          && Math.random() < BRITTLE_SPAWN_CHANCE * cfg.pace.strange) {
        if (!flags.seenBrittle) setFlag('seenBrittle');   // (fuse gate parity); the teach is markDiscovered's card
        markDiscovered('bubble:brittle');
        field.spawn(buildBrittle(effIntensity, cfg.effectIntensity, st.bubbleScale, gateGiants(cfg.enabledVariants)));
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

    // Refill cadence: 1250ms early -> 360ms late (/pace.spawn), floor 280ms. The
    // slower open keeps the shallow chambers calm; it tightens as intensity peaks.
    let interval = (1250 - i * 890) / cfg.pace.spawn;
    interval /= cfg.spawnRateMult;
    interval /= Math.sqrt(prof.density);   // dense chambers refill a touch faster
    interval /= wxSpawnRate();   // Overstim weather: bubbles come faster
    if (st.freefallCadence) interval /= 1.25;   // Freefall sin (unshielded half)
    if (st.slowMoRemainingSec > 0) interval /= SLOWMO_FACTOR;   // slow-mo stretches the cadence
    spawnWait = Math.max(280, interval) / 1000;
  }

  // ============================ the Ripple (right-click) ============================

  function onPointerDownGlobal(e) {
    if (e.button !== 2) return;
    if (state !== 'running' || heldNow() || !st) return;
    if (st.freezeRemainingSec > 0) return;   // a frozen field is already a free-pop window
    const x = e.clientX, y = e.clientY;
    // the ripple fires on bubbles OR on a flash clip under the click - so
    // right-clicking a flash that's covering the field still triggers (and flings
    // it), instead of silently no-op'ing when no bubble sits under the cursor.
    const flashUnder = ctx.flingFlashesNear && ctx.flingFlashesNear(x, y, st.rippleRadiusPx, true);
    if (!flashUnder && !field.nearAny(x, y, st.rippleRadiusPx + RIPPLE_TRIGGER_GRACE_PX)) return;
    if (st.focus < st.rippleFocusCost) {
      sfx('focus_empty', 0.5);
      hudUi.flashFocus();
      field.floatText(`need ${st.rippleFocusCost} focus`, x, y - 30, 'cf-pop--focus');
      return;
    }
    st.focus = clamp(st.focus - st.rippleFocusCost, 0, FOCUS_MAX);
    if (setFlag('seenRippleTeach')) hudUi.announce('🌊 that\'s the ripple. it spends focus — pop treats to refill', 'powerup', 2400);
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
    rippleCastCount++;
    sfx('ripple_cast', 0.6);
    field.castRipple(x, y, st.rippleRadiusPx, st.rippleLifeMs);
    // the same wave flings the on-screen flash clips (the hydra flashes) off
    // screen along the angle of the hit, clearing the view onto the bubbles.
    if (ctx.flingFlashesNear) ctx.flingFlashesNear(x, y, st.rippleRadiusPx);
    // P1: the ripple is a tunnel shockwave too (brightness flash, not a zoom pump).
    ctx.fx.pulseFlash(0.5);
  }

  const onContextMenu = (e) => { e.preventDefault(); };

  // ============================ happy path (scripted first descents) ============================

  /** The beat surface the happy-path director drives (ChaosHappyPath's view of the run). */
  const hpIo = {
    log: (m) => bridge.log(m),
    progress: () => intensity(),
    combo: () => (st ? st.combo : 0),
    ripplesCast: () => rippleCastCount,
    focusFull: () => !!st && st.focus >= st.rippleFocusCost,
    announce: (text, kind, holdMs) => hudUi && hudUi.announce(text, kind, holdMs),
    toast: (text) => hudUi && hudUi.toast(text),
    flags: { has: (k) => !!flags[k], set: (k) => setFlag(k) },
    variantAllowed: (id) => !cfg.enabledVariants || cfg.enabledVariants.includes(id),
    joinVariants: (ids) => {
      if (!cfg.enabledVariants) return;
      for (const id of ids) if (!cfg.enabledVariants.includes(id)) cfg.enabledVariants.push(id);
    },
    get allowCurses() { return cfg.allowCurses; },
    equipmentHas: (id) => st ? st.runEquipment.has(id) : false,
    bark: (event) => bark(event),
    /** A Visual-Novel portrait beat (scripted descents). `beat` is a manifest beat
     * id (resolves the active persona's emote + line + voiceover) or an explicit
     * {emote,line,voUrl}. Returns a promise; safe to fire-and-forget. No-op if the
     * VN overlay isn't built. */
    vnBeat: (beat) => (vn ? vn.beat(beat) : Promise.resolve(false)),
    spawnScriptedThreat(fuseMult) {
      const pink = VARIANTS.find((v) => v.id === 'pink');
      if (!pink) return;
      bridge.log('happy: scripted threat spawns');
      markDiscovered('bubble:pink');
      field.spawn(build(pink, intensity(), {
        fuseTimeMult: st.fuseTimeMult * fuseMult,
        effectIntensity: cfg.effectIntensity,
        sizeScale: st.bubbleScale,
      }));
    },
    /** The one scripted draft (drafts are otherwise off this run). Returns false
     * when the moment is busy (held) so the beat retries next tick. */
    triggerScriptedDraft(pool) {
      if (heldNow() || state !== 'running') return false;
      scriptedDraftActive = true;
      drafting = true;
      state = 'drafting';
      syncHeld();
      field.clearAll(true);
      sfx('wave_clear', 0.6);
      pulse('150,200,255', 0.30);
      pendingWave = st.waveIndex;
      for (const o of pool) markDiscovered('boon:' + o.id);
      presentDraft({
        wave: st.waveIndex,
        options: pool,
        autoResumeSec: cfg.draftAutoResumeSec,
        rerollsLeft: 0,
        allowSkip: false,   // no skip on the classroom table - timeout picks for you
        onPick: (boon, auto) => { if (auto) bark('draft-autopick'); resolveDraft(boon, auto); },
        onSkip: () => resolveDraft(pool[0], true),   // unreachable with allowSkip false
      });
      return true;
    },
    spawnDarter() {
      markDiscovered('bubble:darter');
      field.spawn(buildDarter(intensity()));
    },
    spawnBraindrainDebut() {
      const v = VARIANTS.find((x) => x.id === 'braindrain');
      if (!v) return;
      markDiscovered('bubble:braindrain');
      field.spawn(build(v, intensity(), {
        fuseTimeMult: st.fuseTimeMult * DEBUT_FUSE_MULT,
        effectIntensity: cfg.effectIntensity,
        sizeScale: st.bubbleScale,
      }));
    },
    spawnGolden() {
      markDiscovered('bubble:golden');
      field.spawn(buildGolden());
      field.playChime(0.30);
    },
  };

  /** Lesson-proof gating is retired (items are grabbed in the fall, not unlocked by
   * proofs), so a completed lesson no longer puts anything "for sale". The tracker
   * still runs (first-time Sparks bounties ride it), but this completion callback is
   * now a no-op - the old "LESSON LEARNED - now for sale" card would be misleading. */
  function onLessonComplete(_id) { /* retired: nothing unlocks on proof completion */ }

  function onFirstTimeAwarded(id, amount, label) {
    if (!hudUi) return;
    hudUi.toast(`✨ ${label} — +${amount} ✦ banked`);
    pulse('255,230,150', 0.20);
  }

  // ============================ lifecycle ============================

  function beginCountdown(short) {
    state = 'countdown';
    st = freshState();
    tickAcc = 0;
    spawnWait = 0.8;
    lastNoFocusAnnounce = -10;
    focusLowAccum = 0; focusLowBarked = false;
    teaseDeniedThisRun = 0; teaseDeniedStreakBarked = false;
    heavyUntil = 0;
    pendulumRolledWave = 0; heartRolledWave = 0; heartArmed = false;
    vibeRemainingSec = 0; afterglowApplied = false;
    estimCharges = 0; rabbitCallPending = 0; rabbitStormSec = 0; thoughtAccum = 0;
    heartbeatCd = 0;
    statsFlushCd = STATS_FLUSH_SEC;
    scriptedDraftActive = false;
    finalLandingActive = false;
    vnHold = false;   // a run torn down mid-beat must not carry a stuck field-freeze into the next descent
    clearAmbient();
    // THE BIOMES: roll this descent's place-variants (one per room). Scripted
    // first runs stay biome-free - the classroom plays the classic chambers.
    st.biomes = (cfg.regionMode && !cfg.scriptedFirstRun) ? rollBiomeIds() : null;
    if (bMech) bMech.reset();
    // Four Chambers: Region I is the open fall; the Mood Ring forecasts the next
    // chamber's fixed sky (the BIOME's sky when one overrides it). Legacy runs
    // roll a random loop-2 sky instead.
    weatherNext = cfg.regionMode
      ? (weatherIdForWave(2) ? WEATHER_BY_ID[weatherIdForWave(2)] : null)
      : rollWeather(null);   // loop 1 flies under an open sky; the weather lands with loop 2
    // Tunnel videos hold in view (the Fall's spotlight) during a descent too, so
    // a passing video lingers for its 'video spotlight time' (S.spotSeconds)
    // instead of bolting through. (Was off mid-run; restored by request.)
    if (ctx.spawner) ctx.spawner.setAutoSpotlight(true);
    buildToys();
    syncPhys();
    wireStickyFingers();
    lessons.seed(hostState ? hostState.meta : null, cfg.allowCurses);
    metrics.reset();   // fresh per-run engagement counters
    happy.onRunStarted(cfg.runsCompleted, cfg.scriptedFirstRun);

    // A pre-equipped start boon enters the run already active (before wave 1) -
    // except on the scripted first descent, where it stands down (the classroom).
    if (cfg.equippedStartBoon && !cfg.scriptedFirstRun) {
      const boon = boonById(cfg.equippedStartBoon);
      if (boon) {
        applyBoon(boon);
        markDiscovered('boon:' + boon.id);
        hudUi.announce(`◈ ${boon.name}`, 'powerup', 2200, { artKey: boon.id });
      }
    }

    hudUi.resetCombo();   // "Again" reruns never hide the badge - clear its baseline first
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
        // Descent is live: wake the drift whisper + special tube moods (the hub kept them idle).
        try { ctx.setRunActive && ctx.setRunActive(true); } catch (e) { /* ignore */ }
        bridge.send({ type: 'run-started', difficulty: cfg.difficulty, mode: 'dtrh-web' });
        // Fresh descent: dress the opening chamber. applyRegionSky covers the
        // wall plaster, drift voice AND the chamber's visual grade (Region I's
        // warm dusk fades in over the user-theme hub look during the GO beat) -
        // it runs on the scripted first run too, just without the banner.
        if (cfg.regionMode) {
          applyRegionSky(st.waveIndex);
        } else {
          if (ctx && ctx.wall) ctx.wall.setRegion(0);
          if (ctx && ctx.drift) { ctx.drift.setRegion(0); if (ctx.drift.setBiome) ctx.drift.setBiome(null); }
        }
        // First descent since the verb changed: one quiet line so the hold isn't
        // a mystery. The scripted run 1 lands this at its lone-threat beat instead.
        if (!flags.seenDefuseTutorial && !cfg.scriptedFirstRun) {
          setFlag('seenDefuseTutorial');
          teach(VERB_COPY.snap);
        } else if (cfg.regionMode && !cfg.scriptedFirstRun) {
          // Chamber I's arrival banner (skipped on the first-ever descent, which
          // owns the GO beat with the defuse tutorial, and on the scripted run).
          announceRegion(1);
        }
        if (st.welcomeShower) spawnWelcomeShower();
      },
    });
  }

  function endRun(ranFullCourse) {
    if (state !== 'running') return;
    state = 'recap';
    // Surfaced: hush the drift whisper + calm the tube for the recap/hub idle.
    try { ctx.setRunActive && ctx.setRunActive(false); } catch (e) { /* ignore */ }
    if (ctx && ctx.wall) ctx.wall.setRegion(0); // bare the wall for the recap/warren
    if (ctx && ctx.drift) { ctx.drift.setRegion(0); if (ctx.drift.setBiome) ctx.drift.setBiome(null); } // recap/warren: universal voice only
    if (bMech) bMech.reset();   // THE BIOMES: exit the mechanic (restores dim/beams/mirrors/speed)
    if (st.freezeRemainingSec > 0) endFreeze();
    if (st.slowMoRemainingSec > 0) endSlowMo();
    field.setVibe(false);
    lessons.onRunCompleted(st.shields, cfg.difficulty, ranFullCourse);
    happy.onRunEnded();
    if (ranFullCourse) awardLoopTip();   // the final loop ends at the run clock
    field.clearAll();
    clearAmbient();
    if (ctx.spawner) ctx.spawner.setAutoSpotlight(true); // the idling tunnel may feature again
    ctx.director.killBoost();
    ctx.director.setTimeFactor(0.3);     // idle crawl under the recap
    sfx('surface', 0.5);
    const channelSec = field.channelSeconds();
    if (channelSec > 0.5) bridge.send({ type: 'meta-command', op: 'add-channel-seconds', seconds: channelSec });
    flushAssetStats();   // ship the last engagement delta before the recap

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
      // local-only session telemetry: JS-side counters folded with st's own; the
      // host adds its natively-measured video/voice totals + sums into the store.
      sessionStats: metrics.snapshot(st, ctx.nav.getDepth()),
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
      // THE BIOMES: the route this fall took, for the recap's summary line
      biomes: (st.biomes || []).map((id) => biomeById(id)).filter(Boolean)
        .map((b) => ({ glyph: b.glyph, name: b.name })),
    }, {
      onAgain: () => {
        // Re-request a fresh config (the loadout/settings may have moved, and
        // run 1 -> run 2 must leave the classroom behind).
        overlays.hideRecap();
        shortNextCountdown = true;
        state = 'requesting';
        bridge.send({ type: 'request-run', setup: warren.currentSetup() });
      },
      onSurface: () => returnToWarren(),
    });
    hudUi.update(hudState());
  }

  /** Back to the hub: the recap closes, the tunnel idles, the Warren re-renders
   * on the freshest snapshot (the payout's rebroadcast usually landed already). */
  function returnToWarren() {
    overlays.hideRecap();
    overlays.hideCountdown();
    hudUi.setVisible(false);
    hudUi.setPicks([]);
    field.clearAll();
    clearAmbient();
    if (bMech) bMech.reset();   // a run aborted mid-chamber must not leak its biome mechanic into the hub
    try { ctx.setRunActive && ctx.setRunActive(false); } catch (e) { /* ignore */ }
    if (ctx.spawner) ctx.spawner.setAutoSpotlight(true);
    ctx.director.setTimeFactor(0.3);
    state = 'warren';
    drafting = false;
    // an aborted draft room never commits: drop its state + chrome so the next
    // run's first draft doesn't inherit a stale skip flag
    draftRoomActive = false;
    draftRoomSkip = false;
    hideDraftRoomChrome();
    syncHeld();
    warren.show('menu');
  }

  function syncHeld() {
    field?.setHeld(heldNow());
    field?.setInputLocked(heldNow());
  }

  // ============================ scene integration ============================

  return {
    /** scene.start calls this once the engine is live: mount the WARREN over
     * the idling tunnel; a descent starts when the host answers request-run. */
    attach(sceneCtx) {
      ctx = sceneCtx;
      ffx = createFieldFx(ctx.hud);
      payloadFx = createPayloadFx({ hud: ctx.hud, fx: ctx.fx, media: ctx.media, flashBurst: ctx.flashBurst });
      try { window.__sfPayloadFx = payloadFx; } catch { /* diagnostics seam (m2test) */ }
      try { window.__sfFireJunction = () => fireJunction(); } catch { /* diagnostics seam: force a branch fork */ }
      try {
        // diagnostics seam: jump straight to chamber N (1..4) through the full
        // production commit path (banner + grade + density choreography)
        window.__sfRegion = (n) => {
          if (state === 'running' && cfg && cfg.regionMode) {
            commitWave(clamp(Math.round(n || 1), 1, REGION_COUNT));
          }
        };
        // diagnostics seam: force a biome mid-run - __sfBiome('gallery') rigs
        // the roll for that biome's room and recommits its chamber so the full
        // arrival choreography (banner + grade + mechanic swap) plays.
        window.__sfBiome = (id) => {
          const b = biomeById(String(id || ''));
          if (!b || state !== 'running' || !cfg || !cfg.regionMode) return false;
          if (!st.biomes) st.biomes = rollBiomeIds();
          st.biomes[b.room - 1] = b.id;
          commitWave(b.room);
          return true;
        };
      } catch { /* diagnostics seam */ }
      // THE BIOMES: the mechanic controller - the run brain forwards its seams
      // (tick / pops / grabs / detonations) into whichever mechanic the rolled
      // chamber runs. The facade below is everything a mechanic may touch.
      bMech = createBiomeMech({
        st: () => st,
        get field() { return field; },   // created just below; getter dodges the ordering
        get ffx() { return ffx; },
        get fx() { return ctx.fx; },
        get nav() { return ctx.nav; },
        get spawner() { return ctx.spawner; },
        get wall() { return ctx.wall; },
        sfx, pulse, addHeat, bankGold, hudNow,
        syncPhys: () => syncPhys(),
        firePayload: (spec) => firePayload(spec),
        toast: (t) => hudUi && hudUi.toast(t),
        announce: (text, kind, ms, opts) => hudUi && hudUi.announce(text, kind, ms, opts),
        favoriteAsset: (kind) => (ctx.media && ctx.media.favorite ? ctx.media.favorite(kind) : null),
        assetUrl: (name) => (ctx.media && ctx.media.urlByName ? ctx.media.urlByName(name) : null),
        audioColor: (mode) => { try { setAudioColor(mode); } catch (e) { /* ignore */ } },
      });
      field = createChaosField({
        hud: ctx.hud, fx: ffx,
        canChannel, onBenignPopped, onFreezeCaught,
        onDefused, onDetonated, onTreatExpired, onChannelBroken,
        onDarterCaught, onTeaseTouched, onTeaseDenied, onBrittleShattered, onBoundEnraged,
        onRabbitSmacked: (first) => lessons.onRabbitSmacked(first),
        // Autoplay duo: the logo found a corner - the whole screen pays.
        onDvdCorner: (x, y) => {
          if (!st || state !== 'running' || heldNow() || !st.autoplay) return;
          if (performance.now() < autoplayCdUntil) return;
          autoplayCdUntil = performance.now() + 5000;
          sfx('streak_milestone', 0.6);
          hudUi.announce('📀 CORNER SHOT', 'powerup', 2200, { subText: 'the whole screen pays' });
          pulse('255,215,0', 0.5);
          ctx.fx.pulseFlash(0.7);
          field.castRipple(x, y, Math.hypot(window.innerWidth, window.innerHeight), 1100);
          spawnWelcomeShower();
        },
      });
      hudUi = createChaosHud(ctx.hud, {
      onToyUse: (id) => { const t = toys.find((x) => x.id === id); if (t) useToy(t); },
      onWeatherClick: () => rerollWeather(),
      // hover tooltips stand down whenever something else holds the world
      // (pause / drafts / lesson cards / VN beats / junction rooms / recap)
      isSuppressed: () => state !== 'running' || heldNow() || !!(ctx && ctx.junctions && ctx.junctions.isBusy()),
    });
      // Branching paths: the engine reports which mouth the faller took, and asks
      // us to crawl the tunnel while it hovers at the fork so the choice can
      // breathe. Draft rooms ride the same machinery, routed by commit mode.
      if (ctx.junctions) {
        ctx.junctions.onCommit = (choice) => (choice && choice.mode === 'draft' ? onDraftDoorChosen(choice) : onJunctionChosen(choice));
        ctx.junctions.onLinger = (on, mode) => (mode === 'draft' ? onDraftRoomLinger(on) : onJunctionLinger(on));
      }
      // THE BIOMES: grab arbitration. The Gallery melts a denied grab in your
      // hands; every grab that holds feeds the run-wide tally the Coronation
      // reads back. Outside a live run everything stays 'allow'.
      if (ctx.spawner && ctx.spawner.setGrabHooks) {
        ctx.spawner.setGrabHooks({
          policy: (rec) => (state === 'running' && bMech ? bMech.grabPolicy('card', rec) : 'allow'),
          onGrab: (rec) => { if (state === 'running' && bMech) bMech.onGrabbed('card', rec); },
          onMelt: (rec) => { if (state === 'running' && bMech) bMech.onMelted('card', rec); },
        });
      }
      if (ctx.wall && ctx.wall.setGrabHooks) {
        ctx.wall.setGrabHooks({
          policy: (rec) => (state === 'running' && bMech ? bMech.grabPolicy('poster', rec) : 'allow'),
          onGrab: (rec) => { if (state === 'running' && bMech) bMech.onGrabbed('poster', rec); },
          onMelt: (rec) => { if (state === 'running' && bMech) bMech.onMelted('poster', rec); },
        });
      }
      overlays = createOverlays(ctx.hud);
      // Shared world-hold contract. The VN portrait AND the in-run lesson cards
      // freeze the run the SAME way, so a discovery can't fire under a VN beat and
      // vice-versa (both set vnHold -> heldNow() gates every spawn/input/tick).
      // haltTunnel stops the tunnel dead; duckAudio silences everything but the bed.
      // Restore is state-aware: hub beats must hand back the 0.3 idle crawl, not run speed.
      const haltTunnel = (on) => { try { ctx.director.setTimeFactor(on ? 0 : (state === 'warren' ? 0.3 : baseTime())); } catch (e) { /* ignore */ } };
      const duckAudio = (on) => {
        try { setDucked(!!on); } catch (e) {}
        try { ctx.silenceVoice && ctx.silenceVoice(!!on); } catch (e) {}
        try { bridge.send({ type: 'vn-speaking', on: !!on }); } catch (e) {}
        // Freeze the whole field: no new bubbles, existing ones hold, popping locked.
        // On the run's opening VN beat this also means the bubbles don't start until
        // she's finished - the run "really" begins then.
        vnHold = !!on; syncHeld();
      };
      vn = createVnPortrait(ctx.hud, { getModId: () => activeModId, haltTunnel, duckAudio });
      try { vn.prime(); } catch (e) { /* manifest warms lazily on first beat */ }
      // First-discovery explainers: pause the field, show one card, click/any-key to resume.
      lessonCardUi = createLessonCard(ctx.hud, { haltTunnel, hold: duckAudio });
      lessons = createLessonTracker({ bridge, onComplete: onLessonComplete, onFirstTime: onFirstTimeAwarded });
      lessons.setCoveredProbe(() => covered || heavyActive());
      happy = createHappyPath();
      // The Warren's FTUE director: welcome beats on the first-ever hub open, guided
      // spend on the first return. Reads flags from the metaView warren hands it per
      // call; persists via a direct set-flag (NOT the per-run `flags` mirror, which
      // doesn't carry hub flags).
      hubGuide = createHubGuide({
        vnBeat: (b) => (vn ? vn.beat(b) : Promise.resolve(false)),
        vnCancel: () => { try { if (vn) vn.hide(); } catch (e) { /* ignore */ } },
        teach,
        teachBusy: () => !!(lessonCardUi && lessonCardUi.isBusy()),
        setFlag: (k) => bridge.send({ type: 'meta-command', op: 'set-flag', key: k }),
        log: (m) => bridge.log(m),
      });
      warren = createWarren({
        hud: ctx.hud, bridge,
        stations: ctx.hubStations,
        getMeta: () => (hostState ? hostState.meta : null),
        getMediaStats: () => (hostState ? hostState.mediaStats : null),
        runSetup,
        onDescend: (setup) => {
          state = 'requesting';
          bridge.send({ type: 'request-run', setup });
        },
        onExit: () => { if (requestExit) requestExit(); else bridge.send({ type: 'exit' }); },
        onOptions: ctx.openOptions,
        guide: hubGuide,
      });
      window.addEventListener('pointerdown', onPointerDownGlobal, true);
      window.addEventListener('pointerdown', onGlobalPointerDownCapture, true);
      window.addEventListener('contextmenu', onContextMenu);
      window.addEventListener('keydown', onToyKey);
      hudUi.setVisible(false);
      ctx.director.setTimeFactor(0.3);   // the tunnel idles at a crawl under the hub
      state = 'warren';
      warren.show('menu');
      // Seed the options panel from whatever snapshot already landed pre-attach.
      try { ctx.syncOptionUnlocks && ctx.syncOptionUnlocks((hostState && hostState.meta && hostState.meta.purchasedDials) || []); } catch (e) { /* ignore */ }
      try { ctx.setOptionProgress && ctx.setOptionProgress({ rankIndex: RANKS.forRuns((hostState && hostState.meta && hostState.meta.runsCompleted) | 0) }); } catch (e) { /* ignore */ }
    },

    /** The host answered request-run: deal the fresh config and drop in. */
    onRunConfig(m) {
      if (!m || !m.runConfig || !ctx) return;
      if (state !== 'requesting' && state !== 'warren') return;   // stale answer mid-run
      applyRunConfig(m.runConfig);
      warren.hide();
      ctx.director.setTimeFactor(1);
      ctx.director.reset();
      ctx.nav.reset();          // fresh plunge
      const short = shortNextCountdown;
      shortNextCountdown = false;
      beginCountdown(short);
    },

    /** A fresh meta snapshot landed - the Warren re-renders its shelves, and the
     *  options panel reveals any newly-purchased dials. */
    onMeta() {
      if (warren) warren.refresh();
      try { ctx && ctx.syncOptionUnlocks && ctx.syncOptionUnlocks((hostState && hostState.meta && hostState.meta.purchasedDials) || []); } catch (e) { /* panel not up */ }
      try { ctx && ctx.setOptionProgress && ctx.setOptionProgress({ rankIndex: RANKS.forRuns((hostState && hostState.meta && hostState.meta.runsCompleted) | 0) }); } catch (e) { /* panel not up */ }
    },

    /** Options-drawer "replay her lessons": re-arms every guide/teach flag + the
     *  lesson-card ledger C#-side and forces the next descent to deal the scripted
     *  classroom. The snapshot rebroadcast re-fires the hub welcome live. */
    resetOnboarding() {
      bridge.send({ type: 'meta-command', op: 'reset-onboarding' });
      sfx('ui_unlock', 0.55);
    },

    /** Esc routing: the Warren owns the key while it's up (scene skips its pause). */
    onEsc() {
      if (state === 'warren') return warren.handleEsc();
      if (state === 'requesting') return true;   // swallow while the hole opens
      return false;
    },

    /** scene.js hub-station pointer routing (only live while the Warren is up). */
    onStationPick(id) { if (state === 'warren' && warren) warren.onStationPick(id); },
    onStationMiss() { if (state === 'warren' && warren) warren.onStationMiss(); },

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
        // ---- Wave 2 toys/accessory on the frame clock ----
        if (state === 'running') {
          // the Wand: while the draw window is open the cursor lays ink; the
          // trail itself lives (ages, pops, bounces) inside chaosField.
          if (wandDrawSec > 0) {
            wandDrawSec -= dt;
            const c = field.cursor();
            field.wandInkAt(c.x, c.y);
            if (wandDrawSec <= 0) ctx.fx.holdFlash(false);
          }
          if (wandArcCd > 0) wandArcCd -= dt;   // Live Wire: paces the trail's discharges
          if (rippleArcCd > 0) rippleArcCd -= dt;   // Skinny Dipping: paces the wave's discharges
          if (sonarCd > 0) sonarCd -= dt;           // Trust Exercise: paces the sonar rings
          // the Pump: when the suction ends, the arrivals burst at the cursor
          if (pumpRemainingSec > 0) {
            pumpRemainingSec -= dt;
            if (pumpRemainingSec <= 0) {
              field.phys.pumpPull = 0;
              field.phys.inkFreeze = false;
              const c = field.cursor();
              const got = field.wandSweep(c.x, c.y, 190, true);
              sfx('golden_pop', 0.5);
              if (got > 0) hudUi.toast(`🫧 ${got} arrived at once`);
              // Milking Machine: the pump lets go and the whole drawing detonates.
              if (st.milkingMachine && field.wandInkActive()) {
                const inkGot = field.inkDetonate();
                sfx('estim_zap', 0.55);
                ctx.fx.pulseFlash(0.6);
                pulse('255,180,240', 0.40);
                hudUi.announce('🪄 MILKED', 'powerup', 1800, { subText: inkGot > 0 ? `the drawing takes ${inkGot}` : 'the drawing lets go' });
              }
            }
          }
          // The paddle: a grabbed 3D card sweeps the field - pops treats, snap-
          // defuses lives (rate-capped), flings rabbits. Core mechanic on any
          // run; Sticky Fingers just pays the pop bonus (see onBenignPopped).
          if (ctx.spawner && ctx.spawner.isGrabbing()) {
            const rect = ctx.spawner.heldCardScreenRect();
            if (rect) {
              const hit = field.paddleSweep(rect);
              if (hit.popped || hit.defused || hit.flung) ctx.spawner.notePaddleInteraction(hit);
            }
          }
        }
        // periodically post the per-asset engagement delta home (cheap; skips when empty)
        statsFlushCd -= dt;
        if (statsFlushCd <= 0) { statsFlushCd = STATS_FLUSH_SEC; flushAssetStats(); }
      }
      // Grab-in-the-tube: power-up cards only drift/spawn while actually falling.
      if (ctx && ctx.powerupDrops) ctx.powerupDrops.setSpawnEnabled(state === 'running' && !heldNow());
      field.update(dt);
    },

    /** DtRH intensity feeds the director (demoted to a presentation adapter). */
    moodIntensity() { return state === 'running' ? intensity() : 0; },

    setPaused(v) { paused = !!v; syncHeld(); },
    setHidden(v) { hidden = !!v; syncHeld(); },
    /** A native payload window (mandatory video) fully covers the page. A cover
     * that LIFTS while the run lives = a video endured to its end (porn_dvd). */
    setCovered(v) {
      const was = covered;
      covered = !!v;
      syncHeld();
      // a native payload owns the screen: tunnel pickups stand down
      if (covered && !was && ctx && ctx.spawner) ctx.spawner.clearPickups();
      if (was && !covered && state === 'running' && lessons) lessons.onVideoEndured();
    },

    /** M4: tunnel veils fire their wash only while the run is actually falling. */
    allowVeil() { return state === 'running' && !heldNow(); },

    // ---- grab-in-the-tube power-ups (engine/powerupDrops.js) ----
    /** The raw meta snapshot the drop picker reads (rank + discovered levels). */
    getMeta() { return hostState ? hostState.meta : null; },
    /** Veto an offer: only while running, never re-offer this run, and a consumable
     *  needs a free dock slot (passives always fit - they pin to the relic strip). */
    canOfferPowerup(id, kind) {
      if (state !== 'running' || !st) return false;
      if (st.runEquipment.has(id)) return false;
      if (kind === 'consumable') return toys.length < st.consumableSlots;
      return true;
    },
    /** Duo bait: multiply an offer's weight when grabbing that item would unlock
     *  (score 2) or progress (score 1) a still-draftable duo/trio boon. The nudge
     *  is strongest in the early chambers - there are still drafts left to cash
     *  the synergy in - and fades to its floor by chamber IV. */
    powerupSynergyBoost(id) {
      if (state !== 'running' || !st) return 1;
      const score = duoPartnerScore(id, [...st.runEquipment], st.takenBoonIds);
      if (!score) return 1;
      const early = Math.max(0, Math.min(1, (4 - st.waveIndex) / 3));   // I=1 .. IV=0 (waveIndex 1-based)
      return score >= 2 ? 2.5 + 2.5 * early    // completes/unlocks a duo: x5 -> x2.5
                        : 1.6 + 1.4 * early;   // first half of a pair:    x3 -> x1.6
    },
    /** A card was grabbed: unlock-on-discovery (free), then dock (consumable) or
     *  apply (passive), with the art flying from the tube point to its HUD slot. */
    onPowerupGrabbed(id, kind, screenPos) {
      if (!st || state !== 'running') return false;   // declined: the card stays in the tube
      handlePowerupGrab(id, kind, screenPos);
      return true;
    },

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
      hideDraftRoomChrome();
      if (draftDom && draftDom.root.parentNode) draftDom.root.parentNode.removeChild(draftDom.root);
      draftDom = null; draftRoomActive = false;
      warren?.dispose();
      lessons?.dispose();
      lessonCardUi?.dispose();   // removes its capture keydown + hud node
      hubGuide?.dispose();       // cancels any pending teach-queue timeout
      overlays?.dispose();
      hudUi?.dispose();
      field?.dispose();
      ffx?.dispose();
      payloadFx?.dispose();
      vn?.dispose();   // closes the VN AudioContext + removes its overlay/listeners (else it leaks per attach and hits Chromium's context ceiling)
      try { if (window.__sfPayloadFx === payloadFx) delete window.__sfPayloadFx; } catch { /* seam cleanup */ }
      try { delete window.__sfFireJunction; } catch { /* seam cleanup */ }
      try { delete window.__sfRegion; } catch { /* seam cleanup */ }
      field = null; hudUi = null; overlays = null; ffx = null; payloadFx = null;
      warren = null; lessons = null; happy = null; vn = null;
    },
  };
}
