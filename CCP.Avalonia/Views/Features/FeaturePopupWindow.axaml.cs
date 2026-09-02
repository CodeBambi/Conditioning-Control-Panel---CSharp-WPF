using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Generic modeless popup window that hosts a feature control. Borderless, pink-themed
    /// titlebar, drag-to-move, Escape-to-close, centered on owner. Ported from the WPF head;
    /// see the original for the Phase 4 single-parent rule, which the constructor still enforces.
    /// </summary>
    public partial class FeaturePopupWindow : Window
    {
        /// <summary>Render/design constructor: sample content so --render-view can draw the window.</summary>
        public FeaturePopupWindow() : this(new AppInfoFeatureControl(), "App Info", null, "ℹ") { }

        public FeaturePopupWindow(Control content, string title, IImage? icon = null, string? glyph = null)
        {
            AvaloniaXamlLoader.Load(this);

            var txtTitle = this.FindControl<TextBlock>("TxtTitle")!;
            var imgIcon = this.FindControl<Image>("ImgIcon")!;
            var txtGlyph = this.FindControl<TextBlock>("TxtGlyph")!;
            var contentHost = this.FindControl<ContentControl>("ContentHost")!;

            txtTitle.Text = title;
            Title = title; // also set Window.Title for accessibility

            if (icon != null)
            {
                imgIcon.Source = icon;
                imgIcon.IsVisible = true;
                txtGlyph.IsVisible = false;
            }
            else if (!string.IsNullOrEmpty(glyph))
            {
                txtGlyph.Text = glyph;
                txtGlyph.IsVisible = true;
                imgIcon.IsVisible = false;
            }
            else
            {
                imgIcon.IsVisible = false;
                txtGlyph.IsVisible = false;
            }

            // Single-parent guard (Phase 4): refuse an already-parented control instead of
            // throwing out of a plain card click. The popup opens empty and the log names the type.
            if (content.Parent != null || content.GetVisualParent() != null)
            {
                Log.Error(
                    "[FeaturePopup] Refused to host '{Type}' - it is already parented (the Studio " +
                    "rack owns a permanent instance of every feature panel). ShowFeaturePopup must " +
                    "construct a NEW control for the popup; never re-parent the rack's.",
                    content.GetType().Name);
            }
            else
            {
                contentHost.Content = content;
            }

            this.FindControl<Border>("Titlebar")!.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    try { BeginMoveDrag(e); } catch { /* dragging can throw if not pressed */ }
                }
            };
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();

            // Escape closes the popup.
            // ponytail: needs MainWindow.IsCapturingPanicKey (don't eat Esc while the panic-key
            // picker waits for a key), wired when that host exists on this head
            KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                Close();
                e.Handled = true;
            };
        }
    }
}
