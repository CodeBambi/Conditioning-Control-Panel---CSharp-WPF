# Dialog Porting TODO

## Batch: LoginDialog, DisplayNameDialog, UsernamePickerDialog, AttentionCheckSettingsDialog, AwarenessPresetDetailDialog

- [x] Read source WPF files and existing Avalonia dialog patterns
- [x] Extend `CCP.Core/App.cs` with missing nullable service hooks needed by ported dialogs
- [x] Port `AttentionCheckSettingsDialog` (embed `AttentionCheckFeatureControl`, Test now)
- [x] Port `DisplayNameDialog` (create/change/delete modes, validation)
- [x] Port `UsernamePickerDialog` (availability check)
- [x] Temporarily exclude broken `ChaosGifCascadeOverlay` from CCP.Avalonia build (AvaloniaGif 1.0 is incompatible with Avalonia 12)
- [x] Port `LoginDialog` (providers, account, device-code) — done (lot 8; all 4 flows wired)
- [x] Port `AwarenessPresetDetailDialog` (read/edit preset, triggers, actions) — done (lot-9 L2 confirmed: full trigger/action inline editor, install/clone/delete, prompt validation + moderation log)
- [x] Run `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`

> Batch COMPLETE (verified WS0 lot-9 review, 2026-07-04). This file is historical.
> `UsernamePickerDialog` OG-welcome flow is now wired into the login path (lot-9 L4-06).
