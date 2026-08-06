using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The Wardrobe editor: drag / resize / rotate / flip the equipped decoration and charms on a
    /// to-scale mock of the hero card. Edits ONLY the transform fields of the draft the Customize
    /// dialog handed in (same instance), snapshotting them on open so Cancel really cancels.
    ///
    /// Geometry contract: every constant the stage uses comes from <see cref="WardrobeCatalog"/>
    /// (canvas ratio, charm base fraction, default anchors) and every value it writes is a
    /// normalized fraction, so the live card - at any width, on any viewer's machine - rebuilds
    /// exactly this composition.
    /// </summary>
    public partial class WardrobeEditorDialog : Window
    {
        private const double AvatarSize = 104;
        private const double AvatarLeft = 28;

        private static double DecoCanvas => AvatarSize / WardrobeCatalog.AvatarCircleRatio;

        private sealed class Sprite
        {
            public string Key = string.Empty;          // "deco" or the charm id
            public bool IsDeco;
            public int CharmSlot;                      // default-anchor index for charms
            public string Name = string.Empty;
            public Image Image = null!;
            public Border Chip = null!;
        }

        private readonly ProfileCosmetics _draft;
        private readonly CosmeticTransform? _snapshotDeco;
        private readonly Dictionary<string, CosmeticTransform>? _snapshotCharms;

        private readonly List<Sprite> _sprites = new();
        private Sprite? _selected;
        private bool _dragging;
        private Point _dragStartMouse;
        private (double X, double Y) _dragStartValue;
        private bool _updatingUi;

        private static readonly Brush ChipIdle = Frozen("#26FFFFFF");
        private static readonly Brush ChipIdleBorder = Frozen("#33FFFFFF");
        private static readonly Brush ChipOn = Frozen("#335EC8F2");
        private static readonly Brush ChipOnBorder = Frozen("#5EC8F2");

        public WardrobeEditorDialog(ProfileCosmetics draft, ImageSource? avatar)
        {
            InitializeComponent();

            _draft = draft ?? new ProfileCosmetics();
            _snapshotDeco = _draft.DecoTransform?.Clone();
            _snapshotCharms = _draft.CharmTransforms?.ToDictionary(
                kv => kv.Key, kv => kv.Value.Clone(), StringComparer.Ordinal);

            BuildStageBackdrop(avatar);
            BuildSprites();

            if (_sprites.Count == 0)
            {
                TxtNothingEquipped.Visibility = Visibility.Visible;
                StageFrame.Visibility = Visibility.Collapsed;
            }
            else
            {
                SelectSprite(_sprites[0]);
            }

            LayoutSprites();
        }

        // ============================== build ==============================

        private void BuildStageBackdrop(ImageSource? avatar)
        {
            try
            {
                var banner = CosmeticsCatalog.GetBannerImage(_draft.BannerId);
                if (banner != null)
                {
                    var brush = new ImageBrush(banner)
                    {
                        Stretch = Stretch.UniformToFill,
                        AlignmentX = AlignmentX.Center,
                        AlignmentY = AlignmentY.Center
                    };
                    if (brush.CanFreeze) brush.Freeze();
                    StageBanner.Background = brush;
                }

                // The avatar bubble: same 104px circle + pink ring as the hero.
                var avatarBrush = avatar != null
                    ? (Brush)new ImageBrush(avatar) { Stretch = Stretch.UniformToFill }
                    : new LinearGradientBrush(
                        (Color)ColorConverter.ConvertFromString("#FF69B4"),
                        (Color)ColorConverter.ConvertFromString("#B478FF"), 45);
                if (avatarBrush.CanFreeze) avatarBrush.Freeze();

                var bubble = new Border
                {
                    Width = AvatarSize,
                    Height = AvatarSize,
                    CornerRadius = new CornerRadius(AvatarSize / 2),
                    BorderBrush = Frozen("#FF69B4"),
                    BorderThickness = new Thickness(3),
                    Background = avatarBrush,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(bubble, AvatarLeft);
                Canvas.SetTop(bubble, (240 - AvatarSize) / 2);
                Stage.Children.Add(bubble);
            }
            catch (Exception ex) { App.Logger?.Debug("WardrobeEditor backdrop: {E}", ex.Message); }
        }

        private void BuildSprites()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_draft.AvatarDeco))
                {
                    var art = WardrobeCatalog.GetImage(_draft.AvatarDeco);
                    var item = WardrobeCatalog.Find(_draft.AvatarDeco);
                    if (art != null)
                        AddSprite("deco", isDeco: true, 0,
                            $"{Loc.Get("wardrobe_editor_decoration")} · {item?.Name ?? _draft.AvatarDeco}", art);
                }

                for (var i = 0; i < _draft.Charms.Count && i < ProfileCosmetics.MaxCharms; i++)
                {
                    var id = _draft.Charms[i];
                    var art = WardrobeCatalog.GetImage(id);
                    var item = WardrobeCatalog.Find(id);
                    if (art != null)
                        AddSprite(id, isDeco: false, i, item?.Name ?? id, art);
                }
            }
            catch (Exception ex) { App.Logger?.Debug("WardrobeEditor sprites: {E}", ex.Message); }
        }

        private void AddSprite(string key, bool isDeco, int charmSlot, string name, ImageSource art)
        {
            var image = new Image
            {
                Source = art,
                Stretch = Stretch.Uniform,
                Cursor = Cursors.SizeAll,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            var sprite = new Sprite { Key = key, IsDeco = isDeco, CharmSlot = charmSlot, Name = name, Image = image };
            image.MouseLeftButtonDown += (_, e) => BeginDrag(sprite, e);
            Stage.Children.Add(image);

            var chip = new Border
            {
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(10, 4, 10, 4),
                CornerRadius = new CornerRadius(11),
                Background = ChipIdle,
                BorderBrush = ChipIdleBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = name,                     // registry names are English proper nouns
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold
                }
            };
            chip.MouseLeftButtonUp += (_, _) => SelectSprite(sprite);
            sprite.Chip = chip;
            ItemChips.Children.Add(chip);

            _sprites.Add(sprite);
        }

        // ============================== transforms ==============================

        private CosmeticTransform EnsureTransform(Sprite sprite)
        {
            if (sprite.IsDeco)
                return _draft.DecoTransform ??= new CosmeticTransform();

            _draft.CharmTransforms ??= new Dictionary<string, CosmeticTransform>(StringComparer.Ordinal);
            if (!_draft.CharmTransforms.TryGetValue(sprite.Key, out var t))
            {
                var anchors = WardrobeCatalog.DefaultCharmAnchors;
                var anchor = sprite.CharmSlot < anchors.Count ? anchors[sprite.CharmSlot] : anchors[0];
                t = new CosmeticTransform { X = anchor.X, Y = anchor.Y, Scale = 0.8 };
                _draft.CharmTransforms[sprite.Key] = t;
            }
            return t;
        }

        private CosmeticTransform? PeekTransform(Sprite sprite)
        {
            if (sprite.IsDeco) return _draft.DecoTransform;
            return _draft.CharmTransforms != null && _draft.CharmTransforms.TryGetValue(sprite.Key, out var t)
                ? t : null;
        }

        /// <summary>
        /// Places every sprite from its transform - the same math the live renderer runs, with the
        /// stage's fixed 640x240 standing in for the card.
        /// </summary>
        private void LayoutSprites()
        {
            const double stageW = 640, stageH = 240;

            foreach (var sprite in _sprites)
            {
                var t = PeekTransform(sprite);

                if (sprite.IsDeco)
                {
                    var canvas = DecoCanvas;
                    sprite.Image.Width = canvas;
                    sprite.Image.Height = canvas;
                    var left = AvatarLeft + AvatarSize / 2 - canvas / 2;
                    var top = stageH / 2 - canvas / 2;
                    Canvas.SetLeft(sprite.Image, left);
                    Canvas.SetTop(sprite.Image, top);

                    // Mirrors AdornedAvatar.ApplyDecorationTransform exactly.
                    if (t != null)
                    {
                        var group = new TransformGroup();
                        group.Children.Add(new ScaleTransform(t.Flip ? -t.Scale : t.Scale, t.Scale));
                        group.Children.Add(new RotateTransform(t.Rotation));
                        group.Children.Add(new TranslateTransform(t.X * canvas, t.Y * canvas));
                        sprite.Image.RenderTransform = group;
                    }
                    else
                    {
                        sprite.Image.RenderTransform = null;
                    }
                }
                else
                {
                    // Mirrors MainWindow.LayoutProfileCharms exactly.
                    var anchors = WardrobeCatalog.DefaultCharmAnchors;
                    var anchor = sprite.CharmSlot < anchors.Count ? anchors[sprite.CharmSlot] : anchors[0];
                    var cx = (t?.X ?? anchor.X) * stageW;
                    var cy = (t?.Y ?? anchor.Y) * stageH;
                    var scale = t?.Scale ?? 0.8;
                    var size = Math.Max(12, WardrobeCatalog.CharmBaseHeightFraction * stageH * scale);

                    sprite.Image.Width = size;
                    sprite.Image.Height = size;
                    Canvas.SetLeft(sprite.Image, cx - size / 2);
                    Canvas.SetTop(sprite.Image, cy - size / 2);

                    if (t != null && (t.Flip || Math.Abs(t.Rotation) > 0.05))
                    {
                        var group = new TransformGroup();
                        group.Children.Add(new ScaleTransform(t.Flip ? -1 : 1, 1));
                        group.Children.Add(new RotateTransform(t.Rotation));
                        sprite.Image.RenderTransform = group;
                    }
                    else
                    {
                        sprite.Image.RenderTransform = null;
                    }
                }
            }
        }

        // ============================== selection ==============================

        private void SelectSprite(Sprite sprite)
        {
            _selected = sprite;

            foreach (var s in _sprites)
            {
                var on = ReferenceEquals(s, sprite);
                s.Image.Effect = on
                    ? new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = (Color)ColorConverter.ConvertFromString("#5EC8F2"),
                        BlurRadius = 18,
                        ShadowDepth = 0,
                        Opacity = 0.9
                    }
                    : null;
                s.Chip.Background = on ? ChipOn : ChipIdle;
                s.Chip.BorderBrush = on ? ChipOnBorder : ChipIdleBorder;
            }

            RefreshControls();
        }

        private void RefreshControls()
        {
            _updatingUi = true;
            try
            {
                ControlsRow.IsEnabled = _selected != null;
                var t = _selected != null ? PeekTransform(_selected) : null;
                ScaleSlider.Value = t?.Scale ?? (_selected?.IsDeco == true ? 1.0 : 0.8);
                RotationSlider.Value = t?.Rotation ?? 0;
                ChkFlip.IsChecked = t?.Flip ?? false;
            }
            finally { _updatingUi = false; }
        }

        // ============================== dragging ==============================

        private void BeginDrag(Sprite sprite, MouseButtonEventArgs e)
        {
            SelectSprite(sprite);

            var t = EnsureTransform(sprite);
            _dragging = true;
            _dragStartMouse = e.GetPosition(Stage);
            _dragStartValue = (t.X, t.Y);
            Stage.CaptureMouse();
            e.Handled = true;
        }

        private void Stage_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging || _selected == null || e.LeftButton != MouseButtonState.Pressed) return;

            var t = EnsureTransform(_selected);
            var pos = e.GetPosition(Stage);
            var dx = pos.X - _dragStartMouse.X;
            var dy = pos.Y - _dragStartMouse.Y;

            if (_selected.IsDeco)
            {
                t.X = Math.Clamp(_dragStartValue.X + dx / DecoCanvas, -0.75, 0.75);
                t.Y = Math.Clamp(_dragStartValue.Y + dy / DecoCanvas, -0.75, 0.75);
            }
            else
            {
                t.X = Math.Clamp(_dragStartValue.X + dx / 640d, 0, 1);
                t.Y = Math.Clamp(_dragStartValue.Y + dy / 240d, 0, 1);
            }

            LayoutSprites();
        }

        private void Stage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            Stage.ReleaseMouseCapture();
        }

        private void Stage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_selected == null) return;
            var t = EnsureTransform(_selected);
            t.Scale = Math.Clamp(t.Scale + (e.Delta > 0 ? 0.05 : -0.05), 0.3, 3.0);
            LayoutSprites();
            RefreshControls();
            e.Handled = true;
        }

        // ============================== control row ==============================

        private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingUi || _selected == null) return;
            EnsureTransform(_selected).Scale = Math.Clamp(e.NewValue, 0.3, 3.0);
            LayoutSprites();
        }

        private void RotationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingUi || _selected == null) return;
            EnsureTransform(_selected).Rotation = Math.Clamp(e.NewValue, -180, 180);
            LayoutSprites();
        }

        private void ChkFlip_Changed(object sender, RoutedEventArgs e)
        {
            if (_updatingUi || _selected == null) return;
            EnsureTransform(_selected).Flip = ChkFlip.IsChecked == true;
            LayoutSprites();
        }

        private void BtnResetItem_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;

            if (_selected.IsDeco) _draft.DecoTransform = null;
            else _draft.CharmTransforms?.Remove(_selected.Key);

            LayoutSprites();
            RefreshControls();
        }

        // ============================== footer ==============================

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            // Same draft instance the Customize dialog holds - put the transforms back the way
            // they were when this editor opened. Item choices were never ours to touch.
            _draft.DecoTransform = _snapshotDeco;
            _draft.CharmTransforms = _snapshotCharms;
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private static Brush Frozen(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
    }
}
