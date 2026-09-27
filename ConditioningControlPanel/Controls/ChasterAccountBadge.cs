using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// The linked Chaster account as a face: a round picture (or the name's first letter while
    /// there is none) and, when <see cref="ShowName"/>, the username beside it. Reads
    /// <see cref="ChasterService.Profile"/> and repaints itself on ProfileChanged / LinkChanged,
    /// so a host only places it. Before the profile arrives the name reads "Chaster".
    /// </summary>
    public sealed class ChasterAccountBadge : StackPanel
    {
        public static readonly DependencyProperty AvatarSizeProperty = DependencyProperty.Register(
            nameof(AvatarSize), typeof(double), typeof(ChasterAccountBadge),
            new PropertyMetadata(22.0, (d, _) => ((ChasterAccountBadge)d).Repaint()));

        public static readonly DependencyProperty ShowNameProperty = DependencyProperty.Register(
            nameof(ShowName), typeof(bool), typeof(ChasterAccountBadge),
            new PropertyMetadata(true, (d, _) => ((ChasterAccountBadge)d).Repaint()));

        public double AvatarSize { get => (double)GetValue(AvatarSizeProperty); set => SetValue(AvatarSizeProperty, value); }
        public bool ShowName { get => (bool)GetValue(ShowNameProperty); set => SetValue(ShowNameProperty, value); }

        /// <summary>The name text, so a host can match its font to the line it sits in.</summary>
        public TextBlock NameText { get; }

        private readonly Grid _face = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly Ellipse _plate = new() { Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x8A)) };
        private readonly TextBlock _letter = new()
        {
            Foreground = Brushes.White, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        private readonly Ellipse _picture = new() { Visibility = Visibility.Collapsed };
        private readonly Ellipse _ring = new() { Stroke = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1 };
        private bool _subscribed;

        // One decode per picture for the whole app: the strip and Settings show the same bytes.
        private static byte[]? _decodedFrom;
        private static ImageSource? _decoded;

        public ChasterAccountBadge()
        {
            Orientation = Orientation.Horizontal;
            VerticalAlignment = VerticalAlignment.Center;
            _face.Children.Add(_plate);
            _face.Children.Add(_letter);
            _face.Children.Add(_picture);
            _face.Children.Add(_ring);
            Children.Add(_face);
            NameText = new TextBlock
            {
                Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Children.Add(NameText);
            Loaded += (_, __) => { Subscribe(true); Repaint(); };
            Unloaded += (_, __) => Subscribe(false);
        }

        private void Subscribe(bool on)
        {
            var chaster = App.Chaster;
            if (chaster == null || on == _subscribed) return;
            _subscribed = on;
            if (on) { chaster.ProfileChanged += OnChanged; chaster.LinkChanged += OnChanged; }
            else { chaster.ProfileChanged -= OnChanged; chaster.LinkChanged -= OnChanged; }
        }

        private void OnChanged() => Dispatcher.BeginInvoke(new Action(Repaint));

        private void Repaint()
        {
            try
            {
                var chaster = App.Chaster;
                var profile = chaster?.Profile;
                var name = profile?.Username;
                var size = AvatarSize;
                _face.Width = _face.Height = size;
                _letter.FontSize = Math.Max(8, size * 0.5);
                _letter.Text = string.IsNullOrEmpty(name) ? "C" : char.ToUpperInvariant(name[0]).ToString();

                var image = profile == null ? null : Decode(chaster!.AvatarBytes);
                if (image != null)
                {
                    _picture.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                    _picture.Visibility = Visibility.Visible;
                }
                else
                {
                    _picture.Fill = null;
                    _picture.Visibility = Visibility.Collapsed;
                }

                NameText.Text = string.IsNullOrEmpty(name) ? Loc.Get("chaster_account_name") : name;
                NameText.Visibility = ShowName ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster account badge"); }
        }

        /// <summary>Bytes to a small frozen image, or null when they are not a picture WPF can read.
        /// Decoded at 64 px wide whatever the source size, so a huge file costs a small bitmap.</summary>
        internal static ImageSource? Decode(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            if (ReferenceEquals(bytes, _decodedFrom)) return _decoded;
            ImageSource? result = null;
            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.DecodePixelWidth = 64;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                result = bitmap;
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException or IOException)
            {
                Diag.Swallowed(ex, "chaster avatar is not a picture, the letter stays");
            }
            _decodedFrom = bytes;
            _decoded = result;
            return result;
        }
    }
}
