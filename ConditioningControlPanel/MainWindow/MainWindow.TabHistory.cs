using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel
{
    // Back and forward between tabs (ccp-bugs #1290): mouse side buttons and Alt+Left / Alt+Right.
    // The order lives in Services/UI/TabHistory.cs; this file only feeds it and reads the input.
    public partial class MainWindow
    {
        // The window opens on the dashboard without going through ShowTab.
        private readonly TabHistory _tabHistory = new("settings");

        // True while back/forward is replaying a tab, so ShowTab does not record the move as new.
        private bool _tabHistoryReplaying;

        /// <summary>Called from ShowTab for every key that really switches the tab.</summary>
        private void NoteTabHistory(string tab)
        {
            if (_tabHistoryReplaying) return;
            _tabHistory.Navigate(tab);
        }

        private void InitializeTabHistoryInput()
        {
            // A WebView2 page gets its own mouse buttons (the HWND never routes them through WPF),
            // so this only sees presses on the panel itself.
            PreviewMouseDown += OnTabHistoryMouseDown;
            PreviewKeyDown += OnTabHistoryKeyDown;
        }

        private void OnTabHistoryMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.XButton1)
            {
                if (TabHistoryStep(back: true)) e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.XButton2)
            {
                if (TabHistoryStep(back: false)) e.Handled = true;
            }
        }

        private void OnTabHistoryKeyDown(object sender, KeyEventArgs e)
        {
            // Alt+arrow arrives as Key.System with the arrow in SystemKey.
            if (e.Key != Key.System) return;
            if ((Keyboard.Modifiers & ~ModifierKeys.Alt) != ModifierKeys.None) return;
            if (e.SystemKey != Key.Left && e.SystemKey != Key.Right) return;
            if (FocusOwnsArrows()) return;
            if (TabHistoryStep(back: e.SystemKey == Key.Left)) e.Handled = true;
        }

        // A text box or an embedded browser keeps its own Alt+arrows.
        private static bool FocusOwnsArrows()
        {
            var focused = Keyboard.FocusedElement;
            if (focused is TextBoxBase || focused is PasswordBox) return true;
            if (focused is ComboBox combo && combo.IsEditable) return true;
            var typeName = focused?.GetType().Name ?? string.Empty;
            return typeName.IndexOf("WebView", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TabHistoryStep(bool back)
        {
            if (App.Lockdown?.IsActive == true) return false;
            var target = back ? _tabHistory.Back() : _tabHistory.Forward();
            if (target == null) return false;
            _tabHistoryReplaying = true;
            try { ShowTab(target); }
            catch (Exception ex) { App.Logger?.Debug("Tab history step failed: {E}", ex.Message); }
            finally { _tabHistoryReplaying = false; }
            return true;
        }
    }
}
