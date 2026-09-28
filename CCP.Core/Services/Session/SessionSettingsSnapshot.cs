using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The non-pool settings a session overrides, held in memory only: WPF SessionEngine.SaveCurrentSettings
    /// and RestoreSettings (SessionEngine.cs:1053-1114, 1659-1777), same fields, same order. The three
    /// phrase pools are <see cref="PhrasePoolCustody"/>'s. Plus SubliminalDuration: WPF writes it from the
    /// session's frames and never restores it, leaking it into the user's settings - a WPF bug not carried
    /// over (docs/avalonia-decisions.md).
    /// </summary>
    public sealed class SessionSettingsSnapshot
    {
        private readonly AppSettings _saved = new();

        public static SessionSettingsSnapshot Capture(AppSettings current)
        {
            var snapshot = new SessionSettingsSnapshot();
            Copy(current, snapshot._saved);
            return snapshot;
        }

        public void RestoreTo(AppSettings current) => Copy(_saved, current);

        private static void Copy(AppSettings from, AppSettings to)
        {
            to.FlashEnabled = from.FlashEnabled;
            to.FlashFrequency = from.FlashFrequency;
            to.FlashOpacity = from.FlashOpacity;
            to.FlashClickable = from.FlashClickable;
            to.CorruptionMode = from.CorruptionMode;
            to.FlashAudioEnabled = from.FlashAudioEnabled;
            to.ImageScale = from.ImageScale;
            to.SimultaneousImages = from.SimultaneousImages;
            to.SubliminalEnabled = from.SubliminalEnabled;
            to.SubliminalFrequency = from.SubliminalFrequency;
            to.SubliminalOpacity = from.SubliminalOpacity;
            to.SubliminalDuration = from.SubliminalDuration;
            to.SubAudioEnabled = from.SubAudioEnabled;
            to.SubAudioVolume = from.SubAudioVolume;
            to.AudioDuckingEnabled = from.AudioDuckingEnabled;
            to.DuckingLevel = from.DuckingLevel;
            to.PinkFilterEnabled = from.PinkFilterEnabled;
            to.PinkFilterOpacity = from.PinkFilterOpacity;
            to.SpiralEnabled = from.SpiralEnabled;
            to.SpiralOpacity = from.SpiralOpacity;
            to.BrainDrainEnabled = from.BrainDrainEnabled;
            to.BrainDrainIntensity = from.BrainDrainIntensity;
            to.BubblesEnabled = from.BubblesEnabled;
            to.BubblesFrequency = from.BubblesFrequency;
            to.BubblesClickable = from.BubblesClickable;
            to.BouncingTextEnabled = from.BouncingTextEnabled;
            to.BouncingTextSpeed = from.BouncingTextSpeed;
            to.BouncingTextSize = from.BouncingTextSize;
            to.BouncingTextOpacity = from.BouncingTextOpacity;
            to.MandatoryVideosEnabled = from.MandatoryVideosEnabled;
            to.VideosPerHour = from.VideosPerHour;
            to.LockCardEnabled = from.LockCardEnabled;
            to.LockCardFrequency = from.LockCardFrequency;
            to.PopQuizEnabled = from.PopQuizEnabled;
            to.PopQuizFrequency = from.PopQuizFrequency;
            to.BubbleCountEnabled = from.BubbleCountEnabled;
            to.BubbleCountFrequency = from.BubbleCountFrequency;
        }
    }
}
