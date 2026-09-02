using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Windows/EasterEggWindow.xaml.cs. Deviations:
    ///  - <c>Visibility.Visible</c> becomes <c>IsVisible = true</c>.
    ///  - <c>Click="BtnClose_Click"</c> is wired here for both buttons.
    ///  - The <c>readerCount = -1</c> default is gone and a separate <c>internal</c> render
    ///    constructor takes its place: an optional parameter does not satisfy
    ///    <c>Type.EmptyTypes</c>, so --render-all could not construct the window, and keeping both
    ///    would let <c>new EasterEggWindow()</c> silently pick the sample count. Every caller in
    ///    the WPF head passes a count (MainWindow.UiUpdates.cs:1966), so nothing is lost.
    ///  - The reader-count string is an English literal in the WPF original - there is no loc key
    ///    for it - so it is copied verbatim rather than invented.
    /// </summary>
    public partial class EasterEggWindow : Window
    {
        /// <summary>Render/design constructor: a sample count so --render-view draws the
        /// otherwise-hidden reader line too.</summary>
        internal EasterEggWindow() : this(1337) { }

        public EasterEggWindow(int readerCount)
        {
            AvaloniaXamlLoader.Load(this);

            if (readerCount > 0)
            {
                var txt = this.FindControl<TextBlock>("TxtReaderCount")!;
                txt.Text = $"This rant has been read {readerCount} times";
                txt.IsVisible = true;
            }

            this.FindControl<Button>("BtnCloseTop")!.Click += (_, _) => Close();
            this.FindControl<Button>("BtnCloseFooter")!.Click += (_, _) => Close();
        }
    }
}
