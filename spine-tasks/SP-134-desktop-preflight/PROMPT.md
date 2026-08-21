# SP-134 — Make a contended desktop say so, instead of failing three tests opaquely

## Mission

**The wave-66 land could not certify a green tree, and burned nine full floor runs finding out why.**

Three green, six red. Every failure a `[Collection(RealDesktopCollection)]` fact. The failing test
**moved between runs** — `VideoOverlayCoexistenceTests` four times, `PointerCapabilityTests` once,
`InputCapabilityTests` once. All of them pass **5/5 in isolation**. The cause, identified positively
and only after eight runs of elimination: **the foreground window was `ConditioningControlPanel
v6.8.1` — the shipping WPF product itself.**

**The harness names this residue in advance and by name.** `RealDesktopCollection.cs:44-48`: a FOREIGN
topmost window — *"the shipping WPF product re-asserting `HWND_TOPMOST` on a cadence
(`Services/Flash/FlashService.cs:206-243`)"* — can own a point on the real desktop, and *"no
in-process mechanism can exclude one"*, recorded as *"a gap the floor admits rather than hides"*.

**The gap is admitted. What is missing is that it announces itself.**

Your outcome: **a real-desktop run that refuses at the fixture, naming the window that owns the
desktop — instead of three unrelated assertion failures a reader must reverse-engineer.**

## WHAT THIS IS NOT, AND THE REPOSITORY IS EMPHATIC

**This is not a skip, not a quarantine, not a retry, and not an `allowedSkips` entry.** Three
independent statements already forbid that:

- `RealDesktopCollection.cs:35-38` — *"Not a retry: nothing is ever re-run, and a run that fails still
  fails. Not a skip and not an `allowedSkips` entry."*
- `RealDesktopLease.DescribeRefusal:200-201` — *"A contended desktop is not a flake and must NOT be
  retried away."*
- `client/tests/floor/floor.json`'s `admissionRule` — a listing needs a machine or OS property **plus
  a named machine class where the test DOES execute**. There is none for *"something else had the
  desktop just then."*

**A refusal that makes the suite green is a failure of this packet.** The run must still fail. What
changes is that it fails **once, early, and by name**, instead of three times, late, and opaquely.

## THE CENTRAL TRAP: a detector that cannot see the thing it is named for

`FlashService.cs:206-243` re-asserts topmost **on a cadence** — so the offending window may not be
topmost at the instant you look. **A single-sample check will report a clean desktop and then three
tests will fail anyway**, which is worse than today: it adds a green light in front of the same red.

**Sample over the fixture's lifetime, or state precisely what one sample can and cannot establish.**
If you cannot detect it reliably, **say so and report what you CAN detect** — an honest partial
detector with its blind spot named beats a confident one that misses a cadence.

## THE OTHER TRAPS

### 1. Do not weaken a single assertion
The OS-level facts in `VideoOverlayCoexistenceTests`, `PointerCapabilityTests` and
`InputCapabilityTests` are what SP-099 and SP-100 earned. **Nothing in them changes.** You are adding
a pre-flight, not adjusting a threshold.

### 2. The refusal must name the window, not the condition
*"The desktop is contended"* sends the next reader nowhere. **Name the process, the title, and that
it owns the foreground** — that is the difference between nine runs of elimination and one line of
diagnosis. The wave-66 land found it with `GetForegroundWindow` plus `GetWindowThreadProcessId`.

### 3. Your own product will trip it, and that is correct
`CcpClient.Desktop` raises real windows during headed captures. **A pre-flight that cannot tell our
own capture from a foreign product is useless** — the lease already serialises our processes.
Distinguish them, and say how.

### 4. This is test infrastructure many tests depend on
`RealDesktopCollection` gates every real-desktop fact in the suite. **A defect here reddens work that
is not yours.** Run the full floor before and after and compare failure sets, not just counts.

### 5. Standing rules
No wall-clock waits — `TestWait` only. No TODOs. Every new guard watched red **at the committed
head**, with the SHA.

### 6. Divergence ids: **D279-D288**

## File Scope

| | |
|---|---|
| May change | `client/tests/CcpClient.Tests/RealDesktopCollection.cs`, `client/tests/CcpClient.Tests/DesktopPreflightTests.cs` (new), `client/docs/verification-harness.md`, `client/docs/wpf-surface-reachability.md` (divergences ONLY, D279-D288), and `spine-tasks/SP-134-desktop-preflight/**` |
| Must not change | everything else, and specifically `client/tests/CcpClient.Tests/{VideoOverlayCoexistenceTests,PointerCapabilityTests,InputCapabilityTests,GoonServingTests,CitationSelfTestGateTests}.cs`, `client/src/**`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/execution-census.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-134-desktop-preflight/floor-delta.json` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/RealDesktopCollection.cs` |
| fileScopeMustNotChange | `client/tests/CcpClient.Tests/VideoOverlayCoexistenceTests.cs`, `client/tests/CcpClient.Tests/PointerCapabilityTests.cs`, `client/tests/CcpClient.Tests/InputCapabilityTests.cs`, `client/tests/CcpClient.Tests/GoonServingTests.cs`, `client/tests/CcpClient.Tests/CitationSelfTestGateTests.cs`, `client/src/**`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/execution-census.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-134-desktop-preflight/record.md`, `spine-tasks/SP-134-desktop-preflight/plan.md`, `spine-tasks/SP-134-desktop-preflight/floor-delta.json` |

**Pin: 2573 unit / 152 headless.** `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint BEFORE any edit:** how you detect a foreign topmost window; how you tell it from
   our own capture; **what one sample can and cannot establish given the cadence**; and which edit
   each new guard must red on.
2. Build the pre-flight. **It fails; it does not skip.**
3. Make the refusal name the process, the title and the ownership.
4. **Run the full floor before and after, and compare FAILURE SETS.** A count match is not enough.
5. Divergences **D279-D288**.

## Completion Criteria

- A contended real desktop fails **once, at the fixture, naming the window**.
- No test is skipped, quarantined, retried, or added to `allowedSkips`.
- No OS-level assertion in any existing real-desktop fact is weakened.
- The cadence limitation is stated precisely, or handled and demonstrated.
- Both gates green; build 0 warnings / 0 errors. **If the desktop is contended while you work, say so
  and report which runs were affected** — that is this packet's own subject.

## Do NOT

- Skip, quarantine or retry a real-desktop test.
- Add anything to `allowedSkips`.
- Weaken an OS-level assertion.
- Ship a single-sample detector without stating what it misses.
- Use a divergence id outside D279-D288.

## Git Commit Convention

Conventional commit, `feat(SP-134): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the detection mechanism, the our-window-versus-foreign distinction, the cadence
limitation, the before/after failure sets, and the red demonstrations with the head SHA.
