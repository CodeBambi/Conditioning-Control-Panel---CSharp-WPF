# Endless prototype

Story retains its eight stages and finale. Endless generates a seeded sequence of boards from eleven recipe families. The first ten boards preserve their original seed sequence. The first board pairs the central spiral with two pendulum curtains. Every fourth board is a shorter breather. Difficulty is bounded rather than an unlimited speed or armour ramp.

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
- Colour effects stay off in grey. Portals remain structural and live in grey, including their quiet transit sound. Missing a ball still uses the existing recovery cycle, with no game over.

## Verification

The full Breakout suite passed 422 tests, including swept contact from either face and the rim, oversized picture bubbles, speed-preserving exits, and 200 generator seeds with ball and demolition-weight emergence from both faces of each small portal. New tests cover 200 generator seeds, exact restoration, bounded geometry, native hinge release through spiral capture and launch into a second hinge, simultaneous ordinary-ball capture, clear transitions beyond board eight, and checkpoint validation. Browser checks covered the opening board, mode selection, Save and menu, and Continue.

This is an isolated playable prototype. No production deployment, release bump or server change is included. Play feel, long-session variety and the mix balance remain the purpose of the owner's test.

## Watchable gameplay

Open showcase.html on the same local server for three looping gameplay highlights with an automatic paddle, optional sound, replay, and direct play links. The showcase uses the actual engine and renderer, advances to highlights through ordinary paddle inputs, and never writes checkpoints. The current portal showcase uses seed 2709: Demolition express (board 14) starts at 4 seconds with launch position .30, Spiral delivery (board 13) at 12 seconds, and Backdoor (board 11) at the beginning. With the rally ramp, normal paddle simulation produces a demolition transit at 5.40 seconds, spiral capture at 5.97 and launch at 7.03. No artificial brick breaks, ball relocation, or noLose is used.

## Portal boards

A blue and orange portal join two corridors. Both ends accept hits on either face, including the rim. The first new recipes are Backdoor (board 11), Spiral delivery (board 13) and Demolition express (board 14); later decks shuffle these among the existing recipes, keeping every fourth board a breather.

Each portal is 130 pixels long and 32 pixels deep on the 1280x720 court. Balls and weights have clear exits on both faces. Bulky picture bubbles can travel through a smaller mouth; their destination uses the normal open-space placement when surrounding bricks leave a narrow pocket. Backdoor turns an ordinary paddle shot into a route behind armour. Spiral delivery has authored GIF, spiral and power-up cargo; only the tagged falling payload seeds may wait up to 0.9 seconds to reach its lower mouth. Demolition express releases one exposed hinge into a portal route that meets the persistent dome.

Immediate play links use `play.html?endless=1&seed=2709&board=11&sat=.85`, changing board to 13 or 14 for the other recipes. `showcase.html` displays the same engine with an automatic paddle and direct links to play each scene.

## Demo, full game and rally pace

The desktop has two separate launcher and Play cards. Breakout Demo is free, including signed-out play, and ends after the third Story level with replay and menu actions. Breakout uses the normal Tier 2 entitlement for all eight Story levels and Endless. Both cards carry NEW tags; Goon Game is beside them on Play.

The native host projects `breakout: {storyLimit, endless, demo}` in init and settings frames. A missing or malformed hosted projection falls back to the demo. An explicit Demo door stays a demo even for a patron. Losing full access returns an active game to the demo menu, retaining an Endless checkpoint. Local browser previews can use `play.html?demo=1`; the desktop never grants access from that query. The separately hosted website must provide the same trusted projection before syncing this version; this client change does not deploy or alter its server gate.

While a rally stays alive, base ball speed gains 0.4% per active simulation second, capped at 30% after 75 seconds. Paddle contacts, portals, power-ups and board changes keep the buildup. Losing the final live ball resets it. Pauses, held balls and hit freezes do not advance it; slow effects retain their existing behavior.

Paddle volleys use the shared two-sided portal transport, including grazing rim contacts. Regression coverage checks the real emitted shots, their rotated exit velocity and target damage. The hinge-weight trail now tapers and fades, stays attached to its head and never joins across a teleport.
