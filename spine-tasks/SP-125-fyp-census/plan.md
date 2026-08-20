# SP-125 — plan (committed BEFORE any mapping)

Review level 3, step 1. This file exists so the method is fixed before the answer is known.
Nothing below is a finding; every count in it is a placeholder to be filled by a command, not by me.

## 0. Why this plan is shaped this way

The port has been wrong about a count four times in a row (haptic sites 8 -> 13 -> 14 -> 18) and
**every correction came from widening the universe, never from reading harder.** Each of those four
searches ran over a file LIST assembled by hand. Thirteen missed the DEFAULT video engine because one
file was not on the list; fourteen missed a module this port had already shipped.

So the first commitment in this plan is: **no hand-assembled file list appears anywhere in this
packet.** Every number is produced by a command that walks a DIRECTORY recursively, and the command
is printed next to the number it produced.

The second commitment is that the board row's own evidence is a claim to be tested, not an input.
The row (`client/docs/task-board.md:97`) says `Services/Fyp/` (3 new) + `Resources/web/fyp/` changes.
That row came from a sync ledger. SP-120 found four citations inside its own packet that did not say
what the packet claimed; SP-113 found `AppSettings.cs` citations wrong by ~530 lines and in the wrong
path. **If the row is wrong, that is the headline, not a footnote.**

## 1. The universe, stated as directories

Everything below is enumerated recursively from the directory root named. No file lists.

| # | Root | Recursive? | In/out | Why |
|---|---|---|---|---|
| U1 | `ConditioningControlPanel/` minus `ConditioningControlPanel/CCP.*` | yes | **IN** | The shipping WPF product, v6.8.x. This is the behavioural evidence and the only authority on what For You Feed does. |
| U2 | `Tests/` (repo root) | yes | **IN** | The WPF test project. A behaviour asserted there is behaviour, and it names members the product tree may only construct reflectively. |
| U3 | `Tools/`, `scripts/`, `stream-overlay/`, `installer.iss`, `installer-content-deletions.iss`, `notes-v6.7.*.txt`, `redist/`, `pack-zips/` | yes | **IN** | Non-C# drivers. An installer stanza or a release note can be the only place a surface's payload or entry point is named. This is exactly the class of place the haptic count kept losing files. |
| U4 | `client/` | yes | **IN** | The port. Supplies the capability side of every mapping and the already-shipped-it check that the count of 14 missed. |
| U5 | `docs/` (repo root) | yes | **IN** | Constitution + governance; consulted for rules, not for behaviour. |
| X1 | `ConditioningControlPanel/CCP.*` (7 project dirs) | n/a | **OUT** | First cross-platform attempt. `docs/constitution.md` makes it failure/lessons evidence only; its classes, interfaces, timers and DI topology may never be imported into `client/`. It is excluded from the CAPABILITY side. It is **not** excluded from the token sweep — if `CCP.*` mentions FYP that is a lesson, and I will report it as a lesson, never as a design input. |
| X2 | `**/bin/`, `**/obj/`, `.git/`, `__pycache__/`, `*.log`, `*.binlog`, `*.nettrace*` | n/a | **OUT** | Build output and traces. Not source. |

The union U1..U5 is the whole repository minus X1's role-restriction and X2. **I am not searching a
list of files; I am searching the repository.**

## 2. Method

**M1 — Directory-first counting.** Every count in the census is produced by a recursive walk, and the
exact command is printed beside the number. Two independent counts are taken where a tree could
contain untracked bytes: `find <dir> -type f | wc -l` and `git ls-files <dir> | wc -l`. If they
disagree, both are reported and the difference is explained; they are never silently reconciled.

**M2 — Token sweep to a fixed point, over the whole universe.** Seed tokens: `fyp`, `for you`,
`foryou`, `for-you`, `ghost`, `feed`, `reel`. Run case-insensitively over U1..U5 recursively. Then
**iterate**: every type name, method name, setting key, command-line flag, resource key, and file
name that a hit reveals becomes a new token, and the sweep is re-run until a pass adds nothing. The
fixed point is the enumeration. This is the step that finds the file that is not on anybody's list.

**M3 — Consumer closure before any equivalence claim.** "It is only these N files" is inadmissible
until every consumer is enumerated by grep. For each type and public member in the surface, grep the
whole universe for its bare name and record every hit site. A surface whose consumers were sampled
rather than enumerated is reported as unbounded, not as small.

**M4 — Every cited line is opened.** No `File.cs:line` appears in the census unless I have run
`sed -n '<a>,<b>p'` on that exact path and the census quotes the text I saw. Citations are taken
against the shipping tree at this worktree's HEAD, and the HEAD SHA is recorded in the census so a
future reader can tell whether a drift is mine or theirs.

**M5 — Payload counted, never forked.** `Resources/web/fyp/` is counted by directory walk, by
extension, and by byte total. The port's rule is fixed and is not mine to relax: `client/Directory.Build.props`
links upstream web payloads read-only out of the legacy tree by csproj glob and copies them to
`payload/`; **the bytes stay owned by the legacy tree and are never forked into `client/`** (root
`CLAUDE.md`). I will read the existing glob and the existing served-tree precedent (dtrh, intake) and
state how FYP would be served by the same mechanism. **No proposal to copy bytes will be written.**

## 3. Capability decision rule — fixed now, so the verdict is not a judgement call

The port has seven landed capabilities: **overlay, input, audio, video, pointer, glyph, haptics.**

For every behaviour B that the enumeration produces, I emit a row with four cells, and a row is
invalid unless all four are filled:

1. **WPF evidence** — `File.cs:line`, opened per M4, with the quoted text.
2. **Required primitive** — the mechanism-free statement of what must be possible, phrased as an
   observable, e.g. "a window that renders above others and passes clicks through to what is below".
3. **Port anchor** — `client/src/**/File.cs:line`, opened per M4, or the literal word `none`.
4. **Label**, assigned by this rule and no other:

| Label | Assigned iff |
|---|---|
| **COVERED by <capability>** | A type already in `client/src/**` exposes the required primitive, cited at a line I opened, **and** reaching it needs no new platform interop and no new OS API. |
| **PARTIAL on <capability>** | Such a type exists and is cited, but the WPF behaviour needs a mode, parameter or member that type does not have. **The missing member is named.** A PARTIAL row that cannot name the missing member is a GAP. |
| **GAP: <named primitive>** | No such type exists. The row must name (a) the primitive, (b) the OS API or library WPF uses to get it, (c) what the port would have to build, in one sentence. |

"Probably", "should be easy", "similar to" are not labels. A behaviour with no label is a defect in
this census, not an acceptable residue.

**A GAP is a finding, not a blocker.** It is written down and named; it does not stop the census.

## 4. Verdict rule — fixed now

Applied mechanically to the labelled rows:

- **REFUSED** iff at least one behaviour that is essential to the surface's user-observable identity
  is labelled GAP and has no landed substitute. "Essential" is decided by one test only: **remove the
  behaviour and ask whether a user of the WPF build would still recognise the surface.** The test is
  applied per behaviour and the answer is written down.
- **BUILDABLE-IN-PART** iff the surface decomposes into a subset whose rows are all COVERED or
  PARTIAL, that subset is independently user-observable (a user could open it and use it), and the
  GAP set is named as the residue.
- **BUILDABLE** iff every row is COVERED or PARTIAL.

The verdict is whatever the rows say. **I am not permitted to prefer an outcome**, and the size
statement that follows the verdict is an inventory of named units, never a t-shirt or a day count.

## 5. Ghost mode — how it gets answered

The board row's title contains "ghost mode" and nobody has said what it is. It gets answered **from
source only**: the token sweep locates it, I open the defining lines, and the census states what it
does in behavioural terms.

Then a fixed privacy test, three questions, each answered yes/no with a citation:

1. Does it change what is **persisted** to disk (history, telemetry, logs, screenshots, saved state)?
2. Does it change what is **shown to anyone other than the local user** (capture, streaming, screen
   share, an overlay a second person sees, a window that appears in a recording)?
3. Does it change what **leaves the machine** (network, analytics, upload, a third-party service)?

**Any yes -> it is flagged for the owner as its own item, in its own section, and is NOT folded into
a size estimate.** A privacy-relevant behaviour discovered during a sizing exercise is exactly the
kind of thing that disappears into a number, and it will not disappear into mine.

## 6. Pinning, so this cannot drift a fifth time

`client/tests/CcpClient.Tests/FypCensusTests.cs` (new, pure logic, no Avalonia) reads
`client/docs/fyp-census.md` at runtime and asserts:

- the enumerated product-file count and the exact file names, so a new file upstream reds the suite
  rather than being absorbed;
- the payload file count and the not-forked disposition;
- that every behaviour row carries one of the three labels and none is blank;
- that the ghost-mode privacy answers are present.

This follows the existing in-repo pattern of documents enforced by tests that read them at runtime
(`UpstreamPayloadInventoryTests`, `AiOperationContractTests`, `VersionDerivationTests`). The count
that drifts becomes a red test, not a memory.

## 7. Constraints I am operating under

- `client/src/**` is CLOSED. **This packet writes no product code.** If the census proves something
  must be built, that is the finding and the next packet is authored from it.
- `client/docs/wpf-surface-reachability.md` may be touched for **divergences only**, numbered from
  **D207** onward.
- `client/tests/floor/floor.json` is **never opened**. My count change is declared in
  `spine-tasks/SP-125-fyp-census/floor-delta.json`. Pin is 2309 unit / 144 headless; my observed
  floor will be pin + my declared delta, and I will state both numbers.
- No wall-clock waits. The census tests are pure assertions over a document and need none.
- Both gates run alone: `node client/tests/floor/check-warnings.mjs`, then
  `node client/tests/floor/check-floor.mjs`.

## 8. Order of work after this checkpoint

1. Verify the board row's own evidence (`Services/Fyp/` = 3 files? payload = ?) by M1.
2. Token sweep to a fixed point by M2; report the universe delta against the row.
3. Consumer closure by M3 — is anything outside `Services/Fyp/` driving this surface?
4. Open and quote every citation by M4.
5. Answer ghost mode and run the privacy test.
6. Count the payload and state the serving mechanism by M5.
7. Label every behaviour by the §3 rule; apply the §4 verdict rule.
8. Write `client/docs/fyp-census.md`, pin it with `FypCensusTests.cs`, record divergences from D207,
   declare the floor delta, run both gates.
