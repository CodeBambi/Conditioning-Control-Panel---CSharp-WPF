// WPF Helpers/FontPickerHelper.cs, the part the Avalonia head needs: the sentinel that means "the
// Fredoka face bundled with the app" (it is installed on no machine, so it can never come back from
// the system font list) and the resolver every reader of a stored picker value goes through.
// The stored string is the WPF one, so a settings file moves between the two heads unchanged.

using System;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Helpers;

internal static class FontPicker
{
    /// <summary>Stored value that means "the Fredoka face bundled with the app" (WPF BundledFredoka).</summary>
    internal const string BundledFredoka = "Fredoka (bundled)";

    /// <summary>The family name a stored value draws in: the packed face for the sentinel, else the name.</summary>
    internal static string FamilyNameOf(string name) =>
        string.Equals(name.Trim(), BundledFredoka, StringComparison.OrdinalIgnoreCase) ? AppFonts.DisplayName : name.Trim();

    /// <summary>WPF Resolve: the family for a stored picker value, chained to the fallback so an
    /// uninstalled pick degrades instead of drawing nothing. A comma in the stored name is cut.</summary>
    internal static FontFamily Resolve(string? name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(fallback)) fallback = "Segoe UI";
        try
        {
            if (string.IsNullOrWhiteSpace(name)) return new FontFamily(fallback);
            name = FamilyNameOf(name);
            if (name.Contains(',')) name = name.Split(',')[0].Trim();
            if (string.IsNullOrWhiteSpace(name)) return new FontFamily(fallback);
            return string.Equals(name, fallback, StringComparison.OrdinalIgnoreCase)
                ? new FontFamily(fallback)
                : new FontFamily($"{name}, {fallback}");
        }
        catch
        {
            return new FontFamily(fallback);
        }
    }
}
