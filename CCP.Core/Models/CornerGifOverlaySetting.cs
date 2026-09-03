namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// A single standalone corner-GIF overlay slot. Unlike the session-scoped corner GIF
    /// (see SessionEngine), these are driven by <see cref="Services.CornerGifService"/> and
    /// persist app-wide so users can pin a looping GIF to a screen corner outside any session.
    /// The Spiral card exposes up to two of these (two corners at once).
    /// </summary>
    public class CornerGifOverlaySetting
    {
        public bool Enabled { get; set; } = false;

        /// <summary>Absolute path to the GIF/image. Empty => fall back to the built-in spiral.</summary>
        public string GifPath { get; set; } = "";

        public CornerPosition Position { get; set; } = CornerPosition.BottomLeft;

        /// <summary>Longest-edge target size in logical pixels.</summary>
        public int Size { get; set; } = 300;

        /// <summary>Opacity in percent (1-100).</summary>
        public int Opacity { get; set; } = 18;
    }
}
