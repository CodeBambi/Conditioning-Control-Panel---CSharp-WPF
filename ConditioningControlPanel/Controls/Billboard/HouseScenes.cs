using System.IO;
using System.Windows.Media;
using ConditioningControlPanel.Services.Billboard;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>
    /// The house cards draw their own animated scene (owner, 2026-10-07: the living posters read
    /// badly). The card's art data is still the poster path; its file stem picks the scene, and
    /// any other poster keeps the <see cref="PosterArtView"/>.
    /// </summary>
    public static class HouseScenes
    {
        public static System.Windows.FrameworkElement Create(string? posterPath) =>
            (posterPath == null ? null : Path.GetFileNameWithoutExtension(posterPath)) switch
            {
                "discord" => new DiscordHouseArt(),
                "webapp" => new WebAppHouseArt(),
                "remix" => new RemixHouseArt(),
                "loom" => new LoomHouseArt(),
                "support" => new SupportHouseArt(),
                "backroom" => new WheelArtView(),
                _ => new PosterArtView(posterPath),
            };

        /// <summary>
        /// Stems that only ever existed as a drawn scene: their poster path is a scene key with no
        /// file behind it (the Daily Daze wheel, 2026-10-07).
        /// </summary>
        public static bool DrawnOnly(string? posterPath) =>
            posterPath != null && Path.GetFileNameWithoutExtension(posterPath) == "backroom";
    }
}
