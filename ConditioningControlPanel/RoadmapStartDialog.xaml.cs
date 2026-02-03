using System.Windows;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel;

/// <summary>
/// Themed dialog for starting a roadmap step
/// </summary>
public partial class RoadmapStartDialog : Window
{
    public RoadmapStartDialog(RoadmapStepDefinition stepDef)
    {
        InitializeComponent();

        // Set icon based on step type
        TxtStepIcon.Text = stepDef.StepType == RoadmapStepType.Boss ? "\uD83C\uDFC6" : "\uD83D\uDCF7";

        TxtStepTitle.Text = stepDef.StepType == RoadmapStepType.Boss
            ? $"BOSS: {stepDef.Title}"
            : $"Step {stepDef.StepNumber}: {stepDef.Title}";

        TxtObjective.Text = stepDef.Objective;
        TxtPhotoRequirement.Text = stepDef.PhotoRequirement;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
