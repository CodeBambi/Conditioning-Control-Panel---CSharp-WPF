/* ============================================================================
 * ui/duel/botDuel.js - the practice bot's half of a game night duel.
 *
 * Owner, 2026-09-23: "make in practice the minigame happen so i test". The duel
 * is a two-sided protocol (ui/duel/duelController.js), so in practice the other
 * side has to be the bot. This is that side, and nothing more: it speaks the
 * SAME `t:'duel'` frames through the practice loopback (the guest match the
 * solo driver owns), so the player's own controller, overlay, result and recap
 * tally run exactly as they do against a person. No back door into the
 * player's controller, no frame ever leaves the in-process loopback pair:
 * createBotDuel is built in one place, ui/soloDriver.js, which boot.js only
 * builds in startSolo.
 *
 * IT CANNOT PLAY THE REAL GAME. A duel is now the real Arcademy class in the
 * player's window (ui/duel/arcademyHost.js), and that is a DOM game the bot
 * has no hands for. So the bot reports a BELIEVABLE result for the game the
 * card holds: a per-game plausible range (ui/duel/games.js `bot`), drawn from
 * the seeded practice rng, landing inside the player's grace window. It wins
 * some and loses more.
 *
 * THE RECEIVER DECIDES, on the bot's side too: an inbound start is refused
 * with `sub:'busy'` while it is in a duel or its result hold, on an unexpected
 * idx, past DUEL_MAX_PER_MATCH, or inside the gap (that duel's length +
 * DUEL_GAP_EXTRA_MS). Accepting is silence, as it is for a person.
 *
 * IT THROWS TOO, now and then, so the player also sees the "incoming game"
 * side: first at THROW_FIRST_MS into Live, then a chance every THROW_RETRY_MS.
 * The player's controller decides; a busy answer takes the throw back.
 *
 * A SORT CARD against a player whose build picks noise (night revision 2) runs the
 * same reveal and pick the player sees: the bot picks a board at random somewhere
 * inside the pick window and says so (`sub:'noise'`), and its report lands after
 * the pre-play time as well as the clock.
 *
 * Node-import-safe: clock, timers and rng are injectable (tests run it on a
 * virtual clock).
 * ==========================================================================*/

import { GoonMatchPhase } from '../../core/contracts.js';
import { DUEL_INTRO_MS, pickLength } from './rules.js';
import { botResult, knownGame, normalizeGameId, pickDuelGame } from './games.js';
import { DUEL_GAP_EXTRA_MS, DUEL_MAX_PER_MATCH } from './duelController.js';
import { NOISE_PICK_MS, NOISE_PRE_PLAY_MS, NOISE_REVEAL_MS, rollNoiseSet } from '../../core/noiseSets.js';

/** How late its score lands after the clock runs out (inside the player's grace window). */
export const BOT_REPORT_MS = Object.freeze([300, 1400]);
/** Its own result hold, mirroring the player's. */
export const BOT_RESULT_HOLD_MS = 2600;
/** First throw this long into Live, then a coin flip every THROW_RETRY_MS. */
export const THROW_FIRST_MS = 75000;
export const THROW_RETRY_MS = 30000;
export const THROW_CHANCE = 0.4;

/**
 * @param {object} o
 * @param {object} o.match the practice GUEST match (the bot's side of the loopback)
 * @param {Function} [o.rand] () => [0,1)
 * @param {Function} [o.now]
 * @param {Function} [o.later] (fn, ms) => cancel
 * @param {Function} [o.log]
 * @param {boolean} [o.throws] false = answer only, never throw
 */
export function createBotDuel({
  match, rand = Math.random, now = () => Date.now(),
  later = (fn, ms) => { const t = setTimeout(fn, ms); return () => clearTimeout(t); },
  log = null, throws = true,
} = {}) {
  let nextIdx = 0;
  let started = 0;
  let notBefore = 0;
  let cur = null;            // {idx, len, by, stage, cancels[]}
  const loose = [];          // throw timers, not tied to a duel
  const unsubs = [];

  const say = (m) => { try { if (typeof log === 'function') log(m); } catch (_e) { /* never */ } };
  const pick = (r) => r[0] + rand() * (r[1] - r[0]);
  const isLive = () => !!match && match.phase === GoonMatchPhase.Live;

  function at(ms, fn) {
    const c = later(() => { if (cur) fn(); }, ms);
    if (cur) cur.cancels.push(c);
  }

  /** The time before the game is up on the player's side: the intro card, or the Sort reveal + pick. */
  function prePlayMs(game) {
    return game === 'sort' && match && match.peerPicksNoise ? NOISE_PRE_PLAY_MS : DUEL_INTRO_MS;
  }

  function begin(idx, len, by, game) {
    cur = { idx, len: pickLength(len), by, game: normalizeGameId(game), stage: 'intro', cancels: [], noise: '' };
    nextIdx = Math.max(nextIdx, idx + 1);
    started++;
    say('duel ' + idx + ' ' + (by === 'bot' ? 'thrown' : 'accepted') + ', ' + cur.game + ', ' + cur.len + 's');
    arm();
  }

  /** The report (and, on a Sort noise duel, the pick), from the start of this duel. */
  function arm() {
    // The game runs from the end of the intro to the end of the clock; the bot "finishes" it
    // at the end (nobody can see its screen) and reports a beat later, the way a person's score lands.
    at(prePlayMs(cur.game) + cur.len * 1000 + pick(BOT_REPORT_MS), report);
    // Its noise pick: a random board, at a random moment inside the pick window.
    if (prePlayMs(cur.game) !== DUEL_INTRO_MS) {
      at(NOISE_REVEAL_MS + pick([600, NOISE_PICK_MS - 900]), () => {
        if (!cur || cur.noise) return;
        cur.noise = rollNoiseSet(rand);
        if (match) match.sendDuel({ sub: 'noise', idx: cur.idx, set: cur.noise });
        say('duel ' + cur.idx + ' noise: ' + cur.noise);
      });
    }
  }

  function report() {
    if (!cur || cur.stage === 'result') return;
    cur.stage = 'result';
    const mine = botResult(cur.game, cur.len, rand);
    if (match) match.sendDuel({ sub: 'score', idx: cur.idx, game: cur.game, score: mine.score, tile: mine.tile | 0 });
    say('duel ' + cur.idx + ' reported ' + cur.game + ' tile ' + (mine.tile | 0) + ' score ' + mine.score);
    at(BOT_RESULT_HOLD_MS, () => close(true));
  }

  function close(played) {
    if (!cur) return;
    for (const c of cur.cancels) { try { c(); } catch (_e) { /* gone */ } }
    if (played) notBefore = now() + cur.len * 1000 + DUEL_GAP_EXTRA_MS;
    cur = null;
  }

  function room() { return started < DUEL_MAX_PER_MATCH && now() >= notBefore; }

  function refuse(idx, why) {
    say('duel ' + idx + ' refused: ' + why);
    if (match) match.sendDuel({ sub: 'busy', idx });
  }

  function onStart(f) {
    if (cur && cur.idx === f.idx) {
      // Both threw at once: the host (the player) wins; the bot is the guest and adopts its length.
      if (cur.stage === 'intro' && cur.by === 'bot') {
        for (const c of cur.cancels) { try { c(); } catch (_e) { /* gone */ } }
        cur.cancels = [];
        cur.by = 'them';
        cur.len = pickLength(f.len_s);
        arm();
        say('duel ' + cur.idx + ' collision: the player\'s start wins, ' + cur.len + 's');
      }
      return;
    }
    if (!isLive()) return;
    if (cur) { refuse(f.idx, cur.stage === 'result' ? 'result-hold' : 'busy'); return; }
    if (f.idx !== nextIdx) { refuse(f.idx, 'idx'); return; }
    if (!room()) { refuse(f.idx, 'gated'); return; }
    if (!knownGame(f.game)) { refuse(f.idx, 'game'); return; }
    begin(f.idx, f.len_s, 'them', f.game);
  }

  function onBusy(f) {
    if (!cur || cur.idx !== f.idx || cur.by !== 'bot' || cur.stage !== 'intro') return;
    say('duel ' + f.idx + ' throw taken back (player busy)');
    nextIdx = f.idx;
    started = Math.max(0, started - 1);
    close(false);
  }

  function onFrame(f) {
    if (!f) return;
    if (f.sub === 'start') onStart(f);
    else if (f.sub === 'busy') onBusy(f);
    // 'score' and 'cfg' need nothing from the bot: it never computes the outcome it cannot bank.
  }

  function tryThrow() {
    if (cur || !isLive() || !room()) return false;
    if (!match || !match.peerSupportsNight) return false;
    const idx = nextIdx;
    const len = pickLength(match.peerDuelLen);
    const game = pickDuelGame(match.matchSeed, idx);
    begin(idx, len, 'bot', game);
    if (!match.sendDuel({ sub: 'start', idx, len_s: len, game })) {
      if (cur && cur.idx === idx) { nextIdx = idx; started = Math.max(0, started - 1); close(false); }
      return false;
    }
    return true;
  }

  function armThrows() {
    const retry = () => {
      if (!isLive()) return;
      if (rand() < THROW_CHANCE) tryThrow();
      loose.push(later(retry, THROW_RETRY_MS));
    };
    loose.push(later(() => { if (isLive()) tryThrow(); loose.push(later(retry, THROW_RETRY_MS)); }, THROW_FIRST_MS));
  }

  function stopThrows() { for (const c of loose.splice(0)) { try { c(); } catch (_e) { /* gone */ } } }

  function onPhase(p) {
    if (p === GoonMatchPhase.Live) { if (throws) armThrows(); return; }
    stopThrows();
    if (cur) close(true);
  }

  if (match && typeof match.onDuelFrame === 'function') unsubs.push(match.onDuelFrame(onFrame));
  if (match && typeof match.onPhaseChanged === 'function') unsubs.push(match.onPhaseChanged(onPhase));
  if (isLive()) onPhase(GoonMatchPhase.Live);

  return {
    tryThrow,
    get busy() { return !!cur; },
    get state() { return cur ? { idx: cur.idx, len: cur.len, by: cur.by, stage: cur.stage, game: cur.game, noise: cur.noise } : null; },
    get nextIdx() { return nextIdx; },
    dispose() {
      stopThrows();
      close(false);
      for (const u of unsubs.splice(0)) { try { if (typeof u === 'function') u(); } catch (_e) { /* gone */ } }
    },
  };
}

export default createBotDuel;
