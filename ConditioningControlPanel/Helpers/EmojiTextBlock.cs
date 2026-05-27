using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ConditioningControlPanel.Helpers
{
    /// <summary>
    /// Drop-in TextBlock replacement that auto-replaces color emojis in the
    /// Text string with inline Twemoji images. Anything WPF can't render in
    /// color (Segoe UI Emoji silhouettes) becomes a real color Image inline;
    /// the surrounding non-emoji text stays as Runs.
    ///
    /// Use anywhere a localized or hardcoded string contains a color emoji
    /// (e.g. en.json has "⚡ Flash Images"). Just swap TextBlock -> EmojiTextBlock
    /// and color emojis appear without touching the strings.
    ///
    /// Glyphs Twemoji doesn't ship (or pure ASCII text) fall through as Runs,
    /// so it's safe on plain text.
    /// </summary>
    public class EmojiTextBlock : TextBlock
    {
        private bool _rebuilding;

        static EmojiTextBlock()
        {
            TextProperty.OverrideMetadata(typeof(EmojiTextBlock),
                new FrameworkPropertyMetadata(string.Empty,
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    OnTextChanged));
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var self = (EmojiTextBlock)d;
            if (self._rebuilding) return;
            self.RebuildInlines((string?)e.NewValue ?? string.Empty);
        }

        private void RebuildInlines(string text)
        {
            _rebuilding = true;
            try
            {
                Inlines.Clear();
                foreach (var segment in Split(text))
                {
                    if (segment.IsEmoji)
                    {
                        var src = EmojiImage.Get(segment.Text);
                        if (src != null)
                        {
                            var size = FontSize > 0 ? FontSize * 1.15 : 16;
                            var img = new Image
                            {
                                Source = src,
                                Width = size,
                                Height = size,
                                Stretch = Stretch.Uniform,
                                VerticalAlignment = VerticalAlignment.Center,
                            };
                            Inlines.Add(new InlineUIContainer(img)
                            {
                                BaselineAlignment = BaselineAlignment.Center,
                            });
                            continue;
                        }
                    }
                    Inlines.Add(new Run(segment.Text));
                }
            }
            finally { _rebuilding = false; }
        }

        private readonly struct Segment
        {
            public readonly string Text;
            public readonly bool IsEmoji;
            public Segment(string text, bool isEmoji) { Text = text; IsEmoji = isEmoji; }
        }

        private static IEnumerable<Segment> Split(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;

            var buf = new System.Text.StringBuilder();
            bool bufIsEmoji = false;

            int i = 0;
            while (i < text.Length)
            {
                int cp = char.ConvertToUtf32(text, i);
                int step = char.IsSurrogatePair(text, i) ? 2 : 1;
                bool isEmoji = IsEmojiCodepoint(cp);

                // Group consecutive emoji codepoints (incl. ZWJ sequences and FE0F) together
                // so multi-codepoint emojis like 👯‍♀️ resolve as a single image.
                bool stitchToPrev = isEmoji && bufIsEmoji && cp == 0x200D; // ZWJ continues an emoji
                bool stitchToPrev2 = isEmoji && bufIsEmoji && cp == 0xFE0F; // variation selector

                if (buf.Length == 0)
                {
                    bufIsEmoji = isEmoji;
                    buf.Append(text, i, step);
                }
                else if (isEmoji == bufIsEmoji || stitchToPrev || stitchToPrev2 || (bufIsEmoji && (cp == 0x200D || cp == 0xFE0F)))
                {
                    buf.Append(text, i, step);
                }
                else
                {
                    yield return new Segment(buf.ToString(), bufIsEmoji);
                    buf.Clear();
                    bufIsEmoji = isEmoji;
                    buf.Append(text, i, step);
                }

                i += step;
            }

            if (buf.Length > 0)
                yield return new Segment(buf.ToString(), bufIsEmoji);
        }

        private static bool IsEmojiCodepoint(int cp)
        {
            // Conservative ranges covering color emojis used in the codebase.
            // Excludes pure typography (✕ ✓ ✔ ⚙ etc) where Twemoji either has
            // no asset or the monochrome glyph reads better in our UI.
            if (cp >= 0x1F300 && cp <= 0x1FAFF) return true; // Pictographs, Extended-A/B
            if (cp >= 0x2600 && cp <= 0x26FF)
            {
                // Misc Symbols — only mark as emoji if Twemoji actually has the asset.
                // Common ones used: ☀ (2600), ☁ (2601), ☂ (2602), ☃ (2603),
                // ★ (2605), ☆ (2606), ☎ (260E), ☑ (2611), ☢ (2622), ☣ (2623),
                // ☮ (262E), ☯ (262F), ⚡ (26A1), ⚽ (26BD), ⚾ (26BE)
                // Skip: ⚙ (2699), ⚠ (26A0), ☐ (2610) - keep monochrome
                return cp != 0x2610 && cp != 0x2699 && cp != 0x26A0;
            }
            if (cp >= 0x2700 && cp <= 0x27BF)
            {
                // Dingbats — skip pure typography
                return cp != 0x2713 && cp != 0x2714 && cp != 0x2715 && cp != 0x2716 && cp != 0x2717 && cp != 0x2718 && cp != 0x270F;
            }
            if (cp == 0x200D) return true; // ZWJ inside an emoji sequence
            if (cp == 0xFE0F) return true; // variation selector
            return false;
        }
    }
}
