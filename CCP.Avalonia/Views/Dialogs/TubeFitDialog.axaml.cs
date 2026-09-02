using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// "Tube Fit" editor — a WYSIWYG preview of how the ACTIVE mod's avatar PNG sits inside the
    /// avatar tube, with an Attached/Detached switch and scale/offset sliders. The result is saved
    /// as a per-mod user override (never into the mod itself) and the live tube window is
    /// hot-refreshed.
    ///
    /// The preview replicates the real tube's geometry: the tube window is a 780x1020 rect holding a
    /// Uniform Viewbox over a 780x1080 design canvas, so content renders at 1020/1080 and anything
    /// outside the window rect is clipped. The nested Viewbox/Grid pair in the XAML mirrors that,
    /// and the margin formulas below are copied from AvatarTubeWindow.ApplyTubeLayoutOffsets.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/TubeFitDialog.xaml.cs. Deviations:
    ///  - <c>App.Settings</c> / <c>App.Mods</c> / <c>App.AvatarWindow</c> / <c>App.Logger</c>,
    ///    <c>Services.ModResourceResolver</c>, <c>Services.AvatarPortraitLoader</c> and the
    ///    <c>ModTubeLayout</c> model all live in the WPF head, so the load / save / reset paths are
    ///    stubs and the five working values start at their defaults. Everything between them - the
    ///    sliders, the mode switch, the margin maths, the transforms and the readouts - is the real
    ///    ported logic and runs.
    ///  - The two <c>Image</c> elements are placeholder Borders (the head ships no Resources/ PNGs);
    ///    the avatar box is sized from <see cref="PlaceholderPixelWidth"/>/<see cref="PlaceholderPixelHeight"/>
    ///    by the same uniform-fit formula the real readout uses.
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

        // ponytail: nominal source size of the placeholder avatar, standing in for the real pose
        // PNG's PixelWidth/PixelHeight. Delete both when ModResourceResolver moves to Core and the
        // readout can ask the bitmap.
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

        private readonly int _avatarSet;
        private readonly bool _portraitMode;
        private readonly string[] _poses = { "🧍", "🙋", "💃", "🧎" };
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

            // ponytail: needs App.Mods / App.Settings / Services.AvatarPortraitLoader, wired when
            // they move to Core. Defaults are the first-run state: built-in mod, avatar set 1, no
            // portrait manifest, attached tube.
            _avatarSet = 1;
            _portraitMode = false;

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

            LoadWorkingValues();

            // Start on whatever state the real tube is in, so the preview matches what the user sees.
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
        /// ponytail: needs AppSettings.TubeLayoutOverridesByMod and ModManifest.TubeLayout (both
        /// WPF-head models), wired when they move to Core. The clamps are the original's, so the
        /// call site does not change when the real layout is handed in.
        /// </summary>
        private void LoadWorkingValues()
        {
            _offsetX = Math.Clamp(0, -1000, 1000);
            _detachedOffsetX = Math.Clamp(0, -1000, 1000);
            _scale = Math.Clamp(1.0, 0.1, 3.0);
            _offsetY = Math.Clamp(0, -500, 500);
            _detachedOffsetY = Math.Clamp(0, -500, 500);
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

        // ponytail: LoadPoses() needs Services.ModResourceResolver.ResolveImage plus the embedded
        // Resources/avatar*_pose*.png; wired when the resolver moves to Core. Until then the four
        // slots are placeholder glyphs, so the stepper and the "Pose n/4" readout still work.
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
        /// (AvatarTubeWindow.ModOverridesAttachedTubeOnly, bug report #172).
        /// ponytail: needs Services.ModResourceResolver.HasModOverride, wired when it moves to Core.
        /// </summary>
        private static bool ModOverridesAttachedTubeOnly() => false;

        private void UpdatePreview()
        {
            bool useAttachedLayout = !_detachedMode || ModOverridesAttachedTubeOnly();

            // Tube frame
            _previewTube.Margin = useAttachedLayout ? AttachedTubeMargin : DetachedTubeMargin;
            _txtTubeName.Text = useAttachedLayout ? "tube.png" : "tube2.png";

            // Avatar pose
            _txtPoseGlyph.Text = _poses[_poseIndex];

            // Avatar box + glow (portrait mode shrinks the box and drops the pink glow)
            double maxW = _portraitMode ? LegacyAvatarMaxWidth * PortraitSizeScale : LegacyAvatarMaxWidth;
            double maxH = _portraitMode ? LegacyAvatarMaxHeight * PortraitSizeScale : LegacyAvatarMaxHeight;

            // The placeholder has no intrinsic size, so Stretch="Uniform" is applied by hand: fit
            // the nominal source into the box and take the result as the drawn size.
            double fit = Math.Min(maxW / PlaceholderPixelWidth, maxH / PlaceholderPixelHeight);
            _previewAvatar.Width = PlaceholderPixelWidth * fit;
            _previewAvatar.Height = PlaceholderPixelHeight * fit;

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
            // ponytail: needs App.Mods.ActiveModId == BuiltInMods.LockedId, wired when ModService
            // moves to Core.
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
            double f = Math.Min(boxW / PlaceholderPixelWidth, boxH / PlaceholderPixelHeight);
            _txtImageInfo.Text = Loc.GetF("tube_fit_image_info",
                (int)PlaceholderPixelWidth, (int)PlaceholderPixelHeight,
                (int)Math.Round(PlaceholderPixelWidth * f), (int)Math.Round(PlaceholderPixelHeight * f));
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
            // ponytail: needs AppSettings.TubeLayoutOverridesByMod + App.Settings.Save() +
            // App.AvatarWindow.RefreshTubeLayout(), all WPF-head; wired when they move to Core.
            // The five working values below are what gets written.
            Close();
        }

        private void BtnReset_Click()
        {
            // ponytail: needs App.Settings (drop the override, save) and
            // App.AvatarWindow.RefreshTubeLayout(); wired when they move to Core. The view-local
            // half - reload the working values from the mod's shipped manifest and follow with the
            // preview - is real and runs.
            LoadWorkingValues();
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
