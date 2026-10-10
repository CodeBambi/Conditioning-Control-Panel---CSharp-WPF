using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Arcademy
{
    /// <summary>
    /// The parts of WPF 7.1.5 ArcademyHostService that the REST OF THE APP reads, with or without the
    /// Arcademy window open (:852-1050): does the wallet hold a prize (<see cref="WalletOwnsSku"/>, the
    /// midnight tube glass), and which outfit the Locker armed for EMI (<see cref="EquippedEmiOutfit"/>,
    /// the desk mascot). Live store first (the head's window registers it), the saved file otherwise.
    /// Never throws; failing to read answers "no" / "standard art". The window and its frames are the
    /// head's (CCP.Avalonia Views/Games/GameWindow.Arcademy*.cs).
    /// </summary>
    internal static class ArcademyHostService
    {
        public const string ProductName = "The Arcademy";

        /// <summary>WPF DoorAvailable: the door itself. A field, never a const, so the launch guard is real code.</summary>
        public static bool DoorAvailable = true;

        /// <summary>See <see cref="ArcademyLocalMedia.AnimatedImageHint"/> (ccp-bugs#1086).</summary>
        internal const string AnimatedImageHint = ArcademyLocalMedia.AnimatedImageHint;

        internal static bool IsAnimatedLocalImage(string file) => ArcademyLocalMedia.IsAnimatedLocalImage(file);

        /// <summary>The open window's store (null = closed). Set and cleared by the head.</summary>
        internal static volatile ArcademyMetaStore? LiveMeta;

        /// <summary>Tests name the save file (CorePaths.UserData is fixed for the process).</summary>
        internal static string? MetaPathOverride;

        private static string MetaPath => MetaPathOverride ?? Path.Combine(CorePaths.UserData, "arcademy_meta.json");

        /// <summary>Raised (any thread) when the Locker's outfit may have changed: a <c>lockerOutfit</c>
        /// write from the page, and every Arcademy close. The desk mascot re-reads <see cref="EquippedEmiOutfit"/>.</summary>
        internal static event Action? EmiOutfitChanged;

        internal static void RaiseEmiOutfitChanged()
        {
            try { EmiOutfitChanged?.Invoke(); }
            catch (Exception ex) { Log.Debug("[Arcademy] outfit changed handler: {E}", ex.Message); }
        }

        // ---- the wallet, read from outside -----------------------------------------------------

        /// <summary>True when the Prize Counter sold this sku to the player: the live wallet when the
        /// Arcademy is open, the persisted blob when it is not.</summary>
        public static bool WalletOwnsSku(string sku)
        {
            if (string.IsNullOrWhiteSpace(sku)) return false;
            try
            {
                var live = LiveMeta;
                if (live != null) return live.WalletOwns(sku);
                return WalletOwnsOnDisk(sku);
            }
            catch (Exception ex)
            {
                Log.Debug("[Arcademy] WalletOwnsSku({Sku}): {E}", sku, ex.Message);
                return false;
            }
        }

        private static readonly object _walletDiskLock = new();
        private static HashSet<string>? _walletDiskInv;
        private static long _walletDiskStamp;   // ticks ^ length; any write moves it
        private static string? _walletDiskPath;

        private static long Stamp(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists ? (info.LastWriteTimeUtc.Ticks ^ info.Length) : 0L;
            }
            catch { return 0L; }
        }

        private static bool WalletOwnsOnDisk(string sku)
        {
            var path = MetaPath;
            long stamp = Stamp(path);
            lock (_walletDiskLock)
            {
                if (_walletDiskInv == null || _walletDiskStamp != stamp || _walletDiskPath != path)
                {
                    _walletDiskInv = ReadOwnedSkus(path);
                    _walletDiskStamp = stamp;
                    _walletDiskPath = path;
                }
                return _walletDiskInv.Contains(sku);
            }
        }

        internal static HashSet<string> ReadOwnedSkus(string path)
        {
            var owned = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(path)) return owned;
                var blob = JObject.Parse(File.ReadAllText(path));
                if (blob[ArcademyMetaStore.WalletKey] is not JObject wallet) return owned;
                if (wallet["inv"] is not JObject inv) return owned;
                foreach (var p in inv.Properties())
                {
                    // Same witness the counter uses: a row with a positive count is held.
                    if (p.Value is JObject row && (int?)row["n"] > 0) owned.Add(p.Name);
                }
            }
            catch (Exception ex) { Log.Debug("[Arcademy] ReadOwnedSkus: {E}", ex.Message); }
            return owned;
        }

        // ---- the Locker's outfit, read from outside ---------------------------------------------

        public const string EmiOutfitKey = "lockerOutfit";

        /// <summary>Every outfit the desk has art for, and the prize that buys it.</summary>
        internal static readonly IReadOnlyDictionary<string, string> EmiOutfitSku =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["varsity"] = "emi_varsity",
                ["labcoat"] = "emi_labcoat",
                ["cheer"] = "emi_cheer",
                ["swim"] = "emi_swim",
            };

        /// <summary>The outfit EMI wears on the desk, or null for the standard art: armed in the Locker,
        /// a name the desk knows, AND bought. An armed outfit that is not owned is standard art.</summary>
        public static string? EquippedEmiOutfit()
        {
            try
            {
                var live = LiveMeta;
                string? raw;
                if (live != null) { var t = live.Get(EmiOutfitKey); raw = t != null && t.Type == JTokenType.String ? (string?)t : null; }
                else raw = EmiOutfitOnDisk();

                var name = (raw ?? "").Trim();
                if (name.Length == 0 || !EmiOutfitSku.TryGetValue(name, out var sku)) return null;
                if (!WalletOwnsSku(sku))
                {
                    Log.Debug("[Arcademy] EquippedEmiOutfit: '{Outfit}' is armed but {Sku} is not owned - standard art", name, sku);
                    return null;
                }
                return name;
            }
            catch (Exception ex)
            {
                Log.Debug("[Arcademy] EquippedEmiOutfit: {E}", ex.Message);
                return null;
            }
        }

        private static readonly object _outfitDiskLock = new();
        private static string? _outfitDiskValue;
        private static long _outfitDiskStamp = -1L;   // -1 = never read
        private static string? _outfitDiskPath;

        private static string? EmiOutfitOnDisk()
        {
            var path = MetaPath;
            long stamp = Stamp(path);
            lock (_outfitDiskLock)
            {
                if (_outfitDiskStamp != stamp || _outfitDiskPath != path)
                {
                    _outfitDiskValue = ReadArmedOutfit(path);
                    _outfitDiskStamp = stamp;
                    _outfitDiskPath = path;
                }
                return _outfitDiskValue;
            }
        }

        private static string? ReadArmedOutfit(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var t = JObject.Parse(File.ReadAllText(path))[EmiOutfitKey];
                return t != null && t.Type == JTokenType.String ? (string?)t : null;
            }
            catch (Exception ex)
            {
                Log.Debug("[Arcademy] ReadArmedOutfit: {E}", ex.Message);
                return null;
            }
        }
    }
}
