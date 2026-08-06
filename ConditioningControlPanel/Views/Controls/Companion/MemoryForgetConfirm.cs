using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// The two-step "Forget everything…" flow for Z3, in her voice.
    ///
    /// <para>Design §3 Z3: the wipe asks first — <i>"…all of it? even the good parts?"</i> with
    /// <i>[Yes, wipe it]</i> / <i>[No, keep us]</i>. It is deliberately NOT a modal dialog: the
    /// confirm is an inline strip that replaces the footer, so the surface stays a card and the
    /// tab never gets blocked (the Vault rule). That makes the flow a tiny state machine rather
    /// than a <c>MessageBox</c> call, which is the point — a state machine can be tested, and this
    /// one guards the single most destructive action on the page.</para>
    ///
    /// <para>Invariants held here:</para>
    /// <list type="bullet">
    ///   <item>the destructive command runs only from <see cref="ConfirmCommand"/>, and only while
    ///   armed — a stray click on a stale binding cannot wipe her memory;</item>
    ///   <item>confirming disarms first, so a double-click cannot fire the wipe twice;</item>
    ///   <item>re-binding (the card's viewmodel changed, or the view unloaded) always disarms, so
    ///   an armed confirm can never carry over onto a different companion's diary.</item>
    /// </list>
    /// </summary>
    public sealed class MemoryForgetConfirm : CompanionObservable
    {
        private ICommand? _target;
        private bool _isArmed;

        public MemoryForgetConfirm()
        {
            ArmCommand = new CompanionRelayCommand(Arm, () => CanArm);
            ConfirmCommand = new CompanionRelayCommand(Confirm, () => IsArmed);
            CancelCommand = new CompanionRelayCommand(Cancel);
        }

        /// <summary>True while the in-voice confirm strip is showing instead of the footer.</summary>
        public bool IsArmed
        {
            get => _isArmed;
            private set => Set(ref _isArmed, value);
        }

        /// <summary>There is something to wipe and it is willing to run.</summary>
        public bool CanArm => _target != null && _target.CanExecute(null);

        /// <summary>How many times the wipe actually ran. Diagnostics for the tests.</summary>
        public int ConfirmedCount { get; private set; }

        /// <summary>Shows the question.</summary>
        public ICommand ArmCommand { get; }
        /// <summary>"Yes, wipe it" — the only path that executes the destructive command.</summary>
        public ICommand ConfirmCommand { get; }
        /// <summary>"No, keep us".</summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Points the flow at a viewmodel's <c>ForgetEverythingCommand</c> (null unbinds). Always
        /// disarms: a confirm armed against the previous diary must not survive the swap.
        /// </summary>
        public void Bind(ICommand? forgetEverything)
        {
            _target = forgetEverything;
            IsArmed = false;
            Raise(nameof(CanArm));
        }

        /// <summary>Backs the flow out without running anything (the view calls this on unload).</summary>
        public void Disarm() => IsArmed = false;

        private void Arm()
        {
            if (!CanArm) return;
            IsArmed = true;
        }

        private void Cancel() => Disarm();

        private void Confirm()
        {
            if (!IsArmed) return;
            var target = _target;

            // Disarm before executing: the strip disappears on the first click, so a second one
            // lands on the restored footer instead of running the wipe again.
            IsArmed = false;
            if (target == null || !target.CanExecute(null)) return;

            ConfirmedCount++;
            target.Execute(null);
        }
    }
}
