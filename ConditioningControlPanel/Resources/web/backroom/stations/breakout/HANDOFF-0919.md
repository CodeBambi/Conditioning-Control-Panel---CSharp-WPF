# Breakout handoff 2026-09-19: the Grey World pass

Owner brainstorm after the phone playtests of head cc67304d1. Decisions locked by survey.
Master design doc (section "Handoff: the Grey World pass"):
https://claude.ai/code/artifact/eeaed99e-b637-4500-a1c5-e9d5f10bd9d0

## Where the live build stands (facts an agent must know)

- Starts in COLOUR at saturation 0.15 (`createGame` default, station.js `?sat`). No grey opening.
- BREAKOUT at `breakoutN` 12 grey hits, specials +3 (`plus` in game.js onBrick). Same N every relapse.
- The OLD SELF drifter in render.js fires only on the `split` event. On `breakout` the ball shatters
  into particles + shockwave. Owner never sees an old ball leave; that is why.
- RUNG_AT: ten rungs at 0.10. Gains: brick +0.012, bubble hit +0.03, orbit +0.05, wall +0.10.
  One 60-brick wall is ~+0.82, so the ladder passes in one wall. DO NOT rescale yet (ladder parked).
- WORD_BRICK_P 1/6 (word-fx.js). Voice: station.js `say()`, VOICE_LEVEL 0.5, SAY_GAP_S 1.1,
  speaks on every word brick in COLOUR.
- Gif brick (15% roll) pops out, bursts into a bubble collider, fades after 3 hits, max 3 live.
  Nothing flies toward the viewer from a bubble. Toward-the-viewer (`gif_from`) and fullscreen
  (`gif_full`, 1.5 s) already exist as host/shim effects.
- In GREY nothing spawns (spec line 53).

## Decisions

| Topic | Decision |
| --- | --- |
| Opening | Every sit-down starts in GREY. First breakout at 20 hits, specials still +3. |
| Relapse cost | Counter per sit-down: 20, 15, 11, 8, 6, floor 5. savedSat still restores on breakout. |
| Grey brick identity | Specials drawn dull but denser: darker fill, hard bevel, no face/word/spiral. Plain bricks flat, chalky. Render-only. |
| Grey drops | Specials only (gif, word, spiral, split, jackpot). Plain bricks just break. |
| Grey drop behaviour | Plain drop: the sad picture slides out of the brick and sinks off the bottom. No bubble, no collider. |
| Grey pool niches | Liminal spaces + dead malls; office + spreadsheets; weather + commute (rainy bus stops, traffic, overcast suburbs, packed trains). |
| Grey pool treatment | Desaturate (more when the source is bright), lower contrast, slight vignette, optional 1-2 px blur, slow downward drift. No faces, nothing cute. |
| Breakout moment | Ball splits: colour ball keeps playing, grey shell peels off as a full-size OLD SELF ball, floats up and out over 3-4 s with a wobble, drawn ABOVE the flash. Reuse the drifter, scale up, trigger from `breakout`. |
| Bubble tiers | Replace the 15% gif roll: 70% tier 1, 20% tier 2, 10% tier 3. Total picture bricks unchanged. |
| Tier 1 | One bubble, one hit pops it, picture flies toward the viewer faded (`gif_from`). No lingering collider. |
| Tier 2 | Bubble in a bubble, two hits. First sheds the outer skin with a spark, second releases TWO different pictures at once. |
| Tier 3 | Three skins, three hits, then fullscreen (`gif_full`) with fade in/out, hold longer than 1.5 s. |
| Bubble rules | Nested bubbles never time out. Cap 3 live. Sat reward by tier +0.03 / +0.06 / +0.10. In GREY these bricks plain-drop a sad picture. |
| Word bricks | WORD_BRICK_P 1/6 -> 1/8. |
| Voice | Speak on one word brick in two, SAY_GAP_S 2.5, VOICE_LEVEL 0.35. Near-ball flashes silent. Next lever: speak only on level-ups. |
| 8-level ladder | PARKED. Needs more brainstorming. |

## Build order

1. Grey opening at 20, shrinking relapse counter, breakout drift-out.
2. Noise dial (WORD_BRICK_P, VOICE_LEVEL, SAY_GAP_S, one-in-two gate).
3. Grey brick identity + grey pool with the desaturate pass.
4. Nested bubbles: tier 1, then 2 and 3.
5. 8-level ladder once the brainstorm lands.

## Still open

- 8-level ladder: per-level unlocks (one visual + one audio) and new saturation gains.
- Whether the grey pool needs its own Scrolller source check in api/clip before step 3.

## Working rules (unchanged)

VIBECODE: build, merge to main, no PR ceremony, report what to test. Work in C:/wt-breakout-web
(`git fetch && git reset --hard origin/main` first, push `origin HEAD:main` FROM THAT WORKTREE ONLY).
payloads.js/station.js are CRLF: patch via CRLF-aware node scripts. Tests from the backroom/ folder:
`node --test "stations/breakout/*.test.js" "stations/breakout/words/*.test.js"`.
Deploy = copy stations/breakout/* minus tests into the mirror, then `npx vercel deploy --prod --yes` there.
