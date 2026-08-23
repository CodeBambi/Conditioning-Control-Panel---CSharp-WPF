# For You Feed — census against the shipping WPF source

Evidence tree: this repository at `b76856a7` (`feat/crossplatform`), read on 2026-08-21.
Method: the repeatable inventory rules and source citations stated in this document.

**Verdict: REFUSED, with the inventory below.** Not "too big" — *the wrong shape*. Two of the seven
behaviours the owner used to define this surface are governed by decisions that are not the port's to
make, and the surface's own directory is not a feature but a **shared remote-media subsystem with
twelve compile-time consumers outside itself**, five of which are surfaces the port has already
shipped or already owes.

---

## 1. The universe, and the counts the board row got wrong

The universe was the repository root walked recursively, exclusions being patterns over generated
bytes only (`bin/`, `obj/`, `.git/`, `__pycache__/`, `*.log`, `*.binlog`, `*.nettrace*`, `*.etlx`,
`*.speedscope.json`). No file list was assembled by hand at any point.

### 1.1 Three numbers were in play. All three disagreed.

| Source | Claim | Reality |
|---|---|---|
| `client/docs/task-board.md`, the For You Feed row | "`Services/Fyp/` (3 new)" | **WRONG — 9 files** |
| Wave coordinator, measured independently | 9 `.cs` + an `Online/` subdirectory | **CONFIRMED** |
| This census, `find` and `git ls-files` | 9 files, 3237 lines, 2 directories | — |

```
find ConditioningControlPanel/Services/Fyp -type f | wc -l   ->  9
git ls-files ConditioningControlPanel/Services/Fyp | wc -l   ->  9
```

The two counts agree, so there are no untracked bytes in the tree. **The board row understates the
file count by 3x**, and that is the least of what it understates (§1.3).

### 1.2 The enumeration (re-derived by the pin on every run)

| # | Path (relative to `ConditioningControlPanel/`) | Lines | What it is |
|---|---|---|---|
| F1 | `Services/Fyp/FypAssetManifest.cs` | 155 | Enumerates the user's active preset into the manifest the page consumes. |
| F2 | `Services/Fyp/FypGhostOverlay.cs` | 423 | Ghost mode: the DWM-thumbnail mirror. |
| F3 | `Services/Fyp/FypHostService.cs` | 1159 | The host: window, WebView2 bridge, ghost lifecycle, webcam eye control, XP. |
| F4 | `Services/Fyp/FypMetaStore.cs` | 122 | Per-asset probed metadata + decode fail-strikes, `fyp_meta.json`. |
| F5 | `Services/Fyp/Online/FypOnlineCoordinator.cs` | 365 | Multi-tenant remote channel rotation + per-channel dwell EWMA. |
| F6 | `Services/Fyp/Online/IFeedSource.cs` | 143 | Remote source abstraction + `FeedMediaKind`. Carries THE BRIGHT LINE. |
| F7 | `Services/Fyp/Online/RemoteMediaCache.cs` | 450 | Remote byte cache + temp-file materialisation. |
| F8 | `Services/Fyp/Online/RemoteMediaFormats.cs` | 109 | Format/renderability validation for remote entries. |
| F9 | `Services/Fyp/Online/ScrolllerSource.cs` | 311 | **Third-party network client — scrolller.com GraphQL.** |
| | **Total** | **3237** | |

### 1.3 The row's real error is not the number. `Services/Fyp/Online/` is not part of For You Feed.

`FypOnlineCoordinator.cs:24-30` says so in its own words, and I opened it:

> *"MULTI-TENANT: rotation state (iterators, dead flags, backoff) and the dwell EWMAs are per
> CONSUMER, not per process. **The For You feed, flashes, intake and DTRH each pick their own
> niches**, so a single shared rotation would have them fighting over one set of iterators and
> cross-contaminating each other's taste."*

`Services/Fyp/Online/` is the **app-wide remote-media subsystem**. It happens to live under the FYP
directory for historical reasons (`FypHostService.cs:404-405`: the feed was *"the ONE remote surface
the app-wide gate never reached"*). Porting "For You Feed" as scoped by the board row would mean
porting a subsystem that five other surfaces depend on — **two of which this port has already
shipped** (DTRH, Intake).

That is the haptic-count failure mode exactly: a name-shaped search over a directory nobody
questioned. The correction did not come from reading `Services/Fyp/` harder.

### 1.4 Consumer closure (M3) — 17 files outside `Services/Fyp/` name its types

Enumerated by grepping the whole shipping tree for the nine type names, not by sampling.
**Compile-time consumers (12):**

| File | What it uses |
|---|---|
| `App.xaml.cs:1665` | `RemoteMediaCache.CleanupStaleTempFiles()` at startup |
| `App.xaml.cs:2458,2462,2463` | `--fyp` CLI flag -> `TierGate.RequiresPremium` -> `FypHostService.Launch()` |
| `MainWindow/MainWindow.Assets.cs:2208,2293,2383,2425` | `FypOnlineCoordinator.Catalog`, `ResetAllChannels`, `SanitizeSub` |
| `MainWindow/MainWindow.Lab.cs:290,292` | premium gate then `FypHostService.Launch()` |
| `MainWindow/MainWindow.xaml.cs:1085-1101` | panic ladder: `IsActive`, `IsGhosted`, `ExitGhost`, `RecentlyUnghosted`, `Close` |
| `Services/Arcademy/ArcademyHostService.cs:1513,1594` | `FypOnlineCoordinator.For(..., FeedMediaKind.Any)`, `ResolveChannels` |
| `Services/AutonomyService.cs:1061` | `FypHostService.IsActive` — stands WebVideo down |
| `Services/BubbleCountService.cs:157` | `FypHostService.IsActive` — stands down |
| `Services/Chaos/DtrhAssetManifest.cs:383,431` | `FypOnlineCoordinator.For(...)`, `ResolveChannels` |
| `Services/Flash/FlashService.cs:2896,2940` | `ResolveChannels`, `For(..., FeedMediaKind.GifStill)` |
| `Services/Quiz/IntakeHostService.cs:928,966` | `For(..., FeedMediaKind.Image)`, `ResolveChannels` |
| `Services/Video/VideoService.cs:1657,6797,6871,6890` | `IsActive`, `ResolveChannels`, `For(...Video)`, `RemoteMediaFormats.Validate` |
| `Services/Video/WallpaperService.cs:308,387,407,424,520` | `ResolveChannels`, `For(...Image)`, `Validate`, `MaterializeAsync`, `ReleaseTempFile` |

**Comment-only references (6)**, kept separate because they are not compile-time consumers and a
census that conflated them would be overstating: `Chaos/ChaosFlashOverlay.cs:195`,
`Chaos/ChaosGifCascadeOverlay.cs:357`, `Models/AppSettings.cs:3193,3247`, `Views/Tabs/AssetsTabView.xaml:99`,
`Views/Tabs/PlayTabView.xaml:1095,1126`, `Views/Tabs/PlayTabView.Cards.cs:90`.

**The two `Chaos/` files are the finding that justifies the method.** They reference
`RemoteMediaCache` and contain **no occurrence of the token `fyp` at all**. A `fyp`-token sweep —
including a correctly anchored one — cannot find them. They were found only because the plan's reveal
rule (M2) promoted the surface's own type names to search tokens. That is the difference between this
count and the four haptic counts.

### 1.5 A trap in the token itself

The seed `fyp` **unanchored matches `NotifyPercent` and `INotifyPropertyChanged`** (`...tiFYPercent`,
`...tiFYProperty`). Counted over one named universe — tracked TEXT files under `ConditioningControlPanel/`, every
extension, excluding the `CCP.*` projects and excluding `Resources/web/fyp/` — unanchored reports
**100** files and anchored reports **53**. (The universe matters and is why this number needs its own
sentence: the same anchored sweep over `.cs`/`.xaml` only is 42, and including the payload tree it is
57.) My plan anticipated this for `feed` and `ghost` and **failed to anticipate
it for `fyp` itself**; it was caught by opening the matches rather than counting them. Anchored
pattern of record: `(^|[^A-Za-z])fyp`, case-insensitive.

### 1.6 The settings are in the shipping project, and the tension this section used to describe was never real

All 15 FYP settings properties, and the consent union, are defined in
`ConditioningControlPanel/Models/AppSettings.cs` — the shipping WPF project's own tree.

**CORRECTED 2026-08-22. What this section said before was false, and it is worth stating why
rather than just deleting it.** It read: *the settings live in a tree the constitution calls failure
evidence*, and argued that `ConditioningControlPanel.csproj:52` carried
`<ProjectReference Include="CCP.Core..." />` so `CCP.Core` was **a compile dependency of the shipping
WPF product**. **`main` has never had that `ProjectReference`, and has never contained a `CCP.*`
directory at all.** Both the reference and the relocated `Models/` tree were artifacts of this port
branch's own undeclared divergence — 1180 files of it, deleted on 2026-08-22 (D326).

**So the "real tension with `docs/constitution.md:32`" that §7 D209 was written to resolve did not exist
in the shipping product.** It was manufactured by the branch the census was measured on, and then
reasoned about as if it were a property of the product. D209's resolution is left standing as history
because the reasoning was sound given the tree it was taken against; the PREMISE is what failed. This
is the sharpest single instance of why `ConditioningControlPanel/**` must track `main` exactly, and
why that rule having no enforcement is filed as its own board row.

---

## 2. The payload: 8 files, and it must never be forked

```
find ConditioningControlPanel/Resources/web/fyp -type f | wc -l   ->  8
git ls-files ConditioningControlPanel/Resources/web/fyp | wc -l   ->  8
du -sh                                                             ->  228K
```

`feed.js`, `main.js`, `segments.js`, `stats.js`, `surfaces.js`, `index.html`, `fyp.css`,
`fyp-art.jpg` — 5 JS, 1 HTML, 1 CSS, 1 JPG.

**Corroborated independently:** `client/docs/upstream-payload-inventory.json` already records
`{"name":"fyp","disposition":"not-ported","fileCountAtBaseline":8}`. Two independent records agree,
so this number is not resting on my walk alone.

**It is 8, not 184.** The 184-file tree is `web/goon/`, a different board row. This packet's
framing ("3 new files against Goon Game's 25 files plus 184 payload files") is correct that FYP is
the smaller payload; it is the *code* count that was wrong.

### How the port would serve it, without forking a byte

Exactly as three trees are already served — a four-line linked glob in
`client/src/CcpClient.Desktop/CcpClient.Desktop.csproj`, the bytes staying owned by the legacy tree:

```xml
<Content Include="..\..\..\ConditioningControlPanel\Resources\web\fyp\**\*">
  <Link>payload\fyp\%(RecursiveDir)%(Filename)%(Extension)</Link>
```

Precedent, all present and read: `dtrh` (`:50-51`), `intake` (`:59-60`), `tunnel` (`:69-70`),
`vendor` (`:74-75`). **The payload is the cheapest part of this surface and is not what refuses it.**
No proposal to copy bytes into `client/` appears anywhere in this document.

---

## 3. Behaviour map — every row cites both sides and carries a platform cell

Vocabulary is closed: `COVERED`, `PARTIAL`, `GAP`, `OWNER-GATED`. Essentiality is decided against the
seven noun phrases the owner wrote in the For You Feed row of `client/docs/task-board.md`, not
re-derived per row. The board is cited WITHOUT a line: it is rewritten every wave, so a line
number into it is a citation with an expiry date.

| # | Owner's phrase | WPF evidence (opened) | Required primitive | Port anchor | Label | Platform |
|---|---|---|---|---|---|---|
| B1 | endless conditioning-clip feed | `Services/Fyp/FypHostService.cs:16-18` — feed "rendered in a WebView2 window at `Resources/web/fyp/index.html`" | Render a local HTML/JS page in an embedded browser and exchange messages with it | `client/src/CcpClient.Desktop/Features/Dtrh/DtrhCapabilityProbes.cs:21` — "Embedded WebView surface (Windows = WebView2 NativeWebView, §5)" | **COVERED by video/webview** (DTRH ships it) | Windows: proven (DTRH). **Linux: unproven** — gate: run the feed page in the Avalonia WebView on a real X11/Wayland box; no WSL distro exists here (`client/memories/port-status.md:89-96 @ a8d32c219`) |
| B2 | ghost mode = see-through AND click-through | `Services/Fyp/FypGhostOverlay.cs:9-42` (technique), `:285` (`WS_EX_LAYERED\|WS_EX_TRANSPARENT\|WS_EX_NOACTIVATE\|WS_EX_TOOLWINDOW`), `:379-423` (DWM P/Invokes) | A live translucent mirror of another window's pixels, click-through, composited by the OS | `client/src/CcpClient.Desktop/Overlay/Win32OverlayPresence.cs:18-40` gives layered + click-through + topmost, **but mirrors nothing** | **GAP: live window-thumbnail mirroring** — (a) primitive: DWM thumbnail of another HWND; (b) WPF uses `DwmRegisterThumbnail`/`DwmUpdateThumbnailProperties` + `SetLayeredWindowAttributes(LWA_COLORKEY)` on a **GDI/WinForms** surface; (c) the port would need a DWM-thumbnail host on Windows and an entirely different mechanism on Linux | Windows: **unproven** (nothing in `client/src` registers a DWM thumbnail). **Linux: unproven and unmapped** — X11/Wayland have no `DwmRegisterThumbnail` analogue; Wayland forbids reading other surfaces' pixels without a portal |
| B3 | webcam gaze scrolling | `Services/Fyp/FypHostService.cs:903-1045`; consent `:944`, dialog `:946`, `StartAsync` `:958`, ownership `:964`, conditional stop `:1023`, gaze subscribe `:1045` | Camera capture + face/iris inference + calibrated gaze mapped to a screen point | `none` — webcam is **not** among the port's seven landed capabilities | **OWNER-GATED** (see §5) — `client/docs/capability-inventory.md:70` requires "a consent-contract revision and owner review"; `:78` "a stub that says running is a failure" | Windows: **unproven**. **Linux: unproven** — `capability-inventory.md:78` additionally requires XDG Camera portal/PipeWire proof |
| B4 | opacity control | `Models/AppSettings.cs:3190-3199` — `FypWindowOpacity`, clamp `Math.Clamp(value, 0.01, 1.0)`, "the DWM thumbnail opacity of the see-through mirror, **never the real window's alpha**" | A 0.01-1.0 translucency applied to the mirror | inherits B2 — there is no mirror to apply it to | **GAP: consequent of B2** (the clamp itself is trivial; its subject does not exist) | Windows: unproven. Linux: unproven |
| B5 | any monitor | `Services/Fyp/FypGhostOverlay.cs:75` `_screen = WF.Screen.FromHandle(sourceHwnd)`; `:91` `_form.Bounds = _screen.Bounds` | Resolve the monitor a window is on and fill exactly that monitor in physical px | `client/src/CcpClient.Desktop/Overlay/OverlayDisplays.cs` (per-display overlay placement already lands) | **PARTIAL on overlay** — missing member: "which display does this *foreign* window occupy", i.e. an HWND-to-display resolve | Windows: partial. **Linux: unproven** — mixed-DPI multi-monitor is a headed gate the port has never discharged |
| B6 | survives Show Desktop | `Services/Fyp/FypHostService.cs:711-722` veto of `SC_MINIMIZE` (`handled = true` is `:722`); `:585-593` severs the owner link (`SuspendMainWindowGlue(true)` is `:593`); `:784` heals a minimize that lands anyway | Refuse/heal an OS-initiated minimise of a window the user cannot see | `none` — no `client/src` window subclasses a WndProc to veto a system command | **GAP: system-command veto on a foreign message pump** — (a) primitive: intercept `WM_SYSCOMMAND`/`SC_MINIMIZE`; (b) WPF uses `HwndSource.AddHook`; (c) the port would need a Win32 hook seam, and on Linux there is no equivalent because there is no equivalent of Show Desktop's owner-cascade | Windows: unproven. **Linux: not applicable in this form** — the behaviour would have to be re-derived, not ported |
| B7 | undecodable clips notice-and-swap | `Services/Fyp/FypHostService.cs:213-225` (`media-error`, codes 2/3/4, `RecordFailure` for library ids only); `Services/Fyp/FypMetaStore.cs:29-30` `FailStrikeLimit = 2` | Count decode failures per asset, stop serving after 2 strikes, page past the bad tile | `none` in the port, **but the logic is pure and page-side** | **PARTIAL on video** — missing member: a per-asset failure ledger; the swap itself lives in `main.js`, which the port would serve unmodified | Windows: unproven. Linux: unproven |

### 3.1 What the seven-capability map says overall

- **COVERED: 1 of 7** (B1, and only on Windows).
- **PARTIAL: 2 of 7** (B5, B7).
- **GAP: 3 of 7** (B2, B4, B6) — and B2/B4 are the same gap counted where the owner named two phrases.
- **OWNER-GATED: 1 of 7** (B3).

**Ghost mode is the surface's title and half its identity, and it is the deepest gap in the
inventory.** Applying the essentiality rule fixed in the plan — a behaviour is essential iff it realises one of
the owner’s seven phrases (`plan.md` §4) — B2 realises *“ghost mode = see-through AND click-through”*
verbatim, so it is essential and the rule fires without a judgement call. The board row names it in
the title.

---

## 4. What ghost mode actually is (answered from source)

Nobody had said. It is **not** a transparency setting on the feed window. It is the **OnTopReplica
technique**: the real WebView2 window is **parked off the virtual desktop**, and a second, empty,
GDI-surfaced WinForms window paints a **live DWM thumbnail** of it over the feed's home monitor,
translucent and click-through (`FypGhostOverlay.cs:9-42`, opened in full).

It works that way because the obvious approach does not work, and the source records the failed
experiment (`:14-17`):

> *"`SetLayeredWindowAttributes` on a window that HOSTS WebView2 turns its content solid **BLACK** —
> DWM's layered redirection never sees the Chromium child processes' cross-process
> DirectComposition visuals. (Shipped once, reproduced live 2026-08-03.)"*

Four load-bearing details, each measured rather than assumed by the WPF author (`:18-32`):

1. The mirror must be **GDI/WinForms, never WPF** — a WPF window renders through D3D and
   `LWA_COLORKEY` never matches it.
2. The colour key is **RGB(1,1,1), not black**, so genuinely black pixels inside the mirrored feed
   can never match the key (`:34-36`).
3. Translucency rides **`DWM_TNP_OPACITY`**, not window alpha — `LWA_ALPHA` multiplies surface and
   thumbnail together, so no constant alpha can hide one and keep the other (`:22-25`).
4. Chromium must be told not to throttle the parked window or **the mirror freezes** (`:41-42`).

The two controls that stay clickable (gear, mute) cannot live on the mirror, because its whole
surface is `WS_EX_TRANSPARENT`; each is **its own tiny topmost window without that style** —
"small clickable islands over a click-through sea" (`:100-103`).

**This is the most Windows-specific code in the entire surface.** It is not a style flag the port
could set; it is a compositor feature with no X11 or Wayland analogue, and under Wayland reading
another surface's pixels is deliberately not permitted without a portal.

---

## 5. OWNER-FLAGGED — two decisions, deliberately not priced

Per the plan's §4 carve-out these are **not folded into any size**. Both are decisions, not tasks.

### 5.1 The whole surface fetches NSFW media from a third party, over the network

`ScrolllerSource.cs:12-43` (opened): **`POST https://api.scrolller.com/admin`**, an *unofficial*
GraphQL API of a Reddit media aggregator, with CDN hotlinks played directly. The niche catalog
(`FypOnlineCoordinator.cs:44-62`) is 12 named categories of explicit content, and channel selection
plus dwell-weighted rotation means **the user's revealed preferences shape the requests sent to that
third party**.

`IFeedSource.cs:15-18` records the governing decision, and it is already an owner decision:

> *"**THE BRIGHT LINE (owner decision, 2026-08-10)**: implementations run ON THE USER'S DEVICE and
> fetch straight from the provider. Nothing here may ever route media through CC Labs infrastructure
> — no proxying, no caching, no re-serving."*

For the port this raises questions the census cannot answer: whether the port takes on a dependency
on an unofficial third-party API at all; whether an aggregator's availability belongs on the port's
critical path; and whether the port reproduces the existing consent model or improves it.

### 5.2 Consent granted in the feed unlocks remote media across five other surfaces

`Models/AppSettings.cs:3327`, opened:

```csharp
public bool HasRemoteMediaConsent => _remoteMediaConsented || _fypOnlineConsented;
```

with the documented intent at `:3320-3326`: *"users who already accepted the FYP feed's consent card
agreed to exactly this, and asking them a second time in different words would read as the app having
forgotten. Consent flows one way only — accepting the app-wide card does not silently enable the
premium feed."*

The union is deliberate and defensible, and the one-way rule is honest. **It is still a
privacy-relevant coupling the port must adopt knowingly**: accepting one card inside For You Feed is
what lets `FlashService`, `VideoService`, `WallpaperService`, `IntakeHostService` and
`DtrhAssetManifest` begin fetching remote media. **Two of those five are surfaces the port has
already shipped.** If the port lands the FYP consent card without the union, it diverges; if it
lands the union without the card, it over-grants.

### 5.3 The four privacy questions, answered for the whole surface

| # | Question | Answer | Citation |
|---|---|---|---|
| Q1 | Changes what is **persisted**? | **YES** — `fyp_stats.json`, `fyp_meta.json` (per-asset probed dimensions + decode strikes), `fyp_online.json` (per-channel dwell EWMA = a taste profile) | `FypHostService.cs:37`, `FypMetaStore.cs:16`, `FypOnlineCoordinator.cs:141` |
| Q2 | Changes what is **shown to others**? | **NO, and better than neutral** — the ghost mirror is `WS_EX_TOOLWINDOW` (never in Alt-Tab) and `ShowInTaskbar = false` | `FypGhostOverlay.cs:271`, `:285` |
| Q3 | Changes what **leaves the machine**? | **YES** — HTTPS POSTs to `api.scrolller.com` plus CDN media hotlinks, shaped by the user's niche selection and dwell | `ScrolllerSource.cs:16,48` |
| Q4 | What **sensor** does it turn on, under whose consent? | **The webcam**, under the app-wide webcam consent (`IsConsentCurrent()` + `WebcamConsentDialog`, the same dialog every other call site uses). Stops **only** the camera FYP itself started. Consent is properly gated. | `FypHostService.cs:944,946,958,964,1023` |

**On Q4 I correct the review that prompted the question.** It reported "a camera the user cannot see
running behind an invisible window". The camera is **not** started without consent — `:944` checks
`IsConsentCurrent()` and `:946` raises the standard dialog before `StartAsync()` at `:958`. What is
true, and is the real finding, is the **combination**: in ghost mode the window showing that eye
control is active is parked off-screen and represented only by a translucent, click-through mirror,
while `_fypStartedWebcam` (`:964`) means the camera keeps running for other consumers if FYP did not
start it. Consent is sound; **visibility of an active sensor is the thing worth the owner's
attention**, and the port should not reproduce the arrangement without deciding that deliberately.

---

## 6. Verdict: REFUSED — with the inventory, and what the next packet should be

Applying the plan's §4 rule mechanically: **B2 (ghost mode) is essential by the owner's own title,
is labelled GAP, and has no landed substitute. That alone forces REFUSED.** B3 is OWNER-GATED, which
independently blocks pricing the surface.

**This is not "it is large".** `Services/Fyp/` is 3237 lines and the payload is 8 files; the code
volume is unremarkable. The refusal is about **shape**:

1. **The directory is not the feature.** `Services/Fyp/Online/` (1378 of the 3237 lines, 42%) is the
   app-wide remote-media subsystem, with 12 compile-time consumers outside itself.
2. **The title behaviour has no port mechanism on either OS**, and on Wayland it may have none at all.
3. **One of the seven phrases needs a capability the port has not landed** and that the owner has
   reserved.

### What IS separable, and should be authored next instead

The census found a genuinely buildable unit that the board row does not name:

> **Remote media as its own row.** `IFeedSource` + `FypOnlineCoordinator` + `RemoteMediaFormats` +
> `RemoteMediaCache` + `ScrolllerSource` (1378 lines, 5 files, zero Win32) is pure networking, JSON,
> caching and rotation policy. It is **already** what DTRH and Intake need — both shipped in the port
> — and it has no overlay, no DWM, no webcam and no WebView dependency. It is cross-platform by
> construction and headlessly testable.

That unit is **owner-gated on §5.1, not capability-gated.** The decision needed is "does the port
take a dependency on scrolller.com", and no amount of engineering answers it.

### Named residue, so nothing is lost

| Item | Disposition |
|---|---|
| Ghost mode (B2/B4) | GAP. Needs a DWM-thumbnail investigation on Windows and a Wayland/X11 feasibility answer that may be "no". |
| Gaze scrolling (B3) | OWNER-GATED. Blocked behind the webcam capability decision, `capability-inventory.md:70`. |
| Show Desktop survival (B6) | GAP. Needs a WndProc hook seam; Linux form must be re-derived, not ported. |
| Feed page + payload (B1) | COVERED on Windows by the DTRH WebView; Linux unproven. |
| Any-monitor (B5) | PARTIAL. Needs foreign-HWND-to-display resolution. |
| Notice-and-swap (B7) | PARTIAL. Pure logic, cheapest item in the inventory. |
| Premium entitlement | Out of scope here and already a named blocker: every entry point runs a premium gate keyed `"fyp"` — `TierGate.RequiresPremium(Loc.Get("tab_fyp"), "fyp")` at `App.xaml.cs:2462`, and `TierGate.DemandPremium(Loc.Get("tab_fyp"), "fyp")` at `MainWindow/MainWindow.Lab.cs:291`, which delegates to the same predicate via `Services/TierGate.cs:103`. |

---

## 7. Divergences recorded (D207 onward)

Written into `client/docs/wpf-surface-reachability.md` per the standing obligation.

- **D207** — For You Feed has no port entry point and will not get one from this packet.
- **D208** — `Services/Fyp/Online/` is recorded as an app-wide subsystem, not part of the FYP surface.
- **D209** — FYP settings evidence is read from `CCP.Core/Models/AppSettings.cs` because the shipping
  product project-references it (`ConditioningControlPanel.csproj:52`).

---

## 8. What this census does NOT prove

- **Nothing was built, run, or rendered.** No FYP code exists in `client/`; `client/src/**` was closed
  to this packet and no product code was written.
- **No headed evidence of any kind.** No window was shown, no frame composited, no pixel compared.
  Every capability claim above is a claim about *source*, and B1's "COVERED" means the DTRH WebView
  exists and ships — **not** that the FYP page renders in it.
- **Linux is unproven for every row without exception.** `wsl.exe --list --verbose` reports no
  installed distributions on this machine (`client/memories/port-status.md:89-96 @ a8d32c219`), so every Linux
  cell is a named gate, never a discharge.
- **The Wayland claim in B2 is reasoning, not a measurement.** I did not test a Wayland compositor.
- **The 12 compile-time consumers were derived by grep, not by a compiler.** A consumer reaching these
  types through reflection or a generated partial would not appear.

---

## 9. Pinned enumeration (parsed by `FypCensusTests`, re-derived from the shipping bytes)

This section is the DATA; `client/tests/CcpClient.Tests/FypCensusTests.cs` is the LOGIC. The guard
walks `ConditioningControlPanel/` **by directory, recursively** on every run and compares what the
shipping bytes actually contain against the tables below. Editing this document can never shrink the
search: the directory roots and the type-name needles live in the test, not here.

### 9.1 Product files — `Services/Fyp/`, walked recursively

| Id | Path |
|---|---|
| P1 | Services/Fyp/FypAssetManifest.cs |
| P2 | Services/Fyp/FypGhostOverlay.cs |
| P3 | Services/Fyp/FypHostService.cs |
| P4 | Services/Fyp/FypMetaStore.cs |
| P5 | Services/Fyp/Online/FypOnlineCoordinator.cs |
| P6 | Services/Fyp/Online/IFeedSource.cs |
| P7 | Services/Fyp/Online/RemoteMediaCache.cs |
| P8 | Services/Fyp/Online/RemoteMediaFormats.cs |
| P9 | Services/Fyp/Online/ScrolllerSource.cs |

### 9.2 Consumers outside the surface directory

Every file in the shipping tree (excluding the `CCP.*` first-attempt projects and `Services/Fyp/`
itself) that names one of the surface's nine types. `kind` is `code` for a compile-time consumer and
`comment` for a documentation reference; the split is census data, the SET is re-derived.

| Id | Path | Kind |
|---|---|---|
| C1 | App.xaml.cs | code |
| C2 | Chaos/ChaosFlashOverlay.cs | comment |
| C3 | Chaos/ChaosGifCascadeOverlay.cs | comment |
| C4 | MainWindow/MainWindow.Assets.cs | code |
| C5 | MainWindow/MainWindow.Lab.cs | code |
| C6 | MainWindow/MainWindow.xaml.cs | code |
| C17 | Models/AppSettings.cs | comment |
| C18 | Services/Arcademy/ArcademyHostService.cs | code |
| C7 | Services/AutonomyService.cs | code |
| C8 | Services/BubbleCountService.cs | code |
| C9 | Services/Chaos/DtrhAssetManifest.cs | code |
| C10 | Services/Flash/FlashService.cs | code |
| C11 | Services/Quiz/IntakeHostService.cs | code |
| C12 | Services/Video/VideoService.cs | code |
| C13 | Services/Video/WallpaperService.cs | code |
| C14 | Views/Tabs/AssetsTabView.xaml | comment |
| C15 | Views/Tabs/PlayTabView.Cards.cs | comment |
| C16 | Views/Tabs/PlayTabView.xaml | comment |

### 9.3 Payload — `Resources/web/fyp/`, walked recursively

| Key | Value |
|---|---|
| payload-files | 8 |
| disposition | not-forked |
