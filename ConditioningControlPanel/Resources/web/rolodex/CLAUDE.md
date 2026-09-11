# Rolodex - the 3D feature picker (Resources\web\rolodex)

The picker for the customizable Home dashboard: four stacked rings of 16:9 cards, one ring per
tier band, spun with the mouse or the arrow keys. Phase E of `home-dashboard-slots-rolodex.md`.

Hosted by the WPF app in a WebView2 at **`https://ccp.game/rolodex/index.html`** (virtual host
`ccp.game` -> `Resources\web`, `CoreWebView2HostResourceAccessKind.Allow` so canvas textures work).
Also runs in a plain browser with `?mock=1`, which is how the 3D is iterated without the app.

## 1. Files

| File | What it is |
|---|---|
| `index.html` | import map (`three` -> `../vendor/three/three.module.min.js`), the canvas, the DOM HUD |
| `rolodex.js` | the whole page: scene, card textures, input, HUD, bridge handlers, mock fixture |
| `bridge.js` | verbatim copy of `dtrh\bridge.js` (protocol v1) apart from its header comment |
| `style.css` | HUD chrome, velvet palette, the CSS vignette |

No bundler, no build step, no network fetch, no dependency beyond the vendored three.js r169.
`Resources\web\**\*` is already `Content` in the csproj (and in the `WebFiles` publish glob), so
this folder ships with **no csproj change**. `Resources\web\**\CLAUDE.md` is excluded from both, so
this document does not ship.

## 2. The message contract

postMessage JSON both ways, `{type: ...}` envelope, exactly like dtrh. The page announces `ready`
on load and the host flushes whatever it queued.

### Host -> page

```jsonc
{
  "type": "init",
  "rings": [                       // 1..4, in display order, top ring first
    { "ring": 1,
      "faces": [                   // one card per feature, in ring order
        { "key": "flash",          // the canonical slot key (plan 3.3)
          "title": "Flash Images", // already localized by the host
          "blurb": "One line.",    // already localized, one line, no wrapping
          "tier": 0,               // 0 free, 1 = Tier 1 (gold), 2 = Lab (diamond)
          "locked": false,         // not entitled: draws the LOCKED band
          "art": "data:image/jpeg;base64,..." }   // or null for a placeholder
      ] }
  ],
  "slot": 4,                       // int or null; display only ("slot 5" in the header)
  "mode": "edit",                  // "edit" | "tour"
  "picks": 1,                      // tour mode: how many faces to collect
  "reducedMotion": false,          // OR'd with the page's prefers-reduced-motion
  "lang": "en"                     // carried for completeness; the page localizes nothing
}
```

`{ "type": "focus", "key": "dtrh" }` - make that face's ring the focused one and spin to it.
`{ "type": "close" }` - the host is disposing the view; the page goes inert and stops accepting input.

### Page -> host

| Message | When |
|---|---|
| `{type:'ready', protocol:1}` | once, on load, from `bridge.announceReady()` |
| `{type:'pick', key}` | edit mode, on Enter / click on the front card. One per open: the page marks itself inert and the host closes it |
| `{type:'tourDone', keys:[...]}` | tour mode, when Done is pressed (Done appears once `picks` faces are picked) |
| `{type:'close'}` | Esc or the close button. Also fires after a `tourDone` if the user then presses Esc |
| `{type:'log', level, msg}` | `bridge.log`; `level` is debug / info / warn / error, msg capped at 400 chars |

The page never knows the layout rules. Replace / Split / Move prompting, session-lock refusal and
slot assignment all stay WPF-side after `pick` / `tourDone`. A **locked** face is still pickable by
design (owner call: every slot is a user-chosen upsell); the host's own tier gate refuses later.

## 3. Interaction

| Input | Effect |
|---|---|
| horizontal drag, Left / Right | rotate the focused ring, snapping to the nearest card |
| vertical drag, wheel, Up / Down | step the focus between rings (camera dollies, other rings dim to 34%) |
| Enter, Space, click on the front card | pick |
| click on any other card | focus that ring and spin that card to the front |
| Esc, the close button | `close` |

Tour mode adds a `pick N` counter in the header, a check mark on picked cards, and a Done button
that appears at N. Picking a face that is already picked toggles it off; picking past N drops the
oldest pick so the newest choice always lands.

Rotation is stored as an **unbounded continuous** `group.rotation.y`, so there is never a wrap
discontinuity to tween across; `nearestTurnFor` resolves a face index to the shortest-way turn count.

`reducedMotion` (from `init` or from `prefers-reduced-motion`) makes `tween()` and `approach()`
assign straight to the target, so every move is a cut. There is no animation that is not routed
through those two helpers.

## 4. Layout maths

Ring N has `faces.length` cards on a cylinder of radius
`max(1.55, (CARD_W * 1.22) / (2 * sin(pi / n)))`, which keeps the chord between neighbours wider
than a card, so a ring can gain faces without cards overlapping. Cards are flat
`PlaneGeometry(1.6, 0.9)` parented to a ring `Group` at angle `2*pi*i/n`, normal pointing out of
the cylinder; a second dark plane at `angle + pi` gives the far side card backs instead of holes.
The camera sits at `radius + 2.85` in front of the focused ring, so the front card is always the
same size no matter how many faces the ring carries.

## 5. Card texture

One 512x288 canvas per face, redrawn only on build, on art load and on a pick toggle:
cover-fit art (or a hue-tinted placeholder keyed off the feature key, with the title's initial),
a bottom scrim, title + one-line blurb (measured and ellipsized, never wrapped), a tier badge
(`TIER 1` gold / `LAB` diamond, tier 0 has none), a translucent `LOCKED` band across the bottom,
a purple check disc when picked, and a rim in the tier colour drawn outside the clip.

Art arrives as a data URI in `init` (the host builds JPEGs at decode width 512, quality 80, cached
per mod switch). The page never fetches anything.

## 6. Mock mode

`?mock=1` edit mode, `?mock=tour` tour mode with `picks:3`, `&reduced=1` forces reduced motion.
Mock also engages automatically when `window.chrome.webview` is absent. The fixture carries the 27
real keys in their four rings with `art:null` (placeholder art only) and a stable pseudo-random
locked mix; `pick` / `tourDone` / `close` go to the console and to the on-page `#status` line
instead of to a host.

To run it: serve `Resources\web` over http (ES modules will not load from `file://`) and open
`http://127.0.0.1:PORT/rolodex/index.html?mock=1`.

## 7. Rules for anyone editing this page

1. Dark first. The host swaps a WebView2 HWND straight over the velvet grid and an HWND cannot
   fade, so `html`/`body` and the GL clear colour are all `#0b0710` before any script runs. Never
   introduce a light default.
2. Resize-safe. The host resizes the HWND; `sizeToViewport` is wired to both `resize` and a
   `ResizeObserver`. Do not cache viewport dimensions anywhere else.
3. Everything animated goes through `tween` / `approach` so reduced motion keeps working.
4. No `AddHostObjectToScript`, ever: postMessage only, same as dtrh and arcademy.
5. Keep `bridge.js` a verbatim copy of the dtrh original (header comment aside). If the protocol
   moves, move both.
