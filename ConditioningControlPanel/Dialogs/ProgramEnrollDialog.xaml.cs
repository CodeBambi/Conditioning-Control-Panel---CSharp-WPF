using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Views.Tabs;

namespace ConditioningControlPanel;

/// <summary>
/// The enrollment ceremony. Four steps on one scrollable panel: the whole arc up front,
/// Standard vs Strict, the share level (Private pre-selected but requiring a deliberate tap),
/// and the clock plus a typed contract phrase.
///
/// The dialog decides nothing - it collects. MainWindow.ProgramsTab.cs calls
/// ProgramService.Enroll with what comes back.
/// </summary>
public partial class ProgramEnrollDialog : Window
{
    private readonly ProgramDefinition _program;

    /// <summary>Set only by a real click on a share card, never by the pre-selection.</summary>
    private bool _shareTouched;

    public bool StrictMode { get; private set; }
    public ProgramShareLevel ShareLevel { get; private set; } = ProgramShareLevel.Private;
    public int DayBoundaryHour { get; private set; } = 4;
    public int NudgeHour { get; private set; } = 20;

    public ProgramEnrollDialog(ProgramDefinition program)
    {
        InitializeComponent();

        _program = program ?? throw new ArgumentNullException(nameof(program));

        var accent = ParseBrush(_program.AccentColor);

        TxtProgramIcon.Text = _program.Icon;
        TxtProgramTitle.Text = _program.Title;
        TxtProgramSubtitle.Text = _program.Subtitle;
        TxtProgramPitch.Text = _program.Pitch;

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
                RewardVisibility = hasReward ? Visibility.Visible : Visibility.Collapsed,
                AccentBrush = string.IsNullOrWhiteSpace(chapter.AccentColor)
                    ? accent
                    : ParseBrush(chapter.AccentColor)
            });
        }
        ChapterList.ItemsSource = chapters;

        var allDays = _program.AllDays.ToList();
        var minMinutes = allDays.Count > 0 ? allDays.Min(d => d.SessionMinutes) : 0;
        var maxMinutes = allDays.Count > 0 ? allDays.Max(d => d.SessionMinutes) : 0;
        TxtCommitment.Text = minMinutes == maxMinutes
            ? Loc.GetF("program_enroll_commitment_flat", _program.LengthDays, minMinutes)
            : Loc.GetF("program_enroll_commitment", _program.LengthDays, minMinutes, maxMinutes);

        // (b) Standard vs Strict.
        TxtModeStandardBody.Text = Loc.GetF("program_enroll_mode_standard_body", _program.Rules.DaysOffAllowed);
        if (!_program.Rules.StrictAvailable)
        {
            RadioModeStrict.IsEnabled = false;
            TxtStrictUnavailable.Visibility = Visibility.Visible;
        }

        // (d) The clock.
        for (int h = 0; h < 24; h++)
            CmbBoundaryHour.Items.Add(Loc.GetF("program_enroll_hour_format", h));
        CmbBoundaryHour.SelectedIndex = Math.Clamp(_program.Rules.DefaultDayBoundaryHour, 0, 23);

        CmbNudgeHour.Items.Add(Loc.Get("program_enroll_nudge_off"));
        for (int h = 0; h < 24; h++)
            CmbNudgeHour.Items.Add(Loc.GetF("program_enroll_hour_format", h));
        CmbNudgeHour.SelectedIndex = 21; // 20:00, one past the "Off" row

        TxtSafetyNote.Text = _program.SafetyNote;
        TxtContractPhrase.Text = _program.ContractPhrase;

        UpdateConfirmState();
    }

    private static Brush ParseBrush(string hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) &&
                ColorConverter.ConvertFromString(hex) is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
        catch { /* a bad accent must never block enrollment */ }

        return Application.Current?.TryFindResource("PinkBrush") as Brush ?? Brushes.HotPink;
    }

    /// <summary>Only a real tap counts - the pre-selection deliberately does not.</summary>
    private void ShareCard_Click(object sender, RoutedEventArgs e)
    {
        _shareTouched = true;
        UpdateConfirmState();
    }

    private void ContractInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => UpdateConfirmState();

    private static string Normalize(string? value) =>
        Regex.Replace((value ?? "").Trim(), @"\s+", " ");

    private bool ContractMatches() =>
        string.IsNullOrWhiteSpace(_program.ContractPhrase) ||
        string.Equals(Normalize(TxtContractInput.Text), Normalize(_program.ContractPhrase),
            StringComparison.OrdinalIgnoreCase);

    private void UpdateConfirmState()
    {
        var contractOk = ContractMatches();
        BtnConfirmEnroll.IsEnabled = _shareTouched && contractOk;

        TxtBlockedHint.Text = !_shareTouched
            ? Loc.Get("program_enroll_share_untouched")
            : (contractOk ? "" : Loc.Get("program_enroll_contract_hint"));
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        StrictMode = RadioModeStrict.IsChecked == true && _program.Rules.StrictAvailable;

        ShareLevel = RadioSharePublic.IsChecked == true
            ? ProgramShareLevel.Public
            : RadioShareLedger.IsChecked == true
                ? ProgramShareLevel.Ledger
                : ProgramShareLevel.Private;

        DayBoundaryHour = Math.Clamp(CmbBoundaryHour.SelectedIndex, 0, 23);
        NudgeHour = CmbNudgeHour.SelectedIndex <= 0 ? -1 : CmbNudgeHour.SelectedIndex - 1;

        DialogResult = true;
        Close();
    }
}
