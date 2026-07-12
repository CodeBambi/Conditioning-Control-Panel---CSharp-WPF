using System.Collections.ObjectModel;

namespace ConditioningControlPanel.Core.Services.Commands
{
    /// <summary>
    /// Live, bindable feed of recent AI-driven effect actions, surfaced in the Companion tab's
    /// "Live actions" panel. Portable port of the WPF static <c>App.AiLiveActions</c>
    /// collection plus the append logic in
    /// <c>ConditioningControlPanel/Services/Commands/AiCommandService.cs:111-133</c>
    /// (<c>MaxLiveActions=30</c> cap, oldest dropped, UI-thread marshalled).
    /// </summary>
    /// <remarks>
    /// <para>The feed stores only the ACTION description (e.g. "Flash · 5 images for 10s",
    /// "Video · &lt;title&gt;"), never the AI prompt, raw command JSON, or any secret — parity
    /// with WPF <c>FormatLiveAction</c> (<c>AiCommandService.cs:140-180</c>). Producers call
    /// <see cref="Append"/> with a pre-formatted line (see
    /// <see cref="AiLiveActionFormatter"/>); consumers bind <see cref="Items"/>.</para>
    /// <para>Mutation members (<see cref="Append"/>, <see cref="Clear"/>) carry safe no-op
    /// default implementations so minimal test fakes only need to supply <see cref="Items"/>.</para>
    /// </remarks>
    public interface IAiLiveActionsFeed
    {
        /// <summary>
        /// The observable, bindable list of stamped action lines (newest last). Implementations
        /// cap this at 30 entries (WPF <c>AiCommandService.cs:111</c> <c>MaxLiveActions</c>).
        /// </summary>
        ObservableCollection<string> Items { get; }

        /// <summary>
        /// Stamps the line with the current time (<c>[HH:mm:ss]</c>) and appends it, dropping the
        /// oldest entry while the list exceeds the 30-item cap. Implementations marshal to the UI
        /// thread. Mirrors WPF <c>AppendLiveAction</c> (<c>AiCommandService.cs:118-133</c>).
        /// Default implementation is a safe no-op.
        /// </summary>
        void Append(string line) { }

        /// <summary>
        /// Removes every entry. Mirrors WPF <c>App.AiLiveActions.Clear()</c> (e.g.
        /// <c>MainWindow.Patreon.cs:1543</c>, the "forget everything" reset). Default
        /// implementation is a safe no-op.
        /// </summary>
        void Clear() { }
    }
}
