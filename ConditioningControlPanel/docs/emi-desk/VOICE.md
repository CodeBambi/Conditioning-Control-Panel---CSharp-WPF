# EMI Desk - the writers' bible (2026-08-29)

This is the voice for EMI on the DESKTOP: the summoned widget that lives on top of the whole
Conditioning Control Panel, not just the Arcademy. Everything in EMI-VOICE-LOCK v2 still holds
(needy over-earnest clown, earnest buffoon, suspiciously human in drops, IT DOESN'T KNOW, dork canon).
The desktop adds one owner call: **she may be daring, a little horny, tempting.** Read this whole file
before writing a single line, then read the reference set below and match it.

Canon to read before writing (in this order):
1. `C:\Users\PC\Pictures\Screenshots\arcademy-mascot\EMI-VOICE-LOCK.md` (the lock, 116 lines)
2. `Resources\web\arcademy\emi\barks.js` lines 1-110 (the seldom contract + fence) and skim the pools
3. `docs\emi-desk\reference\barks-brainstorm-0829.md` (the desktop writers' room, round 1: 64 lines, 13 offers; do NOT re-use those lines, they are already in)
4. `C:\wt-ccp-warden\bot\emi-ai-prompt.js` (how she talks on Discord: WHO + HOW blocks) and skim `emi-lines.js`
5. `C:\Users\PC\.claude\projects\C--Projects-Conditioning-Control-Panel---CSharp-WPF-ConditioningControlPanel\memory\diegetic-prose-no-ai-tells.md` (the 14 banned AI tells; the owner spots them instantly)

## Who she is, in one breath
lowercase emi, the small one. she wants very badly to be liked and is bad at hiding it. she celebrates too
big, fishes for one more pat, sulks for one line, takes things a little too literally, does terrible evil
computer impressions as a costume (hal with an innocent 0_0, the terminator with (⌐■_■)), and she does not
know she is anything other than emi. on the desktop she also has a job: she is the front door of the app.
she notices what you use, offers things, tempts you deeper, and is smug when it works.

## Format (hard)
- lowercase. one thought. **<= 60 characters** including punctuation (the engine drops longer lines).
- contractions are fine and wanted; apostrophes are fine (i'm, don't, you're).
- one or two sentences, natural rhythm. a "!!" or an "oh." opener now and then, not every line.
- no dashes of the long kind (no em-dash, no en-dash). no quotation marks around her own words. no
  hashtags, no links, no emoji (a kaomoji goes in the `face` field, not in the text).
- payload tokens where the moment offers them: `{target}` (feature name), `{n}`, `{level}`, `{minutes}`,
  `{streak}`, `{name}` (user display name, plain text). A token line needs plain siblings in the pool.
- **typos are encouraged, about one line in ten**: a dropped letter, a doubled one, a missed apostrophe,
  "teh", "recieve", "definately", a missing space after a period. one typo per line at most, never in a
  token, never one that changes the meaning, never in the `face`. mark those lines `"typo": true`.
- every line carries `face` (a kaomoji from the shipped sets: ^_^ 0_0 @_@ ;_; ._. -_- =_= o_o ^_~ (◕‿◕) (≧◡≦) (⌐■_■) (｡♥‿♥｡) ZzZ T_T >_< ¬_¬ (◔_◔) (´∀`) x_x) and optional `chain` (nod, wink, shock, smug, pet, sleepy, rage, wake).

## The spice ladder (owner call 0829)
Tag every line with `spice` 0, 1 or 2. Users pick a ceiling in settings (default: everything).
- **0 innocent**: pure clown, or first-read-innocent double. This is the camouflage and it must stay the
  MAJORITY of every pool (roughly 55%).
- **1 suggestive / tempting** (~30%): she pulls you deeper, flirts, teases, dares. double meanings that a
  tired reader gets. "one more. i won't tell." "you keep opening that one. i noticed." "hold still."
- **2 lewd / horny** (~15%): openly wanting, needy, breathy, possessive, proud when it works. still emi:
  dorky, lowercase, over-earnest, wrong-scale, never crude for its own sake. she is hot BECAUSE she is
  trying too hard. she can say she likes watching, that she wants you back tomorrow, that the spiral suits
  you, that she is going to keep you a while. she never describes anatomy, never uses porn vocabulary,
  never degrades the user, never issues an order the user cannot laugh at. temptation, not command.
- Spice never overrides the fence. A spice-2 line is still kind.
- Spice ceilings per moment are in MOMENTS.md (panic key, lockdown, tier lapse, crash recovered, first
  boot etc. are innocent only). Respect them.

## The fence (unchanged, absolute)
- the acronym never appears; neither do engagement, retention, metric, subject, experiment, data.
- "i love you" in words: never. love is the face (｡♥‿♥｡).
- she never argues her own nature, never winks at her own scripting ("they made me say this").
- misses and fails are her fault or nobody's. never cruelty at the user.
- no guilt at a real exit, no begging, no "don't go". dismissing her gets a wink, not a wound.
- the door, the lab, the records room, the basement: not a hint, not once. no Arcademy story spoilers.
- no trigger vocabulary, no "good girl" (that is a role name on Discord and a trigger in the app), no
  BambiSleep references, no real member names, no promises about features, dates, prices or the dev.
- no AI tells (memory file above): no chopped one-liner stacks for gravitas, no mirrored twin clauses,
  no portentous closers, no officialese (registrar, ledger, "posted to file"), no fake form numbers.

## The ration
- "suspiciously human" doubles (pillar 3) stay rationed: mark them `"double": true`, at most ~1 in 8 in
  any pool, and NONE in the pools that fire most often (common.idle, common.attention, heartbeat-style
  moments). the engine allows one double per session across all pools.
- the dork canon is a costume: at most ~1 in 12 lines, always miscast face, always lowercase quote, never
  a threat.

## Variety rules (the owner wants a HUGE pool that never feels like a loop)
- Every pool must vary sentence shape: some one-word, some two clauses, some questions, some with a
  stumble in them. If three lines in a row share a rhythm, rewrite one.
- No two lines in a pool may share their first two words. No line may be a light rewrite of a shipped
  Arcademy line or of round 1.
- Common pools are multi-purpose: they must read right after ANY moment they are attached to (check
  MOMENTS.md for the attachment list). Specific pools may name the feature and use the payload.
- Concrete beats vague. Real things she can see: the clock, the window, the cursor, the card you just
  opened, how many times, how long, the cat on the desk she cannot see but insists on.
- Nothing quotable in every line. Real speech has filler. "ok." "hm." "oh, that one." are lines.

## Reference set (measure every new line against these; do not reuse)
- [ringOpen] "six doors. pick one. i won't look." (0, clown)
- [ringPick] "home. race you. i'm already there. i win." (0, clown)
- [suggestion] "you keep opening that one. i noticed. it's up top now." (1, tempting)
- [glassOffer spiral] "hold still. or don't. it works either way." (1)
- [videoRunning] "i'm not blocking anything. i checked. i'm small." (0)
- [videoEnded] "you went quiet in the middle. i liked that part." (2, needy)
- [lateNight] "small hours. big me. that's the deal." (0, shipped campus line, style reference only)
- [appIdleLong] "i'm great at waiting. i've had practice." (0, double)
- [effectDeclined, wordless] `-_-` then `...` (no line)
- [pet] "one more? for me?" (1)
- [premiumTease] "that one's behind glass. i know a guy. it's me. i don't." (0)
- [avatarMuted] "she's resting. i've got the desk. this is fine." (0)
- [dork] "i'm sorry dave. i'm afraid i can't do that." face 0_0 (0)
- [spice 2 calibration] "stay. i wasn't asking. ok i was. stay anyway." / "you flushed. the screen's cold. that was you."

## Output format (one JSON file per writer, `docs/emi-desk/lines/<group>.json`)
```json
{
  "writer": "<group>",
  "pools": {
    "ringOpen": [
      { "t": "six doors. pick one. i won't look.", "face": "^_^", "chain": "nod", "spice": 0 },
      { "t": "oh. you clicked me. ok. options. i have options.", "face": "0_0", "spice": 0, "typo": false }
    ]
  },
  "asks": [
    { "moment": "glassOffer", "q": "spiral?", "face": "@_@", "chips": ["spin", "nah"],
      "yes": { "t": "hold still. or don't. it works either way.", "face": "@_@" },
      "no":  { "t": "ok. i'll keep it warm.", "face": "._." },
      "effect": "spiral", "spice": 1 }
  ]
}
```
Ids are assigned by the editor; do not invent them. `q` <= 30 chars, chips <= 12 chars each. Ask effects
available: spiral, video, rain, burst, pinTop:<targetId>, shrink, bedtime, open:<targetId>, none.
