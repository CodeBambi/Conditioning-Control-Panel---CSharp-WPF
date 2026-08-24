using System.Collections.Concurrent;
using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Features.Companion;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The companion surface's side of A3 (the dial), A4 (the per-app editor) and C10 (the three
/// forget scopes), driven through the REAL view-model over the REAL participant — the same
/// discipline <see cref="CompanionViewModelTests"/> uses, because a dial that moves in a test
/// double proves nothing about the state the filter actually reads.
/// </summary>
public class CompanionPrivacySurfaceTests
{
    // ---- A3: what pressing a segment does, arm for arm (WPF AwarenessPrivacyRuntimeVm.cs:84-116) ----

    [Fact]
    public void FreshSurface_SitsAtOff_WithHerEyesClosed()
    {
        using var h = new Harness();

        Assert.Equal(CompanionPrivacyStop.Off, h.Vm.PrivacyStop);
        Assert.True(h.Vm.StopOffSelected);
        Assert.False(h.Vm.AwarenessConsentGiven);
        Assert.Equal("her eyes are closed. nothing is watched, nothing is counted.", h.Vm.PrivacyHint);
        Assert.False(h.Vm.TitleEditorVisible);
        Assert.Empty(h.Participant.Awareness.TitleAllowList.Entries);
    }

    /// <summary>
    /// THE INVERSION THIS ROW IS ABOUT. Asking for "+ Page titles" enables awareness and OPENS the
    /// editor; it widens nothing by itself, and the dial goes on reading the middle stop until an
    /// app is actually named (WPF :106-113, reason at :24-27). Naming one is the single action
    /// that moves it — and un-naming it moves it back.
    /// </summary>
    [Fact]
    public void AskingForPageTitles_OpensTheEditor_ButTheDialOnlyMovesWhenAnAppIsNamed()
    {
        using var h = new Harness();

        h.Vm.StopPageTitlesSelected = true;

        Assert.True(h.Vm.AwarenessConsentGiven);                              // the capability switch moved
        Assert.True(h.Vm.TitleEditorVisible);                                 // the editor opened
        Assert.Empty(h.Participant.Awareness.TitleAllowList.Entries);         // nothing widened
        Assert.Equal(CompanionPrivacyStop.AppNamesOnly, h.Vm.PrivacyStop);    // the dial did NOT move
        Assert.False(h.Vm.StopPageTitlesSelected);
        Assert.Equal("the category, the app name and a rounded time. never a page title.", h.Vm.PrivacyHint);

        h.Vm.TitleAllowInput = "Browser";
        h.Vm.AddNamedAppCommand.Execute(null);

        Assert.Equal(CompanionPrivacyStop.PlusPageTitles, h.Vm.PrivacyStop);  // now, and only now
        Assert.True(h.Vm.StopPageTitlesSelected);
        Assert.Equal("app names, plus page titles for the apps you name yourself.", h.Vm.PrivacyHint);
        Assert.Equal(["browser"], h.Vm.NamedApps);
        Assert.True(h.Participant.Awareness.TitleAllowList.AllowsTitleFor("Browser"));

        h.Vm.RemoveNamedAppCommand.Execute("browser");

        Assert.Equal(CompanionPrivacyStop.AppNamesOnly, h.Vm.PrivacyStop);
        Assert.Empty(h.Vm.NamedApps);
        Assert.False(h.Participant.Awareness.TitleAllowList.AllowsTitleFor("Browser"));
    }

    [Fact]
    public void TheMiddleStopEmptiesTheList_BecauseThatIsWhatTheLabelPromises()
    {
        using var h = new Harness();
        h.Vm.StopPageTitlesSelected = true;
        h.Vm.TitleAllowInput = "browser";
        h.Vm.AddNamedAppCommand.Execute(null);
        Assert.Equal(CompanionPrivacyStop.PlusPageTitles, h.Vm.PrivacyStop);

        // WPF: "'Broad strokes' is a promise that no page title travels, so selecting it empties
        // the allow list." (AwarenessPrivacyRuntimeVm.cs:97-101.)
        h.Vm.StopAppNamesSelected = true;

        Assert.Equal(CompanionPrivacyStop.AppNamesOnly, h.Vm.PrivacyStop);
        Assert.True(h.Vm.AwarenessConsentGiven);
        Assert.Empty(h.Participant.Awareness.TitleAllowList.Entries);
        Assert.Empty(h.Vm.NamedApps);
        Assert.False(h.Vm.TitleEditorVisible);
    }

    [Fact]
    public void TurningTheDialOff_StopsAwareness_AndTheHintSaysSo()
    {
        using var h = new Harness();
        h.Vm.StopAppNamesSelected = true;
        Assert.True(h.Vm.AwarenessConsentGiven);

        h.Vm.StopOffSelected = true;

        Assert.Equal(CompanionPrivacyStop.Off, h.Vm.PrivacyStop);
        Assert.False(h.Vm.AwarenessConsentGiven);
        Assert.Equal(AiAwarenessConsent.NotGiven, h.Participant.Awareness.Consent);
        Assert.Equal("her eyes are closed. nothing is watched, nothing is counted.", h.Vm.PrivacyHint);
    }

    [Fact]
    public void ARefusedAppName_IsReported_AndNamesNothing()
    {
        using var h = new Harness();
        h.Vm.StopPageTitlesSelected = true;

        h.Vm.TitleAllowInput = "*"; // sanitises to nothing (WPF AwarenessText.cs:183, :191-197)
        h.Vm.AddNamedAppCommand.Execute(null);

        Assert.True(h.Vm.TitleAllowNoticeVisible);
        Assert.Empty(h.Vm.NamedApps);
        Assert.Empty(h.Participant.Awareness.TitleAllowList.Entries);
        Assert.Equal(CompanionPrivacyStop.AppNamesOnly, h.Vm.PrivacyStop);
        Assert.Equal("*", h.Vm.TitleAllowInput); // the refused text stays for editing, never silently eaten
    }

    // ---- C10: three buttons, three confirmations, three outcomes ----

    [Fact]
    public void TheThreeForgetButtonsAskThreeDifferentQuestions()
    {
        using var h = new Harness();

        h.Vm.ForgetThreadCommand.Execute(null);
        var threadTitle = h.Vm.ConfirmTitle;
        var threadBody = h.Vm.ConfirmBody;
        h.Vm.CancelClearCommand.Execute(null);

        h.Vm.RequestClearCommand.Execute(null);
        var conversationTitle = h.Vm.ConfirmTitle;
        var conversationBody = h.Vm.ConfirmBody;
        h.Vm.CancelClearCommand.Execute(null);

        h.Vm.ForgetEverythingCommand.Execute(null);
        var everythingTitle = h.Vm.ConfirmTitle;
        var everythingBody = h.Vm.ConfirmBody;
        h.Vm.CancelClearCommand.Execute(null);

        // Three distinct questions — a scope whose confirmation reads like its neighbour's is a
        // scope the user cannot choose between, which is the whole point of having three.
        Assert.Equal(3, new[] { threadTitle, conversationTitle, everythingTitle }.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, new[] { threadBody, conversationBody, everythingBody }.Distinct(StringComparer.Ordinal).Count());

        // And each one says what SURVIVES, which is what makes them distinguishable at all (WPF's
        // own narrow-scope copy does exactly this, Localization/Languages/en.json:4507).
        Assert.Contains("kept", threadBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("left where it is", conversationBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("every quarantined copy", everythingBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ForgetThread_ClearsTheThreadOnScreenAndOnDisk_AndKeepsTheDocument()
    {
        using var h = new Harness();
        await h.StartMemoryAsync();
        h.Participant.MemoryConsent = AiMemoryConsent.Granted;
        h.Participant.Memory.Append(new AiMemoryTurn(AiMemoryRole.User, "surface-turn"));
        Assert.IsType<OperationOutcome.Completed>(await h.Participant.Memory.LastWriteCompletion!);
        Assert.Contains("surface-turn", File.ReadAllText(h.MemoryFile), StringComparison.Ordinal);

        h.Vm.ForgetThreadCommand.Execute(null);
        h.Vm.ConfirmClearCommand.Execute(null);
        h.PumpEventually();

        Assert.Empty(h.Vm.Bubbles);
        Assert.Contains("kept", h.Vm.ClearOutcomeText, StringComparison.OrdinalIgnoreCase);
        // The narrow scope, on the surface: the document is still there, and the thread is not.
        Assert.True(File.Exists(h.MemoryFile));
        Assert.DoesNotContain("surface-turn", File.ReadAllText(h.MemoryFile), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForgetEverything_TakesTheDocument_AndSaysWhichScopeRan()
    {
        using var h = new Harness();
        await h.StartMemoryAsync();
        h.Participant.MemoryConsent = AiMemoryConsent.Granted;
        h.Participant.Memory.Append(new AiMemoryTurn(AiMemoryRole.User, "surface-turn"));
        Assert.IsType<OperationOutcome.Completed>(await h.Participant.Memory.LastWriteCompletion!);

        h.Vm.ForgetEverythingCommand.Execute(null);
        h.Vm.ConfirmClearCommand.Execute(null);
        h.PumpEventually();

        Assert.False(File.Exists(h.MemoryFile));
        Assert.Contains("quarantined copies included", h.Vm.ClearOutcomeText, StringComparison.OrdinalIgnoreCase);
    }

    // ---- D11: the surface hands the window exactly what is persisted ----

    [Fact]
    public async Task ReadTranscript_IsThePersistedRecord_AndIsNotGatedByMemoryConsent()
    {
        using var h = new Harness();
        await h.StartMemoryAsync();
        Assert.Empty(h.Vm.ReadTranscript());

        h.Participant.MemoryConsent = AiMemoryConsent.Granted;
        h.Participant.Memory.Append(new AiMemoryTurn(AiMemoryRole.User, "first"));
        h.Participant.Memory.Append(new AiMemoryTurn(AiMemoryRole.Assistant, "second"));
        Assert.IsType<OperationOutcome.Completed>(await h.Participant.Memory.LastWriteCompletion!);

        Assert.Equal(
            [new AiMemoryTurn(AiMemoryRole.User, "first"), new AiMemoryTurn(AiMemoryRole.Assistant, "second")],
            h.Vm.ReadTranscript());

        // Withdrawing consent stops the PROMPT read (contract §5 rule 2) but must not blind the
        // user to their own stored document — otherwise the transparency surface would go dark
        // exactly when the user is checking on it.
        h.Participant.MemoryConsent = AiMemoryConsent.Denied;
        Assert.Empty(h.Participant.Memory.ReadPromptContext());
        Assert.Equal(2, h.Vm.ReadTranscript().Count);

        // And it shows only what is retained: a forget empties it.
        h.Vm.ForgetThreadCommand.Execute(null);
        h.Vm.ConfirmClearCommand.Execute(null);
        h.PumpEventually();
        Assert.Empty(h.Vm.ReadTranscript());
    }

    private sealed class QueuedDispatch : IUiDispatch
    {
        private readonly ConcurrentQueue<Action> _queue = new();

        public int Pending => _queue.Count;

        public void Post(Action action) => _queue.Enqueue(action);

        public void Pump()
        {
            while (_queue.TryDequeue(out var action))
            {
                action();
            }
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-privacy-surface-" + Guid.NewGuid().ToString("N"));

        public Harness()
        {
            DispatchBoundary.Bind(Dispatch);
            Directory.CreateDirectory(_dir);
            Participant = new CompanionParticipant(
                new ParticipantInfrastructure(Registry, DispatchBoundary, new DebugLogSink()),
                Capabilities, _dir);
            Vm = new CompanionViewModel(Participant, DispatchBoundary);
        }

        public OperationRegistry Registry { get; } = new();

        public CapabilityRegistry Capabilities { get; } = new();

        public UiDispatchBoundary DispatchBoundary { get; } = new();

        public QueuedDispatch Dispatch { get; } = new();

        public CompanionParticipant Participant { get; }

        public CompanionViewModel Vm { get; }

        public string MemoryFile => Path.Combine(_dir, AiMemoryStore.FileName);

        public Task StartMemoryAsync() => Participant.StartAsync(CancellationToken.None);

        /// <summary>Bounded wait for the off-thread forget to post, then pump it on this thread (the c7 discipline).</summary>
        public void PumpEventually()
        {
            TestWait.UntilSync(() => Dispatch.Pending > 0, "a dispatch post arriving within the bound", () => $"pending={Dispatch.Pending}");
            Dispatch.Pump();
        }

        public void Dispose()
        {
            Participant.StopAsync().GetAwaiter().GetResult(); // wallclock-allow: a passthrough to PersistenceStore.StopAsync, which cancels a generation on the calling thread and touches no file — this bridge waits on nothing
            try
            {
                Directory.Delete(_dir, recursive: true);
            }
            catch (IOException)
            {
                // best-effort temp cleanup
            }
        }
    }
}
