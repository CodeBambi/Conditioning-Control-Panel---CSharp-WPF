# SP-092 — plan checkpoint (Review Level 3, before the first product edit)

Branch `lane/SP-092-entitlement-capability`, worktree
`.claude/worktrees/agent-a4f3867dc8fb992b2`, base `94fb5d14`.

## 1. The four mechanism facts, measured (not trusted)

Read directly in the read-only WPF tree. All four hold at the cited lines; nothing moved.

| Packet claim | Measured | Verdict |
|---|---|---|
| `Services/Auth/SecureAuthTokenStore.cs:14` entropy literal `ConditioningControlPanel_AuthToken_v1` | `:14` `private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ConditioningControlPanel_AuthToken_v1");` | exact |
| same file `:17` path `auth_token.dat` under `%LOCALAPPDATA%/ConditioningControlPanel/` | `:15-17` `Path.Combine(App.UserDataPath, "auth_token.dat")`; `App.UserDataPath` = `App.xaml.cs:157-171` → `CCP_USERDATA_DIR` if rooted, else `SpecialFolder.LocalApplicationData` + `ConditioningControlPanel` | exact, plus one fact the packet does not mention: the shipping app honours a `CCP_USERDATA_DIR` redirect |
| same file `:39,65` `ProtectedData.Protect`/`Unprotect`, `DataProtectionScope.CurrentUser` | `:39` Protect, `:65` Unprotect, both `DataProtectionScope.CurrentUser`, entropy passed | exact |
| `Services/TierGate.cs:88-94` tier is not in the token | `:88-94` is `RequiresLab(string featureName, string dailyKey)`; `:90` `App.Patreon?.HasLabAccess == true`, `:91` `|| App.DailyFree?.IsFreeToday(dailyKey) == true` | exact |

Supporting reads (not cited by the packet, used for the design): `PatreonService.cs:134`
(`HasPremiumAccess` = tier1) and `:172` (`HasLabAccess` = tier2); `SecureAuthTokenStore.cs:41,67`
`SecurityHelper.SecureClear(plainBytes)` after use (the read-use-drop discipline I mirror);
`SecureAuthTokenStore.cs:71-76` a `CryptographicException` is treated by WPF as "corrupt or from
a different user" — that is exactly the case the port must never render as "not a patron".

## 2. The typed outcome

`client/src/CcpClient.Desktop/Entitlement/EntitlementOutcome.cs`

```
EntitlementTier { Supporter = 1, Lab = 2 }        // PatreonService.cs:134,172
EntitlementReason(string Code, string Detail)
abstract EntitlementOutcome
  ├ Entitled(EntitlementTier Tier, string Detail)
  ├ NotEntitled(string Detail)      // ONLY from an authority that explicitly said "no pledge"
  └ Unavailable(EntitlementReason Reason)
```

Reason codes (`EntitlementReasonCodes`), each a separate cause, never one bucket:

- `host-app-data-absent` — no shipping-app data directory (app never installed)
- `host-token-absent` — directory exists, `auth_token.dat` does not (installed, not logged in)
- `host-token-empty` — decrypt succeeded and produced nothing
- `host-token-undecryptable` — DPAPI refused: corrupt, a different Windows user, or upstream
  rotated the entropy to `_v2`
- `host-read-failed` — I/O or permission failure (`io-failure`, shared vocabulary)
- `unsupported-platform` — no DPAPI on this OS (the Linux answer)
- `tier-authority-absent` — the login is readable, this build has no tier authority wired
- `tier-authority-unreachable` — the authority could not be reached (step 7's answer)
- `tier-authority-rejected` — the authority refused the bearer (expired/rotated login)
- `tier-authority-fault` — the lookup threw; detail is the exception TYPE name only

Two deliberate design decisions that defend the central fact:

1. **No `bool` convenience.** No `IsAllowed`, no `Allows(tier)`. Any such accessor collapses
   `Unavailable` into `false` at the consumer, which reinstates the conflation one layer up.
   Consumers use `Match(entitled, notEntitled, unavailable)`, which cannot compile with a
   branch missing.
2. **`NotEntitled` has exactly one producer**: a `TierLookup` whose status is `NoEntitlement`,
   i.e. an authority that answered. Every other path in the whole file set produces
   `Unavailable`.

## 3. Files (all inside File Scope)

Product — `client/src/CcpClient.Desktop/Entitlement/`:

- `EntitlementOutcome.cs` — the three-state outcome, reason codes, `Match`, `Describe()`
  (outcome class + reason code, never detail, never a value).
- `HostAuthToken.cs` — a redacting, disposable handle. `Reveal()` returns
  `ReadOnlySpan<char>` (a span cannot be captured or stored by accident);
  `ToString()` is `host-auth-token(redacted)`; `Dispose()` zeroes the buffer
  (WPF `SecureAuthTokenStore.cs:41,67` `SecureClear`).
- `HostAuthTokenReader.cs` — `IHostAuthTokenReader` seam + typed `HostTokenRead`
  (Found / NoDataDirectory / NoTokenFile / EmptyToken / DecryptFailed / ReadFailed /
  UnsupportedPlatform); `ShippingAppTokenReader` (the Windows read over an injected
  `IHostBlobDecryptor`); `UnsupportedPlatformTokenReader` (the honest Linux answer).
- `HostDpapi.cs` — crypt32 `CryptUnprotectData` with the WPF entropy blob and
  `CRYPTPROTECT_UI_FORBIDDEN`. Own binding because `Persistence/DpapiNative` passes no
  entropy and that file is outside this packet's scope; `ProtectedData` is not inbox on the
  plain `net10.0` TFM (recorded at `Persistence/SecretStores.cs:327-330`) and the csproj is
  outside scope, so no package.
- `ShippingAppDataLocation.cs` — where the shipping app keeps its data
  (`CCP_USERDATA_DIR` if rooted, else `LocalApplicationData/ConditioningControlPanel`;
  WPF `App.xaml.cs:159-171`). Never the port's own root — the port never writes here.
- `EntitlementTierSource.cs` — `IEntitlementTierSource` seam, typed `TierLookup`
  (Entitled / NoEntitlement / Unreachable / Rejected / NotConfigured / Faulted), and
  `UnconfiguredTierSource`, the honest default for this build.
- `HostLoginEntitlement.cs` — the capability: read → map → look up → map, token disposed in
  a `finally`; `ProbeAsync` returning SP-006 `CapabilityState`
  (`Degraded` when the login is readable but no tier authority is configured).

Tests — `client/tests/CcpClient.Tests/EntitlementCapabilityTests.cs` (+ `EntitlementPrivacyTests.cs`).

## 4. Why no HTTP tier client ships here

The tier is a server answer (`TierGate.cs:88-94`). The port has no verified contract for a
"what tier is this bearer" endpoint: every WPF site that presents the CCP auth token does so as
an `X-Auth-Token` header on a POST that also carries a `unified_id` body
(`V2AuthService.cs:627-632, 558, 584, 610`), and `/patreon/validate` authenticates with the
*Patreon OAuth* bearer from a different store, not with `auth_token.dat`
(`PatreonService.cs:531-537`). Writing an endpoint I cannot verify would be guessing an API, and
`client/memories/port-status.md:140` is explicit that WPF's `HttpClient`/header mechanics are
evidence, not a design. So the authority is a seam; the default answers
`Unavailable(tier-authority-absent)`, never `NotEntitled`. A later packet supplies a real one.

## 5. Tests (facts, all in `CcpClient.Tests`, no Avalonia, no waits, no skips)

One per Unavailable reason, each asserting `Unavailable` **and** `IsNotType<NotEntitled>`:
no data directory, no token file, empty token, decrypt failed, read faulted, no DPAPI on this
platform, tier authority absent, tier unreachable, tier rejected, tier faulted.

Plus:

- the flagship table test: every undetermined input in one table → all `Unavailable`, all with
  DISTINCT reason codes, none `NotEntitled`; and the single authority-refusal input → the only
  `NotEntitled` in the file.
- happy path: authority says tier 1 → `Entitled(Supporter)`; tier 2 → `Entitled(Lab)`.
- privacy: the fixture value appears in no `ToString()`, no `Describe()`, no log line, and no
  exception; the outcome carries no token; nothing is written to the port's own root and the
  host fixture directory is byte-unchanged after a resolve.
- lifecycle: the token handle is disposed after `ResolveAsync` and `Reveal()` then throws.
- platform leg without a skip (branching Fact, the `SecretStoreTests.ForCurrentPlatform`
  precedent): on Windows a real crypt32 round-trip with the WPF entropy constant over a
  temp-dir fixture proves the read works; on non-Windows the platform reader is the unsupported
  one and reports `unsupported-platform`. No new `allowedSkips` name (I cannot edit the pin).

No test reads `%LOCALAPPDATA%/ConditioningControlPanel/auth_token.dat`, and no test contains a
real token: every fixture value is a literal `SP092-FIXTURE-NOT-A-REAL-TOKEN…`.

## 6. Mutation check (packet step 6)

Scratch-edit the decrypt-failure arm to return `NotEntitled`, run the per-reason fact, record
the red, restore, `git diff` must be empty, re-run green. Not committed.

## 7. Floor and wiring

`floor-delta.json` declares the unit delta (headless 0). The shared pin
`client/tests/floor/floor.json` is never opened or edited; the observed total will be
`pin + delta` and both numbers go in the report. Nothing is registered and nothing is gated —
SP-091 owns those files this wave — so this is infrastructure only under A-014 and the DTRH
gate is NOT closed by it.
