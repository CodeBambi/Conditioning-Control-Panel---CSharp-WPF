using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace ConditioningControlPanel.Helpers
{
    /// <summary>
    /// Resolves an emoji string ("🎮", "🙏") to a color ImageSource from the
    /// bundled Twemoji SVG set. WPF's TextBlock can't render COLR/CPAL color
    /// fonts (Segoe UI Emoji renders monochrome silhouettes), so any color
    /// emoji in the UI goes through here.
    ///
    /// Filename convention: lowercase hex codepoints joined by "-".
    /// FE0F (variation selector) is stripped when other codepoints exist;
    /// kept for bare single-codepoint emojis that ship as "XXXX-fe0f.svg".
    ///
    /// First lookup parses the SVG (~5-10 ms); subsequent lookups are cache hits.
    /// </summary>
    public static class EmojiImage
    {
        private static readonly ConcurrentDictionary<string, ImageSource?> _cache = new();
        private static readonly WpfDrawingSettings _settings = new()
        {
            IncludeRuntime = false,
            TextAsGeometry = true,
        };

        public static ImageSource? Get(string? emoji)
        {
            if (string.IsNullOrWhiteSpace(emoji)) return null;
            return _cache.GetOrAdd(emoji!, LoadForEmoji);
        }

        private static ImageSource? LoadForEmoji(string emoji)
        {
            var codepoints = new List<int>();
            for (int i = 0; i < emoji.Length;)
            {
                int cp = char.ConvertToUtf32(emoji, i);
                codepoints.Add(cp);
                i += char.IsSurrogatePair(emoji, i) ? 2 : 1;
            }
            if (codepoints.Count == 0) return null;

            // Twemoji strips FE0F (variation selector) when other codepoints
            // are present, but keeps it for single-codepoint files like "23-fe0f.svg".
            var stripped = codepoints.Where(c => c != 0xFE0F).ToList();

            return TryLoadByCodepoints(stripped) ?? TryLoadByCodepoints(codepoints);
        }

        private static ImageSource? TryLoadByCodepoints(List<int> codepoints)
        {
            if (codepoints.Count == 0) return null;
            var name = string.Join("-", codepoints.Select(c => c.ToString("x", CultureInfo.InvariantCulture)));
            return TryLoadByName(name);
        }

        private static ImageSource? TryLoadByName(string name)
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/Resources/Twemoji/{name}.svg", UriKind.Absolute);
                var resource = Application.GetResourceStream(uri);
                if (resource == null) return null;

                using var stream = resource.Stream;
                var reader = new FileSvgReader(_settings);
                var drawing = reader.Read(stream);
                if (drawing == null) return null;

                var image = new DrawingImage(drawing);
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// XAML binding glue. Source: an emoji string. Target: ImageSource for an Image element.
    /// Returns null on miss so callers can DataTrigger a TextBlock fallback if desired.
    /// </summary>
    public class EmojiToImageSourceConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => EmojiImage.Get(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns Visible when an emoji string has NO matching Twemoji asset
    /// (user typed plain text like "yes" or an unmapped glyph). Pair with
    /// EmojiToImageSourceConverter on a sibling Image so the TextBlock
    /// fallback only shows when the image would be blank.
    /// </summary>
    public class EmojiFallbackVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => EmojiImage.Get(value as string) == null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
