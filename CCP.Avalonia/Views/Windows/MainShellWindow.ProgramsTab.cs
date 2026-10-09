using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// WPF MainWindow.ProgramsTab.cs:548 BuildProgramBrowseList. Maps the catalogue to browse
        /// rows; with no <paramref name="svc"/> (the service failed to start) every card stays
        /// browse-only with the truthful unavailable reason (progression#1).
        /// </summary>
        internal static IReadOnlyList<ProgramBrowseItem> BuildProgramBrowseItems(
            IReadOnlyList<ProgramDefinition> library, ProgramService? svc = null)
        {
            var items = new List<ProgramBrowseItem>(library.Count);
            // WPF 608ff3181 (ccp-bugs #966): the active mod's programs lead.
            foreach (var definition in ProgramBrowseOrder.Sort(library, CoreMods.ActiveModId))
            {
                var premium = definition.Tier == ProgramTier.Premium;
                // WPF MainWindow.ProgramsTab.cs:605 (ProgramHasPremium = Patreon.HasPremiumAccess, the
                // same answer CoreEntitlement is seeded with): owned premium cards drop the padlock.
                var locked = premium && !CoreEntitlement.HasPremium;
                var accent = AccentBrush(definition.AccentColor);
                // Mich (28e2aa0bc), WPF ProgramsTab.cs:633-660. Banner: no default, five cards in the same
                // fallback strip read as a copy-paste bug. Crest: sigil, else day 1's mood plate; the art is
                // a luminance mask, so it masks an accent fill, never an Image (ProgramArt.cs header).
                var banner = ModArt.FirstOf(ProgramArtPaths.Banner(definition, includeDefault: false));
                var sigil = ProgramArtPaths.Sigil(definition);
                var crest = ModArt.FirstOf(sigil != null ? new[] { sigil } : Array.Empty<string>(), 256)
                            ?? ModArt.FirstOf(ProgramArtPaths.DayPlate(definition, definition.GetDay(1)), 256);
                var item = new ProgramBrowseItem
                {
                    BannerArt = banner,
                    BannerVisible = banner != null,
                    ArtMask = crest == null ? null : new ImageBrush(crest) { Stretch = Stretch.Uniform },
                    ArtGlowBrush = crest == null ? Brushes.Transparent : ProgramRadialGlowBrush(accent, 130),
                    ArtVisible = crest != null,
                    IconOnlyVisible = crest == null,
                    Definition = definition,
                    ProgramId = definition.Id,
                    Icon = definition.Icon,
                    Title = definition.Title,
                    Subtitle = definition.Subtitle,
                    Pitch = definition.Pitch,
                    LengthLabel = Loc.GetF("programs_length_days", definition.LengthDays),
                    TierLabel = Loc.Get(premium ? "programs_tier_premium" : "programs_tier_free"),
                    TierBrush = premium ? accent : new SolidColorBrush(Color.Parse("#FFA9A3C2")),
                    TierBackground = premium
                        ? new SolidColorBrush(Color.Parse("#33FF69B4"))
                        : new SolidColorBrush(Color.Parse("#332DFF9E")),
                    AccentBrush = accent,
                    IsLocked = locked,
                    CardOpacity = 1.0
                };
                var canEnroll = svc != null && svc.CanEnroll(definition, out _);
                if (locked)
                {
                    // WPF: enabled, routes to the App Info popup (tiers), never to enrollment.
                    item.ActionText = Loc.Get("btn_program_locked");
                    item.IsActionEnabled = svc != null;
                    item.ActionOpacity = svc != null ? 1.0 : 0.5;
                    item.ReasonText = Loc.Get("programs_locked_hint");
                    item.ReasonVisible = true;
                    item.CardOpacity = 0.72;
                }
                else if (!canEnroll)
                {
                    item.ActionText = Loc.Get("btn_program_enroll");
                    item.IsActionEnabled = false;
                    // A program this head can never finish says what it is missing (programs-3a).
                    item.ReasonText = ProgramService.UnavailableReason(definition, Platform.ProgramCapabilities.IsAvailable)
                                      ?? Loc.Get("programs_unavailable");
                    item.ReasonVisible = true;
                    item.CardOpacity = 0.72;
                }
                else
                {
                    item.ActionText = Loc.Get("btn_program_enroll");
                    item.IsActionEnabled = true;
                    item.ActionOpacity = 1.0;
                }
                items.Add(item);
            }

            return items;
        }

        private bool _programCardHooked;

        /// <summary>WPF ProgramTodayCard_Loaded + EnsureProgramsSubscribed: the Home Today card follows
        /// the program's events (progression#1).</summary>
        internal void ProgramTodayCard_Loaded()
        {
            if (!_programCardHooked && App.Programs is { } svc)
            {
                _programCardHooked = true;
                EventHandler repaint = (_, _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(RefreshProgramTodayCard);
                svc.TodayChanged += repaint;
                svc.ProgramLapsed += (_, _) => repaint(null, EventArgs.Empty);
                svc.ProgramGraduated += (_, _) => repaint(null, EventArgs.Empty);
                svc.DayCompleted += (_, _) => repaint(null, EventArgs.Empty);
            }
            RefreshProgramTodayCard();
        }

        /// <summary>WPF ProgramTodayCard_Click: a front-page tile navigates.</summary>
        internal void ProgramTodayCard_Click() => ShowTab("programs");

        /// <summary>WPF MainWindow.ProgramsTab.cs:2237 RefreshProgramTodayCard: "Day N · title · what is left".
        /// ponytail: ApplyProgramBannerArt (progression#3 banner art) is not ported; the card shows no art.</summary>
        internal void RefreshProgramTodayCard()
        {
            try
            {
                var dash = Named<SettingsTabView>("SettingsTab");
                if (dash?.FindControl<Button>("ProgramTodayCard") is not { } card) return;
                var svc = App.Programs;
                var enrollment = svc?.ActiveEnrollment;
                var program = svc?.ActiveProgram;
                var day = svc?.Today;
                var record = svc?.TodayRecord;
                if (svc == null || enrollment == null || program == null || day == null || record == null ||
                    enrollment.State is ProgramEnrollmentState.Withdrawn or ProgramEnrollmentState.Graduated)
                {
                    card.IsVisible = false;
                    return;
                }
                var accent = AccentBrush(program.AccentColor);
                card.BorderBrush = accent;
                if (dash.FindControl<Border>("ProgramTodayAccent") is { } bar) bar.Background = accent;
                if (dash.FindControl<TextBlock>("TxtProgramTodayTitle") is { } title)
                {
                    title.Foreground = accent;
                    title.Text = program.Title;
                }
                string remainder;
                if (enrollment.State == ProgramEnrollmentState.Paused) remainder = Loc.Get("programs_card_paused");
                else if (enrollment.State == ProgramEnrollmentState.Lapsed) remainder = Loc.Get("programs_card_lapsed");
                else
                {
                    var parts = new List<string>();
                    if (!record.SessionCompleted) parts.Add(Loc.Get("programs_card_session_left"));
                    var tasksLeft = svc.RequiredTasks(day).Count(t => !svc.IsTaskComplete(record, t));
                    if (tasksLeft == 1) parts.Add(Loc.Get("programs_card_task_left_one"));
                    else if (tasksLeft > 1) parts.Add(Loc.GetF("programs_card_task_left_many", tasksLeft));
                    remainder = parts.Count == 0 ? Loc.Get("programs_card_all_done") : Loc.GetF("programs_card_remaining", string.Join(", ", parts));
                }
                if (dash.FindControl<TextBlock>("TxtProgramTodayLine") is { } line)
                    line.Text = string.Join("  ·  ", new[] { Loc.GetF("programs_card_day", enrollment.CurrentDay), day.Title, remainder });
                card.IsVisible = true;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "RefreshProgramTodayCard failed");
            }
        }

        /// <summary>WPF ProgramsTab.cs:309 ProgramRadialGlowBrush: accent at alpha, fading out at the radius.</summary>
        internal static IBrush ProgramRadialGlowBrush(IBrush accent, byte alpha,
            double centerX = 0.5, double centerY = 0.5, double radius = 0.75)
        {
            if (accent is not ISolidColorBrush solid) return Brushes.Transparent;
            var c = solid.Color;
            var centre = new global::Avalonia.RelativePoint(centerX, centerY, global::Avalonia.RelativeUnit.Relative);
            return new RadialGradientBrush
            {
                Center = centre,
                GradientOrigin = centre,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(alpha, c.R, c.G, c.B), 0),
                    new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1),
                },
                RadiusX = new global::Avalonia.RelativeScalar(radius, global::Avalonia.RelativeUnit.Relative),
                RadiusY = new global::Avalonia.RelativeScalar(radius, global::Avalonia.RelativeUnit.Relative),
            };
        }

        internal static IBrush AccentBrush(string? hex)
        {
            try
            {
                return string.IsNullOrWhiteSpace(hex)
                    ? new SolidColorBrush(Color.Parse("#FFFF69B4"))
                    : new SolidColorBrush(Color.Parse(hex));
            }
            catch (FormatException)
            {
                return new SolidColorBrush(Color.Parse("#FFFF69B4"));
            }
        }
    }
}
