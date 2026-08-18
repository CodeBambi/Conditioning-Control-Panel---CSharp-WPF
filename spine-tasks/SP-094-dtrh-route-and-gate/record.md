# SP-094 — record

Branch `lane/SP-094-dtrh-route-and-gate`, worktree
`.claude/worktrees/agent-afeee60116cc3e607`, base `e9d25a21` (the amended packet).
Plan checkpoint: `plan.md`, approved with five amendments, all five taken.

**Outcome delivered.** From a cold start with no command-line arguments a user reaches the rail
door `Play`, the DTRH hero card, and `FALL IN` / `Quick Drop` — and the gate in front of them
answers with one of three visibly different results, of which the one every real user hits
today is *"the port could not verify your entitlement"*, never *"you are not a patron"*.

---

## 1. What changed, and why

| File | Why |
|---|---|
| `Navigation/ShellRoutes.cs` | `Play = "play"` declared between Companion and System (WPF rail order, §8.1). The doc comment's "there is deliberately NO DTRH door … until SP-092" is replaced by the reason the rule still binds: DTRH is two hops in WPF, so the rail names the PAGE |
| `Views/Pages/PlayPage.axaml{,.cs}` | The hero card. Badge `PRIME SUBJECT`, title `DOWN THE RABBIT HOLE`, WPF's verbatim blurb, `FALL IN`, `Quick Drop`, and a refusal band in WPF's own colours. The code-behind RENDERS a decision it does not make and switches on the decision TYPE |
| `Views/MainWindow.axaml{,.cs}` | `DoorPlay` in the rail markup, the page mounted, and the hero/badge/band styles. `ShellRouteBinding.ValidateOrThrow` runs unchanged in both directions |
| `Features/Dtrh/DtrhGate.cs` (new) | The Tier-2 door as a **pure function** of `EntitlementOutcome`, consumed through `Match` (which does not compile with a branch missing). Three decisions, and the two refusals are **different types** |
| `Features/Dtrh/DtrhLaunch.cs` (new) | The gate + **the single construction site** for `DtrhLaunchCoordinator` (the `LoomLaunch` pattern), plus the shell minimize/restore |
| `Entitlement/HostLoginEntitlement.cs` | Amendment 2: `Enum.IsDefined` in the `Entitled` arm |
| `Lifecycle/CompositionRoot.cs`, `Lifecycle/ApplicationHost.cs` | Amendment 4: the entitlement capability is composed once and **registered with a probe**, so the object the gate consults is the object the System page reports |
| `App.axaml.cs` | `--dtrh-demo` uses the shell's one coordinator, with the reason for stepping past the gate written at the call site (amendment 5) |
| `client/docs/wpf-surface-reachability.md` | §8.5 corrected (amendment 3) and a new §10 with nine divergence rows |
| tests | `DtrhGateTests` (+10 results), `PlayPageHeadlessTests` (+8), and three existing tests updated for the fourth door and the new capability |

## 2. The three branches, and what proves each

The gate is the FIRST statement of both entries, exactly where WPF puts it
(`MainWindow/MainWindow.Lab.cs:228` FALL IN, `:313` Quick Start), and it resolves **per press**
rather than caching a verdict nobody re-checked.

| Branch | Produced by | Observable outcome | Proved by |
|---|---|---|---|
| `Proceed` | an authority confirming a pledge at or above tier 2 (`Services/TierGate.cs:88-94`) | the **real** `DtrhSlotPickerWindow` opens; no band | `ColdStart_NoArguments_PlayDoorThenFallIn_OpensTheRealSlotPicker_WhenEntitled` — real headless clicks, the **unsubstituted** descent seam, and it asserts the real picker's three cards. Cancelling backs out with no host window, as WPF does |
| `RefusedNotEntitled` | `TierLookupStatus.NoEntitlement`, or a confirmed pledge below the bar | band captioned `LAB ONLY` carrying WPF's verbatim `en.json:4704` sentence; nothing opens | `NotEntitled_FallIn_RefusesWithWpfsTierMessage_AndOpensNothing`, plus two unit facts |
| `RefusedUnverified` | **everything else**, including this build's shipped `UnconfiguredTierSource` | band captioned `COULD NOT VERIFY` carrying a per-reason-code explanation and "Nothing was decided about your account"; nothing opens | `Unavailable_TierAuthorityAbsent_FallIn_RefusesWithADifferentHonestMessage_AndOpensNothing` and `Unavailable_TierAuthorityAbsent_IsTheBranchEveryUserHitsToday_AndIsNeverTheNotEntitledRefusal` |

**The `Unavailable` headless test injects no authority double at all.** It boots the real
composition root with the real shipped authority, so the branch it exercises is literally the
product's configuration today. That is the strongest available statement that this is not an
edge case.

## 3. The wording guard, and the proof that it bites

`DtrhGateTests` runs a named predicate — "does this message contain wording that only belongs to
a real refusal" — over every `Unavailable` message, for all ten `EntitlementReasonCodes` values,
and asserts each code has its own distinct authored wording with none of it present.

The amendment asked to see that guard bite. It does, twice over:

1. **In-suite, permanently.** `TheRefusalWordingGuard_Bites_OnASeededCouldNotVerifyMessage`
   feeds the same predicate four seeded messages — one per banned phrase, including
   `CouldNotVerifyHeader + "upgrade your pledge to unlock it."` — and asserts it catches every
   one, then asserts the real shipped message passes. Seeded in the test rather than in the
   product, so the bite is re-proved on every run instead of once in a transcript.
2. **By source mutation (step 6), run and reverted.** `DtrhGate.Decide`'s `Unavailable` arm was
   collapsed into `new DtrhGateDecision.RefusedNotEntitled(TierRefusalMessage)`. Result:

   ```
   CcpClient.Tests.DtrhGateTests.Unavailable_TierAuthorityAbsent_IsTheBranchEveryUserHitsToday_AndIsNeverTheNotEntitledRefusal [FAIL]
   CcpClient.Tests.DtrhGateTests.EveryUnavailableReasonCode_HasItsOwnAuthoredWording_AndNoneCarriesTheRefusalWording [FAIL]
   CcpClient.HeadlessTests.PlayPageHeadlessTests.Unavailable_TierAuthorityAbsent_FallIn_RefusesWithADifferentHonestMessage_AndOpensNothing [FAIL]
   CcpClient.HeadlessTests.PlayPageHeadlessTests.QuickDrop_IsGatedToo_AndRefusesWithoutOpeningAnything [FAIL]
   ```

   Restored with `git checkout --`; `sha256sum` before and after is
   `0f6c59beb8d7001a6ee042dacfcd69390a432295e88c25fd5bfb8f678c33866d`, and
   `git diff --stat` against a pre-mutation stash of the whole tree was empty. The mutation is
   not committed.

## 4. The card takes the click

`TheGatedCard_TakesTheClick_NothingIsDisabled_AndTheBandNeverSwallowsTheNextPress` asserts, on
real headless input: both buttons `IsEnabled` before the press and still `IsEnabled` after the
refusal; the band `IsHitTestVisible == false`; and a **second** press through the band raises
`GateArrivals` to 2 and is refused again. That is WPF's shape verbatim
(`Views/Tabs/PlayTabView.xaml:503-512`, style `:251-258` — "a band that swallowed the click
would be a dead end"). Nothing on the port's card is disabled in any branch; WPF's one genuinely
disabled part is the checkbox pair, which the port does not have (§10 D18).

## 5. The tray: option (b), and what it costs

Not tucked. The shell is plain-minimized when the DTRH host window opens and restored to its
prior state when the flow ends, reusing `Features/Intake/IntakeHostWindow.axaml.cs:120-162`. The
full reasoning and the user-visible difference are in §10 D20 of the reachability doc.
`ITrayPresence` is **not wired by this packet**, and the board's tray row is untouched.

A detail the amendment asked not to be lost: WPF's DTRH tuck comment says "No notification"
(`MainWindow/MainWindow.RemoteControl.cs:1515`) and the code it calls fires a balloon on its
first-ever invocation (`Services/Notifications/TrayIconService.cs:152-157`). The port reproduces
**neither**, because it does not tuck; D20 records both facts so a future tuck packet builds
against the code rather than the comment.

## 6. Divergences recorded

Nine rows, `client/docs/wpf-surface-reachability.md` §10: **D14** four doors, **D15** D12 closed,
**D16** the band appears at refusal rather than before the click, **D17** no toast and no
App Info & Data, **D18** the two checkboxes absent rather than disabled, **D19** no NEW pill,
**D20** the tray decision, **D21** `Unavailable` as a deliberate improvement on WPF (which fails
closed and renders "could not tell" as "no"), **D22** `--dtrh-demo` past the gate.

§8.5 is corrected in place per amendment 3, with a short note on why the error happened.

## 7. Floor

Pin read from `client/tests/floor/floor.json`: **1090 unit / 39 headless**.
Declared in `floor-delta.json`: **+10 unit / +8 headless**.
Observed by `node client/tests/floor/check-floor.mjs`: **1100 / 47** — exactly `pin + delta` in
both projects, which is the designed state for a bound lane. The reported FLOOR VIOLATION is
that arithmetic and nothing else; `floor.json` was never opened. Skips were exactly the two
pinned Linux-gated names, and the SP-057 data-root pin executed (it did not skip), so the floor
was not blind.

## 8. What this does NOT prove

Nothing here is `presentation-verified`. Headless frames do not verify composited pixels, window
activation, z-order, focus, animation or audio. Specifically undischarged:

- **The minimize/restore.** That the shell really leaves the screen and comes back on a real
  desktop, and that an owned `DtrhHostWindow` behaves correctly while its owner is minimized, is
  a headed claim. The shape is the landed intake precedent, not new code, but this packet
  captured nothing.
- **The DTRH host window itself.** No test here opens one: the entitled FALL IN stops at the
  picker and the entitled Quick Drop uses the descent seam, because `QuickStartAsync` builds a
  real WebView2 host a headless frame cannot present.
- **The band's appearance.** That a user can read the refusal through a 66%-alpha scrim is a
  rendering claim; the tests assert the tree, the visibility, the hit-test flag and the text.
- **The tray.** Untouched. Both its Windows and Linux halves remain undischarged.
- **A real entitled user.** No machine in this lane has an entitlement authority, so `Proceed`
  is proved through the capability's own authority seam, never end-to-end against a live
  service. The port cannot resolve anyone's tier until an owner permission decision lands.

## 9. Notes for the next packet

- **Run `client/tools/verify/self-test.ps1` only on a committed tree.** Its phase-2 restore is
  `git checkout -- src/CcpClient.Desktop/Views/MainWindow.axaml`, which discards *any*
  uncommitted edit to that file — including the packet's own. It cost this lane one
  reconstruction of the rail markup. The harness is correct; the hazard is undocumented.
- The entitlement capability now has a registered probe but **no System-page-specific wording**;
  it renders through the generic capability list. A future packet may want a friendlier row.
- `DtrhLaunch.Descend` is a seam with exactly one substituting test. If a second appears, check
  that the real default is still exercised somewhere.
