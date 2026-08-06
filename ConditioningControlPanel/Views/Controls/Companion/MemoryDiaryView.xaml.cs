using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z3 — the memory diary. See the XAML header for the visual spec.
    ///
    /// <para>Almost code-free by design: filtering, sorting, the kind rails and the hover-reveal
    /// action row are all declarative (viewmodel projection + DataTemplate triggers). What is left
    /// here is the small amount of behaviour a diary needs to feel like one:</para>
    /// <list type="bullet">
    ///   <item><b>the wipe's confirm flow</b> — <see cref="ForgetConfirm"/>, an inline two-step in
    ///   her voice instead of a modal, so the card never blocks the tab;</item>
    ///   <item><b>inline edit ergonomics</b> — the box takes focus when it appears, Enter saves,
    ///   Esc backs out without touching the fact.</item>
    /// </list>
    ///
    /// <para>The diegetic bark on first open per session belongs to whoever owns the bark rules,
    /// and arrives with the wiring pass — not to this view.</para>
    /// </summary>
    public partial class MemoryDiaryView : UserControl
    {
        public MemoryDiaryView()
        {
            InitializeComponent();
            ForgetConfirm = new MemoryForgetConfirm();
            // The fact wall is height-capped and scrolls internally, so without this a wheel notch
            // over it never reaches the page — see CompanionWheelRelay.
            CompanionWheelRelay.Attach(FactWall);
            DataContextChanged += OnDataContextChanged;
            // Leaving the tab backs the question out; the binding itself survives so the button
            // still works when the user comes back.
            Unloaded += (_, _) => ForgetConfirm.Disarm();
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public IMemoryDiaryVm? ViewModel
        {
            get => DataContext as IMemoryDiaryVm;
            set => DataContext = value;
        }

        /// <summary>
        /// The "Forget everything…" two-step. Bound from the footer by name through the
        /// UserControl, so the destructive command has exactly one path to being executed.
        /// </summary>
        public MemoryForgetConfirm ForgetConfirm { get; }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Re-binding always disarms: a confirm armed against the previous diary must never
            // survive a companion switch.
            ForgetConfirm.Bind((e.NewValue as IMemoryDiaryVm)?.ForgetEverythingCommand);
        }

        // =====================================================================================
        //  inline edit
        // =====================================================================================

        /// <summary>Enter saves, Esc backs out. Esc leaves the fact exactly as it was.</summary>
        private void FactEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not IMemoryFactVm fact) return;

            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                if (fact.CommitEditCommand.CanExecute(null)) fact.CommitEditCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                fact.IsEditing = false;
                e.Handled = true;
            }
        }

        private void FactEditCancel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: IMemoryFactVm fact }) fact.IsEditing = false;
        }

        /// <summary>
        /// Puts the caret where the user is already looking. Deferred at Normal priority — the box
        /// is only in the tree once the trigger has run, and Loaded priority is starved in this app.
        /// </summary>
        private void FactEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not TextBox box) return;
            if (e.NewValue is not bool visible || !visible) return;

            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!box.IsVisible) return;
                    box.Focus();
                    box.SelectAll();
                }
                catch (InvalidOperationException)
                {
                    // The card was recycled out from under the focus call — harmless.
                }
            }), DispatcherPriority.Normal);
        }
    }
}
