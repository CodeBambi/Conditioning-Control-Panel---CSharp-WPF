using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The release-content seam. The mod service needs four things from the downloadable-pack
    /// service and nothing else: the ids of the mod packs, the install stamp of a pack, the
    /// manifest of a pack, and to hear when a pack finishes installing. The pack service itself
    /// drives downloads and a web view and stays in the head; it seeds the two providers and
    /// raises the event, and the ids are constants it aliases.
    ///
    /// <para>Unseeded means "no packs on this head": stamps and infos are null, the event never
    /// fires, and the mod service behaves as it does before the pack service exists at startup.</para>
    /// </summary>
    public static class CoreReleaseContent
    {
        public const string PackModBambi = "mod-bambi";
        public const string PackModSissy = "mod-sissy";
        public const string PackModLocked = "mod-locked";
        public const string PackModDrone = "mod-drone";
        public const string PackModInfection = "mod-infection";

        public static volatile Func<string, InstalledPackStamp?>? StampProvider;
        public static volatile Func<string, ContentPackInfo?>? PackInfoProvider;

        /// <summary>
        /// The running build's version string and its patch notes. Every head seeds both: the
        /// Windows head from <c>UpdateService</c> (the hardcoded release constants the installer
        /// flow keys on), the Avalonia head from its own assembly. Core cannot read the version
        /// itself - <c>Assembly.GetEntryAssembly()</c> is the head, or null when hosted, and
        /// <c>typeof(X).Assembly</c> changes meaning the moment a type moves - so an unseeded
        /// head answers "0.0.0" and no notes rather than reporting the engine's own identity as
        /// the app's.
        /// </summary>
        public static volatile Func<string?>? AppVersionProvider;

        /// <summary>
        /// Runs a settings write on the UI thread and blocks until it ran. Returns false while the
        /// UI is shutting down (the action was skipped). Null = run inline (tests, headless).
        /// </summary>
        public static volatile Func<Action, bool>? UiInvoke;
        public static volatile Func<string?>? PatchNotesProvider;

        public static string AppVersion
        {
            get
            {
                try
                {
                    var seeded = AppVersionProvider?.Invoke();
                    if (!string.IsNullOrWhiteSpace(seeded)) return seeded!;
                }
                catch { /* fall through to the unseeded answer */ }
                return "0.0.0";
            }
        }

        /// <summary>Empty when no head has seeded notes; never null.</summary>
        public static string PatchNotes
        {
            get
            {
                try { return PatchNotesProvider?.Invoke() ?? ""; } catch { return ""; }
            }
        }

        public static InstalledPackStamp? GetStampFor(string packId)
        {
            try { return StampProvider?.Invoke(packId); } catch { return null; }
        }

        public static ContentPackInfo? GetPackInfo(string packId)
        {
            try { return PackInfoProvider?.Invoke(packId); } catch { return null; }
        }

        /// <summary>A pack finished installing. Raised on the download thread, as the head's own
        /// event is; subscribers marshal.</summary>
        public static event EventHandler<string>? PackInstalled;

        public static void RaisePackInstalled(object? sender, string packId)
        {
            try { PackInstalled?.Invoke(sender, packId); } catch { /* a subscriber's fault is not the installer's */ }
        }

        /// <summary>
        /// Extensions that count as pack-delivered media. PNG is in deliberately: the packs carry
        /// portrait/pose art as well as voice (builtin-sissyhypno ships 312 PNGs next to its ~1,700
        /// audio files) and a mod whose art did not survive is just as broken to the user as a
        /// silent one. The <c>.json</c> manifests in these folders stay in the installer, so they
        /// must never count — that is what let a stripped install read as fully stocked.
        /// </summary>
        private static readonly string[] MediaExtensions = { ".mp3", ".wav", ".ogg", ".m4a", ".png" };

        /// <summary>Media files among <paramref name="paths"/>, counting no further than <paramref name="stopAt"/>.</summary>
        public static int CountMediaFiles(IEnumerable<string>? paths, int stopAt = int.MaxValue)
        {
            if (paths == null || stopAt <= 0) return 0;

            var count = 0;
            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                var ext = Path.GetExtension(path);
                if (string.IsNullOrEmpty(ext)) continue;

                foreach (var known in MediaExtensions)
                {
                    if (!ext.Equals(known, StringComparison.OrdinalIgnoreCase)) continue;
                    if (++count >= stopAt) return count;
                    break;
                }
            }
            return count;
        }
    }
}
