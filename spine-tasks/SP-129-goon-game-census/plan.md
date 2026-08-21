# SP-129 — plan (committed BEFORE any mapping)

Review level 3, step 1. This file exists so the method is fixed before the answer is known.
**Nothing below is a finding.** Every number that will appear in the census is written here as a
placeholder to be produced by a command, never by me. The only numbers in this file are line numbers
in citations I opened (§11) and the two counts the board row asserts, which are carried as **claims
under test**.

Branch `lane/SP-129-goon-game-census`, worktree base `71ab1bac2` (`feat/crossplatform` tip).
Floor pin **2399 unit / 144 headless**. Divergence ids **D240 onward**.

---

## 0. Why this plan is shaped this way

### 0.1 The central trap is SP-127's, and it is not "count more carefully"

This port has been wrong about an upstream count five times by *arithmetic* (haptic sites
8 → 13 → 14 → 18, For You Feed 3 → 9), and SP-127 found the sixth and sharper class:
**a count that was arithmetically EXACT and meant the wrong thing.** Its row's `Views/Controls/`
"(113 new files)" was exactly 113, and **4 of those 113 were the feature** — 96.5% of the number
belonged to the AI companion surface, the settings pages, and three rack panels this port had
already shipped. The row also **missed the directory the feature actually lived in**, because the
feature was twelve partial-class members of one window.

> *"A wrong number gets corrected; a right number that means something else gets trusted."*

**So the ordering of this census is fixed here and is not negotiable: for every count I inherit or
derive, I establish WHAT IT COUNTS before I establish whether it counts correctly.** §5 makes that
mechanical. A directory is not a feature, and confirming a directory total is not confirming a
surface.

### 0.2 The row's evidence is a claim under test. I inherit neither number

`client/docs/task-board.md:107` carries this row's evidence.

| # | The row's claim (`task-board.md:107`) | Status entering this packet |
|---|---|---|
| R1 | `Services/GoonGame/` — **25 new files** | UNVERIFIED, and its this-surface fraction is UNKNOWN |
| R2 | `Resources/web/goon/` — **184 new payload files** | UNVERIFIED, and its this-surface fraction is UNKNOWN |

R2 has a **second independent record** at `client/docs/upstream-payload-inventory.json:24-28`
(`"fileCountAtBaseline": 184`), which the board itself classes as *"record data, not an assertion"*,
so two agreeing records still do not discharge R2 — they agree about a **total**, and §0.1 is about
what a total counts. The same file at `:28` names `Services/GoonGame/GoonHostService.cs` as the
host; that is a third claim under test, not a starting point.

**Three shapes of error are in scope by name, because all three have already happened here:**

- **A directory that is not the feature** (SP-125). `Services/Fyp/Online/` was 42% of its named
  directory and was an app-wide subsystem with eleven consumers outside itself. If any part of
  `Services/GoonGame/` is a shared transport, media pipeline or presence subsystem with consumers
  elsewhere, the row's 25 is not this surface's 25.
- **A feature that lives somewhere the row does not name** (SP-127). The row names two directories.
  The universe is the repository, so a third location is findable, and `MainWindow/` partials are
  the specific shape that hid the Trainer Card.
- **A payload total that is mostly not the payload of this surface.** 184 files is large enough to
  contain a vendored third-party library, a shared asset set, or another surface's bytes. The
  payload gets the same fraction question as the code (§5.3). Bytes and code are counted in separate
  columns and **never summed** (§4.4).

### 0.3 This surface is the one where the owner carve-out could eat everything, and that is a trap in BOTH directions

The row describes real-time 1v1 duels, **P2P** own-media send, **10 s voice notes with opt-in
consent + push-to-talk**, a **share-link invite**, **Discord rich presence**, a **supporter perk**
on sending, and an **ephemerality contract** on received partner media. On its face that touches
every one of the five owner triggers at once: consent, sensors, networking, persistence,
entitlement.

Two opposite failures are therefore both live, and the rule that prevents each is fixed in advance:

1. **Folding an owner decision into a size.** Prevented by §6: any behaviour meeting the §4.2
   contract test goes into its own owner-flagged section, is never priced, and never appears inside
   a buildable subset.
2. **Manufacturing a refusal by rule.** SP-127 §12.1 hit exactly this: a flat reading of
   "persistence" owner-gates nearly every row of a state-shaped surface and empties the inventory,
   producing a refusal that came from my rule rather than from the code. Prevented by keeping
   §4.2's operative word — `capability-inventory.md:70` says *"**Expanding** sensors, derived data,
   persistence, networking, diagnostics, or telemetry"*.

**The row itself names a candidate for a networking-free decomposition — "solo practice mode".**
I record that as the live decomposition question, and I pre-judge nothing about it: whether it is
separable is answered by opening it, exactly like every other row.

### 0.4 One capability-inventory sentence may be in direct tension with this surface, and I will not resolve it quietly

`client/docs/capability-inventory.md:69` ends: *"Audio capture is never opened."* The board row
describes 10-second voice notes with push-to-talk. If the shipping source confirms the row, the port
cannot take that behaviour without an owner decision that changes an owner-authority document, and
the honest output is a flagged section naming the tension — **not** a size, and **not** my
resolution of it. `docs/constitution.md:37` independently forbids broadening capture and consent
boundaries. I state the tension; the owner settles it.

---

## 1. The universe, stated as directories

### 1.1 The roles

**The universe is the repository root, walked recursively.** There is no enumerated list of roots
that could be incomplete. The rows below are **ROLES over that one walk** — they say what a hit
MEANS, not where the walk goes.

| # | Role | Extent | In/out | What a hit there means |
|---|---|---|---|---|
| **U0** | **THE UNIVERSE** | **the repository root, recursive** | **IN** | Everything not matched by X2 is searched: dotted directories, spaced directories, `spine-tasks/`, `stream-overlay/`, `pack-zips/`, `redist/`, and every root-level file of every extension. **No source file is excluded by name, anywhere, for any reason.** |
| U1 | behaviour | `ConditioningControlPanel/` minus `CCP.*` | IN | The shipping WPF product. The only authority on what the Goon Game does. |
| U2 | behaviour | `Tests/` | IN | The WPF test project. A behaviour asserted there is behaviour, and it names members the product tree may only reach reflectively or through XAML. |
| U3 | drivers | every other root-level path reached by U0 | IN | Non-C# drivers decide real things. SP-125 proved it: `build-installer.bat`, `.gitignore` and `MODDING.md` each carried load-bearing FYP facts its first-pass universe could not see. For a surface with a **share-link invite** this matters more than usual — a URL scheme registration would live in `installer.iss`, not in a `.cs` file. |
| U4 | capability | `client/` | IN | The port. Supplies the port-anchor side of every mapping **and the already-shipped-it check** (§5.2). |
| U5 | rules | `docs/`, `client/docs/`, `CLAUDE.md` files | IN | Governance. Consulted for RULES, never for behaviour and never for status. |
| X1 | `ConditioningControlPanel/CCP.*` | recursive | **IN the sweep, OUT of the capability side** | Searched, because a mention there is a lesson and (per D209) the shipping product project-references `CCP.Core`. **A `CCP.*` artefact may NEVER satisfy COVERED or PARTIAL, not even as corroboration** — `docs/constitution.md:32` forbids importing its classes, interfaces, timers, DI topology **or status claims**, and citing it as proof the port can do something IS a status claim. Enforced structurally: the port-anchor cell accepts `client/src/**` paths only. |
| X2 | `**/bin/`, `**/obj/`, `.git/`, `__pycache__/`, `**/node_modules/`, `*.log`, `*.binlog`, `*.nettrace*`, `*.etlx`, `*.speedscope.json` | n/a | **OUT** | Build output, package caches and traces. **Every exclusion is a pattern over GENERATED bytes. No exclusion names a source file.** |

If this surface's settings turn out to live in `CCP.Core/Models/AppSettings.cs` as FYP's did, D209's
narrow resolution applies unchanged (a linked project is behavioural evidence for the product that
links it, `ConditioningControlPanel.csproj:52`), it is **re-verified rather than assumed**, and it is
flagged rather than resolved unilaterally.

### 1.2 The structural device: I reuse the committed walk, UNMODIFIED

`spine-tasks/SP-127-trainer-card-census/walk.mjs` is already committed
(`4327cb11f`, sha256 `460c93558d7112f4caf35ffc5669bdc609f1f2ee7afec92d5e2a8b3e8bf54fa5`) and a
reviewer verified its structural properties **by running it**:

- its only positional argument is a **ROOT DIRECTORY**, so a hand-assembled file list is *not
  expressible*;
- its exclusions are a **frozen constant** with **no `--exclude` flag**, so no source file can be
  dropped by name, and the set is printed on every run;
- it records every symlink it declines to follow;
- it cross-checks each walk against `git ls-files` and **reports disagreements rather than
  reconciling them**.

**I will run that file in place and change nothing in it.** The packet permits copying it into my
packet folder *if I need to adjust it*; I do not, and a copy is strictly weaker evidence than the
original, because a copy can silently differ from the artefact whose properties were verified. The
census will state the sha256 above beside the invocations, so a reviewer can confirm the bytes I ran
are the bytes that were reviewed. **If I later find I need a change, the change is made in a copy
under `spine-tasks/SP-129-goon-game-census/`, is stated in the census with its reason, and the
original's hash is stated alongside so the delta is visible** — I do not edit another packet's
folder in any case, and that folder is read-only to me.

Grouping a walk by subtree (the §5.1 fraction) is done by shell over the walk's own stdout, with the
full pipeline printed in the census. **A number with no invocation printed beside it is a defect in
the census**, and I will treat it as one.

---

## 2. Method

**M1 — Directory-first counting.** Every count comes from a recursive walk over a DIRECTORY, via
§1.2. Two independent counts (the walk and `git ls-files`) for any tree that could hold untracked
bytes. Disagreements are reported, never reconciled.

**M2 — Both interpretations of an inherited count are tested.** SP-127 found the row's numbers were
**merge-delta** counts, not directory totals. So R1 and R2 are each measured **two ways** — the
directory as it stands today, and the files ADDED by the merge that brought this surface onto the
port branch, by `git diff --diff-filter=A --name-only <merge>^1 <merge> -- <dir>`. The candidate
merge is *identified by search*, not inherited; if more than one range is plausible I report every
range I tested and which one the row's numbers land in, as SP-127 did.

**M3 — Token sweep to a fixed point over U0.** Case-insensitive, recursive, over the repository root.

*Seeds, taken from the board row's own nouns and from nothing else:* `goon`, `goongame`, `duel`,
`1v1`, `heat`, `sudden death`, `payload throw`, `voice note`, `push-to-talk`, `pushtotalk`,
`invite`, `share link`, `sharelink`, `rich presence`, `richpresence`, `discord`, `practice`,
`ephemeral`, `partner`, `match`, `seat`, `peer`, `p2p`, `signal`, `lobby`.

*Anchoring.* `match`, `heat`, `peer`, `invite`, `practice`, `seat` and `signal` over-match badly
(`Regex.Match`, `HeatMap`, `PeerDependency`, `Matches`, …). Each is matched with a word boundary
and, **where an anchored needle still over-matches, I report the noise count and OPEN the matches
rather than quietly narrowing the needle.** SP-125's lesson is exact here: its seed `fyp`
unanchored matched `NotifyPercent`, its own plan failed to anticipate that, and the error was caught
only by opening matches instead of counting them. **Anchored patterns of record are printed in the
census.** I expect `goon` itself to be the cleanest needle and I will still check it rather than
assume it.

*The reveal rule, so the fixed point is defined and not elastic.* A hit **reveals** a new token iff
**(a)** it is inside a file already established as part of the surface, or **(b)** it names one of
the surface's own members (type, method, setting key, resource key, message name, CLI flag, URL
scheme, file name). A hit that merely co-occurs with a needle reveals nothing. **This rule is what
found the two `Chaos/` files in SP-125 that contained no occurrence of the token `fyp` at all.**

**M4 — Consumer closure before any equivalence claim.** "It is only these N files" is inadmissible
until every consumer is enumerated. For each public type and member of the surface, grep U0 for its
bare name and record every hit site, split into **compile-time consumers** and **comment-only
references** (SP-125's split, kept because conflating them overstates). A surface whose consumers
were sampled rather than enumerated is reported as **unbounded**, not as small.

**M5 — Every cited line is opened.** No `File.cs:line` appears in the census, the record or the
divergences unless I ran `sed -n` on that exact path and the text I quote is the text I saw.
Citations are taken against this worktree's HEAD and the SHA is recorded in the census. **This plan
already obeys it (§11).** Three packets running have shipped wrong citations, twice inside their own
headline findings, so §8.2 additionally makes the headline citations machine-checked.

**M6 — The port side is walked, not remembered.** `client/src/` is walked **whole** for every seed
and every upstream member name. "The port has no X" is a claim produced by a walk, never by reading
a board row or a landing log.

**M7 — Bytes are counted, classified, and never forked.** Any `Resources/` or payload tree in scope
is counted by walk, by extension and by byte total, and classified as CODE or BYTES. Upstream web
payloads are linked read-only out of the legacy tree by csproj glob and copied to `payload/`; **the
bytes stay owned by the legacy tree and are never forked into `client/`** (root `CLAUDE.md`, and the
`dtrh`/`intake`/`tunnel`/`vendor` precedent). **No proposal to copy bytes into `client/` will be
written.** If a tree cannot be served that way, the reason is stated as a finding.

**M8 — Verbatim comparison for anything called load-bearing.** Where a threshold, clamp, timing or
ordering is behaviour-visible (the row names a **64 MB video cap** and **10 s voice notes**), the
census quotes the exact upstream expression from an opened line and states `MATCHES` / `DIFFERS` /
`ABSENT` against the port — never a paraphrase, never "equivalent".

---

## 3. Capability decision rule — fixed now, so the verdict is not a judgement call

Every behaviour B gets a row of **five** cells. **A row is invalid unless all five are filled.**

1. **WPF evidence** — `File.cs:line`, opened per M5, with the quoted text.
2. **Required primitive** — mechanism-free, phrased as an observable ("two people on different
   machines see the same score change within a second of each other"). Never an API name.
3. **Port anchor** — a `client/src/**` path and line, opened per M5, or the literal word `none`.
   **`CCP.*` paths are not accepted in this cell** (§1.1 X1).
4. **Label** — exactly one of four. **The vocabulary is closed; I may not invent a fifth.**

| Label | Assigned iff |
|---|---|
| **COVERED by \<anchor\>** | A type already in `client/src/**` **exposes the required primitive**, cited at a line I opened, **and** reaching it needs no new platform interop, no new OS API and no new owner decision. |
| **PARTIAL on \<anchor\>** | Such a type exists and is cited, but the behaviour needs a mode, parameter or member it does not have. **The missing member is NAMED. A PARTIAL that cannot name the missing member is demoted to GAP.** |
| **GAP: \<named primitive\>** | No such type exists. Names (a) the primitive, (b) the OS API or library WPF uses to get it, (c) what the port would have to build, in one sentence. |
| **OWNER-GATED** | The required primitive meets the §4.2 contract test. Gets its own flagged section and **no size** (§6). |

**3.1 The anchor set is the seven landed capabilities OR a shipped in-window precedent.** The port's
seven landed capabilities are structural — they are directories in `client/src/CcpClient.Desktop/`:
`Overlay`, `Input`, `Audio`, `Video`, `Pointer`, `Glyph`, `Haptics` (directory listing opened, §11).
Those are **OS-interop** capabilities. SP-127 established that a UI-over-state surface maps onto no
OS capability and that a rule admitting only those seven would label ordinary widget work `GAP` and
**manufacture a refusal**. So the port-anchor cell accepts **exactly two kinds** of anchor, both
cited at an opened `client/src/**` line:

- **(a) one of the seven OS capabilities**, when the behaviour needs OS interop; or
- **(b) a SHIPPED in-window precedent** — a real page, control, host window, persistence store,
  navigation route or entitlement type already in `client/src/**` that **exposes the required
  primitive**.

**`"Avalonia can do it"` is not an anchor. A shipped file that does it is.** SP-127 §12.3's
correction binds: "a shipped page exists" does not anchor a primitive that page does not expose.
If neither kind exists, the label is GAP and the primitive is named.

5. **Platform** — `Windows: proven|unproven` / `Linux: proven|unproven`, and **when either is
   `unproven`, the exact manual gate is named IN the cell.**

**Why cell 5 is mandatory.** `docs/constitution.md:36`: *"**Windows AND Linux** (X11/Wayland
distinguished where behavior differs) or a documented product blocker. Compilation, a stub, a no-op
fallback, or a **Windows-only test never proves cross-platform support**."* And
`client/memories/port-status.md:89-93` records the measurement: `wsl.exe --list --verbose` reports
*"Windows Subsystem for Linux has no installed distributions."* **`Linux: unproven` is therefore the
DEFAULT for every row of this census**, and no row may launder a Win32-only mechanism into a
cross-platform COVERED.

"Probably", "should be easy", "similar to" are not labels. **An unlabelled behaviour is a defect in
this census. A GAP is a finding, not a blocker** — it is named and the census continues.

---

## 4. Verdict rule — fixed now

### 4.1 "Essential" is anchored to the owner's own noun phrases, not re-derived per row

The owner already wrote this surface's identity as noun phrases at `client/docs/task-board.md:107`,
and **that fixed list is the test**:

> Real-time 1v1 duels: **media payload throwing**, **heat build**, **sudden death**; **P2P own-media
> send** (photos/videos/GIFs, **64 MB video cap**, sending is a **supporter perk** while every seat
> receives and duels in full); **10 s voice notes** with **opt-in consent + push-to-talk (V)**;
> **share-link invite with no account needed**; **Discord rich presence**; **solo practice mode**.
> Privacy contract (upstream's own): **received partner media is ephemeral and never outlives the
> match** — port it as a contract, not an implementation detail.

A behaviour is **essential** iff it realises one of those phrases. The phrase-to-row mapping is
written down explicitly so the word cannot drift mid-census. Phrases the walk proves do not exist in
the shipping source are reported as such (a row-evidence finding); behaviours the walk finds that
the row does NOT name are added as rows and flagged as row omissions.

### 4.2 The owner carve-out is a test on the CONTRACT, not on the row

Adopted verbatim from SP-127 §12.1, because inventing a second convention for the same job would
make the three censuses disagree:

| Situation | Disposition |
|---|---|
| The behaviour **expands** the consent contract, the persistence contract, the network boundary, the sensor set or the entitlement boundary — a new sensor, a new class of stored data, a new destination bytes travel to, a new thing shared with another person, a new thing sold | **OWNER-GATED.** Own flagged section, no size, no label implying the port may proceed. |
| The behaviour **writes to a store the port already owns**, under a contract already in force | **SIZABLE.** Labelled COVERED / PARTIAL / GAP like any other row and counted in the inventory. |

The unit of the test is the **contract**, at surface level (§6), never the individual row, and the
census states which side each row landed on and why. **A second machine is another person**: a
behaviour that sends any byte about the user to a peer, a relay, a signalling server or a Discord
client is on the gated side by construction, whichever verb it uses. SP-127's own review corrected
"POSTs and GETs" to "GETs only" and the disposition did not move, because outbound identity is
outbound identity whatever the verb — that precedent binds here.

### 4.3 The verdict, applied mechanically — evaluated in this order, first match wins

1. **BUILDABLE** iff every row is COVERED or PARTIAL and there are no OWNER-GATED rows.
2. **BUILDABLE-IN-PART** iff a subset of rows is entirely COVERED/PARTIAL, that subset is
   **independently user-observable** (a user could open it and use it), and every remaining row is
   named in the residue with its label. **This clause fires whether the residue is GAP,
   OWNER-GATED, or both.**
3. **REFUSED** iff no such subset exists — i.e. every user-observable decomposition contains a GAP
   or an OWNER-GATED row.

**What an OWNER-GATED row does to the verdict, said plainly:** it behaves exactly like a GAP for
verdict arithmetic — it can never appear inside the buildable subset — but unlike a GAP it is **not
the port's to close**, so it is reported in its own flagged section and never priced. **Clause 2
outranks clause 3 on overlap**, because naming the buildable part is strictly more useful to the
owner than a bare refusal.

**"Independently user-observable" is the one soft predicate in this rule.** SP-127 named it as such
rather than hiding it, and I will do the same: if the verdict turns on that predicate, the census
says so explicitly, states the alternative reading, and states what the verdict would be under it.
**I am not permitted to prefer an outcome.** The verdict is whatever the labelled rows say.

### 4.4 Size is an inventory of named units. Bytes and code are never summed

The size that follows the verdict is **an enumeration of units with their file counts and line
counts**, never a t-shirt letter and never a day count. **Code files and asset bytes are reported in
separate columns and never added together**, because 184 payload files and 25 `.cs` files are not
the same work and a single "209 files" number would hide which one this is. The board row's own
`Size XL` is a claim under test like every other; if the verdict is a size, it is derived from the
inventory and stated with the inventory, and if the row's XL turns out to be wrong in either
direction that is reported.

---

## 5. The this-surface fraction — how it is computed, so it is not an impression

**Every count that reaches the census carries a fraction: how much of it is actually this surface.**
This is the packet's central requirement and §0.1's rule made operational.

### 5.1 The mechanical part: attribution per file

For each file in a counted tree, exactly one attribution, and the evidence for it is stated:

| Attribution | Assigned iff |
|---|---|
| **THIS SURFACE** | The file implements one of §4.1's noun phrases, or exists only to serve a file that does, and **no compile-time consumer outside this surface names its types** (M4). |
| **SHARED** | It has at least one compile-time consumer outside this surface. **The consumer list is printed.** This is SP-125's `Services/Fyp/Online/` shape. |
| **FOREIGN** | It implements a different surface's behaviour and merely shares a directory or a merge with this one. This is SP-127's `Views/Controls/Companion/` shape. |
| **ALREADY SHIPPED IN THE PORT** | It corresponds to a module `client/src/**` already carries. SP-127 found six such files inside one inherited count; the check is mandatory, not opportunistic. |

The fraction reported is `THIS SURFACE / total`, with the other three buckets enumerated beside it.
A fraction with no per-file attribution behind it is not admissible.

### 5.2 The already-shipped-it check is run, not assumed

`client/src/` is walked whole (M6) for every module name found in a counted tree. This is the check
the haptic count of 14 missed and the check that produced SP-127's sharpest sub-finding.

### 5.3 The payload gets the same question

184 files is large enough to hide a vendored library, a shared asset set or another surface's bytes.
The payload tree is walked, grouped by subtree and by extension, and each group is attributed by the
same four-way rule. **A vendored third-party JS library inside the tree is FOREIGN even though it is
required to run the page**, and it is reported separately with its own count, because "how big is
this surface" and "how many bytes must be served" are different questions.

### 5.4 And the row's directories are not a boundary

The row names two directories; the universe is the repository (§1.1). The sweep (M3) runs over U0
and the census reports **every location the surface is found in, including those the row does not
name**. If the surface's implementation lives somewhere else — SP-127's `MainWindow/` shape — that
is the headline, not a footnote.

---

## 6. The owner-flagged sections, expected in advance so I am not surprised into folding one

Per §4.2, each gated behaviour gets **its own section**, is **never folded into a size**, and gets
**no label implying the port may proceed**. This is the shape SP-125 used for the third-party API it
found and SP-127 used for the privacy dialog. §0.3 says all five triggers are plausibly live here,
so I write the expectation down now rather than discovering it under time pressure:

| Trigger | Why it is expected | Governing rule |
|---|---|---|
| **Networking** | "Real-time 1v1 duels", "P2P own-media send", "share-link invite" | `capability-inventory.md:70`; `constitution.md:37` |
| **Sensor + consent** | "10 s voice notes with opt-in consent + push-to-talk" = a microphone | `capability-inventory.md:69` (*"Audio capture is never opened."*), `:70`, `:78`; `constitution.md:37` |
| **Shown to others** | "Discord rich presence"; a second seat sees the user's media | `capability-inventory.md:70` |
| **Entitlement** | "sending is a supporter perk" | §4.2 |
| **Persistence / ephemerality** | "received partner media is ephemeral and never outlives the match" | §4.2 — and note the direction: this is a **constraint** upstream imposes, so the port's question is whether it can HOLD the contract, which is a different question from whether it may store something |

**None of these is assumed to be true.** Each is a claim I answer from the shipping source with an
opened citation, and if the source contradicts the row that is a finding about the row.

### 6.1 The four privacy questions, answered for the surface as a whole

Each answered yes/no **with a citation**. Any yes gets its own flagged section per §4.2.

1. Does it change what is **persisted** to disk?
2. Does it change what is **shown to anyone other than the local user**?
3. Does it change what **leaves the machine** (network, peer, relay, analytics, a third party)?
4. **What sensor does it turn on, under whose consent**, when does it stop, and **can the user see
   it running?** SP-125's finding was not that consent was missing but that an active sensor was
   invisible; that distinction is preserved here rather than collapsed.

### 6.2 What "networking is owner territory" does NOT license

It does not license refusing to describe the networking. **The census states exactly what the
network behaviour IS** — endpoints, transport, what is transmitted, whether it is first-party,
third-party or peer-to-peer, what identity travels with it, and whether any of it is optional —
because the owner cannot decide a question that has not been stated. What it forbids is turning any
of that into a number in a size table.

---

## 7. The payload: counted, classified, served by glob, never forked

M7, applied concretely. The census will state:

- the count from the walk and from `git ls-files`, and whether they agree;
- the breakdown by extension and by subtree, with §5.3's attribution;
- total bytes, stated as bytes-on-disk and explicitly **not** as a packaging decision;
- **how the port would serve it without forking a byte** — the linked read-only csproj glob that
  already serves `dtrh`, `intake`, `tunnel` and `vendor`, cited at the shipped lines in
  `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj` which I will open;
- whether serving it is even meaningful for this surface, given the verdict — a payload for a page
  that cannot be rendered is a cost with no benefit, and that is worth saying if it is true.

**No proposal to copy bytes into `client/` appears anywhere in what I write.**

---

## 8. Pinning — RE-DERIVED FROM THE SHIPPING BYTES, and it pins WHAT I CLAIM

`client/tests/CcpClient.Tests/GoonGameCensusTests.cs` (new, pure logic, no Avalonia runtime).

### 8.1 The vacuity to avoid is named in this repository's own words

`client/tests/CcpClient.Tests/HapticSiteCensusTests.cs:11-15`, opened:

> *"A guard that only checked the document against itself would have passed on all four numbers. So
> the document is the DATA and this file is the LOGIC ... the needle set and the file universe live
> HERE, and the candidate line set is RE-DERIVED from the shipping bytes on every run. Editing the
> census document can never shrink the search."*

and the file-list ban at `:43-45`:

> *"The family: three directories, searched whole and recursively. NOT a file list — a file list is
> exactly how `VideoService.Browser.cs` and `BouncingTextService.cs` were missed twice."*

So: **the roots live in the TEST as DIRECTORY strings**, the counts **re-derive from the
`ConditioningControlPanel/` tree on every run**, upstream gaining a file **reds the suite**, and
editing `client/docs/goon-game-census.md` can never shrink the search.

### 8.2 THE SP-127 DEFECT, AND THE FIX: a pin must watch the NUMBER, not the path

This is the packet's sharpest instruction and it is a correction of the immediately preceding
packet. SP-127's §9.4 pinned **paths** and regex-matched an expression **anywhere in the file**, so
its suite was green while three of its own citations were wrong by 3 and 5 lines — inside the packet
whose own §4.1 grades someone else's comments for exactly that. Its §9.5 asserted only `> 0.90` for
a share it published as 96.5%, so the claim would have survived drift to 91%.

**Therefore, binding on this census:**

- **Every `File.cs:line` citation that carries a headline finding is pinned by LINE.** The census
  §9 carries a table of `key | path | line | needle`, and the guard reads **that exact line** of the
  shipping file and asserts the needle is **on it** — not somewhere in the file. A citation that
  drifts by one line reds the suite.
- **Every fraction is pinned EXACTLY**, to the tenth of a percent, with its numerator, its
  denominator and both terms re-derived from the bytes. No `>` threshold anywhere.
- **Every count is pinned exactly**, re-derived by a recursive directory walk in the test.
- **Any number I publish that a test does NOT re-derive is listed in the census's "what this does
  not prove" section**, by name, with the reason (SP-127's 96.5% was historical, from
  `git diff --diff-filter=A`, and it said so). A published number is either machine-checked or
  explicitly disclaimed; there is no third state.

### 8.3 Structural requirements on the guard

- **Repo root anchors on `client/CcpClient.sln`** (present in every checkout and in a worktree).
- **A missing reference tree FAILS, it never skips.** `PROMPT.md` §4 says so, and SP-127 §13 records
  that the machinery forces it anyway: `VacuousShapeDetector` classifies an early return past a
  filesystem predicate as a silencing shape (`early-return`, `fs-predicate`,
  `VacuousShapeDetector.cs:32-38`) and `VacuousShapeGuardTests` reds the floor unless the site is
  dispositioned in `client/tests/floor/vacuous-shape-ledger.json` — **which is outside this packet's
  write scope.** So the reference check hard-asserts the repo root, the census document **and**
  `ConditioningControlPanel/`, every filesystem predicate lives in a helper rather than in a fact
  body, and there is no branch to be permanently unreachable.
- **Fixture facts** pin the parser and the comparer against temp-dir inputs, so nothing rests on
  today's tree happening to agree.
- Document-shape assertions stay document-only, which is legitimate because their subject IS the
  document: every behaviour row carries one of the four labels and none is blank; every row carries
  a platform cell; the four privacy answers are present; every owner-flagged section exists and
  carries no size.
- **No wall-clock waits.** These are pure assertions over documents and directory walks; none is
  needed. `Thread.Sleep`, bare `Task.Delay` and clock polls are not used.

---

## 9. Constraints I am operating under

- **`client/src/**` is CLOSED. This packet writes no product code.** If the census proves something
  must be built, that is the finding and the next packet is authored from it.
- **Files I may change:** `client/docs/goon-game-census.md` (new),
  `client/docs/wpf-surface-reachability.md` (**divergences ONLY**),
  `client/tests/CcpClient.Tests/GoonGameCensusTests.cs` (new), and
  `spine-tasks/SP-129-goon-game-census/**`. Nothing else, and specifically not
  `client/docs/task-board.md`, not `client/docs/upstream-payload-inventory.json`, not
  `client/tests/floor/**`, not `ConditioningControlPanel/**`, not the sibling census documents or
  their tests.
- **Divergence ids start at D240.** Verified: the highest existing id in
  `client/docs/wpf-surface-reachability.md` is **D225** (`:1469`); the sibling packet holds
  D226-D239, so D240 is the first free id and I stay above D239. `validate-wave.mjs` checks paths,
  not id ranges, so this is mine to hold.
- **Pipes are escaped in table cells.** A bare `|` inside a code span silently truncates the row —
  how D197 and D209 lost their disposition. Every `|` inside a code span in anything I write is
  `\|`, and I grep my own output for unescaped pipes before committing.
- `client/tests/floor/floor.json` is **never opened**. My count change is declared in
  `spine-tasks/SP-129-goon-game-census/floor-delta.json`. **Pin 2399 unit / 144 headless**; my
  observed floor will be **pin + declared delta**, and I state both numbers in the report.
- Both gates alone, from the repo root: `node client/tests/floor/check-warnings.mjs`, then
  `node client/tests/floor/check-floor.mjs`. Build must be 0 warnings / 0 errors.
- `.DONE` is created last and is **not committed**.

---

## 10. Order of work after this checkpoint

1. Run the committed walk (§1.2) over `Services/GoonGame/` and `Resources/web/goon/`. **Verify R1
   and R2** both ways (M2), inheriting neither number.
2. **Attribute every file in both trees by §5.1**, and state the this-surface fraction for each
   count. Run the already-shipped-it check (§5.2). If a count is exact and misleading, that is the
   headline (§0.1).
3. Token sweep to a fixed point by M3 over U0; report every location the surface is found in,
   including any the row does not name (§5.4).
4. Consumer closure by M4 over the shipping tree; classify shared subsystems by §5.1.
5. Open the networking, sensor, entitlement and ephemerality code and state **what it actually
   does** (§6.2) before deciding anything about it.
6. Label every behaviour by the §3 five-cell rule, with the port side walked per M6 and platform
   cells per §3 cell 5.
7. Answer §6.1's four privacy questions; write every yes into its own flagged section (§6).
8. Count and classify the payload per §7; state the serving mechanism; propose no fork.
9. Apply §4.3 mechanically. Verdict with an inventory (§4.4), and a size the next packet can be
   authored against — or the recorded finding that it cannot be, with the inventory that proves it.
10. Write `client/docs/goon-game-census.md`; pin it with `GoonGameCensusTests.cs` per §8, including
    the §8.2 line-level citation pin; record divergences from **D240**; write `record.md`; declare
    the floor delta; run both gates; grep my own output for unescaped pipes.

---

## 11. Citations I opened before writing this plan (M5 applied to the plan itself)

| Cited | Verified? | What it actually says |
|---|---|---|
| `client/docs/task-board.md:107` | **yes** | The Goon Game row. `Services/GoonGame/` (25 new files) + `Resources/web/goon/` (184 new payload files); real-time 1v1 duels, media payload throwing, heat build, sudden death; P2P own-media send, 64 MB video cap, sending a supporter perk; 10 s voice notes with opt-in consent + push-to-talk (V); share-link invite; Discord rich presence; solo practice mode; the ephemerality contract; `Size XL — decompose before scheduling`. |
| `client/docs/upstream-payload-inventory.json:24-28` | **yes** | `"name": "goon"`, `"disposition": "not-ported"`, `"fileCountAtBaseline": 184`, and a note naming `Services/GoonGame/GoonHostService.cs` as the host. A second record of R2; the board classes `fileCountAtBaseline` as record data rather than an assertion. |
| `docs/constitution.md:32` | **yes** | Read-only zones; `CCP.*` never imported — including "or status claims", which is why §1.1 X1 bars it as corroboration. |
| `docs/constitution.md:36` | **yes** | "Windows AND Linux ... a Windows-only test never proves cross-platform support." |
| `docs/constitution.md:37` | **yes** | "never broaden webcam, biometric, secret, path, logging, capture, moderation, consent, or network boundaries." |
| `client/docs/capability-inventory.md:69` | **yes** | Ends *"Audio capture is never opened."* — §0.4. |
| `client/docs/capability-inventory.md:70` | **yes** | "**Expanding** sensors, derived data, persistence, networking, diagnostics, or telemetry requires a consent-contract revision and owner review." The word "Expanding" is the one §4.2 turns on. |
| `client/docs/capability-inventory.md:78` | **yes** | Windows **and** Linux must provide real enumeration/capture/teardown; "A stub that says running is a failure." |
| `client/memories/port-status.md:89-93` | **yes** | WSL2 present, **no installed distributions**; every Linux claim is a named limit on this machine. |
| `client/tests/CcpClient.Tests/HapticSiteCensusTests.cs:11-15` | **yes** | Document is DATA, test is LOGIC; set re-derived from shipping bytes every run; "a guard that only checked the document against itself would have passed on all four numbers." |
| `client/tests/CcpClient.Tests/HapticSiteCensusTests.cs:43-45` | **yes** | "The family: three directories, searched whole and recursively. NOT a file list — a file list is exactly how `VideoService.Browser.cs` and `BouncingTextService.cs` were missed twice." |
| `client/tests/CcpClient.Tests/VacuousShapeDetector.cs:32-38` | **yes** | The seven silencing-shape constants, `early-return` through `dynamic-skip` — §8.3. |
| `client/tests/floor/vacuous-shape-ledger.json:3` | **yes** | The ledger's purpose line; the guard fails on any detected site absent from it. Read only — the file is outside my write scope. |
| `client/docs/wpf-surface-reachability.md:1469` | **yes** | D225 is the highest existing divergence id, so mine start at D240 with D226-D239 reserved to the sibling. |
| `spine-tasks/SP-125-fyp-census/plan.md:136` | **yes** | The COVERED rule already read "A type already in `client/src/**` exposes the required primitive", which is what §3.1 builds on rather than departs from. |
| `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs:161` | **yes** | `PacketsAtOrAboveSp073_DeclareAFloorDeltaAndNeverOwnTheSharedPin` — both halves required: the packet's own `floorDelta` row and the shared-pin disclaimer. Both are present in this packet's `PROMPT.md`. |
| `spine-tasks/SP-127-trainer-card-census/walk.mjs` (whole) | **yes** | Read in full. Its two structural properties are in the code, not in a comment: one positional DIRECTORY argument, a frozen exclusion constant with no `--exclude`, symlinks recorded, `git ls-files` cross-check reported not reconciled. sha256 `460c93558d7112f4caf35ffc5669bdc609f1f2ee7afec92d5e2a8b3e8bf54fa5`, committed at `4327cb11f`. |
| `client/docs/fyp-census.md` (whole) | **yes** | Read in full as one of the two standards this packet is held to. |
| `client/docs/trainer-card-census.md` (whole) | **yes** | Read in full as the other. Its §1.3 is the trap §0.1 encodes. |
| `spine-tasks/SP-127-trainer-card-census/plan.md` (whole) | **yes** | Read in full; §12.1, §12.2, §12.3 and §13 are adopted here rather than re-invented. |
| `client/src/CcpClient.Desktop/` directory listing | **yes** | The seven OS capabilities are structural directories — `Overlay`, `Input`, `Audio`, `Video`, `Pointer`, `Glyph`, `Haptics` — alongside `Entitlement`, `Navigation`, `Persistence`, `Session`, `Tray`, `Features` and `Views`, which are where §3.1(b) anchors would live. |

---

## 12. What I deliberately did NOT open before this checkpoint

| Not opened | Why |
|---|---|
| Anything under `ConditioningControlPanel/Services/GoonGame/` | **Surface content.** Held until after this checkpoint so the method is fixed before the answer is known. Every claim about it in this plan is written as a CLAIM UNDER TEST, never as fact. |
| Anything under `ConditioningControlPanel/Resources/web/goon/` | Same. I have not counted it, grouped it, or looked at a single filename in it. |
| Any `MainWindow.Goon*.cs`, `Views/**Goon**`, `Models/**Goon**` or similar | Same. §5.4 exists precisely because I do not yet know where this surface lives, and guessing before the sweep is how a row's directories become a boundary. |

**I do not know, at the moment of writing this, whether R1 or R2 is right, what fraction of either is
this surface, what the networking actually does, or what the verdict will be.** That is the point of
committing this file first.

---

## 13. Revision 2 — the plan-gate rulings, adopted BEFORE any mapping began

Plan **APPROVED** at the gate; the reviewer verified every §11 citation against this worktree and
returned six rulings plus one practical hazard. They change RULES, so they are written here rather
than left in a message, and they bind the census. Nothing under `Services/GoonGame/` or
`Resources/web/goon/` had been opened when this section was written, so §12's abstention still
holds.

### 13.1 (Ruling 1) The attribution buckets get a precedence order, a residue bucket, and a fifth label

§5.1's four buckets are **neither provably exhaustive nor disjoint**, which the gate caught and I had
not: a dead file, a stray asset or an unreferenced helper fits none of them, and a file can satisfy
both `FOREIGN` and `SHARED`. **A mis-fit file must never be forced into `THIS SURFACE` by the absence
of anywhere else to put it** — that is the one direction that inflates the numerator.

Also, `FOREIGN` was the wrong label for a vendored dependency: a library required to render THIS
surface's page is a **dependency of this surface**, not another surface's behaviour. The disposition
was right; the label was stretched.

**Replacement rule — evaluated in this order, first match wins. One bucket per file, always.**

| # | Bucket | Assigned iff |
|---|---|---|
| 1 | **ALREADY SHIPPED IN THE PORT** | It corresponds to a module `client/src/**` already carries (§5.2 walk). Highest precedence because it is the most decision-relevant and the check most often skipped. |
| 2 | **SHARED** | At least one compile-time consumer outside this surface names its types (M4). **The consumer list is printed.** Outranks `FOREIGN`, because a file with outside consumers is a subsystem question whichever surface authored it. |
| 3 | **FOREIGN** | It implements a **different surface's** behaviour and merely shares a directory, a tree or a merge with this one. |
| 4 | **VENDORED** | Third-party bytes the surface **depends on** to run — a library, a font, a polyfill — authored outside this product. **Not this surface's implementation, but required to serve it.** |
| 5 | **THIS SURFACE** | It implements one of §4.1's noun phrases, or exists only to serve a file that does, **and** buckets 1-4 do not apply. The two-part test (noun phrase AND consumer closure) and its printed evidence are unchanged. |
| 6 | **UNATTRIBUTED — \<reason\>** | None of the above fits. **The reason is written out per file.** A non-empty residue is a finding to report, never a bucket to empty by reassignment. |

The published fraction is `THIS SURFACE / total`, with buckets 1-4 and 6 enumerated beside it.

### 13.2 (Ruling 1c) The fraction and the serving cost are printed adjacent, never one without the other

§5.3 and §7 must print, side by side and in the same table: **the this-surface fraction** and **the
full count of files that must be served to render the surface** (`THIS SURFACE` + `VENDORED` + any
other bucket the page loads). They answer different questions, and a reader who sees only the
fraction would under-read the serving cost — the SP-127 trap running in mirror image.

### 13.3 (Ruling 2) The walk is BOTH run in place AND committed as a byte-identical copy

My argument that a copy is weaker was wrong in one direction, and the correction is right: **a copy
whose sha256 is asserted equal to the original's is exactly as strong, and it additionally survives
the SP-127 folder being deleted or changed.** So:

- `spine-tasks/SP-129-goon-game-census/walk.mjs` is committed, **byte-identical**, sha256
  `460c93558d7112f4caf35ffc5669bdc609f1f2ee7afec92d5e2a8b3e8bf54fa5`. It is **not modified**.
- The census asserts the hash equality of the two paths and **states which path each invocation
  actually used**.

### 13.4 (Ruling 3) Historical fractions are DISCLAIMED, not pinned — said explicitly

§8.2's "every fraction is pinned EXACTLY, no `>` threshold anywhere" collides with M2's merge-delta
measurements, which are **historical** (`git diff --diff-filter=A` against one commit) and cannot
re-derive from today's bytes. Read with §8.2's fourth bullet the resolution is already
"pinned or disclaimed", and the gate is right that leaving it implicit would later force a choice
between a false pin and a weakened one. **Stated explicitly and binding:**

| Kind of number | Disposition |
|---|---|
| Re-derivable from today's shipping bytes | **PINNED EXACTLY** in §9. No `>` threshold, no tolerance. |
| Historical — a merge delta, a `git diff`, a count at a past commit | **DISCLAIMED BY NAME** in the "does not prove" section, with its denominator and the command that produced it. **Never pinned**, because a pin that cannot re-derive is a pin that will one day be wrong and green. |

There is still no third state.

### 13.5 (Ruling 3b) The numeric-token sweep — build it, watch it red, and never weaken it to ship it

Adopted: one fact extracts **every numeric token** from `client/docs/goon-game-census.md` and asserts
each is either present in the §9 pin table or named in the "does not prove" section. This is exactly
the property SP-127 lost and the only mechanism that would have caught it.

**The constraint, and it is the whole value of the fact:** every exclusion is a **CLASS enumerated in
the fact** — the `File.cs:NNN` citation form, ISO dates, version numbers, section references — and
**never a list of individual literals.** A literal-exclusion list is an escape hatch that eats the
property. **I will add an unpinned number to the census and watch the fact go red naming it, before
shipping it**, and the transcript goes in `record.md`. **If the exclusion classes cannot be kept
honest without becoming a literal list, I ship no weakened version**: I record it as a finding, with
a board row, and say why.

### 13.6 (Ruling 4) The owner-flagged sections follow D225's shape exactly

`client/docs/wpf-surface-reachability.md:1469` is the standing precedent and the census follows it
without deviation: it **names the endpoint, the exact service file and the request lines, and every
toggle**, while stating **"OWNER-GATED and deliberately not priced."** Describing a boundary is not
broadening it under `constitution.md:37`; refusing to describe it makes the owner's decision
undecidable. So §6.2 stands, and the shape of every gated section in this census is D225's.

### 13.7 (Ruling 5) The audio tension is quoted WITH its container

`capability-inventory.md:69` is quoted **together with its section header** `## Webcam, face, and
gaze tracking` (`:66`) and with the subject of the bullet it terminates (frames, crops, tensors,
landmarks, per-frame biometric derivatives). Whether *"Audio capture is never opened."* is a
product-wide prohibition on the microphone or a property of the vision pipeline is **genuinely open
on the text**, and the census presents the scope question rather than a bare sentence. **The
resolution is the owner's**, and the census says so in those words.

### 13.8 (Ruling 6) The floor.json read-disclosure is carried verbatim into `record.md`

Not paraphrased and not summarised, so the final reviewer reads it rather than inferring it.

### 13.9 (Hazard) The walk runs FROM THIS WORKTREE, and the `.claude/` hit count is printed

U0 is the repository root. **The MAIN checkout's `.claude/worktrees/` holds full copies of the
tree** — two exist right now — so the same U0 walk run from there would double-count everything.
This worktree's own `.claude/` holds only `README.md`, `agents`, `settings*.json`, `skills`. So the
census **records that the walk ran from this worktree, names the worktree path, and prints the
`.claude/` hit count** for every U0-wide sweep, so a future re-run cannot inflate silently and a
reader can tell immediately whether it did.
