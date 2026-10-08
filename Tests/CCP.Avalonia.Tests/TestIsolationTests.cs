using System.IO;
using System.Linq;
using ConditioningControlPanel;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Proves the assembly-wide <see cref="IsolateProcessStateAttribute"/> hook covers the
/// seams that leaked before (PLAYBOOK P02) and really puts each one back.</summary>
public class TestIsolationTests
{
    /// <summary>The first test in a testhost decides the Avalonia platform for every later one. A setup
    /// with headless drawing (no Skia) made later text measure differently and bitmaps read garbage, so
    /// ChasterRaffleCardLayout, BlinkTrainerSession and EmiBookDemo failed only after it (flake-layout).</summary>
    [Fact]
    public void EveryPlatformSetupUsesSkiaDrawing()
    {
        var bad = Directory.EnumerateFiles(Here(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f =>
            {
                var code = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(f), @"//[^\n]*|/\*.*?\*/", "",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                return System.Text.RegularExpressions.Regex.Matches(code, @"\.UseHeadless\(")
                    .Where(m => !code.Substring(m.Index, System.Math.Min(160, code.Length - m.Index)).Contains("UseHeadlessDrawing = false")
                             || !code.Substring(System.Math.Max(0, m.Index - 160), System.Math.Min(160, m.Index)).Contains("UseSkia()"))
                    .Select(_ => Path.GetFileName(f));
            }).ToList();
        Assert.Empty(bad);
    }

    private static string Here([System.Runtime.CompilerServices.CallerFilePath] string p = "") => Path.GetDirectoryName(p)!;

    [Fact]
    public void EveryTestRunsInsideTheHook() => Assert.NotNull(IsolateProcessStateAttribute.Current);

    [Theory]
    [InlineData(typeof(CoreSettings), nameof(CoreSettings.ServiceProvider))]
    [InlineData(typeof(CoreSecrets), nameof(CoreSecrets.RetrieveProvider))]
    [InlineData(typeof(CoreAccount), nameof(CoreAccount.UnifiedUserId))]
    [InlineData(typeof(CoreMods), nameof(CoreMods.MakeModAwareProvider))]
    [InlineData(typeof(AvApp), nameof(AvApp.Ai))]
    [InlineData(typeof(AvApp), nameof(AvApp.Brain))]
    [InlineData(typeof(ConditioningControlPanel.Services.LockdownService), nameof(ConditioningControlPanel.Services.LockdownService.Current))]
    [InlineData(typeof(ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay), "PanicListenerLive")]
    public void TheHookCoversEverySeamThatLeakedBefore(System.Type type, string member) =>
        Assert.Contains(ProcessStateSnapshot.Members.Value, m => m.DeclaringType == type && m.Name == member);

    [Fact]
    public void RestorePutsStaticsSettingsAndProfileFilesBack()
    {
        var settingsFile = Path.Combine(TestUserDataProfile.Root, "settings.json");
        var stray = Path.Combine(TestUserDataProfile.Root, "isolation-probe.json");
        var hadFile = File.Exists(settingsFile);
        var before = hadFile ? File.ReadAllText(settingsFile) : null;
        var user = CoreAccount.UnifiedUserId;
        var provider = CoreSettings.ServiceProvider;
        var chat = CoreSettings.Current.AiChatEnabled;

        var snapshot = ProcessStateSnapshot.Take();
        CoreAccount.UnifiedUserId = "leaked-user";
        CoreSettings.Current.AiChatEnabled = !chat;
        CoreSettings.ServiceProvider = () => null;
        File.WriteAllText(settingsFile, "{\"leaked\":true}");
        File.WriteAllText(stray, "{}");
        snapshot.Restore();

        Assert.Equal(user, CoreAccount.UnifiedUserId);
        Assert.Same(provider, CoreSettings.ServiceProvider);
        Assert.Equal(chat, CoreSettings.Current.AiChatEnabled);
        Assert.Equal(hadFile, File.Exists(settingsFile));
        if (hadFile) Assert.Equal(before, File.ReadAllText(settingsFile));
        Assert.False(File.Exists(stray));
    }
}
