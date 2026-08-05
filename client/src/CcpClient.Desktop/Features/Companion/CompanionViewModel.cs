using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Companion;

/// <summary>
/// Hand-rolled view-model for the companion surface (no MVVM package — the
/// MainWindowViewModel precedent). Threading (async contract §5): the send/panic/clear
/// operations run OFF the UI thread (<see cref="Task.Run"/>); every UI mutation happens
/// inside a <see cref="UiDispatchBoundary"/> post with a closed-window liveness check
/// (the delegate is harmless if it runs late or never). No SynchronizationContext capture.
///
/// Behavior contracts (WPF parity, record.md §1):
/// - Send: Enter or button; empty/whitespace no-ops (ChatInput.cs:674-675); the user
///   bubble lands immediately and the box clears; a thinking bubble shows in-flight
///   (:705-707) and is REMOVED WHOLE on resolve — nothing partial edits into final state.
/// - Badge truth: bubbles badge from the typed reply BY TYPE only (CompanionBubbleModel).
/// - Status truth: reads the SP-006 capability state, never a registration/selection fact.
/// - Panic-quiet: Stop → pipeline.PanicAsync (typed Cancelled, nothing partial surfaces),
///   then SelectProvider(current) RE-ARMS the generation — without re-arm the owner stays
///   cancelled and every later send would terminate Cancelled (pre-approach consult #7).
///   The calm state is a WORKING state (a post-panic send succeeds — tested).
/// - Memory clear: in-window confirm (default NO, Esc = No, re-entrancy-guarded — WPF
///   default-No shape, Patreon.cs:920-927); Clear runs off the UI thread (it blocks on
///   the write chain, SP-040 consult); success also clears the on-screen chat log
///   (WPF Patreon.cs:933-936); failure/degraded surface honest typed text (a privacy
///   operation must not lie — SP-040 pre-completion consult B).
/// - Consents/cooldowns: session-scoped typed states (owner-pending placeholders; never
///   persisted). Cooldown edits apply to the NEXT extension; a live cooldown is never
///   shortened (registry extend-not-shrink, tested).
/// </summary>
public sealed class CompanionViewModel : INotifyPropertyChanged
{
    private readonly CompanionParticipant _participant;
    private readonly UiDispatchBoundary _ui;
    private string _inputText = string.Empty;
    private bool _inFlight;
    private bool _confirmVisible;
    private bool _clearing;
    private bool _closed;
    private string _statusText = string.Empty;
    private bool _statusAvailable;
    private string _clearOutcomeText = string.Empty;

    public CompanionViewModel(CompanionParticipant participant, UiDispatchBoundary uiDispatch)
    {
        _participant = participant ?? throw new ArgumentNullException(nameof(participant));
        _ui = uiDispatch ?? throw new ArgumentNullException(nameof(uiDispatch));
        Bubbles = [];
        SendCommand = new CompanionCommand(Send, () => CanSend);
        StopCommand = new CompanionCommand(Stop, () => InFlight);
        RequestClearCommand = new CompanionCommand(RequestClear, () => !Clearing && !ConfirmVisible);
        ConfirmClearCommand = new CompanionCommand(ConfirmClear, () => ConfirmVisible && !Clearing);
        CancelClearCommand = new CompanionCommand(() => ConfirmVisible = false, () => ConfirmVisible);
        EscapeCommand = new CompanionCommand(Escape);
        RefreshStatus();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when Escape is pressed with no confirm open (the window closes — W-04 shape).</summary>
    public event EventHandler? CloseRequested;

    public ObservableCollection<CompanionBubbleModel> Bubbles { get; }

    public string InputText
    {
        get => _inputText;
        set
        {
            if (_inputText == value) return;
            _inputText = value;
            Changed(nameof(InputText));
            Changed(nameof(CanSend));
        }
    }

    /// <summary>Send admission: non-empty text, nothing in flight, no confirm/clear active.</summary>
    public bool CanSend => _inputText.Trim().Length > 0 && !InFlight && !ConfirmVisible && !Clearing;

    public bool InFlight
    {
        get => _inFlight;
        private set
        {
            if (_inFlight == value) return;
            _inFlight = value;
            Changed(nameof(InFlight));
            Changed(nameof(CanSend));
        }
    }

    /// <summary>The in-window memory-clear confirm (modal within the window; default NO — the code-behind focuses the No button).</summary>
    public bool ConfirmVisible
    {
        get => _confirmVisible;
        private set
        {
            if (_confirmVisible == value) return;
            _confirmVisible = value;
            Changed(nameof(ConfirmVisible));
            Changed(nameof(CanSend));
        }
    }

    public bool Clearing
    {
        get => _clearing;
        private set
        {
            if (_clearing == value) return;
            _clearing = value;
            Changed(nameof(Clearing));
            Changed(nameof(CanSend));
        }
    }

    /// <summary>Status line — from the SP-006 capability state ONLY (never a registration/selection fact).</summary>
    public string StatusText
    {
        get => _statusText;
        private set { if (_statusText != value) { _statusText = value; Changed(nameof(StatusText)); } }
    }

    /// <summary>Drives the status accent class (available = lit accent; anything else = subdued).</summary>
    public bool StatusAvailable
    {
        get => _statusAvailable;
        private set { if (_statusAvailable != value) { _statusAvailable = value; Changed(nameof(StatusAvailable)); } }
    }

    /// <summary>The memory-clear typed outcome text (empty = hidden).</summary>
    public string ClearOutcomeText
    {
        get => _clearOutcomeText;
        private set
        {
            if (_clearOutcomeText != value) { _clearOutcomeText = value; Changed(nameof(ClearOutcomeText)); Changed(nameof(ClearOutcomeVisible)); }
        }
    }

    public bool ClearOutcomeVisible => _clearOutcomeText.Length > 0;

    /// <summary>Awareness consent toggle → the typed state (code-enforced at operation admission).</summary>
    public bool AwarenessConsentGiven
    {
        get => _participant.Awareness.Consent.Granted;
        set
        {
            _participant.Awareness.Consent = value ? AiAwarenessConsent.Given : AiAwarenessConsent.NotGiven;
            Changed(nameof(AwarenessConsentGiven));
        }
    }

    /// <summary>Memory consent toggle → the typed state the store consults at every write admission.</summary>
    public bool MemoryConsentGranted
    {
        get => _participant.MemoryConsent == AiMemoryConsent.Granted;
        set
        {
            _participant.MemoryConsent = value ? AiMemoryConsent.Granted : AiMemoryConsent.Denied;
            Changed(nameof(MemoryConsentGranted));
        }
    }

    // Cooldown value boxes (session placeholders, §9.2 #4 owner-pending; UI clamp 1-600 —
    // the widest WPF clamp family member, AppSettings.cs:3000-3008,4314 — values remain
    // baselines, never decisions). decimal? matches NumericUpDown.Value for compiled bindings.
    public decimal? ReactionSeconds
    {
        get => (decimal)_participant.Awareness.Values.Reaction.TotalSeconds;
        set => SetCooldowns(reaction: value);
    }

    public decimal? GlobalSeconds
    {
        get => (decimal)_participant.Awareness.Values.Global.TotalSeconds;
        set => SetCooldowns(global: value);
    }

    public decimal? PerKeywordSeconds
    {
        get => (decimal)_participant.Awareness.Values.PerKeyword.TotalSeconds;
        set => SetCooldowns(perKeyword: value);
    }

    public decimal? LoopProtectionSeconds
    {
        get => (decimal)_participant.Awareness.Values.LoopProtection.TotalSeconds;
        set => SetCooldowns(loopProtection: value);
    }

    public ICommand SendCommand { get; }

    public ICommand StopCommand { get; }

    public ICommand RequestClearCommand { get; }

    public ICommand ConfirmClearCommand { get; }

    public ICommand CancelClearCommand { get; }

    public ICommand EscapeCommand { get; }

    /// <summary>The window reads status on open and after every operation (states change only via probes/operations).</summary>
    public void RefreshStatus()
    {
        var state = _participant.Capabilities.GetState(AiOperationPipeline.CapabilityName(AiProviderId.LocalOllama));
        StatusAvailable = state is CapabilityState.Available;
        StatusText = state switch
        {
            CapabilityState.Available => "Companion provider: available (loopback)",
            CapabilityState.Unavailable unavailable => $"Companion provider: unavailable ({unavailable.Reason.Code})",
            CapabilityState.Degraded degraded => $"Companion provider: degraded ({degraded.Reason.Code})",
            CapabilityState.Faulted faulted => $"Companion provider: faulted ({faulted.Reason.Code})",
            // Unprobed/unknown arrive as Unavailable(not-probed/unknown-capability) — covered above.
            _ => $"Companion provider: {state.GetType().Name}",
        };
    }

    /// <summary>Liveness flag (async contract §5.5): posts arriving after close are no-ops.</summary>
    public void OnWindowClosed() => _closed = true;

    private void Send()
    {
        var text = _inputText.Trim();
        if (text.Length == 0 || InFlight || ConfirmVisible || Clearing)
        {
            return;
        }

        // WPF order (ChatInput.cs:688-689): the box clears and the user bubble lands BEFORE
        // the AI call; the thinking bubble is the in-flight affordance (:705-707).
        Bubbles.Add(CompanionBubbleModel.User(text));
        InputText = string.Empty;
        InFlight = true;
        var thinking = CompanionBubbleModel.Thinking();
        Bubbles.Add(thinking);

        _ = Task.Run(async () =>
        {
            var result = await _participant.Pipeline.RunInteractiveAsync(new AiRequest(text)).ConfigureAwait(false);
            _ui.Post(() => ApplySendResult(thinking, result));
        });
    }

    private void ApplySendResult(CompanionBubbleModel thinking, AiOperationResult result)
    {
        if (_closed)
        {
            return;
        }

        // The thinking bubble is removed WHOLE — a final bubble is a fresh model, never an
        // edit of the in-flight one (nothing partial surfaces).
        Bubbles.Remove(thinking);
        InFlight = false;
        if (result.Outcome is OperationOutcome.Completed && result.Reply is { } reply)
        {
            Bubbles.Add(CompanionBubbleModel.ForReply(reply));
        }

        // Cancelled (panic mid-flight): QUIET — no bubble, calm surface, input re-enabled.
        RefreshStatus();
    }

    private void Stop()
    {
        if (!InFlight)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await _participant.Pipeline.PanicAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            // Re-arm (pre-approach consult #7): after panic the owner STAYS cancelled —
            // without a new generation every later send terminates Cancelled. Selecting the
            // current provider begins one (the switch machinery is the re-arm, contract §3
            // rule 2); the calm state the surface returns to is a WORKING state.
            if (_participant.Pipeline.Selected is { } selected)
            {
                _participant.Pipeline.SelectProvider(selected);
            }
        });
    }

    private void RequestClear()
    {
        if (Clearing)
        {
            return;
        }

        ClearOutcomeText = string.Empty;
        ConfirmVisible = true;
    }

    private void ConfirmClear()
    {
        if (Clearing)
        {
            return; // re-entrancy guard: one clear at a time
        }

        ConfirmVisible = false;
        Clearing = true;
        _ = Task.Run(() =>
        {
            // Clear() blocks on the store's write chain (SP-040 consult) — never on the UI thread.
            _participant.Memory.Clear();
            var outcome = _participant.Memory.LastClearOutcome;
            _ui.Post(() =>
            {
                if (_closed)
                {
                    return;
                }

                Clearing = false;
                switch (outcome)
                {
                    case AiMemoryClearOutcome.Cleared:
                        Bubbles.Clear(); // WPF: the on-screen chat log clears too (Patreon.cs:933-936)
                        ClearOutcomeText = "Companion memory cleared.";
                        break;
                    case AiMemoryClearOutcome.Degraded:
                        ClearOutcomeText = "In-session memory cleared; the saved document is from a newer version and was kept.";
                        break;
                    default:
                        // The honest failure path (WPF warning box, Patreon.cs:947-955): a
                        // privacy operation must not lie (SP-040 pre-completion consult B).
                        ClearOutcomeText = "Memory clear failed: the saved file could not be deleted. Try again.";
                        break;
                }
            });
        });
    }

    private void Escape()
    {
        if (ConfirmVisible)
        {
            ConfirmVisible = false; // Esc = the default-NO path (WPF MessageBox default)
        }
        else
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetCooldowns(decimal? reaction = null, decimal? global = null, decimal? perKeyword = null, decimal? loopProtection = null)
    {
        var current = _participant.Awareness.Values;
        _participant.Awareness.Values = new AiCooldownValues(
            ClampSeconds(reaction, current.Reaction),
            ClampSeconds(global, current.Global),
            ClampSeconds(perKeyword, current.PerKeyword),
            ClampSeconds(loopProtection, current.LoopProtection));
        Changed(nameof(ReactionSeconds));
        Changed(nameof(GlobalSeconds));
        Changed(nameof(PerKeywordSeconds));
        Changed(nameof(LoopProtectionSeconds));

        static TimeSpan ClampSeconds(decimal? value, TimeSpan fallback) =>
            value is { } v ? TimeSpan.FromSeconds(Math.Clamp((double)v, 1, 600)) : fallback;
    }

    private void Changed(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name is nameof(CanSend))
        {
            ((CompanionCommand)SendCommand).RaiseCanExecuteChanged();
        }

        if (name is nameof(InFlight))
        {
            ((CompanionCommand)StopCommand).RaiseCanExecuteChanged();
        }

        if (name is nameof(ConfirmVisible) or nameof(Clearing))
        {
            ((CompanionCommand)RequestClearCommand).RaiseCanExecuteChanged();
            ((CompanionCommand)ConfirmClearCommand).RaiseCanExecuteChanged();
            ((CompanionCommand)CancelClearCommand).RaiseCanExecuteChanged();
        }
    }

    /// <summary>Direct ICommand (cheat sheet §Commands — no RoutedCommand; the MainWindowViewModel precedent).</summary>
    private sealed class CompanionCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public CompanionCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
