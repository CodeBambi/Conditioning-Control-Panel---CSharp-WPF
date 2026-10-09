// PORTED from WPF 7.1.5 Controls/Leash/LeashDrawerSection.cs (offers, own card, held cards),
// LeashAskCard.cs (the offer: how strict, Put it on / Not now), LeashSelfCard.cs (holder line, Cut
// leash one click, what waits, pardons, how strict, do not disturb), LeashGateCard.cs (the gate's
// "{0} says" + title + pardon) and LeashHolderCard.cs (name, state, day, let go). Ledger social#1.
// The look is the Friends drawer's (FriendsDrawer.Pill / colours), not the WPF LeashLook chunky
// plates. No free text anywhere: every button sends a preset (Leash CONTRACT).
// ponytail: holder-side sheets (assign / punish / reward / tug), the 7-day strip, receipts timeline,
// sticker shelf, video cap slider, LeashFx sparks and the "?" explainer.
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public sealed partial class LeashTabView
    {
        private ILeashService? _bound;

        /// <summary>The service the page draws (tests hand one in; the app reads LeashHead).</summary>
        internal Func<ILeashService?> Resolve { get; set; } = () => Platform.LeashHead.Service;

        /// <summary>Rebinds to the live service and redraws every card from its snapshot.</summary>
        internal void Rebuild()
        {
            var svc = Resolve();
            if (!ReferenceEquals(svc, _bound))
            {
                if (_bound != null) _bound.SnapshotChanged -= OnSnapshot;
                _bound = svc;
                if (svc != null) svc.SnapshotChanged += OnSnapshot;
            }
            SectionHost.Children.Clear();
            if (svc != null && svc.Available)
            {
                var snap = svc.Snapshot;
                foreach (var o in snap.Offers) SectionHost.Children.Add(OfferCard(svc, o));
                if (snap.Me is { } me) SectionHost.Children.Add(SelfCard(svc, me));
                foreach (var h in snap.Holding) SectionHost.Children.Add(HeldCard(svc, h));
            }
            SyncEmpty();
        }

        private void OnSnapshot(LeashSnapshot _) => Dispatcher.UIThread.Post(Rebuild);

        private static Border Card(string tag, params Control[] rows)
        {
            var sp = new StackPanel { Spacing = 8 };
            foreach (var r in rows) sp.Children.Add(r);
            return new Border
            {
                Tag = tag,
                Background = FriendsDrawer.Raised,
                BorderBrush = FriendsDrawer.Line2,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 12),
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

        private static Button Pill(string label, string tag, bool primary, Action click)
        {
            var b = FriendsDrawer.Pill(label, primary ? FriendsDrawer.Mint : FriendsDrawer.Raised,
                primary ? FriendsDrawer.MintInk : FriendsDrawer.Text, tag, primary ? FriendsDrawer.Mint : FriendsDrawer.Line2);
            b.Padding = new Thickness(14, 6, 14, 6);
            b.Cursor = FriendsDrawer.Hand();
            b.Click += (_, _) => click();
            return b;
        }

        private static WrapPanel Row(params Control[] items)
        {
            var w = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var i in items) { i.Margin = new Thickness(0, 0, 8, 6); w.Children.Add(i); }
            return w;
        }

        /// <summary>WPF LeashAskCard: "{name} wants to hold your leash", how strict, Put it on / Not now.</summary>
        internal static Border OfferCard(ILeashService svc, LeashOffer o)
        {
            var pick = LeashIntensity.Standard;
            var levels = new WrapPanel();
            void Paint()
            {
                levels.Children.Clear();
                foreach (var lv in new[] { LeashIntensity.Soft, LeashIntensity.Standard, LeashIntensity.Strict })
                {
                    var l = lv;
                    var p = Pill(Loc.Get(LeashUiRules.Key(l)), "leash-ask-level-" + l.ToString().ToLowerInvariant(), l == pick,
                        () => { pick = l; Paint(); });
                    p.Margin = new Thickness(0, 0, 6, 0);
                    levels.Children.Add(p);
                }
            }
            Paint();
            return Card("leash-offer-" + o.From.Id,
                Text(o.From.Name + " " + Loc.Get("leash_ask_title"), 15, true),
                Text(Loc.Get("leash_ask_p_tasks")),
                Text(Loc.Get("leash_ask_p_cut")),
                Text(Loc.Get("leash_ask_level"), 12, true, FriendsDrawer.Muted),
                levels,
                Row(Pill(Loc.Get("leash_ask_yes"), "leash-ask-yes", true, () => _ = svc.AnswerAsync(o.From.Id, true, pick)),
                    Pill(Loc.Get("leash_ask_no"), "leash-ask-no", false, () => _ = svc.AnswerAsync(o.From.Id, false, pick))));
        }

        /// <summary>WPF LeashSelfCard + the gate: who holds it, Cut leash (one click, first thing under
        /// the name), what waits at the gate with a pardon, how strict, do not disturb.</summary>
        internal static Border SelfCard(ILeashService svc, MyLeash me)
        {
            var cut = Pill(Loc.Get("leash_cut"), "leash-cut", true, Platform.LeashHead.Cut);
            ToolTip.SetTip(cut, Loc.Get("leash_cut_tip"));
            cut.HorizontalAlignment = HorizontalAlignment.Stretch;
            cut.HorizontalContentAlignment = HorizontalAlignment.Center;

            var rows = new System.Collections.Generic.List<Control>
            {
                Text(me.Holder.Name + " " + Loc.Get("leash_self_holds"), 15, true),
                Text(Loc.GetF("leash_self_day", me.Day), 12, false, FriendsDrawer.Muted),
                cut,
            };

            // The gate (WPF LeashGateCard): every gate punishment, oldest first, with a pardon while any are held.
            var gates = me.Pending.Where(p => p.Kind != PunishKind.Chaster).OrderBy(p => p.At).ToList();
            if (gates.Count > 0) rows.Add(Text(Loc.GetF("leash_self_pending", gates.Count), 13, true, FriendsDrawer.Red));
            foreach (var p in gates)
            {
                var (key, arg) = LeashUiRules.GateTitle(p);
                var line = new StackPanel { Tag = "leash-gate-" + p.Pid, Spacing = 4 };
                line.Children.Add(Text(Loc.GetF("leash_gate_says", p.From.Name), 12, true, FriendsDrawer.Gold));
                line.Children.Add(Text(Loc.GetF(key, arg), 18, true));
                if (me.Pardons > 0)
                {
                    var pid = p.Pid;
                    line.Children.Add(Row(Pill(Loc.GetF("leash_gate_pardon", me.Pardons), "leash-gate-pardon", false,
                        () => _ = svc.PardonAsync(pid))));
                }
                rows.Add(line);
            }
            if (me.Assignment is { } a && a.Status == AssignStatus.Open)
                rows.Add(Text(Loc.GetF("leash_self_task_open", Loc.Get(LeashUiRules.Key(a.Kind)) + " " + a.Size, me.Holder.Name)));
            if (me.Pardons > 0) rows.Add(Text(Loc.GetF("leash_self_pardons", me.Pardons), 12));
            if (gates.Count == 0 && me.Assignment == null) rows.Add(Text(Loc.Get("leash_self_clear"), 12, false, FriendsDrawer.Muted));

            rows.Add(Text(Loc.Get("leash_self_level"), 12, true, FriendsDrawer.Muted));
            var lv = new WrapPanel();
            foreach (var l in new[] { LeashIntensity.Soft, LeashIntensity.Standard, LeashIntensity.Strict })
            {
                var level = l;
                lv.Children.Add(new Border { Margin = new Thickness(0, 0, 6, 0), Child =
                    Pill(Loc.Get(LeashUiRules.Key(level)), "leash-self-level-" + level.ToString().ToLowerInvariant(), level == me.Intensity,
                        () => _ = svc.SetIntensityAsync(level)) });
            }
            rows.Add(lv);

            rows.Add(Text(Loc.Get("leash_self_quiet"), 12, true, FriendsDrawer.Muted));
            var quiet = me.DndUntil is { } u && u > DateTimeOffset.UtcNow;
            var dnd = new WrapPanel();
            foreach (var (d, k) in new[] { (LeashDnd.Off, "leash_dnd_off"), (LeashDnd.OneHour, "leash_dnd_1h"),
                         (LeashDnd.FourHours, "leash_dnd_4h"), (LeashDnd.Today, "leash_dnd_today") })
            {
                var choice = d;
                var on = choice == LeashDnd.Off ? !quiet : false;
                dnd.Children.Add(new Border { Margin = new Thickness(0, 0, 6, 0), Child =
                    Pill(Loc.Get(k), "leash-self-dnd-" + k[10..], on, () => _ = svc.SetDndAsync(choice)) });
            }
            rows.Add(dnd);
            return Card("leash-self", rows.ToArray());
        }

        /// <summary>WPF LeashHolderCard header: name, state, day, and Let go.</summary>
        internal static Border HeldCard(ILeashService svc, HeldLeash h)
        {
            var quiet = h.DndUntil is { } u && u > DateTimeOffset.UtcNow;
            var state = Loc.Get(quiet ? "leash_state_quiet" : h.Online ? "leash_state_online" : "leash_state_offline");
            var who = h.Who.Id;
            return Card("leash-held-" + who,
                Text(h.Who.Name, 15, true),
                Text(Loc.GetF("leash_holder_day", h.Day) + " · " + state, 12, false, FriendsDrawer.Muted),
                Text(Loc.Get(LeashUiRules.Key(h.Intensity)), 12),
                Row(Pill(Loc.GetF("leash_menu_release", h.Who.Name), "leash-release", false, () => _ = svc.ReleaseAsync(who))));
        }
    }
}
