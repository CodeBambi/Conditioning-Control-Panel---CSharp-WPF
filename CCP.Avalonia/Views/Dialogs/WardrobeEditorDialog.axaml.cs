using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// The Wardrobe editor: drag / resize / rotate / flip the equipped decoration and charms on a
    /// to-scale mock of the hero card. Edits ONLY the transform fields of the draft the Customize
    /// dialog handed in (same instance), snapshotting them on open so Cancel really cancels.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/WardrobeEditorDialog.xaml.cs. Deviations:
    ///  - Geometry is Core <c>WardrobeStageGeometry</c>; art is Core <c>WardrobeCatalog</c> decoded
    ///    by <c>Helpers.ModArt.Wardrobe</c> (WPF WardrobeArt).
    ///  - Mouse events -> Pointer events; CaptureMouse -> e.Pointer.Capture(Stage).
    ///  - <c>DialogResult = x; Close()</c> -> <c>Close(x)</c>.
    /// </summary>
    public partial class WardrobeEditorDialog : Window
    {
        /// <summary>The stage box the card mock is fitted into, at the card's own aspect ratio.</summary>
        private const double StageMaxWidth = 660;
        private const double StageMaxHeight = 250;

        private static readonly IReadOnlyList<(double X, double Y)> DefaultCharmAnchors = WardrobeCatalog.DefaultCharmAnchors;

        /// <summary>The live card, scaled down. Every number on the stage comes from this.</summary>
        private readonly WardrobeStageGeometry.Stage _stage;

        private double DecoCanvas => _stage.AvatarSize / WardrobeCatalog.AvatarCircleRatio;

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

        private static readonly IBrush ChipIdle = Brush.Parse("#26FFFFFF");
        private static readonly IBrush ChipIdleBorder = Brush.Parse("#33FFFFFF");
        private static readonly IBrush ChipOn = Brush.Parse("#335EC8F2");
        private static readonly IBrush ChipOnBorder = Brush.Parse("#5EC8F2");

        private readonly Canvas _stageCanvas;
        private readonly Border _stageFrame;
        private readonly WrapPanel _itemChips;
        private readonly Grid _controlsRow;
        private readonly Slider _scaleSlider;
        private readonly Slider _rotationSlider;
        private readonly CheckBox _chkFlip;

        /// <summary>Render/design constructor: a decoration and two charms so --render-view has
        /// something to lay out.</summary>
        public WardrobeEditorDialog() : this(new ProfileCosmetics
        {
            AvatarDeco = "bambi_silk_bow",
            Charms = { "bambi_plush_bunny", "bambi_bubble_wand" },
            CharmTransforms = new Dictionary<string, CosmeticTransform>(StringComparer.Ordinal)
            {
                ["bambi_bubble_wand"] = new CosmeticTransform { X = 0.75, Y = 0.5, Scale = 1.2, Rotation = 20, Flip = true }
            }
        }, null) { }

        /// <param name="cardWidth">The live hero card's width, or 0 when it has not been
        /// measured yet - the stage falls back to a typical windowed hero in that case.</param>
        /// <param name="cardHeight">The live hero card's height, same rule.</param>
        public WardrobeEditorDialog(ProfileCosmetics draft, IImageBrushSource? avatar,
                                    double cardWidth = 0, double cardHeight = 0)
        {
            AvaloniaXamlLoader.Load(this);

            _stageCanvas = this.FindControl<Canvas>("Stage")!;
            _stageFrame = this.FindControl<Border>("StageFrame")!;
            _itemChips = this.FindControl<WrapPanel>("ItemChips")!;
            _controlsRow = this.FindControl<Grid>("ControlsRow")!;
            _scaleSlider = this.FindControl<Slider>("ScaleSlider")!;
            _rotationSlider = this.FindControl<Slider>("RotationSlider")!;
            _chkFlip = this.FindControl<CheckBox>("ChkFlip")!;

            _stageCanvas.PointerMoved += Stage_PointerMoved;
            _stageCanvas.PointerReleased += Stage_PointerReleased;
            _stageCanvas.PointerWheelChanged += Stage_PointerWheelChanged;
            _scaleSlider.ValueChanged += ScaleSlider_ValueChanged;
            _rotationSlider.ValueChanged += RotationSlider_ValueChanged;
            _chkFlip.IsCheckedChanged += (_, _) => ChkFlip_Changed();
            this.FindControl<Button>("BtnResetItem")!.Click += (_, _) => BtnResetItem_Click();
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => BtnCancel_Click();
            this.FindControl<Button>("BtnSave")!.Click += (_, _) => Close(true);

            _stage = WardrobeStageGeometry.ForCard(cardWidth, cardHeight, StageMaxWidth, StageMaxHeight);
            _stageFrame.Width = _stage.Width;
            _stageFrame.Height = _stage.Height;

            _draft = draft ?? new ProfileCosmetics();
            _snapshotDeco = _draft.DecoTransform?.Clone();
            _snapshotCharms = _draft.CharmTransforms?.ToDictionary(
                kv => kv.Key, kv => kv.Value.Clone(), StringComparer.Ordinal);

            BuildStageBackdrop(avatar);
            BuildSprites();

            if (_sprites.Count == 0)
            {
                this.FindControl<TextBlock>("TxtNothingEquipped")!.IsVisible = true;
                _stageFrame.IsVisible = false;
            }
            else
            {
                SelectSprite(_sprites[0]);
            }

            LayoutSprites();
        }

        // ============================== build ==============================

        private void BuildStageBackdrop(IImageBrushSource? avatar)
        {
            // ponytail: needs CosmeticsCatalog.GetBannerImage (WPF head) for the banner; wired when
            // the catalogue moves to Core. The default gradient in the XAML shows through meanwhile.

            // The avatar bubble: the hero's 104px circle + pink ring, at stage scale.
            IBrush avatarBrush = avatar != null
                ? new ImageBrush(avatar) { Stretch = Stretch.UniformToFill }
                : new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#FF69B4"), 0), new GradientStop(Color.Parse("#B478FF"), 1) }
                };

            var bubble = new Border
            {
                Width = _stage.AvatarSize,
                Height = _stage.AvatarSize,
                CornerRadius = new CornerRadius(_stage.AvatarSize / 2),
                BorderBrush = Brush.Parse("#FF69B4"),
                BorderThickness = new Thickness(Math.Max(1, 3 * _stage.Scale)),
                Background = avatarBrush,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(bubble, _stage.AvatarLeft);
            Canvas.SetTop(bubble, _stage.AvatarTop);
            _stageCanvas.Children.Add(bubble);
        }

        private void BuildSprites()
        {
            // WPF BuildSprites: an id whose art is missing gets no sprite at all.
            if (!string.IsNullOrWhiteSpace(_draft.AvatarDeco) && Helpers.ModArt.Wardrobe(_draft.AvatarDeco) is { } decoArt)
                AddSprite("deco", isDeco: true, 0,
                    $"{Loc.Get("wardrobe_editor_decoration")} · {WardrobeCatalog.Find(_draft.AvatarDeco)?.Name ?? _draft.AvatarDeco}", decoArt);

            for (var i = 0; i < _draft.Charms.Count && i < ProfileCosmetics.MaxCharms; i++)
            {
                var id = _draft.Charms[i];
                if (Helpers.ModArt.Wardrobe(id) is { } art)
                    AddSprite(id, isDeco: false, i, WardrobeCatalog.Find(id)?.Name ?? id, art);
            }
        }

        private void AddSprite(string key, bool isDeco, int charmSlot, string name, IImage art)
        {
            var image = new Image
            {
                Source = art,
                Stretch = Stretch.Uniform,
                Cursor = new Cursor(StandardCursorType.SizeAll),
                RenderTransformOrigin = RelativePoint.Center
            };

            var sprite = new Sprite { Key = key, IsDeco = isDeco, CharmSlot = charmSlot, Name = name, Image = image };
            image.PointerPressed += (_, e) => BeginDrag(sprite, e);
            _stageCanvas.Children.Add(image);

            var chip = new Border
            {
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(10, 4, 10, 4),
                CornerRadius = new CornerRadius(11),
                Background = ChipIdle,
                BorderBrush = ChipIdleBorder,
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock
                {
                    Text = name,                     // registry names are English proper nouns
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold
                }
            };
            chip.PointerReleased += (_, _) => SelectSprite(sprite);
            sprite.Chip = chip;
            _itemChips.Children.Add(chip);

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
                var anchor = sprite.CharmSlot < DefaultCharmAnchors.Count ? DefaultCharmAnchors[sprite.CharmSlot] : DefaultCharmAnchors[0];
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
        /// Places every sprite from its transform - the same math the live renderer runs.
        /// </summary>
        private void LayoutSprites()
        {
            var stageW = _stage.Width;
            var stageH = _stage.Height;

            foreach (var sprite in _sprites)
            {
                var t = PeekTransform(sprite);

                if (sprite.IsDeco)
                {
                    var (left, top, canvas) = WardrobeStageGeometry.DecorationRect(_stage.AvatarSize, _stage.AvatarLeft, _stage.AvatarTop);
                    sprite.Image.Width = canvas;
                    sprite.Image.Height = canvas;
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
                    var anchor = sprite.CharmSlot < DefaultCharmAnchors.Count ? DefaultCharmAnchors[sprite.CharmSlot] : DefaultCharmAnchors[0];
                    var (left, top, size) = WardrobeStageGeometry.CharmRect(stageW, stageH, t?.X ?? anchor.X, t?.Y ?? anchor.Y, t?.Scale ?? 0.8);

                    sprite.Image.Width = size;
                    sprite.Image.Height = size;
                    Canvas.SetLeft(sprite.Image, left);
                    Canvas.SetTop(sprite.Image, top);

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
                    ? new DropShadowEffect { Color = Color.Parse("#5EC8F2"), BlurRadius = 18, OffsetX = 0, OffsetY = 0, Opacity = 0.9 }
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
                _controlsRow.IsEnabled = _selected != null;
                var t = _selected != null ? PeekTransform(_selected) : null;
                _scaleSlider.Value = t?.Scale ?? (_selected?.IsDeco == true ? 1.0 : 0.8);
                _rotationSlider.Value = t?.Rotation ?? 0;
                _chkFlip.IsChecked = t?.Flip ?? false;
            }
            finally { _updatingUi = false; }
        }

        // ============================== dragging ==============================

        private void BeginDrag(Sprite sprite, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(_stageCanvas).Properties.IsLeftButtonPressed) return;
            SelectSprite(sprite);

            var t = EnsureTransform(sprite);
            _dragging = true;
            _dragStartMouse = e.GetPosition(_stageCanvas);
            _dragStartValue = (t.X, t.Y);
            e.Pointer.Capture(_stageCanvas);
            e.Handled = true;
        }

        private void Stage_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_dragging || _selected == null || !e.GetCurrentPoint(_stageCanvas).Properties.IsLeftButtonPressed) return;

            var t = EnsureTransform(_selected);
            var pos = e.GetPosition(_stageCanvas);
            var dx = pos.X - _dragStartMouse.X;
            var dy = pos.Y - _dragStartMouse.Y;

            if (_selected.IsDeco)
            {
                t.X = Math.Clamp(_dragStartValue.X + dx / DecoCanvas, -0.75, 0.75);
                t.Y = Math.Clamp(_dragStartValue.Y + dy / DecoCanvas, -0.75, 0.75);
            }
            else
            {
                t.X = Math.Clamp(_dragStartValue.X + dx / _stage.Width, 0, 1);
                t.Y = Math.Clamp(_dragStartValue.Y + dy / _stage.Height, 0, 1);
            }

            LayoutSprites();
        }

        private void Stage_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            e.Pointer.Capture(null);
        }

        private void Stage_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (_selected == null) return;
            var t = EnsureTransform(_selected);
            t.Scale = Math.Clamp(t.Scale + (e.Delta.Y > 0 ? 0.05 : -0.05), 0.3, 3.0);
            LayoutSprites();
            RefreshControls();
            e.Handled = true;
        }

        // ============================== control row ==============================

        private void ScaleSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_updatingUi || _selected == null) return;
            EnsureTransform(_selected).Scale = Math.Clamp(e.NewValue, 0.3, 3.0);
            LayoutSprites();
        }

        private void RotationSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_updatingUi || _selected == null) return;
            EnsureTransform(_selected).Rotation = Math.Clamp(e.NewValue, -180, 180);
            LayoutSprites();
        }

        private void ChkFlip_Changed()
        {
            if (_updatingUi || _selected == null) return;
            EnsureTransform(_selected).Flip = _chkFlip.IsChecked == true;
            LayoutSprites();
        }

        private void BtnResetItem_Click()
        {
            if (_selected == null) return;

            if (_selected.IsDeco) _draft.DecoTransform = null;
            else _draft.CharmTransforms?.Remove(_selected.Key);

            LayoutSprites();
            RefreshControls();
        }

        // ============================== footer ==============================

        private void BtnCancel_Click()
        {
            // Same draft instance the Customize dialog holds - put the transforms back the way
            // they were when this editor opened. Item choices were never ours to touch.
            _draft.DecoTransform = _snapshotDeco;
            _draft.CharmTransforms = _snapshotCharms;
            Close(false);
        }
    }
}
