using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// The enrollment ceremony. Three steps on one scrollable panel: the whole arc up front,
    /// Standard vs Strict, and the clock plus a typed contract phrase.
    ///
    /// The dialog decides nothing - it collects. The caller reads <see cref="StrictMode"/>,
    /// <see cref="DayBoundaryHour"/> and <see cref="NudgeHour"/> and calls the program service.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/ProgramEnrollDialog.xaml.cs. Deviations:
    ///  - <c>ProgramDefinition</c> lives in the WPF head (Models/Program), which this head may not
    ///    reference, so the dialog takes <see cref="ProgramEnrollInfo"/> - the same fields under
    ///    the same names, nothing more. See the ponytail note on that type.
    ///  - WPF's <c>DialogResult</c> becomes <c>Close(bool)</c>, as in TextEditorDialog: Avalonia
    ///    carries the result through <c>ShowDialog&lt;bool?&gt;</c>.
    ///  - <c>SystemParameters.WorkArea</c> -> <c>Screens.Primary.WorkingArea</c> (also the primary
    ///    monitor, also excluding the taskbar) divided by the screen scaling, because Avalonia
    ///    reports it in physical pixels where WPF reported device-independent units.
    ///  - <c>ProgramShareLevel ShareLevel</c> is dropped: the enum is the head's, every new
    ///    enrollment is Private, and nothing in this head reads it.
    ///  - <c>ColorConverter.ConvertFromString</c> -> <c>Color.TryParse</c>; <c>Freeze()</c> ->
    ///    <c>ToImmutable()</c>.
    ///  - PreviewKeyDown -> KeyDown, DragMove() -> BeginMoveDrag(e).
    /// </summary>
    // STILL UNREACHABLE, blocked on an unported caller — not on this dialog.
    // Its only WPF call site is BtnProgramEnroll_Click in
    // ConditioningControlPanel/MainWindow/MainWindow.ProgramsTab.cs:2010, and this head's
    // CCP.Avalonia/Views/Windows/MainShellWindow.ProgramsTab.cs is a wholesale stub for a reason
    // stronger than a missing seam: ProgramDefinition, ProgramEnrollment and ProgramService are
    // all still in ConditioningControlPanel/Models/Program/ and Services/Program/, so there is no
    // ProgramDefinition on this head to hand the constructor and no ProgramService to enrol into.
    // `grep -rl "ProgramDefinition" CCP.Core` returning nothing is the check. Inventing a
    // different entry point would produce an enrol button with nothing behind it.
    public partial class ProgramEnrollDialog : Window
    {
        private readonly ProgramEnrollInfo _program;

        private readonly RadioButton _radioModeStrict;
        private readonly ComboBox _cmbBoundaryHour;
        private readonly ComboBox _cmbNudgeHour;
        private readonly TextBox _txtContractInput;
        private readonly TextBlock _txtBlockedHint;
        private readonly Button _btnConfirmEnroll;

        public bool StrictMode { get; private set; }
        public int DayBoundaryHour { get; private set; } = 4;
        public int NudgeHour { get; private set; } = 20;

        /// <summary>Render/design constructor: sample data so --render-view can draw the dialog.</summary>
        internal ProgramEnrollDialog() : this(SampleProgram()) { }

        public ProgramEnrollDialog(ProgramEnrollInfo program)
        {
            AvaloniaXamlLoader.Load(this);

            _program = program ?? throw new ArgumentNullException(nameof(program));

            _radioModeStrict = this.FindControl<RadioButton>("RadioModeStrict")!;
            _cmbBoundaryHour = this.FindControl<ComboBox>("CmbBoundaryHour")!;
            _cmbNudgeHour = this.FindControl<ComboBox>("CmbNudgeHour")!;
            _txtContractInput = this.FindControl<TextBox>("TxtContractInput")!;
            _txtBlockedHint = this.FindControl<TextBlock>("TxtBlockedHint")!;
            _btnConfirmEnroll = this.FindControl<Button>("BtnConfirmEnroll")!;

            var accent = ParseBrush(_program.AccentColor);

            this.FindControl<TextBlock>("TxtProgramIcon")!.Text = _program.Icon;
            this.FindControl<TextBlock>("TxtProgramTitle")!.Text = _program.Title;
            this.FindControl<TextBlock>("TxtProgramSubtitle")!.Text = _program.Subtitle;
            this.FindControl<TextBlock>("TxtProgramPitch")!.Text = _program.Pitch;

            // (a) The whole arc, up front.
            var chapters = new List<ProgramChapterItem>();
            foreach (var chapter in _program.Chapters)
            {
                var days = chapter.Days.Select(d => d.DayIndex).ToList();
                var first = days.Count > 0 ? days.Min() : 0;
                var last = days.Count > 0 ? days.Max() : 0;

                var hasReward = !string.IsNullOrWhiteSpace(chapter.RewardDescription);
                chapters.Add(new ProgramChapterItem
                {
                    Name = chapter.Name,
                    Subtitle = chapter.Subtitle,
                    DaysLabel = Loc.GetF("program_enroll_chapter_days", first, last),
                    RewardText = hasReward ? chapter.RewardDescription! : "",
                    RewardVisible = hasReward,
                    AccentBrush = string.IsNullOrWhiteSpace(chapter.AccentColor)
                        ? accent
                        : ParseBrush(chapter.AccentColor)
                });
            }
            this.FindControl<ItemsControl>("ChapterList")!.ItemsSource = chapters;

            var allDays = _program.AllDays.ToList();
            var minMinutes = allDays.Count > 0 ? allDays.Min(d => d.SessionMinutes) : 0;
            var maxMinutes = allDays.Count > 0 ? allDays.Max(d => d.SessionMinutes) : 0;
            this.FindControl<TextBlock>("TxtCommitment")!.Text = minMinutes == maxMinutes
                ? Loc.GetF("program_enroll_commitment_flat", _program.LengthDays, minMinutes)
                : Loc.GetF("program_enroll_commitment", _program.LengthDays, minMinutes, maxMinutes);

            // (b) Standard vs Strict.
            this.FindControl<TextBlock>("TxtModeStandardBody")!.Text =
                Loc.GetF("program_enroll_mode_standard_body", _program.Rules.DaysOffAllowed);
            if (!_program.Rules.StrictAvailable)
            {
                _radioModeStrict.IsEnabled = false;
                this.FindControl<TextBlock>("TxtStrictUnavailable")!.IsVisible = true;
            }

            // (d) The clock.
            for (int h = 0; h < 24; h++)
                _cmbBoundaryHour.Items.Add(Loc.GetF("program_enroll_hour_format", h));
            _cmbBoundaryHour.SelectedIndex = Math.Clamp(_program.Rules.DefaultDayBoundaryHour, 0, 23);

            _cmbNudgeHour.Items.Add(Loc.Get("program_enroll_nudge_off"));
            for (int h = 0; h < 24; h++)
                _cmbNudgeHour.Items.Add(Loc.GetF("program_enroll_hour_format", h));
            _cmbNudgeHour.SelectedIndex = 21; // 20:00, one past the "Off" row

            this.FindControl<TextBlock>("TxtSafetyNote")!.Text = _program.SafetyNote;
            this.FindControl<TextBlock>("TxtContractPhrase")!.Text = _program.ContractPhrase;

            // Handlers live here rather than in markup, per the porting convention.
            _txtContractInput.TextChanged += (_, _) => UpdateConfirmState();
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
            _btnConfirmEnroll.Click += (_, _) => BtnConfirm_Click();
            KeyDown += Window_KeyDown;
            PointerPressed += Window_PointerPressed;

            UpdateConfirmState();
        }

        /// <summary>
        /// Caps the content-driven height at the screen the dialog opens on. Runs on open rather
        /// than in the constructor because <see cref="TopLevel.Screens"/> only has a screen list
        /// once the window has a platform impl.
        ///
        /// <para>Not expressible in XAML: the work area is a runtime value, and it is the one that
        /// matters - it excludes the taskbar. The 40 DIP of slack keeps the shadowless chromeless
        /// border off the very edge. Once the clamp binds, the Grid's star row shrinks, the
        /// ScrollViewer inside it takes over, and the Auto footer keeps its buttons.</para>
        /// </summary>
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            try
            {
                var screen = Screens?.Primary;
                if (screen is null) return;
                var available = screen.WorkingArea.Height / screen.Scaling - 40;
                if (available > 0) MaxHeight = Math.Max(MinHeight, available);
            }
            catch
            {
                // No work area (a locked session, a headless render): the content height stands.
            }
        }

        /// <summary>
        /// Escape cancels. A chromeless, non-resizable modal with no close button otherwise leaves
        /// Alt+F4 as the only exit, and this is a screen the user is explicitly allowed to walk
        /// away from. Closing with false is what tells the caller not to enroll.
        /// </summary>
        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            try { Close(false); } catch { /* already closing */ }
        }

        /// <summary>
        /// Chromeless window, so dragging the card is the only way to move it - which matters when
        /// the clamp above has it filling the work area. The controls inside (TextBox, ComboBox,
        /// Buttons, the card radios) all mark the event handled for their own focus handling, so
        /// this only ever fires on the dialog's own dead space.
        /// </summary>
        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            try { BeginMoveDrag(e); } catch { /* not a drag-able moment */ }
        }

        private static IBrush ParseBrush(string hex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out var color))
                    return new SolidColorBrush(color).ToImmutable();
            }
            catch { /* a bad accent must never block enrollment */ }

            try
            {
                if (Application.Current is { } app &&
                    app.TryFindResource("PinkBrush", out var res) && res is IBrush brush)
                    return brush;
            }
            catch { /* fall through */ }

            return Brushes.HotPink;
        }

        private static string Normalize(string? value) =>
            Regex.Replace((value ?? "").Trim(), @"\s+", " ");

        private bool ContractMatches() =>
            string.IsNullOrWhiteSpace(_program.ContractPhrase) ||
            string.Equals(Normalize(_txtContractInput.Text), Normalize(_program.ContractPhrase),
                StringComparison.OrdinalIgnoreCase);

        private void UpdateConfirmState()
        {
            var contractOk = ContractMatches();
            _btnConfirmEnroll.IsEnabled = contractOk;

            _txtBlockedHint.Text = contractOk ? "" : Loc.Get("program_enroll_contract_hint");
        }

        private void BtnConfirm_Click()
        {
            StrictMode = _radioModeStrict.IsChecked == true && _program.Rules.StrictAvailable;

            DayBoundaryHour = Math.Clamp(_cmbBoundaryHour.SelectedIndex, 0, 23);
            NudgeHour = _cmbNudgeHour.SelectedIndex <= 0 ? -1 : _cmbNudgeHour.SelectedIndex - 1;

            Close(true);
        }

        private static ProgramEnrollInfo SampleProgram() => new()
        {
            Icon = "🌸",
            Title = "The Descent",
            Subtitle = "28 days, four chapters",
            Pitch = "A month of careful, escalating conditioning. One session a day, and a short list of tasks around it.",
            AccentColor = "#FF69B4",
            LengthDays = 28,
            SafetyNote = "You can stop at any time, and nothing is lost when you do. Days off are built in. "
                       + "If a day feels wrong, skip it - the program is a schedule, not a contract with anyone but yourself.",
            ContractPhrase = "I choose this, and I choose to keep choosing it.",
            Rules = new ProgramEnrollRules { DaysOffAllowed = 2, StrictAvailable = true, DefaultDayBoundaryHour = 4 },
            Chapters =
            {
                new ProgramEnrollChapter
                {
                    Name = "Chapter I - Settling",
                    Subtitle = "Short sessions, gentle pacing. Learning what the routine feels like.",
                    AccentColor = "#FF8FC7",
                    Days = Enumerable.Range(1, 7).Select(i => new ProgramEnrollDay { DayIndex = i, SessionMinutes = 30 }).ToList()
                },
                new ProgramEnrollChapter
                {
                    Name = "Chapter II - Deepening",
                    Subtitle = "The sessions lengthen and the task list grows a little teeth.",
                    AccentColor = "#E85BB0",
                    RewardDescription = "Unlocks the Deepening ambient track, permanently.",
                    Days = Enumerable.Range(8, 7).Select(i => new ProgramEnrollDay { DayIndex = i, SessionMinutes = 45 }).ToList()
                },
                new ProgramEnrollChapter
                {
                    Name = "Chapter III - Holding",
                    Subtitle = "Consistency over intensity. The same weight, every day, without flinching.",
                    AccentColor = "#C44BA0",
                    Days = Enumerable.Range(15, 7).Select(i => new ProgramEnrollDay { DayIndex = i, SessionMinutes = 60 }).ToList()
                },
                new ProgramEnrollChapter
                {
                    Name = "Chapter IV - The Descent",
                    Subtitle = "The last week. Longest sessions, and the boss day at the end of it.",
                    AccentColor = "#9B3A8C",
                    RewardDescription = "Files the final session into your own catalogue, replayable forever.",
                    Days = Enumerable.Range(22, 7).Select(i => new ProgramEnrollDay { DayIndex = i, SessionMinutes = 75 }).ToList()
                }
            }
        };
    }

    /// <summary>
    /// What the enrollment ceremony reads off a program, and nothing else.
    ///
    /// ponytail: local stand-in for ConditioningControlPanel.Models.Program.ProgramDefinition,
    /// which lives in the WPF head and cannot be referenced from here. Field names and shapes are
    /// the original's, so when Models/Program moves to Core this type is deleted and the
    /// constructor's parameter type changed - no other edit.
    /// </summary>
    public class ProgramEnrollInfo
    {
        public string Icon { get; set; } = "📅";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Pitch { get; set; } = "";
        public string AccentColor { get; set; } = "#FF69B4";
        public int LengthDays { get; set; }
        public string SafetyNote { get; set; } = "";
        public string ContractPhrase { get; set; } = "";
        public List<ProgramEnrollChapter> Chapters { get; set; } = new();
        public ProgramEnrollRules Rules { get; set; } = new();

        public IEnumerable<ProgramEnrollDay> AllDays =>
            Chapters.SelectMany(c => c.Days).OrderBy(d => d.DayIndex);
    }

    /// <summary>A named block of days. See <see cref="ProgramEnrollInfo"/>.</summary>
    public class ProgramEnrollChapter
    {
        public string Name { get; set; } = "";
        public string Subtitle { get; set; } = "";
        /// <summary>Hex, e.g. "#FF69B4". Falls back to the program accent when blank.</summary>
        public string AccentColor { get; set; } = "";
        public string? RewardDescription { get; set; }
        public List<ProgramEnrollDay> Days { get; set; } = new();
    }

    /// <summary>One day. See <see cref="ProgramEnrollInfo"/>.</summary>
    public class ProgramEnrollDay
    {
        public int DayIndex { get; set; }
        public int SessionMinutes { get; set; } = 30;
    }

    /// <summary>Program-wide rules the ceremony shows. See <see cref="ProgramEnrollInfo"/>.</summary>
    public class ProgramEnrollRules
    {
        public int DaysOffAllowed { get; set; } = 1;
        public bool StrictAvailable { get; set; } = true;
        public int DefaultDayBoundaryHour { get; set; } = 4;
    }

    /// <summary>
    /// One row of the arc list. Copied from ConditioningControlPanel/Views/Tabs/ProgramsTabItems.cs:
    /// the type lives in the WPF head, not in Core. RewardVisibility becomes a bool, because
    /// Avalonia binds IsVisible to a bool directly.
    /// </summary>
    public class ProgramChapterItem
    {
        public string Name { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string DaysLabel { get; set; } = "";
        public string RewardText { get; set; } = "";
        public bool RewardVisible { get; set; }
        public IBrush? AccentBrush { get; set; }
    }
}
