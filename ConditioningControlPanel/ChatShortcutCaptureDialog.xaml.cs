using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Modal dialog that listens for the next keypress and captures it as a chat
    /// shortcut. Returns DialogResult=true with <see cref="CapturedKey"/> and
    /// <see cref="CapturedModifiers"/> set on success, or DialogResult=true with
    /// <see cref="ResetToDefault"/>=true if the user clicks Reset.
    /// </summary>
    public partial class ChatShortcutCaptureDialog : Window
    {
        public Key CapturedKey { get; private set; }
        public ModifierKeys CapturedModifiers { get; private set; }
        public bool ResetToDefault { get; private set; }

        public ChatShortcutCaptureDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => Focus();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Ignore modifier-only keys; we want a "real" key to bind to.
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (IsModifierOnly(key)) return;

            // Escape cancels.
            if (key == Key.Escape)
            {
                e.Handled = true;
                DialogResult = false;
                Close();
                return;
            }

            // A bare letter/digit/symbol with no modifier would steal that key
            // globally — typing it in any other app would fire our chat shortcut.
            // Require at least one modifier; F-keys are accepted bare since they
            // rarely collide with text input.
            var mods = Keyboard.Modifiers;
            if (mods == ModifierKeys.None && !IsFunctionKey(key))
            {
                e.Handled = true;
                if (TxtCaptured != null)
                    TxtCaptured.Text = Loc.Get("label_chat_shortcut_needs_modifier");
                return;
            }

            CapturedKey = key;
            CapturedModifiers = mods;
            ResetToDefault = false;

            e.Handled = true;
            DialogResult = true;
            Close();
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            ResetToDefault = true;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
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
}
