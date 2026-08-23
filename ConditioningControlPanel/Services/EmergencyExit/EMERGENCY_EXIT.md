# Emergency Exit - the friction door of Lockdown

> Read this before touching `Services/EmergencyExit/`, `Resources/web/emergency-exit/`, the Lockdown
> active panel, or anything that rewinds the lockdown clock. Sibling primer: `Services/Possession/POSSESSION.md`.

## What it is (owner brief, 2026-08-23)

While a lockdown runs, the user may "End the lockdown" at any time:

1. A **clickable badge** sits somewhere clean in the main UI while lockdown is active; clicking it
   navigates to the Lockdown page. The **premium rail's Lockdown chip** does the same while a
   lockdown is active (instead of its +/- / activate behaviour).
2. The Lockdown active panel shows a **comically huge "EMERGENCY EXIT" button** (real graphics, not a
   text button). Pressing it raises tripwire `EscapeKinds.EmergencyExit` and opens **one of four
   random minigames** in a WebView2 window owned by the main window.
3. Every game ends in a **verdict**:
   - `escape`  -> `LockdownService.Deactivate()` (the real exit, reassembly runs as usual).
   - `sendback` -> a funny in-character remark ("yay! you are soo good at quitting! you always come
     back tho... hihi") and `LockdownService.RestartTimer(game)` - the lockdown restarts with its
     **FULL** duration. Escape counters are kept (friction escalates), safeties untouched.
   - `abandon` (user closes the game window / Esc) -> nothing changes, the timer keeps running.
4. The user may retry as often as they like. It is friction, not a wall - the secret phrase exit
   (click the timer digits 5x, type `let me out`) stays as the real safety valve.

### The four games (web, `Resources/web/emergency-exit/`)

| id | Game | Mechanic | Verdict when COMPLETED |
|----|------|----------|------------------------|
| `labyrinth` | trace the exit | drag a path from entry to exit through a small maze without touching walls under a time limit; when the player is slow the exit **glitches and shifts** (maze reshuffles / exit relocates) and eventually locks them in | **always `sendback`** (the gag: you are so good at quitting, you always come back) |
| `password` | the password game | 5 rounds of stacking, increasingly absurd rules (must contain a number / digits sum to N / include the mod's name / today's weekday / the minutes left / a roman numeral / an emoji / no letter e ...) | `escape` 67% / `sendback` 33% |
| `jigsaw` | rearrange the picture | 3x3 tile puzzle skinned with one of the user's own GIFs (`https://ccp.assets/images/...`); mid-game the picture **glitches and swaps to a different GIF** (and maybe two tiles swap) | `escape` 67% / `sendback` 33% |
| `captcha` | "Confirm you are NOT a good girl" (mod-themed honorific) | press-and-hold the verify box ~4 s to fill the ring while **fake in-page popups advertising CCP features** steal the hold (the user must close them; progress decays while not held). The player must see 3-4 popups before the check can fill. Copy shames them for leaving | `escape` 67% / `sendback` 33% |

A `failed` game (timer ran out, locked in, gave up inside the game) is always `sendback`. The roll
lives in C# (`EmergencyExitHostService.SendBackChance(game)`), never in JS.

## Protocol (page <-> host), JSON over WebView2 `postMessage`

Page -> host (`window.chrome.webview.postMessage(obj)`):

```
{ type: "ready" }                                   // DOM ready; host answers with init
{ type: "game-started", game }
{ type: "game-finished", game, result: "completed" | "failed", elapsedMs, meta: {...} }
{ type: "outro-done", outcome }                     // outro card played; host may close the window
{ type: "quit" }                                    // user abandoned (close X / Esc inside page)
{ type: "log", level: "info"|"warn", msg }
```

Host -> page (`CoreWebView2.PostWebMessageAsJson`):

```
{ type: "init",
  game: "labyrinth"|"password"|"jigsaw"|"captcha",
  attempt: 3,                  // how many times Emergency Exit was opened THIS lockdown (1-based)
  restarts: 1,                 // LockdownService.RestartCount
  remainingSec: 412, durationSec: 600,
  photosafe: false,            // LockdownPhotosafe -> no strobes/hard flicker in games
  lang: "en",
  mod: { id: "builtin-bambisleep", name: "Bambi Sleep", honorific: "good girl", subject: "Bambi" },
  assets: { gifs: ["https://ccp.assets/images/foo.gif", ...] }   // up to 12, shuffled; may be empty
}
{ type: "verdict", outcome: "escape" | "sendback" }
{ type: "close" }              // host is about to close the window (courtesy)
```

Rules:
- The HOST is authoritative: it applies `Deactivate()` / `RestartTimer()` the moment it sends the
  verdict, and closes the window on `outro-done` or after a 8 s failsafe.
- The page never loads remote resources. Origins: `https://ccp.game/emergency-exit/...` (the page,
  Deny), `https://ccp.assets/...` (user media, Allow). Same mapping shape as `ArcademyHostService`.
- Esc inside the page = `quit` (house rule #680: never suppress bare Esc). The host window is
  windowed, owned by MainWindow (`ChaosWebViewHost.Options.OwnedByMainWindow = true`), ~960x640,
  centred on the main window, title "Emergency Exit".
- Mock mode for authoring without the app: `index.html?game=<id>&mock=1` makes `shared/bridge.js`
  fabricate `init` (placeholder mod + three CSS-gradient "gifs") and answer `game-finished` with a
  random verdict after 400 ms. Authors verify in Edge/Chrome (`msedge --headless --screenshot`).

### Shell API (`shared/shell.js`, owned by the labyrinth/password author)

```
EE.registerGame({ id, start(init, api), destroy() })   // one call per game file
api.finish("completed"|"failed", meta)                 // -> posts game-finished, shell shows outro on verdict
api.hud.set(text) / api.hud.timer(sec)                 // shared HUD row ("EMERGENCY EXIT - attempt #3")
api.say(text)                                          // warden line strip under the HUD
api.rng()                                              // seeded per init (attempt+restarts) for reproducible glitches
api.photosafe, api.mod, api.assets, api.lang
```
The shell owns: HUD, the close X + Esc (-> quit), the outro card (remark line per outcome per
game, with `{honorific}` / `{subject}` substitution, then "back to lockdown" 3-2-1 countdown, then
`outro-done`), theme tokens (`shared/theme.css`: bg `#1A1A2E`, card `#252542`, crimson `#DC143C`,
ember `#FF8A5C`, pink `#FF69B4`; Segoe UI; no external fonts/CDNs). Games live in
`games/<id>.js` + optional `games/<id>.css`, loaded by `index.html` from `?game=`.

Remark lines: per game, per outcome, 4+ variants each, in the warden's voice, no em-dashes, never
mean about the person - tease the ACT of leaving. Never use the Windows username or any real name
other than `mod.subject`.

## Ownership (parallel authors, one worktree)

| Owner | Files |
|---|---|
| Fable web A | `Resources/web/emergency-exit/index.html`, `shared/*`, `games/labyrinth.*`, `games/password.*`, `README.md` (web primer) |
| Fable web B | `games/jigsaw.*`, `games/captcha.*`, `games/captcha-ads.js` (feature ad copy) |
| Opus host | `Services/EmergencyExit/EmergencyExitHostService.cs` (+ any helper there), `App.xaml.cs` wiring (static `App.EmergencyExit`), `Services/Companion/BarkService.cs` new `NotifyEmergencyExit*` wrappers + bark rules in the 3 packs |
| Opus UI | `Views/Tabs/LockdownTabView.xaml(.cs)` (huge button + rung readout fix), `MainWindow/MainWindow.Lab.cs`, `MainWindow/MainWindow.PremiumRail.cs` (chip -> navigate), `MainWindow/MainWindow.xaml` (badge), `Resources/lockdown/*.png` art |

Build discipline for EVERY C# author in `C:\wt-ccp-possession`: before `dotnet build`, take the
mutex `mkdir C:\wt-ccp-possession\.buildlock` (retry every 20 s while it exists, give up after 10
min and report), release with `rmdir` when the build ends - even on failure. Never `git reset`,
`git checkout -- .`, `git stash` or commit; the coordinator integrates. Localization keys go into
`loc-additions/<owner>.json` as `{ "en": {k:v}, "de": {...}, ... all 9 languages }` - do NOT edit
`Localization/Languages/*.json` directly (nine authors would race on them).

## Feature ads (captcha popups) - allowed list

Flash, Brain Drain, Mind Wipe, Deeper, Down the Rabbit Hole, Just Drop, Bubbles, Velvet Vault,
Lock Card, Takeover, Awareness, Haptics, Blink Trainer, FYP feed, Sessions, Programs, Companion.
NOT the Arcademy (ships dark). Copy is in-character crimson infomercial, obviously themed, never
Windows chrome (POSSESSION.md "fake dialogs are obviously themed").
