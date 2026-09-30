// PORTED from ConditioningControlPanel/Controls/ChasterAccountBadge.cs: the linked Chaster account
// as a face - a round picture (or the name's first letter while there is none) and the username.
// Reads ChasterService.Profile and repaints on ProfileChanged / LinkChanged, so a host only places it.
using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Controls
{
    public sealed class ChasterAccountBadge : StackPanel
    {
        public static readonly StyledProperty<double> AvatarSizeProperty = AvaloniaProperty.Register<ChasterAccountBadge, double>(nameof(AvatarSize), 22);
        public static readonly StyledProperty<bool> ShowNameProperty = AvaloniaProperty.Register<ChasterAccountBadge, bool>(nameof(ShowName), true);

        public double AvatarSize { get => GetValue(AvatarSizeProperty); set => SetValue(AvatarSizeProperty, value); }
        public bool ShowName { get => GetValue(ShowNameProperty); set => SetValue(ShowNameProperty, value); }

        /// <summary>The name text, so a host can match its font to the line it sits in.</summary>
        public TextBlock NameText { get; } = new()
        {
            Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis,
        };

        private readonly Grid _face = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _letter = new()
        {
            Foreground = Brushes.White, FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        private readonly Ellipse _picture = new() { IsVisible = false };
        private ChasterService? _subscribed;

        // One decode per picture for the whole app, as WPF.
        private static byte[]? _decodedFrom;
        private static Bitmap? _decoded;

        public ChasterAccountBadge()
        {
            Orientation = Orientation.Horizontal;
            VerticalAlignment = VerticalAlignment.Center;
            _face.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x8A)) });
            _face.Children.Add(_letter);
            _face.Children.Add(_picture);
            _face.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1 });
            Children.Add(_face);
            Children.Add(NameText);
            Repaint();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == AvatarSizeProperty || change.Property == ShowNameProperty) Repaint();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _subscribed = ChasterHead.Service;
            if (_subscribed != null) { _subscribed.ProfileChanged += OnChanged; _subscribed.LinkChanged += OnChanged; }
            Repaint();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            if (_subscribed != null) { _subscribed.ProfileChanged -= OnChanged; _subscribed.LinkChanged -= OnChanged; }
            _subscribed = null;
        }

        private void OnChanged() => Dispatcher.UIThread.Post(Repaint);

        private void Repaint()
        {
            try
            {
                var chaster = ChasterHead.Service;
                var profile = chaster?.Profile;
                var name = profile?.Username;
                _face.Width = _face.Height = AvatarSize;
                _letter.FontSize = Math.Max(8, AvatarSize * 0.5);
                _letter.Text = string.IsNullOrEmpty(name) ? "C" : char.ToUpperInvariant(name[0]).ToString();

                var image = profile == null ? null : Decode(chaster!.AvatarBytes);
                _picture.Fill = image == null ? null : new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                _picture.IsVisible = image != null;

                NameText.Text = string.IsNullOrEmpty(name) ? Loc.Get("chaster_account_name") : name;
                NameText.IsVisible = ShowName;
            }
            catch (Exception ex) { Serilog.Log.Debug("[Chaster] account badge: {E}", ex.Message); }
        }

        /// <summary>Bytes to a small bitmap (64 px wide), or null when they are not a picture.</summary>
        internal static Bitmap? Decode(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            if (ReferenceEquals(bytes, _decodedFrom)) return _decoded;
            Bitmap? result = null;
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                result = Bitmap.DecodeToWidth(stream, 64);
            }
            catch (Exception ex) { Serilog.Log.Debug("[Chaster] avatar is not a picture, the letter stays: {E}", ex.Message); }
            _decodedFrom = bytes;
            _decoded = result;
            return result;
        }
    }
}
