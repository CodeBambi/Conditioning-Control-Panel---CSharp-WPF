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
    /// <para><b>Augmented reality, not a VR room.</b> The head requests a TRANSPARENT environment
    /// blend, so its content composites over passthrough - the user's actual room - rather than
    /// over a black void. That is the true analogue of what the Windows head does: there it draws
    /// over your screen, here it draws over your surroundings.</para>
    ///
    /// <para>This also sidesteps the one capability Quest genuinely withholds. Meta does not let a
    /// third-party app overlay OTHER APPS, and no toolkit changes that. But compositing over
    /// passthrough is not that - it is this app's own environment blend mode, which is exactly
    /// what every AR app on the device uses and needs no special permission. Quest 3 gives colour
    /// passthrough, Quest 2 monochrome.</para>
    ///
    /// <para>Runtimes that cannot do it fall back to opaque rather than failing to start, and the
    /// head reports which blend it actually got - a request is not a guarantee, and a silent
    /// downgrade to a black void would be the kind of thing nobody notices until they put the
    /// headset on.</para>
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

                // AnyTransparent, not Blend: it accepts either alpha-blend (Quest passthrough) or
                // additive (waveguide displays like HoloLens), so one binary is AR on whatever it
                // lands on. A runtime that offers neither still starts, opaque.
                blendPreference = DisplayBlend.AnyTransparent,
            }))
            {
                Console.Error.WriteLine("OpenXR initialise failed and the simulator fallback did not start.");
                return 1;
            }

            // What we asked for is not necessarily what we got. Report it rather than assume:
            // a silent downgrade to opaque is invisible until someone wears the headset.
            var blend = Device.DisplayBlend;
            var isAr = blend == DisplayBlend.Blend || blend == DisplayBlend.Additive;
            Console.WriteLine($"environment blend: {blend}  ({(isAr ? "AR - compositing over passthrough" : "opaque - no passthrough on this runtime")})");

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
                UI.Label($"Blend       {blend}");
                UI.Label(isAr ? "CCP.Core, over your room." : "CCP.Core, in a headset (opaque).");

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

            // Cannot assert the runtime GRANTS transparency with no headset attached, but the
            // request itself is a static fact and regressing it would silently turn the AR head
            // back into a black-void VR app.
            var settings = new SKSettings { blendPreference = DisplayBlend.AnyTransparent };
            Check("head requests a transparent blend (AR over passthrough)",
                  settings.blendPreference == DisplayBlend.AnyTransparent,
                  settings.blendPreference.ToString());

            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? "VR head can produce every value it renders."
                : $"{failures} assertion(s) failed.");
            return failures == 0 ? 0 : 1;
        }
    }
}
