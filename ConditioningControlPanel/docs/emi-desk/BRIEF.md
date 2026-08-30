# EMI Desk - build brief (2026-08-29, owner-approved)

EMI (the Arcademy's pixel CRT mascot) leaves the campus and becomes an app-wide, summoned desktop
widget for the Conditioning Control Panel: a draggable, resizable launcher with a ring of feature cards,
a glass that shows media proposals, yes/no offers, and preset barks. No AI. The owner approved the
visual pitch (`reference/pitch-page-body.html` is the page text, `reference/pitch-demo.js` is the
interactive stage's script: a compact, working reference for the face renderer, chains, ring layout,
glass channel painters, offers, summon/dismiss). Read those two files before touching anything.

Worktree: `C:\wt-ccp-emidesk` on branch `feat/emi-desk` (off origin/main d3ac64f). Build with
`cd C:\wt-ccp-emidesk\ConditioningControlPanel; dotnet build` and keep it at zero errors. Do NOT
touch `C:\Projects\Conditioning-Control-Panel---CSharp-WPF` (the owner's dirty checkout).
Commit on `feat/emi-desk` when your chunk builds; never push, never open a PR, never merge.

## Locked decisions (owner calls, do not re-litigate)

1. **Native WPF.** EMI Desk is a WPF port, not a hosted WebView2. WebView2 cannot paint inside a
   layered (AllowsTransparency) window (`Services/Chaos/ChaosWebViewHost.cs`), and the parts worth
   reusing (face.js, chains.js, the small voice/asks logic) are cheaper to port than to bridge.
   Face = text kaomoji drawn in pink on the screen; chains = data. Port faithfully.
2. **Summoned, not always-on.** A small circle in the dashboard nav rail with EMI's live face:
   click = summon, click again = dismiss. A hover x on her body dismisses. Global hotkey chord
   **Ctrl+Alt+E** (default; user-rebindable via the same capture UI as PanicKey/PauseKey; must be a
   chord: the WH_KEYBOARD_LL pause/panic hook shadows bare keys). Check it does not clash with the
   Ctrl+Alt+G Quick Recal chord or the panic/pause defaults. No double-click dismiss (it would delay
   the ring click).

   > **AMENDED 2026-08-29 (wave 3, owner):** the LEFT CLICK PATS HER, anywhere on her body. The ring
   > moved to the RIGHT CLICK plus a hover "cards" glyph top-left. See decision 5 and
   > `docs/primers/EMI_DESK_PRIMER.md` 13.
3. **Intro / outro ~1s.** Summon: pixel smoke bomb + sparkles, then CRT power-on (scale 0.02 ->
   flat line -> full, 4 steps), then the `wake` chain. Dismiss: `wink` chain, CRT power-off, sparkle
   scatter. `pitch-demo.js` `summon()` / `dismiss()` / `smoke()` are the reference.
4. **Mute the avatar while EMI is out.** Setting `EmiDeskMuteAvatar` (default ON) lives in the summon
   settings. While EMI is out and the switch is on, the AI avatar tube neither barks, bubbles nor
   speaks (BarkService + tube speak paths honour `App.EmiDesk.IsOut && MuteAvatar`). If Takeover, She's
   Listening, an AI chat, Awareness or Remote emotes are LIVE at summon time, prompt once per session:
   "Mute / Keep the avatar / Don't ask again" (persist "don't ask" as `EmiDeskMuteDontAsk`). Default
   answer when the prompt is dismissed = keep the avatar. Arbiter when NOT muted: avatar owns voice,
   EMI owns screen: while a tube bubble is live EMI holds to blink-only faces (+20s tail); an EMI reveal
   or fired effect counts against BarkService's min-gap.
5. **The ring.** Right-click her body (wave 3; it was the left click, which now pats her), or
   left-click the six-dot cards glyph that fades in top-left on hover: 6 cards fan around her (away from screen edges), each a 76x58 card
   with the feature's dashboard art (`Resources/features/*.png` where one exists, else a hue tile) and
   the name. Left-click a card = open the feature directly (tab / studio module / host launch). Right-click
   (or a pin glyph on hover) = pin; pinned cards stay in their slot; the rest are filled by the suggester:
   score = sum of opens decayed with a 7-day half-life, gate-aware (premium/lab cards the user cannot
   open are shown locked with a padlock and open the tier gate on click, at most one locked card in the
   ring), never the same card twice, and Arcademy only when `ArcademyHostService.DoorAvailable`.
   Any click outside, Esc, a second right-click on her body or the glyph again closes the ring. A
   PAT does not close it (a pat is affection, not a dismissal, and folding on one would count
   against the ignore streak).

   **The gesture table, wave 3:**

   | Gesture | What it does |
   |---|---|
   | Left-click her body | Pat |
   | Right-click her body | Open / fold the ring |
   | Left-click the cards glyph (top-left, hover) | Open / fold the ring |
   | Hover her head 1.2 s | Pat |
   | Left-click the x (top-right, hover) | Send her away |
   | Drag past 6 DIP | Move her; folds the ring on the first movement |
   | Corner grip | Resize |
   | Right-click a ring CARD | Pin / unpin |
   | Esc, or a click anywhere else | Fold the ring |

   **Pinning also lives in Settings** (wave 3, answering "how can a user customize the bubbles with
   their favourite feat"): Settings > EMI Desk > *Her ring* is a 25-tile checklist writing the SAME
   `EmiState.Pins` through `EmiSuggester`, max 6, with a "let her choose" reset.

   **The tutorial that stops** (wave 3): three nudge tracks teach the pat, the ring and pinning, and
   go permanently silent at 3 pats / 2 ring opens / 1 pin, with a hard cap of 6 lines each, never two
   within 90 s. Never call the glyph a "door" in anything a user can read.
6. **The glass.** Idle for 10s (shipped at 90s; the owner unlocked the rotation for the 2026-08-30
   campus channel port; no input on her, ring closed, no ask open, no line on screen): the glass
   glitch-flips to a channel for 10s: `spiral` (animated spiral; tap = `App.Overlay.ShowOverlayTimed("spiral", 6000, ...)`),
   `video` (a thumbnail of a random local video; tap = `App.Video.PlaySpecificVideo(path)`, EMI goes
   Topmost over the video and says a `videoRunning` line), `burst` (3-4 random local gifs/images on her
   screen; tap = `App.Flash.TriggerFlashOnce(...)`), `rain` (falling thumbnails; tap = a new
   `StartGifRain()` seam carved out of the Chaos GifCascade payload). Any input cancels; a line outranks
   the glass; local assets only unless the app-wide remote-media consent is already granted.
7. **Offers.** Every so often (moment-driven) she asks a question with two chips (or custom chips).
   The question WAITS (owner lock 0825, asks.js): no auto give-up, clicks elsewhere do not cancel it, a
   later line is parked until the strip comes down, no other reaction crosses it. Exactly these end an
   unanswered ask: a chip, Esc while she is focused, the hover x / dismiss, a summon of a full-screen
   feature (video, lockdown, intake). Unanswered = wordless `-_-` 1400ms then `...` then idle; three
   unanswered in a row = no more offers this session. One ask per 10 minutes at most, none before the
   third summon ever (EmiState counts summons). Effects: spiral, video, rain, burst, pin a card to the
   top slot, shrink + snap to corner, bedtime (mute offers till 06:00, never closes the app), open a target.
8. **Voice.** Preset lines only. Lowercase, <= 60 chars, first-read innocent, dork canon, one bubble at
   a time, `say` outranks everything, a global floor of 45s between spontaneous lines, per-moment odds
   and cooldowns from the lines file. Lines are data (see "Lines file"). She can be daring, a little
   horny, tempting (owner call 0829) but never cruel, never guilt at exit, never self-aware about
   scripting, never door/lab hints from the Arcademy story.
9. **Bleeps** (vox.js "Blipese") are a stretch goal, not in scope for the first cut. Leave a
   `IEmiVox` seam.
10. No new NuGet packages.

## Architecture

```
Services/EmiDesk/EmiDeskService.cs        static facade on App: App.EmiDesk
                                          IsOut, Summon(), Dismiss(), Toggle(), Fire(momentId, ctx),
                                          NoteOpen(targetId), hotkey registration, mute arbitration
Services/EmiDesk/EmiFace.cs               face renderer (port of face.js): FrameworkElement that draws
                                          a kaomoji string in #FF69B4 with the exact face.js metrics
Services/EmiDesk/EmiChains.cs             FACES sets + CHAINS + makeSay + playChain (port of chains.js)
Services/EmiDesk/EmiLineEngine.cs         lines file loader + rotation (shuffle bags, no repeat)
Services/EmiDesk/EmiState.cs              persisted state: %LOCALAPPDATA%/ConditioningControlPanel/emi-desk.json
                                          (seen line ids per pool, global recent ring, usage counters,
                                          pins, window rect + monitor, ignore streak, bedtime-until)
Services/EmiDesk/EmiTargets.cs            ring target catalogue (id, label key, thumb, hue, gate, Open())
Services/EmiDesk/EmiSuggester.cs          decayed-usage scoring + ring fill
Services/EmiDesk/EmiChannels.cs           glass channel painters + tap effects
Services/EmiDesk/EmiOffers.cs             ask lifecycle (chips, give-up, ignore streak, effects)
Services/EmiDesk/EmiGifRain.cs            StartGifRain() seam (from Chaos GifCascade)
Windows/EmiDesk/EmiDeskWindow.xaml(.cs)   the widget window: partial class split across
                                          EmiDeskWindow.xaml.cs (body, drag, resize, summon/dismiss FX),
                                          EmiDeskWindow.Ring.cs, EmiDeskWindow.Glass.cs, EmiDeskWindow.Bubble.cs
Controls/EmiDock.xaml(.cs)                the rail circle with the live mini face (+ mute pill)
Resources/emi/desk-lines.json             THE LINES FILE (ships as Content, like Resources/web/**)
docs/primers/EMI_DESK_PRIMER.md           primer (write it last; CLAUDE.md gets a one-line pointer)
```

Window: reuse the avatar tube's windowing recipe (`AvatarTube/AvatarTubeWindow.*`): AllowsTransparency,
Topmost, WS_EX_TOOLWINDOW (no taskbar entry), the 450ms DPI quiesce, ShowActivated=false. Store
position in physical pixels + monitor device name (the DIPs-vs-pixels trap: see
`memory/gaze-tracking-precision-audit.md`), clamp to the work area on restore, default bottom-right of
the main window's monitor. Width 152..420 DIPs, aspect from emi.css (height = w * 0.903 for the screen
area; body PNG governs the whole silhouette). Body art: `Resources/web/arcademy/art/emi/body-*.png`
(idle, smug, shock, pet, sad, sway1-4). Fonts: `Resources/web/arcademy/emi/fonts/` (Noto Sans Mono for
the face, Press Start 2P for pixel labels). The glass rectangle position/size relative to the body is in
`Resources/web/arcademy/emi/emi.css`; copy the numbers.

## Lines file (`Resources/emi/desk-lines.json`)

```json
{
  "version": 1,
  "moments": {
    "ringOpen":   { "pools": ["ringOpen", "common.attention"], "odds": 0.5, "cooldownMs": 20000, "priority": 2, "mix": 0.65 },
    "appIdleLong":{ "pools": ["appIdleLong", "common.idle"],   "odds": 0.3, "cooldownMs": 240000, "priority": 1, "mix": 0.5 }
  },
  "pools": {
    "ringOpen": [
      { "id": "ringOpen.001", "t": "six doors. pick one. i wont look.", "face": "^_^", "chain": "nod", "spice": 0 }
    ],
    "common.attention": [ ... ]
  },
  "asks": [
    { "id": "ask.spiral.001", "moment": "glassOffer", "q": "spiral?", "face": "@_@", "chips": ["spin", "nah"],
      "yes": { "t": "hold still. or dont. it works either way.", "face": "@_@" },
      "no":  { "t": "ok. ill keep it warm.", "face": "._." },
      "effect": "spiral", "spice": 1 }
  ]
}
```

- `t` <= 60 chars, lowercase. `face` is a kaomoji from the FACES sets or any short string; `chain` optional.
- `spice`: 0 innocent, 1 suggestive, 2 lewd. Setting `EmiDeskSpice` (0..2, default 2) filters `<=`.
- `mix` = probability of drawing from the moment's first (specific) pool when both have unseen lines.
- **Rotation rule (owner):** a line is never re-proposed until its pool is exhausted. Per pool keep a
  shuffle bag: deal without replacement from a shuffled copy; when empty, reshuffle the whole pool but
  keep the last 3 dealt out of the first 3 positions. On top, a global recent ring (last 40 ids) that a
  common pool must also avoid while it can. Seen-state persists across launches in `EmiState`.
- Lines are written by the writers' lane into `docs/emi-desk/lines/*.json` and merged by the editor
  into the shipped file; the engine must tolerate unknown moments/pools gracefully (log once, skip).

## Moments (fire sites)

`App.EmiDesk?.Fire("momentId", ctx)` is cheap and safe to call from anywhere (no-op when she is not
out). The scoping lane produces `docs/emi-desk/MOMENTS.md` with the full list and the exact C# fire
sites; the hook agent wires them. The build lane fires its own UI moments directly: desktopFirstBoot,
summoned, dismissed, ringOpen, ringDismissed, ringPick(target), pinAdded, suggestionIgnored3x, resized,
petted, glassOffer(channel), effectFired(channel), effectDeclined, videoRunning, videoEnded, askAnswered,
askIgnored, avatarMuted, avatarKept.

## Settings (`Models/AppSettings.cs`, JsonProperty, follow the file's conventions)

`EmiDeskEnabled` (true: circle visible in the rail), `EmiDeskHotkey` ("Ctrl+Alt+E"), `EmiDeskMuteAvatar`
(true), `EmiDeskMuteDontAsk` (false), `EmiDeskSpice` (2), `EmiDeskOffers` (true), `EmiDeskGlass` (true),
`EmiDeskWidth` (220). Window rect / pins / usage go to `EmiState`, not settings.

## House rules

- Follow existing code conventions (Serilog `Log.Information("[EmiDesk] ...")`, try/catch around every
  timer/async callback, `Application.Current?.Dispatcher` null checks, `HasShutdownStarted` checks).
- Localization: every visible label goes through the 9 `Localization/Languages/*.json` files with a
  `emi_desk_*` key (English text in all 9 is acceptable for the first cut; NEVER a literal newline
  inside a value). EMI's lines are NOT localized.
- No em-dashes in any user-facing text. Read `C:\Users\PC\.claude\projects\C--Projects-Conditioning-Control-Panel---CSharp-WPF-ConditioningControlPanel\memory\diegetic-prose-no-ai-tells.md`.
- Do not modify `Resources/web/arcademy/**` (the web EMI stays untouched).
- Keep chunk boundaries: the partial-class split above exists so parallel agents do not collide.
