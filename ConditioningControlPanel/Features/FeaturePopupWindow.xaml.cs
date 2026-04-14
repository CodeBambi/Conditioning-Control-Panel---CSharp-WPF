using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Generic modeless popup window that hosts a feature UserControl.
    /// Borderless, pink-themed titlebar, drag-to-move, Escape-to-close, centered on owner.
    /// </summary>
    public partial class FeaturePopupWindow : Window
    {
        public FeaturePopupWindow(UserControl content, string title, ImageSource? icon = null, string? glyph = null)
        {
            InitializeComponent();

            TxtTitle.Text = title;
            Title = title; // also set Window.Title for accessibility

            if (icon != null)
            {
                ImgIcon.Source = icon;
                ImgIcon.Visibility = Visibility.Visible;
                TxtGlyph.Visibility = Visibility.Collapsed;
            }
            else if (!string.IsNullOrEmpty(glyph))
            {
                TxtGlyph.Text = glyph;
                TxtGlyph.Visibility = Visibility.Visible;
                ImgIcon.Visibility = Visibility.Collapsed;
            }
            else
            {
                ImgIcon.Visibility = Visibility.Collapsed;
                TxtGlyph.Visibility = Visibility.Collapsed;
            }

            ContentHost.Content = content;

            // Escape closes the popup.
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void Titlebar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { /* dragging can throw if not pressed */ }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
