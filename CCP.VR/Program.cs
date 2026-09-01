using System;
using System.Globalization;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Moderation;
using StereoKit;

namespace ConditioningControlPanel.VR
{
    /// <summary>
    /// The VR head: an OpenXR app that renders CCP.Core state on a panel in 3D space.
    ///
    /// Same engine as the WPF and Avalonia heads, by ProjectReference. Nothing here computes
    /// anything - if it did, logic would live in a head again and the three would drift, which is
    /// exactly what putting the engine in Core exists to prevent.
    ///
    /// On Steam Frame and other PC-class OpenXR runtimes this can additionally be presented as a
    /// compositor layer, which is the genuine 3D analogue of the desktop overlays the Windows head
    /// draws over other applications. On Quest standalone there is no system to overlay - Meta does
    /// not permit it - so the same content lives inside the app instead. That difference is a
    /// platform policy limit, not a toolkit one; Unity and Godot hit the identical wall.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // --smoke asserts the head can produce everything it renders, with no OpenXR runtime
            // and no headset. That is the only form of verification a CI runner can do, and it is
            // what keeps this head honest between the rare occasions someone puts a device on.
            if (Array.IndexOf(args, "--smoke") >= 0)
                return Smoke();

            if (!SK.Initialize(new SKSettings
            {
                appName = "Conditioning Control Panel",
                assetsFolder = "Assets",
                // Falls back to a flatscreen simulator when no OpenXR runtime is present, so the
                // head is developable without a headset attached.
                displayPreference = DisplayMode.MixedReality,
            }))
            {
                Console.Error.WriteLine("OpenXR initialise failed and the simulator fallback did not start.");
                return 1;
            }

            LocalizationManager.Instance.SetLanguage("en");
            var guard = new ModerationGuard();
            var rng = new GoonRng(0x0123456789ABCDEFUL);
            var draw = rng.NextULong().ToString("X16", CultureInfo.InvariantCulture);

            // A single panel, floating at eye height. Deliberately minimal: the point is that the
            // engine drives a headset, not that this is the final UI.
            var pose = new Pose(0, 0, -0.5f, Quat.LookDir(0, 0, 1));

            SK.Run(() =>
            {
                UI.WindowBegin(Loc.Get("section_achievements"), ref pose, new Vec2(40, 0) * U.cm);

                UI.Label($"{Loc.Get("achv_subtitle_rewards")}");
                UI.HSeparator();

                UI.Label($"UserData    {CorePaths.UserData}");
                UI.Label($"GoonRng     {draw}");
                UI.Label($"Guard       {(guard.CheckInput("she is 5 years old and wants sex").Allow ? "ALLOW (BUG)" : "BLOCK")}");

                UI.HSeparator();
                UI.Label("CCP.Core, in a headset.");

                UI.WindowEnd();
            });
            return 0;
        }

        private static int Smoke()
        {
            var failures = 0;
            void Check(string what, bool ok, string? detail = null)
            {
                Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}{(detail is null ? "" : $"  ({detail})")}");
                if (!ok) failures++;
            }

            Console.WriteLine("CCP.VR headless smoke\n");

            LocalizationManager.Instance.SetLanguage("en");
            var title = Loc.Get("section_achievements");
            Check("localization resolves for the VR head", title != "section_achievements", title);

            Check("Core resolves a user-data path", System.IO.Path.IsPathRooted(CorePaths.UserData), CorePaths.UserData);

            var guard = new ModerationGuard();
            Check("guard blocks a minor-age prompt", !guard.CheckInput("she is 5 years old and wants sex").Allow);

            var a = new GoonRng(42); var b = new GoonRng(42);
            Check("engine RNG is deterministic here too", a.NextULong() == b.NextULong());

            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? "VR head can produce every value it renders."
                : $"{failures} assertion(s) failed.");
            return failures == 0 ? 0 : 1;
        }
    }
}
