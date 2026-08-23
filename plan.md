# Packet plan — the five companion entries that need no owner decision

Branch: this worktree. Scope: `Ai/**`, `Features/Companion/**`, `Views/Pages/CompanionPage.*`,
`client/tools/verify/**`, own tests. Floor pin is NOT mine to edit.

## Census against the code (the audit is older than the tree)

| Row | Audit's port claim | What the code says today | Verdict |
|-----|--------------------|--------------------------|---------|
| A3 dial copy | "two session-scoped consent checkboxes" | still true (`CompanionWindow.axaml` AwarenessConsentToggle/MemoryConsentToggle) | BUILD |
| A4 allow-list | "None — consent Granted => title flows" | still true (`AiAwarenessContextPackaging.TryPackage` carries the scrubbed title unconditionally) | BUILD |
| A6 incognito | "None" | **OVERTAKEN** — `AiPrivacyFilters.LooksIncognito` + `ClassifyCapturedTitle`, wired at the capture seam (`AiAwarenessService.Observe`) and at the packaging seam, with `AiPrivacyFilterTests` | REPORT ONLY |
| C10 forget scopes | "One explicit clear" | still true (`AiMemoryStore.Clear()` only) | BUILD |
| D11 transcript | "chat bubbles show the live session only" | still true | BUILD |

Audit citation drift found (code wins, both recorded in the report):
`en.json:4325-4327` -> really **4435-4437**; `AppSettings.cs:3889-3902` -> really **4212-4226**.

## What lands

1. **A4** `AiTitleAllowList` in `Ai/AiPrivacyFilters.cs` (F4 section). Ships EMPTY.
   Sanitisation verbatim from WPF `AwarenessText.SanitizeRuleEntry` (:174-198) /
   `SanitizeRuleList` (:206-220): <=64 chars, >=2 chars, lowercase, control chars and `*%?`
   dropped, role-marker entries rejected, >=1 letter-or-digit, dedupe, <=200 entries.
   `AllowsTitleFor(app)` matches the **caller-supplied `App` field only** — never Title,
   never Category (WPF `ResolveTitle` :461-464 "matched against the app's IDENTITY only").
   `TryPackage` gains a REQUIRED allow-list parameter (not optional: an omitted argument
   must not silently restore the wide behaviour). Empty list => `Title:` empty, frame
   proceeds title-free (WPF :453-466). Moderation still sees the RAW title first —
   unchanged order, errs closed.
2. **A3** `Features/Companion/CompanionPrivacyDial.cs`: three stops derived from
   (consent, allow-list count) exactly as WPF derives them (`AwarenessPrivacyRuntimeVm.cs:332-336`),
   the three hints verbatim (`en.json:4435-4437`), the head (`:4434`) and labels (`:4432,4433,4450`).
   Selecting BroadStrokes clears the allow list (`:97-105`); selecting +PageTitles enables and
   REVEALS the editor without widening, and the dial keeps reporting BroadStrokes until an app
   is actually named (`:106-113` + `:26-27`).
3. **C10** `AiForgetScope { Thread, Conversation, Everything }` on `AiMemoryStore.Forget(scope)`;
   `Clear()` stays the interface member and delegates to `Conversation`.
   - Thread — turns emptied in RAM and on disk, the document itself KEPT with everything else it
     carries (WPF `CompanionBrain.ForgetThread` :550-558, "No memory fact is touched").
   - Conversation — thread + the document leaves the disk with its non-turn payload
     (WPF `ForgetConversation` :571-585 + `MemoryStore.ForgetChatDerived` :643-652).
     **Named limit: the port has no facts model, so the derived arm is the document's non-turn
     payload only; the facts arm rides owner question Q10.**
   - Everything — conversation + every quarantined `ai_memory.corrupt-*.json` sibling, so no
     surviving copy can resurrect what was forgotten (WPF `MemoryStore.Wipe` :592-631, and its
     stated reason at :587-590 for deleting the legacy transcript). Exact prefix match derived
     from the store's own path; never a glob over the data root.
4. **D11** `Features/Companion/CompanionTranscriptWindow.cs`, built in code for WPF's own
   recorded reason (`CompanionTranscriptWindow.cs:21-23`). Read-only over
   `ReadRecent` (the UNGATED inspection read — `ReadPromptContext` stays the consent-gated one).
   Copy verbatim: heading `:4477`, empty state `:4475`, you/her `:4478`/`:4476`, note `:4590`.
5. UI on `CompanionWindow.axaml` + `CompanionViewModel`: the dial strip, the app-name editor,
   three forget buttons through the existing confirm overlay with scope-specific copy, and the
   transcript button.

## Headed evidence (Windows)

Two new surfaces in `capture.ps1` + `checks.json`, both UIA-gated before any pixel:
- `companion-privacy` x {`broad`,`titles`} — the inversion is that pressing "+ Page titles"
  with an empty list does NOT move the dial; naming an app does. Pixel = the third segment's
  own `:checked` fill vs its unchecked fill.
- `companion-transcript` x {`closed`,`open`} — no transcript window in the tree vs one with the
  exact heading and empty-state copy. Pixel = the transcript's ground over the companion's.

## Not in this packet, and why

App awareness (Q2), activity ledger (Q3), media/mic/input sensing (Q4), memory-retention
default (Q1) — all owner-blocked. If any piece above appears to need one, I stop at that line.
