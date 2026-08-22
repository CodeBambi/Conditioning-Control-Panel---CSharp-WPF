# SP-143 — `Prompt` must refuse a second card while one is live, and the refusal must be ATOMIC or it is theatre

## Mission

A Lock Card firing can `Prompt` over a live Bubble Count question and **strand that game until disarm**.
`Win32InputPresence.Prompt` guards `_disposed` and nothing else, then overwrites `_content` and
`_onKeystroke`, so the first card's keystroke callback is simply gone and its question can never be
answered.

**Your outcome: a typed `input-already-prompting` refusal, symmetric to `video-already-playing`, that
actually closes the race rather than narrowing it.**

## The premise, RE-VERIFIED AGAINST SOURCE by the orchestrator at `ee6398b1e`. Reproduce it

- `Input/Win32InputPresence.cs:160` — `Prompt` checks `_disposed`, then nothing else.
- `:198-202` — overwrites `_bounds`, `_content`, `_onKeystroke` under `lock (_gate)` with **no test for a
  live prompt**.
- `Input/InputReasonCodes.cs` — exactly **8** `public const string` codes, and **none** is an
  already-prompting equivalent. (The string `input-unavailable` in that file is prose in a doc comment at
  `:9`, not a code. Do not miscount it as a ninth; the orchestrator did once.)
- `Video/VideoReasonCodes.cs:83` — `VideoAlreadyPlaying = "video-already-playing"`. **The port already
  knows this shape and guards it for video and not for input.**

## THE TRAP, and it is the whole difficulty of this packet

**The obvious fix does not work.** Adding `if (_prompting) return Unavailable(...)` at the top of `Prompt`
looks like the fix, passes a single-threaded test, and **leaves the reported defect open**, because:

- `_prompting` (`:76`) is a **plain non-volatile `bool`**. `IsPrompting` (`:92`) reads it with **no lock and
  no barrier**, and the board row's defect is precisely a caller passing `IsPrompting` on one thread while
  another thread is inside `Prompt`. An unsynchronised re-read inside `Prompt` reproduces the same race one
  frame lower.
- **`_prompting` is not set true until `:225`**, and only then from `observation.Confirmed` — i.e. after the
  window is up and verified. **Every instant between entering `Prompt` and `:225` is a window in which
  `_prompting` is still `false`**, so two threads can both pass the new check and both proceed to the
  overwrite.

**So the claim must be STAKED ON ENTRY and atomically**, not read-then-hoped. `Interlocked.CompareExchange`
on an `int`, or a claim taken under `_gate` that covers both the test and the set, are both defensible.
**Whatever you choose, the test-and-claim must be one indivisible step.**

**And staking early creates the obligation that pays for it: every failure path between the claim and
`:225` must RELEASE it**, or the first failed prompt disables prompting for the life of the process — a
worse outcome than the defect you are fixing, and one a happy-path test will never see. Enumerate those
paths from the source and show that each releases. `:308` already clears `_prompting` on one such path;
find the rest rather than assuming that is the only one.

**Do not change what `IsPrompting` means to existing callers**, and do not silently make `:225`'s
confirmation semantics weaker — `_prompting` must still end up reflecting a card that is genuinely up.

## What is NOT in scope

- **Do not patch callers.** SP-112 explicitly refused to patch a landed module per-caller and it was right;
  the guard belongs inside `Prompt`.
- Do not touch `Update` or `Dismiss` semantics beyond what releasing the claim requires.
- **Do not fix the `IsPrompting` data race for other readers.** If you conclude the public `IsPrompting`
  needs a barrier for callers outside this class, that is a FINDING to report, not this packet's work.

## Standing rules

No TODOs. No wall-clock waits — `TestWait` only (`client/tests/CcpClient.Tests/TestWait.cs`). Conventional
commit. Divergence ids **D340 onward** (SP-142 holds D329-D339 in a parallel lane), exactly five unescaped
pipes per row; escape `|` inside code spans as `\|` and **verify by counting delimiters, not by reading**.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Input/Win32InputPresence.cs`, `client/src/CcpClient.Desktop/Input/InputReasonCodes.cs`, `client/tests/CcpClient.Tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY, D340+), `spine-tasks/SP-143-input-already-prompting/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/Lifecycle/**` and `Session/**` (a PARALLEL LANE OWNS THOSE THIS WAVE), `client/tests/floor/floor.json`, `client/docs/task-board.md`, `docs/constitution.md`, `ConditioningControlPanel/**` (byte-identical to `main` as of SP-141 and it MUST STAY SO), `.claude/**`, `client/tools/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-143-input-already-prompting/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Input/Win32InputPresence.cs` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `docs/constitution.md`, `ConditioningControlPanel/**`, `client/src/CcpClient.Desktop/Lifecycle/**` |
| artifactsMustExist | `spine-tasks/SP-143-input-already-prompting/record.md`, `plan.md`, `floor-delta.json` |

**Base pin: 2622 unit / 152 headless.** Declare the positive delta you observe. Do not edit `floor.json`.

**Standing environmental family on this machine**: `PointerCoexistenceTests` (3) and
`BubbleCountCapabilityTests.THEOVERLAY...`, oscillating 3-4 reds, present on the base with none of your
code. **Compare failure SETS, never counts.** Never close the owner's running application to get a green.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint BEFORE any edit.** State: your atomic claim mechanism and why the alternative loses;
   the COMPLETE enumerated list of failure paths between the claim and `:225`, each with the line that
   releases it; whether `:225`'s meaning changes; and how a test can distinguish a real atomic claim from
   the unsynchronised version that only narrows the race.
2. Add the code to `InputReasonCodes.cs`, worded like its `video-already-playing` sibling.
3. Implement the atomic claim and its releases.
4. Add facts. **At least one must fail against the NAIVE unsynchronised version of the fix**, not merely
   against no fix at all — a test that only distinguishes "guard" from "no guard" does not defend the thing
   this packet is about. Prove it by building the naive version locally and recording what it does.
5. Verify: floor at pin + declared delta, 0W/0E, failure SET unchanged.
6. Divergences **D340 onward**.

## Completion Criteria

- A second `Prompt` while one is live returns a typed `input-already-prompting` refusal and **does not
  overwrite `_content` or `_onKeystroke`**.
- The test-and-claim is atomic, and the record argues why.
- Every failure path between the claim and `:225` releases it, enumerated from source.
- A fact distinguishes the atomic fix from the naive one, with the naive version's behaviour recorded.
- Floor at pin + declared delta; `client/` builds 0W/0E.

## Do NOT

- Patch callers instead of the module.
- Ship the unsynchronised `if (_prompting) return` and call the race closed.
- Leave a failure path that strands prompting for the process lifetime.
- Touch `Lifecycle/**` or `Session/**` — a parallel lane owns them this wave.
- Touch `ConditioningControlPanel/**`.

## Git Commit Convention

Conventional commit, `fix(SP-143): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` carrying: the re-verified premise, the claim mechanism with the rejected alternative, the full
enumerated release-path table, the naive-version comparison and what it proved, the before/after failure
sets, and what this does NOT prove (in particular: whether any real product path drives two prompts
concurrently, which this packet does not establish).
