# Goon Game — census against the shipping WPF source

Evidence scope: the shipping WPF source, re-derived from the committed tree on
2026-08-21. Method: the repeatable inventory rules and source citations stated in this document.

**Verdict: BUILDABLE-IN-PART — and the reason the rest is unbuildable is NOT capability.** One unit
is genuinely buildable today and is named in §7 with its inventory: **Practice mode over the served
payload**, which needs no network, no microphone, no camera and no entitlement. Everything else —
the duel itself, the media send, the voice notes, the invite, the presence — is blocked by decisions
about what leaves the machine, not by anything the port cannot do. That is a different answer from
For You Feed, whose title behaviour had no mechanism on either OS.

**And the row's two counts are BOTH EXACTLY RIGHT, which is why they are dangerous.** 25 and 184 are
the directory totals today, to the file. The row is still misleading three ways, and the first is
the headline:

> **80.0% of the row's 25 files is not the game a user plays.** The duel a player actually plays is
> the JavaScript in the payload. The C# under `Services/GoonGame/` is a **second, parallel
> implementation of the same game**, kept as the parity REFERENCE, and its only entry points are two
> developer CLI flags. Exactly **5 of the 25** sit behind the door a user opens (§1.3).

A wrong number gets corrected; a right number that means something else gets trusted.

---

## 1. The universe, and what the board row's evidence actually says

The universe is the tracked repository tree, walked recursively. Enumeration takes a root directory,
excludes only generated-byte patterns, records declined symlinks, and cross-checks each count against
`git ls-files`. Source paths cannot be dropped by a hand-assembled file list.

Every count below is re-derivable from the source tree and `git ls-files`. Local workflow configuration,
untracked state, and generated output are excluded from product facts.

### 1.1 The row makes two counted claims. Both were tested; neither was inherited

The Goon Game row of `client/docs/task-board.md` (cited without a line: the board is rewritten
every wave, so a line number into it is a citation with an expiry date).

| # | Row claim | Directory today | Verdict |
|---|---|---|---|
| R1 | `Services/GoonGame/` (**25 new files**) | **25** | **number RIGHT, meaning WRONG** — §1.3 |
| R2 | `Resources/web/goon/` (**184 new payload files**) | **184** | **RIGHT, and 97.8% this surface** — §5 |

```
git ls-files ConditioningControlPanel/Services/GoonGame | Measure-Object -Line
  -> 25 files; 12309 source lines
git ls-files ConditioningControlPanel/Resources/web/goon | Measure-Object -Line
  -> 184 files; 12376551 total bytes (CRLF counted as LF, so the figure is the same on both platforms)
```

Both trees agree with `git ls-files`, so there are no untracked bytes in either. **R2 additionally
matches a second independent record** — `client/docs/upstream-payload-inventory.json:26`
(`"fileCountAtBaseline": 184`) — and the board itself classes that field as record data rather than
an assertion, so the agreement is corroboration, not discharge.

**I did not need the merge-delta interpretation.** The trainer-card census found its row's numbers were files added
at one merge rather than directory totals; here both readings would have to be tested only if the
totals disagreed, and they do not. Both numbers are measured from today's bytes and both are pinned.

### 1.2 THE SHAPE OF THIS SURFACE: the game exists TWICE, in two languages

This is not an inference. The shipping source says it, in two files I opened.

`ConditioningControlPanel/Services/GoonGame/GoonHostService.cs:25-27`:

> *"The C# engine under Services/GoonGame stays the reference implementation; the page is a second
> client of the same server (/v2/goon/\*) and of the same deterministic specs (see
> `GoonVectorDumper` for the parity vectors the page's tests consume)."*

`ConditioningControlPanel/Services/GoonGame/GoonVectorDumper.cs:11-13`:

> *"Dumps the C# engine's deterministic output as JSON so the browser client's test suite can prove
> byte-for-byte parity against it. The C# side is the REFERENCE implementation: if the page and this
> file disagree, the page is wrong."*

The mirroring is file-for-file. `GoonMatchService.cs` ↔ `core/match.js`, `GoonScoring.cs` ↔
`core/scoring.js`, `GoonSuddenDeath.cs` ↔ `core/suddenDeath.js`, `GoonRng.cs` ↔ `core/rng.js`,
`GoonDraft.cs` ↔ `core/draft.js`, `MatchClock.cs` ↔ `core/clock.js`, `GoonWire.cs` ↔ `core/wire.js`,
`Rounds/{BubbleRace,QuickDraw,ReactionDuel,StaringContest}Round.cs` ↔ `core/rounds/*.js`, and the
four transports ↔ `net/*.js`. `net/loopbackTransport.js:1` states the direction in its own first
line: *"An in-process transport pair — port of Services/GoonGame/GoonLoopbackTransport.cs."*

**The duplication has already produced one shipped defect, recorded by upstream itself** at
`TransferInboxStore.cs:78-81`: the artifact cap *"raised 24→64 MB on 2026-08-04; this constant was
missed, so the desktop refused every 24..64 MB inbound artifact with `too-big` while a browser peer
accepted them. The two MUST move together."*

### 1.3 THE HEADLINE: 5 of the row's 25 files are behind the door a user opens

The user-facing entry points were traced, not assumed. There are exactly two, plus two developer
flags, and all four were opened:

| Route | Cited | Reaches |
|---|---|---|
| Lab / Play card -> `BtnStartGoon_Click` | `MainWindow/MainWindow.Lab.cs:197`, call at `:201` | `GoonHostService.Launch()` |
| `--goon` | `App.xaml.cs:2435`, call at `:2436` | `GoonHostService.Launch()` |
| `--goon-test` (**developer**) | `App.xaml.cs:2362`, `new GoonTestWindow().Show()` at `:2364` | the dev cockpit -> `GoonGameService` -> the whole C# engine |
| `--goon-vectors` (**developer**) | `App.xaml.cs:2443`, call at `:2447` | `GoonVectorDumper.Run()` |

The reachable set is a **transitive closure over declared type names**, re-derived by the guard on
every run (§10.2): every type declared in each of the 25 files is indexed, trailing `//` comments are
stripped with a string-aware scanner, whole-comment lines are dropped, and the closure is walked from
`GoonHostService.cs`. It settles at **five files** and four edges:

| Edge | Cited |
|---|---|
| `GoonHostService` -> `GoonContracts` | `:310` `var consent = new ConsentSheetMsg();` (declared `GoonContracts.cs:293`) |
| `GoonHostService` -> `TransferInboxStore` | `:368` `TransferInboxStore.Instance.PurgeCommittedSafe("page boot")` |
| `GoonHostService` -> `GoonCacheBridge` | `:383` `GoonCacheBridge.Attach(_host)` |
| `GoonHostService` -> `GoonAvatarCache` | `:962` `GoonAvatarCache.ReadOwnDataUriIfFresh(...)` |

Its mentions of `GoonMatchService` (`:858`), `GoonSignalingClient` (`:63`, `:106`, `:774`) and
`GoonVectorDumper` are **all inside comments**, and none of the four dependencies names an engine
type on a code line.

**The one place the shipped path touches the reference engine is to borrow its consent defaults**,
and the source says the reuse is deliberate: *"the engine's own defaults, never a fork"*
(`GoonHostService.cs:310`). Three of them reach the page's `init` frame at `:343-345` —
`LiveDurationSec` (720 s, `GoonContracts.cs:97`), `ToyCap` (0.7, `:297`) and `PayloadMinGapMs`
(30 000, `:108`).

| Group | Files | Lines | Reached from |
|---|---|---|---|
| **Shipped user path** — `GoonHostService`, `GoonCacheBridge`, `GoonAvatarCache`, `TransferInboxStore`, `GoonContracts` | **5** | **4018** | the Lab card and `--goon` |
| **Reference engine, transports, dev facade, vector dumper** — the other 20 | **20** | **8291** | `--goon-test` and `--goon-vectors` only |
| | 25 | 12309 | |

**5 of 25 = 20.0% of the files, 4018 of 12309 = 32.6% of the lines.** The engine is not dead code —
it is a deliberately maintained **spec oracle** whose output the page's tests assert against — but a
size taken from "25 files" prices porting a second implementation of a game the payload already
implements, plus a developer cockpit, as though it were the work of giving a user a duel.

**This number was wrong in an earlier draft of this document, and how it was wrong is worth keeping.**
The first sweep matched `Goon`-prefixed identifiers and file base names, reported **4**, and missed
`ConsentSheetMsg` — a type whose name carries neither the surface's token nor its file's name. It was
caught only when the closure was implemented as a guard over *declared type names* rather than over
tokens, which is the same lesson the FYP census drew from the two `Chaos/` files that contain no occurrence
of `fyp`. A second, smaller version of the same error was caught in the same pass: a first closure
read `GoonContracts.cs:54`'s **trailing** comment as code and pulled in a sixth file.

### 1.4 The row names two directories and misses three more locations

The anchored repository-wide sweep (§2.1) and a path enumeration both found the surface living
outside the row's boundary.

```
git ls-files | grep -i goon | grep -v Resources/web/goon/ | grep -v Services/GoonGame/
```

| Location | Files | Lines | What it is | Named by the row? |
|---|---|---|---|---|
| `GoonTestPanel.cs`, `GoonTestSimRoundDriver.cs`, `GoonTestWindow.xaml(.cs)` — **at the product root** | 4 | 1737 | The dev cockpit: two player panels in one process, the only compile-time consumer of `GoonGameService` | **NO** |
| `Services/Media/Transfer/` | 6 | 2430 | The own-media compression + transfer cache the send path runs on | **NO** |
| `docs/GOON_{GAME_PLAN,GAME_PROTOCOL,DISCORD_CONTRACT,VOICE_PLAN}.md` | 4 | 1072 | The upstream design contracts, including a **language-neutral wire protocol** | **NO** |
| `Resources/features/goon_game.png` | 1 | — | The Lab card art | **NO** |

`Services/Media/Transfer/` is **the FYP census's `Services/Fyp/Online/` finding running in mirror image**:
not a foreign subsystem hiding inside the surface's directory, but the surface's own subsystem
hiding outside it, invisible to a directory-shaped row. Its only consumers outside itself are the
three Goon files and one hook in the Assets tab (`MainWindow/MainWindow.Assets.cs:1504`), whose own
comment at `:1502` calls it *"The Goon Game transfer cache"*.

**So the surface's real C# footprint is 35 files / 16476 lines, and the row's 25 is 71.4% of it.**
The row understates the code by four dev-cockpit files and a six-file subsystem, while overstating
the shipped game by twenty.

### 1.5 The four design documents are the most portable artefact in the surface

`docs/GOON_GAME_PROTOCOL.md:1-3` describes itself as a *"Language-neutral spec for Goon Game clients
(Windows/WPF today; Expo/React Native and web planned)"* and `:5` says *"If an implementation and
this document disagree, one of them is a bug — fix and mirror."* A port would be the third client
of a protocol that was written to be implemented more than once. **The row does not mention it.**

---

## 2. The sweeps, with their anchored patterns of record

### 2.1 Scope of the repository-wide token sweep

The former repository-wide sweep is intentionally not retained: it included local workflow
state and cannot establish a current product fact. The re-derived product claims in §10 are this
census's current evidence.

The `goon` needle is anchored to avoid incidental substrings. The intake voice-over family under
`Resources/web/intake/assets/vo/` is foreign to this surface and is excluded by §5.1's evidence,
never by name.

### 2.2 Consumer closure over the surface's own type names

```
git grep -n -E "GoonAvatarCache|GoonCacheBridge|GoonDraft|GoonGameService|GoonHostService" -- ConditioningControlPanel
```

Outside `Services/GoonGame/`, in C#: `App.xaml.cs` (2 compile-time, 3 comment),
`MainWindow/MainWindow.Lab.cs` (2 compile-time, 2 comment), `MainWindow/MainWindow.PlayTab.cs`
(comment only), `Views/Tabs/PlayTabView.xaml` (comment only), `GoonTestPanel.cs` /
`GoonTestSimRoundDriver.cs` / `GoonTestWindow.xaml.cs` (compile-time — the dev cockpit, itself part
of this surface), `Models/AppSettings.cs` (the settings; **D209's resolution is moot - see `fyp-census.md` §1.6**),
`Services/Chaos/DtrhAssetManifest.cs:227` (comment), `Services/Media/Transfer/TransferCacheStore.cs:75`
(comment).

**`TransferCacheStore.cs` contains no occurrence of the token `goon` at all** and was found only
because the sweep promoted the surface's own type names to needles — the plan's reveal rule, and the
same mechanism that found the FYP census's two `Chaos/` files.

**A methodological correction, stated rather than applied silently.** Plan §13.1 assigns `SHARED` to
any file with a compile-time consumer outside the surface. Read literally that makes `GoonHostService`
SHARED, because `App.xaml.cs` and `MainWindow.Lab.cs` name it. That is wrong: those are this
surface's **entry points**, not other surfaces consuming it for their own behaviour. `SHARED` is
applied here in the FYP census's sense — a consumer that uses the type to do *its own* work, the way five
surfaces fetched media through `FypOnlineCoordinator`. By that test **no file of this surface is
SHARED**, and the fact is recorded so a reader can re-derive under either reading.

### 2.3 The port side, walked whole

```
git grep -n -i -E "(^|[^A-Za-z])goon" -- client/src
```

**The only hits in `client/src` are twelve lines of `Assets/assets.manifest.json`, and every one is
an intake voice-over filename.** There is no Goon Game code in the port, no entry point, and nothing
that names any of its types.

Walked for the primitives this surface needs
(`webrtc|signaling|microphone|getUserMedia|MediaRecorder|peerconnection|datachannel|WebSocket|rich presence|discord`):
**zero hits of any of them.** The port's only `HttpClient` is
`client/src/CcpClient.Desktop/Features/Intake/IntakeHostWindow.axaml.cs:701`, a GET against its own
`PageOrigin` loopback, whose own log line at `:707` calls it `HARNESS-ONLY`. **The port has no
outbound network boundary at all today.**

---

## 3. The already-shipped check: all nine duel elements are native in the port

Plan §5.2 makes this check mandatory rather than opportunistic, because it is the check the haptic
count that missed a shipped module, and the one that produced the trainer-card census's sharpest sub-finding. Here it lands
differently, and in the port's favour.

`GoonContracts.cs:44-54` freezes the draft pool — the nine things a duel throws:

| Wire code | `GoonElement` | Port module already shipped |
|---|---|---|
| 0 | `Flashes` (`:46`) | `client/src/CcpClient.Desktop/Effects/FlashImagesEffect.cs:55` |
| 1 | `Videos` (`:47`) | `Effects/MandatoryVideoEffect.cs:48` |
| 2 | `Subliminals` (`:48`) | `Effects/SubliminalsEffect.cs:62` |
| 3 | `Bubbles` (`:49`) | `Effects/BubblePopEffect.cs:41` |
| 4 | `LockCards` (`:50`) | `Effects/LockCardEffect.cs:100` |
| 5 | `ToyPatterns` (`:51`) | `Haptics/HapticLimb.cs:71` |
| 6 | `BrainDrain` (`:52`) | `Effects/BrainDrainEffect.cs:67` |
| 7 | `BouncingText` (`:53`) | `Effects/BouncingTextEffect.cs:43` |
| 8 | `Spiral` (`:54`) | `Effects/SpiralOverlayEffect.cs:47` |

**Nine of nine.** Stated precisely so it is not over-read: this does **not** shrink the payload the
port must serve, because a served page renders the elements with its own JS in `exec/` and never
calls into `client/src`. What it establishes is that the *ammunition* of this duel is behaviour the
port already implements natively — which is why §5.1 does NOT put the twenty `exec/` files in the
already-shipped bucket (no port file is a version of them), and why a future decision to drive the
duel from native effects instead of the page is a real option rather than a rewrite.

---

## 4. Behaviour map — every row cites both sides and carries a platform cell

Vocabulary closed: `COVERED`, `PARTIAL`, `GAP`, `OWNER-GATED`. Essentiality is decided against the
noun phrases the owner wrote in the Goon Game row of `client/docs/task-board.md`, not re-derived
per row. An anchor
must **expose the required primitive** at an opened `client/src/**` line.

| # | Owner's phrase | WPF evidence (opened) | Required primitive | Port anchor | Label | Platform |
|---|---|---|---|---|---|---|
| B1 | Real-time 1v1 duels | `docs/GOON_GAME_PROTOCOL.md:141-142` — WebRTC data channel via `/v2/goon/signal`, then peer-to-peer; `Services/GoonGame/GoonWebRtcTransport.cs:12-19` | Two people on different machines see the same match state change within a second of each other | `none` — nothing in `client/src` opens a socket to anything but its own loopback (§2.3) | **OWNER-GATED** (§6.1) — it creates the port's first outbound network boundary | Windows: unproven. Linux: unproven. **Neither cell is dischargeable by engineering** |
| B2 | media payload throwing | `GoonContracts.cs:44-54` (the nine elements); `docs/GOON_GAME_PROTOCOL.md:264-272` (charge costs, risk tiers, 1/30 s rate) | Throw one of nine effect kinds at the other seat, receiver-validated and receiver-resolved | The served page's own `exec/` renderers, over the shipped WebView host precedent (`client/src/CcpClient.Desktop/Features/Dtrh/DtrhCapabilityProbes.cs:22`) | **PARTIAL on the shipped WebView-host precedent** — missing member: the `init`/`manifest` bridge frames (§7). Delivery to a *second seat* is B1 | Windows: unproven — gate: a headed capture of the page in the port's WebView. **Linux: unproven** — gate: run the page in the Avalonia WebView on real X11/Wayland; no WSL distro here (`client/memories/port-status.md:89-93 @ a8d32c219`) |
| B3 | heat build | `docs/GOON_GAME_PROTOCOL.md:268-272` — 1 pt/s x (1 + 0.15 x draft risk sum) x attention multiplier; charge cap 3 | A score and a charge budget that climb while you endure | Same served page; the arithmetic runs in `core/scoring.js` | **PARTIAL on the shipped WebView-host precedent** — same missing member as B2 | Windows: unproven. Linux: unproven — same gates |
| B4 | sudden death | `docs/GOON_GAME_PROTOCOL.md:229-253` — the ladder is a pure function both sides compute; `Services/GoonGame/Rounds/` (5 files, 1098 lines) | Synchronised mini-rounds with a net-score ladder ending at −3 | Same served page (`core/suddenDeath.js`, `ui/sd/`) | **PARTIAL on the shipped WebView-host precedent** — in Practice the ladder runs locally against the scripted peer | Windows: unproven. Linux: unproven |
| B5 | P2P own-media send (photos/videos/GIFs) | `docs/GOON_GAME_PROTOCOL.md:287-296` — second negotiated data channel `goon-media`, **media frames never ride relay or signaling**; `Services/GoonGame/GoonCacheBridge.cs` (892 lines) | Send your own image or video to the other person's screen | `none` | **OWNER-GATED** (§6.2) — user media leaving the machine | Windows: unproven. Linux: unproven |
| B6 | 64 MB video cap | `TransferInboxStore.cs:83` `MaxRecvBytes = 64L * 1024 * 1024`; `Resources/web/goon/net/mediaChannel.js:63` `MAX_ARTIFACT_BYTES = 64 * 1024 * 1024` | Refuse an artifact above the cap before any byte flows | inherits B5 | **OWNER-GATED** — a consequent of B5 (§6.2): a cap has no subject until user media travels. The row also states it imprecisely (§4.2) | Windows: unproven. Linux: unproven |
| B7 | sending is a supporter perk, every seat receives and duels in full | `GoonHostService.cs:896` `App.Patreon?.HasPremiumAccess == true`; **and `:911` `App.Patreon?.HasLabAccess == true`** | Two paid rungs, not one: send = tier 1, **host a room = tier 2** | `client/src/CcpClient.Desktop/Entitlement/EntitlementTierSource.cs:39` — a typed tier lookup exists and is shipped | **OWNER-GATED** (§6.4) — a new thing sold. **The row names one rung of two** (§4.2) | Windows: unproven. Linux: unproven |
| B8 | 10 s voice notes with opt-in consent + push-to-talk (V) | `docs/GOON_VOICE_PLAN.md:61` `VN_MAX_MS = 10_000`, `:62` `VN_MAX_BYTES = 262_144`; **`GoonHostService.cs:489` `e.State = CoreWebView2PermissionState.Allow`** for `CoreWebView2PermissionKind.Microphone` (`:482`) | Record the user's real voice and play it on another person's machine | `none` — the port opens no capture device of any kind | **OWNER-GATED** (§6.3) — a **sensor**, and it sits against `client/docs/capability-inventory.md:69` | Windows: unproven. Linux: unproven — `capability-inventory.md:78` would additionally require a Linux capture proof |
| B9 | share-link invite with no account needed | `docs/GOON_GAME_PROTOCOL.md:67-93` — a `/join` body with no `unified_id` mints a server-side guest id `g_` + 8 random bytes | Let a stranger with no account take the second seat from a URL | `none` | **OWNER-GATED** (§6.1) — a server mints an identity for a person who never installed anything. **Web-client only upstream** (`:88-90`) | Windows: unproven. Linux: unproven |
| B10 | Discord rich presence | `GoonHostService.cs:1422` — `rp-state` dropped entirely unless `GoonRichPresence` is on; `:1428` `App.DiscordRpc?.SetGoonActivity(s)` | Publish to another service that this person is in a duel right now | `none` — no Discord, no IPC, no presence anywhere in `client/src` (§2.3) | **OWNER-GATED** (§6.5) — what is shown to others | Windows: unproven. Linux: unproven |
| B11 | solo practice mode | `Resources/web/goon/ui/soloDriver.js:1-18` — a scripted opponent driving the GUEST half of a **loopback pair**; `ui/screens/title.js:5-6` *"Nothing on this screen touches the network"* | Play a full match alone, against a scripted opponent, with no second machine | `client/src/CcpClient.Desktop/Features/Dtrh/DtrhCapabilityProbes.cs:22` (embedded WebView, shipped) + the payload-link glob at `CcpClient.Desktop.csproj:50-54` (shipped four times) | **PARTIAL on the shipped WebView-host precedent** — missing member: the `init` + `manifest` bridge frames (§7.1) | Windows: unproven — gate: a headed capture of a practice match. Linux: unproven — same gate on X11/Wayland |
| B12 | received partner media is ephemeral and never outlives the match | `TransferInboxStore.cs:62-72` — purged at the startup sweep, at page boot, and at window close; *"Do not 'optimize' persistence back in"* | A store that provably cannot survive the session | `client/src/CcpClient.Desktop/Persistence/PersistenceStore.cs:83` is the port's typed store and is the **opposite** shape — it exists to persist | **GAP: a deliberately non-durable media store with three purge points** — (a) primitive: bytes on disk that are wiped on crash-restart, on re-open and on close; (b) WPF keeps a second index beside the own-artifact one in one root; (c) the port would build it — **and it is only reachable if B5 lands** | Windows: unproven. Linux: unproven — pure filesystem, identical on both |

### 4.1 What the map says overall

- **COVERED: 0 of 12.**
- **PARTIAL: 4 of 12** (B2, B3, B4, B11) — all four on the same anchor and all four blocked on the
  same missing member.
- **GAP: 1 of 12** (B12), and it is unreachable until an owner-gated row lands.
- **OWNER-GATED: 7 of 12** (B1, B5, B6, B7, B8, B9, B10).

**More than half this surface is owner territory, and none of it is capability-refused.** There is no
row here whose primitive the port could not build; there are seven rows whose primitive the port may
not build without an answer.

### 4.2 Two row claims that are imprecise rather than wrong

**"64 MB video cap"** is 64 MiB and is a cap on **any artifact**, not on video: the same constant
gates images (`mediaChannel.js:63`). The row omits the two other caps in the same block —
`MAX_EXEMPT_BYTES = 8 * 1024 * 1024` for un-transcoded originals (`:70`) and
`MAX_SESSION_BYTES = 512 * 1024 * 1024` per match per direction (`:72`) — so a sizer reading the row
would build one limit where the protocol has three.

**"sending is a supporter perk"** is true and incomplete: **hosting a room is a second, higher rung.**
`GoonHostService.cs:911` gates minting a room on `HasLabAccess` (tier 2) while `:896` gates sending on
`HasPremiumAccess` (tier 1), and `MainWindow/MainWindow.PlayTab.cs:107-108` reads both. The row's
one-rung version is **exactly what a stale comment in the shipping source says**:
`MainWindow/MainWindow.Lab.cs:192-193` still reads *"the transfer-your-own-media half is the only
premium part"*. So the omission is inherited, not invented — which is why it is recorded as D246
rather than as a row defect alone.

### 4.3 A citation defect in the shipping source

`Views/Tabs/PlayTabView.xaml:604` cites `MainWindow.Lab.cs:182-186` for the Goon card's
"joining is free" rationale. **Those five lines are the Inspection Bureau's catch block** — a
different feature — and the rationale it meant is at `:191-193`. The second half of the same
citation, `GoonHostService.cs:882-913`, is correct. Recorded as D247 in the D206 shape: an upstream
citation that does not say what it claims, noted so a port author following it does not read the
wrong feature.

---

## 5. The payload: 184 files, counted, attributed, and never forked

### 5.1 Attribution, by plan §13.1's precedence order

```
git ls-files ConditioningControlPanel/Resources/web/goon
  -> Extension inventory: .js=120 .mp3=37 .png=15 .css=5 .json=2 (none)=2 .html=1 .mjs=1 .webmanifest=1
```

| Subtree | Files | Bucket |
|---|---|---|
| `ui/` | 55 | THIS SURFACE |
| `assets/` | 52 | THIS SURFACE (15 PNG, 37 MP3) |
| `exec/` | 20 | THIS SURFACE — see §3 for why NOT "already shipped" |
| `test/` | 19 | THIS SURFACE — the parity/self-test harness (§5.2) |
| `core/` | 15 | THIS SURFACE |
| `net/` | 11 | THIS SURFACE |
| root | 6 | THIS SURFACE |
| `vendor/` | **4** | **VENDORED** — `fflate` (LICENSE + module), `mp4-muxer` (LICENSE + module) |
| `encode/` | 2 | THIS SURFACE |

| Bucket | Files |
|---|---|
| ALREADY SHIPPED IN THE PORT | **0** |
| SHARED | **0** |
| FOREIGN | **0** |
| VENDORED | **4** |
| THIS SURFACE | **180** |
| UNATTRIBUTED | **0** |

**This-surface fraction: 180 / 184 = 97.8%.** The bucket that emptied is the one worth stating: the
shared `Resources/web/vendor/` tree the port already links is **three.js only** (walked: 9 files), so
goon's `fflate` and `mp4-muxer` are not already in the port and are not shared with any other
surface.

### 5.2 The fraction and the serving cost, side by side

Plan §13.2 requires these adjacent, because they answer different questions and a reader who saw
only the first would under-read the second.

| Question | Number |
|---|---|
| How much of the 184 is this surface's own authorship? | **180 (97.8%)** |
| How many files must be served for the page to render? | **164 (89.1%)** |
| What is the difference? | **20 files the browser never loads**: `test/` (19 self-test and parity-vector modules, unreferenced by `index.html`) and `package.json`, whose own first key says *"Browsers never read this file"* |
| Payload bytes | **12 376 551** — CRLF counted as LF so the figure is identical on Windows and Linux; stated as a measurement, **not** as a packaging decision |

### 5.3 How the port would serve it, without forking a byte

Exactly as four trees are already served — a linked read-only glob in
`client/src/CcpClient.Desktop/CcpClient.Desktop.csproj`, the bytes staying owned by the legacy tree:

```xml
<Content Include="..\..\..\ConditioningControlPanel\Resources\web\goon\**\*">
  <Link>payload\goon\%(RecursiveDir)%(Filename)%(Extension)</Link>
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
</Content>
```

Precedent, opened: `dtrh` at `CcpClient.Desktop.csproj:50-54`, plus `intake` (`:59`), `tunnel`
(`:69`) and `vendor` (`:74`). **No proposal to copy bytes into `client/` appears anywhere in this
document.** The payload is the cheapest part of this surface and is not what gates it.

---

## 6. OWNER-FLAGGED — five decisions, deliberately not priced

Per plan §4.2 the test is on the **contract**, not the row: expanding consent, sensors, the network
boundary, persistence or entitlement is owner-gated; writing to a store the port already owns is
sizable. Each section below follows **D225's shape exactly** — it names the endpoint, the exact file
and lines, and every switch — and prices nothing. Describing a boundary is not broadening it under
`docs/constitution.md:37`; refusing to describe it would make the owner's decision undecidable.

### 6.1 The surface contacts a first-party server and then opens a peer connection

**Endpoint.** `https://codebambi-proxy.vercel.app`, a compile-time constant at
`Services/GoonGame/GoonHostService.cs:64`, with the page restricted to the path prefix `/v2/goon/`
at `:69` and enforced at `:792`. **This is the same first-party proxy the leaderboard uses** in
D225, so it is not a new host — but it is a new surface on it.

**Six routes**, specified at `docs/GOON_GAME_PROTOCOL.md:39-66` and implemented client-side in
`Services/GoonGame/GoonSignalingClient.cs` (711 lines): `/v2/goon/invite` (host mints a room, tier-2
gated, 403 `no_host_access`), `/join` (free, no account required), `/leave`, `/signal` (SDP/ICE
mailbox), `/relay` (fallback transport, *"server MUST NOT inspect or persist `data`"*), `/ledger`
(both clients post the signed result; the server stores the pair under both unified ids). Two more
appear in §12: `/v2/goon/report` and `/v2/goon/blocked`.

**What travels**, with the citation split because the two halves sit in different places and this is
the paragraph the owner will read as the description of what leaves the machine. **The headers** are
attached by the host's proxy at `GoonHostService.cs:805-811` — `X-Auth-Token` at `:810`,
`X-Client-Version` at `:811`. **The identity is in the BODY**, and the host writes it only for the
peer-card call (`:1190`, `unified_id = App.UnifiedUserId`); every room-scoped call the C# client
makes builds its own, e.g. `GoonSignalingClient.cs:235`, alongside `display_name` (`:236`) and
`app_version` (`:237`). Display name and app version also cross in `hello`. **Media never rides these routes**
(`docs/GOON_GAME_PROTOCOL.md:293-295`, hard rule).

**Then the machine talks to another machine.** Primary transport is a WebRTC data channel over
public STUN with **no TURN** (`GoonWebRtcTransport.cs:29-31`), falling back to the server relay ring
after a 10 s ICE timeout.

**AND "no TURN" HAS A PRIVACY CONSEQUENCE THE SOURCE DOES NOT STATE, so this census states it.**
`GoonWebRtcTransport.cs:29-30` argues the no-TURN decision on cost — *"running relay infrastructure
for media is exactly what the plan refuses"* — and the token `IP` appears nowhere in
`Services/GoonGame/` or in `docs/GOON_GAME_PROTOCOL.md`. But a relay is the thing that hides a
peer's address, so without one **a successful ICE punch is a direct machine-to-machine connection in
which each side learns the other's public IP address.** That is not a defect and not a divergence:
it is inherent to peer-to-peer without a relay, and it is the price of the decision the source
argues for on other grounds.

**What sharpens it is the composition with this surface's own front door.** `/join` is free, tier-
checked nowhere, and needs no account — the server mints a guest identity for, in B9's words, a
person who never installed anything. So the second seat can be an account-less stranger holding a
pasted link, and on the P2P path that stranger and the user learn each other's addresses. Bounded
honestly in three directions: on relay fallback there is no direct connection and no mutual
disclosure (though media transfer is then silently absent, `docs/GOON_GAME_PROTOCOL.md:293-295`);
the STUN servers themselves are third parties that already observe the user's address; and none of
this is any different from every other P2P application — it is stated because **the owner cannot
weigh a boundary that has not been named**, not because it is unusual.

**What the owner must decide, and no amount of engineering answers:** whether the port takes on an
outbound network boundary at all, when it has none today (§2.3); whether it accepts a **direct**
peer connection to an arbitrary second machine, knowing what that discloses in both directions and
that the second machine may belong to someone with no account; and whether match results are written
to a server-side ledger.

### 6.2 The user's own photos and videos leave the machine

`docs/GOON_GAME_PROTOCOL.md:287-338` specifies a second negotiated data channel `goon-media`
carrying the **sender's own media** so the receiver can display it. Gating is four conditions AND'd
(`:299-303`): a `caps.transfer` version bit, a per-side `consent.media_transfer` declaration on the
consent sheet, the local host's send entitlement, and a live P2P channel. The receiver's offer gate
is ordered and explicit (`:315-321`), the mime allowlist is six types, and **integrity is
receiver-side** — the offered sha256 is a claim the receiver re-hashes itself (`:322-326`).

The desktop half is `Services/GoonGame/GoonCacheBridge.cs` (892 lines) plus the six-file
`Services/Media/Transfer/` subsystem (2430 lines) the row does not name (§1.4).

**What the owner must decide:** whether the port ever transmits a user's own media to another
person, and under what consent.

### 6.3 The microphone is opened, and the host grants it without asking

This is the sharpest of the five, and it is stated with its whole mitigation because overstating it
would be as wrong as missing it.

`Services/GoonGame/GoonHostService.cs:477-495`, opened: on `CoreWebView2.PermissionRequested`, every
kind except `Microphone` is left untouched (`:482`, and the comment at `:484-486` says so
deliberately); `Microphone` is answered `e.State = CoreWebView2PermissionState.Allow` (`:489`) with
`e.Handled = true` (`:493`) to suppress the browser's own prompt. `:496-498` refuses to bank the
answer in the profile. Separately, `ClearBankedMicDenial` (`:456-472`) **proactively writes**
`SetPermissionStateAsync(Microphone, "https://ccp.game", Allow)` into the WebView2 profile, to undo
a denial a previous build may have banked.

**The mitigation is real and is quoted rather than paraphrased** (`:405-410`): the gate is in the
page and is double-locked — voice notes are OFF by default, the toggle refuses to move until an
acknowledgment modal has been read, audio flows only when **both** duelists have opted in, and the
mic is opened per recording and released on button-up (*"no hot mic"*,
`docs/GOON_VOICE_PLAN.md:78-79`). Limits are pinned both ends: `VN_MAX_MS = 10_000` (`:61`),
`VN_MAX_BYTES = 262_144` (`:62`). **The server never sees audio** (`:29`); voice rides the control
lane, not `goon-media` (`:33-35`).

**The tension the owner must resolve, with its container quoted so the scope question is visible.**
`client/docs/capability-inventory.md:66` opens the section **`## Webcam, face, and gaze tracking`**,
and `:69` — a bullet whose subject is *"Frames, crops, tensors, landmarks, gaze samples, and all
per-frame biometric derivatives"* — ends: **"Audio capture is never opened."** Whether that sentence
is a product-wide prohibition on the microphone or a property of the vision pipeline is **genuinely
open on the text**. This census does not resolve it and may not: `:70` puts expanding sensors and
consent under *"a consent-contract revision and owner review"*, and `docs/constitution.md:37` forbids
broadening capture and consent boundaries. **The resolution is the owner's.**

### 6.4 Two paid rungs, and the port has a tier source but no tiers

`GoonHostService.cs:896` (`HasPremiumAccess`, tier 1, sending) and `:911` (`HasLabAccess`, tier 2,
hosting). The server computes the same two verdicts (`docs/GOON_GAME_PROTOCOL.md:44-49`), and the
desktop's local answer exists only so the title screen can dim Host before the round-trip. **Joining
is free and ungated, deliberately** — `Views/Tabs/PlayTabView.xaml:547-549` refuses to draw a
padlock over an open door, and `MainWindow/MainWindow.PlayTab.cs:102-106` dims the two rung labels
rather than disabling anything.

The port has `client/src/CcpClient.Desktop/Entitlement/EntitlementTierSource.cs:39`
(`TierLookup(Status, Tier, Detail)`), so the *shape* exists. Selling two new rungs is an
entitlement-boundary expansion and is the owner's.

### 6.5 Presence tells another service the user is in a duel

`GoonHostService.cs:1422` drops the `rp-state` frame outright unless `GoonRichPresence` is on;
`:1428` calls `App.DiscordRpc?.SetGoonActivity(s)` with a validated enum
(`lobby|live|recap|off`, `:1414-1419`), and `:1636` retracts on teardown. The wider Discord
contract — avatar sharing, DM permission, peer cards over `/v2/goon/peercard` (`:921`) — is
`docs/GOON_DISCORD_CONTRACT.md` (125 lines). **Three of the eleven toggles D225 enumerates are this
surface's** (`GoonShareAvatar`, `GoonShareDiscordDm`, `GoonRichPresence`), so this section and D225
are the same owner question seen from two rows.

### 6.6 The four privacy questions, answered for the whole surface

| # | Question | Answer | Citation |
|---|---|---|---|
| Q1 | Changes what is **persisted**? | **YES, in both directions.** Own compressed artifacts persist in `transfer-cache/`; **received partner media is deliberately NON-durable** and wiped at three points | `Services/Media/Transfer/TransferCacheStore.cs:72-79`; `TransferInboxStore.cs:62-72` |
| Q2 | Changes what is **shown to others**? | **YES** — a live opponent sees score, attention percentage, emotes, optionally the user's own media and real voice; Discord presence publishes the duel | `docs/GOON_GAME_PROTOCOL.md:174` (the `tick` frame); `GoonHostService.cs:1428` |
| Q3 | Changes what **leaves the machine**? | **YES** — POSTs to `codebambi-proxy.vercel.app/v2/goon/*` with `unified_id` and headers, then a direct peer connection | `GoonHostService.cs:64`, `:69`, `:805-811` |
| Q4 | What **sensor**, under whose consent? | **THE MICROPHONE**, under a double-locked in-page opt-in, with the host granting the WebView2 permission unprompted. **The camera is NOT opened** — §6.7 | `GoonHostService.cs:482`, `:489`, `:493`; `docs/GOON_VOICE_PLAN.md:20-27` |

### 6.7 The camera is a reserved protocol capability with NO producer, and the row is right to omit it

Worth stating precisely, because the protocol talks about the camera enough that a reader would
expect a webcam row.

`GoonContracts.cs:40` declares `GoonAttentionMode { Cam = 0, NoCam = 1 }`, the scoring rule has a
cam branch (`docs/GOON_GAME_PROTOCOL.md:269-270`), and `StaringContest` is chosen only when both
sides are in cam mode (`GoonSuddenDeath.cs:452`). **But nothing in the shipping surface produces
it.** `GoonMatchService.cs:169` defaults `LocalAttentionMode` to `NoCam`; the only writer anywhere in
the product that selects `Cam` is the dev cockpit's combo box (`GoonTestPanel.cs:118`, applied at
`:564`); and the in-app page is told `camera = false` outright at `GoonHostService.cs:336`, whose
comment reads *"no webcam bridge into the page in v1"*. `GoonWebRtcTransport.cs:21-23` states the
invariant: no RTP tracks are ever added, so *"there is no path — deliberate or accidental — for mic
or camera to reach the peer."*

**Parity means matching the shipped default, not the available feature** — the rule D212 already
established for the flash luminance layer. **The board row is correct not to mention the webcam**,
and this census records the reserved capability so that a future author who finds `Cam = 0` does not
read it as a shipped sensor.

---

### 6.8 CONTRACT-SILENT, NOT OWNER-GATED — what roots a Goon host would expose

**This is not a sixth owner-gated decision and it is not folded into any size. It is a contract
question with no clause on either side of it**, surfaced here because the owner is the only person
who can close it and because it was found by checking a claim this census had already made (§7.1.1).

The port's only clause about what a web core may serve is
`client/docs/capability-inventory.md:260`, and it is **DTRH-scoped**: it enumerates roots for *that*
host — bundled game files, user media, bundled Chaos art, saved Loom GIFs, the active mod's DTRH
subfolder — under `## Down the Rabbit Hole` (`:226`) → `### Page boot and content` (`:254`). The
other two user-media clauses in that section (`:282`, `:293`) are scoped the same way. **Neither
owner document carries a product-wide web-core or payload-host contract**, so a second web core's
roots are governed by nothing.

**The honest position is that the contract is SILENT, not permissive**, and the difference matters
in exactly one direction: silence is not a grant. §7.1.1's `sizable` verdict does not depend on
reading it as one — it rests on §4.2's triggers not firing — but a port that stood up a Goon host
would be the first web core the port has whose exposed roots no clause describes.

**What the owner may wish to decide, and nothing here presumes an answer:** whether the roots clause
at `:260` is DTRH-specific by intent or is meant to bind every web core; and if the latter, whether
it is restated product-wide before a second core exists rather than after. **Priced at nothing**,
like every other item in §6.


## 7. Verdict: BUILDABLE-IN-PART — with the inventory and the residue

Applying plan §4.3 in order: clause 1 fails (seven OWNER-GATED rows). **Clause 2 fires** — the subset
{B11, B2, B3, B4} is entirely PARTIAL and is independently user-observable — so the verdict is
BUILDABLE-IN-PART and clause 3 is not reached.

### 7.1 The buildable unit, named with its inventory

> **Practice mode over the served payload.** Serve `Resources/web/goon/` from the port's existing
> loopback server in the port's existing WebView host, answer the page's `init` and `manifest`
> frames, and a user plays a complete duel against the scripted opponent: all nine element kinds,
> heat and charges, sudden-death rounds, recap. **No network, no microphone, no camera, no
> entitlement, no Discord.**

| Item | Size |
|---|---|
| Payload to serve | **184 files, 164 of them loaded by the page**, 12 376 551 bytes — **linked read-only, zero bytes forked** (§5.3) |
| Port code already present | The embedded WebView (`Features/Dtrh/DtrhCapabilityProbes.cs:22`), the loopback serving contract (`Features/Intake/IntakeHostWindow.axaml.cs:701-707` names it), and the payload glob shipped four times (`CcpClient.Desktop.csproj:50-54`) |
| Port code to add | A host window plus the bridge subset: **`init` + `manifest`** (host->page) and **`ready` + `log` + `heartbeat`/`pong` + `exit`/`exit-done`** (page->host). The frame catalogue is at `GoonHostService.cs:30-53` and the `init` shape is written out **field-for-field twice** — `GoonHostService.cs:300-350` and `goon/bridge.js:371-440` — so it is transcribable, not reverse-engineered. **`manifest` is decomposed in §7.1.1 — it is the smallest item here, not the largest** |
| `caps` for this unit | `haptics:false`, `camera:false`, `assetCache:false`, `mediaTransfer:false`, `canHost:false`; `solo` defaults on (`goon/bridge.js:391`) |
| Upstream code to port | **none of the 25 as code.** Practice runs entirely in the page on the loopback pair (`ui/soloDriver.js:1-18`, `net/loopbackTransport.js:19-23`). What must be transcribed is **three consent defaults** the `init` frame carries — `LiveDurationSec` 720 (`GoonContracts.cs:97`), `ToyCap` 0.7 (`:297`), `PayloadMinGapMs` 30000 (`:108`) — because the host reads them off `ConsentSheetMsg` rather than inventing them (§1.3) |
| OS interop required | Only what DTRH already ships: an embedded WebView. **Linux unproven, and that is the one real gate** |
| Owner decisions required | **none** — nothing leaves the machine, no sensor opens, nothing is sold |

#### 7.1.1 What answering `manifest` actually costs — decomposed, because it is a media inventory

`manifest { images, videos, skipped, truncated }` is a **port-side inventory of the user's own
media**, and user media is precisely what §6.2 is owner-gated on, so compressing it to one word
would hide the item a plan gate most needs to size. Decomposed, it is **the cheapest thing in this
unit**, because the port already ships both halves:

| Piece | Upstream | Port | Verdict |
|---|---|---|---|
| The enumerator | `GoonHostService.cs:362` calls `DtrhAssetManifest.Build()` — the comment at `:359-361` says it *"Reuses the DtRH manifest builder verbatim (asset-tree deselection, size caps, sampling) — one enumerator for every web core"* | `client/src/CcpClient.Desktop/Features/Dtrh/DtrhUserMedia.cs` is **the port of that same file**, carrying its extensions, caps, walk depth, skipped-count and downsample, plus the single active-pool definition | **ALREADY SHIPPED** |
| The frame | `GoonHostService.cs:372-378` posts `{type, images, videos, skipped, truncated, received}` | `client/src/CcpClient.Desktop/Features/Dtrh/DtrhProtocol.cs:271` builds `{type, images, videos, skipped, truncated}` — **field-for-field identical** | **ALREADY SHIPPED, minus one field** |
| The one field that differs | `received` (`:377`), the accepted-artifact list | absent | **NOT NEEDED for this unit**: the inbox is purged at page boot (`TransferInboxStore.cs:62-72`, and `GoonHostService.cs:368` does it immediately before listing precisely so the rows *"are always empty"*), and a practice-only build has no media channel to fill it. It is a frame-shape stub |

**So `manifest` is a field rename and a `received: []`, over an enumerator and a frame the port
already ships and already tests.**

**And the reason it is `sizable` needs stating precisely, because an earlier draft of this paragraph
got it wrong in the one place it would cost most.** That draft said the port already reads the
user's media directory for DTRH *"under a contract already in force"*, and concluded `sizable`
partly from that grant. **The grant does not reach this surface.** The only owner clause governing
what a web core may expose is `client/docs/capability-inventory.md:260` — *"The host exposes only
the required roots: bundled game files, user media, bundled Chaos art, saved Loom GIFs, and the
active mod's DTRH subfolder"* — and it sits under `## Down the Rabbit Hole` (`:226`) →
`### Page boot and content` (`:254`). All three user-media clauses there (`:260`, `:282`, `:293`)
are **DTRH-scoped**, and neither owner document carries a product-wide web-core or payload-host
contract. **No clause covers a Goon host's roots, because no Goon host exists.**

**The verdict is unchanged and the reason is narrower: `sizable` rests on none of §4.2's five
triggers firing, not on an existing grant covering this use.** No sensor opens, no new class of
data is stored, no destination is added, nothing is shared with another person, nothing is sold —
the bytes are enumerated on the machine and handed to a page running on the same machine. That is
the test §4.2 actually specifies, and it is the whole of what carries this row.

**This is `census.md:24` — "a right number that means something else gets trusted" — happening to
its own author, in the sentence that flips a user-media capability from owner-gated to sizable.**
Every mechanical leg of the inversion verifies; it was the contract leg that asserted a grant it did
not have. Recorded rather than quietly corrected, because the failure mode is this document's
subject.

What `manifest` must NOT become is a route by which that inventory leaves the machine; that is §6.2,
and this unit has no channel to leave by. **The roots a Goon host would expose are a separate,
contract-silent question — §6.8.**

**THE THINNEST JOINT IN THIS DERIVATION, named rather than asserted.** Clause 2's *"independently
user-observable"* is the one predicate that is a judgement, and this unit sits on its edge in a
specific way: **Practice is one of five title-menu items, and the other four lead into §6.** Shipping
it means shipping a title screen where Host, Join, voice notes and media setup all refuse.

Three things keep it on the right side of the line, and a reader may weigh them differently.
(1) Upstream already ships exactly this configuration as a first-class state — `ui/screens/title.js:9-11`
says Practice *"is always present and becomes the PRIMARY action when the page is standalone"*, and
`goon/bridge.js:11-14` gives the standalone path a synthesized `init` precisely so a person with no server
has a working game. (2) A practice duel is a complete play experience, not a stub: nine element
kinds, the full sudden-death ladder, a recap. (3) The alternative reading — that nothing short of a
live 1v1 is user-observable — makes clause 2 unreachable for every unit of this surface, which is the
failure mode plan §0.3 was written to avoid.

**If the owner reads clause 2 more strictly, the verdict is REFUSED with exactly the same
inventory** — §7.1 stays the first thing to build either way, and nothing else in this census moves.
That is the honest bound on the one soft predicate here.

### 7.2 Named residue, so nothing is lost

| Item | Disposition |
|---|---|
| Real-time 1v1 duel (B1) | **OWNER-GATED** (§6.1). The port's first outbound network boundary. Never priced. |
| Own-media send + the 64 MiB cap (B5/B6) | **OWNER-GATED** (§6.2). User media to another person. |
| Voice notes (B8) | **OWNER-GATED** (§6.3). A microphone, against an open question in `capability-inventory.md:69`. |
| Two paid rungs (B7) | **OWNER-GATED** (§6.4). The port has a tier source; selling is the owner's. |
| Share-link invite / anonymous guest (B9) | **OWNER-GATED** (§6.1). Upstream implements it in the **web client only**. |
| Discord rich presence (B10) | **OWNER-GATED** (§6.5). Same owner question as D225, seen from this row. |
| Ephemeral partner inbox (B12) | **GAP**, and unreachable until B5 lands. A deliberately non-durable store with three purge points, which is the opposite shape to the port's `PersistenceStore`. |
| The C# engine (20 of the 25 files) | **NOT OWED.** It is the parity reference for a page the port would serve, not the game. If the port ever wants an independent oracle, `--goon-vectors` already emits one. |
| The dev cockpit (4 files at the product root) | **NOT OWED.** A developer harness behind `--goon-test`. |
| `Services/Media/Transfer/` (6 files, 2430 lines) | Owed **only with B5**, and named here because the row does not name it at all. |
| The four `docs/GOON_*.md` contracts | **The most portable artefact in the surface** (§1.5) and the thing a second implementation should be written against. |

### 7.3 What the next packet should NOT be

**Not "port `Services/GoonGame/`".** §1.3: 80.0% of it is a second implementation of a game the payload
already contains, plus a developer cockpit; the shipped user path is five files, and Practice needs
none of them.

---

## 8. The board row's own framing, checked

| Row phrase | Verdict |
|---|---|
| `Services/GoonGame/` (25 new files) | **Exact, and 20.0% of it is the shipped game** (§1.3) |
| `Resources/web/goon/` (184 new payload files) | **Exact; 97.8% this surface, 164 served** (§5) |
| "real-time 1v1 duels: media payload throwing, heat build, sudden death" | Confirmed |
| "P2P own-media send … 64 MB video cap" | Confirmed; the cap is 64 **MiB**, covers images too, and two further caps go unmentioned (§4.2) |
| "sending is a supporter perk while every seat receives and duels in full" | **Incomplete: hosting is a second, tier-2 rung** (§4.2), and the row's version matches a stale comment at `MainWindow.Lab.cs:192-193` |
| "10 s voice notes with opt-in consent + push-to-talk (V)" | Confirmed, and the host grants the WebView2 microphone permission unprompted (§6.3) |
| "share-link invite with no account needed" | Confirmed, and it is **web-client only** upstream (`docs/GOON_GAME_PROTOCOL.md:88-90`) |
| "Discord rich presence" | Confirmed |
| "solo practice mode" | Confirmed — **and it is the buildable unit** (§7.1) |
| "received partner media is ephemeral and never outlives the match" | Confirmed, with three purge points (§6.6 Q1) |
| "Size XL — decompose before scheduling" | **The instruction was right; the size is not a single number.** The decomposition is §7.1 (buildable now, no owner input) against §7.2's residue. **That residue is seven OWNER-GATED rows resolving to FIVE owner decisions**, and the mapping is stated rather than left for the reader to reconcile: B1+B9 → §6.1 (one network boundary), B5+B6 → §6.2 (a cap has no subject until user media travels), B8 → §6.3, B7 → §6.4, B10 → §6.5. Not one XL row |

---

## 9. What this census does NOT prove

- **Nothing was built, run, or rendered.** No Goon Game code exists in `client/`; `client/src/**` was
  closed to this packet and no product code was written.
- **No headed evidence of any kind.** No window was shown, no frame composited, no pixel compared.
  Every claim above is a claim about *source*. B11's PARTIAL means the WebView host exists and ships,
  **not** that the goon page renders in it — nobody has loaded it.
- **The WPF app was never executed.** No duel was played, no `--goon-test` cockpit opened, no
  `--goon-vectors` run, no server contacted. The protocol document and the source agree with each
  other; neither was checked against a live server, which is private
  (`GoonSignalingClient.cs:16-17`: *"Phase F builds these endpoints in the private repo"*).
- **Linux is unproven for every row without exception.** `wsl.exe --list --verbose` reports no
  installed distributions on this machine (`client/memories/port-status.md:89-93 @ a8d32c219`), so every Linux
  cell is a named gate. Windows is unproven for every row too.
- **The reachability claim in §1.3 is a LEXICAL compile-time trace, not a runtime one.** It is a
  transitive closure over type names declared in the 25 files, computed on stripped code lines. **A
  dependency reached by reflection, by a source generator, by dependency injection, or through a
  XAML-bound path would not appear**, and neither would one expressed only through a type declared
  *outside* these 25 files. Three limitations of the lexical method were hit during this census and
  all three are in the guard's own comments: a `\bGoonSuddenDeath\b` needle cannot match
  `GoonSuddenDeathRunner`; a token sweep over `Goon`-prefixed names cannot see `ConsentSheetMsg`
  (this one changed the headline from 4 to 5); and a trailing `//` comment on a code line reads as
  code unless the stripper is string-aware. **The STRIPPER's bias is conservative** — a block-comment
  continuation line that does not start with `*` is treated as code, so a comment cannot hide an edge
  from it. **That does not make the closure safe overall, and the earlier draft of this bullet
  overclaimed by saying so**: the four invisibility modes listed above are all UNDER-report modes and
  no lexical method can see them. **The count is a lower bound on reachability, not an upper one.**
- **`GoonHostService` does reach outside this surface**, and the trace above only covers
  `Goon`-prefixed names: it consumes `App.DiscordRpc`, `App.Patreon`, `App.Settings`, `UpdateService`
  and `Services/Media/Transfer/`. Those are named where they matter (§1.4, §6) but were not
  enumerated exhaustively.
- **The 20-file serving difference in §5.2 rests on `index.html` not referencing `test/`** and on
  `package.json`'s own statement that browsers never read it. A dynamically constructed import that
  a static read cannot see would change that number.
- **The archive-era repository-wide sweep is intentionally omitted.** It included local workflow
  state and changed when the census itself changed, so it could not establish a stable product fact.
  The re-derived tables in §10 are the current evidence.
- **THE NUMBER SWEEP CANNOT SEE NUMBERS SPELLED AS WORDS.** "four", "six", "seven", "twelve" are
  invisible to it, and that is how two stale tallies survived a green suite at code review. The
  blind spot is stated rather than papered over, and the reason it is not simply closed is worth
  reading: extending the sweep to word forms would give the *appearance* of coverage without the
  substance, because the pin is vocabulary-level (below) and every small integer is already pinned
  by something. **What actually closes that class is §10.4.3**, which re-derives the label tally from
  the map's own rows and compares it against every restatement in this document *including the
  verdict's spelled-out one*. Bind a number to its claim, or accept that you have not bound it.
- **THE NUMBER SWEEP IS BLIND TO CITATIONS BY CONSTRUCTION.** `File.ext:NNN` is one of its excluded
  classes — it has to be, or every citation would demand a pin — so a **citation** corrected in one
  place and left stale in another is invisible to it. Three such stale citations were found in this
  document by hand after two review rounds. `EveryBodyCitationOfAPortAnchor_UsesTheLineThePinTable`
  `Claims` now covers the port-anchor half; the WPF half rests on pinning each citation that carries
  a finding, and on a reader.
- **The number-pin is VOCABULARY-level, not claim-level.** §10.7's fact requires every numeric token
  to appear in a §10 table or in this section; it cannot tell *which* claim a number belongs to, so
  a number pinned for one purpose would satisfy the fact if it were later reused wrongly for
  another. Catching that needs a semantic reading of prose, which is deliberately not attempted. The
  residual is a human review obligation, not a gap to close with a cleverer matcher.
- **Every other number in this document is pinned.** Every count, fraction, line total and constant
  is in §10's tables and re-derives from the shipping bytes on every run. A guard fact enforces
  exactly that (§10.7), so the property is machine-checked rather than promised.

---

## 10. Pinned enumeration (parsed by `GoonGameCensusTests`, re-derived from the shipping bytes)

This section is the DATA; `client/tests/CcpClient.Tests/GoonGameCensusTests.cs` is the LOGIC. The
guard walks `ConditioningControlPanel/` **by directory, recursively** on every run, re-derives every
count and reads **the exact line** of every citation below. The directory roots and the needles live
in the test, not here, so editing this document can never shrink the search.

**§10.4 is this packet's repair of the trainer-card census's pin defect.** That guard pinned *paths* and matched an
expression *anywhere in the file*, so its suite was green while three of its own citations were wrong
by 3 and 5 lines. Here a citation that drifts by **one** line reds the suite.

### 10.1 Directory counts, walked recursively

| Key | Value |
|---|---|
| Services/GoonGame | 25 |
| Services/GoonGame/Rounds | 5 |
| Services/Media/Transfer | 6 |
| Resources/web/goon | 184 |
| Resources/web/goon/ui | 55 |
| Resources/web/goon/assets | 52 |
| Resources/web/goon/exec | 20 |
| Resources/web/goon/test | 19 |
| Resources/web/goon/core | 15 |
| Resources/web/goon/net | 11 |
| Resources/web/goon/vendor | 4 |
| Resources/web/goon/encode | 2 |
| Resources/web/vendor | 9 |

### 10.2 The shipped-user-path split (§1.3), re-derived from the bytes

`reached` is `host` for a file the shipped user path names on a code line, `reference` for the rest.

| Id | Path (relative to `ConditioningControlPanel/Services/GoonGame/`) | Reached |
|---|---|---|
| G1 | GoonHostService.cs | host |
| G2 | GoonCacheBridge.cs | host |
| G3 | GoonAvatarCache.cs | host |
| G4 | TransferInboxStore.cs | host |
| G5 | GoonContracts.cs | host |
| G6 | GoonDraft.cs | reference |
| G7 | GoonGameService.cs | reference |
| G8 | GoonLoopbackTransport.cs | reference |
| G9 | GoonMatchService.cs | reference |
| G10 | GoonMatchTypes.cs | reference |
| G11 | GoonRelayTransport.cs | reference |
| G12 | GoonRng.cs | reference |
| G13 | GoonScoring.cs | reference |
| G14 | GoonSignalingClient.cs | reference |
| G15 | GoonSuddenDeath.cs | reference |
| G16 | GoonTransportBase.cs | reference |
| G17 | GoonVectorDumper.cs | reference |
| G18 | GoonWebRtcTransport.cs | reference |
| G19 | GoonWire.cs | reference |
| G20 | MatchClock.cs | reference |
| G21 | Rounds/BubbleRaceRound.cs | reference |
| G22 | Rounds/GoonRoundModel.cs | reference |
| G23 | Rounds/QuickDrawRound.cs | reference |
| G24 | Rounds/ReactionDuelRound.cs | reference |
| G25 | Rounds/StaringContestRound.cs | reference |

### 10.3 The surface's locations OUTSIDE the row's two directories (§1.4)

| Id | Path (relative to `ConditioningControlPanel/`) |
|---|---|
| X1 | GoonTestPanel.cs |
| X2 | GoonTestSimRoundDriver.cs |
| X3 | GoonTestWindow.xaml |
| X4 | GoonTestWindow.xaml.cs |
| X5 | docs/GOON_GAME_PLAN.md |
| X6 | docs/GOON_GAME_PROTOCOL.md |
| X7 | docs/GOON_DISCORD_CONTRACT.md |
| X8 | docs/GOON_VOICE_PLAN.md |
| X9 | Resources/features/goon_game.png |

### 10.4 Citations pinned BY LINE — the needle must be ON that line

| Key | Path (relative to `ConditioningControlPanel/`) | Line | Needle |
|---|---|---|---|
| proxy-base | Services/GoonGame/GoonHostService.cs | 64 | https://codebambi-proxy.vercel.app |
| allowed-path-prefix | Services/GoonGame/GoonHostService.cs | 69 | /v2/goon/ |
| camera-disabled | Services/GoonGame/GoonHostService.cs | 336 | camera = false |
| mic-kind-guard | Services/GoonGame/GoonHostService.cs | 482 | CoreWebView2PermissionKind.Microphone |
| mic-allow | Services/GoonGame/GoonHostService.cs | 489 | CoreWebView2PermissionState.Allow |
| mic-handled | Services/GoonGame/GoonHostService.cs | 493 | e.Handled = true |
| send-tier | Services/GoonGame/GoonHostService.cs | 896 | HasPremiumAccess |
| host-tier | Services/GoonGame/GoonHostService.cs | 911 | HasLabAccess |
| rp-gate | Services/GoonGame/GoonHostService.cs | 1422 | GoonRichPresence |
| attention-mode-enum | Services/GoonGame/GoonContracts.cs | 40 | Cam = 0, NoCam = 1 |
| element-enum | Services/GoonGame/GoonContracts.cs | 44 | enum GoonElement |
| element-first | Services/GoonGame/GoonContracts.cs | 46 | Flashes = 0 |
| element-last | Services/GoonGame/GoonContracts.cs | 54 | Spiral = 8 |
| nocam-default | Services/GoonGame/GoonMatchService.cs | 169 | GoonAttentionMode.NoCam |
| cam-writer-is-the-cockpit | GoonTestPanel.cs | 118 | GoonAttentionMode.Cam |
| recv-cap | Services/GoonGame/TransferInboxStore.cs | 83 | 64L \* 1024 \* 1024 |
| artifact-cap | Resources/web/goon/net/mediaChannel.js | 63 | MAX_ARTIFACT_BYTES = 64 \* 1024 \* 1024 |
| exempt-cap | Resources/web/goon/net/mediaChannel.js | 70 | MAX_EXEMPT_BYTES = 8 \* 1024 \* 1024 |
| session-cap | Resources/web/goon/net/mediaChannel.js | 72 | MAX_SESSION_BYTES = 512 \* 1024 \* 1024 |
| voice-ms | docs/GOON_VOICE_PLAN.md | 61 | VN_MAX_MS = 10_000 |
| voice-bytes | docs/GOON_VOICE_PLAN.md | 62 | VN_MAX_BYTES = 262_144 |
| goon-build | Resources/web/goon/bridge.js | 43 | r17-20260806 |
| lab-entry | MainWindow/MainWindow.Lab.cs | 201 | GoonHostService.Launch() |
| cli-goon | App.xaml.cs | 2517 | GoonHostService.Launch() |
| cli-goon-test | App.xaml.cs | 2445 | new GoonTestWindow() |
| cli-goon-vectors | App.xaml.cs | 2528 | GoonVectorDumper.Run() |
| playtab-send-rung | MainWindow/MainWindow.PlayTab.cs | 123 | HasPremiumAccess |
| playtab-host-rung | MainWindow/MainWindow.PlayTab.cs | 124 | HasLabAccess |
| stale-single-perk-comment | MainWindow/MainWindow.Lab.cs | 193 | only premium part |
| wrong-citation-site | Views/Tabs/PlayTabView.xaml | 604 | MainWindow.Lab.cs:182-186 |
| wrong-citation-target | MainWindow/MainWindow.Lab.cs | 182 | BtnStartBureau_Click failed |
| assets-hook | MainWindow/MainWindow.Assets.cs | 1493 | TransferCompressionService.Instance.OnPresetChanged |
| consent-msg-decl | Services/GoonGame/GoonContracts.cs | 293 | class ConsentSheetMsg |
| consent-msg-use | Services/GoonGame/GoonHostService.cs | 310 | new ConsentSheetMsg() |
| live-duration-default | Services/GoonGame/GoonContracts.cs | 97 | LiveDurationSecDefault = 720 |
| toy-cap-default | Services/GoonGame/GoonContracts.cs | 297 | ToyCap { get; set; } = 0.7 |
| payload-gap-default | Services/GoonGame/GoonContracts.cs | 108 | PayloadMinGapMs = 30000 |
| two-implementations | Services/GoonGame/GoonHostService.cs | 25 | The C# engine under Services/GoonGame stays |
| assets-hook-comment | MainWindow/MainWindow.Assets.cs | 1491 | The Goon Game transfer cache |
| artifact-cap-history | Services/GoonGame/TransferInboxStore.cs | 80 | 24→64 MB |
| auth-token-header | Services/GoonGame/GoonHostService.cs | 810 | X-Auth-Token |
| client-version-header | Services/GoonGame/GoonHostService.cs | 811 | X-Client-Version |
| unified-id-in-body | Services/GoonGame/GoonSignalingClient.cs | 235 | unified_id |
| ice-timeout | Services/GoonGame/GoonContracts.cs | 104 | IceTimeoutMs = 10000 |
| data-channel-only | Services/GoonGame/GoonWebRtcTransport.cs | 21 | DATA CHANNEL ONLY |
| no-mic-or-camera-to-peer | Services/GoonGame/GoonWebRtcTransport.cs | 22 | for mic or camera to reach the peer |
| no-turn-by-design | Services/GoonGame/GoonWebRtcTransport.cs | 29 | No TURN by design |
| manifest-builder-reuse | Services/GoonGame/GoonHostService.cs | 362 | DtrhAssetManifest.Build() |
| manifest-frame-received | Services/GoonGame/GoonHostService.cs | 377 | received |
| score-formula | docs/GOON_GAME_PROTOCOL.md | 267 | 0.15 |
| payload-rate | docs/GOON_GAME_PROTOCOL.md | 272 | Payload rate: 1 / 30 s |
| host-gate-refusal | docs/GOON_GAME_PROTOCOL.md | 47 | 403 |

### 10.4.1 Derived line and group counts stated in prose

Re-derived by walking each path and counting lines on every run.

| Key | Value |
|---|---|
| goongame-reference-lines | 8291 |
| goongame-reference-share-percent | 80.0 |
| rounds-lines | 1098 |
| goon-cache-bridge-lines | 892 |
| goon-signaling-client-lines | 711 |
| dev-cockpit-files | 4 |
| dev-cockpit-lines | 1737 |
| media-transfer-lines | 2430 |
| design-doc-files | 4 |
| design-doc-lines | 1072 |
| discord-contract-lines | 125 |
| surface-csharp-total-lines | 16476 |
| payload-served-share-percent | 89.1 |
| shared-vendor-tree-files | 9 |
| census-cites-goonhostservice-lines | 18 |
| intake-vo-goon-named-files | 7 |
| ice-timeout-seconds | 10 |
| voice-note-max-seconds | 10 |

### 10.4.3 The behaviour map's label tally, re-derived from its own rows

Added at code review. Three numbers in the first draft of this census were correct in one section and
stale in another, and **the verdict paragraph cited an OWNER-GATED tally its own map contradicted**.
These re-derive by counting labels in §4's rows, and the guard compares them against every
restatement in the document, including the verdict's spelled-out one.

| Key | Value |
|---|---|
| behaviour-rows | 12 |
| label-covered | 0 |
| label-partial | 4 |
| label-gap | 1 |
| label-owner-gated | 7 |

### 10.4.4 The frozen element wire codes, re-derived from the enum

`docs/GOON_GAME_PROTOCOL.md:256-257` calls these frozen and append-only, so a renumbering upstream is
a wire break and must red the suite rather than drift.

| Key | Value |
|---|---|
| element-code-Flashes | 0 |
| element-code-Videos | 1 |
| element-code-Subliminals | 2 |
| element-code-Bubbles | 3 |
| element-code-LockCards | 4 |
| element-code-ToyPatterns | 5 |
| element-code-BrainDrain | 6 |
| element-code-BouncingText | 7 |
| element-code-Spiral | 8 |

### 10.4.2 Payload composition by extension

| Key | Value |
|---|---|
| payload-js | 120 |
| payload-mp3 | 37 |
| payload-png | 15 |
| payload-css | 5 |
| payload-json | 2 |
| payload-no-extension | 2 |
| payload-html | 1 |
| payload-mjs | 1 |
| payload-webmanifest | 1 |

### 10.5 Port anchors pinned BY LINE (`client/src/**`)

| Key | Path (relative to `client/src/CcpClient.Desktop/`) | Line | Needle |
|---|---|---|---|
| flashes | Effects/FlashImagesEffect.cs | 55 | class FlashImagesEffect |
| videos | Effects/MandatoryVideoEffect.cs | 48 | class MandatoryVideoEffect |
| subliminals | Effects/SubliminalsEffect.cs | 62 | class SubliminalsEffect |
| bubbles | Effects/BubblePopEffect.cs | 41 | class BubblePopEffect |
| lockcards | Effects/LockCardEffect.cs | 100 | class LockCardEffect |
| toypatterns | Haptics/HapticLimb.cs | 71 | class HapticLimb |
| braindrain | Effects/BrainDrainEffect.cs | 67 | class BrainDrainEffect |
| bouncingtext | Effects/BouncingTextEffect.cs | 43 | class BouncingTextEffect |
| spiral | Effects/SpiralOverlayEffect.cs | 47 | class SpiralOverlayEffect |
| webview-anchor | Features/Dtrh/DtrhCapabilityProbes.cs | 22 | EmbeddedCapability |
| entitlement-anchor | Entitlement/EntitlementTierSource.cs | 39 | record TierLookup |
| manifest-frame-anchor | Features/Dtrh/DtrhProtocol.cs | 271 | type = "manifest" |

### 10.6 The fractions that carry the findings — pinned EXACTLY, no threshold

Every term is re-derived from the shipping bytes on every run.

| Key | Value |
|---|---|
| goongame-total-files | 25 |
| goongame-host-path-files | 5 |
| goongame-reference-files | 20 |
| goongame-host-path-share-percent | 20.0 |
| goongame-total-lines | 12309 |
| goongame-host-path-lines | 4018 |
| goongame-host-path-line-share-percent | 32.6 |
| surface-csharp-total-files | 35 |
| row-share-of-surface-percent | 71.4 |
| payload-total-files | 184 |
| payload-this-surface-files | 180 |
| payload-vendored-files | 4 |
| payload-this-surface-share-percent | 97.8 |
| payload-served-files | 164 |
| payload-harness-files | 20 |
| payload-total-bytes | 12376551 |
| duel-elements | 9 |
| duel-elements-with-port-module | 9 |

### 10.7 What the guard enforces beyond the tables

- **Every numeric token in this document is pinned or disclaimed.** One fact extracts all of them
  and requires each to appear in §10's tables or in §9. Exclusions are **enumerated CLASSES**, never
  a list of literals: `File.ext:NNN` and `:NNN` citation forms, `§N.N` section references, `DNNN`
  divergence ids, row ids, ISO dates, `vN.N` versions, hash-algorithm names,
  hex shas, and headings.
- **WHERE THE SWEEP CAN LEAK, enumerated — and the list is AS COMPLETE AS FIVE ROUNDS OF
  ADVERSARIAL TESTING HAVE MADE IT, which is not the same as complete.** Five structurally distinct
  holes have been found in this one guard: positional, asymmetric, lexical, overbroad, and
  self-referential. Every one was found by a mechanism rather than by reading, and every one was a
  disagreement between the sweep's two halves rather than a bad exclusion class. **No numeral in
  this table is written as a digit**, because the table sits inside §10 and the fifth hole was this
  table feeding its own examples back into the vocabulary it documents.

  | Axis | Divergence that actually happened | State now |
  |---|---|---|
  | **Section boundaries** | The vocabulary side treated everything after `## 10.` as pinned, and §10 is the last section, so appended prose self-whitelisted | ONE `Sections()` walk feeds both sides, so they cannot disagree about where a section ends |
  | **Class filtering** | The vocabulary side harvested digits RAW while the checking side stripped classes, so row ids and citation line numbers injected seven, twelve, sixteen and twenty-four — **this is how a stale sixteen-percent survived** | Both sides apply `ExcludedNumberClasses` |
  | **What is admissible** | **THE FIFTH HOLE, and this row is where it lived.** "Table row inside §10" did not distinguish a PIN table from a NARRATIVE one, so this axis table — the table documenting the guard's defects — re-injected the integers it was describing, and was the only source of one of them | Vocabulary admits §9 lines and table rows in **pin subsections §10.1-§10.6 only**. §10.7 is narrative and is CHECKED like body prose |
  | **The `Line` column** | Not a divergence but the SAME SHAPE, and inherent to vocabulary-level pinning: §10.4/§10.5 pin a citation by its line number, and that number then whitelists the same integer anywhere in the document — exactly how row ids leaked before them | **Not fixed, and not fixable at this design.** Disclaimed by name in §9; the claim-level substitute is §10.4.3 |
  | **Token regex** | — | One literal regex, read by both |
  | **Normalisation** | — | One `Normalize()`, called by both |

  An edit that changes one side must change the other. Four `TheNumberSweep_*` fixtures pin the
  repaired behaviours, each watched red at the head that ships it.

  **Inside a pin subsection the vocabulary is still soft, and a DIFFERENT mechanism bounds it — in
  every subsection, now, and the "now" is the point.** An earlier draft of this bullet asserted
  defence in depth *"arriving by design rather than by luck"* on the strength of **one probe of one
  subsection** (§10.1). A reviewer ran the same probe on three and found §10.6 — the largest pin
  table, holding the headline fractions — **wide open**. Running it on all ten found **two more**,
  §10.4 and §10.5. **A claim about what a mechanism covers needs the coverage enumerated, not
  sampled**, and generalising from a sample is the failure this document spends its length warning
  about, committed in the sentence describing that failure.

  So: every pin subsection was probed by injecting a two-cell narrative row carrying an invented
  number into it, one at a time, and here is what actually catches it. **The last three rows were leaks, closed in this
  revision.**

  | Subsection | Caught by |
  |---|---|
  | §10.1 | `EveryDirectoryCount_IsRederivedFromTheShippingBytes_NotReadOutOfTheCensus` |
  | §10.2 | `TheSurfaceFileSet_IsExactlyWhatTheWalkFinds` |
  | §10.3 | `TheLocationsOutsideTheRowsTwoDirectories_AllExistInTheShippingTree` |
  | §10.4.1 | `EveryDerivedLineCountStatedInProse_RederivesFromTheBytes` |
  | §10.4.2 | `ThePayloadExtensionHistogram_RederivesFromTheBytes` |
  | §10.4.3 | `TheLabelTally_MatchesTheBehaviourMap_EverywhereItIsRestated` |
  | §10.4.4 | `EveryDuelElement_HasAShippedPortModule` |
  | §10.4 | `EveryPinnedCitation_IsOnTheExactLineItClaims` — **was a leak**: a row that did not parse as a citation was silently skipped, so a narrative row fed the vocabulary and nothing complained. An unparseable row in a citation table is now reported, never skipped |
  | §10.5 | `EveryPortAnchor_IsOnTheExactLineItClaims` — **was a leak**, same cause, same fix |
  | §10.6 | `TheFractionsThatCarryTheFindings_RederiveExactly_WithNoThreshold` — **was a leak**, and the cause should sting: this fact hand-rolled its own mismatch loop over derived keys only, while the shared `AssertAgrees` helper it should have called already carried the reverse check *"census pins it, nothing re-derives it"*. **The property existed in the codebase and was lost by re-implementing instead of reusing.** It now calls the helper |

  **Ten of ten, zero leaking**, re-verified by re-running the probe over every subsection after the
  fixes. This is defence in depth by design *now*; it was not before, and the difference was only
  visible to an enumeration. A future editor should know the two mechanisms hold each other up
  before "simplifying" either one.
- **The label tally re-derives from the map's own rows** (§10.4.3) and is compared against every
  restatement in this document, **including the verdict's spelled-out one** — the check that catches
  a corrected tally which did not propagate to the section a reader stops at.
- **A port anchor cited in body prose must use the line §10.5 pins for it.** This closes the one
  stale-restatement class no other mechanism here can see: **a citation is a class the number sweep
  deliberately EXCLUDES**, so a citation corrected in the pin table and left stale in the prose is
  invisible to it. Scoped to §10.5 because a port anchor is single-purpose; the equivalent rule over
  §10.4 would be noise, because one WPF file is legitimately cited at many different lines — this
  document cites `GoonHostService.cs` at **18** distinct lines in `File.cs:NNN` form, plus further
  bare `:NNN` continuations, and every one means something different:

  ```
  grep -o "GoonHostService\.cs:[0-9]*" client/docs/goon-game-census.md | sort -u | wc -l   ->  18
  ```

  **That asymmetry is a real residual: a stale WPF citation in body prose is still only caught by
  pinning it**, which is why the citations carrying findings are pinned individually.
  **An earlier draft of this bullet said "forty", which was unpinned, imprecise and wrong** — in the
  one document whose whole subject is that failure. It is now derived, pinned in §10.4.1, and
  carries its invocation, which §1 requires of every count here.
- **The two `walk.mjs` copies are byte-identical**, and the sha256 quoted in §1 is re-computed.
- Every behaviour row in §4 carries one of the four labels and a platform cell; §6.6's four privacy
  answers are present; every `## 6.x` owner-flagged section exists.
- Repo root anchors on `client/CcpClient.sln`. A missing census, a missing
  `ConditioningControlPanel/` or an unfindable root is a hard **FAILURE**, never a skip; every
  filesystem predicate lives in a helper rather than in a fact body.
