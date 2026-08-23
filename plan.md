# Packet plan — phrase-backup buttons + in-app toast surface

Checkpoint file; removed before the final report.

## Item 2 first (item 1 consumes it)

`client/src/CcpClient.Desktop/Views/ToastHost.axaml(.cs)` — a `UserControl` docked top-right over
`PageHost` in `MainWindow.axaml`.

Ported outcome (upstream `Services/Notifications/NotificationService.cs`):

- four kinds Info/Success/Warning/Error, each with its own accent (`:12`, `:120-126`)
- stacked top-right of the shell (`MainWindow/MainWindow.xaml:3217-3219`)
- host panel has NO Background so empty space is click-through (`:6-14`, `MainWindow.xaml:3210-3213`)
- every toast carries its own dismiss affordance (`:200-229`)
- auto-dismiss after a duration, on an INJECTED schedule seam (`:94-104`)

Deliberately NOT ported, with reasons: `ShowSticky` + `DismissedNotificationKeys` (`:61-68`,
`:216-226`) — its persistence lives in the deferred notification-settings census entry and it has
no consumer here; the action button (`:175-198`) — its only upstream consumer is TierGate's
"See tiers" (`Services/TierGate.cs:133`) which needs `PlayPage` + an App Info page, both outside
this File Scope; the pending replay queue (`:29-43`) — upstream's service is a static built before
the window, the port's host is a control created with the window.

Clock seam: `Func<TimeSpan, Action, IDisposable> Schedule`, defaulting to
`DispatcherTimer.RunOnce`. Declared locally rather than reusing `Session/ISessionClock` for the
reason `ISessionClock`'s own doc gives for not reusing `Audio/ISoundClock`.

## Item 1

`Views/Pages/SystemPage.axaml(.cs)` gains a Phrase backup module: Export + Import buttons,
`AvaloniaUserFilePicker.For(this)`, an inline confirm strip (the port has no dialog surface —
`StudioPage.axaml:1898-1900` already records that) supplying `confirmReplace`, and every outcome
routed to the toast host.

Stores: `SessionParticipant.SubliminalPreset` / `.LockCardPreset` / `.BouncingTextPreset` are
already public. `MainWindow` already resolves the one `SessionParticipant`; `SystemPage`'s ctor
takes it and the toast host, exactly as `StudioPage` takes its deps.

Text lives in `Views/Pages/PhraseBackupNotices.cs` (the `*Notices.cs` convention), from upstream
`MainWindow/MainWindow.PresetIO.cs:62-134` and `en.json:4881-4886`. `Persisted == false` reads
"restored, but not yet saved".

## Headed evidence

New surface `toast`, states `saved` / `refused`, in `capture.ps1` + `checks.json`. Both states are
the SAME control at the same geometry; only the accent differs, and the accent is chosen by the
TYPED outcome — so each check must fail on the other capture. Gate on UIA text before any pixel.
Real Win32 file dialogs are driven, which is also the first time this port opens one.

## Floor

Report delta only. Never open `client/tests/floor/floor.json`.
