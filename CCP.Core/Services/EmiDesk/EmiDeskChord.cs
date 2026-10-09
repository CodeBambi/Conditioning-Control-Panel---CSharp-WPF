using System;
using System.Collections.Generic;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Services.EmiDesk
{
    /// <summary>Modifiers of a summon chord, independent of any UI toolkit's key enums.</summary>
    [Flags]
    public enum ChordMods { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

    /// <summary>
    /// The EMI Desk summon chord rules both heads share: how a chord is written ("Ctrl+Alt+E") and
    /// what is refused. Lifted from the WPF <c>EmiDeskService.FormatChord</c> / <c>ValidateChord</c>,
    /// which now delegate here; key names are the WPF/Avalonia <c>Key</c> enum names (identical for
    /// the keys a chord uses).
    /// </summary>
    public static class EmiDeskChord
    {
        public const string DefaultHotkey = "Ctrl+Alt+E";

        /// <summary>Render a chord the way it is stored and shown: "Ctrl+Alt+E".</summary>
        public static string Format(ChordMods mods, string key)
        {
            var parts = new List<string>(4);
            if ((mods & ChordMods.Ctrl) != 0) parts.Add("Ctrl");
            if ((mods & ChordMods.Alt) != 0) parts.Add("Alt");
            if ((mods & ChordMods.Shift) != 0) parts.Add("Shift");
            if ((mods & ChordMods.Win) != 0) parts.Add("Win");
            parts.Add(key);
            return string.Join("+", parts);
        }

        /// <summary>Split a stored chord into modifiers and key name. Null when it is empty or has no
        /// modifier (a bare-key global summon would eat that letter in every other app). The key name
        /// is not checked against any enum; the head that arms it does that.</summary>
        public static (ChordMods Mods, string Key)? Parse(string? chord)
        {
            if (string.IsNullOrWhiteSpace(chord)) return null;
            var mods = ChordMods.None;
            string? key = null;
            foreach (var raw in chord.Split('+'))
            {
                var part = raw.Trim();
                if (part.Length == 0) continue;
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= ChordMods.Ctrl; continue;
                    case "alt": mods |= ChordMods.Alt; continue;
                    case "shift": mods |= ChordMods.Shift; continue;
                    case "win": case "windows": mods |= ChordMods.Win; continue;
                }
                if (key != null) return null;
                key = part;
            }
            if (key == null || mods == ChordMods.None) return null;
            return (mods, key);
        }

        /// <summary>
        /// Why a candidate chord cannot be used, or null when it is fine. Localized, for the capture
        /// UI to show inline. Checks: a modifier is required, the base key must not be on the global
        /// keyboard hook (panic / pause), and it must not be the Quick Recal chord (Ctrl+Alt+G).
        /// </summary>
        public static string? Validate(ChordMods mods, string? key, AppSettings? settings)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key) || key == "None") return Loc.Get("emi_desk_hotkey_err_empty");
                if (mods == ChordMods.None) return Loc.Get("emi_desk_hotkey_err_bare");

                if (PanicPolicy.FindHookClash(key, PanicPolicy.HookBoundBaseKeys(settings)) is { } clash)
                    return Loc.GetF("emi_desk_hotkey_err_hook", clash.Name, clash.Key);

                if (mods == (ChordMods.Ctrl | ChordMods.Alt) && string.Equals(key, "G", StringComparison.OrdinalIgnoreCase))
                    return Loc.Get("emi_desk_hotkey_err_quickrecal");
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
