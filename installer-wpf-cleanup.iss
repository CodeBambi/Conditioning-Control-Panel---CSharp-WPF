; =============================================================================================
; UPGRADE FROM WPF 7.1.x - the old program files go, every bit of data stays (owner, 2026-10-10).
;
; Only compiled into the Avalonia head installer (#ifdef AvaloniaHead in installer.iss). Same AppId,
; same folder: Inno leaves behind what it did not install, so without this list a WPF upgrader keeps
; dead WPF binaries and about 360 MB of art for the retired native Chaos run under {app} forever.
;
; Built by hand from the two publish layouts:
;   WPF 7.1.5  ConditioningControlPanel\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish
;   the head   CCP.Avalonia\bin\publish\win-x64 (single file; CCP.Avalonia.csproj Content items)
;
; RULES (Tests/CCP.Core.Tests/InstallerWpfCleanupTests pins every one):
;   * Every entry is ONE explicit path inside {app}. No wildcards, no "..", never {app} itself.
;   * Whole folders (filesandordirs) only where WPF shipped the folder and nothing reads it now.
;   * NEVER user data: nothing under %APPDATA% / %LOCALAPPDATA%, no settings, media, packs, mods,
;     logs, *.dat or web profiles. Resources, Localization, LockedMod, DroneMod, Spirals and
;     assets\sessions / assets\prompts / assets\knowledge.json are left alone on purpose.
;   * [InstallDelete] runs BEFORE [Files]: anything named here that the head does ship is laid
;     down fresh a moment later.
;   * ConditioningControlPanel.exe is not here: the head installs over it under the same name.
; =============================================================================================
;
; WPF binaries the head does not ship beside the exe (its natives ride inside the single file).
Type: files;          Name: "{app}\WebView2Loader.dll"
Type: files;          Name: "{app}\onnxruntime.lib"
Type: files;          Name: "{app}\onnxruntime_providers_shared.lib"
; WPF's stale shipped config stub (Application.Version 2.1.0). Not the user settings file, which
; lives under %APPDATA% and is never named in this file.
Type: files;          Name: "{app}\appsettings.json"
;
; libvlc: the 32-bit runtime a 64-bit app never loads, plus linker import libraries and C headers
; that were swept into the WPF publish. The x64 runtime itself is left for [Files] to overwrite.
Type: filesandordirs; Name: "{app}\libvlc\win-x86"
Type: filesandordirs; Name: "{app}\libvlc\include"
Type: files;          Name: "{app}\libvlc\libvlc.lib"
Type: files;          Name: "{app}\libvlc\libvlccore.lib"
Type: files;          Name: "{app}\libvlc\vlc.lib"
Type: files;          Name: "{app}\libvlc\vlccore.lib"
Type: files;          Name: "{app}\libvlc\win-x64\libvlc.lib"
Type: files;          Name: "{app}\libvlc\win-x64\libvlccore.lib"
Type: files;          Name: "{app}\libvlc\win-x64\vlc.lib"
Type: files;          Name: "{app}\libvlc\win-x64\vlccore.lib"
;
; Art of the native Chaos run, retired on the head (the web descent draws its own). The one
; sprite still read, assets\Chaos\bubbles\braindrain_melt.png, is NOT listed.
Type: filesandordirs; Name: "{app}\assets\Chaos\announce"
Type: filesandordirs; Name: "{app}\assets\Chaos\backdrops"
Type: filesandordirs; Name: "{app}\assets\Chaos\biomes"
Type: filesandordirs; Name: "{app}\assets\Chaos\boons"
Type: filesandordirs; Name: "{app}\assets\Chaos\crafted"
Type: filesandordirs; Name: "{app}\assets\Chaos\guide"
Type: filesandordirs; Name: "{app}\assets\Chaos\hub"
Type: filesandordirs; Name: "{app}\assets\Chaos\materials"
Type: filesandordirs; Name: "{app}\assets\Chaos\portraits"
Type: filesandordirs; Name: "{app}\assets\Chaos\upgrades"
Type: files;          Name: "{app}\assets\Chaos\banner.png"
Type: files;          Name: "{app}\assets\Chaos\menu.png"
Type: files;          Name: "{app}\assets\Chaos\menu_1.png"
Type: files;          Name: "{app}\assets\Chaos\menu_1_fx.png"
Type: files;          Name: "{app}\assets\Chaos\menu_2.png"
Type: files;          Name: "{app}\assets\Chaos\menu_2_fx.png"
Type: files;          Name: "{app}\assets\Chaos\menu_3.png"
Type: files;          Name: "{app}\assets\Chaos\menu_3_fx.png"
Type: files;          Name: "{app}\assets\Chaos\menu_4.png"
Type: files;          Name: "{app}\assets\Chaos\menu_4_fx.png"
Type: files;          Name: "{app}\assets\Chaos\menu_5.png"
Type: files;          Name: "{app}\assets\Chaos\menu_5_fx.png"
Type: files;          Name: "{app}\assets\Chaos\menu_6.png"
Type: files;          Name: "{app}\assets\Chaos\menu_6_fx.png"
Type: files;          Name: "{app}\assets\Chaos\menu_fx.json"
Type: files;          Name: "{app}\assets\Chaos\menu_logo.png"
Type: files;          Name: "{app}\assets\Chaos\menu_logo_fx.json"
Type: files;          Name: "{app}\assets\Chaos\menu_logo_fx.png"
Type: files;          Name: "{app}\assets\Chaos\recap.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\bambifreeze.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\bound.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\braindrain.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\brittle.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\cascade.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\chaperone.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\darter.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\echo.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\flash.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\glitch.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\gold_droplet.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\golden.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\heart.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\htlink.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\mantra.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\pink.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\prism.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\spiral.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\subliminal.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\sweeper.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\tease.png"
Type: files;          Name: "{app}\assets\Chaos\bubbles\video.png"
