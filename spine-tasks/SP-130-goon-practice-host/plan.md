# SP-130 — plan checkpoint (Review Level 3, Step 1)

Written BEFORE any product edit. Nothing under `client/src/**` has been touched.

Every citation below was OPENED in this worktree, not inherited from the census or the packet.

---

## 0. The shape of the unit, restated so the plan can be checked against it

The game exists twice and I am building the HOST (`GoonHostService.cs:25-27`, opened). Practice runs
entirely in the page over an in-process loopback pair (`ui/soloDriver.js:1-18`,
`net/loopbackTransport.js:19-23`, both opened). **No duel logic is ported. No payload byte is
forked.** What the host owes is: serve the tree, answer the frames, and refuse the rest honestly.

---

## 1. The frame subset, with the citation opened for each

### 1.1 Host -> page — 4 emitted, 1 typed-never-emitted

| Frame | Why it is in the subset | Citations opened |
|---|---|---|
| `init` | The packet's core item; written field-for-field twice, so transcribed | `GoonHostService.cs:306-352` (the `_host?.Post(new { type = "init", ... })` block) and `bridge.js:387-470` (`standaloneInit`) |
| `manifest` | The packet's second item | `GoonHostService.cs:372-378`; port halves at `DtrhProtocol.cs:268-277` (`BuildManifest`) and `Features/Dtrh/DtrhUserMedia.cs` (`Build`) |
| `end-run { reason }` | **The exit handshake does not close without it.** The page sends `exit`, then waits: `bridge.on('end-run', () => finishExit('end-run'))` at `boot.js:379`; without an answer the page falls through its own 1.2 s fallback (`boot.js:2436-2442`, `:2449`). A host that never answers makes window-close a 1.2 s stall on every exit | `GoonHostService.cs:34` (catalogue); `boot.js:379`, `:2440-2465`; port precedent `IntakeHostWindow.axaml.cs:112` |
| `fullscreen { on }` | The catalogue says **"always the REAL window state"**; the page reads only the echoed state and its F11 sends `fullscreen-set` (`boot.js:2504-2513`). Echoing the actual state is what stops F11 being a dead key | `GoonHostService.cs:32`; `boot.js:372`; port precedent `IntakeHostWindow.axaml.cs:449-455` |
| `ping` | **Typed, NEVER emitted.** The page answers `ping` with `pong` (`boot.js:377`), but this host ships no paint-stall prod (upstream's is `GoonHostService.cs:74-80`, `PaintStallSeconds`). Classified rather than silently absent | `GoonHostService.cs:33`; `boot.js:377`; the typing discipline is `IntakeProtocol.cs:44-77` (`IntakeEmitClass.PageAttestedNeverEmitted`) |

### 1.2 Page -> host — 9 handled, everything else typed out-of-vocabulary

`ready` (`boot.js:2637` via `bridge.announceReady`, `bridge.js:104`), `log` (`bridge.js:97-101`),
`heartbeat` (`boot.js:2604-2614`), `pong` (`boot.js:377`), `boot-error` (`bridge.js:107`),
`fullscreen-set` (`boot.js:2508-2511`), `exit` (`boot.js:2448`), `exit-done` (`boot.js:2464`),
plus **`net-post`** — see 1.3.

Every other page frame in the catalogue (`GoonHostService.cs:44-53`: `cache-req`, `cache-put`,
`encode-done`, `goon-recv-*`, `discord-*`, `rp-state`, `last-opponent-clear`, `peer-card-req`,
`toy-pattern`, `toy-stop`, `match-result`) parses to a typed `UnknownType`/out-of-vocabulary outcome:
logged presence and shape, never acted on, never dropped silently
(`IntakeHostWindow.axaml.cs:396-410` is the port's precedent).

### 1.3 THE ONE ADDITION TO THE PACKET'S NAMED SUBSET, and its argument

The packet names `ready`/`log`/`heartbeat`/`pong`/`exit`/`exit-done`. **I want to add `net-post`
-> `net-post-result`, answered locally with a typed refusal and never proxied.** Reviewer may strike
it; here is the reason I think it belongs.

- `bridge.js:170-180` (opened): hosted with `viaHost` true, EVERY server call the page makes becomes
  a `net-post` frame and a promise that resolves only when the host answers — or after
  `NET_TIMEOUT_MS = 45000` (`bridge.js:135`).
- So without it, a user pressing **Join** gets a 45-second spinner and then "could not reach the
  server". That is precisely *a door that looks like it works*, for 45 seconds, on the packet's own
  sharpest requirement.
- Answering it is also **the enforcement point for "no network"**: `viaHost: true`
  (`GoonHostService.cs:328`) routes 100% of the page's server traffic into a host that refuses it
  in-process. The alternative (`viaHost:false`) makes the page issue a real `fetch()` itself
  (`bridge.js:182-192`) — a socket I would rather not create even against a loopback origin.
- The frame is in the upstream catalogue (`GoonHostService.cs:36`), so it is transcription, not
  invention. Upstream proxies it (`OnNetPost`, guarded by `AllowedPathPrefix` at `:69`); this host
  refuses it. That difference is D252.

---

## 2. The three consent defaults, re-verified by opening each line

| Value | Line as it reads today | Reaches the frame at |
|---|---|---|
| `liveDurationSec` **720** | `GoonContracts.cs:97` — `public const int LiveDurationSecDefault = 720;      // 12 min` | `GoonHostService.cs:343` |
| `payloadMinGapMs` **30000** | `GoonContracts.cs:108` — `public const int PayloadMinGapMs = 30000;           // 1 per 30 s ...` | `GoonHostService.cs:345` |
| `toyCap` **0.7** | `GoonContracts.cs:297` — `[JsonProperty("toy_cap")] public double ToyCap { get; set; } = 0.7;   // can only LOWER the receiver's own cap` | `GoonHostService.cs:344` |

All three verified on the cited line, not merely in the file. They are read off `ConsentSheetMsg`
(`GoonHostService.cs:310`, *"the engine's own defaults, never a fork"*) — which is why the port
transcribes the three constants and does not port `GoonContracts.cs`.

**Discrepancy recorded, not silently resolved:** the standalone frame sends `toyCap: 0`
(`bridge.js:474`) where the C# host sends `0.7`. I follow the HOST, because this is a host. The
packet and the census both say 0.7 and both are right.

### 2.1 `caps`, per field, with what each is transcribed from

| Field | Value | Source opened |
|---|---|---|
| `haptics` | `false` | `GoonHostService.cs:333` (*"haptics v2 is not merged"*) |
| `brainDrain` | `true` | `GoonHostService.cs:334` -> `BrainDrainAllowed()` at `:880`, which is literally `=> true` |
| `spiral` | `true` | `GoonHostService.cs:335` (*"in-page spiral veil; exec/ owns the renderer"*) |
| `camera` | `false` | `GoonHostService.cs:336` (*"no webcam bridge into the page in v1"*) |
| `video` | `true` | `GoonHostService.cs:337` |
| `mediaTransfer` | `false` | `GoonHostService.cs:338` -> `TransferAllowed()` at `:894` reads `App.Patreon.HasPremiumAccess`. The port has a tier SOURCE and no tiers (D246), so the honest local answer is false |
| `canHost` | `false` | `GoonHostService.cs:339` -> `HostingAllowed()` at `:909` reads `HasLabAccess`. Same reasoning |
| `assetCache` | `false` | **Not a C#-host field at all.** Taken from the standalone shape at `bridge.js:415-419`, because the port ships no `GoonCacheBridge` (upstream attaches it at `GoonHostService.cs:383`). Recorded as D253 |
| `solo` | `true` | The C# host sends no `solo`; the standalone default is `q.get('solo') !== '0'` (`bridge.js:391`). The packet directs it on. Its ONLY consumer is `session.solo` at `boot.js:323`, used at `boot.js:346` in a log line — grep-verified across the tree. Recorded as D256 |

`identity`: `unifiedId: ""` (upstream `App.UnifiedUserId ?? ""`, `:313`), `displayName: "Player"`
(upstream `SafeDisplayName()` at `:860-868`, whose own fallback is the literal `"Player"`; the port
has no display-name setting — grep-verified), `appVersion` from
`VersionSelfCheck.GetInformationalVersion` (`client/src/CcpClient.Desktop/VersionSelfCheck.cs:18`),
which is the port's version authority.

`net`: `serverBase: ""`, `authToken: ""`, `viaHost: true`. Upstream sends the proxy base
(`:64`, `:322`) and the CCP auth token (`:328` -> `SafeAuthToken()` at `:851-855`). **The port reads
no token and names no server.** D251, D252.

`discord`: absent (upstream builds a block at `:350`). No Discord in the port. D257.

`fullscreen`: the real window state, per the catalogue at `:32`.

**A consequence I will state rather than let a reader infer:** `caps.haptics:false` makes the page
advertise **eight** of the nine element kinds — `boot.js:580` only pushes `GoonElement.ToyPatterns`
when `caps.haptics` is truthy, and there are nine in `core/contracts.js:9-19`. The census §7.1 says
a practice duel exercises *"all nine element kinds"* in the same table that pins `haptics:false`.
**That is imprecise for every host including the shipping WPF one**, and it is a finding I report
rather than a file I edit (the census is CLOSED to me). Recorded as D255.

---

## 3. How the four owner-gated doors refuse — and the trap inside the trap

### 3.1 What I found when I opened the page's own refusals

The title menu is built at `ui/screens/title.js:44-98`. **It renders up to eight items**, not five:
Host, Join, Practice, Assets, Voice, Options, How-it-works, and Quit (hosted only). `title.js:4`'s
own header comment says *"a five-item menu"* — the comment is stale against its own code. The
packet's and census's "1 of 5" is inherited from that comment. Reported, not edited.

Now the part that changes the design. Each of the four doors, left to the page, tells the user
something **false in this build**:

| Door | What the page says with my caps | Opened at | Why it is false here |
|---|---|---|---|
| Host | *"hosting is a supporter perk — joining a room is always free"*, item dimmed | `title.js:71-84`, string `strings.js:41` | Implies a purchase would open it. Nothing sells hosting in the port; it is owner-gated and unbuilt |
| Join | routes to the join screen -> signaling -> `net-post`; the only sheets reachable are *"Could not reach the server / check your connection"*, *"Warming up… try again in a minute"*, *"Signed out"*, *"Hosting is a supporter perk"* | `sheets.js:120-170`, `strings.js:940-985`, mapping at `signaling.js:503-560` | Every one of them blames the network, the clock or the account. There is no server to be warming up |
| Voice notes | *"sending your voice is a supporter perk"*; recording, playback and the library stay live on purpose | `voice.js:113-140`, `strings.js:697` | Same false implication, and the screen is explicit that the perk gates only the crossing |
| Media setup / Assets | *"compression lives in the app — this page is running in a plain browser…"* | `assets.js:83`, `:587-599`, `strings.js:751-752` | The page **is** running in the app. The sentence is simply untrue in this build |

I checked for a route by which a host can put its own sentence on the page and there is not one:
`showSignalError`'s default branch appends `detail` (`sheets.js:164-170`), but `lastErrorDetail` is
only ever set for HTTP 402 (`signaling.js:542`), so an arbitrary host sentence cannot reach it.

### 3.2 The refusal design I will build

1. **A typed refusal model** — `GoonDoorRefusal`, one record per door, each naming *what is missing*
   and carrying its owner-gate citation (`§6.1`/D243 for Host+Join, `§6.3`/D244 for voice notes,
   `§6.2`/D248 for media setup). This is the port's own vocabulary (`CapabilityState.Unavailable`,
   `IntakePassDecision.RefusedUndeterminable`, `DtrhProcessFailed.AttachOutcome.Unavailable`).
2. **The caps frame is DERIVED from that model**, not written beside it: `canHost:false` exists
   because the Host refusal exists. A future author cannot re-open a door without deleting its
   refusal, and a test pins exactly that.
3. **The refusal text is shown in HOST chrome** — an always-visible panel in the Avalonia window,
   outside the WebView — because the page's four strings are false here and I may not fork payload
   bytes to fix them. The truthful sentence is beside the page, permanently, not behind a click.
4. **`net-post` is refused in-process** (1.3), so Host and Join fail *immediately* and *without a
   socket* instead of hanging 45 seconds.
5. The page-side dimming/cards still happen (they are driven by my caps) and are **left in place**:
   a missing menu row reads as a broken build (`title.js:62-63` argues exactly this). The port adds
   the true reason; it does not hide upstream's.

Residue named rather than absorbed: correcting the four page strings needs either an overlay shadow
of `ui/strings.js` (forking payload bytes — refused) or a host-authored refusal frame that upstream
does not have. **D254.**

### 3.3 The microphone, which is a REAL residual and not a rhetorical one

Upstream grants the WebView2 microphone permission unprompted (`GoonHostService.cs:489`, D244). This
host grants nothing. But the voice screen is reachable and its recorder calls `getUserMedia`
(`ui/voice/recorder.js`), and with no `PermissionRequested` handler **WebView2's own prompt** is what
answers. The port cannot currently hook that event: Avalonia exposes `CoreWebView2` only as a raw
pointer and every use of it so far is hand-rolled COM vtable interop
(`Features/Dtrh/DtrhProcessFailed.cs:26-60`).

So: **the port never opens a sensor and never grants one; a determined user can still be prompted by
the browser.** That is a NAMED LIMIT (D250), not a claim of "no microphone". I will not invent a
Chromium switch for it — CLAUDE.md forbids guessing flags. If the reviewer wants it closed, that is
a `PermissionRequested`-deny packet of its own.

---

## 4. What I will build, concretely

All new, all under `client/src/CcpClient.Desktop/Features/Goon/`:

| File | What it is | Precedent opened |
|---|---|---|
| `GoonServingRoots.cs` | Typed payload probe: `Present`/`Missing`/`Incomplete` over `payload/goon` beside the exe; required files `index.html`, `boot.js`, `bridge.js` | `Features/Intake/IntakeServingRoots.cs` (whole file) |
| `GoonParticipant.cs` | Owns the §4 server + per-session bridge token. **Reuses `LoopbackServer` UNMODIFIED** with `overlayRoot = payloadRoot = payload/goon` | `Features/Intake/IntakeParticipant.cs:88-104`; `LoopbackServer(payload, payload, media, …)` already exercised at `DtrhLoomTests.cs:153` |
| `GoonProtocol.cs` | The typed vocabulary: `init`/`manifest`/`end-run`/`fullscreen` builders, `ping` typed-never-emitted, the 9 page frames + tolerance outcomes | `Features/Intake/IntakeProtocol.cs` |
| `GoonDoors.cs` | The four typed refusals and the caps derived from them | `Capabilities/CapabilityState`, `IntakePassGate` |
| `GoonHostWindow.axaml(.cs)` | The window: refusal chrome + `NativeWebView` on Windows, typed honest-unsupported elsewhere; bridge wiring; exit flow | `Features/Intake/IntakeHostWindow.axaml(.cs)` |
| `GoonLaunch.cs` | THE one construction site | `Features/Intake/IntakeLaunch.cs` |

Reused read-only from `Features/Dtrh/` (already cross-referenced by `Features/Intake/`, so this is
the established shape, not a new dependency): `LoopbackServer`, `Inbox`, `DtrhUserMedia`,
`DtrhProtocol.DtrhManifestEntry`, `DtrhWatchdog`, `DtrhExitFlow`, `DtrhProfileLock`,
`DtrhCapabilityProbes`, `DtrhProcessFailed`.

**csproj**: the fifth linked read-only glob, byte-identical in shape to `:50-54`:
`..\..\..\ConditioningControlPanel\Resources\web\goon\**\*` -> `payload\goon\...`. **Zero bytes
forked.**

### 4.1 The server decision, and the defect it inherits

I will **reuse `LoopbackServer`** rather than write a Goon-shaped origin, and the reason is that the
goon page plays the user's own **videos**: `LoopbackServer` already ships Range 206/416, the CORS
page/media origin split, traversal refusal, nosniff and deny-by-default 415, all tested. Re-writing
that (the `ChaosTunnelLoopback` route) to get a prettier route prefix would be re-deriving Range
handling for cosmetics. `LoopbackServer` is also OUT of my File Scope, which settles it: I use it
unmodified, exactly as `IntakeParticipant` does.

The inherited cost, measured not guessed: the goon tree's extensions are
`.js 120, .mp3 37, .png 15, .css 5, .json 2, .html 1, .mjs 1, .webmanifest 1` plus two extensionless
`LICENSE` files. `LoopbackServer`'s pinned allowlist (`:24-34`) has nine and **lacks `.mjs` and
`.webmanifest`**, so exactly two files would answer 415:

- `manifest.webmanifest`, requested by `index.html:22` — and `index.html:19` says the PWA chrome is
  *"inert"* under WebView2;
- `vendor/mp4-muxer/mp4-muxer.mjs`, imported only by `encode/encodeWorker.js:37` and
  `test/selftest-encode.js:92` — the encode lane, which `assetCache:false` switches off.

Neither is on the practice boot path. I will pin this with a drift guard (the
`ChaosTunnelLoopbackTests` extension-sweep shape) so a new extension in the goon tree reds the suite
instead of 415-ing silently. **D258.**

---

## 5. Evidence class I expect to reach, per trap 5's three claims

| Claim | Class it needs | What I expect to reach |
|---|---|---|
| The payload **ships** | build-output assertion | **DISCHARGED.** A test walks `payload/goon` in the test assembly's output, counts 184, and names the boot files |
| The page **loads and the handshake completes** | bridge traffic / console / real capture | **NOT REACHED** — see §6. It needs a running window, and no entry point exists inside my File Scope |
| A **duel is playable** | headed | **NOT REACHED, and owed.** A headed gate is never dischargeable by a headless frame |

So, in the packet's own words, I expect to reach **only the first** of the three, and I will name the
second and third as owed. A compile is not a load, and a load is not a duel. Everything else I ship
is a *shape* proof: the frames serialize field-for-field to what the two upstream copies say, and the
refusals exist and are typed. **That is not evidence that the page accepted them.**

---

## 6. THE BLOCKER: the door cannot be opened from inside File Scope

`GoonLaunch` is the construction site, but something must call it, and every caller lives outside my
File Scope:

- the user door would be `client/src/CcpClient.Desktop/Views/Pages/PlayPage.axaml(.cs)` +
  `Views/MainWindow.axaml.cs:61-71` (where `DtrhLaunch` and `IntakeLaunch` are constructed);
- the headed-evidence door would be `client/src/CcpClient.Desktop/App.axaml.cs` (where `--intake-demo`
  reaches past the gate at `:280-311`).

**I will not widen scope silently.** Two options, smallest first, for the reviewer to grant or refuse:

- **(A) harness flag only** — ~6 lines in `App.axaml.cs` mirroring `--intake-demo`. Ships no user
  door; **unlocks claim 2** (a real load, real handshake, real bridge traffic) and makes claim 3
  attemptable on DISPLAY3.
- **(B) the user door** — (A) plus ~10 lines across `Views/MainWindow.axaml.cs` and
  `Views/Pages/PlayPage.axaml(.cs)`. This is what the packet's outcome sentence ("a person opens the
  door") actually requires.

If both are refused, the honest outcome is a host that is complete, tested at the shape level, and
**unreachable** — and I will say exactly that, with the reason, rather than dress it up.

A second, smaller scope note: File Scope names one new test file, in the **unit** project. So the
window's visual tree and its refusal chrome get **no headless coverage** and my headless delta is
**0**. If the reviewer wants the four refusal rows pinned in a real visual tree, File Scope needs
`client/tests/CcpClient.HeadlessTests/GoonPracticeHeadlessTests.cs`.

---

## 7. Tests (all in `client/tests/CcpClient.Tests/GoonPracticeTests.cs`), each with the edit it reds on

| Fact | Reds on this edit |
|---|---|
| `payload/goon` in the build output holds 184 files incl. `index.html`/`boot.js`/`bridge.js`/`ui/soloDriver.js` | deleting or narrowing the csproj glob |
| No file under `client/` (excluding `bin`/`obj`) has the SHA-256 of any `Resources/web/goon` file | copying a payload byte into `client/` |
| `init` serializes to the exact field set and values of the two upstream copies | renaming, dropping or inventing one field |
| The three consent defaults are 720 / 0.7 / 30000 | changing one |
| `caps` is exactly the 9 keys with the pinned values | flipping `canHost`/`mediaTransfer`/`assetCache` on |
| `manifest` is `{type, images, videos, skipped, truncated, received}` with `received` empty, over a temp media root | dropping `received`, or renaming a field |
| Source-text guard over `Features/Goon/**`: no `HttpClient`, no `getUserMedia`, no token read, no non-loopback absolute URL | adding an outbound call, a sensor or a token read |
| Exactly four typed door refusals; each names what is missing; none contains upstream's "supporter perk" framing | adding a fifth, or re-using the false string |
| The caps frame is derived from the refusals | opening a door without deleting its refusal |
| `net-post` produces an immediate typed `net-post-result` refusal and no request object | proxying it |
| The 9 page frames parse typed; unknown/malformed/forward-version are tolerated outcomes | dropping a frame silently |
| Payload probe types `Present`/`Missing`/`Incomplete` | substituting a tree |
| The goon tree's extension set minus `LoopbackServer`'s allowlist is exactly `{.mjs, .webmanifest}` | upstream adding an extension, or the allowlist drifting |

Every one of these will be demonstrated red **at the committed head** before it is trusted, per trap 6.

Floor delta will be declared in `spine-tasks/SP-130-goon-practice-host/floor-delta.json` once the
count is real. Pin is 2457 unit / 144 headless; expected observed = 2457 + my unit delta, 144 + 0.

---

## 8. What I will NOT build

- **No duel logic.** Nothing from the 25 C# files is ported as code; three integer/double constants
  are transcribed and that is all.
- **No forked payload bytes**, and no overlay shadow of any goon file — including `bridge.js`, which
  needs none: goon's own bridge speaks `window.chrome.webview` (`bridge.js:45-47`, `:65-67`, `:93-95`)
  and the port's host->page path is the same synthetic `MessageEvent` dispatch DTRH and intake
  already use (`IntakeHostWindow.axaml.cs:658`).
- **No network**: no `HttpClient`, no server base, no proxy, no `net-post` forwarding.
- **No microphone, no camera** (and no grant of either).
- **No entitlement read**, no tier lookup, no token.
- **No Discord block**, no transfer cache, no inbox store, no `received` rows.
- **No Linux claim.** The embedded WebView is Windows-only by the port's own probe
  (`DtrhCapabilityProbes.cs:35-44`); Linux gets the typed honest-unsupported surface, never a faked one.
- **No headed claim on headless evidence.**

## 9. Divergences reserved: D250-D259

D250 microphone residual · D251 no token · D252 no server, `net-post` refused in-process ·
D253 `assetCache:false` (a field the C# host does not send) · D254 the four page strings are false
here and cannot be corrected without forking bytes · D255 eight of nine elements with `haptics:false`
· D256 `solo:true` where the C# host sends no field · D257 no `discord` block · D258 the `/dtrh/`
route class and the two 415 extensions · D259 reserved for the entry-point outcome.

## 10. Discrepancies found so far (reported, not edited)

1. `title.js:4` says *"a five-item menu"*; `title.js:44-98` renders up to eight. The packet's and
   census §7.1's "1 of 5" inherits the stale comment.
2. Census §7.1 says a practice duel exercises *"all nine element kinds"*; with the `haptics:false`
   pinned in that same table, `boot.js:580` yields **eight**. True of the shipping WPF host too.
3. `client/docs/upstream-payload-inventory.json` types `goon` as `not-ported`. This packet makes it
   served, and `UpstreamPayloadInventoryTests` does **not** check disposition drift — so the entry
   goes stale silently. The file is not in my File Scope; flagged for the orchestrator.
