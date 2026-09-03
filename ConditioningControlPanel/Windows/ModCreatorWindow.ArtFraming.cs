using System;
using ConditioningControlPanel.Helpers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The framing editor behind the UI Art panel: drag and zoom a picked image to choose what
    /// each UI surface shows of it, against a mock of the real surface.
    ///
    /// <para><b>Why this exists.</b> Mort, framing a mod: "is there a way we can get more control
    /// over which part of the image is taken for these? some of them don't really work with the
    /// automatic crop". Before this, the panel's own prose asked authors to compensate by hand --
    /// "the cards crop to a ~135px-tall header, so keep the subject centred" -- which is a
    /// workaround dressed as documentation, and impossible to satisfy for the SHARED files where
    /// one image feeds several differently-shaped frames at once.</para>
    ///
    /// <para><b>One preview per binding, not per file.</b> <c>features/fyp.png</c> is a rail chip
    /// (1.64:1) AND a Play card header (2.22:1); <c>features/lab_quiz_hero.png</c> is a chip, a
    /// card and a page header. Those are shown side by side and framed independently, which is the
    /// entire reason the numbers live in mod.json instead of being baked into the PNG: a baked crop
    /// silently decides every other surface wrong and destroys the author's original so they can
    /// never re-frame it.</para>
    ///
    /// <para><b>Every shape here comes from <see cref="ModArtFramingRegistry"/>.</b> Aspect ratio,
    /// corner radius and which scrim to lay over the art are all read from the surface table, never
    /// retyped -- a preview that carries its own copy of those numbers starts lying the first time
    /// a surface is restyled, and a lying preview is worse than no preview. The scrim GRADIENTS are
    /// rebuilt here to approximate SettingsTabView's ChipScrimBrush / CardScrimBrush rather than
    /// referenced: a fresh cross-dictionary StaticResource into a Theme dictionary has produced
    /// BAML EndOfStream load failures in this codebase before.</para>
    ///
    /// <para>This window only collects numbers. Applying them at runtime is
    /// <c>MainWindow.ApplyArtFraming</c>, and the export/round-trip decisions are in
    /// <see cref="ModArtFramingRules"/> below so they can be tested without a Dispatcher.</para>
    ///
    /// Partial-class side file of <see cref="ModCreatorWindow"/> (see ModCreatorWindow.xaml.cs).
    /// </summary>
    public partial class ModCreatorWindow
    {
        // ─── State ───────────────────────────────────────────────
        // slot key → surface id → framing. Case-insensitive on the slot key so a hand-edited
        // mod.json that says "Features/FYP.png" still lands on the slot it means.
        private readonly Dictionary<string, Dictionary<string, ModArtFraming>> _artFraming =
            new(StringComparer.OrdinalIgnoreCase);

        // The "Frame" affordance per slot, so SetImageSlot/ClearImageSlot can show and label it
        // without walking the visual tree. Kept OUT of border.Tag: that tuple is pattern-matched
        // by shape in two places and widening it would break both.
        private readonly Dictionary<string, Button> _frameButtons = new();

        /// <summary>How wide a preview mock is allowed to get, and how tall. Everything from the
        /// 1.11:1 rail launcher to the 4.59:1 page header has to sit in one readable row.</summary>
        private const double FramePreviewMaxWidth = 260;
        private const double FramePreviewMaxHeight = 170;

        // ─── The "Frame" affordance on a slot ────────────────────

        /// <summary>
        /// True when this slot's resource path actually paints a framed surface, i.e. there is
        /// something for a framing editor to preview. Most slots (achievement badges, skill nodes,
        /// avatar poses) are drawn Uniform at their natural aspect and have no crop to choose.
        ///
        /// <para>FRAMABLE bindings only, not every binding. <c>features/goon_game.png</c> has a
        /// binding (the Play card needs its shipped rect for OUR art) but its brush is
        /// <c>Stretch="Uniform"</c> over a solid plate, so the image letterboxes instead of
        /// cropping and there is no window to choose. Offering a Frame button there would put an
        /// edge-to-edge preview in front of an author whose art is going to sit inside the plate --
        /// the exact class of lie this registry exists to remove.</para>
        /// </summary>
        private static bool SlotIsFramable(string slotKey) =>
            ModArtFramingRegistry.FramableBindingsFor(slotKey).Any();

        /// <summary>
        /// The small "Frame" button that sits in the corner of a framable slot's thumbnail. It is
        /// a Button on purpose: a Button marks MouseLeftButtonDown handled, so clicking it does not
        /// also fire the border's click-to-browse.
        /// </summary>
        private Button CreateFrameAffordance(string slotKey)
        {
            var btn = new Button
            {
                Content = "Frame",
                FontSize = 9,
                Padding = new Thickness(4, 1, 4, 1),
                Background = new SolidColorBrush(Color.FromArgb(190, 30, 30, 50)),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B0B0C8")),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(2, 0, 0, 2),
                Visibility = Visibility.Collapsed,   // nothing to frame until an image is picked
            };
            btn.Click += (_, _) => OpenFramingEditor(slotKey);
            _frameButtons[slotKey] = btn;
            UpdateFrameAffordance(slotKey);
            return btn;
        }

        /// <summary>Shows or hides the affordance as the slot gains and loses its image.</summary>
        private void SetFrameAffordanceVisible(string slotKey, bool visible)
        {
            if (_frameButtons.TryGetValue(slotKey, out var btn))
                btn.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Relabels the affordance so an author can see at a glance which slots they have framed --
        /// otherwise the only way to tell a framed slot from an unframed one is to open every one.
        /// </summary>
        private void UpdateFrameAffordance(string slotKey)
        {
            if (!_frameButtons.TryGetValue(slotKey, out var btn)) return;

            var count = _artFraming.TryGetValue(slotKey, out var perSurface) ? perSurface.Count : 0;
            if (count > 0)
            {
                btn.Content = "Framed";
                btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF69B4"));
                btn.ToolTip = count == 1
                    ? "Framed for 1 surface -- click to adjust"
                    : $"Framed for {count} surfaces -- click to adjust";
            }
            else
            {
                btn.Content = "Frame";
                btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B0B0C8"));
                btn.ToolTip = "Choose which part of this image each UI surface shows";
            }
        }

        /// <summary>Forgets a slot's framing. Called from <c>ClearImageSlot</c> and whenever the
        /// author swaps in a different file: a crop chosen for one picture applied silently to the
        /// next one is a nasty, invisible bug.</summary>
        private void DropArtFraming(string slotKey)
        {
            _artFraming.Remove(slotKey);
            UpdateFrameAffordance(slotKey);
        }

        // ─── The framing editor ──────────────────────────────────

        /// <summary>
        /// Opens the modal framing editor for one slot: one preview per surface the slot's path
        /// paints, each independently pannable and zoomable. Changes are made on clones and only
        /// committed on OK, so Cancel really does cancel.
        /// </summary>
        private void OpenFramingEditor(string slotKey)
        {
            // FramableBindingsFor, matching SlotIsFramable: a non-framable pair must not produce a
            // preview even if something else reaches this method, because its preview cannot be
            // honest about what the surface does with the art.
            var surfaces = ModArtFramingRegistry.FramableBindingsFor(slotKey)
                .Select(b => ModArtFramingRegistry.FindSurface(b.SurfaceId))
                .Where(s => s != null)
                .Select(s => s!)
                .GroupBy(s => s.Id)              // a path is bound to a surface once, but do not rely on it
                .Select(g => g.First())
                .ToList();
            if (surfaces.Count == 0) return;

            if (!_imageSlots.TryGetValue(slotKey, out var filePath) || string.IsNullOrEmpty(filePath))
            {
                MessageBox.Show(this,
                    "Pick an image for this slot first -- there is nothing to frame yet.",
                    "Nothing to frame", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var bitmap = TryDecodeForFraming(filePath);
            if (bitmap == null || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            {
                MessageBox.Show(this,
                    $"\"{Path.GetFileName(filePath)}\" could not be decoded, so it cannot be previewed.\n\n" +
                    "The file may be corrupt, may not be the format its extension claims, or may be too " +
                    "large to decode. Re-save it and pick it again.",
                    "Cannot read that image", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var sourceAspect = (double)bitmap.PixelWidth / bitmap.PixelHeight;

            // Working copies. Clone() exists for exactly this: the previews mutate live objects on
            // every mouse move, and none of that may reach _artFraming unless the author says OK.
            var working = new Dictionary<string, ModArtFraming>(StringComparer.Ordinal);
            foreach (var surface in surfaces)
            {
                var stored = _artFraming.TryGetValue(slotKey, out var perSurface)
                             && perSurface.TryGetValue(surface.Id, out var f)
                    ? f.Clone()
                    : new ModArtFraming();
                working[surface.Id] = stored;
            }

            var resets = new List<Action>();
            var body = new StackPanel { Margin = new Thickness(16) };

            body.Children.Add(new TextBlock
            {
                Text = $"Framing {Path.GetFileName(slotKey)}",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF69B4")),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8),
            });

            // The single-surface blurb has to know whether it can promise exactness: "what you see
            // is what ships" is true of a fixed 69x42 chip and false of a Play card whose width
            // follows the window. The multi-surface blurb makes no such promise, so it is unchanged
            // and the per-card note below carries the caveat for whichever of them are fluid.
            var blurb = surfaces.Count > 1
                ? $"This one file paints {surfaces.Count} differently-shaped surfaces, so each gets its own crop. " +
                  "Frame them one at a time -- what you choose here is stored per surface, which is why you do " +
                  "not have to pre-crop the PNG and lose your original."
                : surfaces[0].FluidWidth
                    ? "This is a mock of the real surface, with the same darkening the app lays over the art. Its " +
                      "height is fixed but its width follows the window, so the shape here is representative rather " +
                      "than exact -- see the note under the preview."
                    : "This is a mock of the real surface, at its real shape, with the same darkening the app lays " +
                      "over the art. What you see is what ships.";
            body.Children.Add(new TextBlock
            {
                Text = blurb + "\n\nDrag to move  ·  mouse wheel to zoom  ·  double-click to go back to the automatic crop.",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#909090")),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
                MaxWidth = 780,
                Margin = new Thickness(0, 0, 0, 14),
            });

            var wrap = new WrapPanel { MaxWidth = 840 };
            var caption = FramingCaptionFor(slotKey);
            foreach (var surface in surfaces)
                wrap.Children.Add(BuildFramingCard(surface, bitmap, sourceAspect, working[surface.Id], caption, resets));
            body.Children.Add(wrap);

            body.Children.Add(new TextBlock
            {
                Text = $"Source image: {bitmap.PixelWidth}x{bitmap.PixelHeight} " +
                       $"({sourceAspect.ToString("0.00", CultureInfo.InvariantCulture)}:1). " +
                       "The numbers under each preview are what lands in mod.json under artFraming, " +
                       "if you ever want to nudge them by hand.",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#707080")),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 780,
                Margin = new Thickness(0, 14, 0, 0),
            });

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0),
            };
            var btnResetAll = CreateFramingDialogButton("Reset all", accent: false);
            var btnCancel = CreateFramingDialogButton("Cancel", accent: false);
            var btnOk = CreateFramingDialogButton("Done", accent: true);
            buttonRow.Children.Add(btnResetAll);
            buttonRow.Children.Add(btnCancel);
            buttonRow.Children.Add(btnOk);
            body.Children.Add(buttonRow);

            var dialog = new Window
            {
                Title = "Frame mod art",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1A2E")),
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                MaxWidth = 920,
                MaxHeight = 840,
                Content = new ScrollViewer
                {
                    Content = body,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                },
            };

            btnResetAll.Click += (_, _) => { foreach (var reset in resets) reset(); };
            btnCancel.Click += (_, _) => dialog.DialogResult = false;
            btnOk.Click += (_, _) => dialog.DialogResult = true;

            bool committed;
            try
            {
                committed = dialog.ShowDialog() == true;
            }
            catch (Exception ex)
            {
                // A modal that throws while showing must not take the mod editor with it.
                App.Logger?.Warning(ex, "Mod editor: framing dialog for {Slot} failed to show", slotKey);
                return;
            }
            if (!committed) return;

            // Commit. Only non-default framings are kept: "centred, no zoom" is precisely what the
            // absence of an entry already means, so storing it would inflate mod.json with no-ops
            // and make every slot look framed in the panel.
            var kept = new Dictionary<string, ModArtFraming>(StringComparer.Ordinal);
            foreach (var (surfaceId, framing) in working)
            {
                var clean = ModArtFramingRules.Sanitize(framing);
                if (!clean.IsDefault) kept[surfaceId] = clean;
            }

            if (kept.Count > 0) _artFraming[slotKey] = kept;
            else _artFraming.Remove(slotKey);

            UpdateFrameAffordance(slotKey);
            UpdateStatusBar();
        }

        /// <summary>
        /// One surface's preview: the mock frame, the live <c>ImageBrush</c>, its scrim and caption,
        /// and the numeric readout. Registers its own reset with <paramref name="resets"/> so
        /// "Reset all" and double-click share one code path.
        /// </summary>
        private FrameworkElement BuildFramingCard(ArtSurface surface, BitmapSource bitmap, double sourceAspect,
                                                  ModArtFraming framing, string caption, List<Action> resets)
        {
            // Shape from the registry. Width is capped first because every surface bar the rail
            // launcher is wider than the box, so the height falls out of the aspect ratio.
            var width = Math.Min(FramePreviewMaxWidth, FramePreviewMaxHeight * surface.AspectRatio);
            var height = width / surface.AspectRatio;
            if (height > FramePreviewMaxHeight)
            {
                height = FramePreviewMaxHeight;
                width = height * surface.AspectRatio;
            }

            var card = new StackPanel { Margin = new Thickness(0, 0, 14, 14) };

            card.Children.Add(new TextBlock
            {
                Text = surface.DisplayName,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB6C1")),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
            });

            // Stretch=Fill on a rect that is already the surface's shape: ToViewbox has done all
            // the aspect arithmetic, so any Uniform* stretch here would apply it a second time and
            // the preview would stop matching what the app draws.
            var brush = new ImageBrush(bitmap)
            {
                Stretch = Stretch.Fill,
                Viewbox = ModArtFramingRegistry.ToViewbox(framing, sourceAspect, surface.AspectRatio).ToRect(),
            };

            var holder = new Grid
            {
                Width = width,
                Height = height,
                Cursor = Cursors.SizeAll,
                // Transparent, not null: an unpainted Grid is not hit-testable, and every mouse
                // handler below lives on the holder so the scrim layers cannot swallow a drag.
                Background = Brushes.Transparent,
                ClipToBounds = true,
            };

            holder.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(surface.CornerRadius),
                Background = brush,
                IsHitTestVisible = false,
            });

            // Scrim and caption straight off the surface record, so a restyle in the registry
            // changes what the author sees here too.
            if (surface.Scrim != ArtScrim.None)
            {
                holder.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(surface.CornerRadius),
                    Background = surface.Scrim == ArtScrim.FullFace ? FullFaceScrim : FootBandScrim,
                    IsHitTestVisible = false,
                });
                holder.Children.Add(BuildScrimCaption(surface, caption, height));
            }

            // The trim band goes UNDER the frame outline but OVER the scrim, so it stays visible
            // against the darkened foot where a subject's chin is most likely to be sitting.
            if (surface.FluidWidth)
                holder.Children.Add(BuildFluidTrimBand(height));

            holder.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(surface.CornerRadius),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#505070")),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
            });

            card.Children.Add(holder);

            // Said once per fluid preview rather than once per dialog: an author framing
            // lab_quiz_hero.png sees a fixed chip, a fixed intake strip and a fluid card side by
            // side, and a dialog-level warning would tar the two exact previews with it.
            if (surface.FluidWidth)
            {
                card.Children.Add(new TextBlock
                {
                    Text = "Width follows the window, so this shape is representative. The app centre-crops " +
                           "the difference; the dashed band is roughly how much of the top and bottom can go " +
                           "at the extremes. Keep the subject inside it and every window width reads right.",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8B08A")),
                    FontSize = 10,
                    LineHeight = 14,
                    Margin = new Thickness(0, 4, 0, 0),
                    MaxWidth = width + 20,
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            var readout = new TextBlock
            {
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808090")),
                FontSize = 10,
                Margin = new Thickness(0, 4, 0, 0),
                MaxWidth = width + 20,
                TextWrapping = TextWrapping.Wrap,
            };
            card.Children.Add(readout);

            void Refresh()
            {
                brush.Viewbox = ModArtFramingRegistry.ToViewbox(framing, sourceAspect, surface.AspectRatio).ToRect();
                readout.Text = DescribeFraming(framing);
            }
            Refresh();

            // Held in a local as well as the shared list: indexing back into the list from the
            // double-click handler would run whichever card was built LAST, not this one.
            void Reset()
            {
                framing.CenterX = 0.5;
                framing.CenterY = 0.5;
                framing.Zoom = 1.0;
                Refresh();
            }
            resets.Add(Reset);

            // ── pan / zoom / reset ──
            Point? dragFrom = null;
            var dragCenterX = 0.5;
            var dragCenterY = 0.5;

            holder.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount >= 2)
                {
                    // Double-click is the escape hatch back to the automatic crop, which is the
                    // one framing that costs nothing to store because it is stored as nothing.
                    Reset();
                    dragFrom = null;
                    holder.ReleaseMouseCapture();
                    e.Handled = true;
                    return;
                }
                dragFrom = e.GetPosition(holder);
                dragCenterX = framing.CenterX;
                dragCenterY = framing.CenterY;
                holder.CaptureMouse();
                e.Handled = true;
            };

            holder.MouseMove += (_, e) =>
            {
                if (dragFrom == null || !holder.IsMouseCaptured) return;

                var now = e.GetPosition(holder);
                var window = ModArtFramingRegistry.ToViewbox(framing, sourceAspect, surface.AspectRatio);

                // Dragging the picture right must move the crop window LEFT, hence the minus:
                // the author is grabbing the image, not the viewfinder. Scaled by the window's own
                // size so a drag across the preview travels exactly one frame's worth of source.
                framing.CenterX = ClampToWindow(dragCenterX - (now.X - dragFrom.Value.X) / width * window.Width, window.Width);
                framing.CenterY = ClampToWindow(dragCenterY - (now.Y - dragFrom.Value.Y) / height * window.Height, window.Height);
                Refresh();
            };

            holder.MouseLeftButtonUp += (_, _) =>
            {
                dragFrom = null;
                holder.ReleaseMouseCapture();
            };
            holder.LostMouseCapture += (_, _) => dragFrom = null;

            holder.MouseWheel += (_, e) =>
            {
                var step = e.Delta > 0 ? 1.12 : 1 / 1.12;
                framing.Zoom = Math.Clamp(framing.Zoom * step, 1.0, ModArtFramingRegistry.MaxZoom);

                // Zooming OUT widens the window, which NARROWS the band of centres that still move
                // it, so the stored centre is pulled back inside the new band instead of being left
                // outside it. Two things go wrong otherwise: the readout advertises an offset the
                // pinned window does not have, and zooming all the way back to 1.0 leaves a centre
                // that ToViewbox ignores but IsDefault does not -- so export writes a no-op entry to
                // mod.json and the slot lights up as "Framed".
                var zoomed = ModArtFramingRegistry.ToViewbox(framing, sourceAspect, surface.AspectRatio);
                framing.CenterX = ClampToWindow(framing.CenterX, zoomed.Width);
                framing.CenterY = ClampToWindow(framing.CenterY, zoomed.Height);

                Refresh();
                // Handled, or the wheel also scrolls the dialog out from under the pointer.
                e.Handled = true;
            };

            return card;
        }

        /// <summary>
        /// The mock caption. A FootBand surface hangs it at the foot, where the real chip and card
        /// captions sit, so the author can see what their subject's chin is about to disappear
        /// behind; a FullFace surface puts it at the top with a stand-in for the stepper row that
        /// is the reason that surface darkens throughout.
        /// </summary>
        private static FrameworkElement BuildScrimCaption(ArtSurface surface, string caption, double height)
        {
            var white = new SolidColorBrush(Colors.White);
            var fontSize = Math.Max(9, Math.Min(13, height / 9));

            var text = new TextBlock
            {
                Text = caption,
                Foreground = white,
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false,
            };

            if (surface.Scrim != ArtScrim.FullFace)
            {
                text.VerticalAlignment = VerticalAlignment.Bottom;
                text.HorizontalAlignment = HorizontalAlignment.Center;
                text.Margin = new Thickness(4, 0, 4, 3);
                return text;
            }

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(3, 2, 3, 3),
                IsHitTestVisible = false,
            };
            stack.Children.Add(text);
            stack.Children.Add(new TextBlock
            {
                Text = "−   15m   +",
                Foreground = white,
                FontSize = Math.Max(8, fontSize - 2),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
                IsHitTestVisible = false,
            });
            return stack;
        }

        /// <summary>
        /// How much of a fluid-width surface's height the runtime can take off the top and bottom,
        /// as a fraction of the preview.
        ///
        /// <para>Derived, not guessed, but still only representative. A Play card declares the
        /// 3-column aspect it has at the design width (~2.85:1); the same card in a 2-column window
        /// is nearer 4.3:1, and <c>UniformToFill</c> fitting a 2.85 window into a 4.3 frame loses
        /// 1 - 2.85/4.3 of the height, half off each edge -- about 17%. 15% is drawn because the band
        /// is a "keep the subject in here" hint rather than a promise about a specific window size,
        /// and a band bigger than the real trim would push authors into framing tighter than they
        /// need to. The honest fix is recomputing the Viewbox from the live control on layout; see
        /// <see cref="ArtSurface.FluidWidth"/> for why that is follow-up.</para>
        /// </summary>
        private const double FluidTrimFraction = 0.15;

        /// <summary>
        /// The dashed safe-area band on a fluid-width preview: everything outside it is what the
        /// runtime's centre crop may eat. Drawn as one dashed rectangle rather than two lines so it
        /// reads as an area to stay inside, which is the instruction.
        /// </summary>
        private static FrameworkElement BuildFluidTrimBand(double height)
        {
            var inset = Math.Round(height * FluidTrimFraction);

            return new System.Windows.Shapes.Rectangle
            {
                // Vertical inset only: the height is the fixed dimension, so the width is never the
                // axis that gets trimmed on these surfaces.
                Margin = new Thickness(0, inset, 0, inset),
                Stroke = new SolidColorBrush(Color.FromArgb(150, 0xE8, 0xD8, 0xB0)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false,
            };
        }

        private static Button CreateFramingDialogButton(string text, bool accent)
        {
            return new Button
            {
                Content = text,
                MinWidth = 84,
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 12,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent ? "#FF69B4" : "#505070")),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent ? "#FF1493" : "#252542")),
                Foreground = new SolidColorBrush(Colors.White),
            };
        }

        /// <summary>
        /// A stand-in for the caption the real surface draws. The slot's own display name is the
        /// closest thing to hand and is about the right length; the parenthetical pixel hint is
        /// dropped because no real caption carries one.
        /// </summary>
        private string FramingCaptionFor(string slotKey)
        {
            var name = _imageNames.TryGetValue(slotKey, out var n) && !string.IsNullOrWhiteSpace(n)
                ? n
                : Path.GetFileNameWithoutExtension(slotKey);

            var paren = name.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) name = name[..paren];
            return name.Trim();
        }

        /// <summary>The numbers, small but readable -- an author may want to nudge them by hand in
        /// mod.json later, and cannot do that without knowing what the drag produced.</summary>
        private static string DescribeFraming(ModArtFraming framing)
        {
            if (framing.IsDefault) return "auto (centre crop) · nothing written to mod.json";

            return string.Format(CultureInfo.InvariantCulture,
                "centre {0:0.###}, {1:0.###}  ·  zoom {2:0.##}x",
                framing.CenterX, framing.CenterY, framing.Zoom);
        }

        /// <summary>
        /// Clamp a focal coordinate to the range that actually MOVES the crop window, which is
        /// <c>[size/2, 1 - size/2]</c> and not <c>[0, 1]</c>.
        ///
        /// <para>Why it matters to the feel: <c>ToViewbox</c> SLIDES the window inside the image
        /// rather than shrinking it, so a centre past that range produces the identical pinned
        /// window. Clamping only to [0,1] therefore let a drag keep pushing the stored centre toward
        /// 0 or 1 long after the picture had stopped moving, and the drag back did nothing at all
        /// until it had re-crossed that dead zone -- the edges felt sticky. Clamping here means the
        /// stored value never leaves the range where it has an effect, so a reversal reverses.</para>
        ///
        /// <para>A window covering the whole axis (the common <c>zoom == 1</c> case on one of the
        /// two axes) leaves no range at all, and the only value that is not a lie is the centre.</para>
        /// </summary>
        /// <param name="v">The proposed centre.</param>
        /// <param name="windowSize">The crop window's size on that axis, in relative units.</param>
        private static double ClampToWindow(double v, double windowSize)
        {
            if (double.IsNaN(v)) return 0.5;
            if (double.IsNaN(windowSize) || windowSize <= 0 || windowSize >= 1.0) return 0.5;

            var half = windowSize / 2.0;
            return Math.Clamp(v, half, 1.0 - half);
        }

        // ─── Scrim brushes ───────────────────────────────────────
        // Approximations of SettingsTabView's ChipScrimBrush / CardScrimBrush, rebuilt here rather
        // than referenced across dictionaries -- see the class comment for why that matters. Frozen
        // and static: one instance serves every preview ever opened.

        private static readonly Brush FootBandScrim = MakeVerticalScrim(
            (0x00, 0.28), (0x73, 0.62), (0xDE, 1.00));

        private static readonly Brush FullFaceScrim = MakeVerticalScrim(
            (0x59, 0.00), (0xB8, 0.42), (0xE6, 1.00));

        private static Brush MakeVerticalScrim(params (byte Alpha, double Offset)[] stops)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            foreach (var (alpha, offset) in stops)
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 0, 0, 0), offset));
            brush.Freeze();
            return brush;
        }

        // ─── Decode ──────────────────────────────────────────────

        /// <summary>
        /// Decodes a picked file for previewing, capped. An author can hand this window a corrupt
        /// PNG, a JPEG wearing a .png extension, or a 12000px poster whose full BGRA decode is
        /// hundreds of megabytes -- none of which may take the app down, so the cap is on the
        /// LONGER edge (capping width alone would decode a tall banner to an enormous height) and
        /// everything is inside one try/catch. Null means "tell the author, do not open".
        /// </summary>
        private static BitmapSource? TryDecodeForFraming(string filePath)
        {
            const int MaxEdge = 900;   // comfortably past the widest preview even at MaxZoom

            try
            {
                var uri = new Uri(filePath, UriKind.Absolute);

                // Header-only peek so the cap can be put on the right axis. If this fails we still
                // try the decode below -- a decoder that cannot read metadata may still read pixels.
                var wide = true;
                try
                {
                    var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    if (decoder.Frames.Count > 0)
                        wide = decoder.Frames[0].PixelWidth >= decoder.Frames[0].PixelHeight;
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug(ex, "Mod editor: no readable header on {Path}, decoding blind", filePath);
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;   // release the file handle
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (wide) bitmap.DecodePixelWidth = MaxEdge;
                else bitmap.DecodePixelHeight = MaxEdge;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Mod editor: could not decode {Path} for the framing preview", filePath);
                return null;
            }
        }

        // ─── Manifest in / out ───────────────────────────────────

        /// <summary>Writes the author's framings into the manifest on export. All of the filtering
        /// lives in <see cref="ModArtFramingRules"/> so it is testable without a window.</summary>
        private void ApplyArtFramingToManifest(ModManifest manifest)
        {
            manifest.ArtFraming = ModArtFramingRules.BuildManifestSection(_artFraming, _imageSlots);
        }

        /// <summary>
        /// Reads framings back when an author reopens a .ccpmod or the active mod, so re-framing is
        /// possible instead of starting over. Keys are mapped onto the editor's own slot keys
        /// case-insensitively; entries naming a surface or a slot this build does not have are
        /// dropped rather than throwing, which is the whole point of the "never rename a surface id"
        /// rule.
        /// </summary>
        private void PopulateArtFramingFromManifest(ModManifest manifest)
        {
            _artFraming.Clear();

            foreach (var (path, perSurface) in ModArtFramingRules.ReadManifestSection(manifest.ArtFraming))
            {
                var slotKey = _imageSlots.Keys.FirstOrDefault(k => string.Equals(k, path, StringComparison.OrdinalIgnoreCase));
                if (slotKey == null) continue;   // a path this editor offers no slot for: unframable here
                _artFraming[slotKey] = perSurface;
            }

            foreach (var slotKey in _frameButtons.Keys)
                UpdateFrameAffordance(slotKey);
        }
    }

    /// <summary>
    /// What survives the trip between the framing editor and <c>mod.json</c>. Split out of the
    /// window for the same reason <see cref="ModImageSlotRules"/> was: these are decisions, they
    /// need no Dispatcher, and they are exactly the part worth pinning with tests.
    ///
    /// <para>Two filters do the real work. <b>Non-default only</b>, because a centred no-zoom
    /// framing is byte-for-byte what the ABSENCE of an entry already means to
    /// <c>ResolveViewbox</c> -- writing it fills a manifest with no-ops that look like decisions.
    /// <b>Image present only</b>, because framing describes a crop of the author's file at
    /// <c>resources/&lt;slot key&gt;</c>; with no file there the app is showing its own built-in
    /// art, which deliberately ignores <c>artFraming</c> and keeps its shipped rect. A framing for
    /// an empty slot is therefore a claim about a picture that does not exist.</para>
    ///
    /// <para>A third filter is pure hygiene: an unknown surface id, and a pair the registry marks
    /// <b>not framable</b>, are both dropped in each direction. Neither can come out of the editor
    /// -- only out of a hand-edited or newer-than-us <c>mod.json</c> -- and the runtime ignores both,
    /// so carrying them would put numbers in a manifest that describe no crop anyone can see.</para>
    /// </summary>
    internal static class ModArtFramingRules
    {
        /// <summary>
        /// Decimals kept in mod.json. A drag produces numbers like 0.47321981, and four decimals
        /// is far finer than a pixel on any of these surfaces -- the rest is noise an author has to
        /// read past when they open the file.
        /// </summary>
        internal const int StoredDecimals = 4;

        /// <summary>
        /// The manifest's <c>artFraming</c> section, or null when there is nothing worth writing
        /// (which keeps the key out of the JSON entirely, since export ignores nulls).
        /// </summary>
        /// <param name="framings">The editor's live map: slot key → surface id → framing.</param>
        /// <param name="imageSlots">The editor's slot map, slot key → picked file or null.</param>
        internal static Dictionary<string, Dictionary<string, ModArtFraming>>? BuildManifestSection(
            IReadOnlyDictionary<string, Dictionary<string, ModArtFraming>> framings,
            IReadOnlyDictionary<string, string?> imageSlots)
        {
            var result = new Dictionary<string, Dictionary<string, ModArtFraming>>(StringComparer.Ordinal);
            if (framings == null) return null;

            foreach (var (slotKey, perSurface) in framings)
            {
                if (string.IsNullOrWhiteSpace(slotKey) || perSurface == null) continue;
                if (!HasImage(imageSlots, slotKey)) continue;

                var kept = new Dictionary<string, ModArtFraming>(StringComparer.Ordinal);
                foreach (var (surfaceId, framing) in perSurface)
                {
                    if (framing == null) continue;

                    // An id this build does not know cannot have been framed against a real
                    // preview, so it is not written back. The cost is that a mod authored on a
                    // LATER build and re-exported here loses that surface's framing -- accepted,
                    // because the alternative is shipping numbers nobody could see or check.
                    if (ModArtFramingRegistry.FindSurface(surfaceId) == null) continue;

                    // Nor a pair the author was never offered. The editor cannot produce one, so
                    // this only catches what came in from a hand-edited mod.json -- and the runtime
                    // ignores framing on a non-framable pair (ResolveViewbox hands it the whole
                    // image), so writing it back would leave numbers in the file that describe
                    // nothing and invite the next reader to "fix" a crop that never happens.
                    if (!ModArtFramingRegistry.IsFramable(slotKey, surfaceId)) continue;

                    var clean = Round(Sanitize(framing));
                    if (clean.IsDefault) continue;
                    kept[surfaceId] = clean;
                }

                if (kept.Count > 0) result[slotKey] = kept;
            }

            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// The same shape coming the other way, for the load/import path: sanitized, no-ops and
        /// unknown surface ids dropped, never throwing on a hand-edited or newer-than-us manifest.
        /// Empty (not null) when there is nothing usable, so callers need no null dance.
        /// </summary>
        internal static Dictionary<string, Dictionary<string, ModArtFraming>> ReadManifestSection(
            IReadOnlyDictionary<string, Dictionary<string, ModArtFraming>>? artFraming)
        {
            var result = new Dictionary<string, Dictionary<string, ModArtFraming>>(StringComparer.OrdinalIgnoreCase);
            if (artFraming == null) return result;

            foreach (var (path, perSurface) in artFraming)
            {
                if (string.IsNullOrWhiteSpace(path) || perSurface == null) continue;

                var kept = new Dictionary<string, ModArtFraming>(StringComparer.Ordinal);
                foreach (var (surfaceId, framing) in perSurface)
                {
                    if (framing == null) continue;
                    if (ModArtFramingRegistry.FindSurface(surfaceId) == null) continue;

                    // Dropped at the door rather than on the way out, so the editor never holds a
                    // framing it has no preview for: there is no Frame button on a non-framable
                    // pair, so a hand-edited entry would otherwise sit in memory invisible and
                    // unfixable, and still be counted by the "Framed for N surfaces" label.
                    if (!ModArtFramingRegistry.IsFramable(path, surfaceId)) continue;

                    var clean = Sanitize(framing);
                    if (clean.IsDefault) continue;
                    kept[surfaceId] = clean;
                }

                if (kept.Count > 0) result[path] = kept;
            }

            return result;
        }

        /// <summary>
        /// A copy with every field inside its legal range. <c>ToViewbox</c> clamps too, but the
        /// editor's sliders, readouts and <c>IsDefault</c> checks all read these values directly,
        /// and a NaN out of a hand-edited mod.json would poison all three.
        /// </summary>
        internal static ModArtFraming Sanitize(ModArtFraming framing) => new()
        {
            CenterX = Coordinate(framing.CenterX),
            CenterY = Coordinate(framing.CenterY),
            Zoom = ZoomOf(framing.Zoom),
        };

        /// <summary>A copy rounded to what mod.json stores.</summary>
        internal static ModArtFraming Round(ModArtFraming framing) => new()
        {
            CenterX = Math.Round(framing.CenterX, StoredDecimals),
            CenterY = Math.Round(framing.CenterY, StoredDecimals),
            Zoom = Math.Round(framing.Zoom, StoredDecimals),
        };

        private static bool HasImage(IReadOnlyDictionary<string, string?> imageSlots, string slotKey)
        {
            if (imageSlots == null) return false;
            if (imageSlots.TryGetValue(slotKey, out var picked)) return !string.IsNullOrEmpty(picked);

            // The editor's slot map is ordinal, so fall back to a case-insensitive sweep for a
            // framing whose key came out of a hand-edited manifest with different casing.
            return imageSlots.Any(kv => string.Equals(kv.Key, slotKey, StringComparison.OrdinalIgnoreCase)
                                        && !string.IsNullOrEmpty(kv.Value));
        }

        private static double Coordinate(double v) =>
            double.IsNaN(v) || double.IsInfinity(v) ? 0.5 : Math.Clamp(v, 0.0, 1.0);

        private static double ZoomOf(double v) =>
            double.IsNaN(v) || double.IsInfinity(v) ? 1.0 : Math.Clamp(v, 1.0, ModArtFramingRegistry.MaxZoom);
    }
}
