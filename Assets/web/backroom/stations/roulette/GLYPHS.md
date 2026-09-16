# Pocket glyphs - the wheel tells you what it will do

Owner brief (2026-09-15): faded glyphs on the inner ring where the ball lands, one effect each, intuitive.

## The pick: four glyphs, one effect each, by the pocket number

| Glyph | Reads as | Effect (section 4 id) | Pockets |
|---|---|---|---|
| spiral | "it spins you" | `fx.spiral_brief` (1.5 s spiral) | 1, 5, 9 ... 33 |
| eye | "it shows you pictures" | `fx.gif_burst` (a flash burst) | 2, 6, 10 ... 34 |
| bubble | "it puts words in you" | `fx.sub_pair` (two words, a short spiral) | 3, 7, 11 ... 35 |
| drop | "it melts you" | `fx.melt` (the brain-drain melt, 6 s) | 4, 8, 12 ... 36 |
| (none) | the house pocket does nothing for you | nothing | 0 |

`glyphFor(n) = ring[(n - 1) % 4]` for 1..36, null for 0 (glyphs.js, pure). Page-side only: the number is
the key, so the assignment is the same on every wheel order the server sends, it never reads the tape, the
payout or the outcome, and the server has no idea it exists. The European order scatters the four kinds
round the rim, so no quarter of the wheel "belongs" to one effect.

## Laws

- Law I: the ring is static and identical every spin. Nothing brightens until the ball is in a pocket the
  server named; the page only animates onto it. A faded glyph shows what a pocket WOULD do, never which one.
- Law VIII: untouched; the press still kicks the rotor before any answer.
- Law X: the landed glyph goes hot on the settle frame (bowl.js's `landed` frame, the same one station.js
  thuds on) and its effect fires on that frame through the existing `beat()` path (`beat('glyph', {pocket})`).

## How it sits with the #1297 callouts

The glyph is WHAT the pocket does; the callout is HOW MUCH you won. A landing now fires the pocket's glyph
effect (win or lose, so the wheel is honest: a miss on the drop pocket still melts you), then the paying
tiers keep their names and their moments (wash, pocket GIF, chips): Chips In, Straight Up, Spiral Wake,
Full Wake. To not double up, the generic fullscreen steps of `land.win` (sub_pair) and `land.straight`
(sub_cascade) are gone: the glyph IS that step now, and the callout still names the pay. The Wake tiers
keep their own signature on top (spiral_full, the jackpot hero), the streak keeps its storm, a near miss
keeps its brief spiral. Cooldowns stay per id (a drop landing twice inside 30 s melts once).

## The reveal

At rest every glyph is a cream engraving at 26% on the pocket's inner slope, just inside the ball's
footprint so it peeks out from under a resting ball. The lighthouse beam brushes them a little as it
passes (they are part of the wheel, not a HUD). On the settle frame the landed glyph snaps to mint at
full opacity with a 0.5 s scale pulse, and stays hot while the ball sits there. No bark, no marquee: the
effect itself is the caption, and the callout already owns the text slot at FX_DELAY_MS.

## Learning the language

Mostly by watching: the mark lights, the effect plays, four kinds, nine landings each in 37. For the
curious, one sentence under the Odds panel (`br_roulette_glyphs_note`, all nine locales) names the four.
No bark on first visit: the whole point is that the wheel explains itself.

## Rejected

- Six glyphs (heart, moon, sparkle): more kinds than the phone ring can tell apart at ~14 px, and the
  candidate effects (sub_single, spiral_full, jackpot) are already spoken for by launch, the Wake and Full.
- Glyph families with a pay tier picking the size (spiral -> brief / full / loom): elegant, but it is
  two keys to learn instead of one, and "one glyph, one effect" is the brief.
- Assigning by wheel position (i % 4) for a perfectly regular ring: the number is the thing a player
  remembers and bets on ("17 is an eye"), and the wheel order is the server's to change.
- Glyphs on the 2D canvas bowl (bowl.js): the seated 3D fixture is the shipped view; the canvas is the harness.
