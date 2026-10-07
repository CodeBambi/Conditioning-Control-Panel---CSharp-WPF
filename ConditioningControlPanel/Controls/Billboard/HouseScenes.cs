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
                _ => new PosterArtView(posterPath),
            };
    }
}
