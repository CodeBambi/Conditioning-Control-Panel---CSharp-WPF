# Resume plan — OS-reclamation teardown row (checkpoint file, removed before report)

Prior lane branch `worktree-agent-ab9683e4d621012ed` (2 commits on `e18b4019e`) restored into this
worktree, minus its own `plan.md`. Nothing inherited on trust.

## Its decision, restated from ITS plan (not from the resume packet)
The resume packet says "the prior lane's file list suggests it took (a)". Its plan.md says (b),
explicitly. Source settles it: (a) requires `App.axaml.cs`'s Exit handler to stop blocking the UI
thread, which is outside this row's file scope, and disposing surfaces at the synchronous head of
shutdown fights the pre-drain flush's documented RE-ARM (`SessionParticipant.cs` FlushAsync remark).
So: (b), with its precondition (the process can actually exit) made true rather than assumed.

## Re-derivation checklist
1. Re-read App.axaml.cs:91-97, SessionParticipant.StopAsync/DisposeSurface, ApplicationHost.
2. Judge the ApplicationHost bound: semantics preserved on the ordinary path, worst-case duration,
   log-sink thread safety of the abandoned-stop continuation.
3. Verify every helper the observations file uses really exists with that signature.
4. Build 0/0, run the unit suite with -trx.
5. FULL mutation set on the final form (the prior lane died inside M3).
6. Remove plan.md.
