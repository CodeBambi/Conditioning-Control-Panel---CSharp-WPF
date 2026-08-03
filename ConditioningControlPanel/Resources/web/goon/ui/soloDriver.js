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
import { GoonMatchPhase, GoonPayloadKind, costOf } from '../core/contracts.js';

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

  match.suddenDeathRunner = new GoonSuddenDeathRunner({ presenter, inputs, logger });

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

  function tryPayload() {
    if (match.phase !== GoonMatchPhase.Live && match.phase !== GoonMatchPhase.SuddenDeath) return;
    const charges = match.scoring.charges;
    const affordable = (match.availablePayloadKinds || [])
      .filter((k) => k !== GoonPayloadKind.BrainDrain && costOf(k) <= charges);
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

  function onPhase(phase) {
    switch (phase) {
      case GoonMatchPhase.Consent: onConsent(); break;
      case GoonMatchPhase.Draft: doDraft(); break;
      case GoonMatchPhase.Live:
        match.setCloseness(1);
        payloadTimer = ledger.interval(tryPayload, PAYLOAD_TRY_MS);
        // Bluffs its closeness dial upward as the match wears on. It is
        // self-reported and bluffable BY DESIGN — the bot should use that.
        ledger.interval(() => {
          if (match.phase !== GoonMatchPhase.Live) return;
          match.setCloseness(Math.min(3, 1 + Math.trunc(rng.nextDouble() * 3)));
        }, 45000);
        break;
      case GoonMatchPhase.Recap:
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
