namespace ConditioningControlPanel.Services.Billboard.Providers
{
    /// <summary>
    /// LIVE: the card ids only. WPF 7.1.5 LiveCards decides off the Lobby snapshot (open tables,
    /// friends in a game); the port has no Lobby service yet, so there is no Live provider and the
    /// deck simply starts at the next card. The ids stay so chip names and snoozes keep their keys.
    /// </summary>
    public static class LiveCards
    {
        public const string CardJoinFriend = "live.friend";
        public const string CardTables = "live.tables";
        public const string CardPlaying = "live.playing";

        /// <summary>The Callback target prefix: "join:chess:p_xxx", "join:goon:ABCD".</summary>
        public const string JoinPrefix = "join:";
    }
}
