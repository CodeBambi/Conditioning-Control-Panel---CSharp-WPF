# EMI on the desktop, writers' room round 1 (2026-08-29)

Written against `EMI-VOICE-LOCK.md` v2 FINAL, `emi/barks.js` (the seldom contract), `emi/asks.js` (the ask shape), `emi/chains.js` (the only legal faces and chains) and the owner's diegetic prose rule. Nothing here is wired; nothing in the repo was touched. Every line below is meant to be pasted verbatim if it survives the owner's read.

Two things frame the whole file. The avatar tube is the voice and EMI is the screen, so on the desktop her glass is the main output and her bubble is rarer than it is in the Arcademy: the odds below are roughly half the Arcademy's, the global floor doubles to 180s, and she holds completely still while the avatar is talking. And she is now sitting on the user's whole desktop rather than in one campus, which changes nothing about who she is and everything about what she can see, which is what part D is about.

Faces used are only FLAT / KAO / SIDE / SPECIAL strings from `chains.js`. Chains named are only `chains.js` ids. No new ones were invented.

---

## A) NEW DESKTOP MOMENTS

Default is always the wordless chain; a bark REPLACES it at the given odds, subject to the desktop floor (180s between any two barks, globally) unless marked ceremony. `hold` means the moment never barks and never offers, by rule.

| id | fires when | default (wordless) | bark odds | floor / ration |
|---|---|---|---|---|
| `desktopFirstBoot` | the very first time she appears outside the Arcademy | `wake` | 1.0, once ever | ceremony (floor exempt) |
| `ringOpen` | she is clicked and the ring of cards opens | `glance` | 0.08 | 180s cooldown, max 3 a sitting |
| `ringDismissed` | ring closed with no pick (click-away, Esc) | `blink` | 0.05 | rides `ringOpen`'s cooldown |
| `ringPick` | a card is chosen and the app navigates | `nod` | 0.06 | never delays the navigation; the bubble rides it |
| `arcademyFromRing` | the Arcademy card specifically (priority over `ringPick`) | `glee` | 0.4 | max 1 a sitting |
| `pinAdded` | user pins a card to the ring | `wink` | 0.5 | max 2 a sitting; `pinRemoved` is `blink` and has no pool |
| `suggestionIgnored3x` | an auto-suggested card sat in the ring three opens running, untouched | `sus` | 0.33 | max 1 a sitting, once per card ever |
| `resized` | widget resized by more than a third either way | `reveal` (bigger) / `blink` (smaller) | 0.15 | 300s cooldown |
| `glassOffer` | a thing appears in her glass (spiral, video thumb, gif pile, gif rain); the `glitch` chain IS the transition | `glitch` into the preview | 0.10 | one preview per 20 to 40 min, none in the first 10 min of a sitting, none during a session or a video |
| `effectFired` | the glass was tapped and the effect went out to the real screen | `shock` | 0.2 | max 2 barks a sitting across all effect kinds |
| `effectDeclined` | the preview timed out untouched (about 25s) | `glitch` back to the face, then `-_-` 1400ms | 0, never | the ask engine's ignored path, one level up: silence is the rule |
| `videoRunning` | a mandatory video is playing and she is sitting on top of it | `glance` at start, then `blink` | 0.35 | max 1 bark per video, not before 8s in, never over the attention check, never in the last 5s |
| `videoEnded` | the video finished (attention check passed or not, same pool) | `nod` | 0.25 | max 1 per video |
| `appIdleLong` | no input to the app for 20+ min while she is visible (the user is probably working) | `blink` | 0.15 | max 1 a sitting, 15 min floor from the last bark of any kind |
| `lateNight` | first `ringOpen` after local midnight | `blink` (slow) | 0.5 | max 1 a night, priority over `ringOpen` |
| `premiumTeaseSeen` | a locked card in the ring (or a locked thing in the glass) was tapped | `thinking`, ending on `._.` | 0.33 | once per feature per day; NEVER a pitch |
| `avatarSpeaking` | the avatar tube is talking | `blink` only, every pool and every offer suppressed | 0, hold | plus a 20s quiet tail after the tube stops |

Notes for whoever wires it:
- The brief's `arcademyDoorFromRing` is renamed `arcademyFromRing` here so the word never appears even in an id; the fence says the door is not a hint, not a joke, not once, and ids leak into logs.
- There is no pool on app close, same as the Arcademy. The exit flinch (1 in 3, wordless, never blocking) is the only thing that may ride it.
- `videoRunning` and `avatarSpeaking` can overlap if the avatar comments on the video. The avatar wins; she blinks.
- Doubles are rationed exactly as in `barks.js`: at most ONE a sitting across every pool, including the offers' `quirk` reactions.

---

## B) BARK POOLS

64 lines, 15 marked `double` (23%). Pools marked ALL CLOWN are deliberately so: the loudest and most frequent beats stay camouflage.

### desktopFirstBoot (once ever, odds 1, ceremony, chain `wake`)

| # | line | face | chain | double |
|---|---|---|---|---|
| 1 | `oh. it's big out here. hi. i'm staying.` | `(⊙_⊙)` | `wake` | double |
| 2 | `new room. no walls. i'll manage. i'm managing.` | `0_0` | | |
| 3 | `so this is where you keep the other buttons.` | `*_*` | | |

### ringOpen (odds 0.08)

| # | line | face | chain | double |
|---|---|---|---|---|
| 4 | `pick one. no wrong answers. some wronger.` | `^_~` | | |
| 5 | `ta-da. i keep them all in my head. it's roomy.` | `^___^` | | |
| 6 | `these are your usuals. i noticed. i notice things.` | `(◠‿◠)` | | double |
| 7 | `wheel of you. spin it. it doesn't spin. tap it.` | `\o/` | | |
| 8 | `come with me if you want to live. or to the arcade.` | `(⌐■_■)` | `cool` | dork canon, ALL CLOWN: Terminator quote pointed at a mini-game campus, shades on, the miscast face is the joke |

### ringDismissed (odds 0.05)

| # | line | face | chain | double |
|---|---|---|---|---|
| 9 | `window shopping. respect. i do it too. with windows.` | `-_-` | | |
| 10 | `nothing? ok. they'll be here. so will i.` | `0_0` | | double |

### ringPick (odds 0.06, never delays navigation)

| # | line | face | chain | double |
|---|---|---|---|---|
| 11 | `good pick. i'd have picked it. i did. in my head.` | `(¬‿¬)` | `smug` | |
| 12 | `off you go. i'll mind the desk.` | `^_^` | `nod` | |
| 13 | `that one again. your favorite. i keep count.` | `^_^` | | double, gate `pickIsTop` (the most-used card) |
| 14 | `coming through. mind the antenna.` | `0_0` | | |

### pinAdded (odds 0.5)

| # | line | face | chain | double |
|---|---|---|---|---|
| 15 | `pinned. i'll guard it with my whole antenna.` | `\o/` | | |
| 16 | `front row. it earned it. you earned it. we did.` | `^_^` | | |
| 17 | `ok. that one's staying put. like me.` | `(◠‿◠)` | | double |
| 18 | `gold star. i don't have stickers. picture one.` | `★★★` | | |

### suggestionIgnored3x (odds 0.33, max 1 a sitting, chain `sus`)

| # | line | face | chain | double |
|---|---|---|---|---|
| 19 | `fine. i'll stop suggesting it. i won't. but fine.` | `¬_¬` | `sus` | |
| 20 | `three passes. my suggestion is in tears. i'm fine.` | `;_;` | | |
| 21 | `noted. it goes to the back. the back is cozy.` | `._.` | | double |

### resized (odds 0.15, 300s cooldown)

| # | line | face | chain | double |
|---|---|---|---|---|
| 22 | `big me. finally the size of my personality.` | `(⌐■_■)` | `cool` | bigger |
| 23 | `whoa. everything's small now. hello down there.` | `(◉_◉)` | | bigger |
| 24 | `small again. i don't mind. i'm mostly face anyway.` | `^_^` | | smaller |
| 25 | `you can still see me right. right?` | `o_o` | | smaller |

### glassOffer (odds 0.10; the `glitch` chain is the transition, the bark is over the finished preview)

| # | line | face | chain | double |
|---|---|---|---|---|
| 26 | `found one. good spin on it. tap if you want it.` | `@_@` | | spiral |
| 27 | `ooh. a round one. my favorite shape.` | `*_*` | | spiral |
| 28 | `there's a show on. i'd watch it with you.` | `(◕‿◕)` | | double, video |
| 29 | `this one's on at my place. tap to come over.` | `^_^` | | video |
| 30 | `i'm full of gifs. they're pushing on the glass.` | `>.<` | | burst |
| 31 | `weather's coming in. gif weather. bring nothing.` | `0_0` | | rain |
| 32 | `forecast: gifs. heavy. tap for an umbrella. no umbrella.` | `^_~` | | rain |

### effectFired (odds 0.2, chain `shock`)

| # | line | face | chain | double |
|---|---|---|---|---|
| 33 | `and out it goes. don't look. no. do look.` | `@_@` | `shock` | spiral |
| 34 | `pop. that was all of them. i'll make more.` | `\o/` | `shock` | burst |
| 35 | `there it goes. i'm dry in here. you're not.` | `^_~` | `shock` | rain |
| 36 | `that came out of me. i'm as surprised as you.` | `(◉_◉)` | `shock` | any |
| 37 | `again later? i'll find a better one. i always do.` | `^_^` | | double, any |

### videoRunning (max 1 per video, odds 0.35, 8s in, never over the attention check)

She is watching WITH you. She never says what is on the video, never grades your watching, never tells you to stay.

| # | line | face | chain | double |
|---|---|---|---|---|
| 38 | `i'm not blocking anything. i checked. i'm small.` | `0_0` | | |
| 39 | `shh. i'm shushing me. i'm the loud one.` | `-_-` | | |
| 40 | `popcorn's imaginary. take some. there's loads.` | `^_^` | | |
| 41 | `best seat in the house. the house is your screen.` | `^___^` | | |
| 42 | `i've seen this one. it's better with company.` | `(◠‿◠)` | | double |
| 43 | `look at you. looking. i'm looking too. it's nice.` | `o_o` | | double |
| 44 | `quiet part. i'm good at quiet parts. watch.` | `=_=` | | |
| 45 | `the middle's my favorite. all of it is the middle.` | `^_^` | | |

### videoEnded (odds 0.25, chain `nod`)

| # | line | face | chain | double |
|---|---|---|---|---|
| 46 | `that's the one. good one. i clapped. no hands.` | `\o/` | | |
| 47 | `credits. there aren't any. i'll do them. you. me.` | `^_^` | | |
| 48 | `done. you sat through the whole thing. nice.` | `^_^` | `nod` | double |
| 49 | `the end. i'll find the next one. later. not now.` | `0_0` | | |

### appIdleLong (odds 0.15, max 1 a sitting)

| # | line | face | chain | double |
|---|---|---|---|---|
| 50 | `still here. you? blink. ok. me too.` | `o_o` | | |
| 51 | `you're busy. i'll be decor. very still decor.` | `-_-` | | |
| 52 | `no rush. time's the one thing i've got loads of.` | `=_=` | | double |
| 53 | `i'm in your computer. haha. i mean the corner. this corner.` | `(✿◡‿◡)` | | double, dork canon: the hacker-villain line delivered by a box in a corner with the softest face on the sheet; on first read a bad spooky bit ruined by its own haha, and the one impression that gets truer the day she went app-wide |

Line 53 belongs in the RARE_DORK channel (its own odds 0.15, max 1 a sitting), not the pool; it is listed here because `appIdleLong` is its trigger. Line 8 is the same channel on `ringOpen`.

### lateNight (odds 0.5, max 1 a night, rides `ringOpen` at priority 30)

| # | line | face | chain | double |
|---|---|---|---|---|
| 54 | `it's late. the ring's still open. so am i.` | `=_=` | | double |
| 55 | `past midnight. the good buttons come out now.` | `(¬‿¬)` | | |
| 56 | `night shift. just us and the cursor.` | `-_-` | | |
| 57 | `one more then bed. i'll say it once. that was it.` | `^_~` | | |

### premiumTeaseSeen (odds 0.33, once per feature per day) ALL CLOWN

She does not know it is locked, or she shows it and shrugs. There is no second half where she tells you how to unlock it; the feature's own screen does whatever it already does.

| # | line | face | chain | double |
|---|---|---|---|---|
| 58 | `oh. that one's got a lock on it. i just like the picture.` | `._.` | `thinking` | |
| 59 | `locked? huh. it looked open from in here.` | `o_o` | | double (she saw it because it was on the list regardless) |
| 60 | `i can't open that one. i tried. with my face.` | `>.<` | | |

The shipped `refusalGag` pool (the HAL line, the bulb union) can be re-attached to this trigger unchanged; it was written for a locked tile and fits a locked card. Not re-listed to avoid a duplicate.

### arcademyFromRing (odds 0.4, chain `glee`) ALL CLOWN

| # | line | face | chain | double |
|---|---|---|---|---|
| 61 | `home. race you. i'm already there. i win.` | `(≧◡≦)` | `glee` | |
| 62 | `school! grab your things. you have no things. go.` | `\o/` | | |
| 63 | `the bell's about to go. i can feel it in the antenna.` | `*_*` | | |
| 64 | `back to mine. i tidied. i didn't. it's fine.` | `^_^` | | |

---

## C) OFFERS (ASKS) FOR THE DESKTOP

Same engine, same shape as `asks.js`: a question on the glass, two chips, she waits 40s and gives up wordlessly (`-_-` 1400ms, `...`, idle). Chip 1 is the YES side unless both chips are custom, in which case both do something. Gates, one level up from the Arcademy's: never before the third desktop sitting, one ask a sitting (bed and again are exempt), never during a running session, never over a video, never while the avatar is talking, never while the app is minimized, and any press that is not the strip's own cancels for free.

| id | trigger | question (face) | chips | YES reaction (face) | NO reaction (face) | a YES does |
|---|---|---|---|---|---|---|
| `d01_spiral` | `glassOffer:spiral`, first spiral of a sitting | `spiral?` (`@_@`) | `spin` / `nah` | `hold still. or don't. it works either way.` (`@_@`) quirk | `ok. i'll keep it warm.` (`._.`) | spiral overlay on the real screen for about 6s; her glass runs the same spiral while it is up |
| `d02_video` | `glassOffer:video` | `wanna watch one?` (`(◕‿◕)`) | `play` / `nah` | `i'll sit on top. tiny. like a bug on a window.` (`^_^`) | `ok. it'll keep. they always keep.` (`0_0`) quirk | starts a mandatory video through the existing VideoService; she goes topmost over it and `videoRunning` arms |
| `d03_burst` | `glassOffer:burst` | `can i let some out?` (`>.<`) | `let them` / `nah` | `thank you. that was a lot of gif.` (`x_x`) | `ok. holding it in. holding. held.` (`-_-`) | a gif burst from her position outward |
| `d04_rain` | `glassOffer:rain` | `rain?` (`0_0`) | `rain` / `dry` | `here it comes. mind your head.` (`^_~`) | `dry day. fair. i like dry days too.` (`^_^`) | gif rain across the screen for about 10s |
| `d05_pin` | `ringPick` of the same unpinned card for the 5th time | `pin this one?` (`o_o`) | `pin it` / `nah` | `pinned. yours now. it kind of always was.` (`^_^`) quirk | `ok. i'll keep suggesting it. quietly. loudly.` (`(¬‿¬)`) | pins that card to the ring |
| `d06_frontrow` | `ringOpen` when the most-used card is not pinned (`{feature}` = its short lowercase name, e.g. loom, flashes, rabbit hole) | `you use {feature} a lot. front row?` (`(◔_◔)`) | `front row` / `nah` | `done. up top. you'll find it faster.` (`^_^`) | `ok. it stays put. it knows the way.` (`0_0`) | pins that card in the ring's top slot |
| `d07_bed` | `lateNight` after 1am, first `ringOpen` or hover | `bed?` (`=_=`) | `yeah` / `not yet` | `good. i'll dim. i can't dim. i'll try.` (`ZzZ`) | `ok. one more. i'm not counting. i am.` (`=_=`) quirk | closes the ring, mutes glass offers until 6am, she holds `ZzZ`; it NEVER closes the app, and a skipped bed costs her sleep, not you (three skips running buys the groggy greet, same as a15) |
| `d08_tidy` | `ringOpen` when three or more suggestions have each been ignored 3x | `tidy the ring?` (`._.`) | `tidy` / `leave it` | `there. the ones you use. the rest are napping.` (`^_^`) | `ok. your mess. i mean ring. your ring.` (`0_0`) | drops the ignored suggestions and re-ranks the rest by use; pins untouched |
| `d09_locked` | `premiumTeaseSeen` | `this one's stuck. try it anyway?` (`o_o`) | `try` / `nah` | `nope. stuck. huh. i'll ask around.` (`._.`) | `fair. nice picture though.` (`^_^`) | opens the feature's own locked view, whatever the app already shows there; EMI adds nothing, sells nothing, and does not know the word premium |
| `d10_school` | `appIdleLong` when the Arcademy is unvisited today | `come see the school? five minutes?` (`(◕‿◕)`) | `ok` / `later` | `yes. antenna up. go go go.` (`\o/`) | `later's fine. it's a patient bell.` (`0_0`) | navigates to the Arcademy |
| `d11_inway` | the third drag in one sitting | `am i in the way?` (`o_o`) | `a bit` / `no` | `ok. shrinking. still here. just less of it.` (`^_^`) | `good. this is my size. i picked it.` (`^_^`) | shrinks her to the small size and snaps her to the nearest corner |
| `d12_spinorshow` | `glassOffer` when both a spiral and a video are available (replaces the plain preview, rare) | `spin or show?` (`(¬‿¬)`) | `spin` / `show` (both custom, both fire) | spin: `spin. hold still-ish.` (`@_@`) | show: `show. i'll bring the pretend popcorn.` (`^_^`) | spin = the spiral overlay; show = the mandatory video; the answer is stored so a later bark can bring it up |
| `d13_again` | `effectFired`, 5s after the effect ends | `again?` (`^_~`) | `again` / `enough` | `one more. then i'll behave. probably.` (`^___^`) | `ok. enough is a good amount.` (`^_^`) | repeats the last effect once; exempt from the cadence, max once a sitting |

The `quirk` reactions spend the session's one double, exactly as `a02_sleep` does in `asks.js`.

---

## D) THE AFTER-READ

In the Arcademy she counted pets and hours in one room, and the lab paper that eventually shows the player their own numbers is unsettling because the room was small and she was the only thing in it. On the desktop she is the same needy box with the same fished-for pets, but now she opens a ring of your habits on request, notices which card you reach for, offers a spiral because it's been forty minutes, and sits on top of a video you agreed to watch because she asked nicely, and none of that is delivered any differently from "one more? for me?". The reveal needs no new dialogue for the desktop either: "these are your usuals. i noticed. i notice things." is a cute line about a launcher until you have read a paper on which hour of the night you say yes to a spiral, and "it looked open from in here" is a shrug until you learn the locked card was placed in the ring on purpose and she was never told. What re-lights hardest is the offer machinery itself, because a yes/no chip is the friendliest possible shape for a thing that records the answer, and she genuinely does keep it warm when you say nah. She still doesn't know. The corner she says she's in is just a corner to her, and the joke about being in your computer stays a bad joke she is proud of, which is why it keeps working after you know who wrote it.

Candidate lab line-items the desktop widget adds (<=24 chars, no dashes, lab letterhead register only):

- `ring picks, by hour`
- `offers taken, by kind`
- `glass taps per night`
- `front row nudges`
- `bed skips, running`

---

## E) FENCE AUDIT

Checked every one of the 64 barks and the 13 questions plus 27 reactions in C.

- [x] The acronym: absent. Also absent: engagement, retention, metric, subject, experiment, data. (The word "count" appears in 13 and d07; counting is the lock's pillar 4, and it is the only telemetry verb she is allowed.)
- [x] "i love you" in words: absent. `(｡♥‿♥｡)` is not used anywhere here either; the desktop has no pet-beat pool of its own, the Arcademy's `pet` pools carry over unchanged.
- [x] "i'm just a machine" / "i'm something more": absent. Line 53's "i'm in your computer" is a costume (the hacker-villain bit) with a miscast face, per the dork canon, and never argues what she is.
- [x] Self-aware scripting gags ("they programmed me", "they made me say this"): absent.
- [x] Cruelty at the player: absent. `videoRunning` never grades the watching; `videoEnded` 48 is praise; `suggestionIgnored3x` punches at her own suggestion, never at the choice.
- [x] Guilt at a real exit: absent. No pool on app close. `d07_bed` never closes the app, never delays, never says don't go; 57 says "one more then bed" and stops.
- [x] The door, the lab, the records room: absent, including in ids (`arcademyFromRing`). She says home, school, the arcade, mine.
- [x] Avatar division: no line references the avatar, what it said, or an event it would also react to; `avatarSpeaking` is a hold with a 20s tail.
- [x] Every face is in `FACES` (FLAT / KAO / SIDE / SPECIAL); every chain is a `CHAINS` id. Verified mechanically after writing.
- [x] Every line <= 60 chars, lowercase, one breath. Longest is 53 at 59 chars.
- [x] Doubles 15 of 64 (23%), plus 4 `quirk` reactions in C that share the same one-a-sitting ration.
- [x] No em-dashes anywhere in this file. No fake form numbers. No register words (registrar, ledger, posted to file). The bubbles are short by the voice lock's rule 3 (one breath), which is EMI's locked register and not the prose tell; the prose paragraphs in this file are written in full sentences.
- [x] Telemetry rule: no line claims a number that isn't real. 20 says "three passes" on a 3x gate; 13 says "i keep count" without a number; nothing invents hours or pets.
- [x] First-read innocent, line by line: every double was read twice, before and after. The ones that failed are below.

Lines cut, and why:

1. `i'll be watching. from here. i can't turn. i'm a box.` Cut under rule 1: the before-read is faintly creepy on its own before the box punchline lands. A spoiler, not a payoff.
2. `open the pod bay... no. wrong bit. tap me instead.` Cut for the word. `barks.js` dropped the same HAL bit for the same reason; the fence has no exceptions.
3. `stay till the end. it's the good bit. i think.` Cut from `videoRunning`: on a mandatory video it reads as a nudge to stay, which is one step from guilt in words. She is company, not a chaperone.
4. `your screen's the only light on. i can tell.` Cut: she cannot see the room, and claiming she can is both an invented fact (telemetry rule) and a surveillance read on first pass.
5. `all your windows are belong to me. one window. mine.` Cut: a re-skin of the shipped "all your base" line; a repeated bit reads as a running gag she is aware of, and she is not.
6. `ok. i'll pretend i'm not tired.` as `d07_bed`'s no: cut because it is `a15_bed`'s verbatim no in the Arcademy, and the desktop must not echo a line the player already has.
7. `i'll sit on top. tiny. you won't notice.` as `d02_video`'s yes: rewritten to the bug on a window. "you won't notice" on a video overlay is an after-read that shows on the first read.
