using System.ComponentModel;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// A settings editor that lives for the whole app session and hooks
    /// <c>App.Settings.Current</c>'s <see cref="INotifyPropertyChanged"/>.
    ///
    /// <para><b>Why this exists.</b> <c>SettingsService.RestoreFrom</c> (cloud restore) and
    /// <c>SettingsService.Reset</c> do not mutate the settings object - they SWAP it for a different
    /// instance and raise <c>CurrentReplaced</c>. Anything that subscribed in its one-and-only
    /// <c>Loaded</c> is then listening to a discarded object: it shows pre-restore values forever,
    /// and its own <c>Unloaded</c> unsubscribe (which re-reads <c>App.Settings.Current</c>) detaches
    /// from the wrong instance. Before the UX restructure this was invisible, because every one of
    /// these panels was rebuilt per popup open; the Studio rack now mounts them permanently.</para>
    ///
    /// <para>Implementers re-point their hook at the current instance and repaint from it.
    /// <c>StudioTabView</c> fans this out over the rack on <c>CurrentReplaced</c>.</para>
    /// </summary>
    internal interface ISettingsRebindable
    {
        void RebindToCurrentSettings();
    }

    /// <summary>
    /// The tracked half of an <see cref="ISettingsRebindable"/> implementation: remembers WHICH
    /// AppSettings instance a handler is attached to, so the detach can target that object rather
    /// than whatever <c>App.Settings.Current</c> happens to be at the time.
    /// </summary>
    internal sealed class SettingsHook
    {
        private readonly PropertyChangedEventHandler _handler;
        private Models.AppSettings? _hooked;

        internal SettingsHook(PropertyChangedEventHandler handler) => _handler = handler;

        /// <summary>Detaches from the previously hooked instance and attaches to the current one.</summary>
        internal void Rebind()
        {
            Unhook();
            _hooked = App.Settings?.Current;
            if (_hooked != null) _hooked.PropertyChanged += _handler;
        }

        internal void Unhook()
        {
            if (_hooked != null) _hooked.PropertyChanged -= _handler;
            _hooked = null;
        }
    }
}
