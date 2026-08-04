/* ============================================================================
 * ui/soloDriver.js — the scripted opponent for Practice mode.
 *
 * Drives the GUEST half of a loopback pair through a whole match so the game is
 * playable alone: it confirms consent, signs the draft agreement, endures
 * what you send it, occasionally sends something back, and answers sudden-death
 * rounds with mediocre-but-honest timings.
 *
 * DELIBERATELY NOT AN AI, and deliberately not good at the game. Its job is to
 * exercise every phase transition and every event seam the real opponent would,
 * so a bug that only shows up "when the other side does X" shows up here first.
 * It plays through the SAME public match API a human client uses — it has no
 * back door, which is what makes it a real integration test.
 *
 * Fairness note: the bot never false-starts a reaction duel (it presses only on
 * GoonStimulusKind.Real). That is a limitation of the script, not a rule — a
 * human opponent absolutely can, and reactionDuel.js scores it as a loss.
 * ==========================================================================*/

import { createLedger } from './router.js';
import { GoonRng } from '../core/rng.js';
import { GoonStimulusKind, fakeRoundInputs } from '../core/rounds/model.js';
import { GoonSuddenDeathRunner } from '../core/suddenDeath.js';
import { GoonMatchPhase, GoonPayloadKind, GoonConsts, costOf } from '../core/contracts.js';

/** How long the bot "thinks" before signing the consent sheet. */
const CONSENT_THINK_MS = [700, 1600];
/** …and before locking its draft, so the human sees the slots fill. */
const DRAFT_THINK_MS = [900, 2200];
/** Gap between the bot's payload attempts. The engine's rate limiter is stricter. */
const PAYLOAD_TRY_MS = 20000;
/** Mediocre human reaction. Fast enough to be a threat, slow enough to beat. */
const REACTION_MS = [300, 480];
/** Lock-card solve time at difficulty 1; climbs with the round. */
const LOCKCARD_MS = [4200, 6800];
/** Gap between bubble pops. */
const BUBBLE_POP_MS = [420, 900];

/* ----------------------------------------------------------------------------
 * THE OPENING SALVO — practice only, and it exists because of one owner report:
 * "from mobile it's not triggering any asset even if I uploaded them and went to
 * practice" (2026-08-04).
 *
 * THE SHAPE OF THAT BUG. In a duel your arsenal shot lands on THEIR screen. In
 * practice the opponent is this script, which has no exec/ renderer at all (see
 * the fake-window block below), so a payload the player fires is a payload
 * nobody ever sees. The only media that reaches the practice player's own screen
 * is (a) the element ramp and (b) what the bot throws BACK — and an inbound
 * payload resolves against the RECEIVER's library (exec/media.js drawFor falls
 * through to drawKind), so the bot's flash burst is drawn from the player's OWN
 * uploads. That is the loop that shows a phone player their library, and it used
 * to depend on the bot first earning charges the slow way: `tryPayload` waits
 * PAYLOAD_TRY_MS, skips 35% of its turns and needs `charges >= cost`, which on a
 * fresh match can be minutes of nothing.
 *
 * So the bot opens on a clock instead of on its economy. The charges are still
 * credited through the engine (`creditCharges`), so the wire truth the receiver
 * validates is the truth — this buys the bot no shot it could not have taken.
 *
 * PRACTICE ONLY, structurally: createSoloDriver is constructed in exactly one
 * place (boot.js startSolo). Duel balance cannot reach this file.
 * ------------------------------------------------------------------------- */
export const SALVO = Object.freeze([
  // Images, almost immediately: the whole point is "I can see my own pictures".
  Object.freeze({ atMs: 6000, kind: GoonPayloadKind.FlashBurst, durationMs: 6000, intensity: 0.7 }),
  // …and a clip, past the engine's 30s payload gap, into a floating window.
  Object.freeze({ atMs: 45000, kind: GoonPayloadKind.Video, durationMs: 45000, intensity: 0.6 }),
]);

/**
 * How long BEFORE a salvo shot its charges are credited.
 *
 * THE RECEIVER IS THE AUTHORITY ON THE SENDER'S WALLET, and it reads that wallet
 * off the last state tick, not off the payload frame — credit-then-fire in the
 * same turn is rejected with `charge economy: claimed 0 < cost 1`, which is
 * exactly what the first cut of this salvo did. GoonConsts.TickIntervalMs is
 * 3000, so a lead comfortably past one tick is what makes the shot land.
 */
export const SALVO_CREDIT_LEAD_MS = 5000;

/**
 * @param {object} o
 * @param {object} o.match the GUEST match (its transport is the loopback peer)
 * @param {string} [o.name] display name the human sees
 * @param {bigint|number} [o.seed]
 * @param {object} [o.logger]
 */
export function createSoloDriver({ match, name = 'Practice', seed = 0xB0BBn, logger = null }) {
  const ledger = createLedger();
  ledger.logger = logger;
  const rng = new GoonRng(typeof seed === 'bigint' ? seed : BigInt(seed | 0));
  let started = false;
  let payloadTimer = 0;
  /** Latched once the bot has made its agreement decision (see doDraft). */
  let draftDecided = false;

  const pick = (range) => range[0] + Math.trunc(rng.nextDouble() * (range[1] - range[0]));
  const log = (m) => { try { logger?.debug?.('[GG solo] ' + m); } catch (_e) { /* ignore */ } };

  match.localDisplayName = name;
  match.localAppVersion = 'bot';

  /* ------------------------------------------------------------ sudden death
   * A presenter that plays the round instead of drawing it. Every timer goes
   * through the ledger, so stopping the driver mid-round cannot leave a pop or
   * a keypress arriving after the match is gone.
   * ----------------------------------------------------------------------- */
  const inputs = fakeRoundInputs();
  let roundTimers = [];

  function clearRound() {
    for (const t of roundTimers) { try { clearTimeout(t); } catch (_e) { /* ignore */ } }
    roundTimers = [];
  }
  function later(fn, ms) {
    const t = setTimeout(() => { try { fn(); } catch (e) { log('round step threw: ' + ((e && e.message) || e)); } }, Math.max(0, ms));
    roundTimers.push(t);
    return t;
  }
  ledger.add(clearRound);

  let difficulty = 1;

  const presenter = {
    showRoundIntro(intro) { difficulty = (intro && intro.difficulty) || 1; },

    showLockCard(spec) {
      const base = pick(LOCKCARD_MS) + (difficulty - 1) * 900;
      const limit = (spec && spec.timeLimitMs) || 60000;
      // Never "solve" after the round's own timeout — that would be a solve the
      // engine has already scored as a miss.
      later(() => inputs.raiseSolved(Math.trunc(rng.nextDouble() * 3)), Math.min(base, limit - 400));
    },
    hideLockCard() { clearRound(); },

    startStaringContest(spec) {
      const dur = (spec && spec.durationMs) || 20000;
      // A steady, unremarkable attention trace…
      for (let t = 500; t < dur; t += 1000) {
        later(() => inputs.raiseSample(0.72 + rng.nextDouble() * 0.2), t);
      }
      // …and a blink somewhere in the last third, most of the time.
      if (rng.nextDouble() < 0.7) later(() => inputs.raiseBlink(), Math.trunc(dur * (0.66 + rng.nextDouble() * 0.3)));
    },
    endStaringContest() { clearRound(); },

    armReactionDuel() { /* the round drives the stimulus; we only answer it */ },
    fireReactionStimulus(kind) {
      if (kind !== GoonStimulusKind.Real) return;   // feints do not fool a script
      later(() => inputs.raisePress(), pick(REACTION_MS));
    },
    endReactionDuel() { clearRound(); },

    startBubbleRace(spec) {
      const bubbles = (spec && spec.bubbles) || [];
      let t = 300;
      for (const b of bubbles) {
        t += pick(BUBBLE_POP_MS);
        const idx = b.index;
        later(() => inputs.raisePop(idx), t);
      }
    },
    endBubbleRace() { clearRound(); },

    showRoundVerdict() { clearRound(); },
  };

  /* SUDDEN DEATH IS DETACHED — owner's call, 2026-08-03, pending the rounds
   * rework. Practice now mirrors a real run exactly: Countdown -> Live -> Recap,
   * with the Live clock settling on score. The presenter above is left wired and
   * unused on purpose; re-enable by restoring this line.
   *
   * match.suddenDeathRunner = new GoonSuddenDeathRunner({ presenter, inputs, logger });
   */
  void GoonSuddenDeathRunner;
  void presenter;

  /* ------------------------------------------------------------------ phases */

  function onConsent() {
    if (match.phase !== GoonMatchPhase.Consent) return;
    if (match.localConsentConfirmed) return;
    // Re-signs after every change the human makes — that is the whole point of
    // the two-lamp strip, and the bot has to honour it like a person would.
    ledger.timer(() => {
      if (ledger.isDisposed) return;
      if (match.phase !== GoonMatchPhase.Consent || match.localConsentConfirmed) return;
      match.confirmConsent();
      log('consent confirmed');
    }, pick(CONSENT_THINK_MS));
  }

  function doDraft() {
    if (match.phase !== GoonMatchPhase.Draft || match.localDraftConfirmed) return;
    // setAllowedElements RAISES draftChanged, which is one of our own subscriptions — without
    // this latch the bot re-decides inside its own notification until the stack blows. (It did,
    // on the first headless run.) The latch also survives the human moving a toggle, which
    // clears both signatures: `draftDecided` stays true, so the bot only re-signs.
    const firstPass = !draftDecided;
    draftDecided = true;

    if (firstPass) {
      // The agreement starts wide open. A practice opponent that vetoes ONE thing exercises the
      // intersection without ever risking the two-element floor.
      const mine = (match.localAllowedElements || []).slice();
      if (mine.length > 3 && rng.nextDouble() < 0.6) {
        const veto = mine[rng.nextInt(0, mine.length)];
        const res = match.toggleAllowedElement(veto);
        log('vetoed ' + veto + (res.ok ? '' : ' (' + res.error + ')'));
      }
    }

    ledger.timer(() => {
      if (ledger.isDisposed || match.phase !== GoonMatchPhase.Draft) return;
      if (match.localDraftConfirmed) return;
      const res = match.confirmDraft();
      log('draft signed, pool ' + (match.sharedElementPool || []).join('+') +
        (res.ok ? '' : ' (' + res.error + ')'));
    }, pick(DRAFT_THINK_MS));
  }

  /** Salvo shots whose charges are credited and not yet spent (see fireSalvo). */
  let salvoArmed = 0;

  function tryPayload() {
    if (match.phase !== GoonMatchPhase.Live && match.phase !== GoonMatchPhase.SuddenDeath) return;
    // A salvo shot's charges are already credited and SPOKEN FOR. Letting the
    // ordinary cadence spend them would turn "the practice player sees a clip at
    // 45s" into a coin flip.
    if (salvoArmed > 0) return;
    const charges = match.scoring.charges;
    // BrainDrain: once per match, and the bot must not spend it on a whim.
    // BubbleSwarm: the human has no such sticker any more (ui/arsenal.js, owner
    // 2026-08-04 — bubbles are the always-on field, so a bubble throwable bought
    // nothing) and the practice bot throws from the player's own deck. The kind
    // stays legal on the wire and stays rendered inbound; nothing local sends one.
    const affordable = (match.availablePayloadKinds || [])
      .filter((k) => k !== GoonPayloadKind.BrainDrain
        && k !== GoonPayloadKind.BubbleSwarm
        && costOf(k) <= charges);
    if (affordable.length === 0) return;
    if (rng.nextDouble() < 0.35) return;    // it does not fire the instant it can

    const kind = affordable[rng.nextInt(0, affordable.length)];
    const res = match.tryFirePayload({
      kind,
      durationMs: 20000 + rng.nextInt(0, 25000),
      intensity: 0.4 + rng.nextDouble() * 0.4,
      text: null,
    });
    log('payload ' + kind + (res.ok ? ' sent' : ' refused: ' + res.error));
  }

  /**
   * Step one of a salvo shot: put the charges in the wallet, early enough that a
   * state tick carries them to the receiver (SALVO_CREDIT_LEAD_MS). A kind the
   * agreement left out never gets funded — the bot cannot send what the two of
   * you did not allow, salvo or not.
   */
  function armSalvo(spec) {
    if (ledger.isDisposed || match.phase !== GoonMatchPhase.Live) return;
    if (!(match.availablePayloadKinds || []).includes(spec.kind)) {
      log('salvo ' + spec.kind + ' skipped (not in the agreed pool)');
      return;
    }
    const cost = costOf(spec.kind);
    const have = (match.scoring && match.scoring.charges) | 0;
    if (have < cost) match.creditCharges(cost - have, 'practice-salvo');
    salvoArmed++;
  }

  /**
   * Step two: fire, through the SAME public API tryPayload uses, so every engine
   * gate (phase, rate limit, caps, the shared pool, the charge check) still
   * applies. Whatever happens the shot stops being reserved.
   */
  function fireSalvo(spec) {
    if (ledger.isDisposed) return;
    if (salvoArmed > 0) salvoArmed--;
    if (match.phase !== GoonMatchPhase.Live) return;
    if (!(match.availablePayloadKinds || []).includes(spec.kind)) return;
    const res = match.tryFirePayload({
      kind: spec.kind,
      durationMs: spec.durationMs,
      intensity: spec.intensity,
      text: null,
    });
    log('salvo ' + spec.kind + (res.ok ? ' sent' : ' refused: ' + res.error));
  }

  /* ------------------------------------------------ fake floating windows */
  // The bot has no exec/ renderer, so its REAL window count never leaves 0 and
  // practice would never show the monitor's window minis. Simulate instead: a
  // video payload the human lands "opens a window" on the bot's screen for its
  // duration (60s ceiling, like the real pool), and now and then the bot "pops
  // a video bubble" of its own. The count rides the real tick field (`vwin`),
  // so the human sees the true monitor pipeline, fed honest-shaped data.
  const fakeWins = [];   // expiry timestamps (ms)
  function publishFakeWins() {
    const now = Date.now();
    for (let i = fakeWins.length - 1; i >= 0; i--) { if (fakeWins[i] <= now) fakeWins.splice(i, 1); }
    match.setLocalWindowCount(fakeWins.length);
  }
  function fakeWindow(ms) {
    if (fakeWins.length >= GoonConsts.VideoWindowsDisplayMax) return;
    fakeWins.push(Date.now() + ms);
    publishFakeWins();
  }

  function onPhase(phase) {
    switch (phase) {
      case GoonMatchPhase.Consent: onConsent(); break;
      case GoonMatchPhase.Draft: doDraft(); break;
      case GoonMatchPhase.Live:
        match.setCloseness(1);
        // The salvo BEFORE the economy timer: it is the only thing that puts the
        // practice player's own library on the practice player's own screen on a
        // predictable clock. Through the ledger, so leaving mid-match cannot land
        // a flash burst on the recap.
        for (const spec of SALVO) {
          ledger.timer(() => armSalvo(spec), Math.max(0, spec.atMs - SALVO_CREDIT_LEAD_MS));
          ledger.timer(() => fireSalvo(spec), spec.atMs);
        }
        payloadTimer = ledger.interval(tryPayload, PAYLOAD_TRY_MS);
        ledger.interval(() => {
          if (match.phase !== GoonMatchPhase.Live) return;
          if (rng.nextDouble() < 0.22) fakeWindow(30000);   // the bot's own "video bubble pop"
          publishFakeWins();                                 // prune expiries either way
        }, 8000);
        // Bluffs its closeness dial upward as the match wears on. It is
        // self-reported and bluffable BY DESIGN — the bot should use that.
        ledger.interval(() => {
          if (match.phase !== GoonMatchPhase.Live) return;
          match.setCloseness(Math.min(3, 1 + Math.trunc(rng.nextDouble() * 3)));
        }, 45000);
        break;
      case GoonMatchPhase.Recap:
        fakeWins.length = 0;
        match.setLocalWindowCount(0);   // zero is always accepted; no phantom count into recap
        clearRound();
        break;
      default: break;
    }
  }

  /* ------------------------------------------------------- inbound payloads */

  function onPayloadAccepted(ev) {
    const p = ev && ev.payload;
    if (!p) return;
    // It endures everything, all the way. A bot that flinched would hide the
    // "+1 charge for them" path from the human's recap.
    const wait = Math.max(1000, (p.duration_ms | 0)) + 300;
    // A landed video "opens a window" on the bot's screen (see fake block above).
    if (p.kind === GoonPayloadKind.Video) fakeWindow(Math.min(60000, Math.max(1000, p.duration_ms | 0)));
    ledger.timer(() => {
      if (ledger.isDisposed) return;
      match.notifyInboundPayloadFinished(p.id, true);
      log('endured ' + p.id);
    }, wait);
  }

  return {
    start() {
      if (started) return;
      started = true;
      ledger.sub(match.onPhaseChanged(onPhase));
      ledger.sub(match.onConsentChanged(onConsent));
      // Re-entry is guarded inside doDraft(); this exists so a pool that grows
      // late (a hello landing after the phase change) still gets drafted.
      ledger.sub(match.onDraftChanged(doDraft));
      ledger.sub(match.onPayloadAccepted(onPayloadAccepted));
      onPhase(match.phase);
      log('driver armed');
    },
    stop() {
      try { clearInterval(payloadTimer); } catch (_e) { /* ignore */ }
      ledger.dispose();
    },
    get inputs() { return inputs; },
  };
}

export default createSoloDriver;
