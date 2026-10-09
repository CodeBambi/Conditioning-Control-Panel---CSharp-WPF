namespace ConditioningControlPanel
{
    /// <summary>
    /// One-slot "Remember" snapshot: the conditioning config (as a Preset, which is
    /// progression-safe by construction) plus the premium toggle states + browser mute.
    /// XP/level/streak are never captured, so a recall can't roll back progression.
    /// Stored as JSON in <c>AppSettings.RememberedConfigJson</c>; both heads read and write it.
    /// </summary>
    public class RememberedConfig
    {
        public Models.Preset? Preset { get; set; }
        public bool Takeover { get; set; }
        public bool Awareness { get; set; }
        public bool Haptics { get; set; }
        public bool BrowserMuted { get; set; }
    }
}
