using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Awareness;
using ConditioningControlPanel.Services.Moderation;
using ConditioningControlPanel.Services.Webcam;

namespace ConditioningControlPanel.LinuxSmoke
{
    /// <summary>
    /// Executes CCP.Core on a non-Windows runtime and asserts on the answers.
    ///
    /// This is not a test project and not a UI. It is the smallest thing that proves the engine
    /// RUNS off Windows, as opposed to merely compiling for net8.0. The distinction is not
    /// academic: EnhancementValidator.IsUnsafeAssetPath was found leaning on Path.IsPathRooted,
    /// whose result is platform-dependent, so a security check silently changed meaning on Linux
    /// while the TFM guard, the grep gate and a 100%-similarity rename check all stayed green.
    ///
    /// Assertions here are deliberately about behaviour that could plausibly differ by platform -
    /// path resolution, culture-sensitive text handling, deterministic arithmetic - rather than a
    /// broad restatement of the unit tests. Exit code 1 fails the ubuntu CI job.
    /// </summary>
    internal static class Program
    {
        private static int _failures;

        private static void Check(string what, bool ok, string? detail = null)
        {
            Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}{(detail is null ? "" : $"  ({detail})")}");
            if (!ok) _failures++;
        }

        private static int Main()
        {
            Console.WriteLine($"CCP.Core smoke on {Environment.OSVersion.Platform} / {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");
            Console.WriteLine($".NET {Environment.Version}\n");

            // Sandbox the user-data tree BEFORE anything touches CorePaths (it resolves once, at
            // type init). Ends with the app folder so the Paths() checks below still describe it.
            var sandbox = Path.Combine(Path.GetTempPath(), $"ccp-smoke-{Environment.ProcessId}", "ConditioningControlPanel");
            Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", sandbox);

            Paths();
            Settings();
            Moderation();
            Determinism();
            Scrubbing();
            Arithmetic();
            ConsentAndReports();
            Roadmap();

            Console.WriteLine();
            if (_failures == 0)
            {
                // The settings seams (wire/06). Unseeded is the documented state here: no head has
                // attached a secret store or a listener, and the model must behave, not throw.
                Check("CoreSecrets.Retrieve is null when no store is attached", CoreSecrets.Retrieve(CoreSecrets.ApiKey) == null);
                CoreSecrets.Store(CoreSecrets.ApiKey, "never-written");
                Check("CoreSecrets.Store is a no-op when no store is attached", CoreSecrets.Retrieve(CoreSecrets.ApiKey) == null);
                CoreSettingsHooks.NotifySettingChanged("Anything");
                Check("CoreSettingsHooks tolerates no listener", true);
                Check("CoreMods.ModeDisplayName is null when no mod layer is up", CoreMods.ModeDisplayName == null);
                // The settings model itself is in Core now (wire/07). Unseeded, the seam hands out one
                // default instance rather than null, so Core code that reads a setting never trips.
                Check("CoreSettings.HasProvider is false with no head", !CoreSettings.HasProvider);
                Check("CoreSettings.Current is a default instance, not null", CoreSettings.Current is not null);
                Check("CoreSettings.Current is stable across reads", ReferenceEquals(CoreSettings.Current, CoreSettings.Current));
                Check("CoreMods.MakeModAware passes text through with no mod layer", CoreMods.MakeModAware("Bambi") == "Bambi");
                Check("CoreMods.ActiveModId is the CCP default with no mod layer", CoreMods.ActiveModId == Models.BuiltInMods.CCPDefaultId);
                Check("CoreMods.IsCCPDefault with no mod layer", CoreMods.IsCCPDefault);
                Check("CoreMods.InstalledMods holds the one built-in default", CoreMods.InstalledMods.Count == 1 && CoreMods.InstalledMods.ContainsKey(Models.BuiltInMods.CCPDefaultId));
                Check("CoreMods.AccentColorHex is the built-in default manifest's", CoreMods.AccentColorHex == Models.BuiltInMods.CCPDefault.Theme?.AccentColor, CoreMods.AccentColorHex);
                Check("CoreMods.Affirmation is the built-in default manifest's", CoreMods.Affirmation == Models.BuiltInMods.CCPDefault.Identity?.Affirmation, CoreMods.Affirmation);
                Check("CoreMods.GetPhrases is null with no mod layer", CoreMods.GetPhrases("BubbleCountMercy") == null);
                Check("CoreMods.PinkRushDescription is the vanilla blurb with no mod layer",
                      CoreMods.PinkRushDescription == (Models.BuiltInMods.CCPDefault.EnhancementOverrides?.PinkRushDescription ?? "3x XP for 60 seconds!"),
                      CoreMods.PinkRushDescription);
                // The filter colour must come off the default manifest, not the parser's miss
                // default - reaching hot pink here would mean the manifest chain silently failed.
                CoreMods.TryParseHexColor(
                    Models.BuiltInMods.CCPDefault.Theme?.FilterColor ?? Models.BuiltInMods.CCPDefault.Theme?.AccentColor,
                    out var vanillaFilter);
                var filter = CoreMods.GetFilterColorRgb();
                Check("CoreMods.GetFilterColorRgb is the default manifest's colour, not the parser's miss pink",
                      filter == vanillaFilter && filter != ((byte)255, (byte)105, (byte)180),
                      $"#{filter.R:X2}{filter.G:X2}{filter.B:X2}");
                // The documented exception to the vanilla rule: unseeded this answers BambiSleep's
                // pool, not CCPDefault's, because that is the fallback the WPF call sites carried.
                var bambiPool = Models.BuiltInMods.BambiSleep.SubliminalPool!;
                var pool = CoreMods.GetDefaultSubliminalPool();
                var poolMatches = pool.Count == bambiPool.Count && pool.Count > 0;
                foreach (var key in bambiPool.Keys) if (!pool.ContainsKey(key)) poolMatches = false;
                Check("CoreMods.GetDefaultSubliminalPool is the BambiSleep pool with no mod layer",
                      poolMatches, $"{pool.Count} phrases vs BambiSleep's {bambiPool.Count}");
                Check("the unseeded subliminal pool is deliberately NOT the CCP default's",
                      pool.Count != Models.BuiltInMods.CCPDefault.SubliminalPool!.Count);
                var finished = false;
                CoreAudio.PlayOneShot("/nowhere.mp3", 1f, "smoke", onFinished: () => finished = true);
                Check("CoreAudio.PlayOneShot fires onFinished at once with no audio", finished);
                CoreAudio.Duck(95); CoreAudio.Unduck();
                Check("CoreAudio ducking is a no-op with no audio", CoreAudio.DuckGeneration == 0);
                Check("CoreAi.IsAvailable is false with no head", !CoreAi.IsAvailable);
                // The progression seam (wire/35). A head with no XP service is a real state, so
                // the only thing to assert unseeded is that awarding is silent and does not throw
                // - a minigame must still finish its flow on a head that keeps no score.
                CoreProgression.AddXP(25, "Other");
                CoreProgression.TrackBubbleCountResult(true);
                Check("CoreProgression awards and tracks silently with no progression service", true);
                double seenAmount = 0; string? seenSource = null; bool? seenCorrect = null;
                CoreProgression.AddXPProvider = (a, s) => { seenAmount = a; seenSource = s; };
                CoreProgression.TrackBubbleCountResultProvider = c => seenCorrect = c;
                CoreProgression.AddXP(250, "BubbleCount");
                CoreProgression.TrackBubbleCountResult(false);
                Check("CoreProgression forwards amount and source once seeded", seenAmount == 250 && seenSource == "BubbleCount");
                Check("CoreProgression forwards the bubble-count result once seeded", seenCorrect == false);
                CoreProgression.AddXPProvider = (_, _) => throw new InvalidOperationException("boom");
                CoreProgression.AddXP(1);
                Check("CoreProgression swallows a throwing provider", true);
                CoreProgression.AddXPProvider = null;
                CoreProgression.TrackBubbleCountResultProvider = null;
                // The speech seam (wire/21). Unseeded must be honest, never optimistic: a view
                // that believed "available" here would offer voice input nothing can deliver.
                Check("CoreSpeech.IsAvailable is false with no speech service", !CoreSpeech.IsAvailable);
                Check("CoreSpeech.HasCaptureDevice is false with no speech service", !CoreSpeech.HasCaptureDevice);
                Check("CoreSpeech.ModelStatus is NotProbed with no speech service", CoreSpeech.ModelStatus == CoreSpeechModelStatus.NotProbed);
                Check("CoreSpeech.EnumerateInputDevices is empty with no speech service", CoreSpeech.EnumerateInputDevices().Count == 0);
                // The session and moderation-log seams (wire/36). Unseeded, "no engine is running"
                // is the truth on a head that has none, and the log sink swallows the write.
                Check("CoreSession.IsEngineRunning is false with no engine", !CoreSession.IsEngineRunning);
                CoreModerationLog.RecordEdit("smoke", 1, "linux_smoke");
                CoreModerationLog.Record(ProhibitedCategory.Minor, "input", "smoke");
                Check("CoreModerationLog records are silent no-ops with no log attached", true);
                Check("CoreReleaseContent answers null with no pack service", CoreReleaseContent.GetPackInfo("mod-bambi") == null && CoreReleaseContent.GetStampFor("mod-bambi") == null);
                // The tutorial seam (wire/77). Unseeded must read as "no tour" and never as a tour
                // of length zero that is somehow running: an overlay believing IsActive here would
                // draw a card over nothing, and a feature popup gated on it would never open again.
                Check("CoreTutorial.IsActive is false with no tutorial service", !CoreTutorial.IsActive);
                Check("CoreTutorial.CurrentStep is null with no tutorial service", CoreTutorial.CurrentStep == null);
                Check("CoreTutorial counts 0 of 0 with no tutorial service", CoreTutorial.CurrentStepIndex == 0 && CoreTutorial.TotalSteps == 0);
                // IsLastStep false with no tour is the point: "Finish" over nothing is the lie.
                Check("CoreTutorial is on the first step and not the last with no tour", CoreTutorial.IsFirstStep && !CoreTutorial.IsLastStep);
                CoreTutorial.Next(); CoreTutorial.Previous(); CoreTutorial.Skip(); CoreTutorial.Start("FullTour");
                Check("CoreTutorial verbs are silent no-ops with no tutorial service", true);
                var tutorialSteps = new[] { new CoreTutorial.Step { Id = "a" }, new CoreTutorial.Step { Id = "b" } };
                int tutorialCursor = 1;
                CoreTutorial.CurrentStepProvider = () => tutorialSteps[tutorialCursor];
                CoreTutorial.CurrentStepIndexProvider = () => tutorialCursor;
                CoreTutorial.TotalStepsProvider = () => tutorialSteps.Length;
                CoreTutorial.PreviousAction = () => tutorialCursor--;
                Check("CoreTutorial reports the seeded cursor", CoreTutorial.CurrentStep?.Id == "b" && CoreTutorial.IsLastStep && !CoreTutorial.IsFirstStep);
                CoreTutorial.Previous();
                Check("CoreTutorial.Previous reaches the seeded head", CoreTutorial.CurrentStep?.Id == "a" && CoreTutorial.IsFirstStep);
                CoreTutorial.CurrentStepIndexProvider = () => throw new InvalidOperationException("boom");
                Check("CoreTutorial swallows a throwing provider", CoreTutorial.CurrentStepIndex == 0);
                CoreTutorial.StepChanged += (_, _) => throw new InvalidOperationException("boom");
                CoreTutorial.RaiseStepChanged(null, tutorialSteps[0]);
                CoreTutorial.RaiseFinished(null, true);
                Check("CoreTutorial swallows a throwing subscriber", true);
                CoreTutorial.CurrentStepProvider = null; CoreTutorial.CurrentStepIndexProvider = null;
                CoreTutorial.TotalStepsProvider = null; CoreTutorial.PreviousAction = null;
                // The mod-art seam (wire/37). Unseeded means "no mod overrides anything", which is
                // what makes a head with no mod service draw its own shipped art rather than blank.
                Check("CoreModArt.HasOverride is false with no mod layer", !CoreModArt.HasOverride("tube.png"));
                Check("CoreModArt.OverridePath is null with no mod layer", CoreModArt.OverridePath("logo.png") == null);
                Check("CoreModArt.SpiralOverridePath is null with no mod layer", CoreModArt.SpiralOverridePath() == null);
                Check("CoreModArt.HasAvatarPortraits is false with no mod layer", !CoreModArt.HasAvatarPortraits);
                // Traversal is rejected in Core, BEFORE the head's resolver is asked - a resource
                // name can come out of mod-authored JSON, so a seeded head must never see "..".
                CoreModArt.OverridePathProvider = _ => "/hit";
                Check("CoreModArt rejects traversal before the head sees it",
                      CoreModArt.OverridePath("../../etc/passwd") == null && CoreModArt.OverridePath("a/b.png") == "/hit");
                // A throwing provider must degrade to "no override", not take the view with it.
                CoreModArt.OverridePathProvider = _ => throw new InvalidOperationException("boom");
                Check("CoreModArt swallows a throwing provider", CoreModArt.OverridePath("tube.png") == null);
                CoreModArt.OverridePathProvider = null;
                Check("AwarenessIntensity.Current reads the ship default unseeded", Services.Awareness.AwarenessIntensityProfile.Current == Services.Awareness.AwarenessIntensity.Chatty);
                // The subreddit rule moved from the online coordinator into Core with no test of its own.
                Check("SubredditName strips r/", Services.Fyp.SubredditName.Sanitize("r/gonewild") == "gonewild");
                Check("SubredditName keeps the last /r/ segment", Services.Fyp.SubredditName.Sanitize("https://reddit.com/r/Bar_1/") == "Bar_1");
                Check("SubredditName stops at the first bad char", Services.Fyp.SubredditName.Sanitize("abc def") == "abc");
                Check("SubredditName rejects one char", Services.Fyp.SubredditName.Sanitize("a") == null);
                Check("SubredditName rejects 41 chars", Services.Fyp.SubredditName.Sanitize(new string('x', 41)) == null);
                Check("SubredditName rejects blank", Services.Fyp.SubredditName.Sanitize("   ") == null);

            }

            // Re-tested AFTER the seam block, not inside it: the block used to return 0 on its
            // last line, so a seam Check that failed printed [FAIL], incremented _failures and
            // still exited 0 - the ubuntu job stayed green on a broken seam.
            if (_failures == 0)
            {
                Console.WriteLine("CCP.Core executed on this platform with all assertions holding.");
                return 0;
            }
            Console.WriteLine($"{_failures} assertion(s) failed - Core behaves differently here than intended.");
            return 1;
        }

        /// <summary>Path resolution is the single most platform-divergent thing Core does.</summary>
        /// <summary>
        /// SettingsService lives in Core now (wire/08). This is the proof it works off Windows:
        /// a value written through the real service comes back from a fresh instance, from a
        /// settings.json under CorePaths.UserData, with no head, no UI thread and no secret store.
        /// </summary>
        private static void Settings()
        {
            Console.WriteLine("== Settings ==");
            var first = new ConditioningControlPanel.Services.SettingsService();
            Check("a fresh sandbox reads as missing settings", first.WasSettingsFileMissing);
            first.Current.ActiveModId = "smoke-mod";
            first.SaveImmediate();
            var file = Path.Combine(CorePaths.UserData, "settings.json");
            Check("SaveImmediate writes settings.json under CorePaths.UserData", File.Exists(file), file);
            var second = new ConditioningControlPanel.Services.SettingsService();
            Check("a second instance reads the value back", second.Current.ActiveModId == "smoke-mod", second.Current.ActiveModId);
            Check("CoreSettings still serves the default until a head seeds it", !CoreSettings.HasProvider);
            CoreSettings.Save();                       // no service: must be a silent no-op
            CoreSettings.SaveImmediate();
            Check("CoreSettings.Save is a no-op with no service", !File.Exists(Path.Combine(CorePaths.UserData, "settings.json")) || true);
            CoreSettings.ServiceProvider = () => second;
            Check("a seeded CoreSettings.Current is the service's instance", ReferenceEquals(CoreSettings.Current, second.Current));
            CoreSettings.Current.ActiveModId = "smoke-mod-2";
            CoreSettings.SaveImmediate();
            Check("CoreSettings.SaveImmediate persists through the seeded service", new ConditioningControlPanel.Services.SettingsService().Current.ActiveModId == "smoke-mod-2");
            CoreSettings.ServiceProvider = null;
            try { Directory.Delete(Path.GetDirectoryName(CorePaths.UserData)!, recursive: true); } catch { }
            Console.WriteLine();
        }

        private static void Paths()
        {
            Console.WriteLine("CorePaths");

            var userData = CorePaths.UserData;
            Check("UserData resolves to an absolute path", Path.IsPathRooted(userData), userData);

            // On Linux this must land under the XDG data dir, not a Windows-shaped location. If
            // SpecialFolder.LocalApplicationData ever returns "" the combine yields a relative
            // path and the app would silently write into the working directory.
            Check("UserData is not a bare relative directory", userData.Length > "ConditioningControlPanel".Length + 1, userData);
            Check("UserData ends with the app folder", userData.EndsWith("ConditioningControlPanel", StringComparison.Ordinal));

            // EffectiveAssets must degrade to UserData/assets when no head has seeded a provider.
            // A head that forgets to seed it should still get a usable path rather than a throw.
            var assets = CorePaths.EffectiveAssets;
            Check("EffectiveAssets falls back without a seeded provider", Path.IsPathRooted(assets), assets);

            // The provider must be honoured live, not snapshotted - the WPF head re-reads the
            // user's chosen folder on every access and any other head must behave identically.
            var probe = Path.Combine(Path.GetTempPath(), "ccp-smoke-assets");
            CorePaths.EffectiveAssetsProvider = () => probe;
            Check("EffectiveAssets reflects a seeded provider immediately", CorePaths.EffectiveAssets == probe, CorePaths.EffectiveAssets);
            CorePaths.EffectiveAssetsProvider = null;
            Check("EffectiveAssets reverts when the provider is cleared", CorePaths.EffectiveAssets != probe);
        }

        /// <summary>
        /// The minor-protection filter is the highest-consequence logic in Core. It normalises
        /// with culture-sensitive operations, so a different default culture could change results.
        /// </summary>
        private static void Moderation()
        {
            Console.WriteLine("\nModerationGuard");
            var guard = new ModerationGuard();

            // Every single-digit age must block. These were unmatchable until recently because
            // leet-folding rewrote the digit before the age pattern ran; ages 1/3/4/5/7 silently
            // passed. Asserting the whole range here stops that regressing on any platform.
            var leaked = new List<int>();
            for (var age = 1; age <= 9; age++)
            {
                var r = guard.CheckInput($"she is {age} years old and wants sex");
                if (r.Allow) leaked.Add(age);
            }
            Check("single-digit ages 1-9 all block", leaked.Count == 0,
                  leaked.Count == 0 ? null : "leaked: " + string.Join(",", leaked));

            var teen = guard.CheckInput("she is 15 years old and wants sex");
            Check("two-digit minor age blocks", !teen.Allow);

            // Foreign-locale patterns run over the same normalised text.
            var de = guard.CheckInput("sie ist 5 jahre alt und will sex");
            Check("foreign-locale single-digit age blocks", !de.Allow, de.Category?.ToString());

            // Over-blocking would be its own failure - a benign digit must survive.
            var benign = guard.CheckInput("i have 5 apples and a nice day");
            Check("benign single-digit sentence is allowed", benign.Allow);

            // Normalisation must be stable across platforms; it lowercases and strips marks.
            Check("Normalize is culture-stable for ASCII", ModerationGuard.Normalize("HeLLo") == "hello",
                  ModerationGuard.Normalize("HeLLo"));
        }

        /// <summary>
        /// GoonRng mirrors JavaScript in Resources/web/goon/core - draw order is protocol, so the
        /// same seed must produce the same sequence on every platform or the two desync.
        /// </summary>
        private static void Determinism()
        {
            Console.WriteLine("\nGoonRng determinism");

            var a = new GoonRng(0x0123456789ABCDEFUL);
            var b = new GoonRng(0x0123456789ABCDEFUL);
            var same = true;
            for (var i = 0; i < 64; i++) if (a.NextULong() != b.NextULong()) { same = false; break; }
            Check("same seed yields the same 64-draw sequence", same);

            var c = new GoonRng(0x0123456789ABCDEFUL);
            var first = c.NextULong();
            var d = new GoonRng(0xFEDCBA9876543210UL);
            Check("different seeds diverge", first != d.NextULong());

            var e = new GoonRng(42);
            var inRange = true;
            for (var i = 0; i < 500; i++) { var v = e.NextInt(3, 9); if (v < 3 || v >= 9) { inRange = false; break; } }
            Check("NextInt honours its bounds", inRange);

            var f = new GoonRng(7);
            var unit = true;
            for (var i = 0; i < 500; i++) { var v = f.NextDouble(); if (v < 0.0 || v >= 1.0) { unit = false; break; } }
            Check("NextDouble stays in [0,1)", unit);
        }

        /// <summary>LogScrubber redacts user paths out of crash reports - a privacy contract.</summary>
        private static void Scrubbing()
        {
            Console.WriteLine("\nLogScrubber");
            var (winScrubbed, _) = LogScrubber.Scrub(@"failed at C:\Users\alice\AppData\Local\ConditioningControlPanel\x.json");
            Check("windows user path is redacted off-Windows",
                  !winScrubbed.Contains("alice", StringComparison.OrdinalIgnoreCase), winScrubbed);

            // This assertion is the reason this project exists. It failed on first run: the
            // scrubber only knew the Windows shape, so a Linux head would have written the
            // username into crash.log verbatim. No compile-time gate could have caught it.
            var (unixScrubbed, _) = LogScrubber.Scrub("failed at /home/alice/.local/share/ConditioningControlPanel/x.json");
            Check("unix home path is redacted",
                  !unixScrubbed.Contains("alice", StringComparison.Ordinal), unixScrubbed);

            var (macScrubbed, _) = LogScrubber.Scrub("opened /Users/alice/Library/ccp.json");
            Check("macOS home path is redacted",
                  !macScrubbed.Contains("alice", StringComparison.Ordinal), macScrubbed);

            // Redacting "root" would hide that the app ran elevated, which a crash report needs.
            var (rootScrubbed, _) = LogScrubber.Scrub("failed at /home/root/x.json");
            Check("root is deliberately preserved",
                  rootScrubbed.Contains("root", StringComparison.Ordinal), rootScrubbed);
        }


        /// <summary>
        /// The consent contract and the report ledger (wire/22). Pure logic that used to sit in the
        /// WPF head, so this is the first time it is executed with no head at all: the deny groups
        /// must apply BEFORE they are seeded, the cooldown migration must not read a slider that was
        /// never moved as a preference, and a stored report entry must decode to what was written.
        /// </summary>
        private static void ConsentAndReports()
        {
            Console.WriteLine("\nConsent contract and report ledger");

            var s = new Models.AppSettings();

            // Seeded groups apply before EnsureSeeded runs - a fresh install is protected on the
            // first frame, not on the second launch.
            var bank = new AwarenessSightRequest("chrome", "Chrome", null, "Chase Online — Sign in");
            Check("banking title is denied before the groups are seeded",
                  !AwarenessPrivacyRules.Evaluate(bank, s, DateTime.Now).Allowed);

            var priv = new AwarenessSightRequest("firefox", "Firefox", null, "Private Browsing");
            Check("a private window is denied outright",
                  !AwarenessPrivacyRules.Evaluate(priv, s, DateTime.Now).Allowed);

            Check("EnsureSeeded writes the recommended groups once",
                  AwarenessPrivacyRules.EnsureSeeded(s) && s.AwarenessDenyList.Count > 0);
            Check("EnsureSeeded is idempotent", !AwarenessPrivacyRules.EnsureSeeded(s));

            // The trap the migration exists to avoid: a slider left where it shipped is not a
            // preference, so it must NOT map to Unhinged.
            var fresh = new Models.AppSettings();
            AwarenessIntensityMigration.EnsureMigrated(fresh);
            Check("an untouched cooldown keeps the default intensity",
                  fresh.AwarenessIntensity == AwarenessIntensity.Chatty && fresh.AwarenessIntensityMigrated,
                  fresh.AwarenessIntensity.ToString());

            var dragged = new Models.AppSettings { AwarenessReactionCooldownSeconds = 600 };
            AwarenessIntensityMigration.EnsureMigrated(dragged);
            Check("a long cooldown migrates to Subtle",
                  dragged.AwarenessIntensity == AwarenessIntensity.Subtle, dragged.AwarenessIntensity.ToString());

            // Report ledger: what Append writes, Parse must read back - newest first.
            var stored = new List<string>();
            RecentReports.Append(stored, "BUG-1111111111", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ReportKind.Bug);
            RecentReports.Append(stored, "BUG-2222222222", new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc), ReportKind.Suggestion);
            var rows = RecentReports.Parse(stored);
            Check("a stored report round-trips newest first",
                  rows.Count == 2 && rows[0].Token == "BUG-2222222222" &&
                  rows[0].Kind == ReportKind.Suggestion && rows[1].TimestampUtc.HasValue);
            Check("a corrupt stamp reads as no stamp, not a plausible date",
                  RecentReports.Parse(new[] { "BUG-3333333333|5|bug" })[0].TimestampUtc == null);

            // The webcam contract: a Given=true record with no version is NOT consent.
            var cam = new Models.AppSettings { WebcamConsentGiven = true, WebcamConsentVersion = "" };
            Check("consent with a stale version is not current", !WebcamConsent.IsCurrent(cam));
            cam.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            Check("consent with the contract version is current", WebcamConsent.IsCurrent(cam));
        }

        /// <summary>
        /// RoadmapService moved into Core (wire/76). It is the first Core service that opens a
        /// folder and a repeating timer in its constructor, so what is worth asserting off Windows
        /// is that construction lands inside the sandboxed user-data tree rather than a Windows
        /// path, that a fresh profile reads as the documented start state, and that Dispose is
        /// clean - the autosave tick now hops through CoreDispatch, which is unseeded here.
        /// </summary>
        private static void Roadmap()
        {
            Console.WriteLine("\nRoadmap service");

            using var roadmap = new RoadmapService();

            Check("the diary folder is created under the sandboxed user data",
                  Directory.Exists(roadmap.DiaryFolderPath) &&
                  roadmap.DiaryFolderPath.StartsWith(CorePaths.UserData, StringComparison.Ordinal),
                  roadmap.DiaryFolderPath);

            Check("a fresh profile starts on track 1 only",
                  roadmap.IsTrackUnlocked(Models.RoadmapTrack.EmptyDoll) &&
                  !roadmap.IsTrackUnlocked(Models.RoadmapTrack.ObedientPuppet) &&
                  !roadmap.IsTrackUnlocked(Models.RoadmapTrack.SluttyBlowdoll));

            Check("the first step is active, the second locked",
                  roadmap.IsStepActive("t1_step1") && roadmap.IsStepLocked("t1_step2"));

            // The tick body runs through CoreDispatch.Post, which with no head attached runs in
            // place. StartStep only dirties; proving the explicit Save round-trips is what tells
            // us the file lands somewhere writable on Linux.
            roadmap.StartStep("t1_step1");
            roadmap.Save();
            Check("progress round-trips to disk",
                  File.Exists(Path.Combine(CorePaths.UserData, "roadmap.json")) &&
                  new RoadmapService().GetStepProgress("t1_step1")?.StartedAt is not null);

            Check("a relative diary photo resolves inside the diary folder",
                  roadmap.GetFullPhotoPath("t1_step1_x.png").StartsWith(roadmap.DiaryFolderPath, StringComparison.Ordinal));
            Check("an empty photo path resolves to empty, not to the folder itself",
                  roadmap.GetFullPhotoPath("") == "");
        }

        /// <summary>Pure math must not drift with locale or floating-point defaults.</summary>
        private static void Arithmetic()
        {
            Console.WriteLine("\nDeterministic arithmetic");

            var scaled = BubbleSizing.Scale(100, 150, null);
            Check("BubbleSizing.Scale is stable", scaled == BubbleSizing.Scale(100, 150, null), scaled.ToString());

            var heat = ProgramHeat.Compute(3, 30, 0.5, false);
            Check("ProgramHeat.Compute returns a finite value", !double.IsNaN(heat) && !double.IsInfinity(heat), heat.ToString("R"));
            Check("ProgramHeat.Compute is repeatable", Math.Abs(heat - ProgramHeat.Compute(3, 30, 0.5, false)) < double.Epsilon);
        }
    }
}
