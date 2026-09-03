using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
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
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/TubeFitDialog.xaml.cs. Deviations:
    ///  - <b>The whole value path is real.</b> <c>ModTubeLayout</c>,
    ///    <c>AppSettings.TubeLayoutOverridesByMod</c>, <c>ModManifest.TubeLayout</c>,
    ///    <c>SelectedAvatarSet</c>, <c>AvatarTubeDetached</c> and <c>BuiltInMods.LockedId</c> are
    ///    all in Core, so load / save / reset persist for real against <see cref="CoreSettings"/>
    ///    and <see cref="CoreMods"/>. The mod's shipped manifest layout is read through
    ///    <c>CoreMods.InstalledMods[ActiveModId].Manifest.TubeLayout</c>, which is what
    ///    <c>ModService.ActiveMod</c> hands back.
    ///  - <b>The avatar art is real.</b> The four poses load through <see cref="CoreModArt"/> -
    ///    the active mod's override if it ships one, else this head's own
    ///    <c>avares://CCP.Avalonia/Resources/avatar*_pose*.png</c> - and are painted into the
    ///    preview Border as a Uniform <see cref="ImageBrush"/>, with the readout reading the
    ///    bitmap's real pixel size. Portrait mode and the tube.png-only fallback come off the same
    ///    seam. Nothing loading falls back to the pose glyph and a nominal
    ///    <see cref="PlaceholderPixelWidth"/>x<see cref="PlaceholderPixelHeight"/>, so the preview
    ///    is never blank.
    ///  - Still head-side, with a note: the tube window's <c>RefreshTubeLayout()</c>. The tube
    ///    frame itself stays the placeholder capsule - that is markup this layer does not own.
    ///  - WPF's <c>LayoutTransform</c> has no Avalonia twin on a plain control, so the avatar scale
    ///    is a <c>RenderTransform</c> about the centre. It does not grow the parent Border, so a
    ///    scaled avatar expands symmetrically rather than upward.
    ///  - <c>DragMove()</c> -> <c>BeginMoveDrag(e)</c>; <c>MouseLeftButtonDown</c> ->
    ///    <c>PointerPressed</c>; <c>RoutedPropertyChangedEventArgs&lt;double&gt;</c> ->
    ///    <c>RangeBaseValueChangedEventArgs</c>; <c>TryFindResource</c> gains an out parameter.
    /// </summary>
    public partial class TubeFitDialog : Window
    {
        // Legacy avatar image box in design units (AvatarTubeWindow.xaml defaults for ImgAvatar).
        private const double LegacyAvatarMaxWidth = 198;
        private const double LegacyAvatarMaxHeight = 306;
        private const double PortraitSizeScale = 0.88;   // matches AvatarTubeWindow.ApplyPortraitChrome
        private const double PortraitRaisePx = 30;
        private const double PortraitShiftX = 10;

        // Nominal source size, used only when no pose bitmap loaded at all - no mod override AND
        // no shipped PNG. With a bitmap the readout asks it for its real pixel size.
        private const double PlaceholderPixelWidth = 512;
        private const double PlaceholderPixelHeight = 768;

        // Where the placeholder tube sits in the 780x1080 canvas, per mode. Chosen so the capsule
        // is centred on the avatar column each margin formula below aims at (329.5 attached,
        // 174.5 detached), which is what tube.png and tube2.png do with their own art.
        private static readonly Thickness AttachedTubeMargin = new Thickness(165, 40, 0, 0);
        private static readonly Thickness DetachedTubeMargin = new Thickness(10, 40, 0, 0);

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
        /// <summary>The four loaded poses; a slot is null when neither the mod nor this head has it.</summary>
        private readonly Bitmap?[] _poses = new Bitmap?[4];
        /// <summary>Drawn in place of a pose that would not load, so the stepper still reads.</summary>
        private static readonly string[] PoseGlyphs = { "🧍", "🙋", "💃", "🧎" };
        /// <summary>The XAML gradient, kept so a null pose can put the placeholder back.</summary>
        private readonly IBrush? _placeholderAvatarBrush;
        private int _poseIndex;

        private readonly RadioButton _rbAttached;
        private readonly RadioButton _rbDetached;
        private readonly Slider _sldScale;
        private readonly Slider _sldOffsetX;
        private readonly Slider _sldOffsetY;
        private readonly Border _previewTube;
        private readonly Border _previewAvatarBorder;
        private readonly Border _previewAvatar;
        private readonly TextBlock _txtTubeName;
        private readonly TextBlock _txtPoseGlyph;
        private readonly TextBlock _txtScaleValue;
        private readonly TextBlock _txtOffsetXValue;
        private readonly TextBlock _txtOffsetYValue;
        private readonly TextBlock _txtPose;
        private readonly TextBlock _txtImageInfo;

        public TubeFitDialog()
        {
            AvaloniaXamlLoader.Load(this);

            _modId = CoreMods.ActiveModId;
            _avatarSet = Math.Max(1, CoreSettings.Current.SelectedAvatarSet);
            // False with no mod layer up, which is the legacy four-pose avatar - exactly what this
            // head draws. A seeded head answers from the active mod's portrait manifest.
            _portraitMode = CoreModArt.HasAvatarPortraits;

            _rbAttached = this.FindControl<RadioButton>("RbAttached")!;
            _rbDetached = this.FindControl<RadioButton>("RbDetached")!;
            _sldScale = this.FindControl<Slider>("SldScale")!;
            _sldOffsetX = this.FindControl<Slider>("SldOffsetX")!;
            _sldOffsetY = this.FindControl<Slider>("SldOffsetY")!;
            _previewTube = this.FindControl<Border>("PreviewTube")!;
            _previewAvatarBorder = this.FindControl<Border>("PreviewAvatarBorder")!;
            _previewAvatar = this.FindControl<Border>("PreviewAvatar")!;
            _txtTubeName = this.FindControl<TextBlock>("TxtTubeName")!;
            _txtPoseGlyph = this.FindControl<TextBlock>("TxtPoseGlyph")!;
            _txtScaleValue = this.FindControl<TextBlock>("TxtScaleValue")!;
            _txtOffsetXValue = this.FindControl<TextBlock>("TxtOffsetXValue")!;
            _txtOffsetYValue = this.FindControl<TextBlock>("TxtOffsetYValue")!;
            _txtPose = this.FindControl<TextBlock>("TxtPose")!;
            _txtImageInfo = this.FindControl<TextBlock>("TxtImageInfo")!;

            // Handlers live here rather than in markup, per the porting convention.
            this.FindControl<Border>("TitleBar")!.PointerPressed += TitleBar_PointerPressed;
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();
            _rbAttached.IsCheckedChanged += Mode_Checked;
            _rbDetached.IsCheckedChanged += Mode_Checked;
            _sldScale.ValueChanged += SldScale_ValueChanged;
            _sldOffsetX.ValueChanged += SldOffsetX_ValueChanged;
            _sldOffsetY.ValueChanged += SldOffsetY_ValueChanged;
            this.FindControl<Button>("BtnPrevPose")!.Click += (_, _) => StepPose(-1);
            this.FindControl<Button>("BtnNextPose")!.Click += (_, _) => StepPose(+1);
            this.FindControl<Button>("BtnSave")!.Click += (_, _) => BtnSave_Click();
            this.FindControl<Button>("BtnReset")!.Click += (_, _) => BtnReset_Click();
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close();

            _placeholderAvatarBrush = _previewAvatar.Background;
            LoadPoses();

            LoadWorkingValues(EffectiveLayout());

            // Start on whatever state the real tube is in, so the preview matches what the user sees.
            _detachedMode = CoreSettings.Current.AvatarTubeDetached;
            _suppressSliderEvents = true;
            if (_detachedMode) _rbDetached.IsChecked = true; else _rbAttached.IsChecked = true;
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
            if (CoreSettings.Current.TubeLayoutOverridesByMod?.TryGetValue(_modId, out var userLayout) == true
                && userLayout != null)
                return userLayout;

            return ManifestLayout();
        }

        /// <summary>The active mod's shipped layout. <c>CoreMods.InstalledMods</c> unseeded is the
        /// built-in CCP default, which is what ModService answers with no mod layer up.</summary>
        private ModTubeLayout? ManifestLayout() =>
            CoreMods.InstalledMods.TryGetValue(_modId, out var pkg) ? pkg?.Manifest?.TubeLayout : null;

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
                _sldScale.Value = _scale;
                _sldOffsetX.Value = _detachedMode ? _detachedOffsetX : _offsetX;
                _sldOffsetY.Value = _detachedMode ? _detachedOffsetY : _offsetY;
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
        /// Loads the 4 pose PNGs for the selected avatar set, falling back to set 1's same pose and
        /// then to nothing - the chain WPF's LoadPoses / AvatarTubeWindow.LoadAvatarPoses walk.
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

            // Land on the first pose that actually loaded, so the preview opens on art.
            for (int i = 0; i < _poses.Length; i++)
            {
                if (_poses[i] != null) { _poseIndex = i; break; }
            }
        }

        /// <summary>
        /// The mod's override first (<see cref="CoreModArt"/>), then this head's own shipped copy
        /// under <c>avares://</c>. Null when neither exists. Never throws: a mod's broken PNG
        /// degrades to the built-in exactly as ModResourceResolver.LoadFrozen does, and a missing
        /// built-in degrades to the glyph.
        ///
        /// ponytail: private because TubeFitDialog is the seam's first consumer on this head. Hoist
        /// it to a head-wide helper when a second view (Flash, BubblePop, Video, Studio, Spiral,
        /// AchievementPopup ...) wants the same two-step.
        /// </summary>
        private static Bitmap? TryLoadImage(string resourceName)
        {
            var overridePath = CoreModArt.OverridePath(resourceName);
            if (overridePath != null)
            {
                try { if (File.Exists(overridePath)) return new Bitmap(overridePath); }
                catch (Exception ex) { Log.Warning(ex, "[TubeFit] mod override {Path} would not load", overridePath); }
            }

            try
            {
                var uri = new Uri($"avares://CCP.Avalonia/Resources/{resourceName}");
                if (!AssetLoader.Exists(uri)) return null;
                using var stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[TubeFit] built-in {Name} would not load", resourceName);
                return null;
            }
        }

        private void StepPose(int delta)
        {
            _poseIndex = (_poseIndex + _poses.Length + delta) % _poses.Length;
            UpdatePreview();
        }

        // ============================================================
        // PREVIEW
        // ============================================================

        /// <summary>
        /// True when the active mod overrides tube.png but not tube2.png — in that case the detached
        /// state uses the mod's tube.png AND the attached margins, exactly like the real tube
        /// (AvatarTubeWindow.ModOverridesAttachedTubeOnly, bug report #172). With no mod layer up
        /// both answers are false, so detached keeps the detached margins - the shipped behaviour.
        /// </summary>
        private static bool ModOverridesAttachedTubeOnly()
            => CoreModArt.HasOverride("tube.png") && !CoreModArt.HasOverride("tube2.png");

        private void UpdatePreview()
        {
          try
          {
            bool useAttachedLayout = !_detachedMode || ModOverridesAttachedTubeOnly();

            // Tube frame
            _previewTube.Margin = useAttachedLayout ? AttachedTubeMargin : DetachedTubeMargin;
            _txtTubeName.Text = useAttachedLayout ? "tube.png" : "tube2.png";

            // Avatar pose. A loaded bitmap paints the box; a null slot keeps the XAML gradient and
            // the glyph, so the preview never goes blank on a head that is missing the art.
            var pose = _poses[_poseIndex];
            _txtPoseGlyph.Text = PoseGlyphs[_poseIndex];
            _txtPoseGlyph.IsVisible = pose == null;
            _previewAvatar.Background = pose != null
                ? new ImageBrush(pose) { Stretch = Stretch.Uniform }
                : _placeholderAvatarBrush;

            // Avatar box + glow (portrait mode shrinks the box and drops the pink glow)
            double maxW = _portraitMode ? LegacyAvatarMaxWidth * PortraitSizeScale : LegacyAvatarMaxWidth;
            double maxH = _portraitMode ? LegacyAvatarMaxHeight * PortraitSizeScale : LegacyAvatarMaxHeight;

            // The Border has no intrinsic size, so Stretch="Uniform" is applied by hand: fit the
            // source into the box and take the result as the drawn size. The ImageBrush is Uniform
            // over exactly that box, so the pose lands where WPF's <Image Stretch="Uniform"> put it.
            double fit = Math.Min(maxW / SourcePixelWidth, maxH / SourcePixelHeight);
            _previewAvatar.Width = SourcePixelWidth * fit;
            _previewAvatar.Height = SourcePixelHeight * fit;

            _previewAvatar.Effect = _portraitMode ? null : new DropShadowEffect
            {
                Color = ResolvePinkColor(),
                BlurRadius = 20,
                OffsetX = 0,
                OffsetY = 0,
                Opacity = 0.6
            };

            // Avatar scale
            _previewAvatar.RenderTransform = Math.Abs(_scale - 1.0) > 0.001
                ? new ScaleTransform(_scale, _scale)
                : null;

            // Margins — copied from AvatarTubeWindow.ApplyTubeLayoutOffsets
            _previewAvatarBorder.Margin = useAttachedLayout
                ? new Thickness(5, 100, 126 - _offsetX, 210 + _offsetY)
                : new Thickness(5, 100, 436 - _detachedOffsetX, 228 + _detachedOffsetY);

            // Per-set border transform — copied from AvatarTubeWindow.ApplyAvatarTransform
            _previewAvatarBorder.RenderTransform = BuildBorderTransform();

            UpdateReadouts(maxW, maxH);
          }
          catch (Exception ex)
          {
            Log.Warning(ex, "[TubeFit] Failed to update preview");
          }
        }

        /// <summary>Current pose's real pixel width, or the nominal size when nothing loaded.</summary>
        private double SourcePixelWidth => _poses[_poseIndex]?.PixelSize.Width ?? PlaceholderPixelWidth;

        /// <summary>Current pose's real pixel height, or the nominal size when nothing loaded.</summary>
        private double SourcePixelHeight => _poses[_poseIndex]?.PixelSize.Height ?? PlaceholderPixelHeight;

        private ITransform? BuildBorderTransform()
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
            if (_modId == BuiltInMods.LockedId)
                return new ScaleTransform(1.06, 1.06);

            return null;
        }

        private Color ResolvePinkColor()
        {
            if (this.TryFindResource("PinkColor", out var found) && found is Color themed) return themed;
            return Color.FromRgb(0xFF, 0x69, 0xB4);
        }

        /// <summary>
        /// Refreshes the slider value texts and the "Image WxH px, shown WxH px" line. The shown
        /// size is the uniform fit of the source pixels into the scaled avatar box.
        /// </summary>
        private void UpdateReadouts(double maxW, double maxH)
        {
            _txtScaleValue.Text = $"×{_scale:0.00}";
            _txtOffsetXValue.Text = $"{(_detachedMode ? _detachedOffsetX : _offsetX)} px";
            _txtOffsetYValue.Text = $"{(_detachedMode ? _detachedOffsetY : _offsetY)} px";
            _txtPose.Text = Loc.GetF("tube_fit_pose", _poseIndex + 1, _poses.Length);

            double boxW = maxW * _scale;
            double boxH = maxH * _scale;
            double f = Math.Min(boxW / SourcePixelWidth, boxH / SourcePixelHeight);
            _txtImageInfo.Text = Loc.GetF("tube_fit_image_info",
                (int)SourcePixelWidth, (int)SourcePixelHeight,
                (int)Math.Round(SourcePixelWidth * f), (int)Math.Round(SourcePixelHeight * f));
        }

        // ============================================================
        // CONTROL HANDLERS
        // ============================================================

        private void Mode_Checked(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_suppressSliderEvents) return;   // ctor is still wiring the initial state

            _detachedMode = _rbDetached.IsChecked == true;
            PushSlidersFromWorkingValues();      // retarget the offset sliders at this mode's pair
            UpdatePreview();
        }

        private void SldScale_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_suppressSliderEvents) return;
            _scale = Math.Clamp(e.NewValue, 0.1, 3.0);
            UpdatePreview();
        }

        private void SldOffsetX_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_suppressSliderEvents) return;
            var value = (int)Math.Round(Math.Clamp(e.NewValue, -1000, 1000));
            if (_detachedMode) _detachedOffsetX = value; else _offsetX = value;
            UpdatePreview();
        }

        private void SldOffsetY_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_suppressSliderEvents) return;
            var value = (int)Math.Round(Math.Clamp(e.NewValue, -500, 500));
            if (_detachedMode) _detachedOffsetY = value; else _offsetY = value;
            UpdatePreview();
        }

        // ============================================================
        // SAVE / RESET / CANCEL
        // ============================================================

        private void BtnSave_Click()
        {
            var settings = CoreSettings.Current;

            settings.TubeLayoutOverridesByMod ??= new Dictionary<string, ModTubeLayout>();
            settings.TubeLayoutOverridesByMod[_modId] = new ModTubeLayout
            {
                AvatarOffsetX = _offsetX,
                AvatarDetachedOffsetX = _detachedOffsetX,
                AvatarScale = _scale,
                AvatarOffsetY = _offsetY,
                AvatarDetachedOffsetY = _detachedOffsetY
            };
            // The dictionary was mutated in place, so no INPC auto-save fired - persist explicitly.
            CoreSettings.Save();

            Log.Information("[TubeFit] Saved tube layout override for mod {ModId} " +
                "(scale {Scale:0.00}, attached {OffX}/{OffY}, detached {DetX}/{DetY})",
                _modId, _scale, _offsetX, _offsetY, _detachedOffsetX, _detachedOffsetY);

            // ponytail: needs the tube window's RefreshTubeLayout() to re-read the override live.
            // CCP.Avalonia/Views/AvatarTube/AvatarTubeWindow.axaml.cs has no such method yet, so
            // the saved layout is picked up on the tube's next construction rather than at once.
            Close();
        }

        private void BtnReset_Click()
        {
            if (CoreSettings.Current.TubeLayoutOverridesByMod?.Remove(_modId) == true)
                CoreSettings.Save();

            Log.Information("[TubeFit] Reset tube layout to mod default for mod {ModId}", _modId);

            // ponytail: needs RefreshTubeLayout() - see BtnSave_Click.

            // Reload the working values from the mod's shipped manifest so the preview follows.
            LoadWorkingValues(ManifestLayout());
            PushSlidersFromWorkingValues();
            UpdatePreview();
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }
    }
}
