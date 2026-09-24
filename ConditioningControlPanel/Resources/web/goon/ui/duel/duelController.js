/* ============================================================================
 * ui/duel/duelController.js - game night duels, end to end on one side.
 *
 * A GAME CARD drops from bubble pops (ui/drops.js rolls it, ui/arsenal.js holds
 * it). Throwing it sends `t:'duel' sub:'start'`; both screens show a short
 * "incoming game" card, then the same seeded Deep End board for the duel
 * length. At the end each side sends `sub:'score'` with its own board, waits a
 * grace window for theirs (silence counts as 0) and computes the same winner
 * from the same two numbers. The winner adds DUEL_WIN_BONUS to its own match
 * score through GoonScoring.awardBonus, which rides the next state tick.
 *
 * WHAT A DUEL NEVER TOUCHES: Mercy (z60, above this overlay, and a phase change
 * out of Live closes the duel with no bonus), panic, lockdown, session state.
 * The ramp keeps running underneath; only throws pause (arsenal asks busy()).
 *
 * THE LENGTH: the host's pick wins. The host sends `sub:'cfg'` once at Live and
 * the engine keeps it (match.peerDuelLen, so a HUD that mounts late still sees
 * it); a guest's own start frames carry the host's number too, and each side
 * plays the length on the start frame it acted on.
 *
 * SIMULTANEOUS THROWS: both sides number duels from the same counter, so two
 * cards thrown at once carry the same idx and the second start is a no-op.
 *
 * Node-import-safe: the DOM lives in ui/duel/duelView.js, injected.
 * ==========================================================================*/

import { GoonMatchPhase } from '../../core/contracts.js';
import { createBoard, openingSpawn, play, deepest } from './board.js';
import {
  DUEL_INTRO_MS, DUEL_REPORT_GRACE_MS, bonusFor, cardEligible, duelOutcome, duelSeed, pickLength,
} from './rules.js';
import { getDuelLength } from '../screens/customize.js';
import { finishedMatches } from '../nightProgress.js';

const HINT_KEY = 'goon.night.cardHint.v1';
const RESULT_HOLD_MS = 2600;

/* ---- the recap's one line: survives the HUD being torn down at Recap ---- */
let summary = { won: 0, lost: 0, tied: 0 };
/** Duel tally of the current / last match. Read by ui/screens/recap.js. */
export function duelSummary() { return Object.assign({}, summary); }

/** First game card ever: true exactly once per device. */
export function takeFirstCardHint() {
  try {
    if (typeof localStorage === 'undefined') return false;
    if (localStorage.getItem(HINT_KEY)) return false;
    localStorage.setItem(HINT_KEY, '1');
    return true;
  } catch (_e) { return false; }
}

/**
 * @param {object} o
 * @param {object} o.match     GoonMatchService
 * @param {object} [o.view]    ui/duel/duelView.js handle (null = headless)
 * @param {object} [o.audio]
 * @param {Function} [o.onLog]
 * @param {Function} [o.now]   ms clock (tests)
 * @param {Function} [o.later] (fn, ms) => cancel (tests)
 * @param {Function} [o.finished] finished-match count (tests)
 * @param {Function} [o.duelLength] this player's Customize pick (tests)
 */
export function createDuelController({
  match, view = null, audio = null, onLog = null,
  now = () => Date.now(),
  later = (fn, ms) => { const t = setTimeout(fn, ms); return () => clearTimeout(t); },
  finished = finishedMatches,
  duelLength = getDuelLength,
} = {}) {
  let nextIdx = 0;
  let hostLen = null;        // guest: the host's cfg, once it arrives
  let cur = null;            // {idx, len, board, mine, stage, cancels[]}
  const peerScores = new Map();
  const unsubs = [];
  summary = { won: 0, lost: 0, tied: 0 };

  const log = (e) => { try { if (typeof onLog === 'function') onLog(e); } catch (_e) { /* never */ } };
  const sfx = (id) => { try { if (audio && typeof audio.sfx === 'function') audio.sfx(id); } catch (_e) { /* stub */ } };
  const v = (name, ...args) => { try { if (view && typeof view[name] === 'function') view[name](...args); } catch (_e) { /* the view is never load-bearing */ } };

  function isLive() { return !!match && match.phase === GoonMatchPhase.Live; }
  function busy() { return !!cur; }
  function myLen() {
    if (match && match.isHost) return pickLength(duelLength());
    return pickLength(hostLen || (match && match.peerDuelLen));
  }

  function at(ms, fn) {
    const cancel = later(() => { if (cur) fn(); }, ms);
    if (cur) cur.cancels.push(cancel);
  }

  function begin(idx, len, by) {
    cur = { idx, len: pickLength(len), by, board: null, mine: null, stage: 'intro', cancels: [], endsAt: 0 };
    nextIdx = Math.max(nextIdx, idx + 1);
    const seed = duelSeed(match && match.matchSeed, idx);
    cur.board = createBoard(seed);
    openingSpawn(cur.board);
    sfx('gg-fire');
    v('intro', { by, len: cur.len, hint: takeFirstCardHint() });
    log({ t: 'duel-start', idx, len: cur.len, by });
    at(DUEL_INTRO_MS, () => {
      cur.stage = 'play';
      cur.endsAt = now() + cur.len * 1000;
      v('board', cur.board, { secondsLeft: cur.len });
      tick();
    });
  }

  function tick() {
    if (!cur || cur.stage !== 'play') return;
    const left = Math.ceil((cur.endsAt - now()) / 1000);
    if (left <= 0) { endPlay(); return; }
    v('timer', left);
    at(250, tick);
  }

  /** One swipe / arrow. Returns the move result, or null when no board is live. */
  function input(dir) {
    if (!cur || cur.stage !== 'play') return null;
    const res = play(cur.board, dir);
    if (res.moved) v('board', cur.board, { merges: res.merges });
    return res;
  }

  function endPlay() {
    if (!cur || cur.stage !== 'play') return;
    cur.stage = 'wait';
    cur.mine = { tile: deepest(cur.board), score: cur.board.score };
    if (match) match.sendDuel({ sub: 'score', idx: cur.idx, score: cur.mine.score, tile: cur.mine.tile });
    v('waiting');
    if (peerScores.has(cur.idx)) { resolve(); return; }
    at(DUEL_REPORT_GRACE_MS, resolve);
  }

  function resolve() {
    if (!cur || cur.stage !== 'wait') return;
    cur.stage = 'result';
    const theirs = peerScores.get(cur.idx) || null;
    const outcome = duelOutcome(cur.mine, theirs);
    const bonus = bonusFor(outcome);
    if (bonus > 0 && match && match.scoring && typeof match.scoring.awardBonus === 'function') match.scoring.awardBonus(bonus);
    if (outcome === 'win') summary.won++; else if (outcome === 'lose') summary.lost++; else summary.tied++;
    sfx(outcome === 'win' ? 'gg-endured' : 'gg-drop-dud');
    v('result', { outcome, bonus, mine: cur.mine, theirs: theirs || { tile: 0, score: 0 } });
    log({ t: 'duel-end', idx: cur.idx, outcome, bonus });
    at(RESULT_HOLD_MS, close);
  }

  function close() {
    if (!cur) return;
    for (const c of cur.cancels) { try { c(); } catch (_e) { /* gone */ } }
    peerScores.delete(cur.idx);
    cur = null;
    v('close');
  }

  function onFrame(f) {
    if (!f) return;
    if (f.sub === 'cfg') { if (match && !match.isHost) hostLen = pickLength(f.len_s); return; }
    if (f.sub === 'score') {
      peerScores.set(f.idx, { tile: f.tile, score: f.score });
      if (cur && cur.idx === f.idx && cur.stage === 'wait') resolve();
      return;
    }
    if (f.sub === 'start') {
      if (!isLive()) return;
      if (cur) return;            // same idx (a simultaneous throw) or a busy board: one duel at a time
      begin(f.idx, f.len_s, 'them');
    }
  }

  function onPhase(p) {
    if (p === GoonMatchPhase.Live) {
      if (match && match.isHost) match.sendDuel({ sub: 'cfg', len_s: myLen() });
      return;
    }
    // Leaving Live (mercy, sudden death, recap, teardown) ends a running duel. No bonus.
    if (cur) { log({ t: 'duel-abort', idx: cur.idx }); close(); }
  }

  if (match && typeof match.onDuelFrame === 'function') unsubs.push(match.onDuelFrame(onFrame));
  if (match && typeof match.onPhaseChanged === 'function') unsubs.push(match.onPhaseChanged(onPhase));
  if (isLive()) onPhase(GoonMatchPhase.Live);
  v('bind', { input });

  return {
    busy,
    input,
    /** The arsenal's seam for the game card slot. */
    arsenalHook: {
      eligible(held) {
        return cardEligible({
          peerNight: !!(match && match.peerSupportsNight),
          finished: finished(),
          inDuel: busy(),
          held,
        });
      },
      /** Show the slot at all: the peer speaks night and it is the player's second match. */
      visible() { return !!(match && match.peerSupportsNight) && finished() >= 1; },
      busy,
      throwCard() {
        if (busy() || !isLive()) return false;
        const idx = nextIdx;
        const len = myLen();
        if (!match || !match.sendDuel({ sub: 'start', idx, len_s: len })) return false;
        begin(idx, len, 'you');
        return true;
      },
      firstHint: () => takeFirstCardHint(),
    },
    /** Test seam. */
    get state() { return cur ? { idx: cur.idx, len: cur.len, stage: cur.stage, by: cur.by } : null; },
    get board() { return cur ? cur.board : null; },
    endNow() { endPlay(); },
    dispose() {
      close();
      for (const u of unsubs) { try { if (typeof u === 'function') u(); } catch (_e) { /* gone */ } }
      unsubs.length = 0;
      v('dispose');
    },
  };
}

export default createDuelController;
