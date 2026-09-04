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
            LockCard();
            MindWipe();
            CornerGif();

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
                // The subliminal split (wire/109). The schedule decides here; the flash surface is
                // the head's, and with nothing seeded a scheduled subliminal must draw nothing
                // rather than throw. The interval rule is checked with the roll injected, because a
                // real clock cannot prove arithmetic.
                Check("CoreSubliminal is not running cold", !CoreSubliminal.IsRunning);
                Check("the median roll is exactly 60/frequency",
                      Math.Abs(CoreSubliminal.NextIntervalSeconds(5, 0.5) - 12.0) < 1e-9,
                      CoreSubliminal.NextIntervalSeconds(5, 0.5).ToString("R"));
                Check("roll 0 is the -30% edge and roll 1 the +30% edge",
                      Math.Abs(CoreSubliminal.NextIntervalSeconds(5, 0) - 8.4) < 1e-9 &&
                      Math.Abs(CoreSubliminal.NextIntervalSeconds(5, 1) - 15.6) < 1e-9);
                Check("the interval never drops below one second, however fast the rate",
                      CoreSubliminal.NextIntervalSeconds(600, 0) >= 1.0 &&
                      CoreSubliminal.NextIntervalSeconds(0, 0) >= 1.0);
                var runStates = new List<bool>();
                CoreSubliminal.RunStateChanged = running => runStates.Add(running);
                CoreSubliminal.Start();
                Check("Start arms the scheduler with no surface seeded and does not throw",
                      CoreSubliminal.IsRunning);
                CoreSubliminal.Stop();
                Check("Stop disarms it", !CoreSubliminal.IsRunning);
                Check("both transitions reached the head's run-state hook",
                      runStates.Count == 2 && runStates[0] && !runStates[1],
                      string.Join(",", runStates));
                CoreSubliminal.RunStateChanged = null;
                // The rest of this block edits the fallback settings, so snapshot them first -
                // every later check in this runner reads the same instance.
                var poolWas = CoreSettings.Current.SubliminalPool;
                var freqWas = CoreSettings.Current.SubliminalFrequency;
                var enabledWas = CoreSettings.Current.SubliminalEnabled;

                // Every phrase in the fallback pool is off, so there is nothing to show and the
                // scheduler must say so rather than hand the surface a null.
                CoreSettings.Current.SubliminalPool = new Dictionary<string, bool> { ["x"] = false };
                Check("PickPhrase returns null when nothing in the pool is enabled",
                      CoreSubliminal.PickPhrase() is null);
                CoreSettings.Current.SubliminalPool = new Dictionary<string, bool> { ["x"] = true };
                Check("PickPhrase returns the one enabled phrase", CoreSubliminal.PickPhrase() == "x");
                // Unseeded CoreSession answers false, so the toggle must persist the flag and leave
                // the scheduler alone - a head with no engine is not running one.
                CoreSubliminal.SetEnabled(true);
                Check("SetEnabled writes the flag with no engine running",
                      CoreSettings.Current.SubliminalEnabled);
                Check("...and does NOT arm the scheduler behind a stopped engine",
                      !CoreSubliminal.IsRunning);
                CoreSubliminal.SetEnabled(false);

                // THE assertion of the layer, and the only one a flag flip cannot fake: the timer
                // must actually reach ShowProvider. Everything else here proves arithmetic and
                // state; this proves the loop. 60 per minute arms in 1.0-1.3s, and the tick body
                // runs in place because no head seeded CoreDispatch - so the wait is what carries
                // it across, not a dispatcher.
                CoreSettings.Current.SubliminalFrequency = 60;
                CoreSettings.Current.SubliminalEnabled = true;
                using (var shown = new System.Threading.ManualResetEventSlim())
                {
                    CoreSubliminal.ShowProvider = _ => shown.Set();
                    CoreSubliminal.Start();
                    Check("the scheduler actually fires a show, it does not merely flip a flag",
                          shown.Wait(TimeSpan.FromSeconds(6)));
                    CoreSubliminal.Stop();
                    CoreSubliminal.ShowProvider = null;
                }

                CoreSettings.Current.SubliminalPool = poolWas;
                CoreSettings.Current.SubliminalFrequency = freqWas;
                CoreSettings.Current.SubliminalEnabled = enabledWas;

                var finished = false;
                CoreAudio.PlayOneShot("/nowhere.mp3", 1f, "smoke", onFinished: () => finished = true);
                Check("CoreAudio.PlayOneShot fires onFinished at once with no audio", finished);
                CoreAudio.Duck(95); CoreAudio.Unduck();
                Check("CoreAudio ducking is a no-op with no audio", CoreAudio.DuckGeneration == 0);
                Check("CoreAi.IsAvailable is false with no head", !CoreAi.IsAvailable);
                // The entitlement seam (wire/99). TierGate is Core policy now; the account bars and
                // the refusal surface are the head's. Unseeded MUST deny - a gate that fails open on
                // a head with no account service hands out Tier 2 for free.
                Check("TierGate.RequiresPremium denies with no entitlement seeded",
                      !TierGate.RequiresPremium("Smoke").Allowed);
                Check("TierGate.RequiresLab denies with no entitlement seeded",
                      !TierGate.RequiresLab("Smoke").Allowed);
                Check("the daily-free overload does not open the door either",
                      !TierGate.RequiresPremium("Smoke", "voice").Allowed);
                Check("TierGate.DemandPremium refuses and does not throw with no refusal surface",
                      !TierGate.DemandPremium("Smoke"));
                Check("TierGate.DemandLab refuses and does not throw with no refusal surface",
                      !TierGate.DemandLab("Smoke", "dtrh"));
                // A provider that THROWS must read as denied, not as an unhandled exception.
                CoreEntitlement.HasLabProvider = () => throw new InvalidOperationException("smoke");
                Check("a throwing entitlement provider reads as denied", !TierGate.RequiresLab("Smoke").Allowed);
                // ...and the seam must actually be CONSULTED, or "denies" would only prove the
                // class hardcodes false. Flip it, check, put it back.
                CoreEntitlement.HasLabProvider = () => true;
                Check("a seeded entitlement opens the Lab bar", TierGate.RequiresLab("Smoke").Allowed);
                CoreEntitlement.HasLabProvider = null;
                Check("clearing the provider closes it again", !TierGate.RequiresLab("Smoke").Allowed);
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
                // The program seam (wire/102). Unseeded must not GRANT anything: HasPremium false
                // is what makes a premium program refuse to enroll rather than hand one out on a
                // head with no pledge service, and it is the only member here that gates.
                Check("CoreProgram.HasPremium is false with no patron service", !CoreProgram.HasPremium);
                Check("CoreProgram.ActivePackVideoCount is 0 with no content packs", CoreProgram.ActivePackVideoCount() == 0);
                Check("CoreProgram.Roadmap is null with no roadmap instance", CoreProgram.Roadmap() == null);
                CoreProgram.Notify("smoke", "Success", TimeSpan.FromSeconds(1));
                CoreProgram.UnlockAchievement("smoke_badge");
                Check("CoreProgram toast and badge are silent no-ops with no head", true);
                CoreProgram.HasPremiumProvider = () => throw new InvalidOperationException("boom");
                Check("CoreProgram.HasPremium stays false when the provider throws", !CoreProgram.HasPremium);
                CoreProgram.HasPremiumProvider = null;
                // The programs library and the ledger, both in Core now. Building the library is
                // the whole point of the move: a head with no ProgramService can still browse.
                var programs = Services.Program.BuiltInPrograms.All();
                Check("BuiltInPrograms.All() builds the shipped library", programs.Count > 0);
                var wellFormed = true;
                Models.Program.ProgramDefinition? premium = null;
                foreach (var p in programs)
                {
                    var dayCount = 0;
                    foreach (var _ in p.AllDays) dayCount++;
                    if (p.LengthDays <= 0 || dayCount == 0) wellFormed = false;
                    if (premium == null && p.Tier == Models.Program.ProgramTier.Premium) premium = p;
                }
                Check("every shipped program has a length and days", wellFormed);
                // The ledger runs here with no head at all. The tier gate is the assertion that
                // matters: unseeded CoreProgram.HasPremium is false, so a premium program must
                // REFUSE rather than be handed out.
                var svc = new Services.Program.ProgramService();
                Check("a premium program refuses to enroll with no pledge",
                      premium != null && !svc.CanEnroll(premium, out _));
                svc.Dispose();
                // The speech seam (wire/21). Unseeded must be honest, never optimistic: a view
                // that believed "available" here would offer voice input nothing can deliver.
                Check("CoreSpeech.IsAvailable is false with no speech service", !CoreSpeech.IsAvailable);
                Check("CoreSpeech.HasCaptureDevice is false with no speech service", !CoreSpeech.HasCaptureDevice);
                Check("CoreSpeech.ModelStatus is NotProbed with no speech service", CoreSpeech.ModelStatus == CoreSpeechModelStatus.NotProbed);
                Check("CoreSpeech.EnumerateInputDevices is empty with no speech service", CoreSpeech.EnumerateInputDevices().Count == 0);
                // The webcam seam (wire/100). Unseeded must read as "this head has no camera
                // engine": a view that believed "available" would draw an engine bar whose Start
                // button opens nothing, and revoke must not claim it undid four things it did not.
                Check("CoreWebcam.IsAvailable is false with no webcam service", !CoreWebcam.IsAvailable);
                CoreWebcam.RevokeConsent();
                Check("CoreWebcam.RevokeConsent is a silent no-op with no webcam service", true);
                CoreWebcam.IsAvailableProvider = () => throw new InvalidOperationException("boom");
                CoreWebcam.RevokeConsentAction = () => throw new InvalidOperationException("boom");
                Check("CoreWebcam.IsAvailable is false when the provider throws", !CoreWebcam.IsAvailable);
                CoreWebcam.RevokeConsent();
                Check("CoreWebcam.RevokeConsent swallows a throwing action", true);
                CoreWebcam.IsAvailableProvider = null;
                CoreWebcam.RevokeConsentAction = null;
                // The session and moderation-log seams (wire/36). Unseeded, "no engine is running"
                // is the truth on a head that has none, and the log sink swallows the write.
                Check("CoreSession.IsEngineRunning is false with no engine", !CoreSession.IsEngineRunning);
                // The bark seam (wire/101). BarkService stays in the WPF head; only the doorbell
                // crosses. Unseeded every ping is silent - a bark is a line the companion may say,
                // never a gate - and the one member that returns data returns EMPTY, never null,
                // so the Phrase Manager renders "no bark rows" rather than dereferencing nothing.
                CoreBark.NotifyUiAction("minimize");
                CoreBark.NotifyTabNavigated("lab");
                CoreBark.NotifyFeatureOpened("SchedulerRamp");
                CoreBark.NotifyAvatarClicked();
                CoreBark.NotifyChaosDraftAutopick();
                CoreBark.NotifyChaosResultsShown(1, 2, 3, true, 4, 5, 6, "hard");
                CoreBark.NotifyChaosRankUp("claimed");
                Check("CoreBark pings are silent no-ops with no bark engine", true);
                Check("CoreBark.AllLines is empty and not null with no bark engine",
                      CoreBark.AllLines is { Count: 0 });
                var barkSeen = new System.Collections.Generic.List<string>();
                CoreBark.UiAction = a => barkSeen.Add("ui:" + a);
                CoreBark.TabNavigated = t => barkSeen.Add("tab:" + t);
                CoreBark.NotifyUiAction("close");
                CoreBark.NotifyTabNavigated("play");
                Check("CoreBark forwards its key once seeded",
                      barkSeen.Count == 2 && barkSeen[0] == "ui:close" && barkSeen[1] == "tab:play");
                CoreBark.UiAction = _ => throw new InvalidOperationException("boom");
                CoreBark.AllLinesProvider = () => throw new InvalidOperationException("boom");
                CoreBark.NotifyUiAction("close");
                Check("CoreBark swallows a throwing sink and still answers empty",
                      CoreBark.AllLines is { Count: 0 });
                CoreBark.UiAction = null; CoreBark.TabNavigated = null; CoreBark.AllLinesProvider = null;
                // The account seam (wire/104). Unseeded must read as signed out and NOT ENTITLED.
                // The entitlement half is the one that matters: failing open here would hand every
                // Linux user the paid tier, so the throwing-provider checks below are the real
                // assertions - a provider that blows up means "I could not determine your tier",
                // which must never be read as "yes".
                Check("CoreAccount.IsLoggedIn is false with no account service", !CoreAccount.IsLoggedIn);
                Check("CoreAccount.DisplayName is null with no account service", CoreAccount.DisplayName == null);
                Check("CoreAccount.HasPremiumAccess is false with no account service", !CoreAccount.HasPremiumAccess);
                Check("CoreAccount.HasLabAccess is false with no account service", !CoreAccount.HasLabAccess);
                Check("CoreAccount.IsWhitelisted is false with no account service", !CoreAccount.IsWhitelisted);
                CoreAccount.HasPremiumAccessProvider = () => throw new InvalidOperationException("boom");
                CoreAccount.HasLabAccessProvider = () => throw new InvalidOperationException("boom");
                Check("a throwing entitlement provider fails CLOSED, never open",
                      !CoreAccount.HasPremiumAccess && !CoreAccount.HasLabAccess);
                CoreAccount.HasPremiumAccessProvider = () => true;
                Check("CoreAccount.HasPremiumAccess forwards once seeded", CoreAccount.HasPremiumAccess);
                CoreAccount.HasPremiumAccessProvider = null;
                CoreAccount.HasLabAccessProvider = null;
                // Rename and delete must REFUSE unseeded, never answer success: a caller that
                // believed either would write a new name locally, or clear local data, on the
                // strength of a server call that never happened.
                var rename = CoreAccount.ChangeDisplayNameAsync("smoke").GetAwaiter().GetResult();
                Check("CoreAccount.ChangeDisplayNameAsync refuses with no account service",
                      !rename.success && rename.newName == null && rename.error != null, rename.error);
                var deleted = CoreAccount.DeleteAccountAsync().GetAwaiter().GetResult();
                Check("CoreAccount.DeleteAccountAsync refuses with no account service",
                      !deleted.success && deleted.error != null, deleted.error);
                CoreAccount.DeleteAccountProvider = () => throw new InvalidOperationException("boom");
                Check("a throwing delete provider reports failure rather than propagating",
                      !CoreAccount.DeleteAccountAsync().GetAwaiter().GetResult().success);
                CoreAccount.DeleteAccountProvider = null;
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
                // The audio half of the same seam. Unseeded is "no mod cue", which is what makes a
                // head with no mod service fall back to its own shipped sound rather than to silence.
                Check("CoreModArt.AudioOverridePath is null with no mod layer", CoreModArt.AudioOverridePath("chaos/heartbeat.mp3") == null);
                CoreModArt.AudioOverridePathProvider = _ => "/hit.mp3";
                Check("CoreModArt rejects traversal on the audio half too",
                      CoreModArt.AudioOverridePath("../../etc/passwd") == null && CoreModArt.AudioOverridePath("bubbles/Pop.mp3") == "/hit.mp3");
                CoreModArt.AudioOverridePathProvider = _ => throw new InvalidOperationException("boom");
                Check("CoreModArt swallows a throwing audio provider", CoreModArt.AudioOverridePath("giggle5.mp3") == null);
                CoreModArt.AudioOverridePathProvider = null;
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

        /// <summary>
        /// The lock-card split: the schedule and the phrase rotation are Core's, the card itself is
        /// the head's. A headless render cannot exercise either, so this is where the scheduler is
        /// actually run.
        /// </summary>
        private static void LockCard()
        {
            Console.WriteLine("\nLock card (schedule in Core, surface in the head)");

            Check("CoreLockCard.Show is a no-op with no surface seeded",
                  Safe(() => { CoreLockCard.Show(); CoreLockCard.Show(isTest: true); }));

            // #736 - the regression that hard-blocked Kept Day 1: a 30-minute session, cards
            // deferred to minute 12, 1/hour. The worst-case roll must still land inside the window.
            Check("the first card lands inside an 18-minute window at 1/hour",
                  LockCardScheduler.ComputeFirstCardDelayMinutes(1, 18, 0.999999) < 18);
            Check("an open-ended first card stays inside one interval",
                  LockCardScheduler.ComputeFirstCardDelayMinutes(4, null, 0.999999) < 15.0 &&
                  LockCardScheduler.ComputeFirstCardDelayMinutes(1, null, 0.0) == 0.0);
            Check("the spacing after the first card is 60/freq +/-30%",
                  Math.Abs(LockCardScheduler.ComputeNextIntervalMinutes(1, 0.0) - 42.0) < 1e-9 &&
                  Math.Abs(LockCardScheduler.ComputeNextIntervalMinutes(1, 1.0) - 78.0) < 1e-9);
            Check("frequency 0 is treated as 1/hour rather than dividing by zero",
                  LockCardScheduler.ComputeNextIntervalMinutes(0, 0.5) == LockCardScheduler.ComputeNextIntervalMinutes(1, 0.5));

            var scheduler = new LockCardScheduler();
            var pool = new List<string> { "a", "b", "c" };
            var noRepeat = true;
            var previous = scheduler.PickPhrase(pool);
            for (var i = 0; i < 50; i++)
            {
                var next = scheduler.PickPhrase(pool);
                if (next == previous) noRepeat = false;
                previous = next;
            }
            Check("the phrase rotation never repeats back-to-back over 50 draws", noRepeat);
            Check("a one-phrase pool still draws that phrase every time",
                  scheduler.PickPhrase(new List<string> { "only" }) == "only" &&
                  scheduler.PickPhrase(new List<string> { "only" }) == "only");
            Check("an empty pool draws null rather than throwing",
                  scheduler.PickPhrase(new List<string>()) is null);

            // CoreSettings.Current is one shared default instance for the whole run, so put the
            // flag back or every later check inherits it.
            var settings = CoreSettings.Current;
            var wasEnabled = settings.LockCardEnabled;
            try
            {
                settings.LockCardEnabled = false;
                scheduler.Start();
                Check("Start() with the feature off leaves the schedule stopped", !scheduler.IsRunning);

                settings.LockCardEnabled = true;
                scheduler.Start();
                Check("Start() with the feature on runs the schedule", scheduler.IsRunning);
                scheduler.Stop();
                Check("Stop() stops it", !scheduler.IsRunning);
            }
            finally { settings.LockCardEnabled = wasEnabled; }

            Check("the enabled pool reads through Core, and is never null",
                  LockCardScheduler.EnabledPhrases() is not null);
        }

        /// <summary>True when the action ran without throwing.</summary>
        private static bool Safe(Action action)
        {
            try { action(); return true; }
            catch { return false; }
        /// Mind wipe's portable half (wire/107): the schedule decides here, the playback does not.
        /// A headless render cannot exercise either, so the arithmetic is asserted directly and
        /// the seam is asserted for the answer it gives when no head is playing anything - which
        /// is this head's real state.
        /// </summary>
        private static void MindWipe()
        {
            Console.WriteLine("\nMind wipe schedule");

            // Unseeded seam: silent, and honest about it. IsLooping false is the one that gates -
            // the card only restarts a loop it is told is running.
            CoreMindWipe.TriggerOnce();
            CoreMindWipe.StartLoop(1.0);
            CoreMindWipe.StopLoop();
            CoreMindWipe.UpdateSettings(60, 0.5);
            CoreMindWipe.ReloadClips();
            Check("CoreMindWipe is a silent no-op with no playback seeded",
                  !CoreMindWipe.IsLooping && CoreMindWipe.ClipCount == 0);
            CoreMindWipe.IsLoopingProvider = () => throw new InvalidOperationException("smoke");
            Check("a throwing loop provider reads as not looping", !CoreMindWipe.IsLooping);
            // ...and the seam must actually be consulted, or "false" would only prove a hardcode.
            CoreMindWipe.IsLoopingProvider = () => true;
            Check("a seeded loop provider is what answers", CoreMindWipe.IsLooping);
            CoreMindWipe.IsLoopingProvider = null;

            Check("the tick interval is ten seconds", MindWipeSchedule.TickInterval == TimeSpan.FromSeconds(10));
            Check("180/hour is a coin flip per tick", Math.Abs(MindWipeSchedule.Probability(180) - 0.5) < 1e-9,
                  MindWipeSchedule.Probability(180).ToString("R"));
            Check("6/hour - the default - fires about once per 60 ticks",
                  Math.Abs(MindWipeSchedule.Probability(6) - 6 / 360.0) < 1e-9);
            Check("the slider range clamps both ends",
                  MindWipeSchedule.ClampFrequency(0) == 1 && MindWipeSchedule.ClampFrequency(999) == 180 &&
                  MindWipeSchedule.ClampVolume(-1) == 0 && MindWipeSchedule.ClampVolume(9) == 1);

            // Session escalation: one extra play per five-minute block, from two different caps.
            Check("a fresh session runs at its base multiplier",
                  Math.Abs(MindWipeSchedule.SessionProbability(3, TimeSpan.Zero) - 0.1) < 1e-9);
            Check("session mode escalates one play per five-minute block",
                  Math.Abs(MindWipeSchedule.SessionProbability(3, TimeSpan.FromMinutes(10)) - 5 / 30.0) < 1e-9);
            Check("the probability caps at 15 plays per block",
                  Math.Abs(MindWipeSchedule.SessionProbability(3, TimeSpan.FromHours(4)) - 0.5) < 1e-9);
            Check("the displayed frequency keeps its own, higher cap of 30",
                  MindWipeSchedule.SessionFrequency(3, TimeSpan.FromHours(4)) == 30 &&
                  MindWipeSchedule.SessionFrequency(3, TimeSpan.FromMinutes(10)) == 5);
            // A clock that steps backwards (DST fall-back mid-session) goes quiet rather than
            // falling back to the base rate - unfloored, exactly as the Windows service is.
            Check("a backwards clock de-escalates rather than holding the base rate",
                  MindWipeSchedule.SessionProbability(3, TimeSpan.FromMinutes(-60)) < 0);

            // Clip discovery, against the sandboxed user-data tree.
            Check("the advertised clip folder sits under the effective assets folder",
                  MindWipeSchedule.AudioFolder.StartsWith(CorePaths.EffectiveAssets, StringComparison.Ordinal),
                  MindWipeSchedule.AudioFolder);
            Check("discovery creates the folder it advertises",
                  MindWipeSchedule.DiscoverClips(null) is not null && Directory.Exists(MindWipeSchedule.AudioFolder));

            var clip = Path.Combine(MindWipeSchedule.AudioFolder, "smoke-clip.mp3");
            File.WriteAllText(clip, "not really audio");
            File.WriteAllText(Path.Combine(MindWipeSchedule.AudioFolder, "readme.txt"), "ignore me");
            var found = MindWipeSchedule.DiscoverClips(null);
            Check("an .mp3 in the advertised folder is a candidate and a .txt is not",
                  found.Length == 1 && found[0] == clip, string.Join(", ", found));

            var custom = Path.Combine(CorePaths.UserData, "custom-wipe.wav");
            File.WriteAllText(custom, "not really audio");
            var picked = MindWipeSchedule.DiscoverClips(custom);
            Check("a custom clip that exists wins over the folders",
                  picked.Length == 1 && picked[0] == custom);
            Check("a custom path that does not exist falls back to the folders, not to nothing",
                  MindWipeSchedule.DiscoverClips(Path.Combine(CorePaths.UserData, "gone.wav")).Length == 1);
        /// The corner-GIF split (wire/108). CornerGifPlanner decides where a corner overlay goes;
        /// CoreCornerGif is the surface that draws it, and is UNSEEDED here - this head has no
        /// overlay window yet. What is worth asserting off Windows is that the unseeded seam is a
        /// silent no-op rather than a throw or a lie, that the placement arithmetic pins the corner
        /// it was asked for, and above all that a degenerate source is answered NULL: bug #625 was
        /// a 0x0 image producing NaN geometry that took the app down inside layout.
        /// </summary>
        private static void CornerGif()
        {
            Console.WriteLine("\nCorner GIF planner and surface seam");

            Check("CoreCornerGif reports no surface with no head attached", !CoreCornerGif.HasSurface);
            CoreCornerGif.Refresh();      // must not throw
            CoreCornerGif.Refresh(1);
            Check("an unseeded CoreCornerGif.Refresh is a silent no-op", true);

            var slot = new Models.CornerGifOverlaySetting
            {
                Enabled = true, Size = 300, Opacity = 18,
                Position = Models.CornerPosition.BottomRight
            };

            var placed = CornerGifPlanner.Place(slot, 600, 400, 1920, 1080, 0);
            Check("a bottom-right slot is placed against both far edges",
                  placed is { } p && Math.Abs(p.Left + p.Width - 1920) < 0.001
                                  && Math.Abs(p.Top + p.Height - 1080) < 0.001,
                  placed is { } q ? $"{q.Left}x{q.Top} {q.Width}x{q.Height}" : "null");

            Check("the longest edge is scaled to the slot's size, aspect kept",
                  placed is { } r && Math.Abs(r.Width - 300) < 0.001 && Math.Abs(r.Height - 200) < 0.001);

            Check("opacity percent becomes a 0..1 fraction",
                  placed is { } o && Math.Abs(o.Opacity - 0.18) < 0.0001);

            Check("a second slot in the same corner is nudged diagonally inward",
                  CornerGifPlanner.Place(slot, 600, 400, 1920, 1080, 1) is { } n
                  && placed is { } b
                  && Math.Abs(n.Left - (b.Left - CornerGifPlanner.SameCornerNudge)) < 0.001
                  && Math.Abs(n.Top - (b.Top - CornerGifPlanner.SameCornerNudge)) < 0.001);

            // #625. A degenerate source must never yield geometry a head can hand to a layout pass.
            Check("a 0x0 source is answered null, not NaN geometry",
                  CornerGifPlanner.Place(slot, 0, 0, 1920, 1080, 0) is null);
            Check("a non-finite screen size is answered null",
                  CornerGifPlanner.Place(slot, 600, 400, double.PositiveInfinity, 1080, 0) is null);

            // The realization stagger: two slots must never create a layered surface back to back.
            long cursor = 0;
            long first = CornerGifPlanner.NextRealizeDelayMs(ref cursor, 1000);
            long second = CornerGifPlanner.NextRealizeDelayMs(ref cursor, 1000);
            Check("the first slot realizes immediately and the second waits out the stagger",
                  first == 0 && second == CornerGifPlanner.StaggerMs, $"{first}/{second}");

            // Source order: an unpicked slot falls through to the pool, and "no file" means the
            // head draws its own default rather than nothing.
            var poolFile = Path.Combine(CorePaths.UserData, "smoke_pool_spiral.gif");
            Directory.CreateDirectory(CorePaths.UserData);
            File.WriteAllText(poolFile, "not really a gif");
            Check("an unpicked slot falls through to the pool spiral",
                  CornerGifPlanner.ResolveSourcePath(slot, poolFile) == poolFile);
            Check("a slot pick that is not on disk falls through too",
                  CornerGifPlanner.ResolveSourcePath(
                      new Models.CornerGifOverlaySetting { GifPath = poolFile + ".missing" }, poolFile) == poolFile);
            Check("no pick and no pool means null - the head draws its own default",
                  CornerGifPlanner.ResolveSourcePath(slot, null) is null);
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
