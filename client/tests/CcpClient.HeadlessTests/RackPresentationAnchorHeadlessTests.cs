using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The join between the rack's real brushes and the headed manifest that reads them back
/// off composited pixels (<c>client/tools/verify/checks.json</c>).
///
/// <para><b>The gap this closes.</b> The manifest names four colours; the product names them in
/// <c>MainWindow.axaml</c>. Nothing connected the two. Change a brush and the headed checks do not
/// go red — they go SILENT, because nobody runs a headed capture on every commit, and the next
/// person to run one gets a failure that looks like a regression in the harness. These facts make
/// that a floor failure that names the manifest entry to update.</para>
///
/// <para><b>Evidence class, said plainly.</b> Draw-verified and nothing more. A headless frame
/// resolves styles; it does not composite, so no fact here claims any of these colours reached a
/// screen. Headed captures and interaction checks establish presentation; a headless frame never
/// discharges them.</para>
/// </summary>
public class RackPresentationAnchorHeadlessTests : HeadlessTest
{
    private async Task<MainWindow> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp122-headless-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot { SettingsPathFactory = () => Path.Combine(dir, "settings.json") };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        Track(host!);

        var window = new MainWindow(host!);
        window.Show();
        window.UpdateLayout();
        return window;
    }

    /// <summary>
    /// The rack row the headed harness captures, in the state the headed harness captures it: a
    /// real click, exactly as <c>capture.ps1</c>'s left-click drive does.
    /// </summary>
    private static RadioButton OpenFlashRow(MainWindow window)
    {
        var row = Row(window, "RowFlashImages");
        row.BringIntoView();
        window.UpdateLayout();
        var centre = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("the Flash Images row is not in the window's visual tree");
        window.MouseDown(centre, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(centre, MouseButton.Left, RawInputModifiers.None);
        window.UpdateLayout();
        Assert.True(row.IsChecked);
        return row;
    }

    private static RadioButton Row(MainWindow window, string name) =>
        window.GetVisualDescendants().OfType<RadioButton>().FirstOrDefault(r => r.Name == name)
        ?? throw new InvalidOperationException($"no rack row '{name}'");

    /// <summary>The colour a named manifest check expects, read out of the real manifest file.</summary>
    private static Color Expected(string checkName)
    {
        var path = Path.Combine(RepoRoot(), "client", "tools", "verify", "checks.json");
        using var document = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        foreach (var check in document.RootElement.GetProperty("checks").EnumerateArray())
        {
            if (check.GetProperty("name").GetString() == checkName)
            {
                return Color.Parse(check.GetProperty("expectedColor").GetString()!);
            }
        }

        throw new Xunit.Sdk.XunitException($"checks.json has no check named '{checkName}'");
    }

    private static void AssertSameRgb(Color expected, IBrush? actual, string checkName, string where)
    {
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(actual);
        var colour = brush.Color;
        Assert.True(
            expected.R == colour.R && expected.G == colour.G && expected.B == colour.B,
            $"{where} resolves to #{colour.R:X2}{colour.G:X2}{colour.B:X2}, but checks.json's '{checkName}' "
            + $"reads pixels back expecting #{expected.R:X2}{expected.G:X2}{expected.B:X2}. The headed check "
            + "will now fail on a working build; update the manifest entry in the same change as the brush");
    }

    [AvaloniaFact]
    public async Task TheOpenRowsMarkerAndFillAreTheColoursTheHeadedChecksReadBack()
    {
        var window = await BootAsync();
        var row = OpenFlashRow(window);

        AssertSameRgb(Expected("rack-row-selected-marker"), row.BorderBrush,
            "rack-row-selected-marker", "RadioButton.rack-row:checked BorderBrush");
        AssertSameRgb(Expected("rack-row-selected-fill"), row.Background,
            "rack-row-selected-fill", "RadioButton.rack-row:checked Background");

        // The marker is a LEFT band, and the manifest's border-color-band check reads the left
        // edge. A thickness that moved to another edge would leave the headed check reading a
        // strip of fill and failing on a working build.
        Assert.Equal(new Thickness(3, 0, 0, 0), row.BorderThickness);
    }

    [AvaloniaFact]
    public async Task TheClosedRowShowsTheRackGroundTheHeadedCheckReadsBack()
    {
        var window = await BootAsync();

        // A closed row paints nothing of its own — Background and BorderBrush are Transparent
        // (MainWindow.axaml:75-77) — so what the camera sees at a closed row is the RACK's ground.
        // That is the surface rack-row-unselected-ground names, so it is the one to anchor.
        var closed = Row(window, "RowSubliminals");
        Assert.False(closed.IsChecked);

        var rack = closed.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("rack"));
        AssertSameRgb(Expected("rack-row-unselected-ground"), rack.Background,
            "rack-row-unselected-ground", "Border.rack Background");
    }

    [AvaloniaFact]
    public async Task TheArmedDotIsTheColourTheHeadedCheckReadsBack()
    {
        var window = await BootAsync();
        var row = OpenFlashRow(window);

        // Armed on a cold start, and that is the product's own default rather than a test setup:
        // SessionPresetDocument.FlashEnabled is true (:64, ported from WPF's AppSettings.
        // FlashEnabled), so the module is enabled and the session is not running.
        var dot = row.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single(e => e.Classes.Contains("dot"));
        Assert.Contains("armed", dot.Classes);
        Assert.DoesNotContain("live", dot.Classes);

        AssertSameRgb(Expected("rack-row-dot-armed"), dot.Fill,
            "rack-row-dot-armed", "Ellipse.dot.armed Fill");

        // And the OFF state's check reads the fill BEHIND a hollow ring, so its anchor is the open
        // row's own background — the same colour, named by a different check for a different
        // reason. Naming both stops a future edit from moving one and leaving the other.
        AssertSameRgb(Expected("rack-row-dot-off"), row.Background,
            "rack-row-dot-off", "the open row's Background, seen through the unfilled dot");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "client", "CcpClient.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new Xunit.Sdk.XunitException($"repo root not found above {AppContext.BaseDirectory}");
    }
}
