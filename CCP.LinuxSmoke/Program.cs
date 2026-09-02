using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Moderation;

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

            Paths();
            Moderation();
            Determinism();
            Scrubbing();
            Arithmetic();

            Console.WriteLine();
            if (_failures == 0)
            {
                Console.WriteLine("CCP.Core executed on this platform with all assertions holding.");
                return 0;
            }
            Console.WriteLine($"{_failures} assertion(s) failed - Core behaves differently here than intended.");
            return 1;
        }

        /// <summary>Path resolution is the single most platform-divergent thing Core does.</summary>
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
