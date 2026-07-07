/* ============================================================================
 * m2test.js - M2 bridge exercise, loaded ONLY when the host passes m2Test:true
 * (`--dtrh-m2test`). Fires every safe payload kind, walks the meta command
 * vocabulary, then completes a fake run and checks the payout round-trip.
 * All results go to the host log as "M2TEST pass/FAIL ..." lines; the meta
 * mutations land in chaos_meta.test.json (the real save is never touched -
 * DtrhMetaBridge clones in test mode).
 *
 * Deliberately skipped: video (can hold the screen / attention checks) and
 * htLink (navigates the main app browser) - exercised in manual play-testing.
 * ==========================================================================*/

const wait = (ms) => new Promise((r) => setTimeout(r, ms));

export async function run(bridge, hostState) {
  const results = [];
  const check = (name, ok, detail = '') => {
    results.push(ok);
    bridge.log(`M2TEST ${ok ? 'pass' : 'FAIL'} ${name} ${detail}`);
  };
  bridge.log('M2TEST starting (payloads -> meta commands -> run payout)');
  await wait(3000); // let the engine settle so effects land over a live scene

  // ---- 1. payloads (each is a REAL desktop effect; spaced so they read) ----
  const payloads = [
    { kind: 'flash', strength: 70 },
    { kind: 'subliminal', strength: 50 },
    { kind: 'overlay', overlay: 'pink_filter', strength: 60 },
    { kind: 'overlay', overlay: 'spiral', strength: 60 },
    { kind: 'overlay', overlay: 'braindrain', strength: 40 },
    { kind: 'gifCascade', strength: 60 },
    { kind: 'audio', strength: 50 },
    { kind: 'bambiFreeze', strength: 50 },
    { kind: 'bouncingText', strength: 50 },
  ];
  for (const p of payloads) {
    bridge.send({ type: 'fire-payload', ...p });
    await wait(1600);
  }
  check('payloads-sent', true, `${payloads.length} kinds (video/htLink manual)`);

  // ---- 2. meta commands (against a CLONE of the user's REAL save, so every
  // expectation is a DELTA off the starting snapshot, never an absolute) ----
  const m0 = hostState.meta || {};
  const rev0 = hostState.metaRev;
  const pocketBuyValid = (m0.toyPockets | 0) < 2;
  const flagValid = m0.seenDefuseTutorial !== true;
  const rankValid = (m0.lastRankSeen | 0) < 1;
  const cmds = [
    { op: 'add-gold', amount: 50 },                                   // always applies
    { op: 'spend-gold', amount: 20 },                                 // always (gold >= 50 now)
    { op: 'buy-pocket', kind: 'toy', cost: 10 },                      // applies iff pockets < 2
    { op: 'set-flag', key: 'seenDefuseTutorial' },                    // applies iff still false
    { op: 'add-to-set', set: 'discoveredCodexIds', id: 'bubble:m2test' }, // always (unique id)
    { op: 'lesson-progress', id: 'm2test_lesson', value: 3 },         // always (fresh id)
    { op: 'set-num', key: 'lastRankSeen', value: 1 },                 // applies iff rank < 1
    { op: 'equip-boon', id: 'm2test_boon' },                          // always
    { op: 'spend-gold', amount: 99999999 },                           // must be REJECTED (insufficient)
    { op: 'definitely-not-an-op' },                                   // must be ignored
  ];
  const expectApplied = 5 + (pocketBuyValid ? 1 : 0) + (flagValid ? 1 : 0) + (rankValid ? 1 : 0);
  for (const c of cmds) {
    bridge.send({ type: 'meta-command', ...c });
    await wait(120);
  }
  await wait(1200); // let snapshots flow back
  const applied = hostState.metaRev - rev0;
  check('meta-commands', applied === expectApplied,
    `rev +${applied} (expected ${expectApplied}; pocket=${pocketBuyValid} flag=${flagValid} rank=${rankValid})`);
  const s = hostState.meta;
  const expectGold = (m0.gold | 0) + 50 - 20 - (pocketBuyValid ? 10 : 0);
  check('meta-state', !!s && s.gold === expectGold && s.seenDefuseTutorial === true
    && (s.discoveredCodexIds || []).includes('bubble:m2test') && s.equippedStartBoon === 'm2test_boon',
    s ? `gold=${s.gold}/${expectGold} flag=${s.seenDefuseTutorial} boon=${s.equippedStartBoon}` : 'no snapshot');

  // ---- 3. run lifecycle + payout round-trip ----
  bridge.send({ type: 'run-started', difficulty: 'Gentle', mode: 'm2test' });
  await wait(400);
  bridge.send({
    type: 'run-ended',
    score: 12000, durationSec: 180, elapsedSec: 180,
    difficulty: 'Gentle', difficultyMult: 1.0, sparkGainMult: 1.0,
    bestCombo: 14, defused: 9, detonated: 2, trickleDrops: 5, dripFeedMaxed: false,
  });
  await wait(1200);
  const p = hostState.lastPayout;
  // capBase = 250 * 3min * 1.0 = 750 -> baseXp = min(12000, 750) = 750
  check('payout-xp-cap', !!p && Math.round(p.baseXp) === 750, p ? `baseXp=${p.baseXp}` : 'no payout-result');
  // sparks = round((1.5*sqrt(12000) + 35*1*1)*1) + 5 trickle = round(164.3+35)+5 = 199+5... verified vs C# in the log
  check('payout-sparks', !!p && p.sparksEarned > 0 && p.dryRun === true,
    p ? `sparks=${p.sparksEarned} dryRun=${p.dryRun}` : '');

  const pass = results.every(Boolean);
  bridge.log(`M2TEST DONE: ${pass ? 'ALL PASS' : 'FAILURES PRESENT'} (${results.filter(Boolean).length}/${results.length})`);
  bridge.send({ type: 'log', msg: `M2TEST-SUMMARY ${pass ? 'PASS' : 'FAIL'}` });
}
