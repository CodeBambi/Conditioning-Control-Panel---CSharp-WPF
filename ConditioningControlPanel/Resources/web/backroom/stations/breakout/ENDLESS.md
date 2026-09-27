# Endless prototype

Story retains its eight stages and finale. Endless generates a seeded sequence of boards from eight recipe families. The first board pairs the central spiral with two pendulum curtains. Every fourth board is a shorter breather. Difficulty is bounded rather than an unlimited speed or armour ramp.

## Play locally

From the checkout root, set SOUND_PORT=8947 and run:

    node ConditioningControlPanel/Resources/web/backroom/shared/sound/serve.mjs

Open http://127.0.0.1:8947/backroom/stations/breakout/play.html?endless=1

Pick Endless, then Play or Continue. New run chooses a fresh seed. Escape or P pauses. Save and menu stores the current board's boundary: Continue rebuilds that intact board with its starting colour and best combo. Storage is local to the browser. Menu browsing never replaces a checkpoint.

For a reproducible colour preview, append &seed=2709&board=1&sat=.85. Board is one-based. With seed 2709, board 1 is spiral plus hinges, board 3 is Tide plus hinges, board 6 is reforming words plus spiral. The existing &still query tests reduced motion. Preview links intentionally select the specified board instead of the saved run.

## Boundaries

- endless-layout.js owns a deterministic stream for geometry. game.js uses a separate per-board stream for prizes, armour and payloads, so earlier physics cannot change a restored board.
- Tide and Reform move only their tagged Endless pieces. Hinges and static pieces retain their own geometry and identities.
- Released pendulum balls can enter the persistent spiral, orbit, then launch toward a remaining hinge or nearby brick. The demolition orbit has its own slot; it never displaces an ordinary captured ball.
- Demolition flight stays above the paddle, has a finite lifetime, and permits at most two spiral captures. New boards clear old mechanism state.
- Geometry, object counts, trail lengths and title caches are bounded. Reduced motion retains the mechanic while removing decorative trails, bursts and shake.
- Colour effects and new audio cues stay off in grey. Missing a ball still uses the existing recovery cycle, with no game over.

## Verification

The full Breakout suite passed 380 tests. New tests cover 200 generator seeds, exact restoration, bounded geometry, native hinge release through spiral capture and launch into a second hinge, simultaneous ordinary-ball capture, clear transitions beyond board eight, and checkpoint validation. Browser checks covered the opening board, mode selection, Save and menu, and Continue.

This is an isolated playable prototype. No production deployment, release bump or server change is included. Play feel, long-session variety and the mix balance remain the purpose of the owner's test.

## Watchable gameplay

Open showcase.html on the same local server for three looping gameplay highlights with an automatic paddle, optional sound, replay, and direct play links. The showcase uses the actual engine and renderer, advances to highlights through ordinary paddle inputs, and never writes checkpoints. Slingshot garden starts at 30 seconds, Tidal chimes at 25 seconds, and Moving mantra at 3 seconds. The first two contain natural hinge releases; no artificial brick breaks, ball relocation, or noLose is used.
