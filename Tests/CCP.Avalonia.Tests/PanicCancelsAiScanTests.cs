using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Every panic route on both heads cancels pending AI follow-ups (getbacktome delays and their
/// nested effects): a source scan, because most of these routes need a live desktop to drive.</summary>
public sealed class PanicCancelsAiScanTests
{
    [Theory]
    [InlineData("CCP.Avalonia/Views/Windows/MainShellWindow.PanicKey.cs", "internal void HandlePanicKeyPress(DateTime now)", "CancelPendingAi();")]
    [InlineData("CCP.Avalonia/Views/Windows/PanicSurfaces.cs", "internal static IReadOnlyList<Surface> All", "MainShellWindow.CancelPendingAi()")]   // tray + voice (PanicSurfacesTests)
    [InlineData("CCP.Core/Services/RemoteControl/RemoteCommands.cs", "public static void StopEffects(bool force)", "if (force) Commands.AiCommandService.CancelAll();")]
    [InlineData("ConditioningControlPanel/MainWindow/MainWindow.xaml.cs", "private void HandlePanicKeyPress()", "CancelPendingAi();")]
    [InlineData("ConditioningControlPanel/MainWindow/MainWindow.xaml.cs", "private void RunEmergencyPanicTeardown()", "CancelPendingAi();")]
    [InlineData("ConditioningControlPanel/MainWindow/MainWindow.RemoteControl.cs", "internal void TriggerPanicFromRemote()", "CancelPendingAi();")]
    [InlineData("ConditioningControlPanel/Services/RemoteControlService.cs", "private void StopAllRemoteEffects(bool force)", "if (force) MainWindow.CancelPendingAi();")]
    public void PanicRouteCancelsPendingAi(string file, string signature, string call)
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), file));
        var start = src.IndexOf(signature, System.StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found in {file}");
        var end = src.IndexOf("\n        }\n", start, System.StringComparison.Ordinal);
        Assert.Contains(call, src.Substring(start, end - start));
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
