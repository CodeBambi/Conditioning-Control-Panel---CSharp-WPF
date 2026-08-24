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
/// - Status truth: reads the probed capability state, never a registration/selection fact.
/// - Panic-quiet: Stop → pipeline.PanicAsync (typed Cancelled, nothing partial surfaces),
///   then SelectProvider(current) RE-ARMS the generation — without re-arm the owner stays
///   cancelled and every later send would terminate Cancelled (pre-approach consult #7).
///   The calm state is a WORKING state (a post-panic send succeeds — tested).
/// - Memory clear: in-window confirm (default NO, Esc = No, re-entrancy-guarded — WPF
///   default-No shape, Patreon.cs:920-927); Clear runs off the UI thread (it blocks on
///   the write chain, per consult); success also clears the on-screen chat log
///   (WPF Patreon.cs:933-936); failure/degraded surface honest typed text (a privacy
///   operation must not lie — pre-completion consult B).
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
    private bool _titleEditorVisible;
    private string _titleAllowInput = string.Empty;
    private string _titleAllowNotice = string.Empty;
    private AiForgetScope _pendingScope = AiForgetScope.Conversation;

    public CompanionViewModel(CompanionParticipant participant, UiDispatchBoundary uiDispatch)
    {
        _participant = participant ?? throw new ArgumentNullException(nameof(participant));
        _ui = uiDispatch ?? throw new ArgumentNullException(nameof(uiDispatch));
        Bubbles = [];
        EffectPermissions =
        [
            .. AiEffectPermissions.Rows.Select(row => new CompanionPermissionRow(
                row,
                () => _participant.Permissions,
                permissions => _participant.Permissions = permissions,
                _participant.Executor.Handles)),
        ];
        SendCommand = new CompanionCommand(Send, () => CanSend);
        StopCommand = new CompanionCommand(Stop, () => InFlight);
        RequestClearCommand = new CompanionCommand(() => RequestForget(AiForgetScope.Conversation), () => !Clearing && !ConfirmVisible);
        ForgetThreadCommand = new CompanionCommand(() => RequestForget(AiForgetScope.Thread), () => !Clearing && !ConfirmVisible);
        ForgetEverythingCommand = new CompanionCommand(() => RequestForget(AiForgetScope.Everything), () => !Clearing && !ConfirmVisible);
        ConfirmClearCommand = new CompanionCommand(ConfirmClear, () => ConfirmVisible && !Clearing);
        CancelClearCommand = new CompanionCommand(() => ConfirmVisible = false, () => ConfirmVisible);
        AddNamedAppCommand = new CompanionCommand(AddNamedApp);
        RemoveNamedAppCommand = new CompanionCommand((Action<object?>)RemoveNamedApp);
        EscapeCommand = new CompanionCommand(Escape);
        ShowTranscriptCommand = new CompanionCommand(() => TranscriptRequested?.Invoke(this, EventArgs.Empty));
        RefreshStatus();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when Escape is pressed with no confirm open (the window closes — W-04 shape).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>D11: raised when the user asks to read the persisted record. The window owns the child window's lifetime; the view-model owns nothing visual.</summary>
    public event EventHandler? TranscriptRequested;

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

    /// <summary>Status line — from the probed capability state ONLY (never a registration/selection fact).</summary>
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
            RaiseDialChanged();
        }
    }

    // =========================================================================================
    //  A3 — the privacy dial, and A4 — the per-app title allow-list it reads
    // =========================================================================================

    /// <summary>
    /// The stop the state IS (never a stored level — <see cref="CompanionPrivacyDial"/> explains
    /// why). Derived from the awareness consent and the F4 allow-list's size, exactly as WPF
    /// derives it (AwarenessPrivacyRuntimeVm.cs:332-336).
    /// </summary>
    public CompanionPrivacyStop PrivacyStop =>
        CompanionPrivacyDial.Derive(_participant.Awareness.Consent.Granted, _participant.Awareness.TitleAllowList.Count);

    /// <summary>What the current stop does, in one line (en.json:4435-4437).</summary>
    public string PrivacyHint => CompanionPrivacyDial.HintFor(PrivacyStop);

    /// <summary>Segment binding for "─ Off". Setting it true turns awareness off; setting it false is ignored (a radio group deselects by selecting another).</summary>
    public bool StopOffSelected
    {
        get => PrivacyStop is CompanionPrivacyStop.Off;
        set { if (value) { SelectStop(CompanionPrivacyStop.Off); } }
    }

    /// <summary>Segment binding for "◔ App names only".</summary>
    public bool StopAppNamesSelected
    {
        get => PrivacyStop is CompanionPrivacyStop.AppNamesOnly;
        set { if (value) { SelectStop(CompanionPrivacyStop.AppNamesOnly); } }
    }

    /// <summary>Segment binding for "◉ + Page titles". Pressing it opens the editor; it does not widen anything on its own.</summary>
    public bool StopPageTitlesSelected
    {
        get => PrivacyStop is CompanionPrivacyStop.PlusPageTitles;
        set { if (value) { SelectStop(CompanionPrivacyStop.PlusPageTitles); } }
    }

    /// <summary>
    /// Whether the per-app editor is on screen. Opened by ASKING for the third stop (WPF
    /// "Everything: enable, then ASK. Nothing widens because a segment was pressed.",
    /// AwarenessPrivacyRuntimeVm.cs:106-113), and it stays open while any app is named so the
    /// user can take one back.
    /// </summary>
    public bool TitleEditorVisible
    {
        get => _titleEditorVisible || _participant.Awareness.TitleAllowList.Count > 0;
        private set
        {
            if (_titleEditorVisible == value) return;
            _titleEditorVisible = value;
            Changed(nameof(TitleEditorVisible));
        }
    }

    /// <summary>The apps whose page titles may travel. EMPTY by default — no title travels for anything.</summary>
    public ObservableCollection<string> NamedApps { get; } = [];

    /// <summary>The app-name box the Add button reads.</summary>
    public string TitleAllowInput
    {
        get => _titleAllowInput;
        set
        {
            if (_titleAllowInput == value) return;
            _titleAllowInput = value;
            Changed(nameof(TitleAllowInput));
        }
    }

    /// <summary>Why the last Add was refused (empty = nothing to say). Never a throw, never silent.</summary>
    public string TitleAllowNotice
    {
        get => _titleAllowNotice;
        private set
        {
            if (_titleAllowNotice == value) return;
            _titleAllowNotice = value;
            Changed(nameof(TitleAllowNotice));
            Changed(nameof(TitleAllowNoticeVisible));
        }
    }

    public bool TitleAllowNoticeVisible => _titleAllowNotice.Length > 0;

    /// <summary>
    /// The ten permission switches, in upstream's grid order
    /// (<c>Views/Controls/Companion/AiPermissionsGrid.xaml:185-199</c>). The list itself never
    /// changes; each row reads and writes the participant's typed permission state.
    /// </summary>
    public IReadOnlyList<CompanionPermissionRow> EffectPermissions { get; }

    /// <summary>
    /// The master switch (upstream's <c>AllowAiToControlEffects</c>,
    /// <c>MainWindow/MainWindow.Patreon.cs:1476</c>). While it is off nothing is admitted whatever
    /// the ten switches say, and the panel of switches is hidden exactly as upstream hides it
    /// (<c>:1477</c>) — the ticks are REMEMBERED, not cleared, so turning the master back on
    /// restores what the user chose rather than silently re-admitting a default set.
    /// </summary>
    public bool EffectsMasterEnabled
    {
        get => _participant.Permissions.MasterEnabled;
        set
        {
            if (_participant.Permissions.MasterEnabled == value)
            {
                return;
            }

            _participant.Permissions = _participant.Permissions.WithMaster(value);
            Changed(nameof(EffectsMasterEnabled));
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

    /// <summary>The CONVERSATION scope (audit row C10): the thread plus everything kept beside it.</summary>
    public ICommand RequestClearCommand { get; }

    /// <summary>The THREAD scope: she forgets what was said and keeps the rest of the document.</summary>
    public ICommand ForgetThreadCommand { get; }

    /// <summary>The EVERYTHING scope: the conversation plus every quarantined copy of it.</summary>
    public ICommand ForgetEverythingCommand { get; }

    public ICommand ConfirmClearCommand { get; }

    public ICommand CancelClearCommand { get; }

    /// <summary>A4: names the app in <see cref="TitleAllowInput"/>. A rejected entry is reported, never thrown.</summary>
    public ICommand AddNamedAppCommand { get; }

    /// <summary>A4: takes one named app back (parameter = the entry).</summary>
    public ICommand RemoveNamedAppCommand { get; }

    public ICommand EscapeCommand { get; }

    /// <summary>D11: opens the read-only transcript over this window.</summary>
    public ICommand ShowTranscriptCommand { get; }

    /// <summary>
    /// D11: the persisted pairs, oldest first — the UNGATED inspection read
    /// (<see cref="AiMemoryStore.ReadRecent"/>), never the consent-gated prompt read. The store's
    /// append-trim already bounds the document, so asking for everything is asking for the
    /// retention window.
    /// </summary>
    public IReadOnlyList<AiMemoryTurn> ReadTranscript() => _participant.Memory.ReadRecent(int.MaxValue);

    /// <summary>The confirm overlay's heading — scope-specific, because the three scopes delete different things.</summary>
    public string ConfirmTitle => _pendingScope switch
    {
        AiForgetScope.Thread => "Clear the conversation?",
        AiForgetScope.Conversation => "Reset companion memory?",
        _ => "Forget everything?",
    };

    /// <summary>
    /// The confirm overlay's body. Each line says what SURVIVES as well as what goes, which is
    /// the property that makes the three scopes distinguishable to a user at all (WPF's own
    /// narrow-scope copy does the same: "She forgets the thread — in memory and on disk — and
    /// starts fresh on your next message. What she knows about you is untouched.",
    /// en.json:4507).
    /// </summary>
    public string ConfirmBody => _pendingScope switch
    {
        AiForgetScope.Thread =>
            "She forgets the thread — in memory and on disk — and starts fresh on your next message. "
            + "The saved document itself is kept. This can't be undone.",
        AiForgetScope.Conversation =>
            "Delete the saved companion memory, and everything kept alongside it. "
            + "Any copy she couldn't read is left where it is. This can't be undone.",
        _ =>
            "Delete the saved companion memory AND every quarantined copy of it, so nothing can bring "
            + "the conversation back. This can't be undone.",
    };

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

    private void RequestForget(AiForgetScope scope)
    {
        if (Clearing)
        {
            return;
        }

        _pendingScope = scope;
        Changed(nameof(ConfirmTitle));
        Changed(nameof(ConfirmBody));
        ClearOutcomeText = string.Empty;
        ConfirmVisible = true;
    }

    private void ConfirmClear()
    {
        if (Clearing)
        {
            return; // re-entrancy guard: one clear at a time
        }

        var scope = _pendingScope;
        ConfirmVisible = false;
        Clearing = true;
        _ = Task.Run(() =>
        {
            // Forget() blocks on the store's write chain (per consult) — never on the UI thread.
            _participant.Memory.Forget(scope);
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
                        ClearOutcomeText = scope switch
                        {
                            AiForgetScope.Thread => "Conversation cleared; the saved document was kept.",
                            AiForgetScope.Conversation => "Companion memory cleared.",
                            _ => "Companion memory cleared, quarantined copies included.",
                        };
                        break;
                    case AiMemoryClearOutcome.Degraded:
                        ClearOutcomeText = "In-session memory cleared; the saved document is from a newer version and was kept.";
                        break;
                    default:
                        // The honest failure path (WPF warning box, Patreon.cs:947-955): a
                        // privacy operation must not lie (pre-completion consult B).
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

    /// <summary>
    /// What pressing a segment DOES (WPF `AwarenessPrivacyRuntimeVm.Intensity`, :84-116, arm for
    /// arm): Off disables awareness; "App names only" enables it and EMPTIES the allow-list,
    /// because that stop is a promise no page title travels (:97-101); "+ Page titles" enables it
    /// and OPENS the editor, widening nothing by itself (:106-113) — the dial goes on reporting
    /// the middle stop until an app is actually named.
    /// </summary>
    private void SelectStop(CompanionPrivacyStop stop)
    {
        var allowList = _participant.Awareness.TitleAllowList;
        switch (stop)
        {
            case CompanionPrivacyStop.Off:
                _participant.Awareness.Consent = AiAwarenessConsent.NotGiven;
                TitleEditorVisible = false;
                break;

            case CompanionPrivacyStop.AppNamesOnly:
                _participant.Awareness.Consent = AiAwarenessConsent.Given;
                allowList.Clear();
                NamedApps.Clear();
                TitleEditorVisible = false;
                break;

            default:
                _participant.Awareness.Consent = AiAwarenessConsent.Given;
                TitleEditorVisible = true;
                break;
        }

        TitleAllowNotice = string.Empty;
        Changed(nameof(AwarenessConsentGiven));
        RaiseDialChanged();
    }

    private void AddNamedApp()
    {
        var raw = _titleAllowInput;
        if (_participant.Awareness.TitleAllowList.Add(raw))
        {
            // The stored form is the SANITISED one, so the chip a user sees is the string the
            // filter actually matches — never the raw text, which would over-promise.
            NamedApps.Add(AiTitleAllowList.SanitizeEntry(raw)!);
            TitleAllowInput = string.Empty;
            TitleAllowNotice = string.Empty;
            TitleEditorVisible = true;
            RaiseDialChanged();
            return;
        }

        // Refusals are reported, never thrown and never silent (AiTitleAllowList.Add).
        TitleAllowNotice = AiTitleAllowList.SanitizeEntry(raw) is null
            ? "That is not usable as an app name: at least two characters, and no wildcards."
            : "Already named.";
    }

    private void RemoveNamedApp(object? parameter)
    {
        if (parameter is not string entry || !_participant.Awareness.TitleAllowList.Remove(entry))
        {
            return;
        }

        NamedApps.Remove(entry);
        TitleAllowNotice = string.Empty;
        RaiseDialChanged();
    }

    // One notification for every property the dial derives, so the strip, the hint and the editor
    // can never disagree about the same state.
    private void RaiseDialChanged()
    {
        Changed(nameof(PrivacyStop));
        Changed(nameof(PrivacyHint));
        Changed(nameof(StopOffSelected));
        Changed(nameof(StopAppNamesSelected));
        Changed(nameof(StopPageTitlesSelected));
        Changed(nameof(TitleEditorVisible));
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
            ((CompanionCommand)ForgetThreadCommand).RaiseCanExecuteChanged();
            ((CompanionCommand)ForgetEverythingCommand).RaiseCanExecuteChanged();
            ((CompanionCommand)ConfirmClearCommand).RaiseCanExecuteChanged();
            ((CompanionCommand)CancelClearCommand).RaiseCanExecuteChanged();
        }
    }

    /// <summary>Direct ICommand (cheat sheet §Commands — no RoutedCommand; the MainWindowViewModel precedent).</summary>
    private sealed class CompanionCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<bool>? _canExecute;

        public CompanionCommand(Action execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute)
        {
        }

        /// <summary>The parameterised form, for the one command that acts on a list item (remove-named-app).</summary>
        public CompanionCommand(Action<object?> execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute(parameter);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
