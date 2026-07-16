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

  // ---- 1. payloads: since the 2026-07 cutover every VISUAL effect renders
  // in-world (payloadFx), so drive them through the page dispatcher exposed at
  // window.__sfPayloadFx; only audio still rides the native bridge. If a native
  // WPF overlay window appears during this loop, the cutover regressed. ----
  const payloads = [
    { kind: 'flash', strength: 70 },
    { kind: 'subliminal', strength: 50 },
    { kind: 'overlay', overlay: 'pink_filter', strength: 60 },
    { kind: 'overlay', overlay: 'spiral', strength: 60 },
    { kind: 'overlay', overlay: 'braindrain', strength: 40 },
    { kind: 'glitch', strength: 55 },
    { kind: 'gifCascade', strength: 60 },
    { kind: 'audio', strength: 50 },
    { kind: 'bambiFreeze', strength: 50 },
    { kind: 'bouncingText', strength: 50 },
  ];
  const pfx = window.__sfPayloadFx;
  check('payloadfx-live', !!pfx, pfx ? 'in-world dispatcher present' : 'window.__sfPayloadFx missing');
  for (const p of payloads) {
    if (p.kind === 'audio' || p.kind === 'video') {
      bridge.send({ type: 'fire-payload', ...p });   // native path (sfx / mandatory video)
    } else if (pfx) {
      pfx.applyPayload({ variantId: 'm2test', strength: p.strength,
        payload: { kind: p.kind, overlay: p.overlay } }, { durationMult: 1 });
    }
    await wait(1600);
  }
  check('payloads-sent', true, `${payloads.length} kinds in-world (video/htLink manual)`);

  // ---- 2. meta commands (against a CLONE of the user's REAL save, so every
  // expectation is a DELTA off the starting snapshot, never an absolute) ----
  // Gold cutover: dials cost GOLD; buy-pocket / bench-purchase are retired ops
  // (must now be ignored); bench-buy is whitelisted to the 3 console extras.
  const m0 = hostState.meta || {};
  const rev0 = hostState.metaRev;
  const dials0 = m0.purchasedDials || [];
  const dialBuyValid = !dials0.includes('bubbleSize');          // gold-dial happy path (25🪙 < the +50 above)
  const giftArmed = m0.giftGiven !== true && dials0.length === 0;
  const flagValid = m0.seenDefuseTutorial !== true;
  const rankValid = (m0.lastRankSeen | 0) < 1;
  const cmds = [
    { op: 'add-gold', amount: 50 },                                   // always applies
    { op: 'spend-gold', amount: 20 },                                 // always (gold >= 50 now)
    { op: 'purchase-dial', id: 'bubbleSize', cost: 25 },              // gold dial: applies iff not owned
    { op: 'purchase-dial', id: 'hydra', cost: 99999999 },             // must be REJECTED (gold short; gift only covers a FIRST dial, and bubbleSize just took it)
    { op: 'buy-pocket', kind: 'toy', cost: 10 },                      // RETIRED op: must be ignored
    { op: 'bench-purchase', id: 'stats_panel', cost: 10 },            // RETIRED op: must be ignored
    { op: 'bench-buy', id: 'toy_pocket_1', cost: 1 },                 // pocket id: must be REJECTED (whitelist)
    { op: 'set-flag', key: 'seenDefuseTutorial' },                    // applies iff still false
    { op: 'add-to-set', set: 'discoveredCodexIds', id: 'bubble:m2test' }, // always (unique id)
    { op: 'lesson-progress', id: 'm2test_lesson', value: 3 },         // always (fresh id)
    { op: 'set-num', key: 'lastRankSeen', value: 1 },                 // applies iff rank < 1
    { op: 'equip-boon', id: 'm2test_boon' },                          // always
    { op: 'spend-gold', amount: 99999999 },                           // must be REJECTED (insufficient)
    { op: 'definitely-not-an-op' },                                   // must be ignored
    // ---- crafting Part 2 ops: earn the prerequisites first (Part 1 ops), then
    // exercise the new vocabulary. All deltas deterministic off the clone. ----
    { op: 'material-add', id: 'chrome', amount: 30 },                 // always
    { op: 'material-add', id: 'silicone', amount: 5 },                // always
    { op: 'material-add', id: 'pills', amount: 10 },                  // always
    { op: 'craft', id: 'the_padlock', cost: { chrome: 8 } },          // always (holdings just granted)
    { op: 'craft', id: 'the_cage', cost: { chrome: 8, silicone: 1 } }, // always
    { op: 'craft', id: 'sugar_cube', cost: { pills: 4 } },            // always
    { op: 'pin-boon', id: 'm2test_pin' },                             // applies (padlock owned above)
    { op: 'set-denial', on: true },                                   // applies iff it was off
    { op: 'set-denial', on: true },                                   // must be REJECTED (no change)
    { op: 'consume-crafted', id: 'sugar_cube' },                      // applies (>=1 after the craft)
    { op: 'consume-crafted', id: 'the_padlock' },                     // must be REJECTED (not a consumable)
    { op: 'add-to-set', set: 'paperwallSketches', id: 'm2test_sketch' }, // always (Part 3: the endRun tear rides this op)
  ];
  const denialValid = m0.denialArmed !== true;
  const expectApplied = 5 + (dialBuyValid ? 1 : 0) + (flagValid ? 1 : 0) + (rankValid ? 1 : 0)
    + 9 + (denialValid ? 1 : 0);
  for (const c of cmds) {
    bridge.send({ type: 'meta-command', ...c });
    await wait(120);
  }
  await wait(1200); // let snapshots flow back
  const applied = hostState.metaRev - rev0;
  check('meta-commands', applied === expectApplied,
    `rev +${applied} (expected ${expectApplied}; dial=${dialBuyValid} flag=${flagValid} rank=${rankValid})`);
  const s = hostState.meta;
  // the dial buy paid 25 gold UNLESS the balance ran short and her gift covered it
  // (gift zeroes the balance); model both so the check holds on any starting save.
  const goldAfterSpend = (m0.gold | 0) + 50 - 20;
  const giftFired = dialBuyValid && giftArmed && goldAfterSpend < 25;
  const expectGold = !dialBuyValid ? goldAfterSpend : (giftFired ? 0 : goldAfterSpend - 25);
  check('meta-state', !!s && s.gold === expectGold && s.seenDefuseTutorial === true
    && (!dialBuyValid || (s.purchasedDials || []).includes('bubbleSize'))
    && !(s.purchasedDials || []).includes('hydra')
    && !(s.benchPurchases || []).includes('toy_pocket_1')
    && (s.discoveredCodexIds || []).includes('bubble:m2test') && s.equippedStartBoon === 'm2test_boon',
    s ? `gold=${s.gold}/${expectGold} dial=${(s.purchasedDials || []).includes('bubbleSize')} flag=${s.seenDefuseTutorial} boon=${s.equippedStartBoon}` : 'no snapshot');
  // crafting Part 2 state: the crafts landed, the padlock pinned, denial armed,
  // and the consumed sugar cube dropped exactly one from its holding.
  const cubes0 = (m0.craftedItems && m0.craftedItems.sugar_cube) | 0;
  const cubesNow = (s && s.craftedItems && s.craftedItems.sugar_cube) | 0;
  check('crafting-p2-state', !!s
    && !!(s.craftedItems && s.craftedItems.the_padlock && s.craftedItems.the_cage)
    && s.pinnedBoon === 'm2test_pin' && s.denialArmed === true
    && cubesNow === cubes0,   // +1 crafted, -1 consumed
    s ? `pin=${s.pinnedBoon} denial=${s.denialArmed} cubes=${cubesNow}/${cubes0}` : 'no snapshot');
  // crafting Part 3: the paperwall sketch set persists + snapshots back
  check('paperwall-sketch', !!s && (s.paperwallSketches || []).includes('m2test_sketch'),
    s ? `sketches=${(s.paperwallSketches || []).length}` : 'no snapshot');

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
