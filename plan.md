# SP-143 recovered as fresh work — plan

Scope: `client/src/CcpClient.Desktop/Input/` + own test files. Nothing in Effects/, Session/, Views/,
floor.json.

## Defect verified in today's tree (ae1751459)

- `Win32InputPresence.Prompt` (:159-290) has NO second-card refusal. Confirmed.
- Both consumers guard caller-side and on DIFFERENT threads:
  `Effects/LockCardEffect.cs:352` (`Compose`, paced clock thread) and
  `Effects/BubbleCountEffect.cs:661` (`Ask`, surface thread — see its own comment at :630-633).
  BubbleCount's comment at :657-660 names the other direction as unfixed.
- `_prompting` is written at :225 — the END of Prompt — so a caller-side `IsPrompting` test is open
  for the whole width of the call and both threads pass.
- BRIEF IS WRONG ON ONE POINT: the file DOES contain `Interlocked` today, three times
  (:850, :877 `_keystrokesSeen`; :913 `_callbackFaults`). Report it.

## Build

1. `InputReasonCodes.InputAlreadyPrompting = "input-already-prompting"`.
2. `private int _promptClaim` + `Interlocked.CompareExchange(ref _promptClaim, 1, 0)` at the top of
   `Prompt`, body split into `PromptClaimed`. Interlocked, not `_gate`: `_gate` is taken on NATIVE
   callback frames (`Raise` :895, `PaintInto` :672), so widening it to cover the prompt body
   deadlocks against the window's own dispatch; a CAS cannot be widened that way.
3. Release rule = the RETURN VALUE, not `_prompting`: keep the claim only for `Available`/`Degraded`
   (the two states that mean "a card is on screen"), release on every `Unavailable` and on an
   exception. Shorter proof than the old branch's `if (!_prompting)`, which needed an argument about
   the number of writers of a private field.
4. Release beside `_prompting = false` in `Dismiss` (:392, before the observation checks) and
   `Dispose` (:502, before the non-Windows return).

## Facts (CcpClient.Tests, RealDesktopCollection)

- second prompt over a live card → typed refusal, first card's rectangle/callback/LastPrompt intact,
  third prompt still refused (loser did not release the winner's claim).
- release ledger: a prompt that refused gives the claim back; a dismissed card gives it back.
- N concurrent prompts off a `Barrier`: exactly one admitted, the rest `input-already-prompting`.
  Deterministic invariant (the winner keeps the claim until Dismiss); overlap probability is what
  gives it discriminating power, so mutation-measure it.

## Old branch (355511f6d, worktree-agent-a317780fb9c84ce4f) — read, not merged

Same mechanism (CAS). Its release rule `if (!_prompting)` is correct today but proves itself by
counting the four writers of `_prompting`; mine proves itself from the returned state. Its test legs
are good and I am reusing the shapes (impossible ORIGIN for a late refusal, barrier race, hit test
at both rectangles).
