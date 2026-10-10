// PORTED from WPF 7.1.5 LauncherWindow.xaml.cs: the Media pill (BtnGear_Click :222), the "Add a
// desktop shortcut" link under the panel CTA (ShortcutLink_Click :498, ShowShortcutResult :504), the
// tile's hover shortcut button (LauncherWindow.Tiles.cs :163) and the bottom row's "What's new" link
// (WhatsNew_Click :659). Every button's click cue is the window-wide one in LauncherWindow.Fx.cs.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Launcher;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class LauncherWindow
    {
        /// <summary>WPF _shortcutTextTimer: the link says how it went for 3 s, then asks again.</summary>
        private readonly DispatcherTimer _shortcutTextTimer = new() { Interval = TimeSpan.FromSeconds(3) };

        /// <summary>Tests: the writer. Null = Platform/LauncherShortcutWriter.</summary>
        internal Func<string?, bool>? ShortcutWriter { get; set; }

        /// <summary>The loc key the shortcut link wears right now.</summary>
        internal string ShortcutLinkKey { get; private set; } = "launcher_add_shortcut";

        private void HookLinks()
        {
            _shortcutTextTimer.Tick += (_, _) => { _shortcutTextTimer.Stop(); BindShortcutLink("launcher_add_shortcut"); };
            Closed += (_, _) => _shortcutTextTimer.Stop();
            BindShortcutLink("launcher_add_shortcut");
        }

        /// <summary>Bound, not assigned: a language switch follows the link whichever line it shows.</summary>
        private void BindShortcutLink(string key)
        {
            ShortcutLinkKey = key;
            ShortcutLinkText.Bind(TextBlock.TextProperty, new Binding($"[{key}]")
                { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
        }

        private void ShortcutLink_Click(object? sender, RoutedEventArgs e) => WriteShortcut(null);

        /// <summary>WPF ShowShortcutResult(LauncherShortcuts.TryCreateDesktopShortcut(id)).</summary>
        internal void WriteShortcut(string? gameId)
        {
            bool ok;
            try { ok = (ShortcutWriter ?? LauncherShortcutWriter.TryCreateDesktopShortcut)(gameId); }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] shortcut for {Id} threw", gameId); ok = false; }
            BindShortcutLink(ok ? "launcher_shortcut_added" : "launcher_shortcut_failed");
            _shortcutTextTimer.Stop();
            _shortcutTextTimer.Start();
        }

        /// <summary>WPF LauncherWindow.Tiles.cs:163: the round link button at the plate's top right,
        /// unseen until the pointer is on the tile. It handles its own press, so the tile's
        /// whole-card Play never fires with it. The mystery card has none.</summary>
        private Button ShortcutButton(Border tile, string gameId)
        {
            var btn = new Button
            {
                Content = "🔗", Width = 32, Height = 32, FontSize = 15, Opacity = 0,
                CornerRadius = new CornerRadius(16), Padding = new Thickness(0), BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 8, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Background = Res("SurfaceBgBrush"), Foreground = Res("TextSecondaryBrush"),
                FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe UI Symbol, Segoe UI"),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = "tile-shortcut",
            };
            ToolTip.SetTip(btn, Loc.Get("launcher_add_shortcut"));
            btn.Click += (_, e) =>
            {
                e.Handled = true;
                if (MainShellWindow.LockdownActive) return;
                LauncherSfx.Click();   // the window-wide cue skips everything inside the games grid
                WriteShortcut(gameId);
            };
            tile.PointerEntered += (_, _) => btn.Opacity = 1;
            tile.PointerExited += (_, _) => btn.Opacity = 0;
            return btn;
        }

        // ------------------------------------------------------------------ Media

        /// <summary>WPF BtnGear_Click: the dialog over the launcher; if it will not open, the panel's
        /// Settings page instead.</summary>
        private async void BtnGear_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new LauncherMediaDialog { OpenAssetBrowser = () => OpenPanel(p => p.ShowTab("assets")) };
                await dlg.ShowDialogSafe(this);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Launcher] Game media dialog failed; falling back to the settings tab");
                OpenPanel(p => p.ShowTab("appsettings"));
            }
        }

        // ------------------------------------------------------------------ What's new

        /// <summary>WPF WhatsNew_Click: the same notes dialog the panel shows, over the launcher, so
        /// the panel is not woken for them; if it will not open, Home on the panel.</summary>
        private async void WhatsNew_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Dialogs.WhatsNewDialog(
                    Loc.GetF("set2_whats_new_title_0", CoreReleaseContent.AppVersion),
                    CoreReleaseContent.PatchNotes);
                await dlg.ShowDialogSafe(this);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[Launcher] What's New dialog failed; falling back to the settings tab");
                OpenPanel(p => p.ShowTab("settings"));
            }
        }
    }
}
