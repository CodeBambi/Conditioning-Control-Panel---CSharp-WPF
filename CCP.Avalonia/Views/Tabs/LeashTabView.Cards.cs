// The Leash page's GATE STAND-IN. WPF 7.1.5 lays LeashGateCard over the whole panel
// (MainWindow.Leash.cs) and starts the punishment through LeashTaskRunner; neither is on this head
// yet, so MainShellWindow.CheckLeashGate brings the player to this page and this block lists what
// waits at the gate ("{0} says" + title) with a pardon while any are held. Everything else on the
// page is WPF's LeashDrawerSection (LeashTabView.cs). Remove this file when LeashGateCard lands.
// No free text anywhere: every button sends a preset (Leash CONTRACT).
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public sealed partial class LeashTabView
    {
        /// <summary>WPF LeashGateCard's words, as a block under the section: every gate punishment,
        /// oldest first, with a pardon while any are held. Null when nothing waits.</summary>
        internal static Border? GateStandIn(ILeashService svc, MyLeash me)
        {
            var gates = me.Pending.Where(p => p.Kind != PunishKind.Chaster).OrderBy(p => p.At).ToList();
            if (gates.Count == 0) return null;
            var sp = new StackPanel { Spacing = 8 };
            foreach (var p in gates)
            {
                var (key, arg) = LeashUiRules.GateTitle(p);
                var line = new StackPanel { Tag = "leash-gate-" + p.Pid, Spacing = 4 };
                line.Children.Add(Text(Loc.GetF("leash_gate_says", p.From.Name), 12, true, FriendsDrawer.Gold));
                line.Children.Add(Text(Loc.GetF(key, arg), 18, true));
                if (me.Pardons > 0)
                {
                    var pid = p.Pid;
                    var b = FriendsDrawer.Pill(Loc.GetF("leash_gate_pardon", me.Pardons), FriendsDrawer.Raised, FriendsDrawer.Text, "leash-gate-pardon", FriendsDrawer.Line2);
                    b.Padding = new Thickness(14, 6, 14, 6);
                    b.Cursor = FriendsDrawer.Hand();
                    b.HorizontalAlignment = HorizontalAlignment.Left;
                    b.Click += (_, _) => _ = svc.PardonAsync(pid);
                    line.Children.Add(b);
                }
                sp.Children.Add(line);
            }
            return new Border
            {
                Tag = "leash-gate-standin",
                Background = FriendsDrawer.Raised,
                BorderBrush = FriendsDrawer.Line2,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 6, 0, 12),
                Child = sp,
            };
        }

        private static TextBlock Text(string s, double size = 13, bool bold = false, IBrush? fg = null) => new()
        {
            Text = s,
            FontSize = size,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap,
            Foreground = fg ?? FriendsDrawer.Text,
        };
    }
}
