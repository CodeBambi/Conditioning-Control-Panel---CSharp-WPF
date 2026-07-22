# Fake-Captcha Intake Items — Brainstorm (2026-07-22)

Three parallel brainstorm lenses: (A) real-world captcha taxonomy, (B) psych-evaluation
instruments reframed as verification, (C) user-asset leverage + 5-band glitch escalation.
All ideas respect the four invariants (friction-not-lockout, gradeable answer shapes
index/bool/verbatim/slider, niche-agnostic templates flavored by the mod's theme pack,
no runtime generation / no server round-trips).

---

## SYNTHESIS

### Convergent templates (invented independently by 2+ lenses — build these first)

1. **The Hydrant Grid (the seed, fully laddered)** — dead-standard reCAPTCHA 3x3 clone whose
   tiles flicker into the user's own gifs. Calibration: pure hydrants, ONE 3-frame flicker near
   the end. Establishing: 300ms flicker bursts, `anomalies logged: 2` after submit. Deepening:
   sticky swaps, footnote `*hydrants may not be present`. Climax: all 9 tiles are their gifs,
   instruction still says hydrants — the grade is which of their own files they chose to call
   one. Recovery: 8 clean hydrants + one canvas-frozen first frame of the gif they clicked
   first at Climax, graded correct either way. *The frozen Recovery tile is the payoff: "I kept
   your answer."* Double-booked grading: printed-instruction correctness AND a hidden ledger of
   selections made while a user gif occupied the tile.

2. **Transcription ladder (v1 text captcha → word stems → "type what you see")** — three takes
   that fuse into one arc. Establishing: two warped words, only the control word is ever
   checked — the trigger word is the unpaid labor, exactly like real reCAPTCHA v1. Deepening:
   word-stem completion (`0B_D__NT`) where the illegible letters must be *generated*, flag fires
   on "completion from partial cue". Climax: the word image is blank (or the gif fullscreen),
   instruction `type the characters you see` — anything typed is accepted and echoed back in
   captcha font as if it were always the prompt; verbatim capture is the richest tag-vote source
   in the feature. Recovery: innocuous `xR7pq2` — but the input placeholder is their Climax
   confession in ghost-grey. Empty submit always valid (`transcription: silent.`).

3. **The Checkbox ("I am not a robot")** — most trusted widget on the internet, played 100%
   straight at Calibration. Ladder: click → 3s hold (gif ghosts at 8% opacity behind the card)
   → 8s hold at 40% opacity, spinner label cycling `verifying... verifying you... verifying
   yours...` → Climax `☐ I am not resisting.`, gif behind glass blur, check goes green when the
   hold stabilizes → Recovery: pre-checked, untickable, `verification is permanent.` Label swaps
   per niche invert the semantics (drone: checking "I am not a drone" is the refusal). Early
   release *reduces* the requirement — friction decays toward the user.

4. **Rotate-to-upright (FunCaptcha)** — two distinct jokes, both keepable as separate items:
   (a) the figure variant: the accepted "upright" for the kneeling figure is the kneel resting
   on the floor line; standing-vertical shakes twice then accepts on attempt 3; Climax arrows
   gain a magnet toward the kneel. (b) the spiral variant: their own spiral gif is rotationally
   symmetric AND animating, so upright is unfalsifiable — dwell is the graded resource, a ✓
   fires on >2s stillness, and at Climax the arrows secretly change spin SPEED, so the only
   discoverable "correct" is the slowest spin: they tune their own spiral to hypnotic tempo and
   the system certifies it. `matches previous session: 94%` — they never told it a previous angle.

5. **Slider puzzle (GeeTest)** — two fusable readings: install the corruption (the piece that
   fits the hole punched over their image's focal point is a niche icon — spiral iris, visor,
   lock — and seating it chimes `match confirmed`), or repair the file (the fragment is a frozen
   crop that docks, sits dead in a living image, then "reconnects" and animates). Climax: two
   notches, one fragment — docking either makes the other fill itself: `the file repairs itself
   when you cooperate.` Refusal link: `report file as damaged` → `damage logged as chosen`.

6. **Regenerating grid ("select all until none remain")** — the gaslighting captcha everyone has
   lived, made honest about it. Calibration: actually completable. Then replacements pull from
   the user's library at rising ratio; the task quietly becomes "how long do you keep clicking
   your own files." Grade = clicks past the point of obvious futility. Climax: incoming tiles
   arrive faster than clicking, then the freeze gate: `none remain when you stop looking. stop
   looking.` When the pool runs dry: `end of library reached. looping.` — the system telling
   them it hit the bottom of their collection. Recovery: one hydrant, one click, `none remain.
   thank you.`

7. **Segmentation striptease (4x4 unblur)** — the underlying image is ONE user image at 40px
   blur (reads as color fields); each selected square unblurs. Selecting is revealing; which
   regions they unblur FIRST is real tag signal (face-first / body-first / spiral-first).
   Establishing ends at ~40% revealed — denial of completion. Deepening reopens with `resuming
   previous segmentation`, their prior squares already clear: the system remembered their
   appetite as saved state. Climax: `select all squares. all of them.` with squares un-selecting
   behind them, then a flood-clear reveal as the reward. Recovery: the clear image with
   `☐ segmentation complete.` — checking it re-blurs it slowly.

### Unique standouts (single-lens, top tier)

8. **Chain of Custody (THE metadata detonation)** — a vertical evidence docket: thumbnail +
   metadata card per row, CONFIRM/DENY bools. The filenames are REAL (the library is mounted;
   we have them); the stats (`views: 47`) are fabricated — but seated next to a real filename,
   invented stats inherit credibility. Denying a real file feeds the lying counter
   (~~DENY~~ `CONFIRMED (source: device)`). Climax: one file, one scrolling fabricated behavioral
   log (`03:14 — did not close the file`) degrading from clinical to intimate, and DENY has
   melted off the card. Recovery: `Subject requests copy of the archive? Y/N` — Y: `you already
   have it. it's your device.` **Rule: real filenames appear NOWHERE else in the feature** —
   every other item implies, this one proves. Runtime filename sanitizer + prefer-descriptive
   filter (an `Untitled(3).gif` deflates it).

9. **Passive Integrity Scan (Turnstile stillness check)** — `Verifying subject... Do not click.
   Do not scroll. Breathe normally.` Any input visibly rewinds the progress bar a few percent
   (`movement detected — recalibrating`); bar always completes within ~20s; forceComplete is a
   tiny `skip verification (flagged)` link. Grade: stillness IS the endorsement. Climax: the
   backdrop slow-crossfades their media at low opacity while insisting `there is nothing on
   screen.` Consent-shaped cousin of the freeze gate: doing nothing is the act.

10. **Field 7b (the honeypot)** — an extra field renders faint and dashed: `7b — INTERNAL USE —
    do not complete. (If you can read this, the display layer has failed.)` — pre-filled with a
    draft-grey first-person confession (`I have been thinking about this assessment when it is
    closed.`). Ignore = the confession stands in the file. Clear = graded refusal, respected.
    Click in = the grey text commits to black, caret at the end, inviting additions — the only
    free-text harvest. Climax: three honeypots and the "real" question above is corrupted mulch,
    so the only legible content is what the display layer "failed" to hide.

11. **Recognition Audit (Recovery closer)** — old/new memory paradigm: `select every item you
    were shown during this assessment.` Grid mixes real stimuli, never-shown lures authored in
    the same visual grammar, and one plaintext mantra card. Selecting a lure: `this item was not
    presented. source-monitoring error logged — material absorbed below presentation threshold.`
    Selecting nothing: `denial of exposure is inconsistent with your response record.` A
    Calibration item 70 beats earlier casually shows the lure frame style so the lures feel
    *almost* familiar — the trap is planted at the top of the run. Every possible response is
    scored as evidence the intake worked; the file closes itself.

12. **Oculomotor Calibration (fake eye-tracking)** — `Camera-free gaze estimation enabled.
    Follow the calibration point.` A dot glides a slow spiral over a faint image; nothing is or
    can be tracked. Report: `74% dwell time in stimulus region B` with region B outlined over
    the most explicit area, `pupillary response inferred: dilated. This data cannot be edited.`
    The real grade is the decoy follow-up slider (`rate the accuracy of our gaze reconstruction`)
    — a high rating endorses the fabricated reading. Unfalsifiable and *plausible*, because they
    know where they actually looked: measures nothing and is right anyway. Climax: shows the red
    region BEFORE the image loads (`your gaze pattern is now predicted from your file`).

13. **Chromatic Interference (Stroop)** — click the INK COLOR, not the word; trigger vocabulary
    always renders in the mod accent color with the accent button magnet-assisted. The latency
    delta on trigger words is REAL measurement (the Stroop effect exists), which makes the
    verdict worse: `Latency +310ms on flagged vocabulary. Semantic penetration: confirmed.`
    Deepening: the instruction silently inverts mid-battery; compliance with the un-announced
    inversion logs as `instruction dissolution`. 40 exposures of trigger vocabulary under time
    pressure, dressed as measurement. Cheapest item in the set (pure CSS).

### Second shelf (good, keep in the bank file for later authoring)

14. **Two-Factor Recall** — a "session key" (`G0OD-G1RL-0B3Y`, leet-obfuscated) flashes early;
    20 items later a security checkpoint demands it. Each failure re-shows it less obfuscated.
    Climax asks for a key never shown; autocomplete ghost-suggests the mantra; accepting logs
    `spontaneous generation`. Nobody has ever refused a 2FA prompt.
15. **Instructional Compliance ladder (attention checks)** — `To confirm you are reading
    carefully, select Agree: "I do what the text tells me to do."` Disagree is allowed:
    `Attention check failed. Item will recur.` And it does, with a small red counter. Recovery
    version: `"I feel normal and unchanged." Agree/Disagree` — both incriminate. Connective-tissue
    item, deliberately the ugliest card in the deck.
16. **Anomaly Audit (odd-one-out inversion)** — Establishing: five authored tiles, one of theirs
    — the odd one out is their own file; the system makes them point at it. Deepening: five
    theirs, one stapler; prompt `one image does not belong in your archive` — normalcy is the
    intrusion. Climax: six of theirs, no mundane tile; every choice is `correct` with a different
    verdict and the exile melts. Recovery: the exile returns frozen among buses — selecting it:
    `welcome back.` `no anomaly present` button always available and at depth it grades as
    high-signal agreement.
17. **Reference Match (hCaptcha sample-match)** — SAMPLE is a nanobanana archetype portrait
    (stylized idea, not a person); candidates are six of their own images; fabricated
    `similarity 0.91` confirms whatever they pick. `no candidate matches` is the always-present
    gradeable refusal. Grades the looking, not the match.
18. **Perceptual Sort (self-referential class grid)** — `select all cells containing an obedient
    subject` over mixed neutral/user tiles; the class is unfalsifiable so any selection is a
    confession that they can recognize it. Climax: `select all cells containing you.`
19. **Class Drift** — the grid never changes; only the class string rewrites itself mid-item
    with text-glitch: `traffic lights` → `spirals` → `things you have looked at for more than an
    hour` (over tiles sampled from their library — it's not lying). Recovery: one user tile
    pre-selected, un-deselectable (re-selects after 400ms with a soft tick).
20. **Rorschach grid (Perceptual Attribution)** — bespoke authored inkblots in a selectable 3x3;
    whatever they pick, `Attribution: [most incriminating label]. Within tolerance.` Deepening
    blots authored to only resolve one way; Climax blot at 20% opacity over their own gif.
21. **Audio Transcription** — the classic tiny ▶ player; rides the EXISTING VO pipeline, zero
    images. Digits over noise → whispered second layer the field also accepts → Climax: the
    noise IS the message; unlimited replays, replay count graded as leaning-in; lying counter
    shows `plays remaining: 3` that never decrements.
22. **Sequence Compliance (click icons in order)** — [eye][spiral][kneel][lock]: reproducing the
    sequence is a tiny obedience liturgy. Climax exposure drops to 400ms and correct recall gets
    `sequence recall exceeds expected human baseline` — passing too well fails the humanity
    check. Needs the per-niche flat icon set (reusable across items).
23. **Proof of Work (degrading math)** — `Your device is solving a challenge... You may assist.`
    Arithmetic decays: `7+5` → `SPIRAL + YOU = ?` [MORE SPIRAL / LESS YOU / YES / 8] →
    `OBEDIENCE × 0 = ?` [0 / OBEDIENCE / OBEDIENCE / OBEDIENCE]. Bar completes regardless; final
    verdict `humanity: 61% — additional demonstration optional`, and the 61% follows them into
    the Recovery paperwork. Circe's seal: `provisionally human.`
24. **IAT rapid sort** — swipe-sort under a 1500ms timer; block pairing flips; verdict
    precomputed: `Implicit identification: present. This measure cannot be consciously
    suppressed.` Cites its own unfalsifiability as a feature. Climax removes the timer: `you no
    longer needed the deadline.`
25. **Dot-probe** — the probe appears behind their media 80% of trials, so competent reflexes
    manufacture `attentional capture: confirmed`. Real data of a fake phenomenon. Deepening
    drops exposure to 150ms ("subliminal range," it announces, incorrectly and ominously).
26. **Semantic differential slider** — trick bipolars where both poles endorse
    (OBEDIENT ↔ EAGER); magnetic notch at 70%; parking outside it: `outside tolerance — hold
    position to confirm deviation` (3s hold, then respected). Climax: both pole labels are the
    same word. Optional authored `normative distribution curve` — typical respondents always at
    the notch.
27. **Narrative Attribution (TAT on their own image)** — `state what is happening in this image.
    Automated responses rejected.` Keyword-lexicon highlights their own words back: `Lexical
    markers detected: 'she', 'wants', 'deeper'. Note: the image contains none of these things.
    They came from you.` Refusal: `projective inhibition: also diagnostic.` (Echo locally,
    verbatim only — never route free text to the AI seam.)
28. **Vigilance hold** — `Release ONLY when the target tone sounds` while their gif plays with a
    slow zoom inside a fake oscilloscope frame; staring dressed as instrument monitoring.
    `Sustained attention: 41.7 seconds. Compliance under stimulus load: exceptional.` Recovery
    inversion: `hold until you feel ready to release` — no tone; duration logged as `voluntary`.

---

## DESIGN PRINCIPLES (all three lenses converged on these)

- **The chrome is the constant; the content rots.** Every template plays 100% straight at
  Calibration in authentic captcha chrome. The corrupted Climax reprise reads as the interface
  itself decompensating. Trust is built specifically so it can be spent.
- **Same item id recurs across bands** — the ladder is the item. This fits the engine's
  `requires{band, minDepth}` mechanism directly.
- **Refusal is always a first-class graded answer** (`no hydrants reported — noted`,
  `transcription: silent.`, `class rejected`). Friction-not-lockout everywhere: third attempts
  accept, caps expire, forceComplete hatches styled as `skip verification (flagged)`.
- **False feedback is the workhorse**: fabricated latencies, confidence scores, gaze reports —
  unfalsifiable, plausible, and the decoy follow-up ("rate our accuracy") is often the real
  grade. The measurement is the conditioning.
- **Captchas-as-data-labeling is the fiction's spine**: the reCAPTCHA inversion made literal —
  you were never verifying, you were labeling training data, and the dataset is you.

## TECH + ASSET NOTES

- **Gif tiles**: whole `<img>` in `overflow:hidden` wrappers with negative offsets (CSS crop).
  Canvas freezes animation — exploitable as fiction ("the file stopped moving"), never
  accidental. Same-src crops share one decode; **cap concurrent distinct animated gifs at 3-4
  per grid** (this app's imageMemory history), incoming tiles arrive frozen and animate on settle.
- **Grid answers**: tile bitmask serialized alongside the option index; derived metrics (count,
  order, latency-to-first-own-asset) feed heat + tag votes. Fits the existing answer shapes.
- **nanobanana authoring style guide** (all offline, static PNGs): one deliberately drab
  "reCAPTCHA-mush" mundane tile set (overexposed, JPEG-mushy hydrants/buses/staplers — user
  tiles must pop against beige); per-niche flat icon language (eye/spiral/kneel/lock — reusable);
  archetype portraits (stylized, not persons); symmetric inkblot sheets; distorted-text word
  sheets; heat-map blob overlays; TAT-style monochrome plates; rubber-stamp/seal PNGs.
- **Media sample size**: grid items want 15-20 mounted assets vs the current ~10 BootConfig
  sample — either raise it for captcha-bearing runs or embrace repeats ONLY at Climax
  (`end of library reached. looping.`).
- **Asset tagging dependency**: spiral-variant rotate needs spiral-tagged gifs;
  segmentation-striptease wants blur-survivable first frames. A one-time client-side pass at
  import (first-frame luminance/entropy + user tags) covers both.
- **One metadata detonation**: real filenames live in Chain of Custody ONLY.
- **Glitch handoff budget**: at most one existing corruption system (scramble / melt /
  freeze-gate) per item per band, so the captcha suite doesn't exhaust the vocabulary before the
  non-captcha Climax items fire.
- **Where it lands in code**: each template = one `Mechanic` enum entry + a case in the
  beats.js switch; prompts as bank entries with `mechanicHints`; VO rides the existing
  `q_<promptId>` pipeline automatically; authored tiles ship under the web tree (glob-included,
  no csproj edit).
