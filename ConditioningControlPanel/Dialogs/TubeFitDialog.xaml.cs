using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>
    /// "Tube Fit" editor — a WYSIWYG preview of how the ACTIVE mod's avatar PNG sits inside the
    /// avatar tube, with an Attached/Detached switch and scale/offset sliders. The result is saved
    /// as a per-mod user override in <see cref="AppSettings.TubeLayoutOverridesByMod"/> (never into
    /// the mod itself) and the live tube window is hot-refreshed.
    ///
    /// The preview replicates the real tube's geometry: the tube window is a 780x1020 rect holding a
    /// Uniform Viewbox over a 780x1080 design canvas, so content renders at 1020/1080 and anything
    /// outside the window rect is clipped. The nested Viewbox/Grid pair in the XAML mirrors that,
    /// and the margin formulas below are copied from AvatarTubeWindow.ApplyTubeLayoutOffsets.
    /// </summary>
    public partial class TubeFitDialog : Window
    {
        // Legacy avatar image box in design units (AvatarTubeWindow.xaml defaults for ImgAvatar).
        private const double LegacyAvatarMaxWidth = 198;
        private const double LegacyAvatarMaxHeight = 306;
        private const double PortraitSizeScale = 0.88;   // matches AvatarTubeWindow.ApplyPortraitChrome
        private const double PortraitRaisePx = 30;
        private const double PortraitShiftX = 10;

        // Working copy of the five tube-layout values. Committed to settings only on Save.
        private int _offsetX;
        private int _detachedOffsetX;
        private double _scale = 1.0;
        private int _offsetY;
        private int _detachedOffsetY;

        private bool _detachedMode;
        private bool _suppressSliderEvents;

        private readonly string _modId;
        private readonly int _avatarSet;
        private readonly bool _portraitMode;
        private readonly ImageSource?[] _poses = new ImageSource?[4];
        private int _poseIndex;

        public TubeFitDialog()
        {
            InitializeComponent();

            _modId = App.Mods?.ActiveModId ?? BuiltInMods.CCPDefaultId;
            _avatarSet = Math.Max(1, App.Settings?.Current?.SelectedAvatarSet ?? 1);
            _portraitMode = Services.AvatarPortraitLoader.HasManifestForActiveMod();

            LoadPoses();
            LoadWorkingValues(EffectiveLayout());

            // Start on whatever state the real tube is in, so the preview matches what the user sees.
            _detachedMode = App.Settings?.Current?.AvatarTubeDetached ?? false;
            _suppressSliderEvents = true;
            if (_detachedMode) RbDetached.IsChecked = true; else RbAttached.IsChecked = true;
            _suppressSliderEvents = false;

            PushSlidersFromWorkingValues();
            UpdatePreview();
        }

        // ============================================================
        // VALUE PLUMBING
        // ============================================================

        /// <summary>
        /// The layout currently in force for this mod: the user's saved override wins, else the mod
        /// manifest's tubeLayout, else null (all defaults). Mirrors ModService.EffectiveTubeLayout.
        /// </summary>
        private ModTubeLayout? EffectiveLayout()
        {
            if (App.Settings?.Current?.TubeLayoutOverridesByMod?.TryGetValue(_modId, out var userLayout) == true
                && userLayout != null)
                return userLayout;

            return ManifestLayout();
        }

        private ModTubeLayout? ManifestLayout() => App.Mods?.ActiveMod?.Manifest?.TubeLayout;

        private void LoadWorkingValues(ModTubeLayout? layout)
        {
            _offsetX = Math.Clamp(layout?.AvatarOffsetX ?? 0, -1000, 1000);
            _detachedOffsetX = Math.Clamp(layout?.AvatarDetachedOffsetX ?? 0, -1000, 1000);
            _scale = Math.Clamp(layout?.AvatarScale ?? 1.0, 0.1, 3.0);
            _offsetY = Math.Clamp(layout?.AvatarOffsetY ?? 0, -500, 500);
            _detachedOffsetY = Math.Clamp(layout?.AvatarDetachedOffsetY ?? 0, -500, 500);
        }

        /// <summary>
        /// Points the sliders at the current mode's offset pair. Suppressed so the ValueChanged
        /// handlers don't write the old mode's numbers back into the new mode's fields.
        /// </summary>
        private void PushSlidersFromWorkingValues()
        {
            _suppressSliderEvents = true;
            try
            {
                SldScale.Value = _scale;
                SldOffsetX.Value = _detachedMode ? _detachedOffsetX : _offsetX;
                SldOffsetY.Value = _detachedMode ? _detachedOffsetY : _offsetY;
            }
            finally
            {
                _suppressSliderEvents = false;
            }
        }

        // ============================================================
        // AVATAR POSES
        // ============================================================

        /// <summary>
        /// Loads the 4 pose PNGs for the selected avatar set through the mod resolver, falling back
        /// to set 1's same pose then to nothing (mirrors AvatarTubeWindow.LoadAvatarPoses).
        /// </summary>
        private void LoadPoses()
        {
            string prefix = _avatarSet == 1 ? "avatar_pose" : $"avatar{_avatarSet}_pose";

            for (int i = 0; i < _poses.Length; i++)
            {
                _poses[i] = TryLoadImage($"{prefix}{i + 1}.png");

                if (_poses[i] == null && _avatarSet > 1)
                    _poses[i] = TryLoadImage($"avatar_pose{i + 1}.png");
            }

            // Land on the first pose that actually loaded so the preview isn't blank.
            for (int i = 0; i < _poses.Length; i++)
            {
                if (_poses[i] != null) { _poseIndex = i; break; }
            }
        }

        private static ImageSource? TryLoadImage(string name)
        {
            var resolved = Services.ModResourceResolver.ResolveImage(name);
            if (resolved != null) return resolved;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri($"pack://application:,,,/Resources/{name}", UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;   // neither mod override nor embedded resource exists
            }
        }

        private void BtnPrevPose_Click(object sender, RoutedEventArgs e)
        {
            _poseIndex = (_poseIndex + _poses.Length - 1) % _poses.Length;
            UpdatePreview();
        }

        private void BtnNextPose_Click(object sender, RoutedEventArgs e)
        {
            _poseIndex = (_poseIndex + 1) % _poses.Length;
            UpdatePreview();
        }

        // ============================================================
        // PREVIEW
        // ============================================================

        /// <summary>
        /// True when the active mod overrides tube.png but not tube2.png — in that case the detached
        /// state uses the mod's tube.png AND the attached margins, exactly like the real tube
        /// (AvatarTubeWindow.ModOverridesAttachedTubeOnly, bug report #172).
        /// </summary>
        private static bool ModOverridesAttachedTubeOnly() =>
            Services.ModResourceResolver.HasModOverride("tube.png")
            && !Services.ModResourceResolver.HasModOverride("tube2.png");

        private void UpdatePreview()
        {
            try
            {
                bool useAttachedLayout = !_detachedMode || ModOverridesAttachedTubeOnly();

                // Tube frame
                PreviewTube.Source = Services.ModResourceResolver.ResolveImage(useAttachedLayout ? "tube.png" : "tube2.png");

                // Avatar pose
                PreviewAvatar.Source = _poses[_poseIndex];

                // Avatar box + glow (portrait mode shrinks the box and drops the pink glow)
                double maxW = _portraitMode ? LegacyAvatarMaxWidth * PortraitSizeScale : LegacyAvatarMaxWidth;
                double maxH = _portraitMode ? LegacyAvatarMaxHeight * PortraitSizeScale : LegacyAvatarMaxHeight;
                PreviewAvatar.MaxWidth = maxW;
                PreviewAvatar.MaxHeight = maxH;
                PreviewAvatar.Effect = _portraitMode ? null : new DropShadowEffect
                {
                    Color = ResolvePinkColor(),
                    BlurRadius = 20,
                    ShadowDepth = 0,
                    Opacity = 0.6
                };

                // Avatar scale
                PreviewAvatar.LayoutTransform = Math.Abs(_scale - 1.0) > 0.001
                    ? new ScaleTransform(_scale, _scale)
                    : null;

                // Margins — copied from AvatarTubeWindow.ApplyTubeLayoutOffsets
                PreviewAvatarBorder.Margin = useAttachedLayout
                    ? new Thickness(5, 100, 126 - _offsetX, 210 + _offsetY)
                    : new Thickness(5, 100, 436 - _detachedOffsetX, 228 + _detachedOffsetY);

                // Per-set border transform — copied from AvatarTubeWindow.ApplyAvatarTransform
                PreviewAvatarBorder.RenderTransform = BuildBorderTransform();

                UpdateReadouts(maxW, maxH);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[TubeFit] Failed to update preview");
            }
        }

        private Transform? BuildBorderTransform()
        {
            if (_portraitMode)
                return new TranslateTransform(PortraitShiftX, -PortraitRaisePx);

            if (_avatarSet > 1)
            {
                var group = new TransformGroup();
                group.Children.Add(new ScaleTransform(1.12, 1.12));
                group.Children.Add(new TranslateTransform(10, 0));
                return group;
            }

            // Locked's set 1 ("The Lure") art reads smaller than the other stages.
            if (App.Mods?.ActiveModId == BuiltInMods.LockedId)
                return new ScaleTransform(1.06, 1.06);

            return null;
        }

        private Color ResolvePinkColor()
        {
            if (TryFindResource("PinkColor") is Color themed) return themed;
            return Color.FromRgb(0xFF, 0x69, 0xB4);
        }

        /// <summary>
        /// Refreshes the slider value texts and the "Image WxH px, shown WxH px" line. The shown
        /// size is the uniform fit of the source pixels into the scaled avatar box.
        /// </summary>
        private void UpdateReadouts(double maxW, double maxH)
        {
            TxtScaleValue.Text = $"×{_scale:0.00}";
            TxtOffsetXValue.Text = $"{(_detachedMode ? _detachedOffsetX : _offsetX)} px";
            TxtOffsetYValue.Text = $"{(_detachedMode ? _detachedOffsetY : _offsetY)} px";
            TxtPose.Text = Loc.GetF("tube_fit_pose", _poseIndex + 1, _poses.Length);

            if (PreviewAvatar.Source is BitmapSource bmp && bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
            {
                double boxW = maxW * _scale;
                double boxH = maxH * _scale;
                double f = Math.Min(boxW / bmp.PixelWidth, boxH / bmp.PixelHeight);
                TxtImageInfo.Text = Loc.GetF("tube_fit_image_info",
                    bmp.PixelWidth, bmp.PixelHeight,
                    (int)Math.Round(bmp.PixelWidth * f), (int)Math.Round(bmp.PixelHeight * f));
            }
            else
            {
                TxtImageInfo.Text = string.Empty;
            }
        }

        // ============================================================
        // CONTROL HANDLERS
        // ============================================================

        private void Mode_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressSliderEvents) return;   // ctor is still wiring the initial state

            _detachedMode = RbDetached.IsChecked == true;
            PushSlidersFromWorkingValues();      // retarget the offset sliders at this mode's pair
            UpdatePreview();
        }

        private void SldScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSliderEvents) return;
            _scale = Math.Clamp(e.NewValue, 0.1, 3.0);
            UpdatePreview();
        }

        private void SldOffsetX_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSliderEvents) return;
            var value = (int)Math.Round(Math.Clamp(e.NewValue, -1000, 1000));
            if (_detachedMode) _detachedOffsetX = value; else _offsetX = value;
            UpdatePreview();
        }

        private void SldOffsetY_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSliderEvents) return;
            var value = (int)Math.Round(Math.Clamp(e.NewValue, -500, 500));
            if (_detachedMode) _detachedOffsetY = value; else _offsetY = value;
            UpdatePreview();
        }

        // ============================================================
        // SAVE / RESET / CANCEL
        // ============================================================

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var settings = App.Settings?.Current;
            if (settings == null) { Close(); return; }

            settings.TubeLayoutOverridesByMod ??= new Dictionary<string, ModTubeLayout>();
            settings.TubeLayoutOverridesByMod[_modId] = new ModTubeLayout
            {
                AvatarOffsetX = _offsetX,
                AvatarDetachedOffsetX = _detachedOffsetX,
                AvatarScale = _scale,
                AvatarOffsetY = _offsetY,
                AvatarDetachedOffsetY = _detachedOffsetY
            };
            // The dictionary was mutated in place, so no INPC auto-save fired — persist explicitly.
            App.Settings?.Save();

            App.Logger?.Information("[TubeFit] Saved tube layout override for mod {ModId} " +
                "(scale {Scale:0.00}, attached {OffX}/{OffY}, detached {DetX}/{DetY})",
                _modId, _scale, _offsetX, _offsetY, _detachedOffsetX, _detachedOffsetY);

            App.AvatarWindow?.RefreshTubeLayout();
            Close();
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            var settings = App.Settings?.Current;
            if (settings?.TubeLayoutOverridesByMod?.Remove(_modId) == true)
                App.Settings?.Save();

            App.Logger?.Information("[TubeFit] Reset tube layout to mod default for mod {ModId}", _modId);

            App.AvatarWindow?.RefreshTubeLayout();

            // Reload the working values from the mod's shipped manifest so the preview follows.
            LoadWorkingValues(ManifestLayout());
            PushSlidersFromWorkingValues();
            UpdatePreview();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }
}
