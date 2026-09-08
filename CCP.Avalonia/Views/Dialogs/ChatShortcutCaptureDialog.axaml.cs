using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Modal dialog that listens for the next keypress and captures it as a chat
    /// shortcut. Closes with <c>true</c> and <see cref="CapturedKey"/> and
    /// <see cref="CapturedModifiers"/> set on success, or with <c>true</c> and
    /// <see cref="ResetToDefault"/>=true if the user clicks Reset. Consumers use
    /// <c>ShowDialog&lt;bool&gt;</c>; Avalonia has no <c>DialogResult</c>.
    /// </summary>
    public partial class ChatShortcutCaptureDialog : Window
    {
        private readonly CheckBox _chkGlobal;
        private readonly TextBlock _txtCaptured;

        public Key CapturedKey { get; private set; }

        /// <summary>
        /// Avalonia's <see cref="KeyModifiers"/>, not WPF's <c>ModifierKeys</c> — the latter lives
        /// in System.Windows.Input, which the net8.0 head cannot reference.
        /// </summary>
        public KeyModifiers CapturedModifiers { get; private set; }

        public bool ResetToDefault { get; private set; }

        /// <summary>
        /// State of the "activate from any app" checkbox at dialog close. Caller seeds it
        /// from settings before <see cref="Window.ShowDialog{TResult}"/> and reads it back after.
        /// </summary>
        public bool GlobalHotkey
        {
            get => _chkGlobal.IsChecked == true;
            set => _chkGlobal.IsChecked = value;
        }

        public ChatShortcutCaptureDialog()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new ChatShortcutCaptureViewModel();

            _chkGlobal = this.FindControl<CheckBox>("ChkGlobal")!;
            _txtCaptured = this.FindControl<TextBlock>("TxtCaptured")!;

            // WPF wired these in markup; the porting rules put them in the constructor.
            KeyDown += Window_KeyDown;
            this.FindControl<Button>("BtnReset")!.Click += (_, _) => { ResetToDefault = true; Close(true); };
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);

            Loaded += (_, _) => Focus();
        }

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            // Ignore modifier-only keys; we want a "real" key to bind to.
            // WPF's Key.System/e.SystemKey dance has no Avalonia equivalent: Alt+X arrives here
            // as the real key with KeyModifiers.Alt already set.
            var key = e.Key;
            if (IsModifierOnly(key)) return;

            // Escape cancels.
            if (key == Key.Escape)
            {
                e.Handled = true;
                Close(false);
                return;
            }

            // A bare letter/digit/symbol with no modifier would steal that key
            // globally — typing it in any other app would fire our chat shortcut.
            // Require at least one modifier; F-keys are accepted bare since they
            // rarely collide with text input.
            var mods = e.KeyModifiers;
            if (mods == KeyModifiers.None && !IsFunctionKey(key))
            {
                e.Handled = true;
                _txtCaptured.Text = Loc.Get("label_chat_shortcut_needs_modifier");
                return;
            }

            CapturedKey = key;
            CapturedModifiers = mods;
            ResetToDefault = false;

            e.Handled = true;
            Close(true);
        }

        private static bool IsModifierOnly(Key k)
        {
            return k is Key.LeftCtrl or Key.RightCtrl
                or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt
                or Key.LWin or Key.RWin
                or Key.System or Key.None;
        }

        private static bool IsFunctionKey(Key k) => k >= Key.F1 && k <= Key.F24;
    }

    /// <summary>Strings from CCP.Core's Loc. See the porting notes in the repo-root CLAUDE.md
    /// for why {loc:Str} becomes a binding.</summary>
    public sealed class ChatShortcutCaptureViewModel
    {
        public string LocTitle => Loc.Get("dialog_chat_shortcut_capture_title");
        public string LocPrompt => Loc.Get("dialog_chat_shortcut_capture_prompt");
        public string LocListening => Loc.Get("label_chat_shortcut_listening");
        public string LocHint => Loc.Get("dialog_chat_shortcut_capture_hint");
        public string LocGlobalCheckbox => Loc.Get("chat_shortcut_global_checkbox");
        public string LocGlobalTooltip => Loc.Get("chat_shortcut_global_tooltip");
        public string LocReset => Loc.Get("btn_reset_to_default");
        public string LocCancel => Loc.Get("btn_cancel");
    }
}
