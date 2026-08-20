# SP-125 — plan (committed BEFORE any mapping)

Review level 3, step 1. This file exists so the method is fixed before the answer is known.
Nothing below is a finding; every count in it is a placeholder to be filled by a command, not by me.

**Revision 2, after plan review returned REVISE.** Three blockers, all accepted, all fixed below:
the pin was document-only (§6), U3 was a hand-assembled file list that dropped files carrying my own
seed token (§1), and the label rule made cross-platform claims with no platform cell (§3). Five
non-blocking corrections also taken (§2, §3, §4, §5). **I verified every citation the review rested
on before editing** — see §9.

## 0. Why this plan is shaped this way

The port has been wrong about a count four times in a row (haptic sites 8 -> 13 -> 14 -> 18) and
**every correction came from widening the universe, never from reading harder.** Each of those four
searches ran over a file LIST assembled by hand. Thirteen missed the DEFAULT video engine because one
file was not on the list; fourteen missed a module this port had already shipped.

So the first commitment in this plan is: **no hand-assembled file list appears anywhere in this
packet.** Every number is produced by a command that walks a DIRECTORY recursively, and the command
is printed next to the number it produced.

**Revision 2 note: my own U3 broke that rule on the first pass.** I wrote a list of root files and
then claimed the union was "the whole repository". It was not, and it dropped three files that carry
my own seed token — including the one line that decides how this payload could be served at all. That
is the same shape as the haptic count losing the default video engine, committed in the very plan
whose purpose was to prevent it. Fixed in §1.

The second commitment is that the board row's own evidence is a claim to be tested, not an input.
The row (`client/docs/task-board.md:97`) says `Services/Fyp/` (3 new) + `Resources/web/fyp/` changes.
That row came from a sync ledger. SP-120 found four citations inside its own packet that did not say
what the packet claimed; SP-113 found `AppSettings.cs` citations wrong by ~530 lines and in the wrong
path. **If the row is wrong, that is the headline, not a footnote.**

**The third commitment, added in revision 2: the coordinator's count is also a claim.** Review reports
9 `.cs` files plus an `Online/` subdirectory where the row says 3, and names them. I will not inherit
that number either. It is reconciled against my own recursive walk, and if my walk disagrees with the
review I report both. What the review's finding does change is my **priority**: if `Online/`,
`RemoteMediaCache`, `ScrolllerSource` and `IFeedSource` are real, this surface **fetches remote media
from an external third-party service over the network**, which is a different kind of object from the
local feature the row describes and lands on the owner's standing external-connections decision. That
possibility is now explicitly in scope for the privacy test (§5), which revision 2 widens from
ghost mode to the **whole surface**.

## 1. The universe, stated as directories

**CORRECTED IN REVISION 2.** The rule is now uniform: **the universe is the repository root, walked
recursively**, minus the exclusions in X2 and minus X1's role-restriction. There is no enumerated
list of roots to be incomplete.

| # | Root | Recursive? | In/out | Why |
|---|---|---|---|---|
| **U0** | **`.` — the repository root** | **yes** | **IN** | **The universe. Everything not explicitly excluded below is searched, including dotted directories (`.github/`), spaced directories (`img state/`), `spine-tasks/`, `stream-overlay/`, `pack-zips/`, `redist/`, and every root-level file regardless of extension — `.bat`, `.iss`, `.gitignore`, `.md`, `.txt`.** |
| U1 | `ConditioningControlPanel/` minus `CCP.*` | yes | IN (role: **behaviour**) | The shipping WPF product, v6.8.x. The only authority on what For You Feed does. |
| U2 | `Tests/` | yes | IN (role: **behaviour**) | The WPF test project. A behaviour asserted there is behaviour, and it names members the product tree may only construct reflectively. |
| U3 | ~~named root files~~ **the rest of the root walk** | yes | IN (role: **drivers**) | **Was a hand list; now covered by U0.** Non-C# drivers decide real things. Proven, not hypothesised — see the three hits below. |
| U4 | `client/` | yes | IN (role: **capability**) | The port. Supplies the capability side of every mapping and the already-shipped-it check that the count of 14 missed. |
| U5 | `docs/` | yes | IN (role: **rules**) | Constitution + governance; consulted for rules, not for behaviour. |
| X1 | `ConditioningControlPanel/CCP.*` | yes | **IN the sweep, OUT of the capability side** | First cross-platform attempt. Searched, because a mention there is a lesson. **But a `CCP.*` artefact may NEVER satisfy COVERED or PARTIAL, not even as corroboration** — `docs/constitution.md:32` forbids importing its "classes, interfaces, timers, DI topology, **or status claims**", and citing it as evidence that the port can do something is precisely a status claim. Structurally enforced too: the port-anchor cell accepts `client/src/**` paths only. |
| X2 | `**/bin/`, `**/obj/`, `.git/`, `__pycache__/`, `*.log`, `*.binlog`, `*.nettrace*`, `*.etlx`, `*.speedscope.json` | n/a | **OUT** | Build output and traces. Not source. Every exclusion is a pattern over generated bytes; **no source file is excluded by name.** |

### Proof that the revision-2 correction was necessary, not cosmetic

Three files outside my first-pass U3 carry the seed token `fyp`, and I opened all three:

- **`build-installer.bat:80`** — *"backs the FYP feed, Exclusives, DtRH, the Goon Game and the default
  video engine,"* (the sentence begins at `:79`, *"The WebView2 Evergreen bootstrapper (~1.6 MB) is
  COMMITTED in redist\ - WebView2"*). **WebView2 backs the FYP feed, and the installer must never ship
  without it.**
- **`.gitignore:226`** — *"# WebView2 is now load-bearing for FYP, Exclusives, DtRH, Goon Game and the
  default"* / `:227` *"# video engine, so an installer build must never silently skip it"*.
- **`MODDING.md:107`** — the Vault & dashboard art row lists `features/fyp.png` and
  `features/fyp_banner.png`, with the geometry note *"the strips are wider - `fyp_banner.png`
  1376x459"*.

The first two are the same fact from two independent files: **the shipping FYP feed is a WebView2
surface with no fallback.** That is the single most load-bearing input to "how would the port serve
this", and my first-pass universe could not see it. The third is the art contract that pairs with
whatever `FypAssetManifest` turns out to be.

## 2. Method

**M1 — Directory-first counting.** Every count in the census is produced by a recursive walk, and the
exact command is printed beside the number. Two independent counts where a tree could hold untracked
bytes: `find <dir> -type f | wc -l` and `git ls-files <dir> | wc -l`. Disagreements are reported and
explained, never silently reconciled.

**M2 — Token sweep to a fixed point, over U0.** Case-insensitive, recursive, over the repository root.

*Seeds.* `fyp`, `for you`, `foryou`, `for-you`, `ghost`, `feed`, `reel`, plus the seeds the board row
itself hands over and my first pass failed to list: `mosaic`, `scroll`, `Scrolller`, `tile`, `clip`,
`gaze`, `opacity`, `click-through`, `clickthrough`, `topmost`, `Show Desktop`, `WebView2`.

*Anchoring (revision 2).* `feed` and `ghost` are matched with **word boundaries** (`\bfeed\w*`,
`\bghost\w*`); unanchored `feed` floods on `feedback` and unanchored `ghost` on unrelated prose. Where
an anchored needle still over-matches I report the noise count rather than quietly narrowing it.

*The reveal rule (revision 2), so the fixed point is defined and not elastic.* A hit **reveals** a new
token if and only if the hit is **(a)** inside a file already established as part of the surface, or
**(b)** a line that names one of the surface's own members (type, method, setting key, command-line
flag, resource key, file name). A hit that merely co-occurs with a needle reveals nothing. Without
this rule the sweep either terminates by fiat or expands to the whole repository; with it, iteration
to a fixed point is mechanical.

**M3 — Consumer closure before any equivalence claim.** "It is only these N files" is inadmissible
until every consumer is enumerated by grep. For each type and public member in the surface, grep U0
for its bare name and record every hit site. A surface whose consumers were sampled rather than
enumerated is reported as unbounded, not as small.

**M4 — Every cited line is opened.** No `File.cs:line` appears in the census unless I ran `sed -n` on
that exact path and the census quotes the text I saw. Citations are taken against this worktree's
HEAD and the SHA is recorded in the census, so a later drift is attributable.

**M5 — Payload counted, never forked.** `Resources/web/fyp/` counted by directory walk, by extension,
by byte total. The rule is fixed and not mine to relax: `client/Directory.Build.props` links upstream
web payloads read-only out of the legacy tree by csproj glob and copies them to `payload/`; **the
bytes stay owned by the legacy tree and are never forked into `client/`** (root `CLAUDE.md`). I read
the existing glob and the served-tree precedent (`dtrh`, `intake`) and state how FYP would be served
by that same mechanism. **No proposal to copy bytes will be written.**

## 3. Capability decision rule — fixed now, so the verdict is not a judgement call

The port has seven landed capabilities: **overlay, input, audio, video, pointer, glyph, haptics.**

Every behaviour B gets a row of **five** cells. A row is invalid unless all five are filled.

1. **WPF evidence** — `File.cs:line`, opened per M4, with the quoted text.
2. **Required primitive** — mechanism-free statement of what must be possible, phrased as an
   observable: "a window that renders above others and passes clicks through to what is below".
3. **Port anchor** — `client/src/**/File.cs:line`, opened per M4, or the literal word `none`.
   **`CCP.*` paths are not accepted in this cell** (§1 X1).
4. **Label** — exactly one of:

| Label | Assigned iff |
|---|---|
| **COVERED by \<capability\>** | A type already in `client/src/**` exposes the required primitive, cited at a line I opened, **and** reaching it needs no new platform interop and no new OS API. |
| **PARTIAL on \<capability\>** | Such a type exists and is cited, but the WPF behaviour needs a mode, parameter or member that type does not have. **The missing member is named.** A PARTIAL that cannot name the missing member is demoted to GAP. |
| **GAP: \<named primitive\>** | No such type exists. Names (a) the primitive, (b) the OS API or library WPF uses to get it, (c) what the port would have to build, in one sentence. |

5. **Platform (NEW IN REVISION 2)** — `Windows: proven|unproven` / `Linux: proven|unproven`, and
   **when either is `unproven`, the exact manual gate is named in the cell.**

**Why cell 5 exists.** `COVERED` is a claim about a **Windows + Linux** product, and revision 1 made
that claim with no platform cell at all — the words Linux, Windows and WSL appeared nowhere in it.
`docs/constitution.md:36` is explicit: *"**Windows AND Linux** (X11/Wayland distinguished where
behavior differs) or a documented product blocker. Compilation, a stub, a no-op fallback, or a
Windows-only test never proves cross-platform support."* And `client/memories/port-status.md:89-96`
records the measurement: `wsl.exe --list --verbose` reports *"Windows Subsystem for Linux has no
installed distributions"*, so **on this machine every Linux claim is a named gate, never a discharge.**
Default for this packet is therefore `Linux: unproven` unless I hold evidence otherwise, and a census
row may not launder a Win32-only mechanism into a cross-platform COVERED.

This is not hypothetical for this surface: review reports `FypGhostOverlay.cs:285`/`:379` as
`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, i.e. Win32 extended window
styles. **I have not opened those lines yet** and will verify them at mapping time per M4; if they say
what review says, that row's platform cell cannot read `Linux: proven` on any evidence available here.

"Probably", "should be easy", "similar to" are not labels. An unlabelled behaviour is a defect in this
census, not an acceptable residue. **A GAP is a finding, not a blocker** — it is named and the census
continues.

## 4. Verdict rule — fixed now

Applied mechanically to the labelled rows.

**"Essential" is anchored, not re-derived per behaviour (revision 2).** The owner already wrote the
identity of this surface as noun phrases at `client/docs/task-board.md:97`, and that fixed list is the
test:

> endless conditioning-clip feed; **ghost mode = see-through AND click-through**; webcam **gaze
> scrolling**; opacity control; any monitor; survives Show Desktop; undecodable clips show a notice
> and swap out.

A behaviour is **essential** iff it realises one of those phrases. The test is applied against that
list and the mapping is written down per phrase, so the word "essential" cannot drift mid-census.

- **REFUSED** iff at least one essential behaviour is labelled GAP with no landed substitute.
- **BUILDABLE-IN-PART** iff the surface decomposes into a subset whose rows are all COVERED or
  PARTIAL, that subset is independently user-observable (a user could open it and use it), and the
  GAP set is named as the residue.
- **BUILDABLE** iff every row is COVERED or PARTIAL.

**Owner-authority carve-out (NEW IN REVISION 2).** A behaviour whose required primitive is governed by
an owner decision in `client/docs/capability-inventory.md` is written into its **own owner-flagged
section**, exactly like the ghost-mode finding, and is **never folded into the size.** It gets no
label that implies the port may proceed, because that decision is not mine and not the census's.

This is live for the webcam gaze-scrolling phrase. `capability-inventory.md:70`: *"Expanding sensors,
derived data, persistence, networking, diagnostics, or telemetry requires a consent-contract revision
and owner review."* `:71`: *"Camera starts only after current explicit consent and an explicit user
start/accepted feature prompt."* `:78`: *"Windows and Linux must provide real enumeration, capture,
calibration, inference, semantic events, and teardown. ... **A stub that says running is a failure.**"*
Webcam is **not among the port's seven landed capabilities**, so this is a gap under owner authority,
and the census will say so in its own section rather than pricing it.

The verdict is whatever the rows say. **I am not permitted to prefer an outcome**, and the size that
follows is an inventory of named units, never a t-shirt or a day count.

## 5. The privacy test — applied to the WHOLE SURFACE, not just ghost mode

Ghost mode is answered **from source only**: the sweep locates it, I open the defining lines, the
census states what it does behaviourally. The board title names it and nobody has said what it is.

**Revision 2 widens the test from ghost mode to the entire surface**, because `Services/Fyp/Online/`
is reportedly the network half and a privacy question scoped to one feature would have walked past it.
Four questions, each answered yes/no **with a citation**, for the surface as a whole:

1. Does it change what is **persisted** to disk (history, telemetry, logs, screenshots, saved state)?
2. Does it change what is **shown to anyone other than the local user** (capture, streaming, screen
   share, an overlay a second person sees, a window that appears in a recording)?
3. Does it change what **leaves the machine** (network, analytics, upload, a third-party service)?
4. **(NEW) What sensor or consent state does it turn on, and under whose consent?** Which sensor,
   which consent record, who granted it, when it stops, and **whether the user can see it running.**

Question 4 exists because review reports `FypHostService.cs:941-978` starting a camera behind an
invisible window, with `:915`/`:1023` stopping only a camera FYP itself started. **I have not opened
those lines and will verify them per M4** — but a camera the user cannot see running is exactly the
finding that a three-question test aimed at ghost mode would have missed, and exactly the finding that
must not be folded into a size.

**Any yes -> its own section, flagged for the owner, NOT folded into a size estimate.** A
privacy-relevant behaviour discovered during a sizing exercise is the kind of thing that disappears
into a number, and it will not disappear into mine.

## 6. Pinning — RE-DERIVED FROM THE SHIPPING BYTES, not a document agreeing with itself

**BLOCKER 1, accepted in full.** Revision 1 had `FypCensusTests.cs` read `client/docs/fyp-census.md`
and assert counts and names **from that document**. Nothing in that reaches the shipping bytes, so it
is satisfiable by a document agreeing with itself and a fifth upstream drift stays green. That is the
exact vacuity this repo already closed once, and the precedent guard says so in its own words at
`client/tests/CcpClient.Tests/HapticSiteCensusTests.cs:11-15`, which I opened:

> *"A guard that only checked the document against itself would have passed on all four numbers. So
> the document is the DATA and this file is the LOGIC ... the candidate line set is RE-DERIVED from
> the shipping bytes on every run. Editing the census document can never shrink the search."*

`client/tests/CcpClient.Tests/FypCensusTests.cs` (new, pure logic, no Avalonia runtime) is therefore
built to that precedent:

- **The directory roots live in the TEST, as DIRECTORIES, not a file list.** Same construction as
  `HapticSiteCensusTests.cs:46-51`, whose own comment reads *"The family: three directories, searched
  whole and recursively. NOT a file list — a file list is exactly how `VideoService.Browser.cs` and
  `BouncingTextService.cs` were missed twice."* Editing the census document can never shrink my search
  either.
- **The two COUNTS re-derive from `ConditioningControlPanel/**` on every run** — the product-file
  enumeration and the payload file count — and are compared against the census. Upstream gaining a
  file reds the suite; the document cannot talk itself out of it.
- **Repo root anchors on `client/CcpClient.sln`** (present in every checkout and in a worktree).
  **Root-not-found and missing-census are hard FAILURES that never skip.**
- **A half-present reference tree FAILS.** "Unreachable reference tree" means exactly one thing: no
  `ConditioningControlPanel/` directory at all. Directory present but the FYP tree absent is a corrupt
  checkout and fails.
- **The branch taken is written to test output**, so a permanently-unreachable guard is visible in the
  TRX instead of passing silently forever.
- The three **document-shape** assertions stay document-only, which is legitimate because their
  subject *is* the document: every behaviour row carries one of the three labels and none is blank;
  every row carries a platform cell; the four privacy answers are present.

Precedent for doc-enforcing tests generally: `UpstreamPayloadInventoryTests`, `AiOperationContractTests`,
`VersionDerivationTests`. The count that drifts becomes a red test, not a memory.

## 7. Constraints I am operating under

- `client/src/**` is CLOSED. **This packet writes no product code.** If the census proves something
  must be built, that is the finding and the next packet is authored from it.
- `client/docs/wpf-surface-reachability.md` — **divergences only**, numbered from **D207** onward
  (review confirms D207 is the correct next number).
- `client/tests/floor/floor.json` is **never opened.** My count change is declared in
  `spine-tasks/SP-125-fyp-census/floor-delta.json`. Pin 2309 unit / 144 headless; my observed floor
  will be pin + declared delta, and I state both numbers.
- No wall-clock waits. These are pure assertions over documents and directory walks; none is needed.
- Both gates alone: `node client/tests/floor/check-warnings.mjs`, then
  `node client/tests/floor/check-floor.mjs`.

## 8. Order of work after this checkpoint

1. Verify the board row's evidence by M1 — and reconcile against the review's reported 9 `.cs` +
   `Online/`, inheriting neither number.
2. Token sweep to a fixed point by M2 over U0; report the universe delta against the row.
3. Consumer closure by M3 — is anything outside `Services/Fyp/` driving this surface?
4. Open and quote every citation by M4, including the Win32 style flags and the camera-start lines
   review reported.
5. Answer ghost mode; run the four-question privacy test over the whole surface.
6. Count the payload; state the serving mechanism by M5; resolve the WebView2-with-no-fallback fact
   against what the port actually has.
7. Label every behaviour by the §3 five-cell rule; apply §4, including the owner-authority carve-out.
8. Write `client/docs/fyp-census.md`; pin it with a re-deriving `FypCensusTests.cs` per §6; record
   divergences from D207; declare the floor delta; run both gates.

## 9. Citations I verified before accepting this review

Per M4, I opened every line the review rested on rather than taking the verdict:

| Cited | Verified? | What it actually says |
|---|---|---|
| `client/tests/CcpClient.Tests/HapticSiteCensusTests.cs:11-15` | **yes** | Document-against-itself would have passed all four numbers; set re-derived from shipping bytes every run. |
| `HapticSiteCensusTests.cs:46-51` | **yes** | `FamilyDirectories` = three directory strings; comment names the two files a file list missed twice. |
| `HapticSiteCensusTests.cs:17-25` | yes (read beyond the citation) | Anchor on `client/CcpClient.sln`; root-not-found and missing census are hard failures; half-present tree fails; branch written to output. |
| `docs/constitution.md:32` | **yes** | Read-only zones; `CCP.*` never imported — incl. "**or status claims**", which is why X1 bars it as corroboration. |
| `docs/constitution.md:36` | **yes** | "Windows AND Linux ... a Windows-only test never proves cross-platform support." |
| `client/memories/port-status.md:89-96` | **yes** | WSL2 present, **no installed distributions**; every Linux claim is a named limit on this machine. |
| `client/docs/capability-inventory.md:70`, `:71`, `:78` | **yes** | Consent-contract revision + owner review; camera only on explicit consent + explicit start; Windows **and** Linux real capture, "a stub that says running is a failure." |
| `build-installer.bat:80` | **yes** | "backs the FYP feed, Exclusives, DtRH, the Goon Game and the default video engine". |
| `.gitignore:226` | **yes** | "WebView2 is now load-bearing for FYP, Exclusives, DtRH, Goon Game and the default video engine". |
| `MODDING.md:107` | **yes** | `features/fyp.png`, `features/fyp_banner.png`; "`fyp_banner.png` 1376x459". |
| `FypGhostOverlay.cs:285`/`:379`, `FypHostService.cs:903-1045`/`:915`/`:941-978`/`:1023` | **NOT YET** | Surface content, deliberately not opened before APPROVE. Carried in this plan as **review-supplied claims to verify at mapping time**, never as established fact. |
