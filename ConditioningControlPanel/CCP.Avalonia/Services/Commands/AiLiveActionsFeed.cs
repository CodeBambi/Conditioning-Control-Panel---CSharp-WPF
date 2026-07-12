using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Commands;

namespace ConditioningControlPanel.Avalonia.Services.Commands
{
    /// <summary>
    /// Avalonia implementation of <see cref="IAiLiveActionsFeed"/> — the bindable "Live actions"
    /// feed shown on the Companion tab. Backed by a singleton
    /// <see cref="ObservableCollection{T}"/> with a 30-item cap (oldest dropped) and UI-thread
    /// marshalling, mirroring the WPF static <c>App.AiLiveActions</c> collection plus
    /// <c>AppendLiveAction</c> in
    /// <c>ConditioningControlPanel/Services/Commands/AiCommandService.cs:111-133</c>.
    /// </summary>
    /// <remarks>
    /// <para>Registered as a DI singleton (<c>ServiceCollectionExtensions</c>) so the producer
    /// (<c>AiCommandService</c>) and the consumer (<c>CompanionTabViewModel</c>) share one
    /// collection instance — the port's DI equivalent of WPF's static
    /// <c>App.AiLiveActions</c>.</para>
    /// <para>AI commands may execute off the UI thread; <see cref="ObservableCollection{T}"/>
    /// mutation must happen on the UI thread or the bound <c>ItemsControl</c> throws, so both
    /// <see cref="Append"/> and <see cref="Clear"/> marshal via <see cref="Dispatcher.UIThread"/>
    /// exactly like WPF's <c>dispatcher.CheckAccess()/BeginInvoke</c> path.</para>
    /// </remarks>
    public sealed class AiLiveActionsFeed : IAiLiveActionsFeed
    {
        /// <summary>Last-N feed cap — WPF <c>AiCommandService.cs:111</c> <c>MaxLiveActions = 30</c>.</summary>
        private const int MaxLiveActions = 30;

        /// <inheritdoc/>
        public ObservableCollection<string> Items { get; } = new();

        /// <inheritdoc/>
        public void Append(string line)
        {
            // WPF AiCommandService.cs:120-132 — ObservableCollection mutation must run on the UI
            // thread. The AI command dispatcher can run off-thread, so marshal via Dispatcher.UIThread.
            void Apply()
            {
                // WPF AiCommandService.cs:126 — stamp [HH:mm:ss]; :127-128 add then trim to the cap.
                var stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
                Items.Add(stamped);
                while (Items.Count > MaxLiveActions) Items.RemoveAt(0);
            }

            if (Dispatcher.UIThread.CheckAccess()) Apply();
            else Dispatcher.UIThread.Post(Apply, DispatcherPriority.DataBind);
        }

        /// <inheritdoc/>
        public void Clear()
        {
            // WPF App.AiLiveActions.Clear() (e.g. MainWindow.Patreon.cs:1543 "forget everything").
            if (Dispatcher.UIThread.CheckAccess()) Items.Clear();
            else Dispatcher.UIThread.Post(() => Items.Clear(), DispatcherPriority.DataBind);
        }
    }
}
