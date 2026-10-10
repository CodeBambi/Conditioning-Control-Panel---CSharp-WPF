using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Arcademy;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The Arcademy host, the projection half (WPF ArcademyHostService.cs :678-1720 BuildInit and its
    /// builders, :3430-3640 set-setting, :5762-5890 the settings watch). Field names are the page's
    /// contract (BUILD-CONTRACT 4.1); consent and ceiling flags go out already resolved.
    /// </summary>
    internal sealed partial class GameWindow
    {
        private const string ArcDeepEndBoardSizeKey = "de_board_size";
        private const string ArcDeepEndWideBoard = "5x5";
        private const int ArcMaxGameSettingsBagChars = 60000;
        private const int ArcMaxKeybindsJsonChars = 7000;

        internal object BuildArcademyInit()
        {
            var s = CoreSettings.Current;
            var now = DateTime.Now;
            var phrases = BuildArcademyWords();   // ONE shuffled draw feeds words AND triggers
            return new
            {
                type = "init",
                protocol = 1,
                platform = new { isTouch = false, hasHaptics = false, host = "desktop" },   // SEAM(haptics): WPF App.Haptics.IsConnected
                modId = CoreMods.ActiveModId ?? "builtin-bambisleep",
                lexicon = MergeArcademyModTable(ArcademyLexicon.NeutralLexicon, "lexicon.json"),
                palette = MergeArcademyModTable(ArcademyLexicon.NeutralPalette, "palette.json"),
                masterIntensity = s?.ArcademyMasterIntensity ?? 0.7,
                caps = BuildArcademyCaps(s),
                effectIntensity = s?.ChaosEffectIntensity ?? 0.85,
                audioLevels = BuildArcademyAudioLevels(s),
                audioMute = s?.ArcademyAudioMute ?? false,
                // WPF launches its view with --autoplay-policy=no-user-gesture-required and says so here.
                // The port's WebHost takes no browser arguments, so the page waits for a click (its web behaviour).
                autoplayOk = false,
                sfxSamples = ArcademyStems("sfx"),
                masterVolume = Math.Clamp((s?.MasterVolume ?? 32) / 100.0, 0.0, 1.0),
                remoteMediaEnabled = ArcademyRemoteMediaEnabled(),
                remoteMediaRatio = Math.Clamp((s?.RemoteMediaRatio ?? 30) / 100.0, 0.0, 1.0),
                offlineMode = s?.OfflineMode ?? false,
                audioAudible = s?.SubAudioAudible ?? false,
                audioOnlySession = false,
                protectBrowserVideo = s?.ProtectBrowserVideoPlayback ?? true,
                motionLevel = ArcademyMotionLevel(),
                performanceMode = s?.PerformanceMode ?? false,
                reducedMotion = ArcademyMotionLevel() != 2,
                words = phrases,
                // Recorded clips only, on the asset server's ccp.subaudio / ccp.modaudio routes; a phrase
                // with no clip (or a mod the audio policy keeps off the Bambi clips) stays a text row.
                triggers = BuildArcademyTriggers(phrases),
                houseTriggers = BuildArcademyHouseTriggers(),
                utcDateSeed = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                localDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                overrideCalendar = LoadArcademyOverrideCalendar(),
                meta = _arcMeta?.Snapshot() ?? new JObject(),
                settings = BuildArcademySettingsBag(s),
                keybinds = ArcParseObject(s?.ArcademyKeybindsJson),
                hideTutorial = s?.ArcademyHideTutorial ?? false,
                presenceShare = ArcademyPresenceShare(s),
                panicKeyEnabled = s?.PanicKeyEnabled ?? true,
                panicKey = s?.PanicKey ?? "Escape",
                devDoor = false,
                subject = BuildArcademySubject(s),
                profile = BuildArcademyProfile(s),
                economy = BuildArcademyEconomy(),
            };
        }

        /// <summary>WPF SeedNativeState, the audio-only half: init is a snapshot of settings, not of what
        /// is happening now. The mandatory-video and browser-video suspends are not ported.</summary>
        private void SeedArcademyNativeState()
        {
            try { if (CoreSettings.Current?.AudioOnlySession == true) Post(new { type = "suspend", on = true, reason = "audio-only" }); }
            catch (Exception ex) { Log.Debug("[Game] arcademy seed: {E}", ex.Message); }
        }

        private object BuildArcademyEconomy()
        {
            try
            {
                var payday = ArcademyWalletSyncService.ServerPayday ?? ArcademyEconomy.PickPayday(
                    DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), _arcMeta?.EnrolledGameKeys());
                var (extra, honors) = _arcMeta?.LeverUnlocks() ?? (false, false);
                return new
                {
                    catalog = ArcademyEconomy.CatalogJson(),
                    payday = new { gameKey = payday.GameKey, mult = payday.Mult },
                    leverUnlocks = new { extra, honors },
                    settingUnlocks = new JArray
                    {
                        new JObject
                        {
                            ["key"] = ArcDeepEndBoardSizeKey,
                            ["value"] = ArcDeepEndWideBoard,
                            ["sku"] = ArcademyEconomy.SkuDeepEndWideBoard,
                            ["owned"] = _arcMeta?.WalletOwns(ArcademyEconomy.SkuDeepEndWideBoard) == true,
                        },
                    },
                };
            }
            catch (Exception ex)
            {
                Log.Debug("[Game] arcademy economy: {E}", ex.Message);
                return new
                {
                    catalog = new JArray(),
                    payday = new { gameKey = (string?)null, mult = 1 },
                    leverUnlocks = new { extra = false, honors = false },
                    settingUnlocks = new JArray(),
                };
            }
        }

        /// <summary>The student ID. The page keeps its own EMI and her name; nothing here feeds the desktop
        /// companion. avatarUrl stays null: the avatar cache (WPF ArcademyAvatarCache) is not ported.</summary>
        private static object BuildArcademyProfile(AppSettings? s)
        {
            try
            {
                var linked = AccountSeed.Discord?.IsAuthenticated == true;
                var name = CoreAccount.DisplayName;
                return new
                {
                    name = string.IsNullOrWhiteSpace(name) ? null : name!.Trim(),
                    avatarUrl = (string?)null,
                    discordLinked = linked,
                    presenceShare = ArcademyPresenceShare(s),
                };
            }
            catch (Exception ex)
            {
                Log.Debug("[Game] arcademy profile: {E}", ex.Message);
                return new { name = (string?)null, avatarUrl = (string?)null, discordLinked = false, presenceShare = "off" };
            }
        }

        private void PushArcademyProfile(string? result = null)
        {
            try { Post(new { type = "profile", profile = BuildArcademyProfile(CoreSettings.Current), result }); }
            catch (Exception ex) { Log.Debug("[Game] arcademy profile push: {E}", ex.Message); }
        }

        private static object BuildArcademyCaps(AppSettings? s) => new
        {
            flashRate = s?.ArcademyCapFlashRate ?? 1.0,
            flashOpacity = s?.ArcademyCapFlashOpacity ?? 1.0,
            subDensity = s?.ArcademyCapSubDensity ?? 1.0,
            duckDepth = s?.ArcademyCapDuckDepth ?? 1.0,
            bubbleRate = s?.ArcademyCapBubbleRate ?? 1.0,
            binauralDepth = s?.ArcademyCapBinauralDepth ?? 1.0,
            bgIntensity = s?.ArcademyCapBgIntensity ?? 1.0,
        };

        private static object BuildArcademyAudioLevels(AppSettings? s)
        {
            var levels = s?.ArcademyAudioLevels ?? AppSettings.DefaultArcademyAudioLevels();
            double Level(string group, double fallback) =>
                levels != null && levels.TryGetValue(group, out var v) && double.IsFinite(v)
                    ? Math.Clamp(v, 0.0, AppSettings.ArcademyAudioCeiling(group))
                    : fallback;
            return new
            {
                fx = Level("fx", 0.48),
                voice = Level("voice", 0.85),
                tutorial = Level("tutorial", 0.85),
                drops = Level("drops", 0.4),
                music = Level("music", 1.0),
            };
        }

        private const string ArcSubjectSalt = "ccp-annex-subject-2026";

        private static readonly string[] ArcSubjectWords =
        {
            "paper", "drawer", "folder", "carbon", "staple", "filing", "copier", "binder",
            "archive", "cabinet", "printer", "lamp", "stamp", "memo", "index", "teal",
        };

        /// <summary>The annex terminal's subject file: resolved here so the page downstairs counts nothing itself.
        /// The code and password are deterministic theatre off the install identity, the same derivation as WPF.</summary>
        internal static (string Code, string Password) ArcademySubjectCredentials(AppSettings? s)
        {
            var identity = s?.UnifiedId;
            if (string.IsNullOrWhiteSpace(identity))
                identity = "offline:" + (s?.InstallDate ?? "") + ":" + (s?.OfflineUsername ?? "");
            using var mac = new HMACSHA256(Encoding.UTF8.GetBytes(ArcSubjectSalt));
            var h = mac.ComputeHash(Encoding.UTF8.GetBytes(identity));
            var hex = Convert.ToHexString(h, 0, 6);
            var code = hex.Substring(0, 4) + "-" + hex.Substring(4, 4) + "-" + hex.Substring(8, 4);
            var password = ArcSubjectWords[h[6] & 0x0F] + "-" + ArcSubjectWords[h[7] & 0x0F] + "-"
                + (h[8] % 100).ToString("00", CultureInfo.InvariantCulture);
            return (code, password);
        }

        private static object BuildArcademySubject(AppSettings? s)
        {
            try
            {
                var (code, password) = ArcademySubjectCredentials(s);
                var p = App.Achievements?.Progress;
                return new
                {
                    code,
                    password,
                    date = s?.InstallDate,
                    level = s?.PlayerLevel ?? 0,
                    xp = p?.TotalXPEarned ?? 0,
                    minutes = s?.TotalConditioningMinutes ?? 0,
                    videoMinutes = p?.TotalVideoMinutes ?? 0,
                    spiralMinutes = p?.TotalSpiralMinutes ?? 0,
                    achievements = p?.UnlockedAchievements?.Count ?? 0,
                    appStreak = s?.CurrentStreak ?? 0,
                    appStreakBest = s?.HighestStreak ?? 0,
                    sessionsStarted = p?.TotalSessionsStarted ?? 0,
                    flashes = p?.TotalFlashImages ?? 0,
                    bubbles = p?.TotalBubblesPopped ?? 0,
                    lockCards = p?.TotalLockCardsCompleted ?? 0,
                    keywordTriggers = p?.KeywordTriggersFired ?? 0,
                };
            }
            catch (Exception ex)
            {
                Log.Debug("[Game] arcademy subject: {E}", ex.Message);
                return new { };
            }
        }

        // ---- words, clips, tables ------------------------------------------------------------

        private static string[] BuildArcademyWords()
        {
            try
            {
                var active = CoreSettings.Current?.SubliminalPool?.Where(kv => kv.Value).Select(kv => kv.Key)
                    .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
                if (active == null || active.Count == 0) return Array.Empty<string>();
                var rng = new Random();
                for (int i = active.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (active[i], active[j]) = (active[j], active[i]);
                }
                return active.Take(60).ToArray();
            }
            catch (Exception ex)
            {
                Log.Debug("[Game] arcademy words: {E}", ex.Message);
                return Array.Empty<string>();
            }
        }

        private static readonly string[] ArcHouseWords =
        {
            "FOCUS", "RELAX", "BREATHE", "LET GO", "SINK", "DEEPER", "DRIFT", "BLANK",
            "EMPTY", "LISTEN", "OBEY", "GOOD", "AGAIN", "STAY", "SMILE", "SOFTER",
            "MELT", "CALM", "DROP", "TRUST", "QUIET", "OPEN", "GIVE IN", "FLOAT",
        };

        private static readonly Dictionary<string, string[]> ArcStemCache = new(StringComparer.Ordinal);

        /// <summary>Bare mp3 names in the page's own assets/&lt;folder&gt; (WPF BuildSfxSamples / SublimStems).</summary>
        private static string[] ArcademyStems(string folder)
        {
            lock (ArcStemCache)
            {
                if (ArcStemCache.TryGetValue(folder, out var hit)) return hit;
                string[] stems;
                try
                {
                    var dir = Path.Combine(AppContext.BaseDirectory, "Resources", "web", "arcademy", "assets", folder);
                    stems = !Directory.Exists(dir)
                        ? Array.Empty<string>()
                        : Directory.EnumerateFiles(dir, "*.mp3", SearchOption.TopDirectoryOnly)
                            .Select(f => Path.GetFileNameWithoutExtension(f) ?? string.Empty)
                            .Where(n => !string.IsNullOrWhiteSpace(n))
                            .OrderBy(n => n, StringComparer.Ordinal)
                            .ToArray();
                }
                catch (Exception ex)
                {
                    Log.Debug("[Game] arcademy stems {F}: {E}", folder, ex.Message);
                    stems = Array.Empty<string>();
                }
                return ArcStemCache[folder] = stems;
            }
        }

        /// <summary>The school's own 24 words that have a recorded clip beside the page. Listed always,
        /// audible only under SubAudioAudible (page-relative urls, so they resolve on any host).</summary>
        private static object[] BuildArcademyHouseTriggers()
        {
            try
            {
                var have = new HashSet<string>(ArcademyStems("sublim"), StringComparer.OrdinalIgnoreCase);
                if (have.Count == 0) return Array.Empty<object>();
                var audible = CoreSettings.Current?.SubAudioAudible == true;
                var rows = new List<object>(ArcHouseWords.Length);
                foreach (var word in ArcHouseWords)
                {
                    var slug = word.Trim().ToLowerInvariant().Replace(' ', '_');
                    if (slug.Length == 0 || !have.Contains(slug)) continue;
                    rows.Add(new { text = word, audio = audible ? "./assets/sublim/" + slug + ".mp3" : null });
                }
                return rows.ToArray();
            }
            catch (Exception ex)
            {
                Log.Debug("[Game] arcademy house triggers: {E}", ex.Message);
                return Array.Empty<object>();
            }
        }

        private static JObject MergeArcademyModTable(Dictionary<string, string> defaults, string fileName)
        {
            var result = new JObject();
            foreach (var kv in defaults) result[kv.Key] = kv.Value;
            try
            {
                var installed = CoreMods.ActiveModPackage?.InstalledPath;
                if (string.IsNullOrEmpty(installed)) return result;
                var path = Path.Combine(installed, "resources", "arcademy", fileName);
                if (!File.Exists(path)) return result;
                var mod = JObject.Parse(File.ReadAllText(path));
                foreach (var p in mod.Properties())
                {
                    if (!defaults.ContainsKey(p.Name) || p.Value.Type != JTokenType.String) continue;
                    var v = (string?)p.Value;
                    if (string.IsNullOrWhiteSpace(v) || v.Length > 96) continue;
                    result[p.Name] = v;
                }
            }
            catch (Exception ex) { Log.Debug("[Game] arcademy mod table {File}: {E}", fileName, ex.Message); }
            return result;
        }

        private static JObject? LoadArcademyOverrideCalendar()
        {
            try
            {
                var path = Path.Combine(CorePaths.UserData, "arcademy_calendar.json");
                return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null;
            }
            catch (Exception ex)
            {
                Log.Debug("[Game] arcademy calendar: {E}", ex.Message);
                return null;
            }
        }

        private static int ArcademyMotionLevel() =>
            global::ConditioningControlPanel.Motion.MotionGate.ResolveLevel(CoreSettings.Current?.MotionLevel ?? MotionLevel.Full, OsReducedMotion.AnimationsEnabled) switch
            {
                MotionLevel.Off => 0,
                MotionLevel.Reduced => 1,
                _ => 2,
            };

        /// <summary>The remote-media gate, resolved once here exactly as WPF does.</summary>
        private static bool ArcademyRemoteMediaEnabled()
        {
            var s = CoreSettings.Current;
            return s != null && s.MediaSource != "local" && s.HasRemoteMediaConsent;
        }

        private static string ArcademyPresenceShare(AppSettings? s)
        {
            var v = (s?.ArcademyPresenceShare ?? "").Trim().ToLowerInvariant();
            return Array.IndexOf(AppSettings.ArcademyPresenceShares, v) >= 0 ? v : "off";
        }

        private static JObject? ArcParseObject(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JObject.Parse(json); } catch { return null; }
        }

        private static object[] BuildArcademySubLibrary()
        {
            var s = CoreSettings.Current;
            if (s == null) return Array.Empty<object>();
            return s.BuildRemoteSubLibraryView()
                .Select(r => (object)new { name = r.Name, ok = r.Ok, videoCount = r.VideoCount, stillOnly = r.StillOnly, selected = r.Selected })
                .ToArray();
        }

        /// <summary>The per-game bag plus what a game needs to let the player choose its media. localAssets
        /// and localFolders (the local sampler's manifest) are not ported: they go out empty, never guessed.</summary>
        private static JObject BuildArcademySettingsBag(AppSettings? s)
        {
            var bag = ArcParseObject(s?.ArcademySettingsJson) ?? new JObject();
            // Each wrapped on its own, so one bad folder cannot cost the page its whole settings bag.
            bag["localAssets"] = new JObject { ["gifs"] = new JArray(), ["stills"] = new JArray() };
            try { bag["localAssets"] = BuildArcademyLocalAssets(); }
            catch (Exception ex) { Log.Debug("[Game] arcademy bag local assets: {E}", ex.Message); }
            bag["localFolders"] = new JArray();
            try { bag["localFolders"] = ArcademyLocalMedia.BuildLocalFolders(ArcAssetsRoot, s?.DisabledAssetPaths); }
            catch (Exception ex) { Log.Debug("[Game] arcademy bag folders: {E}", ex.Message); }
            // The Loom's saved spirals on the asset server's ccp.spirals route (the page appends them to its bundled set).
            bag["loomSpirals"] = new JArray();
            try { bag["loomSpirals"] = BuildArcademyLoomSpirals(); }
            catch (Exception ex) { Log.Debug("[Game] arcademy bag loom: {E}", ex.Message); }
            try
            {
                var catalog = new JArray();
                foreach (var n in FypOnlineCoordinator.Catalog)
                    catalog.Add(new JObject { ["id"] = n.Id, ["label"] = n.Label, ["subs"] = new JArray(n.Subs ?? Array.Empty<string>()) });
                bag["remoteCatalog"] = catalog;
            }
            catch (Exception ex) { Log.Debug("[Game] arcademy bag catalog: {E}", ex.Message); }
            try { bag["subLibrary"] = JArray.FromObject(BuildArcademySubLibrary()); }
            catch (Exception ex) { Log.Debug("[Game] arcademy bag library: {E}", ex.Message); }
            try
            {
                var presets = new JArray();
                foreach (var p in s?.AssetPresets ?? new List<AssetPreset>())
                {
                    if (p == null || string.IsNullOrWhiteSpace(p.Id)) continue;
                    presets.Add(new JObject { ["id"] = p.Id, ["name"] = p.Name ?? "" });
                }
                bag["assetPresets"] = presets;
            }
            catch (Exception ex) { Log.Debug("[Game] arcademy bag presets: {E}", ex.Message); }
            bag["remoteMediaEnabled"] = ArcademyRemoteMediaEnabled();
            bag["remoteConsent"] = s?.HasRemoteMediaConsent ?? false;
            bag["mediaSource"] = s?.MediaSource ?? "local";
            return bag;
        }

        // ============================ set-setting ============================

        private void OnArcademySetSetting(JObject o)
        {
            var key = ((string?)o["key"] ?? "").Trim();
            if (key.Length == 0 || key.Length > 64) return;
            var s = CoreSettings.Current;
            if (s == null) return;
            object? echo;
            _arcSuppressSettingEcho = true;
            try { echo = ApplyArcademySetting(s, key, o["value"]); }
            catch (Exception ex)
            {
                Log.Warning("[Game] arcademy set-setting({Key}): {E}", key, ex.Message);
                return;
            }
            finally { _arcSuppressSettingEcho = false; }
            try { CoreSettings.Save(); } catch (Exception ex) { Log.Debug("[Game] arcademy settings save: {E}", ex.Message); }
            // Only the echo moves the page: it is what is STORED, post-clamp, never what was asked for.
            Post(new { type = "setting", key, value = echo });
        }

        private object? ApplyArcademySetting(AppSettings s, string key, JToken? value)
        {
            double Num(double fallback)
            {
                double d;
                try { d = (double?)value ?? fallback; } catch { d = fallback; }
                return double.IsFinite(d) ? d : fallback;
            }
            bool Flag(bool fallback) { try { return (bool?)value ?? fallback; } catch { return fallback; } }

            switch (ArcStrip(key, "caps."))
            {
                case "masterIntensity": s.ArcademyMasterIntensity = Num(0.7); return s.ArcademyMasterIntensity;
                case "flashRate": s.ArcademyCapFlashRate = Num(1.0); return s.ArcademyCapFlashRate;
                case "flashOpacity": s.ArcademyCapFlashOpacity = Num(1.0); return s.ArcademyCapFlashOpacity;
                case "subDensity": s.ArcademyCapSubDensity = Num(1.0); return s.ArcademyCapSubDensity;
                case "duckDepth": s.ArcademyCapDuckDepth = Num(1.0); return s.ArcademyCapDuckDepth;
                case "bubbleRate": s.ArcademyCapBubbleRate = Num(1.0); return s.ArcademyCapBubbleRate;
                case "binauralDepth": s.ArcademyCapBinauralDepth = Num(1.0); return s.ArcademyCapBinauralDepth;
                case "bgIntensity": s.ArcademyCapBgIntensity = Num(1.0); return s.ArcademyCapBgIntensity;
                case "effectIntensity": s.ChaosEffectIntensity = Num(0.85); return s.ChaosEffectIntensity;
                // The one consent flag the page may write: an allowlist, so a bad frame can only REDUCE what shows.
                case "presenceShare":
                    s.ArcademyPresenceShare = (value?.Type == JTokenType.String ? (string?)value : null) ?? "off";
                    return s.ArcademyPresenceShare;
                case "audioMute": s.ArcademyAudioMute = Flag(false); return s.ArcademyAudioMute;
                case "hideTutorial": s.ArcademyHideTutorial = Flag(false); return s.ArcademyHideTutorial;
                case "masterVolume":
                    s.MasterVolume = (int)Math.Round(Math.Clamp(Num(0.32), 0.0, 1.0) * 100);
                    return Math.Clamp(s.MasterVolume / 100.0, 0.0, 1.0);
                case "remoteMediaRatio":
                    s.RemoteMediaRatio = (int)Math.Round(Math.Clamp(Num(0.30), 0.0, 1.0) * 100);
                    return Math.Clamp(s.RemoteMediaRatio / 100.0, 0.0, 1.0);
                case "keybinds":
                    // Refuse, never wipe: a non-object echoes what is stored.
                    if (value is not JObject kb) return ArcParseObject(s.ArcademyKeybindsJson);
                    var kbJson = kb.ToString(Formatting.None);
                    if (kbJson.Length > ArcMaxKeybindsJsonChars) return ArcParseObject(s.ArcademyKeybindsJson);
                    s.ArcademyKeybindsJson = kbJson;
                    return ArcParseObject(s.ArcademyKeybindsJson);
            }

            var group = ArcStrip(key, "audioLevels.");
            if (AppSettings.DefaultArcademyAudioLevels().ContainsKey(group))
            {
                var levels = s.ArcademyAudioLevels ?? AppSettings.DefaultArcademyAudioLevels();
                var clamped = Math.Clamp(Num(0.5), 0.0, AppSettings.ArcademyAudioCeiling(group));
                levels[group] = clamped;
                s.ArcademyAudioLevels = levels;   // reassign so the change is announced
                return clamped;
            }

            if (!ArcademyPrizeGateAllows(s, key, value))
                return (ArcParseObject(s.ArcademySettingsJson) ?? new JObject())[key];
            return SetArcademyGameSetting(s, key, value);
        }

        /// <summary>The wide board is a prize: the host refuses the write until the wallet owns it,
        /// whatever the page says (someone already on it before the counter existed keeps it).</summary>
        private bool ArcademyPrizeGateAllows(AppSettings s, string key, JToken? value)
        {
            if (!string.Equals(key, ArcDeepEndBoardSizeKey, StringComparison.Ordinal)) return true;
            if (!string.Equals((value as JValue)?.Value as string, ArcDeepEndWideBoard, StringComparison.Ordinal)) return true;
            try
            {
                var stored = (ArcParseObject(s.ArcademySettingsJson) ?? new JObject())[key] as JValue;
                if (string.Equals(stored?.Value as string, ArcDeepEndWideBoard, StringComparison.Ordinal)) return true;
                if (_arcMeta?.WalletOwns(ArcademyEconomy.SkuDeepEndWideBoard) == true) return true;
                Log.Information("[Game] arcademy: the wide board is not bought yet - '{Key}' refused", key);
                return false;
            }
            catch (Exception ex)
            {
                Log.Debug("[Game] arcademy prize gate: {E}", ex.Message);
                return true;   // a wallet we cannot read never costs the player a setting
            }
        }

        private static object? SetArcademyGameSetting(AppSettings s, string key, JToken? value)
        {
            if (value == null || value.Type is JTokenType.Object or JTokenType.Array) return null;
            if (value.Type == JTokenType.String && ((string?)value)?.Length > 256) return null;
            var bag = ArcParseObject(s.ArcademySettingsJson) ?? new JObject();
            if (bag[key] == null && bag.Count >= 200) return null;
            var previous = bag[key]?.DeepClone();
            bag[key] = value.DeepClone();
            bag.Remove("localAssets");   // a host-built manifest riding the bag at init; never persisted
            var json = bag.ToString(Formatting.None);
            if (json.Length > ArcMaxGameSettingsBagChars)
            {
                Log.Warning("[Game] arcademy: per-game settings bag would be {N} chars - '{Key}' refused, bag left intact", json.Length, key);
                return previous;
            }
            s.ArcademySettingsJson = json;
            return bag[key];
        }

        private static string ArcStrip(string key, string prefix) =>
            key.StartsWith(prefix, StringComparison.Ordinal) ? key[prefix.Length..] : key;

        // ============================ the settings watch ============================

        private AppSettings? _arcHookedSettings;

        private void HookArcademySettings(bool on)
        {
            try
            {
                if (_arcHookedSettings != null) _arcHookedSettings.PropertyChanged -= OnArcademySettingChanged;
                _arcHookedSettings = null;
                if (!on) return;
                var s = CoreSettings.Current;
                if (s == null) return;
                s.PropertyChanged += OnArcademySettingChanged;
                _arcHookedSettings = s;
            }
            catch (Exception ex) { Log.Debug("[Game] arcademy settings watch: {E}", ex.Message); }
        }

        /// <summary>A setting changed in the panel while the campus is up: the page hears it as the same
        /// `setting` echo its own writes get. An audio-only session starting mid-class suspends the class.</summary>
        private void OnArcademySettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (IsClosedOrClosing || !IsReady || sender is not AppSettings s) return;
            try
            {
                if (e.PropertyName == nameof(AppSettings.AudioOnlySession))
                {
                    Post(new { type = "suspend", on = s.AudioOnlySession, reason = "audio-only" });
                    return;
                }
                if (e.PropertyName is nameof(AppSettings.RemoteSubLibrary) or nameof(AppSettings.FypOnlineCustomSubs))
                {
                    PushArcademyLibrary();
                    return;
                }
                if (e.PropertyName == nameof(AppSettings.ArcademyPresenceShare)) PushArcademyProfile();
                if (_arcSuppressSettingEcho) return;   // the page's own write already gets a reply
                var (key, value) = ArcademyProjectedSetting(s, e.PropertyName);
                if (key != null) Post(new { type = "setting", key, value });
            }
            catch (Exception ex) { Log.Debug("[Game] arcademy setting changed: {E}", ex.Message); }
        }

        internal static (string? Key, object? Value) ArcademyProjectedSetting(AppSettings s, string? prop) => prop switch
        {
            nameof(AppSettings.ArcademyMasterIntensity) => ("masterIntensity", s.ArcademyMasterIntensity),
            nameof(AppSettings.ArcademyCapFlashRate) => ("caps.flashRate", s.ArcademyCapFlashRate),
            nameof(AppSettings.ArcademyCapFlashOpacity) => ("caps.flashOpacity", s.ArcademyCapFlashOpacity),
            nameof(AppSettings.ArcademyCapSubDensity) => ("caps.subDensity", s.ArcademyCapSubDensity),
            nameof(AppSettings.ArcademyCapDuckDepth) => ("caps.duckDepth", s.ArcademyCapDuckDepth),
            nameof(AppSettings.ArcademyCapBubbleRate) => ("caps.bubbleRate", s.ArcademyCapBubbleRate),
            nameof(AppSettings.ArcademyCapBinauralDepth) => ("caps.binauralDepth", s.ArcademyCapBinauralDepth),
            nameof(AppSettings.ArcademyCapBgIntensity) => ("caps.bgIntensity", s.ArcademyCapBgIntensity),
            nameof(AppSettings.ArcademyAudioMute) => ("audioMute", s.ArcademyAudioMute),
            nameof(AppSettings.ArcademyHideTutorial) => ("hideTutorial", s.ArcademyHideTutorial),
            nameof(AppSettings.ArcademyAudioLevels) => ("audioLevels", BuildArcademyAudioLevels(s)),
            nameof(AppSettings.ChaosEffectIntensity) => ("effectIntensity", s.ChaosEffectIntensity),
            nameof(AppSettings.MasterVolume) => ("masterVolume", Math.Clamp(s.MasterVolume / 100.0, 0.0, 1.0)),
            nameof(AppSettings.RemoteMediaRatio) => ("remoteMediaRatio", Math.Clamp(s.RemoteMediaRatio / 100.0, 0.0, 1.0)),
            nameof(AppSettings.MediaSource) => ("remoteMediaEnabled", ArcademyRemoteMediaEnabled()),
            nameof(AppSettings.OfflineMode) => ("offlineMode", s.OfflineMode),
            nameof(AppSettings.MotionLevel) => ("motionLevel", ArcademyMotionLevel()),
            nameof(AppSettings.ProtectBrowserVideoPlayback) => ("protectBrowserVideo", s.ProtectBrowserVideoPlayback),
            nameof(AppSettings.ArcademyPresenceShare) => ("presenceShare", ArcademyPresenceShare(s)),
            _ => (null, null),
        };
    }
}
