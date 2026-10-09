// --click-through: drive the real shell with real pointer input, headlessly, and photograph
// every step.
//
// WHY THIS EXISTS. The port had three proofs and none of them could see a broken button:
//   --smoke      boots the head and resolves strings. Never touches a control.
//   --render-all draws all 190 views. A still frame, and it CONSTRUCTS each view directly, so it
//                cannot tell that nothing in the app ever opens it - 28 finished views were
//                unreachable for months while this reported 189/189.
//   --nav-check  calls ShowTab(...) in code. That is the navigation METHOD, not the button.
//
// So a defect that survives all three is a defect nobody sees until they run the app: the nav
// doors parked at IsHitTestVisible="False" in markup, never set back, drew their entries and
// swallowed every click on them. Only the Home door worked, because it is the one panel the
// markup does not park. Four months of green runs; found by a person clicking it.
//
// This driver closes that gap. It dispatches a REAL MouseDown/MouseUp pair at a control's real
// on-screen centre, through Avalonia's real hit-testing, and asserts what the app did afterwards.
// If a control is invisible, zero-sized, covered by something else, or not hit-testable, the click
// lands where a user's click would land - on whatever is actually there - and the step fails.
//
// WHAT IT STILL CANNOT PROVE, stated so no one reads a green run as more than it is: it runs
// headless, so it proves nothing about the compositor - click-through, topmost, per-monitor
// placement and the overlay windows are invisible to it. It also does not know what a feature is
// SUPPOSED to look like; it only knows what this app does. A step that passes means "the click
// reached a control and the app changed the way this file says it should", never "the feature
// works as it does on Windows".

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ConditioningControlPanel.Avalonia
{
    internal static class ClickThrough
    {
        private sealed record Step(string Control, string Expect, Func<Views.Windows.MainShellWindow, bool> Check);

        public static int Run(string outDir)
        {
            Directory.CreateDirectory(outDir);
            RenderProof.EnsureSetUp();

            var w = new Views.Windows.MainShellWindow { Width = 1400, Height = 900 };
            w.Show();
            Pump();

            // The script. Each step clicks ONE named control and states what must be true after.
            // Keep the expectations behavioural - "the You door is open", not "Height is 224" -
            // because an implementation detail in an assertion is how the door bug hid: the old
            // check watched Height, and Height was never the thing that was broken.
            // Nav rework (WPF 7.1.5): the rail is always open and labelled, a row click lands on its
            // section, and a section's pages are pills on the strip above the page (NavPill_<key>,
            // built in code, so they are found through the visual tree, not the name scope).
            var steps = new List<Step>
            {
                new("DoorYou",          "the You row opens You",              s => s.ExpandedDoor == "you"),
                new("NavPill_quests",   "its Quests pill navigates",          s => s.CurrentTab == "quests"),
                new("DoorStudio",       "the Studio row opens Studio",        s => s.ExpandedDoor == "studio"),
                new("NavPill_presets",  "its Presets pill navigates",         s => s.CurrentTab == "presets"),
                new("DoorPlay",         "the Play row opens Play",            s => s.ExpandedDoor == "play"),
                new("NavPill_deeper",   "its Deeper pill navigates",          s => s.CurrentTab == "deeper"),
                new("DoorCompanion",    "the Companion row opens Companion",  s => s.ExpandedDoor == "companion"),
                new("DoorSocial",       "the Social row opens Social",        s => s.ExpandedDoor == "social"),
                new("DoorLibrary",      "the Library row opens Library",      s => s.ExpandedDoor == "library"),
                new("DoorHome",         "the Home row opens the dashboard",   s => s.CurrentTab == "settings"),
                new("DoorSettings",     "the gear opens Settings",            s => s.CurrentTab == "appsettings"),
            };

            var fails = 0;
            var n = 0;

            if (w.NavRailExpanded) Console.WriteLine("  [PASS] the rail is open and labelled");
            else { fails++; Console.Error.WriteLine("  [FAIL] the nav rail is not open"); }
            Save(w, Path.Combine(outDir, "00-start.png"));

            foreach (var step in steps)
            {
                n++;
                var target = w.FindControl<Control>(step.Control)
                    ?? w.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == step.Control);
                if (target is null)
                {
                    Fail($"step {n}: no control named {step.Control}");
                    continue;
                }

                if (!TryClick(w, target, out var why))
                {
                    Fail($"step {n}: {step.Control} could not be clicked - {why}");
                    Save(w, Path.Combine(outDir, $"{n:00}-{step.Control}-FAIL.png"));
                    continue;
                }

                Pump();
                var ok = false;
                try { ok = step.Check(w); }
                catch (Exception ex) { why = ex.Message; }

                Save(w, Path.Combine(outDir, $"{n:00}-{step.Control}-{(ok ? "ok" : "FAIL")}.png"));
                if (ok) Console.WriteLine($"  [PASS] click {step.Control,-16} -> {step.Expect}");
                else Fail($"step {n}: clicked {step.Control} and {step.Expect} did not happen");
            }

            Console.WriteLine(fails == 0
                ? $"\nclick-through: {steps.Count} clicks, every one reached its control and did what it says. Frames in {outDir}"
                : $"\nclick-through: {fails} of {steps.Count} steps failed. Frames in {outDir}");
            return fails == 0 ? 0 : 1;

            void Fail(string m) { fails++; Console.Error.WriteLine("  [FAIL] " + m); }
        }

        /// <summary>
        /// A click at the control's real centre, in window coordinates, through the real hit test.
        /// Refuses rather than fakes when the control is not actually clickable, and says which of
        /// the three ways it was not: not visible, no size, or something else is on top of it.
        /// That last case is the one a direct <c>RaiseEvent</c> would hide, and it is exactly what
        /// a covered or hit-test-disabled panel looks like to a user.
        /// </summary>
        private static bool TryClick(Window w, Control target, out string why)
        {
            why = "";
            if (!target.IsEffectivelyVisible) { why = "not visible"; return false; }
            if (target.Bounds.Width <= 0 || target.Bounds.Height <= 0) { why = "zero size"; return false; }

            var origin = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), w);
            if (origin is not { } p) { why = "does not map into the window"; return false; }

            var hit = w.InputHitTest(p);
            if (hit is null) { why = "nothing hit-testable at its centre"; return false; }
            if (hit is not Visual v || (!ReferenceEquals(hit, target) && !v.GetVisualAncestors().Contains(target)))
            {
                why = $"the click lands on {hit.GetType().Name}, not on it";
                return false;
            }

            w.MouseMove(p);
            w.MouseDown(p, MouseButton.Left);
            w.MouseUp(p, MouseButton.Left);
            return true;
        }

        private static void Pump()
        {
            for (var i = 0; i < 4; i++) RenderProof.Pump();
        }

        private static void Save(Window w, string path)
        {
            try { w.CaptureRenderedFrame()?.Save(path); }
            catch (Exception ex) { Console.Error.WriteLine($"  (frame not saved: {ex.Message})"); }
        }
    }
}
