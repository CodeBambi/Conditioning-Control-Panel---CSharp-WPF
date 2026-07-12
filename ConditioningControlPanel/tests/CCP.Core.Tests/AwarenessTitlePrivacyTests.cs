using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Awareness;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the HARD privacy contract for foreground-window-title awareness
/// (linux-foreground-title-contract.md §1.3; AwarenessService.cs header): the RAW
/// foreground window title is memory-only input for change detection — it is NEVER written
/// to disk, NEVER sent over the network, and NEVER logged. The WPF head's debug line that
/// logged the full raw title (WPF Services/UI/WindowAwarenessService.cs:483-486) was
/// deliberately NOT ported. Log lines carry only the DERIVED detected name.
///
/// These tests assert the invariant directly: feed the engine a raw-title SENTINEL via a
/// fake provider, run the classification poll, capture every formatted log message, and
/// assert the sentinel never appears in any of them. This is the same class of invariant
/// as the webcam rule (frames never hit disk/network). It applies to ALL heads, so it
/// lives in Core.Tests, not the Linux head.
/// </summary>
public class AwarenessTitlePrivacyTests
{
    /// <summary>
    /// A sentinel raw-title string that does NOT match any classifier keyword, so the
    /// engine classifies it as Unknown (detectedName = "something") — guaranteeing the
    /// sentinel substring cannot leak into a derived name that IS legitimately logged.
    /// </summary>
    private const string SentinelRawTitle = "ZIRCONIA_SENTINEL_RAW_TITLE_987654321_DISCLOSE_ME";

    private sealed class CapturingLogger<TCategory> : ILogger<TCategory>
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // Capture the FULLY FORMATTED message (the same string a real sink would write),
            // so the assertion exercises the rendered output, not the structured template.
            if (formatter is not null)
            {
                Messages.Add(formatter(state, exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public bool WasSettingsFileMissing => true;
        public List<string> PendingPresetReinstalls { get; } = new();
        public void Save() { }
        public void Save(bool suppressCloudBackup = false) { }
        public void SaveImmediate(bool suppressCloudBackup = false) { }
        public void RestoreFrom(AppSettings settings) { }
        public void Reset() { }
    }

    private sealed class FakeTitleProvider : IForegroundWindowTitleProvider
    {
        public string? Title { get; set; }
        public string? GetForegroundWindowTitle() => Title;
    }

    [Fact]
    public void RawWindowTitle_IsNeverLogged_ToAnyLoggerSink()
    {
        var settings = new FakeSettingsService();
        settings.Current.AwarenessModeEnabled = true;
        settings.Current.AwarenessConsentGiven = true;
        var provider = new FakeTitleProvider { Title = SentinelRawTitle };
        var logger = new CapturingLogger<AwarenessService>();
        var engine = new AwarenessService(settings, provider, logger);

        int activityChanges = 0;
        engine.ActivityChanged += (_, _) => activityChanges++;

        // Run the classification poll (the internal seam used by AwarenessEngineTests — no
        // real timer). WPF :483-486 logged the raw title here at Debug; this port must not.
        engine.PollTick(DateTime.Now);

        // Non-vacuity: the title WAS read and processed (classification ran, the Unknown
        // detectedName changed from "" → "something", firing ActivityChanged).
        Assert.True(activityChanges >= 1, "expected the poll to read the title and classify");

        // The hard invariant: the raw title sentinel appears in NO captured log message.
        Assert.All(logger.Messages,
            msg => Assert.DoesNotContain(SentinelRawTitle, msg, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages,
            m => m.Contains(SentinelRawTitle, StringComparison.Ordinal));
    }

    [Fact]
    public void RawWindowTitle_IsNeverLogged_AcrossIdleAndChangePaths()
    {
        var settings = new FakeSettingsService();
        settings.Current.AwarenessModeEnabled = true;
        settings.Current.AwarenessConsentGiven = true;
        var provider = new FakeTitleProvider { Title = SentinelRawTitle };
        var logger = new CapturingLogger<AwarenessService>();
        var engine = new AwarenessService(settings, provider, logger);

        // First poll: change-detection path (title differs from initial "").
        engine.PollTick(DateTime.Now);
        // Second poll: same title → idle/same-window path (WPF :489-497). Neither path may log
        // the raw title.
        engine.PollTick(DateTime.Now.AddSeconds(1));
        // Third poll: a different sentinel → another change-detection pass.
        provider.Title = SentinelRawTitle + "_SECOND";
        engine.PollTick(DateTime.Now.AddSeconds(2));

        Assert.DoesNotContain(logger.Messages,
            m => m.Contains(SentinelRawTitle, StringComparison.Ordinal));
    }

    [Fact]
    public void NullTitle_FallbackProvider_ClassifiesUnknown_WithoutCrashing()
    {
        // §1.4 degrade: a head whose provider returns null (Linux fallback backend on a
        // Wayland/stock-GNOME desktop) must keep the engine alive classifying Unknown — no
        // reactions, no crash. This is exactly what LinuxForegroundWindowTitleProvider +
        // FallbackTitleBackend produce on a non-X11 session.
        var settings = new FakeSettingsService();
        settings.Current.AwarenessModeEnabled = true;
        settings.Current.AwarenessConsentGiven = true;
        var provider = new FakeTitleProvider { Title = null };
        var engine = new AwarenessService(settings, provider, logger: null);

        var ex = Record.Exception(() => engine.PollTick(DateTime.Now));

        Assert.Null(ex);
        Assert.Equal(ActivityCategory.Unknown, engine.CurrentActivity);
    }
}
