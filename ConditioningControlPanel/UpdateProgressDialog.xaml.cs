using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel
{
    public partial class UpdateProgressDialog : Window
    {
        public UpdateProgressDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Update the progress display (0-100)
        /// </summary>
        public void SetProgress(int progress)
        {
            progress = System.Math.Clamp(progress, 0, 100);
            TxtProgress.Text = $"{progress}%";

            // Get the parent Grid, then its parent Border for width calculation
            if (ProgressFill.Parent is Grid grid && grid.Parent is Border border)
            {
                double maxWidth = border.ActualWidth - 6;
                if (maxWidth > 0)
                {
                    ProgressFill.Width = (maxWidth * progress) / 100.0;
                }
            }
        }
    }
}
