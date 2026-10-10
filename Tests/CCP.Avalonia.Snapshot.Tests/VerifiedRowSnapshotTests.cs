using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using VerifyTests;
using static VerifyXunit.Verifier;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CCP.Avalonia.Snapshot.Tests;

/// <summary>
/// Locks the look of every parity row that is `verified`/`improved` in docs/avalonia-parity.md:
/// one baseline per row id under Snapshots/. A new baseline is accepted only with the WPF
/// side-by-side image path in the commit message (docs/avalonia-parity.md, "Locking a row").
/// ~/ccp-port/bin/ledger-snapshot-check.sh fails a verified/improved row without a file here.
/// </summary>
public sealed class VerifiedRowSnapshotTests
{
    /// <summary>Row id -> the window that row's WPF side-by-side compared against.</summary>
    private static readonly Dictionary<string, Func<Window>> Rows = new()
    {
        ["win-layered-audio"] = () => new LayeredAudioWindow(),
        ["release-content-mod-manager-rows"] = () => new ModManagerDialog(),
    };

    public static TheoryData<string> RowIds => new(Rows.Keys);

    // SSIM, not byte equality: Skia's PNG encoder and zlib may differ in bytes for identical
    // pixels across distros. 0.995 still fails on one changed margin, text change or colour swap
    // in a 480x660 window (fail-proof in the commit), and passes Arch vs ubuntu-latest AA noise.
    internal const double SsimThreshold = 0.995;

    [ModuleInitializer]
    internal static void Init()
    {
        // Disposable profile before CorePaths is first read; XDG is sandboxed by the linked dispatcher file.
        var profile = Path.Combine(Path.GetTempPath(), "ccp-snapshot-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", profile);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(profile, true); } catch { } };
        VerifyAvalonia.Initialize();
        VerifierSettings.UseSsimForPng(SsimThreshold);
    }

    [Theory]
    [MemberData(nameof(RowIds))]
    public Task VerifiedRowRenderIsLocked(string rowId) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        // The --render-all path: app builder, Inter font, Skia, real drawing, in-memory state only.
        RenderProof.Rendering = true;
        RenderProof.EnsureSetUp();
        CoreSettings.Current.MotionLevel = MotionLevel.Off;

        var window = Rows[rowId]();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            await Verify(window).UseDirectory("Snapshots").UseFileName(rowId);
        }
        finally
        {
            // Verify resumes off the UI thread; close the window back on it.
            await Dispatcher.UIThread.InvokeAsync(window.Close);
        }
    });
}
