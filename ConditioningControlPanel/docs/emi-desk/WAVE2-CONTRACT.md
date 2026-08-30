# Ask EMI - Wave 2 ("the book") build contract

> Owner-acked 2026-08-30 as part of the four-wave plan. Like WAVE1-CONTRACT.md this is the ONLY
> coordination point between the lanes. Ids here are load-bearing. Nobody renames anything without
> changing both lanes.

## What Wave 2 is

1. A **codex window**: EMI's copy of the field manual, pixel-bound, spawned by her.
2. A **renderer** for it: volumes, spine tabs, page turn, stamps, CSS figures. No image assets.
3. **Volumes I to III written down to size** - 14 chapters, one screen each.
4. **"take me there"** on a chapter, wired to `EmiTargets`, tier-aware.

Wave 2 does NOT include the search index, the 57 "?" buttons, or volumes IV to VI. Those are Wave 3.
It does not include an AI router at any point - EMI Desk is preset lines and no AI, by a design lock.

## Three things the recon changed

1. **No csproj work.** `ConditioningControlPanel.csproj:559` already ships `Resources\web\**\*` as a
   wildcard Content row. The bundle lands at `Resources/web/codex/` and is picked up with no edit.
2. **Do not write a WebView2 host.** `Chaos/ChaosWebViewHost.cs` is a finished one: folder mappings,
   `AllowsTransparency=false` (the layered trap, already solved), chromeless vs titled, owner link,
   a JS message bridge, windowed size, centre-on-main. Reuse it. The fail-soft pattern is
   `Controls/SpiralEmbedView.cs:139` - `CoreWebView2Environment.GetAvailableBrowserVersionString()`
   in a try/catch, and a visible panel on failure, never an empty hole.
3. **The site does not cover everything.** There is no guide page for the spiral, the overlays or the
   pink filter - the hits are nav links on every page, not content. Those chapters are written from
   the app's own second source: `Services/HelpContentService.cs` (57 topics, English-only C#,
   including `help_caption_spiral_overlay` and `help_caption_pink_filter`) and the settings UI.

## The bundle

```
Resources/web/codex/
  index.html          the shell: spine, page, margin, footer
  codex.css           the whole look. no images, no CDN, no fonts beyond what ships
  codex.js            navigation, page turn, stamps, bridge messages
  codex.json          volumes + chapter order + titles (the index)
  chapters/<id>.json  one file per chapter, the schema below
```

Everything is offline and local. No network at all: no CDN, no Google Fonts, no analytics. The
window maps the folder with `CoreWebView2HostResourceAccessKind.Deny` like the other hosts do.

## Chapter schema (writers write these; C# also reads them)

One chapter is **one screen**. Target 450-650 words. The whole point of the cut is that 49,000
words is a website's budget, not a book somebody reads inside an app.

```json
{
  "id": "the-panic-key",
  "volume": 1,
  "order": 3,
  "title": "the panic key",
  "blurb": "one key, everything off.",
  "target": "settings",
  "tour": "Settings",
  "margin": { "t": "press it once. i'm not offended.", "face": "-_-" },
  "blocks": [
    { "type": "p",        "text": "..." },
    { "type": "steps",    "items": ["...", "..."] },
    { "type": "figure",   "kind": "stack-drop", "caption": "..." },
    { "type": "callout",  "text": "..." },
    { "type": "limit",    "text": "the one honest limitation" }
  ]
}
```

- `target` - an `EmiTargets` id (`EmiTargets.All`) or `null`. Drives "TAKE ME THERE".
- `tour` - a `TutorialType` **name** or `null`. Drives "WALK ME THROUGH IT". Never an ordinal.
- `margin` - EMI in the margin, exactly one line per chapter. Her rules apply: lowercase, one
  thought, **<= 60 characters**, no em/en dashes, no emoji in `t`, `face` from the shipped set in
  `Resources/emi/desk-lines.json`. She never explains the chapter; she reacts to it.
- `limit` - every chapter carries **one honest limitation**. A manual that admits nothing is not
  trusted, and this app has real edges. Verify the edge before you write it. **Only Brain Drain
  is excluded from screen capture** (`BrainDrainLayer.ExcludeFromCapture` overrides a `BaseLayer`
  default of false), so the spiral, the pink filter, flashes and subliminals DO appear in a
  recording. Gaze needs light. The panic key cannot un-decode a frame already in flight.

### Figure vocabulary - CSS and SVG only, pick from this list

The renderer implements these ten; writers pick, and never invent an eleventh. No PNG, no
nano-banana, no generated art anywhere in this wave.

`stack-drop` `pulse` `layers` `timeline` `dial` `checklist` `spiral` `wave` `grid-fill` `clock`

## Volumes I to III - 14 chapters

| id | vol | title | primary source |
|----|-----|-------|----------------|
| `first-light` | I | first light | `guide-getting-started.html` (Requirements, Installation, First Launch) |
| `your-folders` | I | your folders | `guide-getting-started.html` (Adding Your Content) + `guide-settings.html` (Files & Data) |
| `the-panic-key` | I | the panic key | `guide-settings.html` (Panic Key) + `guide-advanced.html` (Dangerous Features) |
| `the-room` | I | the room | `guide-getting-started.html` (Interface, Basic Workflow) + `guide-settings.html` (Multi-Monitor) |
| `flashes` | II | flashes | `guide-flash-images.html` |
| `videos` | II | videos | `guide-videos.html` |
| `subliminals` | II | subliminals | `guide-subliminals.html` |
| `spiral-and-filters` | II | the spiral and the filters | **no site page** - `HelpContentService` + the Studio UI |
| `sound` | II | sound | `guide-settings.html` (Audio Settings) |
| `sessions` | III | sessions | `guide-sessions.html` |
| `the-scheduler` | III | the scheduler | `guide-scheduler.html` (Scheduler, Behavior) |
| `ramps` | III | ramps | `guide-scheduler.html` (Intensity Ramp, Link to Ramp) + `guide-sessions.html` (XP Bonuses) |
| `takeover` | III | takeover | `guide-takeover.html` |
| `lockdown` | III | lockdown | `guide-lockdown.html` |

Volume titles: **I first light / II the instruments / III the session floor.** IV to VI exist on the
spine as locked tabs with no pages behind them yet, so the shape of the book is honest from day one.

## Prose voice (the book, not her)

The chapters are the manual's voice, not EMI's: second person, plain, calm, short sentences. She
only ever appears in `margin`. House rules that apply to both:

- **No em-dashes or en-dashes anywhere.** Plain hyphens. This is a standing owner rule.
- No AI tells. No "delve", no "it's not just X, it's Y", no triads, no cheerful summary paragraph.
- Say the limitation out loud rather than selling past it.
- The word "door" is the app's own word for the rail and is fine in chapter prose. It stays off
  EMI's tongue: never in a `margin` line, never in UI chrome. There it is the book, the manual,
  the shelf.

## The window (C# lane)

- An ordinary `Window`. **Not layered** - `AllowsTransparency` stays false or WebView2 paints
  nothing and the codex looks dead.
- Pixel chrome is drawn **in CSS inside the page**, not in WPF. The window is a frame around it.
- Fail soft: no runtime, a dead browser process or a failed navigation shows a native WPF panel that
  reads the same `chapters/*.json` and lists them as scrolling text, plus a link to the website.
  Never an empty hole.
- Bookmark: last chapter id in `EmiState`, restored on open.
- Reduced motion: honour it. All motion is on navigation; nothing animates while somebody reads.

### Bridge messages (JS -> C#, over the host's existing message channel)

| message | payload | C# does |
|---------|---------|---------|
| `codex:ready` | - | log, stop the loading state |
| `codex:open` | `{ chapter }` | store the bookmark |
| `codex:target` | `{ id }` | `EmiTargets.Find(id)`; if `Locked`, say so and do not open |
| `codex:tour` | `{ type }` | `MainWindow.StartTutorial(Enum.Parse<TutorialType>(...))`, close the book |
| `codex:close` | - | close the window |

Every handler validates its payload against the catalogue and swallows. A page that asks for an
unknown target or an unparseable tour does nothing at all.

### Entry points in Wave 2

1. **Her ring** - one new `EmiTargets` row, `codex`, always available, never locked, placed
   **seventh**. Catalogue order is load-bearing: the first six available entries ARE the ring for a
   brand new user, and displacing one of those is an owner call, not a build decision.
2. **Her offer** - a two-chip ask on a new moment `bookOffer`, effect verb `book:open`.
   The two-chip law from Wave 1 still holds: `PickAsk` drops anything but exactly two chips.

The 57 "?" buttons and the search line are Wave 3.

## File ownership

| lane | owns |
|------|------|
| A1 window | `Windows/EmiCodexWindow.xaml(.cs)`, `Services/EmiDesk/EmiCodex.cs`, the `codex` row in `EmiTargets.cs`, the `book:open` verb in `EmiOffers.cs`, the `bookOffer` moment in `desk-lines.json`, new `en.json` keys, its tests |
| A2 renderer | `Resources/web/codex/**` entirely, plus `tools/codex-extract.py` |
| F1 writer | `docs/codex/i-*.json`, `docs/codex/ii-*.json` |
| F2 writer | `docs/codex/iii-*.json`, `docs/codex/spine.json`, `docs/emi-desk/lines/w2-book.json` |

Nobody writes `Resources/web/codex/chapters/` directly - the orchestrator merges the writers' files
there after validation, exactly as Wave 1 merged lines into `desk-lines.json`.

**One worktree, one index.** Wave 1 lost attribution when two agents staged at the same time: stage
and commit in a single command, and commit compiling states only.
