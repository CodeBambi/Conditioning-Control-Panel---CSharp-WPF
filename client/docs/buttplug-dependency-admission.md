# Buttplug Dependency — Admission Record

`client/port.txt` requires researching a dependency before selecting it. The owner admitted the
Buttplug route on 2026-08-23; this records what that package actually costs, **measured in this
repository rather than read off a page**.

The package is not committed yet. It was added, restored, built, listed, and reverted, so the
numbers below are observations rather than expectations.

## Measured here

| Fact | Result | How |
|---|---|---|
| Restores and builds on `net10.0` | **Yes — 0 warnings, 0 errors** | added to `CcpClient.Desktop.csproj`, `dotnet restore` + full solution build |
| Transitive additions | **Exactly one: `Newtonsoft.Json 13.0.4`** | `dotnet list package --include-transitive`, run with and without Buttplug; 0 occurrences without it |
| Target framework | `netstandard2.0` minimum | package metadata |
| Licence | BSD 3-Clause, Nonpolynomial LLC | package metadata |

## The one real cost

**A second JSON stack.** This client uses `System.Text.Json` throughout and carries no
`Newtonsoft.Json` today — verified both ways above. Admitting Buttplug brings Newtonsoft in
transitively. Nothing in our code has to *use* it, but it ships, it is a supply-chain surface, and a
future contributor will find two serializers available and may reach for the wrong one.

That is a cost to accept knowingly, not a blocker. The alternative — speaking Buttplug protocol v3
over a raw WebSocket ourselves — trades one transitive package for a hand-maintained implementation
of someone else's evolving wire spec, which is a far worse trade.

## Status of the project, corrected

A search result claimed the C# repo was *"archived ~2 years ago"* in favour of a Rust FFI approach
that *"ended up being a partial failure."* That is stale. It has been **un-archived and revived as a
client-only, pure .NET implementation with no Rust FFI bindings**, and `5.0.1` was published
**2026-06-08**. The embedded server is deliberately gone: the client is expected to run against
**Intiface Central** (GUI) or **Intiface Engine** (CLI), which the *user* installs.

That matters for the admission argument: like Lovense, this is a client of a separate program on
loopback, not a driver. No kernel or driver boundary opens.

## The API this sink would drive

From the shipping provider (`Services/Haptics/ButtplugProvider.cs`), spec v4 mapping:

- `new ButtplugWebsocketConnector(new Uri("ws://127.0.0.1:12345"))`, then `ConnectAsync`
- `StartScanningAsync` / `StopScanningAsync`, devices via `client.Devices`
- Per feature: `device.GetFeaturesWithOutput(OutputType.Vibrate)` →
  `feature.RunOutputAsync(DeviceOutput.Vibrate.Percent(intensity))`
- `device.StopAsync()` to stop one device

**Outputs LATCH**, so unlike the Lovense LAN mode there is no expiry and no keep-alive question at
all.

## The unresolved design question, and it is testability

The Lovense sink is proved against a **real loopback HTTP server**, because what that route can get
wrong is the shape of a request another program parses. The equivalent honesty here is harder:

- `ButtplugClient` and `ButtplugWebsocketConnector` are concrete types. A sink that news them up
  cannot be exercised without a server.
- A real test server means implementing enough of **Buttplug protocol v3** — handshake, device list,
  output commands — to be worth trusting. That is a meaningful build, and a wrong implementation
  would prove the sink against a fiction.
- Wrapping the client behind our own interface makes the sink testable but moves the untested part
  into the wrapper, which is where the real protocol risk lives.

Neither option is free, and picking the wrong one produces a green suite over an unproven sink —
the failure mode this port has hit repeatedly. This should be decided before the sink is written,
not during.

One thing the shipping provider does that the port must **not** copy: it waits a fixed
`Task.Delay(2000)` after `StartScanningAsync` to let devices appear. Buttplug raises device-added
events; a fixed sleep is both slower and less reliable, and this repository bans wall-clock waits in
tests outright.

## Sources

- [NuGet: Buttplug 5.0.1](https://www.nuget.org/packages/Buttplug/) — version, date, frameworks, licence, dependencies
- [buttplugio/buttplug-csharp](https://github.com/buttplugio/buttplug-csharp) — un-archived, client-only, spec v3, Intiface required
