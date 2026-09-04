/* ============================================================================
 * backroom/kit/voice.js - the dealer.
 *
 * E.M.I. works the floor down here. She is not the pit boss and she is not on
 * the house's side: she is the one person in the room who is pleased to see
 * you, and she says so whether the cabinet paid or not.
 *
 * FOUR RULES, and they are the difference between a dealer and a slot machine
 * with subtitles.
 *  - SYMPATHY ON LOSSES, never a jab. The house took the chips; the dealer does
 *    not also take a swing. A loss line is warm, short, and already looking at
 *    the next hand.
 *  - NEVER PROMISE THE NEXT ONE. No "you are due", no "it has to hit soon".
 *    That is the one sentence a room like this must not say out loud, and it is
 *    a lie about how the odds work besides.
 *  - DIEGETIC. She is a person at a table in a back room, not an assistant with
 *    a notification. No system voice, no em-dashes, no exclamation stacks.
 *  - THROUGH THE LEXICON. Every line is a KEY (Law VII: the accent comes from
 *    the lexicon, never from AI), so a mod re-voices the whole dealer by
 *    shipping `lexicon.json` rows. Nothing here reads modId; nothing may.
 *
 * The values are mirrored key-for-key into ArcademyHostService.NeutralLexicon.
 * Every one is under 96 characters, because a longer row can never be skinned.
 * ==========================================================================*/

/** Every line the dealer has, by bucket. Buckets are picked, never cycled, so
 *  two hands in a row can repeat - a dealer with a strict rotation reads like a
 *  tape, and a dealer who occasionally says the same thing twice reads like a
 *  person who has been on shift a while. */
export const BK_VOICE = Object.freeze({
  /* ---- dealing, before anything is known ---- */
  bk_say_deal_1: 'Chips down. Let us see what the night thinks of you.',
  bk_say_deal_2: 'Good. Hands where I can see them, eyes where I put them.',
  bk_say_deal_3: 'Here we go, sweetheart.',
  bk_say_deal_4: 'One more. You always did say one more.',
  bk_say_deal_5: 'Settle in. This part is my favourite.',

  /* ---- a win, ordinary size ---- */
  bk_say_win_1: 'There it is. Well played.',
  bk_say_win_2: 'The house nods and pays. Rare thing, that.',
  bk_say_win_3: 'Look at you. Colour me impressed.',
  bk_say_win_4: 'Yours. Take it before I change my mind.',
  bk_say_win_5: 'Nice. Very nice.',

  /* ---- a loss. WARM. Never a jab, never a promise about the next one. ---- */
  bk_say_loss_1: 'Not that one. Shake it off, love.',
  bk_say_loss_2: 'The floor keeps that one. Happens to everybody here.',
  bk_say_loss_3: 'Ah. So close it almost counts, which it does not.',
  bk_say_loss_4: 'Gone. You are still the best thing in this room.',
  bk_say_loss_5: 'That is the game. No shame in it.',
  bk_say_loss_6: 'The house takes it. I would rather it had not.',

  /* ---- a big win, worth stopping the table for ---- */
  bk_say_big_1: 'Oh, that is a NUMBER. Say it out loud for me.',
  bk_say_big_2: 'The whole room just looked over. Let them look.',
  bk_say_big_3: 'That is the good stuff. Breathe, then count it.',
  bk_say_big_4: 'Well now. Somebody is having a night.',
  bk_say_big_5: 'I felt that one land. Beautiful.',

  /* ---- THE DROP: the wedge that dims the room ---- */
  bk_say_drop_1: 'Ah. The room wants you for a second.',
  bk_say_drop_2: 'Lights down, pet. This bit is not about the chips.',
  bk_say_drop_3: 'There. Sink for me.',
  bk_say_drop_4: 'The spiral called your name. It does that.',
  bk_say_drop_5: 'Eyes here. Just here. Good.',

  /* ---- the royal, the once-a-season thing that takes the pot ---- */
  bk_say_royal_1: 'The pot. The whole pot. I have never seen it go.',
  bk_say_royal_2: 'That is a royal. Sit down before you fall down.',
  bk_say_royal_3: 'Every light in the room is on you. Earned.',
  bk_say_royal_4: 'They will talk about this hand for a while.',

  /* ---- the cage ---- */
  bk_say_cage_1: 'Sparkle in, chips out. One way only, you know the rule.',
  bk_say_cage_2: 'Counting it out for you now.',
  bk_say_cage_3: 'There. Try to make it last past the first cabinet.',
  bk_say_cage_4: 'Careful with that. It spends faster than it earns.',
  bk_say_cage_5: 'Spend it slowly. Or do not. I am not your mother.',
});

/** The buckets, and how many lines each one actually has. Kept as data so a
 *  new line is one row above and nothing else. */
const BUCKETS = Object.freeze({
  deal: 5, win: 5, loss: 6, big: 5, drop: 5, royal: 4, cage: 5,
});

/**
 * createVoice({ t, rng, log }) -> { line(bucket), say(bucket), buckets }
 *
 * `t` is the SHELL's `t(key, fallback)`, not the floor's `bkT`: these lines
 * carry no slots to fill, so they want the plain two-argument resolver and its
 * mod-row-then-English fallback chain. `rng` is core/rng.js's `makeRng` output (a 0..1 function) if
 * the caller has a seeded one; otherwise Math.random, because a dealer's small
 * talk is the one thing on this page that does NOT need to be reproducible.
 */
export function createVoice(opts) {
  const o = opts || {};
  const t = (typeof o.t === 'function') ? o.t : ((k) => BK_VOICE[k] || '');
  const rand = (typeof o.rng === 'function') ? o.rng : Math.random;
  let lastKey = '';

  /** One line from a bucket. Never the same key twice running, which is the
   *  only anti-repeat this needs: the bucket is small and the ear is patient. */
  function line(bucket) {
    const n = BUCKETS[bucket] || 0;
    if (!n) return '';
    let key = '';
    for (let tries = 0; tries < 4; tries++) {
      const i = 1 + Math.floor(Math.max(0, Math.min(0.999999, Number(rand()) || 0)) * n);
      key = 'bk_say_' + bucket + '_' + i;
      if (key !== lastKey) break;
    }
    lastKey = key;
    const en = BK_VOICE[key];
    return t(key, en) || en || '';
  }

  return {
    line,
    buckets: Object.keys(BUCKETS),
    /** The table, so a caller can mirror or diff it without importing twice. */
    table: BK_VOICE,
  };
}

export default createVoice;
