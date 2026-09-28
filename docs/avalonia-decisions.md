# Avalonia port: decisions log

Every non-trivial decision made while porting the WPF head to Avalonia 12, newest last.
The WPF head (ConditioningControlPanel/) is the reference for behaviour and looks; entries
here record where and why the port chose something, and who advised.

| Date | Question | Options | Choice | Advised by |
|---|---|---|---|---|
| 2026-09-28 | Which .NET does the stack target? | stay net8.0 (EOL 2026-11-10); net10.0 (LTS to 2028-11-14); net11 (STS, not GA) | net10.0 across every project (#1764) | user |
| 2026-09-28 | How is the live Avalonia UI driven for verification? | Avalonia DevTools MCP (paid); Keincheck embedded (MIT); none | Keincheck 0.12.0, Debug builds only, http://127.0.0.1:3001 (#1765) | user |
| 2026-09-28 | Platform verification scope | Windows + Linux; Linux only | Build and verify on Linux (X11/Wayland); Windows compile/test CI kept as a safety net, not a gate | user |
| 2026-09-28 | Fate of the WPF head | delete; keep shipping; retire but keep | Retire from shipping, keep in repo as the frozen, compiling reference | user |
| 2026-09-28 | Media backend for the Avalonia head (audio + video, Linux and Windows) | NAudio on Windows + another lib on Linux; SDL/OpenAL/miniaudio bindings; LibVLCSharp for both; WebView video | LibVLCSharp for audio and video on both OSes (one library, already used by WPF for video); NAudio stays WPF-only; seed the existing CoreAudio seam; no custom mixer (OS mixes one player per layer); ducking via WASAPI on Windows, pactl on Linux. See ~/ccp-port/briefs/oracle-media-backends.md | oracle |
| 2026-09-28 | Transparent video overlays on Wayland | emulate; drop on Wayland | Deferred to the overlay unit; will go to oracle-deep (user-visible behaviour change) | oracle (pending oracle-deep) |
