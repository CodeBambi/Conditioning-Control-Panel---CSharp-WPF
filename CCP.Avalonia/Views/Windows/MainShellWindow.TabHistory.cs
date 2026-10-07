// PORTED from ConditioningControlPanel/MainWindow/MainWindow.TabHistory.cs: back and forward
// between tabs (ccp-bugs #1290, the rail's Back arrow from eb6ccc404). The order lives in Core
// (Services/UI/TabHistory.cs); this file only feeds it and reads the input.
// Guards WPF has on TabHistoryStep (MainWindow.TabHistory.cs:82-88) that are ABSENT here, each because
// its surface does not exist on this head yet: App.StartupLadder.IsModalUp (this head's StartupLadder
// has no modal ladder - see its ponytail note), RemoteControlOverlay visibility, and the leash gate.

using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // The window opens on the dashboard without going through ShowTab.
        private readonly TabHistory _tabHistory = new("settings");

        // True while back/forward is replaying a tab, so ShowTab does not record the move as new.
        private bool _tabHistoryReplaying;

        /// <summary>Called from ShowTab for every key that really switches the tab.</summary>
        private void NoteTabHistory(string tab)
        {
            if (_tabHistoryReplaying) return;
            // "lab" and "play" are one view: record the view, or Back lands on the same page.
            _tabHistory.Navigate(tab == "lab" ? "play" : tab);
            RefreshNavBack();
        }

        /// <summary>The rail's Back arrow (above search) shows only when there is a tab to go back to.</summary>
        private void RefreshNavBack()
        {
            if (Named<Button>("BtnNavBack") is { } back) back.IsVisible = _tabHistory.CanGoBack;
        }

        private void BtnNavBack_Click(object? sender, RoutedEventArgs e) => TabHistoryStep(back: true);

        private void InitializeTabHistoryInput()
        {
            AddHandler(PointerPressedEvent, OnTabHistoryPointerPressed, RoutingStrategies.Tunnel);
            AddHandler(KeyDownEvent, OnTabHistoryKeyDown, RoutingStrategies.Tunnel);
        }

        private void OnTabHistoryPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
            if (kind == PointerUpdateKind.XButton1Pressed) { if (TabHistoryStep(back: true)) e.Handled = true; }
            else if (kind == PointerUpdateKind.XButton2Pressed) { if (TabHistoryStep(back: false)) e.Handled = true; }
        }

        private void OnTabHistoryKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyModifiers != KeyModifiers.Alt) return;
            if (e.Key != Key.Left && e.Key != Key.Right) return;
            if (FocusOwnsArrows()) return;
            if (TabHistoryStep(back: e.Key == Key.Left)) e.Handled = true;
        }

        // A text box or an embedded browser keeps its own Alt+arrows.
        private bool FocusOwnsArrows()
        {
            var focused = FocusManager?.GetFocusedElement();
            if (focused is TextBox) return true;
            if (focused is ComboBox { IsEditable: true }) return true;
            return focused?.GetType().Name.Contains("WebView", StringComparison.OrdinalIgnoreCase) == true;
        }

        internal bool TabHistoryStep(bool back)
        {
            // A lockdown or a tutorial card holds the page on purpose; Back must not switch under it.
            if (LockdownActive || CoreTutorial.IsActive) return false;
            var target = back ? _tabHistory.Back() : _tabHistory.Forward();
            if (target == null) return false;
            _tabHistoryReplaying = true;
            try { ShowTab(target); }
            catch (Exception ex) { Serilog.Log.Debug("Tab history step failed: {E}", ex.Message); }
            finally { _tabHistoryReplaying = false; RefreshNavBack(); }
            return true;
        }
    }
}
