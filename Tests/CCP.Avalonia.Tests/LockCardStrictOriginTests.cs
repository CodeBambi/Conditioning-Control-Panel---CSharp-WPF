using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>SAFETY (hunt3 IC1): a leash "lines" punishment and a remote trigger_lock_card open a card
/// that is never strict, even when the player's own Lock Card setting is strict. Only the player's own
/// machine may open a strict card.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class LockCardStrictOriginTests
{
    private const string Phrase = "k27 never strict";

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AppA>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static Task WithStrictSetting(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var (oldStrict, oldVoice) = (s.LockCardStrict, s.LockCardVoiceMode);
        var phrases = s.LockCardPhrases;
        var hadPhrase = phrases.TryGetValue(Phrase, out var oldPhrase);
        s.LockCardStrict = true;
        s.LockCardVoiceMode = false;
        phrases[Phrase] = true;
        try
        {
            LockCardWindow.ForceCloseAll();
            body();
        }
        finally
        {
            LockCardWindow.ForceCloseAll();
            Dispatcher.UIThread.RunJobs();
            s.LockCardStrict = oldStrict;
            s.LockCardVoiceMode = oldVoice;
            if (hadPhrase) phrases[Phrase] = oldPhrase; else phrases.Remove(Phrase);
            CoreSettings.SaveImmediate();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ALeashLinesCardIsNeverStrict_WhateverTheSettingSays() => WithStrictSetting(() =>
    {
        var host = new LeashTaskHost();
        try
        {
            Assert.True(host.ShowLockCard());
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(LockCardWindow.Primary);
            Assert.False(LockCardWindow.Primary!.IsStrict);
        }
        finally { host.Dispose(); }
    });

    [Theory]
    [InlineData(LockCardOrigin.Leash)]
    [InlineData(LockCardOrigin.Remote)]
    public Task ACardFromAParticipantIsNeverStrict(LockCardOrigin origin) => WithStrictSetting(() =>
    {
        LockCardWindow.ShowNext(isTest: false, origin);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(LockCardWindow.Primary);
        Assert.False(LockCardWindow.Primary!.IsStrict);
    });

    [Fact]
    public Task ThePlayersOwnCardStillFollowsTheSetting() => WithStrictSetting(() =>
    {
        LockCardWindow.ShowNext(isTest: true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(LockCardWindow.Primary!.IsStrict);
    });

    /// <summary>The tripwire: any leash or remote source file that opens a lock card must name its
    /// origin (ShowNext with LockCardOrigin.Leash / Remote) and may never call ShowOnAllMonitors,
    /// which takes a raw strict flag.</summary>
    [Fact]
    public void NoLeashOrRemoteFileCanOpenAStrictCard()
    {
        var root = Path.Combine(RepoRoot(), "CCP.Avalonia");
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(p => Path.GetFileName(p).Contains("Leash", StringComparison.OrdinalIgnoreCase)
                     || Path.GetFileName(p).Contains("Remote", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.NotEmpty(files);
        var seen = 0;
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            Assert.False(Regex.IsMatch(text, @"LockCardWindow\s*\.\s*ShowOnAllMonitors\s*\("), $"{f}: raw strict flag on a leash / remote path");
            Assert.False(Regex.IsMatch(text, @"CoreLockCard\s*\.\s*Show\s*\("), $"{f}: the local card seam on a leash / remote path");
            foreach (Match m in Regex.Matches(text, @"LockCardWindow\s*\.\s*ShowNext\s*\(([^;]*);"))
            {
                seen++;
                Assert.True(Regex.IsMatch(m.Groups[1].Value, @"LockCardOrigin\s*\.\s*(Leash|Remote)\b"), $"{f}: {m.Value}");
            }
        }
        Assert.True(seen >= 2, "the leash host and the remote verb both open cards");
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
