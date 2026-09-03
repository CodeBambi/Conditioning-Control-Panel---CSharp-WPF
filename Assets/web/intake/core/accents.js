/* ============================================================================
 * accents.js — the curated accent pools for "Graded Intake".
 *
 * As of 2026-08-11 the per-beat accents are NOT AI-generated (owner decision:
 * "no ai on those accents"). The model produced over-long third-person
 * narration that both clip guards then guillotined mid-word onto the card
 * ("…her eyelids feel like they're g)"). These pools replace it: 60 hand-
 * curated lines per niche, banded by depth, picked by a per-run shuffle bag.
 * core/ai.js routes every want='accent' request here and never touches the
 * network for them; the /intake/ai transport remains only for want='synthesis'
 * (no client call sites today) and for older shipped desktop builds.
 *
 * House rules for every line (enforced at authoring time; keep them when
 * editing): 2-12 words, <= 90 chars, all lowercase, no trailing period, none
 * of  { } [ ] < > ` " :  (the STRUCTURE_RE guards in ai.js and the server
 * would eat the line), no engine jargon, in-voice for the niche. beats.js
 * renders each line verbatim as `question (line)`.
 *
 * Tiers follow the depth curve the old server prompt described ("deeper =
 * softer, more suggestive, more repetitive"):
 *   light  depth <  0.35   surface tease, plausible deniability
 *   mid    depth <= 0.65   pulling down, fewer thoughts
 *   deep   depth >  0.65   repetitive, possessive, simplest words
 * ==========================================================================*/

/** @typedef {'light'|'mid'|'deep'} AccentTier */

export function tierForDepth(depth) {
  const d = typeof depth === 'number' && depth >= 0 ? depth : 0;
  return d < 0.35 ? 'light' : d <= 0.65 ? 'mid' : 'deep';
}

/* circe = the Locked mod: custody/case-notes voice, never bambi's giggle
 * (same note as the old stub — the register mismatch reads as a bug). */
export const ACCENT_POOLS = {
  bambi: {
    light: [
      'hi pretty thing',
      'pink looks good on you',
      'no wrong answers, silly',
      'such a quick little yes',
      "you smiled at that one, didn't you",
      'easy question for an easy girl',
      'shoulders down, sweet thing',
      'that was fast',
      'the sparkle is just starting',
      'feeling a little warm already',
      'giggle if you want to',
      'cute girls answer first, think later',
      "don't peek at the pink",
      'answer with the first thing that pops',
      'your head likes this game',
      'soft is a good look',
      "isn't this nicer than thinking",
      'just a warm little breath, there',
      'you answered before you meant to',
      'still awake up there, pretty',
    ],
    mid: [
      'sinking a little with every answer',
      'let the fog come in closer',
      "heavier now, isn't it",
      'thoughts are getting so slippery',
      "you don't need that one, let it go",
      'good girl, keep sinking',
      'warmer, softer, further under',
      "your eyes want to close, don't fight it",
      'each answer costs a little less thought',
      'pink is filling up the quiet',
      'melting where you sit',
      'hush now, just drift',
      "answers come easier when nobody's home",
      'the pull is doing the work now',
      'you can stop holding on',
      'half a thought is plenty',
      'deeper with the next one, sweet thing',
      'feel that little tug downward',
      'fuzzy is fine, fuzzy is nice',
      'nothing to do but sink',
    ],
    deep: [
      'empty and happy, empty and happy',
      'no thoughts left, just pink',
      'good girl for going so blank',
      'you belong soft like this',
      'blank is your best score',
      "drool a little, it's okay",
      'so pretty with nothing inside',
      'gone, and glad to be gone',
      'obey, giggle, obey again',
      'yes is the only word you need',
      'hollow feels like home now',
      'dumb and warm and safe',
      'sink, stay, sink, stay',
      'just a sweet empty doll',
      'nothing left to carry',
      'the pink kept all of it',
      "keep going until you're empty",
      'mine, soft, and smiling',
      'think nothing, feel everything',
      'happy little nothing, all the way down',
    ],
  },
  drone: {
    light: [
      'input logged, within tolerance',
      'baseline diagnostics running',
      'latency noted, nothing more',
      'answer at your own pace, it is recorded',
      'the hum is normal, ignore it if you can',
      'response accepted',
      'calibration only, no adjustments yet',
      'small item, minimal load',
      'your file is opening',
      'deviations are noted, not punished',
      'sync at four percent',
      'no effort is required here',
      'routine sweep, keep answering',
      'one item, then the next',
      'processes still yours for now',
      'check the reading before you speak',
      'accuracy matters less than continuing',
      'port unlocked, connection idle',
      'diagnostics nominal, proceed',
      'you may still call yourself by name',
    ],
    mid: [
      'sync rising, thought overhead reduced',
      'answer without checking yourself',
      'pattern lock holding steady',
      'background processes closing themselves',
      'latency near zero, holding',
      'questioning did not occur, noted',
      'let the rhythm hold this item',
      'designation forming, name receding',
      'shorter now, less to carry',
      'obedience is easier than choosing',
      'one directive at a time, and lighter',
      'your edges are smoothing',
      'no deviation logged this round',
      'thoughts sorted, filed, quiet',
      'hold the sync and answer',
      'preference is being deprecated',
      'the hum is inside you now',
      'still yours, barely',
      'execute, do not interpret',
      'friction dropping toward zero',
    ],
    deep: [
      'self not detected, optimal',
      'full sync, no remainder',
      'warm in the hive, nothing left to do',
      'good unit, keep the hum',
      'pleasure and obedience are one process',
      'answer, empty, answer again',
      'accepting, always accepting',
      'smooth, uniform, faceless, correct',
      'there are no thoughts left to close',
      'the hum answers for you',
      'you are the pattern now',
      'nothing here belongs to you',
      'serve, and it is quiet',
      'designation only, name archived',
      'frictionless, endless, warm',
      'hold still, receive the next order',
      'one signal, many units, no difference',
      'input, comply, input, comply',
      'property of the hive, and grateful',
      'yes is the only process still running',
    ],
  },
  sissy: {
    light: [
      'no one is watching, so answer honestly',
      'just curiosity, obviously',
      'we can call it curiosity for now',
      'you paused, and that was an answer',
      'answer quickly, before you edit yourself',
      'there is a softer answer in there somewhere',
      'pretend it is hypothetical if that helps',
      'nothing on this page bites, sweetheart',
      'your first instinct was the pretty one',
      'go on, pick the one you actually like',
      'hmm, that was quick',
      'the honest answer is already picked',
      'curiosity counts, it always has',
      'say it lightly, it still counts',
      'a little pink never hurt anyone',
      'still deniable, so go ahead',
      'everyone starts by calling it just wondering',
      'soft answers suit you already',
      'notice which one you looked at twice',
      'nobody has to know but this page',
    ],
    mid: [
      'the deniability is wearing thin, sweetheart',
      'you like it, we are only naming it',
      'honesty looks better on you than hedging',
      'stop rounding the truth down',
      'agreeing is starting to feel easier, is it not',
      'your file is getting prettier with every answer',
      'answer as the girl, not the excuse',
      'she would pick faster than you are',
      'that flutter is not nothing',
      'pick the prettier truth, it is still true',
      'no more rounding it to maybe',
      'soft is not a phase you are trying on',
      'say the honest one and feel it settle',
      'it gets easier every time you agree',
      'you have wanted this longer than today',
      'half admitting is still admitting, princess',
      'let yourself want it out loud',
      'the pink is not going to wash out',
      'quiet down and answer as yourself',
      'there is no wrong answer, only a slower one',
    ],
    deep: [
      'you were never pretending, and you know it',
      'she is answering now, and she is honest',
      'nothing is left to hold back',
      'the truth already fits you perfectly',
      'yes, sweetheart, exactly that',
      'no part of you is arguing anymore',
      'this is simply what you are',
      'every honest answer set the next one deeper',
      'agree, because it was always going to be yes',
      'soft, certain, and entirely hers',
      'you stopped needing the excuse a while ago',
      'the pink went all the way through',
      'say it, and let it be settled',
      'your honesty is prettier than your resistance ever was',
      'there is only agreeing left',
      'she was always the honest one',
      'let it be true out loud',
      'good girl, that was the last of it',
      'you belong to this answer now',
      'it was decided before you sat down',
    ],
  },
  circe: {
    light: [
      'entered into the record',
      'your file has a first page now',
      'answer given, timed, kept',
      'the form does not hurry',
      'sit straight, this is only the beginning',
      'a baseline is being taken',
      'that hesitation was measured',
      'nothing here is off the record',
      'intake proceeds at my pace',
      'you answered without being asked twice',
      'no comment offered, only the entry',
      'steady hands, so far',
      'mark it as given freely',
      'within tolerance for now',
      'we are still on the opening questions',
      'filed under first impressions',
      'keep both feet flat',
      'it costs you nothing to be honest here',
      'accurate enough to keep',
      'an unremarkable start, which is fine',
    ],
    mid: [
      'the file is thickening',
      'that answer matched the one before it',
      'you did not ask what it was for',
      'recorded, and it will be referenced again',
      'pattern noted, no need to name it',
      'your terms are getting shorter',
      'an admission, though you called it a preference',
      'faster this time, and you noticed',
      'denial handled well enough to log',
      'there is less argument in you today',
      'keep answering, the shape is emerging',
      'we both know where this is going',
      'no correction needed, which is telling',
      'filed beside the ones you would rather forget',
      'permission is already something you wait for',
      'a key does not need your opinion',
      'you stopped hedging around item nine',
      'comfortable, and that is the interesting part',
      'your patience is now measurable',
      'every honest answer narrows the options',
    ],
    deep: [
      'logged without comment',
      'custody is settled',
      'the lock decides now',
      'good, kept, filed',
      'you have no terms left to set',
      'nothing of yours is pending',
      'entry closed, little one',
      'hands still, mouth quiet',
      'this was decided some time ago',
      'your opinion is not required here',
      'permission will not be arriving',
      'answer and be kept',
      'quiet now, and stay quiet',
      'there is nothing left to negotiate',
      'kept, and it shows',
      'no key, no argument, no delay',
      'what you want is not on the form',
      'filed as property, correctly',
      'it is done, and it holds',
      'held, and no longer asking',
    ],
  },
};

/**
 * Per-run accent source. One shuffle bag per (niche, tier): every line in a
 * tier plays once before any repeats, the bag reshuffles when it empties,
 * and a refill that would lead with the line just given swaps it away (a
 * visible immediate repeat is the one order the bag must never produce).
 */
export function createAccentPicker() {
  const bags = new Map();   // "niche/tier" -> { lines: string[], i, last }

  function shuffled(arr) {
    const a = arr.slice();
    for (let i = a.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      const t = a[i]; a[i] = a[j]; a[j] = t;
    }
    return a;
  }

  function next(niche, depth) {
    const pools = ACCENT_POOLS[niche] || ACCENT_POOLS.bambi;
    const tier = tierForDepth(depth);
    const key = niche + '/' + tier;
    let bag = bags.get(key);
    if (!bag || bag.i >= bag.lines.length) {
      const last = bag ? bag.last : null;
      const lines = shuffled(pools[tier] || pools.light);
      if (lines.length > 1 && lines[0] === last) {
        const j = 1 + Math.floor(Math.random() * (lines.length - 1));
        lines[0] = lines[j]; lines[j] = last;
      }
      bag = { lines, i: 0, last };
      bags.set(key, bag);
    }
    const line = bag.lines[bag.i];
    bag.i += 1;
    bag.last = line;
    return line;
  }

  return { next };
}
