using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// Z8 · ROSTER. See the XAML header.
    ///
    /// <para>Like every re-parented cell, this control does no work of its own: it forwards to the
    /// MainWindow handler the old tab forwarded to, so the roster's behaviour is byte-for-byte what
    /// it was before the move.</para>
    /// </summary>
    public partial class WorkshopRosterCell : UserControl
    {
        public WorkshopRosterCell()
        {
            InitializeComponent();
        }

        private void CompanionCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.CompanionCard_Click(sender, e);
        }

        private void BtnCompanionPersonality_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnCompanionPersonality_Click(sender, e);
        }
    }
}
