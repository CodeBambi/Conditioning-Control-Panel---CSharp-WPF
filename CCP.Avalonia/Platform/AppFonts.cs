// Linux has none of the Windows faces the views name (Segoe UI, Consolas, Courier New). Avalonia skips a
// family it cannot resolve and ends on the desktop's default sans, so every "Consolas, Courier New"
// readout (clocks, prices, counters, logs) drew PROPORTIONAL there and its columns stopped lining up.
// Avalonia's FontFamilyMappings is consulted only when a requested family cannot be resolved, so the
// table below costs Windows nothing (every name resolves there) and needs no call-site edit.
//
// Both targets already ship: Noto Sans Mono is packed for the EMI ring cards (Resources/emi/fonts), Inter
// comes with Avalonia.Fonts.Inter and is what the Fluent theme draws everything else in.
//
// Fredoka, the display face (page titles, the board, the launcher): WPF 7.1.5 packs it as
// /Fonts/#Fredoka and every WPF call site names that pack path, so it draws on every machine. Here it
// is packed at Resources/fonts (SIL OFL 1.1, OFL-Fredoka.txt beside it) and the bare family name the
// views use ("Fredoka, Segoe UI") is mapped to it on EVERY OS. It is one variable file whose default
// instance is Light: WPF cannot move the weight axis either, so both heads draw Light and embolden.
//
// Not covered (docs/avalonia-linux-exceptions.md, Fonts): Segoe MDL2 Assets (icon glyphs; no
// redistributable twin is in the tree).
// STATIC ONLY: written on a Windows desk with no Linux run. First thing to look at on a real box.

using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class AppFonts
{
    internal const string BundledMono = "avares://CCP.Avalonia/Resources/emi/fonts#Noto Sans Mono";
    internal const string BundledSans = "fonts:Inter#Inter";
    /// <summary>The packed display face. The family name is the font's typographic family (name id 16).</summary>
    internal const string DisplayName = "Fredoka";
    internal const string BundledDisplay = "avares://CCP.Avalonia/Resources/fonts#Fredoka";

    /// <summary>Windows family name -> the bundled family that stands in where it is missing.</summary>
    internal static readonly IReadOnlyDictionary<string, string> Substitutes = new Dictionary<string, string>
    {
        ["Consolas"] = BundledMono,
        ["Courier New"] = BundledMono,
        ["Cascadia Mono"] = BundledMono,
        ["monospace"] = BundledMono,     // a CSS generic, not a family any font manager resolves
        ["Segoe UI"] = BundledSans,
        ["Segoe UI Semibold"] = BundledSans,
        ["Segoe UI Black"] = BundledSans,
        ["Arial"] = BundledSans,
    };

    /// <summary>The mappings as Avalonia wants them. Fredoka on every OS (it is installed on none);
    /// the Windows stand-ins off Windows only: on Windows every name above is installed, and the
    /// port's look there must not move.</summary>
    internal static FontManagerOptions Options()
    {
        var map = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
        try { map[DisplayName] = new FontFamily(BundledDisplay); }
        catch { /* never a failed start over a font */ }
        if (OperatingSystem.IsWindows()) return new FontManagerOptions { FontFamilyMappings = map };
        foreach (var (name, target) in Substitutes)
        {
            try { map[name] = new FontFamily(target); }
            catch { /* a target that does not parse is a mapping we do not have, never a failed start */ }
        }
        return new FontManagerOptions { FontFamilyMappings = map };
    }
}
