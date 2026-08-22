# SP-140 — Decode a real clip, and MEASURE what the Media Foundation divergence actually costs

## Mission

**No frame has ever been decoded by this port.** Every video fact to date ran against an
uncompressed `BI_RGB` AVI the suite writes in managed code (board line 71). Two survivors stand:
**M-y**, the video processor is never exercised, and **M-w**, the openable format set is untested
against real files.

**The owner designated a real media directory on 2026-08-22: `C:\Code\ccp media`, 390 files, 6.9 GB,
of which `videos/` holds 54 (53 `.mp4`, 1 `.mov`).** `client/port.txt` allows exactly this
designation. The port has already been run against it and its manifest reconciled every file, but a
manifest ENUMERATES; it does not DECODE.

**And there is a parity question waiting on this that nobody can answer today.** D124
(`wpf-surface-reachability.md:1123`) records the port decoding through **Media Foundation**
(`Video/MediaFoundationClipSource.cs:143`, `MFCreateSourceReaderFromURL`) where upstream decodes
through **LibVLC**, and names the cost in as many words: *"the playable set becomes what Windows can
open rather than what LibVLC can, so a container Windows has no decoder for refuses with
`video-clip-unreadable` where the shipping app would play it."*

**That cost has never been measured. It is a number, the owner's library can produce it, and it is
the difference between a theoretical divergence and videos that silently do not play.**

Your outcome: **one standing test that decodes a real compressed clip, and a measured answer to how
many of the owner's 54 real videos this port can actually open.**

## Two deliverables, and do not collapse them

### 1. The standing test — closes M-y and M-w
A committed fixture the floor can run forever, on any machine, with no dependency on the owner's
directory. The acceptance on board line 71 says **ONE compressed fixture closes both survivors.**

**THE PROVENANCE TRAP, and you must address it explicitly.** If you generate the fixture with Media
Foundation's own sink writer, then Media Foundation decoding it proves close to nothing — the
encoder and decoder are the same stack, and a container it cannot handle is exactly what will not be
produced. **State whether your fixture is circular in that sense.** A small, honestly-described
circular fixture that exercises the processor (closing M-y) plus a clear statement that it does NOT
bound the openable set (leaving M-w to deliverable 2) is a better outcome than a claim that both are
closed. Choose, justify, and say which survivor each half actually closes.

### 2. The measurement — quantifies D124
Open **all 54** of the owner's real videos through the port's real `MediaFoundationClipSource` and
record, per file: does it open, does a frame decode, and if it refuses, the typed reason and the
HRESULT. Report the count that succeed and the count that fail.

**This is EVIDENCE for the record, not a floor test.** It must not enter the suite: it depends on a
directory that exists on one machine, and a test that skips when the directory is absent is exactly
the vacuous-green shape this repository bans. **Run it, record the numbers with the reasons, delete
the harness or leave it as an explicitly non-suite tool.**

**Do not read or copy the owner's media anywhere else.** Open, decode, record the verdict, release.
Nothing is copied into the repo, nothing is logged beyond filename and outcome, and **no file content
and no still frame is written anywhere.**

## What the number means, stated before you have it

- **54 of 54 open** → D124's cost is theoretical on this library. Say so; it is a real result and
  the strongest one available.
- **Some refuse** → those are **videos that play in the shipping app and do not play here**, which is
  a user-visible parity defect and belongs on the board as one, with the container and codec named.
- **The `.mov` is the interesting case** — the DTRH manifest already declines to serve it as
  media-like-but-not-served, which is a different mechanism from whether MF can decode it. **Two
  different questions about one file; do not merge them.**

## The other traps

### 1. This machine has ONE display and a contended desktop
The real-desktop families flake environmentally and the victim MOVES: a lane measured 14
`GlyphCapabilityTests` and zero `PointerCoexistenceTests`; the land an hour later saw 3
`PointerCoexistenceTests` and no Glyph. **Compare FAILURE SETS, never counts.** Do not chase them.

### 2. Decoding is not presenting
A decoded frame in memory is not a composited pixel. **`client/docs/verification-harness.md`
governs**, a headless frame never discharges a headed gate, and nothing here is
`presentation-verified`. Cadence, order and timing stay unmeasured and you must say so: a clip
playing at half speed or backwards would still pass everything you write.

### 3. Standing rules
No wall-clock waits — `TestWait` only. No TODOs. Every new guard watched red **at the committed
head**, with the SHA. `| **Dnnn** |` rows carry exactly five unescaped pipes: escape `|` inside code
spans as `\|` and **verify by counting delimiters, not by reading.**

### 4. Divergence ids: **D323 onward** (D311-D322 are SP-139's)

## File Scope

| | |
|---|---|
| May change | `client/tests/CcpClient.Tests/RealClipDecodeTests.cs` (new), `client/tests/CcpClient.Tests/assets/**` (the fixture), `client/src/CcpClient.Desktop/Video/**` (ONLY if the measurement exposes a defect — report first), `client/docs/wpf-surface-reachability.md` (divergences ONLY, D323 onward), and `spine-tasks/SP-140-real-clip-decode/**` |
| Must not change | everything else, and specifically `client/tests/CcpClient.Tests/{PointerCoexistenceTests,InputCapabilityTests,OverlayCapabilityTests,PointerCapabilityTests,GlyphCapabilityTests,RealDesktopCollection}.cs`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/capability-inventory.md`, `client/docs/execution-census.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-140-real-clip-decode/floor-delta.json` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/RealClipDecodeTests.cs` |
| fileScopeMustNotChange | `client/tests/CcpClient.Tests/PointerCoexistenceTests.cs`, `client/tests/CcpClient.Tests/GlyphCapabilityTests.cs`, `client/tests/CcpClient.Tests/RealDesktopCollection.cs`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/capability-inventory.md`, `client/docs/execution-census.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-140-real-clip-decode/record.md`, `spine-tasks/SP-140-real-clip-decode/plan.md`, `spine-tasks/SP-140-real-clip-decode/floor-delta.json` |

**Pin: 2616 unit / 152 headless.** `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint BEFORE any edit:** where the fixture comes from and **whether it is circular**;
   which survivor each deliverable actually closes; how the measurement runs without entering the
   suite; and which edit each new guard reds on.
2. Build the standing test.
3. Run the measurement over all 54 files. **Report the number before drawing any conclusion from it.**
4. If any file fails to open, name the container and codec and propose a board row. **Do not fix the
   decoder in this packet** — report it.
5. Divergences **D323 onward**.

## Completion Criteria

- A real compressed clip decodes through `MediaFoundationClipSource` in a committed, machine-independent test.
- The fixture's provenance and any circularity are stated, and each survivor is closed or explicitly not.
- All 54 real videos measured, with per-failure reasons and HRESULTs.
- Nothing from the owner's media copied, logged beyond name and outcome, or written anywhere.
- No test skips on a missing directory; nothing added to `allowedSkips`.
- Build 0 warnings / 0 errors. Contended real-desktop reds expected; compare sets.

## Do NOT

- Claim M-w closed by a Media-Foundation-encoded fixture without naming the circularity.
- Add a test that skips when the owner's directory is absent.
- Copy, log or write out any of the owner's media content or frames.
- Fix the decoder in this packet if the measurement exposes a defect. Report it.
- Use a divergence id below D323.

## Git Commit Convention

Conventional commit, `feat(SP-140): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the fixture's provenance and circularity verdict, the per-survivor closure claim,
the full 54-file measurement with reasons, the evidence class, the red demonstrations with the head
SHA, and the before/after failure sets.
