using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
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
        /// Maps the Core catalogue to browse rows (WPF BuildProgramBrowseList :596-680). A card is
        /// enrollable only with a writable service whose CanEnroll agrees; no service, a read-only
        /// one, a locked card or a refusal shows the reason instead.
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
                var canEnroll = !locked && svc is { IsReadOnly: false } && svc.CanEnroll(definition, out _);
                var accent = AccentBrush(definition.AccentColor);
                // WPF ProgramsTab.cs:633-660. Banner: no default, five cards in the same fallback
                // strip read as a copy-paste bug. Crest: sigil, else day 1's mood plate; the art is a
                // luminance mask, so it masks an accent fill, never an Image (ProgramArt.cs header).
                var banner = ModArt.FirstOf(ProgramArtPaths.Banner(definition, includeDefault: false));
                var sigil = ProgramArtPaths.Sigil(definition);
                var crest = ModArt.FirstOf(sigil != null ? new[] { sigil } : Array.Empty<string>(), 256)
                            ?? ModArt.FirstOf(ProgramArtPaths.DayPlate(definition, definition.GetDay(1)), 256);
                items.Add(new ProgramBrowseItem
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
                    ActionText = Loc.Get(locked ? "btn_program_locked" : "btn_program_enroll"),
                    IsActionEnabled = canEnroll,
                    ActionOpacity = canEnroll ? 1.0 : 0.5,
                    // A refused card never blames a missing pledge (programs_locked_hint promises a
                    // pledge unlocks it, and no premium program is finishable here yet); a program
                    // this head can never finish says what it is missing (programs-3a decision).
                    ReasonText = canEnroll ? "" : ProgramService.UnavailableReason(definition, Platform.ProgramCapabilities.IsAvailable)
                                 ?? Loc.Get("programs_unavailable"),
                    ReasonVisible = !canEnroll,
                    CardOpacity = locked ? 0.72 : 1.0
                });
            }

            return items;
        }

        // ---- lifecycle (WPF MainWindow.ProgramsTab.cs:1999-2270). 3a: Enroll, Withdraw, today's
        // session. Under Lockdown each refuses (P05; WPF has no Lockdown gate here - the Avalonia
        // shell refuses every control that starts or stops something). ----

        /// <summary>P05: true (and says so) when Lockdown holds; the caller stops.</summary>
        private async Task<bool> ProgramRefusedByLockdown()
        {
            if (!LockdownActive) return false;
            await MessageDialog.ShowAsync(this, Loc.Get("title_lockdown"), Loc.Get("msg_you_are_in_lockdown_mode_nthere_is_no_escape"));
            return true;
        }

        private void RefreshProgramsTab() => Named<ProgramsTabView>("ProgramsTab")?.RefreshPrograms();

        /// <summary>WPF BtnProgramEnroll_Click :1999: CanEnroll BEFORE the dialog, Enroll only on a confirmed one.</summary>
        internal async Task EnrollProgramAsync(string? programId)
        {
            try
            {
                var svc = App.Programs;
                var def = svc?.Library.FirstOrDefault(p => string.Equals(p.Id, programId, StringComparison.OrdinalIgnoreCase));
                if (svc == null || def == null || svc.IsReadOnly) return;
                if (await ProgramRefusedByLockdown()) return;
                if (!svc.CanEnroll(def, out var reason))
                {
                    Serilog.Log.Information("Program enrollment blocked for {Program}: {Reason}", def.Id, reason);
                    await MessageDialog.ShowAsync(this, Loc.Get("programs_unavailable_title"), Loc.Get("programs_unavailable"));
                    return;
                }
                var dialog = new ProgramEnrollDialog(def);
                if (await dialog.ShowDialogSafe<bool?>(this) != true) return;
                // ShareLevel: the Avalonia dialog has no share picker, so the WPF default (Private) applies.
                svc.Enroll(def, dialog.StrictMode, dayBoundaryHour: dialog.DayBoundaryHour, nudgeHour: dialog.NudgeHour);
                RefreshProgramsTab();
            }
            catch (Exception ex) { Serilog.Log.Error(ex, "Program enrollment failed"); }
        }

        /// <summary>WPF BtnProgramWithdraw_Click :2079: always available while an enrollment exists; the
        /// confirm says when it also ends today's session.</summary>
        internal async Task WithdrawProgramAsync()
        {
            try
            {
                if (App.Programs is not { ActiveEnrollment: not null, IsReadOnly: false } svc) return;
                if (await ProgramRefusedByLockdown()) return;
                var sessionLive = App.Sessions is { IsRunning: true } r && svc.IsProgramSession(r.CurrentSession);
                var confirmed = await MessageDialog.ConfirmAsync(this, Loc.Get("programs_withdraw_confirm_title"),
                    Loc.Get(sessionLive ? "programs_withdraw_confirm_body_session" : "programs_withdraw_confirm_body"),
                    okText: Loc.Get("btn_program_withdraw_confirm"), cancelText: Loc.Get("btn_program_withdraw_keep"));
                if (!confirmed) return;
                svc.Withdraw();
                RefreshProgramsTab();
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Program withdraw failed"); }
        }

        /// <summary>WPF StartProgramSession :2200: never on top of a running session; the runner is
        /// attached on every start (WPF App.Programs?.AttachSessionEngine).</summary>
        internal async Task StartProgramSessionAsync()
        {
            try
            {
                if (App.Programs is not { IsReadOnly: false } svc || App.Sessions is not { } runner) return;
                if (await ProgramRefusedByLockdown()) return;
                if (runner.IsRunning)
                {
                    Serilog.Log.Information("[Programs] Start refused - a session is already running");
                    RefreshProgramsTab();
                    await MessageDialog.ShowAsync(this, Loc.Get("programs_session_busy_title"), Loc.Get("programs_session_busy_body"));
                    return;
                }
                var session = svc.BuildTodaySession();
                if (session == null)
                {
                    Serilog.Log.Warning("[Programs] BuildTodaySession returned null - nothing to start");
                    await MessageDialog.ShowAsync(this, Loc.Get("title_error"), Loc.Get("programs_session_start_failed"));
                    return;
                }
                svc.AttachSessionRunner(runner);
                StartSession(session);
                ProgramEngineBridge.RaiseSessionChanged();
                Serilog.Log.Information("[Programs] Started program session: {Name}", session.Name);
            }
            catch (Exception ex) { Serilog.Log.Error(ex, "[Programs] Failed to start today's session"); }
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
