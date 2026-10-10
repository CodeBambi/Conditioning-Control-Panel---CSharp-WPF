using ConditioningControlPanel.Controls.Billboard;

namespace ConditioningControlPanel.Services.Billboard.Showcase
{
    /// <summary>Registers the showcase's art view ("clip") with the deck. Call once at startup.</summary>
    public static class ShowcaseArtRegistration
    {
        public static void Register() =>
            BillboardArt.Register(ShowcaseRules.ArtKey, data => new ClipArtView(data as ShowcaseClipArt));
    }
}
