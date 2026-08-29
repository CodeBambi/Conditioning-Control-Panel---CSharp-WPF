# EMI Desk - editor's audit of the writers' room delivery (2026-08-29)

Scope: the seven writers' files in `lines/` (1,694 lines + 58 asks) plus the approved round-1
lines transcribed into `lines/round1.json` (64 lines + 13 asks; round-1 section E cuts stay cut,
verified mechanically: none of the seven E texts is in the shipped file; E7's approved rewrite
"like a bug on a window" is the one that ships). Source total 1,758 lines + 71 asks.

Result: `Resources/emi/desk-lines.json`, 356,994 bytes: 90 pools, 1,691 lines + 30 hold rows,
67 asks, 90 moments, 40 deferred. Rebuild with `python docs/emi-desk/tools/merge-lines.py`;
audit with `python docs/emi-desk/tools/check-lines.py` (re-runnable, exit 1 on errors).

Final checker line:

```
check-lines: 90 pools, 1691 lines (+30 holds), 67 asks, 90 moments, 40 deferred | typos 172 (10.2%), doubles 78, spice s0=65% s1=25% s2=10% | 0 errors, 3 warnings
```

The three warnings are deliberate keeps: `ringPick.024` and `common.deeper.026` open a sentence
with "deeper" as a noun (a card name / a direction, not an instruction), and `ask.dragged3x.001`
sits on a deferred moment (inert until `dragged3x` lands; kept so the ask is not lost).

## 1. Mechanical audit (pass 1)

What the writers got wrong, all fixed by normalisation in `merge-lines.py` (nothing cut for it):

| finding | count | fix |
|---|---|---|
| chain `pet` / `sleepy` (neither is in chains.js) | 18 lines | stored as `love` / `blink` |
| British spellings (favourite/cosy/colouring/colour) | 9 lines | US spelling (VOICE.md) |
| scalar `when` values (`stopped`, `failed`, `smaller`, `channelIs:x`) | 4 pools | `!running`, `!passed`, `!bigger`, `channel:x` |
| `double: true` in frequent pools (idle, attention, idleShort, appIdleLong, summoned, ringOpen, petted, featureOpened) | 3 lines (incl. round-1 #6) | de-flagged (ringOpen fires too often to carry the one double) |
| hold rows written as text lines | 30 rows | `t: ""`, `holdMs`, `chain: null` allowed |
| glassOffer asks with no channel gate | 10 asks | auto-gated by effect (`spiral` -> `channel:spiral`, etc.) |
| face `(´∀`)` (VOICE.md yes, chains.js no) | 61 lines | allowed; engine renders KAO class |
| ids | all | `<pool>.<3-digit>` / `ask.<moment>.<3-digit>`, assigned in source order after cuts |

Fence sweep (acronym family, door/lab/records/basement, "i love you", trigger words,
premium/prices/pay/subscribe/patreon, "don't go"/"please stay", programmed/scripted, "dave"):
one literal hit (`bubbleCountLost` "some don't go." about bubbles, rewritten, 2.2) and one false
positive ("tidy little unlock" on tierUp; `unlock` was dropped from the fence because the word is
the app's own vocabulary for skills and cards).

Length/case/dash/quote/ascii: 0 violations after the writers' own passes. Exact duplicates across
pools: 6, cut (2.1). Near-verbatim of shipped `barks.js` lines: 2, cut (2.1).

## 2. By-ear audit (pass 2)

### 2.1 Cut: 37 lines

Top reasons, in order: (a) 18 near-duplicates across pools that draw together (a specific pool
and a common it attaches to, or the two halves of a merged pool); (b) 7 light rewrites of round-1
lines the writers had read (#5, #11 x2, #14, #15, #16, #18, #21); (c) 5 triple-ups of the same
reference or joke (Borg x3 -> 1, "does not compute" x3 -> 1, "insufferable" x3 -> 2, "you have a
type" x3 -> 2, gold-star-in-the-notebook x2 -> 1); (d) 2 light rewrites of shipped Arcademy
lines; (e) 3 filler collisions (`hm.` / `ok.` / same-pixel stare already in common.idle);
(f) 1 pitch; (g) 1 induction wink.

| # | writer | pool | line | reason |
|---|---|---|---|---|
| 1 | ui-companion | summoned | back on the desk. mind the antenna | rewrite of round1 #14 |
| 2 | ui-companion | ringOpen | {target} is up top. it earned that | rewrite of round1 #16 |
| 3 | ui-companion | pinAdded | {target} pinned. i'll polish it | rewrite of round1 #15 |
| 4 | ui-companion | suggestionIgnored3x | alright. it goes back in the drawer | rewrite of round1 #21 |
| 5 | ui-companion | sheListeningOn | listening mode. not me. her | same idea as feature-b sheListeningOn 06 (merged pool) |
| 6 | feature-b | sheListeningOn | say something. i cant hear it | same opener + idea as ui sheListeningOn 03 (merged pool) |
| 7 | feature-b | sheListeningOn | she's got ears now. i've got a face | near-verbatim of ui sheListeningOn 02 (merged pool) |
| 8 | feature-b | takeoverEnded | {minutes} minutes of her calling it | same first two words as ui takeoverEnded 01 (merged pool) |
| 9 | feature-b | pinkRushStarted | pink! everything counts triple | same joke as session-progression pinkRushStarted 00 (merged pool) |
| 10 | feature-b | pinkRushStarted | rush hour. the pink kind. mind your feet | near-verbatim of session-progression pinkRushStarted 06 (merged pool) |
| 11 | feature-a | featureOpened | hm. {target}. bold. i'd have picked it too | rewrite of round1 #11 |
| 12 | common-a | common.tease | hm. bold. i'd have picked the same | rewrite of round1 #11 |
| 13 | feature-a | flashesStarted | resistance is futile | third Borg line; on a flashes-start beat it reads as an induction wink |
| 14 | time-tier-lifecycle | premiumTeaseSeen | resistance is futile | third Borg line (ringOpen keeps the one) |
| 15 | feature-a | bubbleCountLost | does not compute | triple with common.dork 05 |
| 16 | feature-b | overlaySpiralUp | does not compute | triple with common.dork 05 |
| 17 | common-a | common.tease | going to be insuferable | third "insufferable" (avatarMuted 09, common.win 45 stay) |
| 18 | session-progression | achievementUnlocked | well. that's going in the notbook. gel pen | same joke as xpBigAward 09; both draw common.win |
| 19 | session-progression | sessionCompleted | and done. gold star. drawing it. it's lopsided | same joke as mantraCompleted 04 |
| 20 | time-tier-lifecycle | tierUp | big day. gold star. two. | rewrite of round1 #18 |
| 21 | session-progression | levelUp | {level}!! wait till the cat hears | same joke as common.win 10, which levelUp draws |
| 22 | feature-a | arcademyOpened | you picked school. on purpose | same first three words as arcademyFromRing 06; both fire on one open |
| 23 | time-tier-lifecycle | morningFirst | shall we play a game? | dup of common.dork 00 |
| 24 | time-tier-lifecycle | backSoon | missed me? blink once | near dup of summoned 13; both fire on a summon |
| 25 | time-tier-lifecycle | premiumTeaseSeen | the good stuff's in there | reads as a pitch; the moment bans pitches |
| 26 | common-a | common.encourage | breathe. i'll fake one too | near dup of flashesStopped 05 |
| 27 | common-b | common.afterEffect | ok. that was a lot. breathe. | near dup of sessionPaused 02 |
| 28 | common-b | common.lateNight | can't sleep? i can't either | near dup of bedtimeBroken 03 (attached pool); same opener as lateNight 48 |
| 29 | feature-b | videoEnded | you went somewhere for a bit. i held the corner | same first five words as common.afterEffect 29, which videoEnded draws |
| 30 | common-a | common.tease | you have a type. it's that. | third "you have a type" (featureOpenedRepeat 03, suggestionIgnored3x 13 both draw common.tease) |
| 31 | ui-companion | ringOpen | ta. da. two syllables | rewrite of round1 #5 "ta-da"; same opener |
| 32 | ui-companion | effectFired | did you see that. i did that | near dup of common.win 04, which effectFired draws |
| 33 | time-tier-lifecycle | idleShort | i've been staring at the same pixel | near dup of common.idle 14 (same spot), which idleShort draws |
| 34 | time-tier-lifecycle | idleShort | hm. | filler already in common.idle, which idleShort draws |
| 35 | time-tier-lifecycle | idleShort | ok. | filler already in common.idle, which idleShort draws |
| 36 | common-b | common.win | did you see that? you did it | light rewrite of the shipped Arcademy line "you saw that one. you saw it. i saw you see it." |
| 37 | feature-a | bubbleCountWon | right number! i had a different one | light rewrite of the shipped Arcademy line "correct. i had a different answer. yours was the right one." |

### 2.2 Fixed in place: 24 lines

| writer | pool | before | after | why |
|---|---|---|---|---|
| ui-companion | ringDismissed | browsing. i saw where the cursor lingered... | browsing. the cursor lingered on one. i won't say which. | surveillance on first read |
| ui-companion | ringPick | you pick it every night... | you pick it every time. it's got you. i'm a little jealous. | invented telemetry ("every night") |
| ui-companion | suggestionIgnored3x | {target} sat there and you looked right past it. rude | ... it wept. | "rude" lands on the player |
| ui-companion | petted | you pet me before you do anything else... | pets before anything else? i'd pick me first too. | invented telemetry |
| feature-b | sheListeningOn | the mic's on. she hears... | mic's live. she hears. i just see. say hi anyway. | opener collision in the merged pool |
| feature-b | sheListeningOn | your voice is on the table now... | voice on the table now. i'd hold it. carefully. | opener collision in the merged pool |
| feature-a | brainDrainOn | blur's up. don't fight it... | blur's up. squint all you like. it wins. i'd cheer though. | "don't fight it. you'd lose" is an instruction |
| feature-b | videoRunning | being this still suits you. i'm allowed... | being this still suits you. i'd say it twice. suits you. | third "i'm allowed to say that" |
| session-progression | engineStarted | i like this part. the part where you don't stop it | i like this part. the part where you sit back. | consent after-read on first read |
| session-progression | sessionFeatureArrived | one more thing on. you didn't ask... | one more thing on. the plan asked for it. i just clapped. | "you didn't ask. it came anyway" |
| session-progression | sessionLastMinute | almost. stay with it... | almost. nearly there. i'm right beside you. sort of. | "stay with it" is an instruction |
| common-b | common.lateNight | i like you like this. late and loose... | late and loose and still clicking. i'd keep this one. | third "i like you like this" |
| common-b | common.afterEffect | i like you like this. quiet and floaty... | quiet and floaty and mine for a bit. that's the good bit. | third "i like you like this" |
| common-b | common.lateNight | cant sleep? good.... | not sleepy? good. no. i mean. stay up with me a bit. | opener collision with bedtimeBroken 03 |
| feature-a | bubbleCountLost | some don't go. | some stay put. that's all. i stay put sometimes. i know. | literal fence phrase |
| ui-companion | pinAdded | ok that one's yours now... | that one's yours now. everyone can see. i can see. | opener shared with round1 #17 |
| ui-companion | ringPick | that one. i had a feeling... | oh, that one. i had a feeling. i have a lot of feelings. | opener shared with round1 #13 |
| ui-companion | glassOffer | this one's my favorite. you'd sit... | my favorite. you'd sit still for it. i know you would. | opener shared with round1 #29 |
| time-tier-lifecycle | appIdleLong | still here. still small... | still small. still yours. still here. mostly the middle one. | opener shared with round1 #50 |
| time-tier-lifecycle | lateNight | it's late and you're still... | late, and you're still looking at me. keep going. | opener shared with round1 #54 |
| time-tier-lifecycle | lateNight | one more. i wont tell... | go on. one more. i wont tell the clock. | opener shared with round1 #57 |
| feature-b | overlaySpiralUp | {n} seconds of that. | that's {n} seconds of it. i'll time it wrong. on purpose. | opener shared inside the pool |
| common-a | common.smallTalk | ok. | mm. | `ok.` lives in common.idle |
| common-a | common.smallTalk | hm. | well. | `hm.` lives in common.idle |

### 2.3 Asks: 4 cut, 3 fixed

Cut: ui-companion "let a few out?" (dup of round1 d03 "can i let some out?"); ui-companion
"front row for that one?" (dup of round1 d06); round1 d08 "tidy the ring?" (effect `tidy` is not
in the allowed set and there is no tidy action in the widget contract); round1 d09 "this one's
stuck. try it anyway?" (re-opens the tier gate right after a refusal: a pitch by another name).

Fixed: feature-a "want me smaller?" -> "shrink me? less to blur." (dup q of the longSitting ask);
feature-b "confetti? it's gifs. close." -> "throw some gifs at it?" (dup q of the lockdownEnded
ask); feature-b "burst? while it's triple?" no-reaction rewritten (was "i'm full of gif" = round1
#30).

Editor additions on round 1: d06 q "{target} again? front row?" with gate `topUnpinned` (needs
the ring owner to pass that flag); d10 q "see the school? five minutes?"; d12 gets `effectNo:
"video"` (spin or show, both chips act); d13 effect `{channel}` (repeat what just fired).

### 2.4 Merged pools

MOMENTS.md assigned the same beat to two writers four times. Merged, with the collisions above
resolved: `sheListeningOn` (ui 6 + feature-b 7 = 13 after cuts), `takeoverEnded` (ui 8 +
feature-b 7 = 15), `pinkRushStarted` (feature-b 6 + session-progression 8 = 14), and the
`lateNight` asks (time-tier 2 + round1 d07 = 3, all on the same 10-minute cadence).

## 3. The 20 spiciest / edgiest lines (editor's list)

All spice 2 unless noted; all pass the fence on first read; all are behind the per-fire ceiling,
so a moment with `spiceCeiling` 1 never sees them. Flagging for the owner because "mine" / "keep
you" / "i get what's left" is the desktop's new register and he has not heard it yet.

| id | spice | line |
|---|---|---|
| glassOffer.009 | 2 | look what i found. it turns. so will you. haha. tap. |
| effectFired.017 | 2 | that's out now. no taking it back. sit with it. i am. |
| effectFired.018 | 2 | you let me. right onto your screen. i'll remember that. |
| takeoverEnded.007 | 2 | she had you. now i do. that's how the desk works. it's mine. |
| takeoverEnded.006 | 2 | that was {minutes} minutes of not you. i get what's left. |
| brainDrainOn.008 | 2 | drain's on. i'll do the thinking. you've earned a break. |
| subliminalsStarted.007 | 2 | you won't remember these. i will. for both of us. |
| sessionFeatureArrived.012 | 2 | right on schedule. the schedule wants you further in. |
| sessionStarted.024 | 2 | sit back. the next {minutes} minutes are already spoken for. |
| sessionCompleted.005 | 2 | complete. you look softer. don't fix it. (writer's own flag) |
| sessionCompleted.010 | 2 | you went all the way down. i watched. i'd do it again. |
| longSitting.010 | 2 | i've had you all afternoon. i'm not giving you back yet. |
| levelUp.024 | 2 | and there it is. another reason to keep you. i am. |
| common.deeper.032 | 2 | i want you a bit further in. just a bit. ok, a lot. |
| common.deeper.037 | 2 | mine for a bit. the bit's long. i measured it wrong. |
| common.lateNight.039 | 2 | after midnight you say yes more. i keep that to myself. |
| common.lateNight.045 | 2 | you'd look good a little dizzier. not offering. i am. |
| common.afterEffect.043 | 2 | you sink so nicely. i'll be thinking about that a while. |
| common.afterEffect.041 | 2 | good. i wanted you a little dazed. it's a good look. |
| ask.glassOffer.008 | 2 | q "can i put it on you?" chips do it / not now, effect spiral |

Closest to the fence and kept on purpose: `subliminalsStarted.007` ("you won't remember these")
names what subliminals do without a trigger word; `brainDrainOn.008` ("i'll do the thinking") is
the one line that flirts with an instruction, kept because it is framed as her offer, not his
order, and sits behind ceiling 2 on a feature the user just switched on himself.

## 4. Writers' own flags

- session-progression: `sessionCompleted.005` "complete. you look softer. don't fix it." (kept, in
  the list above).
- time-tier-lifecycle: `premiumTeaseSeen.015` "that's a want, not a need. i know the look. i have
  it." (s1) and `premiumTeaseSeen.024` "you want in. i want you in. one of us has a padlock."
  (s1). Both kept: neither names a price or a tier, both punch at her.
- common-a/common-b: the 13 lines they marked spice 2 = `common.tease.032` / `.043` and
  `common.deeper.006 .008 .010 .015 .018 .023 .028 .032 .035 .036 .037`. All kept; `common.deeper`
  is attached only to moments with ceiling 2.

## 5. Totals per pool (after edits)

| pool | lines | s0 | s1 | s2 | typos | doubles | tokens |
|---|---|---|---|---|---|---|---|
| desktopFirstBoot | 11 | 11 | 0 | 0 | 1 | 1 | 0 |
| ringOpen | 27 | 14 | 7 | 6 | 2 | 0 | 5 |
| common.dork | 34 | 34 | 0 | 0 | 3 | 0 | 0 |
| ringDismissed | 17 | 11 | 6 | 0 | 2 | 2 | 0 |
| ringPick | 29 | 16 | 7 | 6 | 3 | 2 | 4 |
| pinAdded | 18 | 12 | 6 | 0 | 2 | 1 | 2 |
| suggestionIgnored3x | 17 | 11 | 6 | 0 | 2 | 2 | 4 |
| resized | 19 | 13 | 6 | 0 | 2 | 0 | 2 |
| glassOffer | 32 | 19 | 7 | 6 | 3 | 2 | 2 |
| effectFired | 19 | 11 | 4 | 4 | 2 | 2 | 0 |
| videoRunning | 33 | 21 | 8 | 4 | 3 | 3 | 3 |
| videoEnded | 18 | 11 | 4 | 3 | 1 | 2 | 1 |
| appIdleLong | 28 | 18 | 7 | 3 | 2 | 0 | 2 |
| lateNight | 29 | 15 | 10 | 4 | 2 | 3 | 0 |
| premiumTeaseSeen | 26 | 19 | 7 | 0 | 1 | 2 | 2 |
| arcademyFromRing | 19 | 13 | 6 | 0 | 2 | 0 | 0 |
| summoned | 24 | 11 | 7 | 6 | 2 | 0 | 3 |
| dismissed | 15 | 9 | 6 | 0 | 2 | 1 | 2 |
| petted | 25 | 12 | 7 | 6 | 2 | 0 | 3 |
| bedtimeSet | 8 | 8 | 0 | 0 | 1 | 0 | 0 |
| bedtimeBroken | 8 | 3 | 2 | 3 | 1 | 1 | 2 |
| avatarMuted | 15 | 9 | 6 | 0 | 2 | 0 | 0 |
| avatarKept | 8 | 5 | 3 | 0 | 1 | 0 | 0 |
| takeoverEnded | 15 | 7 | 4 | 4 | 2 | 1 | 3 |
| sheListeningOn | 13 | 4 | 5 | 4 | 0 | 0 | 0 |
| featureOpened | 24 | 13 | 8 | 3 | 2 | 0 | 8 |
| featureOpenedRepeat | 8 | 3 | 4 | 1 | 1 | 0 | 4 |
| flashesStarted | 14 | 6 | 5 | 3 | 1 | 0 | 0 |
| flashesStopped | 8 | 5 | 3 | 0 | 1 | 1 | 2 |
| attentionCheckPassed | 15 | 7 | 5 | 3 | 2 | 0 | 0 |
| attentionCheckFailed | 15 | 11 | 4 | 0 | 1 | 0 | 2 |
| brainDrainOn | 15 | 6 | 5 | 4 | 1 | 1 | 2 |
| bubbleCountWon | 14 | 10 | 4 | 0 | 2 | 0 | 2 |
| bubbleCountLost | 14 | 14 | 0 | 0 | 1 | 0 | 0 |
| blinkTrainerStarted | 8 | 4 | 3 | 1 | 1 | 0 | 2 |
| arcademyOpened | 14 | 10 | 4 | 0 | 1 | 1 | 0 |
| dtrhOpened | 8 | 5 | 3 | 0 | 1 | 0 | 0 |
| fypOpened | 8 | 4 | 2 | 2 | 1 | 0 | 0 |
| intakeOpened | 8 | 5 | 3 | 0 | 1 | 0 | 0 |
| intakeClosed | 8 | 6 | 2 | 0 | 1 | 0 | 0 |
| lockdownArmed | 8 | 8 | 0 | 0 | 1 | 1 | 3 |
| lockdownEnded | 8 | 8 | 0 | 0 | 1 | 1 | 2 |
| mantraCompleted | 8 | 4 | 3 | 1 | 1 | 1 | 0 |
| overlaySpiralUp | 14 | 7 | 5 | 2 | 1 | 1 | 2 |
| subliminalsStarted | 8 | 5 | 2 | 1 | 1 | 1 | 0 |
| pinkRushStarted | 14 | 5 | 6 | 3 | 2 | 0 | 0 |
| engineStarted | 25 | 13 | 6 | 6 | 3 | 2 | 0 |
| engineStopped | 15 | 11 | 4 | 0 | 1 | 1 | 2 |
| rampStepUp | 8 | 4 | 1 | 3 | 1 | 0 | 2 |
| sessionFeatureArrived | 15 | 8 | 4 | 3 | 1 | 0 | 6 |
| sessionStarted | 25 | 11 | 8 | 6 | 3 | 3 | 8 |
| sessionPaused | 8 | 8 | 0 | 0 | 1 | 0 | 0 |
| sessionResumed | 8 | 4 | 3 | 1 | 1 | 0 | 3 |
| sessionPhaseChanged | 15 | 8 | 3 | 4 | 2 | 0 | 8 |
| sessionHalfway | 8 | 3 | 2 | 3 | 1 | 1 | 2 |
| sessionLastMinute | 8 | 3 | 2 | 3 | 1 | 0 | 0 |
| sessionCompleted | 24 | 10 | 8 | 6 | 2 | 3 | 5 |
| sessionAbandoned | 8 | 8 | 0 | 0 | 1 | 0 | 0 |
| levelUp | 24 | 11 | 7 | 6 | 3 | 2 | 9 |
| xpBigAward | 15 | 10 | 5 | 0 | 2 | 0 | 5 |
| achievementUnlocked | 24 | 15 | 9 | 0 | 2 | 2 | 8 |
| questCompleted | 15 | 10 | 5 | 0 | 1 | 1 | 3 |
| streakMilestone | 15 | 8 | 3 | 4 | 1 | 1 | 6 |
| streakKept | 15 | 10 | 5 | 0 | 2 | 1 | 4 |
| streakBroken | 15 | 15 | 0 | 0 | 1 | 0 | 2 |
| skillUnlocked | 8 | 5 | 3 | 0 | 1 | 1 | 0 |
| smallHours | 15 | 8 | 5 | 2 | 2 | 1 | 2 |
| morningFirst | 14 | 9 | 5 | 0 | 2 | 0 | 0 |
| idleShort | 22 | 12 | 7 | 3 | 2 | 0 | 2 |
| longSitting | 15 | 8 | 5 | 2 | 2 | 1 | 2 |
| backSoon | 7 | 5 | 1 | 1 | 1 | 0 | 1 |
| lockedCardTapped | 8 | 5 | 3 | 0 | 1 | 1 | 1 |
| tierUp | 7 | 4 | 3 | 0 | 1 | 0 | 1 |
| tierLapse | 15 | 15 | 0 | 0 | 2 | 0 | 1 |
| dailyFreeToday | 8 | 4 | 4 | 0 | 1 | 0 | 2 |
| appOpened | 25 | 14 | 7 | 4 | 3 | 2 | 0 |
| updateAvailable | 8 | 6 | 2 | 0 | 1 | 0 | 2 |
| afterUpdate | 15 | 10 | 5 | 0 | 2 | 1 | 2 |
| crashRecovered | 8 | 8 | 0 | 0 | 1 | 0 | 0 |
| common.attention | 45 | 34 | 11 | 0 | 5 | 0 | 0 |
| common.idle | 48 | 43 | 5 | 0 | 5 | 0 | 0 |
| common.encourage | 45 | 45 | 0 | 0 | 5 | 2 | 0 |
| common.tease | 47 | 22 | 23 | 2 | 4 | 3 | 0 |
| common.deeper | 49 | 19 | 19 | 11 | 5 | 4 | 0 |
| common.smallTalk | 50 | 47 | 3 | 0 | 5 | 1 | 0 |
| common.lateNight | 48 | 25 | 13 | 10 | 5 | 4 | 0 |
| common.afterEffect | 49 | 28 | 11 | 10 | 5 | 1 | 0 |
| common.win | 49 | 41 | 8 | 0 | 5 | 3 | 0 |
| common.loss | 48 | 48 | 0 | 0 | 5 | 3 | 0 |
| common.hold | 30 holds | | | | | | |
| total | 1691 | 1098 | 420 | 173 | 172 | 78 | 158 |

Spice split 65 / 25 / 10 against the brief's ~55 / 30 / 15: light on 1 and 2 because the commons
(half the file) run innocent by design. Typos 10.2% (target 8-12%). Doubles 78 = 4.6% of lines,
none in the frequent pools. Thin pools: `backSoon` 7 and `tierUp` 7 after cuts, both rare
moments; `sheListeningOn` has 0 typos (merged from two writers who each put theirs in the cut
lines) - fine for a 13-line pool.

Asks: 67 = glassOffer 14, summoned 5, lateNight 4, petted 4, ringOpen 3, then 2 each on ringPick,
ringDismissed, appIdleLong, effectFired, videoEnded, pinkRushStarted, and 1 each on 25 other moments
(incl. dragged3x, deferred). premiumTeaseSeen carries none (the moment bans asks). glassOffer's 14
are split by channel gate, so any one fire sees 3-4 of them.

## 6. MOMENTS.md items I could not map, and what I did instead

- **Global floor**: MOMENTS.md §3 says 180 s, BRIEF §8 says 45 s. Documented 45 s (the owner
  locked the BRIEF); the moment `cooldownMs` values follow MOMENTS.md.
- **Dork odds**: MOMENTS.md says 0.12 per fire, BRIEF §6 says "one dork per launch, ~8%".
  Shipped `dork.odds` 0.08; one number to change if the owner wants 0.12.
- **`dragged3x`** (round1 d11) is a DEFER moment; the ask ships inert under `ask.dragged3x.001`.
- **`tidy` effect** (round1 d08): no such action exists in the widget contract; ask cut.
- **`pet` / `sleepy` chains**: not in chains.js; mapped to `love` / `blink` (18 lines).
- **`ringDismissed` ctx**: MOMENTS.md lists none, but d05 ("open it for you?") needs the top
  card; documented `{target}` on ringDismissed = same value as ringOpen.
- **`topUnpinned`** on ringOpen: not in MOMENTS.md; added so d06 does not offer to pin a card that
  is already pinned.
- **`{target}` display names**: MOMENTS.md's ctx for featureOpened is the raw tab key
  (`gradedintake`) and videoRunning's is a file name; the schema says the hook maps to a display
  name before the engine sees it.
- **askAnswered / appClosing**: MOMENTS.md gives them pools; shipped as `pools: []` (the reaction
  and the flinch are the line).
- **Hold with a 300 s tail** (panicPressed): MOMENTS.md says "five minutes of nothing"; modelled
  as `tailMs: 300000` on a hold moment rather than a special case.
- **Frequent-pool doubles**: MOMENTS.md allows doubles anywhere; VOICE.md rations one per launch.
  De-flagged in the eight frequent pools so the ration is not always spent on an idle line.

## 7. Files

- `Resources/emi/desk-lines.json` (generated, shipped; csproj Content item
  `Resources\emi\**\*.json`, PreserveNewest, ExcludeFromSingleFile).
- `docs/emi-desk/lines/round1.json` (round-1 transcription, `writer: "round1"`).
- `docs/emi-desk/tools/merge-lines.py` (cuts, fixes, moments table, generator).
- `docs/emi-desk/tools/check-lines.py` (auditor; takes writers' files as args to audit raw).
- `docs/emi-desk/LINES-SCHEMA.md` (schema, draw algorithm, ctx per moment).
- Nothing in `docs/emi-desk/lines/` was deleted or edited; every change lives in the merge script.
