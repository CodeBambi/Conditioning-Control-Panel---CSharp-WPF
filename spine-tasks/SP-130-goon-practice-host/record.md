# SP-130 — record

Branch `lane/SP-130-goon-practice-host`, base `e3aee3e21`. Plan checkpoint at `3dc52dde5`,
implementation at `84a37016b`.

---

## 1. The frame subset, with the citation opened for each

The frame catalogue is `GoonHostService.cs:29-54` (the packet said `:30-53`; the block is `:29-54`
and the row lines below are the corrected ones).

### Host -> page

| Frame | Catalogue | Built from | Why it is in the subset |
|---|---|---|---|
| `init` | `:29` | `GoonHostService.cs:311-354` + `bridge.js:387-470` | The census's first named item; written field-for-field TWICE, so transcribed |
| `manifest` | `:30` | `GoonHostService.cs:372-378`, over `DtrhProtocol.cs:268-277` + `Features/Dtrh/DtrhUserMedia.cs` | The census's second named item |
| `fullscreen {on}` | `:31` | consumed at `boot.js:372`; F11 sends `fullscreen-set` at `boot.js:2504-2513` | The page reads only the ECHOED state, so without the echo F11 is a dead key |
| `end-run {reason}` | `:33` | consumed at `boot.js:379` | The exit handshake does not complete without it; the page otherwise falls through its own 1.2 s fallback (`boot.js:2449`) |
| `net-post-result {id,status,body}` | `:34` | `GoonProtocol.BuildNetPostResult` | THE REFUSAL — see §3 |
| `ping` | `:32` | — | **TYPED, NEVER EMITTED.** The page answers it with `pong` (`boot.js:377`); this host ships no paint-stall prod (upstream's is `GoonHostService.cs:74-80`) |

### Page -> host — 9 handled

`ready` (`:42`, sent at `bridge.js:106`), `log` (`:42`, `bridge.js:97-101`), `heartbeat` (`:43`,
`boot.js:2604-2614` — `paint` is OMITTED not zeroed at `:2606-2610`, and is nullable here for that
reason), `pong` (`:43`, `boot.js:377`), `boot-error` (`:43`, `bridge.js:109`), `fullscreen-set`
(`:43`, `boot.js:2508-2511`), `exit` (`:44`, `boot.js:2448`), `exit-done` (`:44`, `boot.js:2464`),
`net-post` (`:47`, `bridge.js:169-179`).

Everything else in the catalogue — the transfer-cache family (`:48-49`), the received-inbox family
(`:50-51`), the Discord family (`:52-54`), and the two upstream stubs at `:45`/`:46` — parses to a
typed out-of-vocabulary outcome, logged by name and never acted on.

## 2. The three consent defaults, verified on the cited line

| Value | The line, as it reads | Reaches the frame at |
|---|---|---|
| `liveDurationSec` **720** | `GoonContracts.cs:97` — `public const int LiveDurationSecDefault = 720;      // 12 min` | `GoonHostService.cs:343` |
| `toyCap` **0.7** | `GoonContracts.cs:297` — `[JsonProperty("toy_cap")] public double ToyCap { get; set; } = 0.7;` | `:344` |
| `payloadMinGapMs` **30000** | `GoonContracts.cs:108` — `public const int PayloadMinGapMs = 30000;` | `:345` |

Upstream reads them off `ConsentSheetMsg` — *"the engine's own defaults, never a fork"* (`:310`) —
so the port transcribes three constants and ports none of `GoonContracts.cs`. The page's own
standalone frame sends `toyCap: 0` (`bridge.js:474`); the HOST's value is 0.7 and this is a host.

## 3. How each of the four doors refuses

The page already refuses all four **and every one of its sentences is false in this build**:
`ui/strings.js:41` ("hosting is a supporter perk"), `ui/sheets.js:120-170` (network / warming-up /
signed-out), `ui/strings.js:731` via `ui/screens/voice.js:138-140` ("sending your voice … is a
supporter perk"), `ui/strings.js:751-752` via `ui/screens/assets.js:83` ("running in a plain
browser"). There is no host->page route for a true sentence: `ui/sheets.js:164-170`'s `detail` slot
is written only for HTTP 402 (`net/signaling.js:542`). Correcting the strings means forking payload
bytes. **So the truthful refusal is host chrome.**

| Door | How it refuses, mechanically | What the user is told |
|---|---|---|
| **Host** | `caps.canHost=false` (derived) dims the page item; every `/invite` call is refused in-process | "Hosting a duel is not in this build. This build has no outbound network connection of any kind: no duel server client, no signaling, no peer connection. Nothing here is waiting on a payment or a sign-in." + census §6.1 / D243 |
| **Join** | every `/join` call refused in-process, immediately, with the refusal's own text in the body | "Joining a duel is not in this build. The same missing piece as Host … Joining is free upstream; here it is absent." + census §6.1 / D243 |
| **Voice notes** | `caps.mediaTransfer=false` (derived); no permission granted to the page | "Voice notes are not in this build. Recording one opens a microphone. This build opens no capture device of any kind and grants no microphone permission to the page (the shipping app grants it unprompted; this host does not)." + census §6.3 / D244 |
| **Media setup** | `caps.assetCache=false` + `caps.mediaTransfer=false` (both derived) | "Sending your own media is not in this build. There is no compression queue and no peer channel to send over … Your media is listed for this machine's own page and goes nowhere else." + census §6.2 / D248 |

All four rows are rendered on a docked, always-visible rail in the host window, built from
`GoonDoors.Refused` — and **the caps are COMPUTED from that same list** (`GoonDoors.CapsFor`), so a
door cannot be re-opened on the wire without deleting the refusal that explains why it is shut.

## 4. The guards, and the edit each was demonstrated to red on

Every demonstration was made **at the committed head `84a37016b`**, then reverted.

| Edit made | What went red |
|---|---|
| `CapsFor`'s three derived members rewritten as literals `false` | `Caps_AreComputedFromTheRefusalSet_NotWrittenBesideIt` — 1 failed / 82 passed |
| `bridge.js` copied to `Features/Goon/forked-bridge.js` | `NoGoonPayloadByte_IsForkedIntoTheClientTree`, naming the file and its upstream twin |
| `solo` moved from the frame root into `caps` | `Init_CarriesExactlyTheUpstreamRootFields`, `Init_IsProtocolOne_AndSoloIsOnAtTheFrameRoot`, `Init_Caps_AreExactlyEightMembers_InTheUpstreamOrder` — 3 failed |
| The Host refusal's text made to say "supporter perk" | `NoRefusal_BorrowsThePagesFalseFraming(banned: "perk")` |
| The Host refusal DELETED | unit: 11 failed, including `ThisBuildsCaps_RefuseAllThreeGatedMembers` (canHost really re-opens on the wire) and `ExactlyFourDoors_AreRefused…`; headless: `TheRefusalRail_CarriesOneRowPerOwnerGatedDoor` and `TheRefusalRail_NeverRepeatsThePagesSupporterPerkFraming` |
| A `.svg` added to the served output tree | `TheServedTreesDeniedFiles_AreExactlyTheFourNamedOnes` and `PayloadGlob_CopiesTheWholeUpstreamGoonTree_IntoTheBuildOutput` |

## 5. Evidence class reached, per claim

| Claim | Class it needs | Reached |
|---|---|---|
| The payload **ships** | build-output assertion | **YES.** 184 files under `payload/goon` in the build output, relative-path-equal to the upstream tree and SHA-256 identical file by file; zero bytes forked into `client/` |
| The page **loads and the handshake completes** | bridge traffic / console / a real capture | **NO.** Not attempted. Every frame fact here is a serialization shape, not a page that accepted it |
| A **duel is playable** | headed | **NO, and owed.** A headed gate is never dischargeable by a headless frame |

**A compile is not a load, and a load is not a duel.** This packet reached the first only. The host
window is never SHOWN in any test — showing it builds a real embedded browser — so surface
selection, WebView2 navigation, the duck/restore, the microphone residual and the whole exit
handshake are **unexercised**. Linux is a named gate: the typed unsupported surface is justified by
`DtrhCapabilityProbes.cs:35-43`, but the gate that would confirm it is a WSLg/X11 run observing that
surface, and none was made.

## 6. Floor

Pin **2457 unit / 144 headless**. Declared delta: **+83 unit / +8 headless**
(`floor-delta.json`). Observed at the last full run: **2540 unit / 152 headless** = pin + delta,
exactly. `floor.json` was never opened.

## 7. Blockers and scope

1. **`Assets/assets.manifest.json` needs 184 new copied entries, and it is outside File Scope.**
   `AssetVerifier.VerifyCopied` (`Manifest/AssetManifest.cs:296-327`) sweeps every file under each
   copied top-level root and fails any without a manifest entry, so the new `payload/goon/**` files
   fail `AssetManifestTests.CopiedDirection_RealManifest_…` and `SelfCheck_RealAssembly_ExitZero_…`.
   The remedy is exactly two edits: 184 entries in
   `client/src/CcpClient.Desktop/Assets/assets.manifest.json` (the shape SP-023/SP-054/SP-061 used
   for dtrh/intake/tunnel) and the pinned copied count in
   `client/tests/CcpClient.Tests/AssetManifestTests.cs:144` from **3700** to **3884**. Every prior
   payload packet carried `assets.manifest.json` in its File Scope; this one does not. **Not made.
   Reported.**
2. **`--goon-demo` granted but not built.** The demo flags are parsed in
   `client/src/CcpClient.Desktop/Program.cs:230,255` and threaded into `new App(...)` at `:335`. A
   flag added to `App.axaml.cs` alone would be a parameter nothing can set. Reported rather than
   half-wired (D259).
3. **`client/docs/upstream-payload-inventory.json` types `goon` as `not-ported`** and this packet
   makes it served. `UpstreamPayloadInventoryTests` checks well-formedness and tree presence only,
   never disposition, so the entry goes stale silently. Outside File Scope; the orchestrator holds it.

## 8. Discrepancies found, and how each was resolved

| Found | Resolution |
|---|---|
| `ui/screens/title.js:4` says "a five-item menu"; `:44-98` renders up to **eight** | Source over spec. The packet's and census's "1 of 5" inherits the stale comment. Recorded, nothing edited |
| Census §7.1 says a practice duel exercises "all nine element kinds"; `boot.js:580` gates `ToyPatterns` on `caps.haptics`, which the same table pins `false` | **Eight**, and true of the shipping WPF host too. D255. The census was closed to this packet |
| The packet cited the catalogue as `:30-53` and `net-post-result` as `:36` | Corrected at source: the block is `:29-54`, `net-post-result` is `:34`, `net-post` is `:47`. `:36` is `cache-put-result` |
| The packet cited `caps.assetCache` at `bridge.js:415-419` | That range is the justifying comment; the field is `:420` |
| The 415 set was planned as two extensions | It is **four FILES**: `Path.GetExtension` returns `""` for the two `vendor/*/LICENSE` files, which the allowlist also misses (`LoopbackServer.cs:447-449`). Pinned by file name |
| `LaunchFaultText` has no Goon headline and is outside File Scope | The headline lives on `PlayPage` as `GoonFaultHeadline`, verbatim from `MainWindow.Lab.cs:207` minus its colon — note the verb is **"open"**, not "start". The body still composes through the shared helper, so its empty-line layout hazard cannot arrive by this route |

## 9. Files changed

- **New:** `client/src/CcpClient.Desktop/Features/Goon/{GoonServingRoots,GoonDoors,GoonProtocol,GoonParticipant,GoonLaunch}.cs`, `GoonHostWindow.axaml(.cs)`
- **Modified:** `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj` (the fifth linked read-only glob), and the three granted wiring touches — `Views/MainWindow.axaml.cs` (one construction site, one page argument, one property), `Views/Pages/PlayPage.axaml(.cs)` (one door, one fault line)
- **Tests:** `client/tests/CcpClient.Tests/GoonPracticeTests.cs` (83), `client/tests/CcpClient.HeadlessTests/GoonPracticeHeadlessTests.cs` (8)
- **Docs:** `client/docs/wpf-surface-reachability.md` (D250-D259 only)
