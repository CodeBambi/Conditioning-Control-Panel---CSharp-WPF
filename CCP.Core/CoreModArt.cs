using System;
using System.IO;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The mod-art seam: "does the active mod (or event skin) replace this picture - or this
    /// sound - and where is the replacement on disk". The WPF head answers it with
    /// <c>Services.ModResourceResolver</c>, which cannot move here - it decodes to
    /// <c>System.Windows.Media.ImageSource</c> and falls back to <c>pack://</c> URIs, both head
    /// types. Only the RESOLUTION half is portable, so only the resolution half is here.
    ///
    /// <para><b>The contract is "override or nothing".</b> Every method answers about the
    /// OVERRIDE only; null means "no mod art for this name, draw your own built-in". Core must
    /// never learn how a head packages its shipped art - <c>pack://</c> on WPF, <c>avares://</c>
    /// on Avalonia, something else on VR - so do not add a fallback here. Each head decodes the
    /// returned absolute path itself (<c>Avalonia.Media.Imaging.Bitmap</c> / <c>BitmapSource</c>).
    /// </para>
    ///
    /// <para>Unseeded answers "no override" to everything, which is exactly right for a head with
    /// no mod service: it draws the art it ships. Nothing throws and nothing returns a null a
    /// caller would dereference - <see cref="HasOverride"/> is false, the paths are null, and a
    /// null path is the documented "use your own" answer rather than a failure.</para>
    ///
    /// <para>One delegate, not two: "has an override" IS "the path is not null", and a second
    /// bool provider would be a second chance for the two to disagree. Volatile for the reason
    /// spelled out in <see cref="CoreMods"/> - the head seeds on the startup thread while views
    /// read from wherever they happen to run.</para>
    /// </summary>
    public static class CoreModArt
    {
        /// <summary>
        /// Absolute file path of the active override for a Resources-relative logical name
        /// ("tube.png", "features/Phrase_Lock.png", "avatar_pose1.png"), or null when nothing
        /// overrides it. The head is handed an already-validated forward-slash relative path.
        /// </summary>
        public static volatile Func<string, string?>? OverridePathProvider;

        /// <summary>
        /// Whether the active mod ships an emotive-portrait avatar manifest AND its portraits are
        /// on disk. False means the legacy four-pose avatar, which is what a head with no mod
        /// layer must draw.
        /// </summary>
        public static volatile Func<bool>? HasAvatarPortraitsProvider;

        /// <summary>
        /// The override's absolute path, or null for "no override - draw your built-in art".
        /// Traversal is rejected HERE, before any head sees the string, because the name can come
        /// from mod-authored JSON: a rooted path or a <c>..</c> segment is answered null.
        /// </summary>
        public static string? OverridePath(string? resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath)) return null;

            var path = resourcePath!.Replace('\\', '/');
            if (path.Contains("..") || Path.IsPathRooted(path)) return null;

            var p = OverridePathProvider;
            if (p is null) return null;
            try { return p(path); } catch { return null; }
        }

        /// <summary>
        /// The active mod's replacement for a sounds-relative logical name ("giggle5.mp3",
        /// "bubbles/Pop.mp3", "chaos/heartbeat.mp3"), or null for "no mod cue - play your own".
        ///
        /// <para>Separate from <see cref="OverridePath"/> because the WPF chains genuinely
        /// differ: the image chain probes <c>resources/&lt;path&gt;</c>, the audio chain probes
        /// <c>resources/sounds/&lt;path&gt;</c> AND swaps .wav for .mp3 (and back) so a mod may
        /// ship either format. One provider serving both would have to guess which rule to
        /// apply.</para>
        ///
        /// <para>Override ONLY, same as the image half. Core must never learn where a head keeps
        /// its shipped cues - the WPF head ends on <c>ContentLocator</c>, another head may not -
        /// so a null here means "fall back the way you already do", never "silence".</para>
        /// </summary>
        public static volatile Func<string, string?>? AudioOverridePathProvider;

        /// <summary>
        /// The mod's replacement cue as an absolute path, or null for none. Traversal is rejected
        /// HERE, before any head sees the string: a cue name can come out of mod-authored JSON.
        /// </summary>
        public static string? AudioOverridePath(string? soundRelativePath)
        {
            if (string.IsNullOrEmpty(soundRelativePath)) return null;

            var path = soundRelativePath!.Replace('\\', '/');
            if (path.Contains("..") || Path.IsPathRooted(path)) return null;

            var p = AudioOverridePathProvider;
            if (p is null) return null;
            try { return p(path); } catch { return null; }
        }

        /// <summary>True when an author replaced this picture. Equivalent to a non-null
        /// <see cref="OverridePath"/>, and derived from it so the two cannot drift.</summary>
        public static bool HasOverride(string? resourcePath) => OverridePath(resourcePath) != null;

        /// <summary>
        /// The spiral GIF's override, probing both layouts a mod may use: <c>spirals/spiral.gif</c>
        /// (what the mod template scaffolds) then <c>spiral.gif</c> at the resources root. Null =
        /// no mod spiral, so the head draws its own. Mirrors
        /// <c>ModResourceResolver.ResolveSpiralUri</c>.
        /// </summary>
        public static string? SpiralOverridePath()
            => OverridePath("spirals/spiral.gif") ?? OverridePath("spiral.gif");

        /// <summary>Portrait-mode gate; false with no mod layer up. Faults are swallowed.</summary>
        public static bool HasAvatarPortraits
        {
            get { try { return HasAvatarPortraitsProvider?.Invoke() ?? false; } catch { return false; } }
        }
    }
}
