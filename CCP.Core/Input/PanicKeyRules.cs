// The pure half of the Windows panic listener (CCP.Avalonia/Platform/Win32PanicKey.cs), ported from
// WPF 7.1.5: ConditioningControlPanel/Services/Input/GlobalKeyboardHook.cs (HookCallback's Lockdown
// block), Services/Leash/LeashHoldToCut.cs (a held key is ONE press) and the key names
// WPF's KeyInterop.KeyFromVirtualKey gives the hook (the setting stores Key.ToString()).

using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Input
{
    /// <summary>
    /// Windows virtual-key codes for the key names the settings store (WPF and Avalonia
    /// <c>Key.ToString()</c>; both enums carry the same names and aliases). Name -> code accepts every
    /// alias ("Return" and "Enter", "Next" and "PageDown", "Oem3" and "OemTilde"), so a key bound on
    /// either head matches the same physical key. PURE.
    /// </summary>
    public static class VirtualKeys
    {
        public const int Escape = 0x1B, Tab = 0x09, F4 = 0x73, LWin = 0x5B, RWin = 0x5C, F24 = 0x87;

        private static readonly Dictionary<string, int> ByName = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> ByCode = new();

        static VirtualKeys()
        {
            // First name per code is the canonical one NameOf returns.
            void Add(int vk, params string[] names)
            {
                foreach (var n in names) ByName[n] = vk;
                ByCode.TryAdd(vk, names[0]);
            }
            Add(0x08, "Back", "Backspace");
            Add(0x09, "Tab");
            Add(0x0C, "Clear", "OemClear");
            Add(0x0D, "Return", "Enter");
            Add(0x13, "Pause");
            Add(0x14, "Capital", "CapsLock");
            Add(0x1B, "Escape", "Esc");
            Add(0x20, "Space");
            Add(0x21, "PageUp", "Prior");
            Add(0x22, "PageDown", "Next");
            Add(0x23, "End");
            Add(0x24, "Home");
            Add(0x25, "Left");
            Add(0x26, "Up");
            Add(0x27, "Right");
            Add(0x28, "Down");
            Add(0x29, "Select");
            Add(0x2A, "Print");
            Add(0x2B, "Execute");
            Add(0x2C, "Snapshot", "PrintScreen");
            Add(0x2D, "Insert");
            Add(0x2E, "Delete");
            Add(0x2F, "Help");
            for (var d = 0; d <= 9; d++) Add(0x30 + d, "D" + d);
            for (var c = 'A'; c <= 'Z'; c++) Add(c, c.ToString());
            Add(0x5B, "LWin");
            Add(0x5C, "RWin");
            Add(0x5D, "Apps");
            Add(0x5F, "Sleep");
            for (var n = 0; n <= 9; n++) Add(0x60 + n, "NumPad" + n);
            Add(0x6A, "Multiply");
            Add(0x6B, "Add");
            Add(0x6C, "Separator");
            Add(0x6D, "Subtract");
            Add(0x6E, "Decimal");
            Add(0x6F, "Divide");
            for (var f = 1; f <= 24; f++) Add(0x6F + f, "F" + f);
            Add(0x90, "NumLock");
            Add(0x91, "Scroll", "ScrollLock");
            Add(0xA0, "LeftShift");
            Add(0xA1, "RightShift");
            Add(0xA2, "LeftCtrl");
            Add(0xA3, "RightCtrl");
            Add(0xA4, "LeftAlt");
            Add(0xA5, "RightAlt");
            Add(0xA6, "BrowserBack");
            Add(0xA7, "BrowserForward");
            Add(0xA8, "BrowserRefresh");
            Add(0xA9, "BrowserStop");
            Add(0xAA, "BrowserSearch");
            Add(0xAB, "BrowserFavorites");
            Add(0xAC, "BrowserHome");
            Add(0xAD, "VolumeMute");
            Add(0xAE, "VolumeDown");
            Add(0xAF, "VolumeUp");
            Add(0xB0, "MediaNextTrack");
            Add(0xB1, "MediaPreviousTrack");
            Add(0xB2, "MediaStop");
            Add(0xB3, "MediaPlayPause");
            Add(0xB4, "LaunchMail");
            Add(0xB5, "SelectMedia");
            Add(0xB6, "LaunchApplication1");
            Add(0xB7, "LaunchApplication2");
            Add(0xBA, "OemSemicolon", "Oem1");
            Add(0xBB, "OemPlus");
            Add(0xBC, "OemComma");
            Add(0xBD, "OemMinus");
            Add(0xBE, "OemPeriod");
            Add(0xBF, "OemQuestion", "Oem2");
            Add(0xC0, "OemTilde", "Oem3");
            Add(0xDB, "OemOpenBrackets", "Oem4");
            Add(0xDC, "OemPipe", "Oem5");
            Add(0xDD, "OemCloseBrackets", "Oem6");
            Add(0xDE, "OemQuotes", "Oem7");
            Add(0xDF, "Oem8");
            Add(0xE2, "OemBackslash", "Oem102");
        }

        /// <summary>The virtual-key code for a stored key name; 0 when blank or unknown (the key cannot fire).</summary>
        public static int Of(string? name)
            => !string.IsNullOrWhiteSpace(name) && ByName.TryGetValue(name.Trim(), out var vk) ? vk : 0;

        /// <summary>The canonical key name for a virtual-key code, or null for a code with no name here.</summary>
        public static string? NameOf(int vk) => ByCode.TryGetValue(vk, out var n) ? n : null;
    }

    /// <summary>
    /// A held panic key is ONE press (WPF LeashHoldToCut, bug hunt 2026-09-29 DESK-5): Windows
    /// auto-repeats a held key's down about thirty times a second, and two counted presses inside
    /// 2 s quit the app. The first down is the press; a down within <see cref="RepeatGap"/> of the
    /// last one with no key-up between is a repeat and is swallowed, so a lost key-up can never eat
    /// the next real press. While leashed the repeats also time a five second hold (<c>Due</c>, once
    /// per hold), which the Leash's hold-to-cut reads. PURE; not thread-safe (one listener thread).
    /// </summary>
    public sealed class PanicKeyHold
    {
        public static readonly TimeSpan Hold = TimeSpan.FromSeconds(5);

        /// <summary>Longer than the slowest Windows repeat delay (1 s).</summary>
        public static readonly TimeSpan RepeatGap = TimeSpan.FromMilliseconds(1200);

        private DateTime? _since;
        private DateTime _last;
        private bool _asked;

        public bool Held => _since != null;

        public TimeSpan? HeldFor(DateTime now) => _since is { } s ? now - s : null;

        /// <summary>A key-down of the panic key. <c>Repeat</c> = swallow it (not a new press);
        /// <c>Due</c> = the hold just reached five seconds while leashed (true once per hold).</summary>
        public (bool Repeat, bool Due) Down(DateTime now, bool leashed)
        {
            var repeat = _since != null && now - _last <= RepeatGap && now >= _last;
            if (!repeat) { _since = now; _asked = false; }
            _last = now;
            var due = leashed && !_asked && now - _since!.Value >= Hold;
            if (due) _asked = true;
            return (repeat, due);
        }

        /// <summary>The panic key came up.</summary>
        public void Up()
        {
            _since = null;
            _asked = false;
        }
    }

    /// <summary>
    /// Lockdown's system-key block (WPF GlobalKeyboardHook.HookCallback under SuppressSystemKeys):
    /// the Windows keys, Alt+Tab / Alt+F4 / Alt+Esc (Alt held = WM_SYSKEYDOWN), Ctrl+Esc and
    /// Ctrl+Shift+Esc. Bare Esc is NEVER blocked (#680: it ate every Escape in every app), and no other
    /// Alt chord is (#338: menu mnemonics died). Ctrl+Alt+Del is kernel level and stays the safety
    /// valve. Key-downs only. PURE.
    /// </summary>
    public static class SystemKeyBlock
    {
        public static bool Suppress(int vk, bool sysKeyDown, bool ctrlDown)
        {
            if (vk == VirtualKeys.LWin || vk == VirtualKeys.RWin) return true;
            if (sysKeyDown && (vk == VirtualKeys.Tab || vk == VirtualKeys.F4 || vk == VirtualKeys.Escape)) return true;
            return vk == VirtualKeys.Escape && ctrlDown;   // Ctrl+Esc (Start), Ctrl+Shift+Esc (Task Manager)
        }
    }
}
