using System;
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
    }
}
