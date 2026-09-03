# Emergency Exit - web primer

The four friction minigames behind Lockdown's huge EMERGENCY EXIT button. The WHY, the
host protocol and the verdict rules live in `Services/EmergencyExit/EMERGENCY_EXIT.md`
(read that first). This file is the web-side HOW: file map, shell API, mock mode, how
to add a game.

Served by `EmergencyExitHostService` (WebView2) as
`https://ccp.game/emergency-exit/index.html?game=<id>`; user media arrives as
`https://ccp.assets/images/...` URLs inside `init.assets.gifs`. Window ~960x640, tolerate
800x560+. No build step, no frameworks, no CDNs, no external fonts (Segoe UI stack).
Classic `<script>` files (not ES modules) so `file://` authoring works.

## File map

| File | Owner | What |
|---|---|---|
| `index.html` | web A | reads `?game=`, loads `shared/theme.css`, `shared/bridge.js`, `shared/shell.js`, then `games/<id>.js` + optional `games/<id>.css` |
| `shared/theme.css` | web A | tokens (`--ee-bg #1A1A2E`, `--ee-card #252542`, `--ee-crimson #DC143C`, `--ee-ember #FF8A5C`, `--ee-pink #FF69B4`, text/dim/mute), HUD/say/stage/outro chrome, `.ee-card`, `.ee-btn`, `.ee-glitch`, `.ee-ember-flash`, photosafe counterparts |
| `shared/bridge.js` | web A | `EE.bridge` postMessage wrapper + mock host |
| `shared/shell.js` | web A | `EE.registerGame`, HUD, say strip, close X + Esc, verdict outro + 3-2-1, remark pools for all four games |
| `games/labyrinth.js/.css` | web A | trace the exit (canvas maze, glitching exit, 25 s) |
| `games/password.js/.css` | web A | the password game (5 stacking rules) |
| `games/jigsaw.js/.css` | web B | 3x3 tile puzzle on the user's GIFs |
| `games/captcha.js/.css`, `games/captcha-ads.js` | web B | "confirm you are NOT a {honorific}" press-and-hold + fake feature popups |

Everything under `Resources/web/**` ships via the csproj `Content` glob (`CopyToOutputDirectory
PreserveNewest`); nothing to add when you create a file here.

## Lifecycle

```
page loads -> shell posts {type:"ready"}
host posts  {type:"init", game, attempt, restarts, remainingSec, durationSec, photosafe, lang, mod, assets}
shell: photosafe class on <html>, HUD text, seeded rng, then game.start(init, api); posts game-started
game:  ... api.finish("completed"|"failed", meta) -> shell posts game-finished, locks the stage
host:  applies Deactivate()/RestartTimer() and posts {type:"verdict", outcome:"escape"|"sendback"}
shell: game.destroy(); outro card (title + remark line with {honorific}/{subject}) -> 3-2-1 -> posts outro-done
host:  closes the window (or after its 8 s failsafe)
```

Esc / the HUD X: before `finish` = `{type:"quit"}` (host closes, lockdown keeps running);
between `finish` and the verdict = ignored; during the outro = skip straight to `outro-done`.
Esc is never suppressed (house rule #680).

## Shell API (`games/<id>.js`)

```js
EE.registerGame({
  id: 'labyrinth',
  start(init, api) { /* build into api.mount, run */ },
  destroy() { /* stop timers / raf; may leave DOM */ }
});
```

| `api.*` | |
|---|---|
| `finish(result, meta)` | `"completed"` or `"failed"`; posts `game-finished` once, locks the stage, waits for the verdict |
| `hud.set(text)` | HUD text (default `attempt #N  ·  sent back Mx`); `{honorific}`/`{subject}` substituted |
| `hud.timer(sec)` | HUD countdown readout; `null` hides; <= 5 s gets the low (crimson pulse) style |
| `say(text)` | warden line strip under the HUD (substituted) |
| `rng()` | `[0,1)` seeded from game + attempt + restarts + duration (mock: `&seed=` too) |
| `photosafe` `mod` `assets` `lang` `init` | straight from init (`assets.gifs` may be empty) |
| `mount` | the stage `<main>` to build into (flex, centred, ~960x554 at design size) |
| `remainingSec()` | lockdown seconds left, ticking locally from `init.remainingSec` (shown in the HUD too) |
| `glitch(el, ms)` | adds `.ee-glitch` (chromatic jitter; photosafe = soft ember tint) |
| `fill(text)` | `{honorific}`/`{subject}` substitution |
| `log(msg)` | `[game] msg` to console + host log frame |

The shell owns the verdict and the outro; games never show a verdict themselves. A game
that "fails" (timer out, locked in) calls `finish("failed")`; the host maps failed to
sendback. The roll for completed games lives in C#, never here.

Photosafe: `init.photosafe` => `<html class="ee-photosafe">`. No strobes, no hard flicker,
static bursts become soft tints/slides. Every keyframe you add that blinks needs an
`.ee-photosafe` counterpart (see theme.css for the pattern) and your canvas code must
check `api.photosafe`.

## Mock mode (authoring without the app)

```
index.html?game=labyrinth&mock=1
```
`shared/bridge.js` fabricates `init` (mod `builtin-bambisleep` / "Bambi Sleep" / "good girl"
/ "Bambi", three inline-SVG gradient "gifs", attempt 3, restarts 1, 412 s of 600 s left)
60 ms after `ready`, and answers `game-finished` with a random verdict after 400 ms
(labyrinth / failed = sendback, else 67/33). Knobs: `&verdict=escape|sendback`,
`&photosafe=1`, `&remaining=<sec>`, `&duration=<sec>`, `&attempt=<n>`, `&restarts=<n>`,
`&seed=<n>`, `&gifs=0`, `&lang=xx`. Everything logs to the console as `[EE] ...`.
Any non-hosted page (no `window.chrome.webview`) is mock automatically.

Headless check (Edge):
```
msedge --headless=new --disable-gpu --window-size=960,640 --virtual-time-budget=3000 \
  --screenshot=ee-labyrinth.png "file:///.../emergency-exit/index.html?game=labyrinth&mock=1"
msedge --headless=new --disable-gpu --dump-dom "file:///.../index.html?game=password&mock=1"
```
`tests/` is not shipped; the logic tests for the two web-A games live in the author's
scratchpad and exercise `EE.util`, the maze carver (`EE.games.labyrinth._test`) and the
rule validators (`EE.games.password._test`) with a tiny fake DOM under node.

## Adding a game

1. Create `games/<id>.js` (classic script, wrap in an IIFE, call `EE.registerGame` once) and
   optionally `games/<id>.css` (use the `--ee-*` tokens; prefix your classes `<id>-`).
2. Build everything inside `api.mount`; `start` must be synchronous and cheap (the loader
   hides right after it returns). Stop every timer / raf in `destroy()`.
3. Add the id to the `GAMES` allowlist in `index.html`, remark pools (4+ per outcome, warden
   voice, tease the act of leaving, never the person, no em-dashes; an optional `failed` pool
   is used instead of `sendback` when the game called `finish("failed")`) to `REMARKS` in
   `shared/shell.js`, and the row in EMERGENCY_EXIT.md (+ the C# `SendBackChance`).
4. Verify in mock mode headlessly (screenshot + `--dump-dom` for JS errors) and, where the
   game has logic worth a test, under node with a fake DOM.
