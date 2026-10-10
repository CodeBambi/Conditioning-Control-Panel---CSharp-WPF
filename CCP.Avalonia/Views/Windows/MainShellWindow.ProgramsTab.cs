using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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
        /// one, a locked card or a refusal shows the reason instead. A locked premium card stays
        /// pressable while a service exists: it opens the plans page, never the enrollment (WPF).
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
                    // WPF: the locked button is live and routes to the plans page (EnrollProgramAsync).
                    IsActionEnabled = locked ? svc != null : canEnroll,
                    ActionOpacity = (locked ? svc != null : canEnroll) ? 1.0 : 0.5,
                    // A locked card carries WPF's pledge hint; a program this head can never finish
                    // says what it is missing (programs-3a decision).
                    ReasonText = canEnroll ? null
                        : locked ? Loc.Get("programs_locked_hint")
                        : ProgramService.UnavailableReason(definition, Platform.ProgramCapabilities.IsAvailable)
                          ?? Loc.Get("programs_unavailable"),
                    ReasonVisible = !canEnroll,
                    CardOpacity = canEnroll ? 1.0 : 0.72
                });
            }

            return items;
        }

        // ---- lifecycle (WPF MainWindow.ProgramsTab.cs:1999-2270): Enroll, Withdraw, today's
        // session, Pause/Resume, Restart, Dismiss and the ritual photo. Under Lockdown the ones that
        // start or stop something refuse (P05; WPF has no Lockdown gate here - the Avalonia shell
        // refuses every control that starts or stops something). A read-only service refuses all. ----

        /// <summary>P05: true (and says so) when Lockdown holds; the caller stops.</summary>
        private async Task<bool> ProgramRefusedByLockdown()
        {
            if (!LockdownActive) return false;
            await MessageDialog.ShowAsync(this, Loc.Get("title_lockdown"), Loc.Get("msg_you_are_in_lockdown_mode_nthere_is_no_escape"));
            return true;
        }

        /// <summary>WPF SuppressNextSessionSummary (MainWindow.ProgramsTab.cs:2111): ends OUR session without
        /// the "ended early" recap on top of a confirm (withdraw) or a panic. Foreign sessions are untouched.</summary>
        internal void EndProgramSessionQuietly(string reason)
        {
            if (App.Programs is not { } svc || App.Sessions is not { IsRunning: true } r || !svc.IsProgramSession(r.CurrentSession)) return;
            _suppressNextSessionSummary = true;
            svc.StopProgramSessionIfRunning(reason, suppressAbandonTracking: true);
        }

        /// <summary>The Programs tab and the Home Today card, after any lifecycle change.</summary>
        private void RefreshProgramsTab()
        {
            Named<ProgramsTabView>("ProgramsTab")?.RefreshPrograms();
            RefreshProgramTodayCard();
        }

        /// <summary>WPF BtnProgramEnroll_Click :1999: CanEnroll BEFORE the dialog, Enroll only on a confirmed one.</summary>
        internal async Task EnrollProgramAsync(string? programId)
        {
            try
            {
                var svc = App.Programs;
                var def = svc?.Library.FirstOrDefault(p => string.Equals(p.Id, programId, StringComparison.OrdinalIgnoreCase));
                if (svc == null || def == null) return;
                // WPF ShowAppInfoPopup: a locked premium card opens the plans, never the ceremony.
                if (def.Tier == ProgramTier.Premium && !CoreEntitlement.HasPremium)
                {
                    OpenAppSettingsSection("account");
                    return;
                }
                if (svc.IsReadOnly) return;
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
                    defaultToCancel: true,   // WPF MessageBoxResult.No
                    okText: Loc.Get("btn_program_withdraw_confirm"), cancelText: Loc.Get("btn_program_withdraw_keep"));
                if (!confirmed) return;
                EndProgramSessionQuietly("withdraw");   // WPF :2111: the confirm already said it stops
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
                StartSession(session);   // the row repaints when the runner really starts (StartSession)
                Serilog.Log.Information("[Programs] Started program session: {Name}", session.Name);
            }
            catch (Exception ex) { Serilog.Log.Error(ex, "[Programs] Failed to start today's session"); }
        }

        /// <summary>WPF BtnProgramPauseResume_Click :2050: Resume, or Pause unless today's own session is
        /// in flight (ProgramService.CanPause), which says so instead.</summary>
        internal async Task PauseResumeProgramAsync()
        {
            try
            {
                if (App.Programs is not { ActiveEnrollment: { } enrollment, IsReadOnly: false } svc) return;
                if (await ProgramRefusedByLockdown()) return;
                if (enrollment.State == ProgramEnrollmentState.Paused) svc.Resume();
                else if (!svc.Pause())
                {
                    await MessageDialog.ShowAsync(this, Loc.Get("programs_pause_blocked_title"), Loc.Get("programs_pause_blocked_body"));
                    return;
                }
                RefreshProgramsTab();
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Program pause/resume failed"); }
        }

        /// <summary>WPF BtnProgramRestart_Click: a lapsed run starts its next attempt.</summary>
        internal async Task RestartProgramAsync()
        {
            try
            {
                if (App.Programs is not { IsReadOnly: false } svc) return;
                if (await ProgramRefusedByLockdown()) return;
                svc.RestartAfterLapse();
                RefreshProgramsTab();
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Program restart failed"); }
        }

        /// <summary>WPF BtnProgramDismissGraduated_Click: clears the finished run, back to browse.</summary>
        internal void DismissGraduatedProgram()
        {
            try
            {
                if (App.Programs is not { IsReadOnly: false } svc) return;
                svc.DismissGraduated();
                RefreshProgramsTab();
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Program graduation dismiss failed"); }
        }

        /// <summary>WPF BtnProgramSubmitRitual_Click :2140: one local photo for today's ritual task.</summary>
        internal async Task SubmitProgramRitualAsync(string? taskId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(taskId) || App.Programs is not { IsReadOnly: false } svc) return;
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Loc.Get("programs_photo_dialog_title"),
                    AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp", "*.webp" } } },
                });
                var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
                if (path == null) return;
                svc.SubmitRitualTask(taskId, path, null);
                RefreshProgramsTab();
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Program ritual submission failed"); }
        }

        // ---- the Home Today card (WPF MainWindow.ProgramsTab.cs:2237) ----

        private ProgramService? _programCardHooked;

        /// <summary>WPF ProgramTodayCard_Loaded + EnsureProgramsSubscribed: the Home Today card follows
        /// the program's events (progression#1).</summary>
        internal void ProgramTodayCard_Loaded()
        {
            if (App.Programs is { } svc && !ReferenceEquals(svc, _programCardHooked))
            {
                _programCardHooked = svc;
                EventHandler repaint = (_, _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(RefreshProgramTodayCard);
                svc.TodayChanged += repaint;
                EventHandler<ProgramLapsedEventArgs> lapsed = (_, _) => repaint(null, EventArgs.Empty);
                EventHandler<ProgramDayEventArgs> day = (_, _) => repaint(null, EventArgs.Empty);
                svc.ProgramLapsed += lapsed;
                svc.ProgramGraduated += day;
                svc.DayCompleted += day;
                // The service outlives the shell: a closed shell must not stay rooted by it.
                Closed += (_, _) => { svc.TodayChanged -= repaint; svc.ProgramLapsed -= lapsed; svc.ProgramGraduated -= day; svc.DayCompleted -= day; };
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
