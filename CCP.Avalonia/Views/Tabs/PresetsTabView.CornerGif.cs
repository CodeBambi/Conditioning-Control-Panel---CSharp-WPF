// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Presets.cs (7.1.5): the session detail's
// "corner GIF" option. :527 the panel per session, :539 the switch folds its settings, :551 the picker,
// :578 size (400 ms debounce, the overlay is recreated), :609 corner, :617 opacity, :1586 the picks applied
// at start. Live edits reach a running session (#474) through SessionRunner.UpdateCornerGif.
using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class PresetsTabView
    {
        private string _selectedCornerGifPath = "";
        private DispatcherTimer? _cornerGifSizeDebounce;
        private object? _selectCornerGifLabel;

        private void WireCornerGifOption()
        {
            _selectCornerGifLabel = BtnSelectCornerGif.Content;
            ChkCornerGifEnabled.IsCheckedChanged += (_, _) => CornerGifSettings.IsVisible = ChkCornerGifEnabled.IsChecked == true;
            BtnSelectCornerGif.Click += async (_, _) => await PickCornerGifAsync();
            SliderCornerGifSize.ValueChanged += SliderCornerGifSize_Changed;
            SliderCornerGifOpacity.ValueChanged += SliderCornerGifOpacity_Changed;
            foreach (var rb in new[] { RbCornerTL, RbCornerTR, RbCornerBL, RbCornerBR })
                rb.IsCheckedChanged += (s, _) =>
                {
                    // Live during a session (#474). Only the button that became checked speaks.
                    if ((s as RadioButton)?.IsChecked == true) RunningSession()?.UpdateCornerGif(position: SelectedCornerPosition());
                };
        }

        /// <summary>WPF :527: shown for a session that offers it, switched off, settings folded.</summary>
        private void ShowCornerGifOption(Session session)
        {
            CornerGifOptionPanel.IsVisible = session.HasCornerGifOption;
            if (!session.HasCornerGifOption) return;
            TxtCornerGifDesc.Text = session.LocalizedCornerGifDescription;
            ChkCornerGifEnabled.IsChecked = false;
            CornerGifSettings.IsVisible = false;
        }

        /// <summary>WPF :1586: the picks go onto the session as it starts. Unticked means off: a session
        /// object started once with the option on does not keep it for the next start.</summary>
        internal void ApplyCornerGifPicks(Session? session)
        {
            if (session is not { HasCornerGifOption: true }) return;
            bool on = ChkCornerGifEnabled.IsChecked == true;
            session.Settings.CornerGifEnabled = on;
            if (!on) return;
            session.Settings.CornerGifPath = _selectedCornerGifPath;
            session.Settings.CornerGifPosition = SelectedCornerPosition();
            session.Settings.CornerGifSize = (int)SliderCornerGifSize.Value;
            session.Settings.CornerGifOpacity = (int)SliderCornerGifOpacity.Value;
        }

        internal CornerPosition SelectedCornerPosition()
        {
            if (RbCornerTL.IsChecked == true) return CornerPosition.TopLeft;
            if (RbCornerTR.IsChecked == true) return CornerPosition.TopRight;
            if (RbCornerBR.IsChecked == true) return CornerPosition.BottomRight;
            return CornerPosition.BottomLeft;
        }

        private static ConditioningControlPanel.Services.SessionRunner? RunningSession() =>
            App.Sessions is { IsRunning: true } r ? r : null;

        private async System.Threading.Tasks.Task PickCornerGifAsync()
        {
            try
            {
                if (TopLevel.GetTopLevel(this) is not { } top) return;
                var storage = top.StorageProvider;
                var start = Path.Combine(CorePaths.EffectiveAssets, "images");
                var picked = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Loc.Get("title_select_corner_gif"),
                    AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType("GIF") { Patterns = new[] { "*.gif" } }, FilePickerFileTypes.All },
                    SuggestedStartLocation = Directory.Exists(start) ? await storage.TryGetFolderFromPathAsync(start) : null,
                });
                if (picked.Count != 1 || picked[0].TryGetLocalPath() is not { } path) return;
                SetCornerGifPath(path);
            }
            catch (Exception ex) { Log.Warning(ex, "Corner GIF pick failed"); }
        }

        internal void SetCornerGifPath(string path)
        {
            _selectedCornerGifPath = path;
            BtnSelectCornerGif.Content = $"📁 {Path.GetFileName(path)}";
            RunningSession()?.UpdateCornerGif(path: path);   // live (#474): the overlay is recreated
        }

        private void SliderCornerGifSize_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            TxtCornerGifSize.Text = $"{(int)e.NewValue}px";
            if (RunningSession() == null) return;
            // Live, once the slider rests: every change recreates the overlay.
            _cornerGifSizeDebounce ??= NewSizeDebounce();
            _cornerGifSizeDebounce.Stop();
            _cornerGifSizeDebounce.Start();
        }

        private DispatcherTimer NewSizeDebounce()
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                RunningSession()?.UpdateCornerGif(size: (int)SliderCornerGifSize.Value);
            };
            return t;
        }

        private void SliderCornerGifOpacity_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            TxtCornerGifOpacity.Text = $"{(int)e.NewValue}%";
            RunningSession()?.UpdateCornerGif(opacity: (int)e.NewValue);
        }
    }
}
