namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// How a LibVLC player that should make no sound is kept silent.
    ///
    /// On Windows LibVLC's mmdevice output keeps Mute and Volume on ONE audio session shared by
    /// every player in the process, whichever LibVLC instance built it. A preview player
    /// that set <c>Mute = true</c> or <c>Volume = 0</c> therefore silenced whatever else was
    /// playing: a picture clip on a flash or bubble killed the Bubble Count video's sound part way
    /// through while the bubble pops, which play through NAudio, kept going (ccp-bugs #1260).
    ///
    /// The rule: a silent player gets <see cref="NoAudioOption"/> on its Media and never touches
    /// Mute or Volume. Only the players that carry the sound (the mandatory video, the Bubble Count
    /// primary and the like) may write them. LibVlcSilenceTests holds the source to it.
    /// </summary>
    public static class LibVlcSilence
    {
        /// <summary>Media option that skips the audio track entirely.</summary>
        public const string NoAudioOption = ":no-audio";
    }
}
