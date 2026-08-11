using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Generic modeless popup window that hosts a feature UserControl.
    /// Borderless, pink-themed titlebar, drag-to-move, Escape-to-close, centered on owner.
    ///
    /// <para><b>PHASE 4 (UX restructure) — this window SURVIVES, as the pop-out.</b> The Studio
    /// rack (Views/Tabs/StudioTabView.xaml) is now the primary home of the per-feature config
    /// panels, but the dashboard mosaic still opens these popups and they still work, because the
    /// two hosts never share an instance: <c>MainWindow.ShowFeaturePopup</c> constructs a FRESH
    /// <c>Features/*FeatureControl</c> on every single open (it always did — see the thirteen
    /// <c>new X()</c> call sites in MainWindow.Presets.cs), while the rack builds its own set once
    /// and keeps them for the app's lifetime.</para>
    ///
    /// <para><b>THE ONE RULE: never hand this window a control that is already hosted somewhere
    /// else.</b> A WPF UserControl has exactly one parent, so <c>ContentHost.Content = rackPanel</c>
    /// would either throw ("already the logical child of another element") or silently rip the
    /// panel out of the Studio tab, firing Unloaded/Loaded — which re-runs each panel's
    /// LoadFromSettings and re-subscribes its handlers. That borrow-and-return mechanism is
    /// exactly the <c>DetachWebcamBarInto</c> trick Phase 2 deleted; do not reintroduce it here.
    /// Two independent instances stay in step for free, because every feature panel re-reads
    /// <c>App.Settings</c> on PropertyChanged. The constructor enforces the rule rather than
    /// trusting it (see below).</para>
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

            // Single-parent guard (Phase 4). Hosting an already-parented control is a
            // programming error, not a user-reachable state - but the failure mode without this
            // is an unhandled WPF exception out of a plain card click, i.e. a crash dialog, and
            // its message ("Specified element is already the logical child of another element")
            // says nothing about the Studio rack. Refuse instead: the popup opens empty and the
            // log names the exact type, which is a bug report rather than a lost session.
            if (LogicalTreeHelper.GetParent(content) != null || VisualTreeHelper.GetParent(content) != null)
            {
                App.Logger?.Error(
                    "[FeaturePopup] Refused to host '{Type}' - it is already parented (the Studio " +
                    "rack owns a permanent instance of every feature panel). ShowFeaturePopup must " +
                    "construct a NEW control for the popup; never re-parent the rack's.",
                    content.GetType().Name);
            }
            else
            {
                ContentHost.Content = content;
            }

            // Escape closes the popup.
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                // Don't eat Esc while the panic-key picker is waiting for a key —
                // otherwise the user can never assign Escape (or anything else, if
                // they instinctively hit Esc to abort): the popup closes before the
                // global hook records the capture.
                var owner = Application.Current?.MainWindow as MainWindow;
                if (owner?.IsCapturingPanicKey == true) return;
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
