using System.Windows;

namespace ConditioningControlPanel;

/// <summary>
/// Themed dialog for confirming photo submission
/// </summary>
public partial class RoadmapConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

    public RoadmapConfirmDialog(string stepTitle, string photoRequirement)
    {
        InitializeComponent();

        TxtStepTitle.Text = $"\"{stepTitle}\"";
        TxtRequirement.Text = photoRequirement;
    }

    private void BtnNo_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }

    private void BtnYes_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
        Close();
    }
}
