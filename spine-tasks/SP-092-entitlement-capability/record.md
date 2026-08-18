# SP-092 — record

Branch `lane/SP-092-entitlement-capability`, base `94fb5d14`.
Worktree `.claude/worktrees/agent-a4f3867dc8fb992b2`. Review level 3.

## 0. Status: INFRASTRUCTURE ONLY (A-014). The DTRH gate is NOT closed.

This packet registers nothing and gates nothing — SP-091 owns the composition root, the views
and the navigation shell this wave, and a lane may not reach into a sibling's scope. What
landed is a capability plus its tests, wired to nothing. **Down The Rabbit Hole is still
ungated in the port, and this packet does not change that.** The gate closes when a later
packet consumes this capability and renders its three outcomes.

## 1. The four mechanism facts, measured

All four hold at the cited lines. Nothing moved; no re-grep was needed.

| Packet claim | What I measured | Verdict |
|---|---|---|
| `Services/Auth/SecureAuthTokenStore.cs:14` — entropy literal `ConditioningControlPanel_AuthToken_v1` | `:14` `private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ConditioningControlPanel_AuthToken_v1");` | exact |
| same file `:17` — `auth_token.dat` under `%LOCALAPPDATA%/ConditioningControlPanel/` | `:15-17` `private static readonly string StoragePath = Path.Combine(App.UserDataPath, "auth_token.dat");` | exact, with one addition below |
| same file `:39,65` — `ProtectedData.Protect`/`Unprotect`, `DataProtectionScope.CurrentUser` | `:39` `ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser)`; `:65` `ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser)` | exact |
| `Services/TierGate.cs:88-94` — the tier is not in the token | `:88-94` is `RequiresLab(string featureName, string dailyKey)`; `:90` `App.Patreon?.HasLabAccess == true`, `:91` `|| App.DailyFree?.IsFreeToday(dailyKey) == true`, `:92-93` the verdict | exact |

**One fact the packet does not mention, found while confirming the path.** `App.UserDataPath`
is not unconditionally `%LOCALAPPDATA%/ConditioningControlPanel`: `App.xaml.cs:157-171` honours
a rooted `CCP_USERDATA_DIR` first. The port's locator (`Entitlement/ShippingAppDataLocation.cs`)
ports that precedence rather than the packet's simplification. Consequence if it had not: a
user who redirected the shipping app would get `Unavailable(host-token-absent)` — still not a
refusal, but the wrong reason, which is the failure mode one notch down from the one this
packet exists to prevent.

**Two supporting facts used for the design**, both read rather than assumed:
`PatreonService.cs:134` (`HasPremiumAccess` is TIER 1 despite the name) and `:172`
(`HasLabAccess` is TIER 2, the DTRH bar) fix the two tiers;
`SecureAuthTokenStore.cs:58-76` shows `Retrieve()` returning a bare `null` for BOTH "no file"
and "DPAPI refused the blob" — the exact conflation this packet is built to refuse to inherit.

## 2. The typed outcome

`client/src/CcpClient.Desktop/Entitlement/EntitlementOutcome.cs`

```
EntitlementTier { Supporter = 1, Lab = 2 }          // no None member: "no tier" is an outcome
abstract EntitlementOutcome
  ├ Entitled(EntitlementTier Tier, string Detail)
  ├ NotEntitled(string Detail)
  └ Unavailable(EntitlementReason Reason)           // EntitlementReason(Code, Detail)
```

Ten reason codes, one per distinct cause, never one bucket: `host-app-data-absent`,
`host-token-absent`, `host-token-empty`, `host-token-undecryptable`, `io-failure`,
`unsupported-platform`, `tier-authority-absent`, `tier-authority-unreachable`,
`tier-authority-rejected`, `tier-authority-fault`. The two shared codes are taken from
`CapabilityReasonCodes` by reference so the vocabularies cannot drift.

Two decisions defend the central fact at the API rather than only in the implementation:

1. **No boolean.** There is no `IsAllowed`/`Allows(tier)`. Such an accessor must map
   `Unavailable` onto true or false, and false rebuilds the conflation at the consumer where
   no test in this packet could see it. Consumption is `Match(entitled, notEntitled,
   unavailable)`, which does not compile with a branch missing.
   `TheOutcomeTypeExposesNoBooleanThatCouldCollapseUnavailableIntoARefusal` enforces this
   reflectively, so a later "convenience" addition reds.
2. **One producer for `NotEntitled`**: a `TierLookup` whose status is `NoEntitlement`, i.e. an
   authority that was asked and answered. Every other path in the namespace is `Unavailable`.

## 3. Which test proves the conflation cannot happen

`EntitlementCapabilityTests.EveryUndeterminedInput_IsUnavailableWithItsOwnReason_AndOnlyAnAuthorityRefusalIsNotEntitled`
is the flagship: ten undetermined inputs in one table, each asserted `IsNotType<NotEntitled>`
**and** `IsType<Unavailable>`, with ten DISTINCT reason codes, followed by the single input
entitled to produce a refusal. The distinctness assertion is deliberate — one shared "something
went wrong" code would be the first step back towards a caller collapsing them into "locked".

Each row also has its own named fact (`NoShippingAppDataDirectory_…`, `NoStoredLoginFile_…`,
`DecryptFailure_…`, `EmptyStoredLogin_…`, `UnreadableStore_…`, `PlatformWithoutDpapi_…`,
`NoAuthorityConfigured_…`, `AuthorityUnreachable_…`, `AuthorityRejectedTheStoredLogin_…`,
`AuthorityThrew_…`), and `AuthorityAnswersNoPledge_IsTheOneAndOnlyRefusal` is the positive
control that keeps the whole set from being satisfiable by a capability that can never say
`NotEntitled` at all.

## 4. Prove it bites (packet step 6)

Scratch edit, `HostLoginEntitlement.cs`, the `HostTokenReadStatus.DecryptFailed` arm changed to
`new EntitlementOutcome.NotEntitled("SCRATCH MUTATION — must never be committed")`. Observed,
run through the slot gate:

```
Failed: 3, Passed: 23, Total: 26
  EntitlementCapabilityTests.DecryptFailure_IsUnavailable_NeverNotEntitled                     [FAIL]
  EntitlementCapabilityTests.EveryUndeterminedInput_…_AndOnlyAnAuthorityRefusalIsNotEntitled   [FAIL]
  EntitlementPrivacyTests.TheRealPlatformCapability_OverBytesItCannotRead_IsNeverARefusal      [FAIL]
  Assert.IsNotType() Failure: Value is the exact type
```

The third red is worth noting: it is the REAL crypt32 path on this machine refusing a
non-blob, so the mutation is caught by an unmocked path as well as by the seam-driven ones.
Restored byte-identically — `sha256(HostLoginEntitlement.cs)` before the mutation and after the
restore are both `ad83052234ea11c667d2dc5d85f6307c7d9860e319c181348081cf8e69317a28`, and a tree
search for `SCRATCH MUTATION` returns nothing. The mutation is not committed.

## 5. The packet's step-7 question, answered

**"The token is valid but the tier lookup cannot reach the server."** That is
`EntitlementOutcome.Unavailable(tier-authority-unreachable)`, and it must be.

The defence: reachability of a server is a fact about the network, not about the account. The
port knows one thing at that moment — a login exists and is readable — and does not know the
thing it was asked. `NotEntitled` there would assert a fact about the user's pledge that
nothing observed supports, and it would present identically to a legitimate refusal: paid
features gone, no explanation, no way to tell a bug from a policy. `Entitled` there would be
the fake-available shape the truthful-capability contract bans and would hand out Tier-2
content on a dropped Wi-Fi connection.

Note this deliberately diverges from WPF, which falls back to a CACHED tier on an HTTP failure
and keeps a 14-day grace window (`PatreonService.cs:557-561`, `:134`, `:172`). That divergence
is recorded rather than hidden: the port has no validated cache to fall back to, inventing one
from another app's `settings.json` would be a tier the port never verified, and the packet
forbids inventing a tier when the server cannot be reached. A later packet that wants
grace-window parity should implement it as an explicit authority that answers `Entitled` from a
cache it owns — not as a silent reinterpretation of "unreachable".

Related, same reasoning: `tier-authority-rejected` (a 401 on the bearer) is also `Unavailable`,
not `NotEntitled`. An authority that refused the credential never reached the entitlement
question. WPF treats the same 401 as recoverable and refreshes before dropping anything
(`PatreonService.cs:541-555`).

## 6. The bridge limits, built in rather than discovered

1. **A port-only user has no token.** This capability presumes the shipping WPF app is
   installed and logged in as the same Windows user. Such a user gets
   `Unavailable(host-app-data-absent)`. **This is a bridge to a real entitlement service, not
   the destination**, and it must not be shipped or described as a finished entitlement system.
2. **Windows only.** DPAPI has no Linux equivalent; the Linux answer is
   `Unavailable(unsupported-platform)`, the same honest shape `ISecretStore` already reports
   there (`Persistence/SecretStores.cs:54-58`). A Linux stub returning `NotEntitled` was the
   banned shape and is not present; `PlatformWithoutDpapi_IsUnavailable_NeverNotEntitled`
   pins it.
3. **It couples the port to another app's on-disk format.** If upstream rotates the entropy to
   `_v2`, every read becomes `Unavailable(host-token-undecryptable)` — loud and typed, never a
   quiet downgrade. `EntitlementPrivacyTests.TheShippingAppsOwnConstantsAreWhatTheReaderUses`
   reads the WPF source at runtime and reds if the literal, the filename or the
   `DataProtectionScope.CurrentUser` line drifts, so the break is knowable rather than merely
   survivable.
4. **No tier authority ships here.** The default is `UnconfiguredTierSource` →
   `Unavailable(tier-authority-absent)`. There is no verified "what tier is this bearer"
   endpoint to port: every WPF site that presents this token does so as an `X-Auth-Token`
   header on a POST that also carries a `unified_id` body (`V2AuthService.cs:558,584,610,627-632`),
   and `/patreon/validate` authenticates with the *Patreon OAuth* bearer from a different store
   (`PatreonService.cs:531-537`). Writing an endpoint I cannot verify would be guessing an API.
   Consequence, stated plainly: **as landed, this capability can never return `Entitled` in
   production** — it returns a typed "login readable, tier unknown". The consuming packet
   supplies the authority.

## 7. Privacy compliance

- The token is read, used, dropped: `HostLoginEntitlement.ResolveAsync` disposes the read
  result in a `using` on every path, and `HostAuthToken.Dispose` zeroes the buffer — the WPF
  `SecurityHelper.SecureClear` discipline (`SecureAuthTokenStore.cs:41,67`).
  `TheOutcomeCarriesNoTokenAndTheHandleIsDroppedAfterResolve` proves the handle is dead after a
  resolve (a later `Reveal()` throws rather than returning an empty value that would read
  downstream as "no login").
- `HostAuthToken.ToString()` is `host-auth-token(redacted)`, and `Reveal()` returns a
  `ReadOnlySpan<char>` — a span cannot be captured by a lambda, stored in a field, or held
  across an `await`, so the value cannot leak by accident through a closure.
- Logging is outcome CLASS plus reason CODE (`EntitlementOutcome.Describe()`), matching the
  port's route-classes-only precedent (`Features/Dtrh/LoopbackServer.cs:505`). Exception
  MESSAGES are never propagated into a reason detail — only `ex.GetType().Name` — because a
  message can carry a URL, a header or a path;
  `AuthorityThrew_IsUnavailable_NeverNotEntitled_AndCarriesNoMessage` and
  `UnreadableStore_IsUnavailable_NeverNotEntitled` assert the message is absent and the type
  name present.
- Nothing is written: `ResolvingWritesNothing_NotIntoTheHostStoreAndNotBesideIt` asserts the
  host store's bytes, timestamp and directory listing are unchanged after a resolve AND a
  probe, and `NothingUnderEntitlementCanWriteACredentialAnywhere` is a lexical guard that the
  `Entitlement/` namespace contains no write path at all (no `File.Write*`, no `FileStream`, no
  `CryptProtectData`, no secret-store call). WPF's own clear-on-decrypt-failure
  (`SecureAuthTokenStore.cs:75`) is deliberately NOT ported: that is someone else's file.
- No test contains a real token (every value is the literal
  `SP092-FIXTURE-NOT-A-REAL-TOKEN-4c1f9e`) and no test reads the developer's real
  `auth_token.dat`. Every store used by a test is a temp directory the test creates and
  deletes; the decrypt step is an injected seam.
- **One tension, named rather than smoothed over.** The board's handling rule says the token is
  "never logged, persisted, copied or transmitted anywhere", while the tier is by definition a
  server answer. As landed the two do not collide, because this packet ships no transmitter at
  all. The packet that adds a real authority inherits the question and should answer it
  explicitly: presenting the bearer to the FIRST-PARTY server that issued it is the only
  transmission that can ever be in scope.

## 8. Gates

Everything below ran through `node client/tools/gate/with-slot.mjs --slots 3 -- …`.

- `dotnet build client/CcpClient.sln -c Debug --nologo` → **0 warnings, 0 errors**.
- `node client/tests/floor/check-floor.mjs` → `CcpClient.Tests` observed **1078**, pin **1052**.
  **1052 + 26 = 1078**, exactly the delta declared in `floor-delta.json`. The reported FLOOR
  VIOLATION is the expected multi-lane shape: this lane never opens or edits
  `client/tests/floor/floor.json`, and the orchestrator sums the packet deltas at land.
  The run itself is green: `Failed: 0, Passed: 1076, Skipped: 2` — the two skips are the
  pre-existing OS-gated names already in `allowedSkips`; this packet adds no skip and no
  `allowedSkips` entry.
- `dotnet test client/tests/CcpClient.HeadlessTests/…` → **35 passed, 0 failed**, unchanged
  (headless delta 0).

**The shared-pin wrapper aborts at the first project violation**, so the floor script did not
reach the headless project on this run; the headless suite was therefore run directly to
confirm it is green and unmoved.

## 9. A scope collision, resolved inside scope

`VacuousShapeGuardTests` flagged three of my facts as silencing SHAPES: two `File.Exists`/
`Directory.Exists` predicates (`fs-predicate`) and one `if (OperatingSystem.IsWindows()) … else
…` platform branch (`platform-predicate`, `assertions-all-nested`). Dispositioning them means
editing `client/tests/floor/vacuous-shape-ledger.json`, which is outside this packet's File
Scope. I did not widen scope and I did not weaken the guard:

- Both fs-predicates were REMOVED rather than dispositioned — the tests now read the file or
  enumerate the directory directly, so a missing input throws loudly instead of being probed
  for. That is strictly better than the predicate they replaced.
- The platform branch was replaced by
  `TheRealPlatformCapability_OverBytesItCannotRead_IsNeverARefusal`, which drives the REAL
  platform capability (real crypt32 on Windows, the unsupported reader elsewhere) over bytes
  that are not a valid host token and asserts, unconditionally and non-vacuously on either OS,
  that the outcome is `Unavailable` and never `NotEntitled`, with the code in the two-element
  set the two platforms can legitimately produce.

**Named consequence.** The POSITIVE Windows leg — a blob protected with the shipping app's
entropy decrypting back through the real path — is inherently Windows-only, and a committed
OS-gated fact needs either an `allowedSkips` name in `floor.json` or a ledger disposition, both
outside this lane. So that leg is **measured manually, not automated**. Measured on this
machine (Windows 11, .NET 10, run through the slot gate, scratch fact deleted immediately
afterwards and never committed):

- fixture value protected with `ConditioningControlPanel_AuthToken_v1`, `CurrentUser` scope,
  written to a temp store → `ForCurrentPlatform(...).ResolveAsync` returned **`Entitled(Lab)`**
  and the authority seam received exactly the fixture value: the real crypt32 binding, the real
  entropy constant and the real file name work end to end.
- the same fixture protected with `..._v2` → **`Unavailable(host-token-undecryptable)`**, not
  `NotEntitled`: the entropy-rotation limit behaves as designed against real DPAPI.

That measurement is evidence, not a landed gate. **A packet that can edit the ledger or the pin
should convert it into a committed OS-gated fact.**

## 10. What this work does NOT prove

- **Nothing about the real user's real login.** No test touched
  `%LOCALAPPDATA%/ConditioningControlPanel/auth_token.dat`; the packet forbids it and I did not.
  That the owner's actual token decrypts through this path is UNVERIFIED here — the manual
  measurement above used a fixture blob this machine protected, which proves the mechanism but
  not that particular file.
- **Nothing about the tier.** No authority exists, so no server answer has ever been observed.
  Every claim about `Entitled` in this packet is a claim about the mapping, driven by a stub.
- **Nothing about UI, gating, focus, rendering, or the DTRH door.** This is compile-and-unit
  evidence only: no interaction, no rendering, no window behaviour, no audio, no animation, and
  no headed capture. A headless frame would not discharge a headed gate here, and no frame of
  any kind was taken.
- **Nothing about Linux at runtime.** The Linux answer is proven by construction and by unit
  test on Windows (`UnsupportedPlatformTokenReader`), not by executing on a Linux box.
- The lexical privacy guard is a TOKEN scan: a write reached through an indirection it does not
  name is invisible to it.
