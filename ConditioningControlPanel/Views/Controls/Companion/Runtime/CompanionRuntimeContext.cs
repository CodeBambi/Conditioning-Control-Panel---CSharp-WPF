using System;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// What every wired-up zone viewmodel needs from the page it lives on: the window whose
    /// handlers still own the dialogs, and the navigator that lets one zone deep-link into another.
    ///
    /// <para><b>Why the window comes through a delegate.</b> The room is built by
    /// <c>CompanionTabView</c> in its constructor, which runs while the tab UserControl is still
    /// being parsed — <c>Window.GetWindow(this)</c> is null at that point and stays null until the
    /// tab is in MainWindow's tree. Capturing it once would give every command a permanent null.
    /// Asking for it per invocation also means the room survives the window being replaced, which is
    /// what the preview harness and the tests do.</para>
    ///
    /// <para><b>Why it routes back to MainWindow at all.</b> The dialogs these commands open —
    /// the prompt editor, the Ollama wizard, the sampler sheet, the trigger editor — are MainWindow
    /// methods with a decade of behaviour in them (moderation gates, explicit-content
    /// acknowledgement, settings saves). The redesign moves where they are ASKED FOR, not what they
    /// do. Re-implementing any of them here would be how the swap loses a gate.</para>
    /// </summary>
    internal sealed class CompanionRuntimeContext
    {
        private readonly Func<MainWindow?> _window;

        public CompanionRuntimeContext(Func<MainWindow?> window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
        }

        /// <summary>The host window, or null before the tab is in a tree (and after it leaves one).</summary>
        public MainWindow? Window
        {
            get
            {
                try { return _window(); }
                catch (Exception) { return null; }
            }
        }

        /// <summary>The page's cross-zone navigator. Set by the room viewmodel when the view claims it.</summary>
        public ICompanionRoomNavigator? Navigator { get; set; }

        /// <summary>
        /// Runs <paramref name="action"/> against the window when there is one. A command fired
        /// before the tab is in a tree does nothing rather than throwing — this is a UI page, and a
        /// click that arrives one dispatcher turn early may never be the reason the app dies.
        /// </summary>
        public void WithWindow(Action<MainWindow> action)
        {
            var window = Window;
            if (window == null) return;
            try
            {
                action(window);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Companion room: command failed");
            }
        }

        /// <summary>Runs a piece of work that touches no window, with the same never-throw contract.</summary>
        public static void Guarded(Action action, string what)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Companion room: {What} failed", what);
            }
        }

        /// <summary>An empty <see cref="System.Windows.RoutedEventArgs"/> for forwarding to a legacy handler.</summary>
        public static System.Windows.RoutedEventArgs Routed() => new();
    }
}
