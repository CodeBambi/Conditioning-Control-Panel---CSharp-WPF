using System.ComponentModel;
using System.Windows.Input;
using CcpClient.Desktop.Features;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Views;

/// <summary>
/// Hand-rolled view-model for the dashboard slice. No MVVM package is admitted (the packet
/// bans new package admissions; CommunityToolkit.Mvvm stays deferred to its own decision) —
/// this file is the whole framework. ONE toggle command path (A-004): the right-click
/// quick-toggle and the keyboard path both execute <see cref="ToggleCommand"/>, and the
/// card's lit ring derives from the operation authority, never the persisted flag.
/// </summary>
public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly StatusTickerParticipant _ticker;
    private readonly PersistenceStore<DemoSettings> _store;
    private readonly QuickToggleDispatch _quickToggle;
    private string _tickText = "ticker off";
    private string _cardTitle = "Demo: Status Ticker";

    public MainWindowViewModel(ApplicationHost host)
    {
        _ticker = host.Participants.OfType<StatusTickerParticipant>().First();
        _store = host.Participants.OfType<PersistenceStore<DemoSettings>>().First();
        // A-004/SP-014: the ONE dispatch entry — stable card ID → the toggle operation.
        // Neutral cards (Visuals/System parity) would simply have no entry (no fake cards).
        _quickToggle = new QuickToggleDispatch(
            new Dictionary<string, Action> { [CardId] = Toggle });
        ToggleCommand = new ToggleFeatureCommand(this);
        // SP-004 pattern: the reporter is invoked only on the UI thread, inside a boundary post.
        _ticker.TickReporter = text => TickText = text;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The single direct <see cref="ICommand"/> (cheat sheet §Commands — no RoutedCommand).</summary>
    public ICommand ToggleCommand { get; }

    /// <summary>
    /// The demonstrator card's STABLE dispatch identity — the only key quick-toggle resolves
    /// on. Never localized, never a display string: <see cref="CardTitle"/> is display text.
    /// </summary>
    public string CardId => StatusTickerParticipant.FeatureId;

    /// <summary>
    /// Display title bound by the card's title TextBlock. Mutable display text (rewording /
    /// future localization territory): mutating it must never affect dispatch — the
    /// title-mutation negative test proves it.
    /// </summary>
    public string CardTitle
    {
        get => _cardTitle;
        set
        {
            if (_cardTitle == value)
            {
                return;
            }

            _cardTitle = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardTitle)));
        }
    }

    /// <summary>The dispatch the ONE command path resolves through (public so tests assert resolution outcomes).</summary>
    public QuickToggleDispatch QuickToggle => _quickToggle;

    /// <summary>The card's lit ring: derives from the operation authority (<see cref="StatusTickerParticipant.IsOperationLive"/>).</summary>
    public bool TickerLit => _ticker.IsOperationLive;

    /// <summary>Load-bearing collapse: the tick text leaves layout when the operation is off.</summary>
    public bool TickerVisible => _ticker.IsOperationLive;

    public string TickText
    {
        get => _tickText;
        private set
        {
            if (_tickText == value)
            {
                return;
            }

            _tickText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TickText)));
        }
    }

    /// <summary>
    /// Toggle order (pre-approach consult): the operation first, so the ring responds from
    /// the owner immediately; then the flag mutation; then the serialized save — an SP-004
    /// owned completion, never awaited on the UI thread.
    /// </summary>
    internal void Toggle()
    {
        var target = !_ticker.IsOperationLive;
        _ticker.SetEnabled(target);
        _store.Mutate(s => s.StatusTickerEnabled = target);
        _ = _store.Save();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TickerLit)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TickerVisible)));
    }

    /// <summary>
    /// The dispatch key comes from the command parameter (the card's stable ID); null or
    /// unknown IDs no-op silently — WPF <c>else return</c> parity (Presets.cs:818).
    /// <see cref="CanExecuteChanged"/> is inert by contract.
    /// </summary>
    private sealed class ToggleFeatureCommand(MainWindowViewModel vm) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => vm._quickToggle.TryToggle(parameter as string);
    }
}
