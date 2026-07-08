/* ============================================================================
 * happyPath.js - the scripted-run director for the first descents, ported from
 * ChaosHappyPath.cs. Run 1 is fully scripted (the host deals the naked config
 * when RunsCompleted == 0: treats only, no drafts/darters/sins, gentle air) -
 * this module fires its teach beats in order. Run 2 gets light flag-guarded
 * debuts (braindrain + the guaranteed lucky bubble). Runs 4+ rig the FIRST
 * draft of a run once: the defanged demo sin (double_or_nothing, shielded),
 * and - any time both halves are worn - the electrified_rabbits duo demo.
 *
 * All state is run-scoped; every beat is wrapped so the script can never hurt
 * a run. The C# original also holds the Madam's narrative while scripting -
 * the narrative layer is not ported (Story mode is disabled in WPF too).
 * ==========================================================================*/

import { boonById } from './boons.js';

// ---- tunables (ChaosHappyPath.cs, one block) ----
const R1_THREAT_AT_PROGRESS = 0.30;
const R1_DRAFT_AT_PROGRESS = 0.55;
const R1_DRAFT_FALLBACK_PROGRESS = 0.70;
const R1_DARTER_AT_PROGRESS = 0.88;
const R1_THREAT_FUSE_MULT = 3.0;
const R1_STREAK_TEACH_COMBO = 3;
const R2_BRAINDRAIN_AT_PROGRESS = 0.25;
const R2_GOLDEN_AT_PROGRESS = 0.50;
const R1_DRAFT_POOL = ['extra_shield', 'defuse_chain', 'golden_touch', 'welcome_shower', 'heavy_drop'];

export function createHappyPath() {
  let runsAtStart = 0;
  let scripted = false;
  let active = false;

  // run 1 beats
  let streakTaught, threatSpawned, threatDefuseSeen, variantsJoined, draftFired, darterSpawned;
  let introStarted;   // the VN welcome plays once, on the first scripted tick
  // run 2 beats
  let braindrainDebuted, goldenSpawned;
  // draft rigging
  let draftsThisRun, shieldRigBoonId, firstSinDraftPending;

  function onRunStarted(runsCompleted, isScripted) {
    active = true;
    runsAtStart = runsCompleted | 0;
    scripted = !!isScripted;
    streakTaught = threatSpawned = threatDefuseSeen = variantsJoined = false;
    draftFired = darterSpawned = false;
    introStarted = false;
    braindrainDebuted = goldenSpawned = false;
    draftsThisRun = 0;
    shieldRigBoonId = null;
    firstSinDraftPending = false;
  }

  function onRunEnded() { active = false; scripted = false; }

  /**
   * Driven from the 250ms run tick. `io` is the run brain's beat surface:
   * { progress, combo, announce(text, kind, opts), toast, spawnScriptedThreat(fuseMult),
   *   triggerScriptedDraft(pool), spawnDarter(), spawnBraindrainDebut(), spawnGolden(),
   *   flags: {has(key), set(key)}, variantAllowed(id) }
   */
  function tickRun(io) {
    if (!active) return;
    try {
      if (scripted) tickFirstRun(io);
      else {
        // dev play-test: --dtrh-m2test lets the VN welcome fire on any descent
        try { if (typeof window !== 'undefined' && window.__dtrhVnTest) maybePlayIntro(io); } catch (e) { /* ignore */ }
        if (runsAtStart === 1) tickSecondRun(io);
      }
    } catch (e) {
      // The script must never hurt a run - but a swallowed beat is invisible,
      // so surface it in the host log.
      try { io.log && io.log('happyPath.tick: ' + (e && (e.stack || e.message))); } catch (e2) { /* ignore */ }
    }
  }

  // The Visual-Novel welcome: the persona greets, then tells you the one rule.
  // Fire-and-forget (async overlays); once per run. Also reachable in test mode
  // (window.__dtrhVnTest, set by boot on --dtrh-m2test) so it's play-testable on
  // any descent without needing a fresh profile.
  function maybePlayIntro(io) {
    if (introStarted || !io.vnBeat) return;
    introStarted = true;
    (async () => {
      try {
        await io.vnBeat('intro1');   // persona greets (emote + line + voice from the manifest)
        await io.vnBeat('intro2');   // the one rule: watch the bubbles, pop them
      } catch (e) { /* the run must never wait on the VN */ }
    })();
  }

  function tickFirstRun(io) {
    maybePlayIntro(io);
    if (!streakTaught && io.combo() >= R1_STREAK_TEACH_COMBO) {
      streakTaught = true;
      io.announce('pops in a row build a streak. it pays more.', 'streak', 3200);
      io.toast('🔥 a streak. keep it alive.');
    }
    if (!threatSpawned && io.progress() >= R1_THREAT_AT_PROGRESS) {
      threatSpawned = true;
      if (!io.flags.has('seenDefuseTutorial')) {
        io.flags.set('seenDefuseTutorial');
        io.announce('press and HOLD a live one to snap it', 'freeze', 3200);
      }
      io.spawnScriptedThreat(R1_THREAT_FUSE_MULT);
      io.toast('◉ one live one. take your time with it.');
    }
    if (!draftFired && io.progress() >= R1_DRAFT_AT_PROGRESS
        && (threatDefuseSeen || io.progress() >= R1_DRAFT_FALLBACK_PROGRESS)) {
      const pool = R1_DRAFT_POOL
        .map((id) => boonById(id))
        .filter((b) => b && !b.curse)
        .sort(() => Math.random() - 0.5)
        .slice(0, 3);
      draftFired = pool.length === 0 || io.triggerScriptedDraft(pool);
    }
    if (!darterSpawned && io.progress() >= R1_DARTER_AT_PROGRESS) {
      darterSpawned = true;
      io.announce('🐇 a white rabbit. catch it.', 'powerup', 3200);
      io.spawnDarter();
    }
  }

  /** A defuse completed. On run 1 the first one opens the live whitelist. */
  function onDefuseCompleted(io) {
    if (!active || !scripted) return;
    try {
      if (!threatSpawned || variantsJoined) { threatDefuseSeen = threatDefuseSeen || threatSpawned; return; }
      threatDefuseSeen = true;
      variantsJoined = true;
      io.joinVariants(['pink', 'spiral']);
      io.announce('pink and spiral drift in. hold them down too.', 'powerup', 3200);
      io.toast('◉ more of them live now.');
    } catch (e) { /* ignore */ }
  }

  function tickSecondRun(io) {
    if (!braindrainDebuted && io.progress() >= R2_BRAINDRAIN_AT_PROGRESS) {
      braindrainDebuted = true;
      if (!io.flags.has('seenBraindrain') && io.variantAllowed('braindrain')) {
        io.flags.set('seenBraindrain');
        io.announce('◍ BRAINDRAIN. bigger. heavier. hold it down.', 'powerup', 3200);
        io.toast('◍ something heavy sinks in beside you');
        io.spawnBraindrainDebut();
      }
    }
    if (!goldenSpawned && io.progress() >= R2_GOLDEN_AT_PROGRESS) {
      goldenSpawned = true;
      io.spawnGolden();
    }
  }

  /**
   * Called right after every draft deal (mutates `options` in place). Run 4+
   * (runsAtStart >= 3, once ever) guarantees a SHIELDED double_or_nothing; the
   * first run with the_spanker + e_stim both equipped guarantees the duo card.
   * `io` adds: { allowCurses, equipmentHas(id), announceTeach(text) , bark(event) }
   */
  function rigDraft(options, io) {
    try {
      draftsThisRun++;
      if (draftsThisRun !== 1 || !options.length) return;

      if (runsAtStart >= 3 && !io.flags.has('seenFirstSin') && io.allowCurses) {
        const don = boonById('double_or_nothing');
        if (!don) return;
        if (!options.some((o) => o.id === don.id)) {
          let idx = options.findIndex((o) => o.curse);
          if (idx < 0) idx = options.length - 1;
          options[idx] = don;
        }
        shieldRigBoonId = don.id;
        firstSinDraftPending = true;
        io.announce('☠ a sin is on the table. the first taste is free. this once.', 'bad', 3200);
        return;
      }

      if (!io.flags.has('seenDuoDemo') && io.equipmentHas('the_spanker') && io.equipmentHas('e_stim')) {
        const duo = boonById('electrified_rabbits');
        if (!duo) return;
        if (!options.some((o) => o.id === duo.id)) options[options.length - 1] = duo;
        io.flags.set('seenDuoDemo');
        io.announce('a gold card. your toys learned to work together.', 'powerup', 3200);
        io.bark('duo-demo');
      }
    } catch (e) { /* ignore */ }
  }

  /** The run-4 demo sin was picked: its downside cannot fire, this once. */
  function shouldShieldSin(boonId) {
    if (!shieldRigBoonId || boonId !== shieldRigBoonId) return false;
    shieldRigBoonId = null;
    return true;
  }

  /** Any draft resolved (pick or skip): the first-sin beat is spent either way. */
  function onDraftResolved(io) {
    if (!firstSinDraftPending) return;
    firstSinDraftPending = false;
    try { io.flags.set('seenFirstSin'); } catch (e) { /* ignore */ }
  }

  return {
    onRunStarted, onRunEnded, tickRun, onDefuseCompleted,
    rigDraft, shouldShieldSin, onDraftResolved,
    isScripting: () => active && runsAtStart < 4,
    isScripted: () => scripted,
  };
}
