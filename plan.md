# Slice 7 — the consent-gated remote-media broker: findings and decision

Scaffolding file. Removed before this lane reports complete; the content folds into the report.

## What upstream's broker actually is (read, not guessed)

- `Services/Arcademy/ArcademyHostService.cs:1473-1616` — `assets-request` handler. Bright line at
  `:1474-1476`: "this machine talks to the provider directly and the page fetches the media
  itself." Closed-gate branch at `:1501-1505`. Async batch at `:1536-1602`.
- The gate: `:1626-1632` — `MediaSource != "local" && HasRemoteMediaConsent`, plus
  `App.Settings.Current.OfflineMode`.
- The provider: `FypOnlineCoordinator.For("arcademy", …)` → `ScrolllerSource.cs:48`
  **`POST https://api.scrolller.com/admin`**, an unofficial GraphQL API of a Reddit media
  aggregator, with a spoofed browser User-Agent, `Origin` and `Referer`
  (`ScrolllerSource.cs:73-77`), and CDN hotlinks played directly by the page.
- The taxonomy: `FypOnlineCoordinator.cs:44-62` — 12 named adult-content niches; the user's
  selection and dwell EWMA shape which requests go to that third party
  (`FypOnlineCoordinator.cs:19-22`, persisted per-consumer).
- The consent model: `Models/AppSettings.cs:3354` —
  `HasRemoteMediaConsent => _remoteMediaConsented || _fypOnlineConsented`, a UNION of two
  one-time coaching cards, one of which lives inside the For You Feed.

## Decision: DEFER the network, BUILD the refusal

Three independent reasons, each traceable to a landed document rather than to convenience:

1. **The third-party dependency is an open owner decision, not this row's.**
   `client/docs/fyp-census.md:239-256` records it as owner-flagged and unanswered: "the decision
   needed is 'does the port take a dependency on scrolller.com', and no amount of engineering
   answers it." The gaze row repeats it as "a separate boundary decision." Nothing in
   `client/docs/owner-decisions.md` answers it.
2. **The consent model cannot be reproduced with what this port has.** The union needs two
   one-time cards and an app-wide media-source picker. The port has none of them, and `Views/**`
   belongs to another lane. A consent bool in the Arcademy's own settings document with no card
   behind it is the approximated gate the packet forbids.
3. **The owner declined the analogous decision three days ago.** `owner-decisions.md:111` — the
   Goon row was left BLOCKED expressly because it would be "the port's first outbound network
   boundary. Today the only `HttpClient` here is a loopback probe."

## What gets built instead

Upstream's CLOSED-GATE branch, which in this build is the only reachable branch, and which is a
real user-observable outcome the port currently gets wrong: today `assets-request` is classified
`LaterSlice`, logged and dropped — SILENCE. `ArcademyHostService.cs:1479-1481`: "A closed gate
answers with an empty array rather than silence, because silence is what leaves a page spinning."

- `ArcademyProtocol`: `assets-request` parses and classifies as handled; `BuildAssetsClosed`.
- `ArcademySession`: answers every request with `{type:"assets", reqId, urls:[], done:true}`.
- `LaterSlice` is deleted — after this there is no upstream page→host type it names.

## Facts (all in `client/tests/CcpClient.Tests/ArcademyRemoteMediaTests.cs`)

1. Every request is answered with a terminating empty batch under its own reqId (including one
   that carried none — upstream's `?? ""` at `:1487`).
2. The consent flag is NOT load-bearing: every fact combination still answers empty.
3. Handling a request opens no socket — `System.Net.Sockets` ConnectStart telemetry, with the
   observer proved non-blind by a real loopback connect in the same test.
4. The feature carries no outbound-capable API — lexical chokepoint guard, matcher proved
   non-blind against a file that really does hold one.
