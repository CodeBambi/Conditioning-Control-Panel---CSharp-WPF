// Linux has none of the Windows faces the views name (Segoe UI, Consolas, Courier New). Avalonia skips a
// family it cannot resolve and ends on the desktop's default sans, so every "Consolas, Courier New"
// readout (clocks, prices, counters, logs) drew PROPORTIONAL there and its columns stopped lining up.
// Avalonia's FontFamilyMappings is consulted only when a requested family cannot be resolved, so the
// table below costs Windows nothing (every name resolves there) and needs no call-site edit.
//
// Both targets already ship: Noto Sans Mono is packed for the EMI ring cards (Resources/emi/fonts), Inter
// comes with Avalonia.Fonts.Inter and is what the Fluent theme draws everything else in.
//
// Not covered (docs/avalonia-linux-exceptions.md, Fonts): Segoe MDL2 Assets (icon glyphs; no
// redistributable twin is in the tree) and Fredoka (the display face is not packed on EITHER OS yet).
// STATIC ONLY: written on a Windows desk with no Linux run. First thing to look at on a real box.

using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class AppFonts
{
    internal const string BundledMono = "avares://CCP.Avalonia/Resources/emi/fonts#Noto Sans Mono";
    internal const string BundledSans = "fonts:Inter#Inter";

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

    /// <summary>The mappings as Avalonia wants them. Off Windows only: on Windows every name above is
    /// installed, and the port's look there must not move.</summary>
    internal static FontManagerOptions? Options()
    {
        if (OperatingSystem.IsWindows()) return null;
        var map = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, target) in Substitutes)
        {
            try { map[name] = new FontFamily(target); }
            catch { /* a target that does not parse is a mapping we do not have, never a failed start */ }
        }
        return new FontManagerOptions { FontFamilyMappings = map };
    }
}
