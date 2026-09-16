# Piece by Piece - the front door

The out-of-game surfaces of the board: menu, lobby, past games, profile, the end card. One glass card
over the live board (`door/`), never a WPF window. This file is the flow spec and the click ledger; the
code cites it.

## The rule: two clicks to a game, three at the outside

Click 1 is always the strip on the Play wall (WPF, `PlayTabView.xaml`), which opens the page on the
MENU with the men already standing on the board behind it.

| path | clicks | what happens |
|---|---|---|
| Quick match (online) | **2** | strip, QUICK MATCH. You are put in the lobby as "looking"; when the server pairs you the match-found beat runs 3-2-1 and the board deals itself. No third click; a tap on the board during the count starts it now. |
| Play here (hotseat) | **2** | strip, PLAY HERE. Deals at once. |
| Join a specific player | **3** | strip, LOBBY, the JOIN pill on a name. The other side's accept is one tap on their card (PLAY). |
| Accept a challenge | **3** | strip, LOBBY, PLAY on the "x wants a game" line. An ignored ask ignores itself after 8 s. |
| Rematch | **1** | REMATCH on the end card (online: sides swap). |
| Watch a past game | **3** | strip, PAST GAMES, WATCH. |
| Profile | **2** | strip, PROFILE. |

Esc is back, then out: any screen -> menu -> `pbp:exit` (boot's own). A countdown, a replay and the
end card all answer Esc. Nothing modal, nothing held.

## The card

Same card every screen (`.door-card`, `min(440px, 92vw)`), so the eye learns one place. It arrives
once with REVEAL (340 ms `cubic-bezier(.2,1.5,.4,1)`); a screen swap inside it is a 160 ms crossfade
and the card stays put. The board under it keeps drifting (camera sway 0.3, Law III); the veil dims
the edges and leaves the middle readable. The HUD chips and the camera row park (`.parked`) while the
door is up and come back when the game deals.

**One BREATH per screen**: the primary button's pink glow (3600 ms, the HUD vignette's cadence). In the
lobby while looking there is no primary, so the waiting dot breathes instead. Nothing else loops.

Screens:

- **menu** - title, QUICK MATCH (primary), PLAY HERE, then lobby / past games / profile as links. The
  foot says how to leave and who you are at the board.
- **lobby** - "N at the board - you are visible as <name>", the incoming ask line when there is one,
  QUICK MATCH, then the list: name, how long they have waited, JOIN. Empty state is a sentence and the
  dot, never a blank. Hosted with no signed-in account it is one line, "sign in to play online", with
  QUICK MATCH off; a hosted page never gets the mock's invented room. The lobby is left when the game
  deals (`go()`), not when the found screen replaces the lobby screen: the pairing lands through the
  lobby's poll, and a lobby still entered mid-game would re-advertise the player.
- **found** - the JACKPOT-shaped beat, kept small: "matched with <name>" lands with THE THUD (scale
  2.1 -> 0.86 -> 1, 340 ms), the side chip, then 3-2-1 at 700 ms a step (CHIME LADDER: each number
  BOUNCEs in, `tick` under it), and the board deals. "tap to start now" is written under it.
- **games** - the shelf, newest first: opponent and when, the outcome word (win / loss / draw from the
  player's seat; "white won" when the seat is unknown), the move count, WATCH.
- **replay** - the card folds to a strip at the bottom so the board is the subject: to-start, back,
  the move in SAN, forward, play/pause, a scrubber. The last move's squares light up as they do live.
- **profile** - the name you show as (typed here in a plain browser; hosted, it is the account's
  display name off `pbp:identity` and the box is read-only, because that is the name the server
  lists), then games / wins / losses / draws / streak / taken / favourite piece / time at the board /
  rating. Rating reads **unrated** until the server rates; the door invents no number.
- **end** - waits 1400 ms so the board's own end beat lands first, then: the result line with THE THUD
  (a loss adds a SHIVER after it: sympathy, never silence), the tally line, `#door-recap` (empty, the
  XP and recap lane fills it), REMATCH (primary), BACK TO THE DOOR.

Reduced motion (`PBP.settings.reducedMotion` or the OS query) sets `.still`: every animation and
transition is cut, classes still land, so each screen simply is where it should be.

## What the door owns, and what it does not

- `door/door.js` - the screens, Esc, the countdown, the end card, the replay stepper.
- `door/store.js` - the shelf (`localStorage` `pbp-games`, 50 newest) and the profile counts read off it.
  A game is saved on `gameover` from `game.record()`: moves in SAN, plies, result, duration, captures,
  the seat (`me`) and the opponent's name.
- `net/lobby.js` - the ONE interface the door talks to (`enter / leave / list / onList / quickMatch /
  challenge / onChallenge / cancel`) and a mock behind it so every screen works with no host and no
  network. The server lane ships `net/lobbyServer.js` exporting `createServerLobby(opts)` with the same
  shape; `boot.js` prefers it when present, `?lobby=mock` forces the mock.
- `boot.js` deals the game (`startGame({ mode, match })`, which puts the hotseat back in the chair if
  an online seat was there (`game.switchBack()`), resets the referee and the clocks in place and emits
  `local` with the mode); `?hotseat=1`, `?auto=N` and `?door=0` deal at once so every existing shot
  still photographs a live board. `window.PBP.match` carries the match for the online lane; the door
  never runs a relay. BACK TO THE DOOR after an online game switches back the same way, so the menu
  board is a fresh position and not the mate.
- `hud.js` owns the two things an online seat can say that are not a move: RESIGN (asks once,
  "resign? yes / no") and OFFER DRAW, with his offer as an accept / decline line. Bottom right of the
  HUD, online only, hidden outright in a hotseat.
- The door does NOT: pay XP, rate anyone, keep games anywhere but the shelf, or touch the referee
  mid-game.

## Proof

`node smoke/door-shot.mjs --out DIR` serves the web root and photographs sixteen screens headless
through `smoke/shot.mjs` (`--eval` levers on `window.PBP.door.debug` and `window.PBP.lobby.debug`; a
fake transport through `net/api.js` for the online seat; `door.debug.host` for the hosted states),
failing on any console error.
