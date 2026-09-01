using System.Collections.Generic;

namespace ConditioningControlPanel.Models
{
    public class HelpContent
    {
        public string SectionId { get; set; } = "";
        public string Icon { get; set; } = "?";
        public string Title { get; set; } = "";
        public string WhatItDoes { get; set; } = "";
        public List<string> Tips { get; set; } = new();
        public string HowItWorks { get; set; } = "";

        // --- Video help system (optional) ---------------------------------
        // When set, a "?" affordance can open HelpVideoWindow to play a short
        // muted looping clip. All three are nullable/opt-in; absent => no clip.

        /// <summary>
        /// File name (or relative sub-path) of a bundled tutorial clip under
        /// Resources\tutorial_videos\, e.g. "calibration_intro.mp4". Resolved via
        /// Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources",
        /// "tutorial_videos", ClipFile). Null/blank => no video surface shown.
        /// </summary>
        public string? ClipFile { get; set; }

        /// <summary>
        /// Localization key for the caption shown below the clip, resolved via
        /// {loc:Str CaptionKey}. Null => no caption.
        /// </summary>
        public string? CaptionKey { get; set; }

        /// <summary>
        /// Absolute https URL for the "Watch full tutorial" button. Null/blank =>
        /// button hidden. Typically built from <see cref="App.TutorialBaseUrl"/>.
        /// </summary>
        public string? FullTutorialUrl { get; set; }

        public bool HasTips => Tips?.Count > 0;
        public bool HasHowItWorks => !string.IsNullOrEmpty(HowItWorks);
        public bool HasClip => !string.IsNullOrWhiteSpace(ClipFile);
    }
}
