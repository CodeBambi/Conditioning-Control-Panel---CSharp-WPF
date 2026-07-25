using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #686 — "Critical audio bug": a throwing Unduck() left the 2.5s duck-rescan timer alive, which
/// re-ducked every audio session on the machine forever AND re-walked the WASAPI enumeration chain
/// 24x/min. Each sweep stranded a pile of COM wrappers (endpoint collection, device, session
/// manager, session collection, sessions) for the finalizer, which never caught up — observed
/// ~25MB/min native growth to 6GB private bytes / 8824 handles.
///
/// The leak fix is <see cref="AudioService.RenderSessionScope"/>: every wrapper produced by a sweep
/// is owned by a scope and released deterministically at the end of the using block. These tests
/// cover that release contract with fakes (no audio hardware involved).
/// </summary>
public class DuckSessionScopeTests
{
    private sealed class FakeWrapper : IDisposable
    {
        private readonly List<string> _log;
        private readonly bool _throwOnDispose;

        public FakeWrapper(string name, List<string> log, bool throwOnDispose = false)
        {
            Name = name;
            _log = log;
            _throwOnDispose = throwOnDispose;
        }

        public string Name { get; }
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            _log.Add(Name);
            if (_throwOnDispose) throw new InvalidOperationException($"{Name} refused to release");
        }
    }

    /// <summary>
    /// Children must be released before their parents (session -> session collection -> manager
    /// -> device -> endpoint collection), i.e. reverse acquisition order.
    /// </summary>
    [Fact]
    public void Scope_DisposesOwnedWrappers_InReverseAcquisitionOrder()
    {
        var log = new List<string>();
        var scope = new AudioService.RenderSessionScope();
        scope.Own(new FakeWrapper("endpoints", log));
        scope.Own(new FakeWrapper("device", log));
        scope.Own(new FakeWrapper("manager", log));
        scope.Own(new FakeWrapper("sessions", log));

        scope.Dispose();

        Assert.Equal(new[] { "sessions", "manager", "device", "endpoints" }, log);
    }

    /// <summary>
    /// A wrapper whose Dispose throws (endpoint yanked mid-sweep, COM object already released by
    /// its parent) must not abort the sweep — everything else still has to be released, or the
    /// leak comes straight back.
    /// </summary>
    [Fact]
    public void Scope_ThrowingDispose_DoesNotStrandTheOtherWrappers()
    {
        var log = new List<string>();
        var first = new FakeWrapper("endpoints", log);
        var bad = new FakeWrapper("device", log, throwOnDispose: true);
        var last = new FakeWrapper("sessions", log);

        var scope = new AudioService.RenderSessionScope();
        scope.Own(first);
        scope.Own(bad);
        scope.Own(last);

        scope.Dispose(); // must not throw

        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, bad.DisposeCount);
        Assert.Equal(1, last.DisposeCount);
    }

    /// <summary>
    /// Disposing twice (using block + an explicit call) must not double-release COM wrappers.
    /// </summary>
    [Fact]
    public void Scope_SecondDispose_IsANoOp()
    {
        var log = new List<string>();
        var wrapper = new FakeWrapper("device", log);

        var scope = new AudioService.RenderSessionScope();
        scope.Own(wrapper);
        scope.Dispose();
        scope.Dispose();

        Assert.Equal(1, wrapper.DisposeCount);
    }

    /// <summary>
    /// NAudio does not make every wrapper in the chain IDisposable, and nulls are possible when an
    /// endpoint vanishes mid-enumeration. Neither may blow up the sweep.
    /// </summary>
    [Fact]
    public void Scope_IgnoresNullAndNonDisposableEntries()
    {
        var log = new List<string>();
        var wrapper = new FakeWrapper("device", log);

        var scope = new AudioService.RenderSessionScope();
        scope.Own(null);
        scope.Own(new object());
        scope.Own(wrapper);

        scope.Dispose(); // must not throw

        Assert.Equal(1, wrapper.DisposeCount);
    }

    [Fact]
    public void Scope_WithNothingOwned_DisposesCleanly()
    {
        var scope = new AudioService.RenderSessionScope();
        scope.Dispose();
        Assert.Empty(scope.Sessions);
    }

    /// <summary>
    /// The Test Audio button used to count Resources/sounds recursively (~5200 files) on the UI
    /// thread. The count is a diagnostic only, so it is now bounded.
    /// </summary>
    [Fact]
    public void CountFilesBounded_StopsAtTheProbeCap()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-686-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const int Cap = 500;
            for (int i = 0; i < Cap + 25; i++)
                File.WriteAllText(Path.Combine(dir, $"f{i}.mp3"), "x");

            var count = AudioService.CountFilesBounded(dir, SearchOption.AllDirectories);

            Assert.Equal(Cap, count);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CountFilesBounded_CountsExactlyBelowTheCap_AndSurvivesAMissingFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-686-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            for (int i = 0; i < 7; i++)
                File.WriteAllText(Path.Combine(dir, $"f{i}.mp3"), "x");

            Assert.Equal(7, AudioService.CountFilesBounded(dir, SearchOption.TopDirectoryOnly));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }

        Assert.Equal(0, AudioService.CountFilesBounded(dir, SearchOption.AllDirectories));
    }
}
