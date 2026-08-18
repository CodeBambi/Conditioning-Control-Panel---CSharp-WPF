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
| `client/docs/wpf-surface-reachability.md` | §8.5 corrected (amendment 3) and a new §10 with eleven divergence rows (D14-D24) |
| tests | `DtrhGateTests` (+10 results), `PlayPageHeadlessTests` (+9), and three existing tests updated for the fourth door and the new capability |

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

## 5a. THE REFUSAL WAS UNREADABLE, AND A HEADED CAPTURE FOUND IT

**This packet shipped a presentation defect, it was caught by a headed capture I could not
take, and it is fixed.** Saying so plainly because it was on my own undischarged list in §8 —
"that a user can read the refusal through a 66%-alpha scrim is a rendering claim" — and it
turned out to be a real defect rather than a theoretical one. Every test I had written passed
while the surface was broken.

**What was wrong.** The band is a translucent scrim (`#A8120A1E`, ~66%) laid over the card, and
the three-sentence refusal was placed **directly on it**. Composited, the card's own title and
blurb showed *through* the message: `DOWN THE RABBIT HOLE` ran across the middle of the second
line, the blurb ran between the message lines, and the wrapped lines had no leading so they
crowded each other. The words were correct and unreadable — which, for a surface whose entire
job is to say something honest and specific, is a failure of the same order as saying the wrong
thing. Evidence: `client/tools/verify/artifacts/port-dtrh-refusal.png`.

**Why I reasoned wrong.** I read `IsHitTestVisible="False"` as the band's contract and
reproduced it exactly, and I read the ~66% alpha as the band's look and reproduced that exactly
too — but I never asked what WPF puts ON the scrim. It puts a glyph and **one short no-wrap
line**, and the file says why in a comment I had already read: *"The pitch is the toast the
click raises, which carries the 'See tiers' button - not this"*
(`Views/Tabs/PlayTabView.xaml:270-273`). WPF's prose lives on a **toast**, an opaque surface
with its own ground. I ported the scrim and silently dropped the second layer, because the port
has no toast — and then put the prose on the layer that was never meant to hold it.

**The fix, and the two fixes I did not take.** The message gets a **plate**: its own opaque
panel (`#FF1B1424`, 1px `#FFB47BFF` rim, corner 10, inset 16, max 600 DIP) inside the band, plus
`LineHeight="18"` on the message and left-aligned prose. The scrim is untouched at WPF's alpha
and still shows the badge, the title's edge, the blurb and both buttons around the plate. I did
**not** make the scrim opaque — that buys legibility by destroying the quality the alpha exists
for (`:247-248`, "so the card art still reads through it. Seeing what you are missing is the
entire job") — and I did **not** shorten the message.

**How it is now guarded.** `TheRefusalMessage_SitsOnItsOwnOpaquePlate_AndTheScrimKeepsWpfsAlpha`
pins **both** alphas — scrim `0xA8`, plate `0xFF` — that the message is a visual descendant of
the plate, that the plate is narrower than the band, that the band still refuses hit-testing,
and that the wording is unchanged. Either half alone can be "fixed" in a way that loses the
other, so both are pinned. That is a structural guard, not a legibility proof: no headless
assertion can read a screen.

**Re-checked headed.** I drove the same route with a throwaway UIA script in my scratchpad
(never added to `client/tools/**`): cold start, no arguments, real click on `DoorPlay`, real
click on `FallInButton`, window captured. The message reads cleanly and the card reads around
it. **This does not discharge a headed gate** — it is my own eyeball check of a fix, and the
capture that matters is the orchestrator's.

## 6. Divergences recorded

Eleven rows, `client/docs/wpf-surface-reachability.md` §10: **D14** four doors, **D15** D12 closed,
**D16** the band appears at refusal rather than before the click, **D17** no toast and no
App Info & Data, **D18** the two checkboxes absent rather than disabled, **D19** no NEW pill,
**D20** the tray decision, **D21** `Unavailable` as a deliberate improvement on WPF (which fails
closed and renders "could not tell" as "no"), **D22** `--dtrh-demo` past the gate, **D23** the
message plate under WPF's still-translucent scrim (§5a), **D24** the unported drop-day grant
(§6a).

§8.5 is corrected in place per amendment 3, with a short note on why the error happened.

## 6a. WPF GRANTS ON TWO CONDITIONS AND THIS PORT IMPLEMENTS ONE

Caught by the final review. `TierGate.RequiresLab` is
`App.Patreon?.HasLabAccess == true || App.DailyFree?.IsFreeToday(dailyKey) == true`
(`Services/TierGate.cs:90-91`), and **both** DTRH call sites pass the KEYED overload
(`MainWindow/MainWindow.Lab.cs:228,313`). The comment two lines above the `:228` I cited
throughout says what the key is for: *"Keyed: on a server-declared DtRH drop day
(DailyFreeService, off-pool override) the door opens for everyone"* (`:225-227`).

**User-visible: on a drop day, a free user who would fall in on WPF is refused by the port** —
and refused with the tier message, which on that day is not just a gap but wrong, because the
feature really is free that day.

**The port implements the tier term only, and that is the honest answer, not a shortcut.** There
is no `DailyFreeService`, no `/config/daily-feature` fetch and no server. DTRH reaches that list
**only** through a server override; the local rotation never lands on tier-2 content
(`Services/DailyFreeService.cs:16-18,133-144`). A locally-decided "free today" would hand out
tier-2 content on a date this port picked for itself — worse than a recorded gap. Recorded as
§10 D24 with its close condition: when the port has a server-supplied daily-free key, the gate
ORs it in where WPF does, with the same `"dtrh"` key.

**How it stayed invisible, which matters more than the miss.** `DtrhGate.cs` cited
`TierGate.cs:88-94` — the range that *contains* the second term — while describing only
`HasLabAccess`, and `DtrhLaunch.cs` quoted the keyed call while describing unkeyed semantics.
`wpf-surface-reachability.md:105-107`, in the §3 my own Step 1 told me to read first, already
said *"Allowed with Lab access **or** when the server names `dtrh` as today's free feature"* —
and I carried the second half of that sentence into D21 while dropping the first. This is the
same failure as the §8.5 title correction, one layer down: **a partial reading written into a
load-bearing comment, where it then reads as the whole truth to everyone after.** Both comments
now name both terms and point at D24.

## 7. Floor

Pin read from `client/tests/floor/floor.json`: **1090 unit / 39 headless**.
Declared in `floor-delta.json`: **+10 unit / +9 headless** (the ninth headless fact is the
layering guard from §5a).
Observed by `node client/tests/floor/check-floor.mjs`: **1100 / 48** — exactly `pin + delta` in
both projects, which is the designed state for a bound lane. The reported FLOOR VIOLATION is
that arithmetic and nothing else; `floor.json` was never opened. Skips were exactly the two
pinned Linux-gated names, and the SP-057 data-root pin executed (it did not skip), so the floor
was not blind.

`pwsh client/tools/verify/self-test.ps1` — **SELF-TEST PASS**, run on the committed tree, with
the layout probe reporting all four doors and `rail-door-selected-border` at 888/918 pixels
(fraction 0.967). Adding the Play door did not disturb the harness SP-091 re-anchored, and the
harness was run, never edited. It is a Completion Criterion and this is the assertion of it.

## 8. What this does NOT prove

Nothing here is `presentation-verified`. Headless frames do not verify composited pixels, window
activation, z-order, focus, animation or audio. Specifically undischarged:

- **The drop-day grant is ABSENT, not merely unproven.** §6a / §10 D24. Every other item on this
  list is something built but unmeasured; this one is a capability the port does not have, and
  on a WPF drop day a free user is refused here. Named separately so it is never read as a
  testing gap.
- **The minimize/restore has NO TEST AT ALL.** Nothing in either project touches `WindowState`:
  `DuckOwner`/`RestoreOwner` are exercised by no test, headless or otherwise, and the entitled
  paths in the suite stop before a host window ever opens, which is precisely what would raise
  `HostOpened`. Calling it "the landed intake precedent, not new code" understated it — the
  shape is precedent, but the prior-state branching (Maximized comes back Maximized, else Normal
  + Activate) is **new code that nothing runs**. The safety property survives structurally
  rather than by test: the port minimizes instead of `Hide()`, so the taskbar button is the way
  back even if the restore logic were wrong. Whether the shell really leaves the screen and
  returns, and how an owned `DtrhHostWindow` behaves while its owner is minimized, are headed
  claims this packet captured nothing for.
- **The `catch` fallback on the user path is exercised by nothing.** `DtrhLaunch.cs`'s
  `catch (Exception ex)` around `ResolveAsync` maps a throw to
  `Unavailable(tier-authority-fault)` so a fault can never become a dead control or a refusal of
  the account. It sits on the product path and no test drives a throwing reader or authority
  through it. The reasoning is sound and unverified; a throwing-seam test would close it cheaply.
- **The DTRH host window itself.** No test here opens one: the entitled FALL IN stops at the
  picker and the entitled Quick Drop uses the descent seam, because `QuickStartAsync` builds a
  real WebView2 host a headless frame cannot present.
- **The band's appearance.** That a user can read the refusal is a rendering claim; the tests
  assert the tree, the visibility, the hit-test flag, both layers' alphas and the text. **This
  item was on the list at the first submission and turned out to be a REAL defect, not a
  theoretical one — see §5a.** It is fixed, re-checked headed by hand, and still not discharged
  by anything in this suite: the guard is structural, and only a headed capture reads a screen.
- **The tray.** Untouched. Both its Windows and Linux halves remain undischarged.
- **A real entitled user.** No machine in this lane has an entitlement authority, so `Proceed`
  is proved through the capability's own authority seam, never end-to-end against a live
  service. The port cannot resolve anyone's tier until an owner permission decision lands.

## 9. Notes for the next packet

- **`self-test.ps1` used to destroy uncommitted work** — its phase-2 restore was
  `git checkout -- src/CcpClient.Desktop/Views/MainWindow.axaml`, which discarded any
  uncommitted edit to that file, including this packet's own rail markup. Reported at the first
  submission and **fixed on `feat/crossplatform` at `09c93d6b`**, which this lane is rebased
  onto: the harness now restores from bytes captured before its mutation and proves the restore
  byte-for-byte. No lane needs to work around it again.
- **A headless suite cannot see a composition defect, and this packet is the proof.** Every test
  passed while the refusal was unreadable (§5a). When a packet's outcome is "the user can read
  X", a headed capture is not optional polish — it is the only instrument that measures the
  claim.
- The entitlement capability now has a registered probe but **no System-page-specific wording**;
  it renders through the generic capability list. A future packet may want a friendlier row.
- `DtrhLaunch.Descend` is a seam with exactly one substituting test. If a second appears, check
  that the real default is still exercised somewhere.
