using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CcpClient.Desktop;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Views;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The two Studio pickers a user could not reach before: WHICH SPIRAL is drawn, and WHAT COLOUR the
/// tint is.
///
/// <para>Both backends were already landed and neither had a control in front of it — the Spiral
/// panel rendered a read-only library line and the Pink Filter panel rendered a read-only swatch,
/// and the page said so in its own comments. What these facts are about is the gesture: a real
/// selection on a real control reaching the persisted document the module reads.</para>
///
/// <para><b>No file dialog is involved and none is depended on.</b> The spiral picker offers what
/// the library folder already holds and nothing else, which is why these tests seed that folder
/// before the app boots rather than driving a chooser.</para>
///
/// <para>Draw-level ONLY (verification-harness.md evidence class): visual tree, control state, real
/// input routing. No composited pixel is claimed here.</para>
/// </summary>
public class StudioPickerHeadlessTests
{
    private sealed record Boot(ApplicationHost Host, MainWindow Window, string SpiralsFolder, ManualScriptedClock Clock)
    {
        public StudioPage Studio => (StudioPage)Window.PageFor(ShellRoutes.Studio);
    }

    private static async Task<Boot> BootAsync(params string[] spiralFiles)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-studio-picker-" + Guid.NewGuid().ToString("N"));
        var spirals = SpiralLibrary.Folder(Path.Combine(dir, "assets"));
        Directory.CreateDirectory(spirals);
        foreach (var name in spiralFiles)
        {
            File.WriteAllBytes(Path.Combine(spirals, name), [0x00]);
        }

        var clock = new ManualScriptedClock();
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            ScriptedClockFactory = () => clock,
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);

        var window = new MainWindow(host!);
        host!.BindUiDispatch(new AvaloniaUiDispatch());
        window.Show();
        window.UpdateLayout();
        return new Boot(host, window, spirals, clock);
    }

    private static T Descendant<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}' in the mounted page");

    private static void Click(Window window, Control control)
    {
        control.BringIntoView();
        window.UpdateLayout();
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        window.UpdateLayout();
    }

    private static void OpenRow(MainWindow window, string row)
    {
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, row));
    }

    // =====================================================================================
    //  the spiral library picker
    // =====================================================================================

    /// <summary>
    /// The picker offers the whole library plus the "let the library choose" entry, and choosing an
    /// entry is what writes the module's path. Before this landed the document could only be
    /// changed by hand-editing the file on disk.
    /// </summary>
    [AvaloniaFact]
    public async Task PickingASpiralWritesThePathTheModuleResolves_AndThePanelNamesIt()
    {
        var boot = await BootAsync("Apple.png", "banana.jpg", "zebra.gif");
        var window = boot.Window;
        OpenRow(window, "RowSpiralOverlay");

        var picker = Descendant<ComboBox>(window, "SpiralPicker");
        Assert.Equal(
            [
                "Library default (the first file in the folder)",
                "Apple", "banana", "zebra",
            ],
            picker.ItemsSource!.Cast<string>());

        // Nothing configured: the first entry, and the module draws the library's own first file.
        Assert.Equal(0, picker.SelectedIndex);
        Assert.Equal(string.Empty, window.Session.SpiralPreset.Current.Path);
        Assert.Equal(
            "Drawing Apple.png.",
            Descendant<TextBlock>(window, "SpiralLibraryState").Text);

        picker.SelectedIndex = 3;
        window.UpdateLayout();

        Assert.Equal(
            Path.Combine(boot.SpiralsFolder, "zebra.gif"),
            window.Session.SpiralPreset.Current.Path);
        Assert.Equal(
            "Drawing zebra.gif.",
            Descendant<TextBlock>(window, "SpiralLibraryState").Text);

        // And back to the library's choice, which is the empty string upstream stores for it.
        picker.SelectedIndex = 0;
        window.UpdateLayout();
        Assert.Equal(string.Empty, window.Session.SpiralPreset.Current.Path);
        Assert.Equal("Drawing Apple.png.", Descendant<TextBlock>(window, "SpiralLibraryState").Text);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// Refresh is upstream's own button (<c>Features/SpiralFeatureControl.xaml:176-184</c>) and it
    /// is owed for upstream's own reason: the folder belongs to the user and changes while the app
    /// is running. Without the press the new file is not in the list, which is what stops this fact
    /// passing on a picker that re-enumerated on every repaint anyway.
    /// </summary>
    [AvaloniaFact]
    public async Task RefreshPicksUpAFileDroppedIntoTheFolderAfterTheAppStarted()
    {
        var boot = await BootAsync("Apple.png");
        var window = boot.Window;
        OpenRow(window, "RowSpiralOverlay");

        var picker = Descendant<ComboBox>(window, "SpiralPicker");
        Assert.Equal(2, picker.ItemsSource!.Cast<string>().Count());

        File.WriteAllBytes(Path.Combine(boot.SpiralsFolder, "dropped-in.gif"), [0x00]);
        Assert.DoesNotContain("dropped-in", picker.ItemsSource!.Cast<string>());

        Click(window, Descendant<Button>(window, "SpiralRefreshButton"));

        Assert.Equal(["Library default (the first file in the folder)", "Apple", "dropped-in"],
            picker.ItemsSource!.Cast<string>());

        // The selection is re-derived from the document, so the refresh moved no dial.
        Assert.Equal(0, picker.SelectedIndex);
        Assert.Equal(string.Empty, window.Session.SpiralPreset.Current.Path);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// An empty library is the ordinary first-run state (this port bundles no spiral art at all —
    /// D86), and the picker says so with one entry rather than pretending to offer a built-in.
    /// </summary>
    [AvaloniaFact]
    public async Task AnEmptyLibraryLeavesThePickerWithNothingButItsDefaultEntry()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenRow(window, "RowSpiralOverlay");

        var picker = Descendant<ComboBox>(window, "SpiralPicker");
        Assert.Equal(["Library default (the first file in the folder)"], picker.ItemsSource!.Cast<string>());
        Assert.Equal(0, picker.SelectedIndex);

        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  the tint picker
    // =====================================================================================

    /// <summary>
    /// Picking a tint writes the hex the module parses, repaints the swatch that was previously
    /// read-only, and the words stop calling the colour a default. Reset writes upstream's own
    /// empty string and the fallback comes back.
    /// </summary>
    [AvaloniaFact]
    public async Task PickingATintWritesTheColourAndRepaintsTheSwatch_AndResetPutsTheDefaultBack()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenRow(window, "RowPinkFilter");

        var picker = Descendant<ComboBox>(window, "PinkFilterTintPicker");
        var swatch = Descendant<Rectangle>(window, "PinkFilterSwatch");
        var words = Descendant<TextBlock>(window, "PinkFilterTintState");

        Assert.Equal(0, picker.SelectedIndex);
        Assert.Equal(string.Empty, window.Session.PinkFilterPreset.Current.Colour);
        Assert.Equal(Color.FromRgb(0xFF, 0x69, 0xB4), ((SolidColorBrush)swatch.Fill!).Color);
        Assert.Contains("(the default)", words.Text, StringComparison.Ordinal);

        // "Cyan" — a colour nobody would mistake for the hot-pink fallback.
        picker.SelectedIndex = 5;
        window.UpdateLayout();

        Assert.Equal("#31E1F7", window.Session.PinkFilterPreset.Current.Colour);
        Assert.Equal(Color.FromRgb(0x31, 0xE1, 0xF7), ((SolidColorBrush)swatch.Fill!).Color);
        Assert.DoesNotContain("(the default)", words.Text, StringComparison.Ordinal);
        Assert.Contains("#31E1F7", words.Text, StringComparison.Ordinal);

        Click(window, Descendant<Button>(window, "PinkFilterTintResetButton"));

        Assert.Equal(string.Empty, window.Session.PinkFilterPreset.Current.Colour);
        Assert.Equal(0, picker.SelectedIndex);
        Assert.Equal(Color.FromRgb(0xFF, 0x69, 0xB4), ((SolidColorBrush)swatch.Fill!).Color);
        Assert.Contains("(the default)", words.Text, StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// NEITHER PICKER IS SESSION-LOCKED, and that is upstream's classification rather than an
    /// oversight: the spiral library card and the colour buttons are all unmarked upstream
    /// (<c>Features/SpiralFeatureControl.xaml:151-199</c>,
    /// <c>Features/PinkFilterFeatureControl.xaml:102-105</c>), and this port's scripted session
    /// writes neither <c>SpiralPath</c> nor <c>Colour</c>. A session prescribes how MUCH, never
    /// which picture or which colour.
    /// </summary>
    [AvaloniaFact]
    public async Task ThePickersStayLiveDuringASession_EvenThoughTheOpacityDialsBesideThemDoNot()
    {
        var boot = await BootAsync("Apple.png", "zebra.gif");
        var window = boot.Window;

        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowScriptedSession"));
        Click(window, Descendant<RadioButton>(window, "SessionRowMorningDrift"));
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));
        Assert.True(window.Session.Scripted.Running);

        // The dials the session DOES own, on the same two panels, for contrast.
        Assert.False(Descendant<Slider>(window, "SpiralOpacitySlider").IsEnabled);
        Assert.False(Descendant<Slider>(window, "PinkFilterOpacitySlider").IsEnabled);

        var spiralPicker = Descendant<ComboBox>(window, "SpiralPicker");
        var tintPicker = Descendant<ComboBox>(window, "PinkFilterTintPicker");
        Assert.True(spiralPicker.IsEnabled);
        Assert.True(tintPicker.IsEnabled);
        Assert.True(Descendant<Button>(window, "SpiralRefreshButton").IsEnabled);
        Assert.True(Descendant<Button>(window, "PinkFilterTintResetButton").IsEnabled);

        // And they still WORK, which is the half a disabled-state assertion cannot show.
        spiralPicker.SelectedIndex = 2;
        tintPicker.SelectedIndex = 3;
        window.UpdateLayout();

        Assert.Equal(
            Path.Combine(boot.SpiralsFolder, "zebra.gif"),
            window.Session.SpiralPreset.Current.Path);
        Assert.Equal("#C77DFF", window.Session.PinkFilterPreset.Current.Colour);

        await boot.Host.ShutdownAsync();
    }
}
