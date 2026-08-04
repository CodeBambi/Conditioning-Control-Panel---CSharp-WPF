using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics.Core;

namespace ConditioningControlPanel.Services.Haptics
{
    /// <summary>
    /// PHASE F — TWO-WAY TOY INPUT.
    ///
    /// Turns the raw <see cref="HapticDeviceManager.ToyEvent"/> stream (Lovense Toy Events WS;
    /// Buttplug button features) into something the app can consume:
    ///  - <see cref="ButtonPressed"/>: debounced "the user squeezed a toy" for any connected toy.
    ///  - <see cref="WaitForButtonAsync"/>: awaitable one-shot with a timeout, for confirm prompts.
    ///  - user-override back-off: a <see cref="ToyEventKind.StrengthChanged"/> means the user
    ///    reached for the toy themselves, so the mixer's CONTINUOUS layers step aside for
    ///    <see cref="HapticSettings.UserOverrideCooldownSec"/> seconds instead of fighting them.
    ///
    /// Owned and constructed by <see cref="Services.HapticService"/> (App.xaml.cs is not touched by
    /// Phase F — see docs/HAPTICS_OVERHAUL_PLAN.md). Providers raise events from IO threads, so
    /// every consumer callback is marshalled to the dispatcher when there is one.
    /// </summary>
    public sealed class ToyInputService : IDisposable
    {
        /// <summary>Buttons bounce, and Lovense sends button-down + button-pressed for one squeeze.
        /// One logical press per this window.</summary>
        public const int DebounceMs = 350;

        private readonly HapticDeviceManager _devices;
        private readonly HapticMixer _mixer;
        private readonly HapticSettings _settings;

        private readonly object _gate = new();
        private readonly List<TaskCompletionSource<bool>> _waiters = new();
        private long _lastPressAt;
        private long _lastStrengthAt;
        private bool _disposed;

        /// <summary>A toy button was pressed (debounced). Raised on the UI dispatcher when one
        /// exists, so handlers may touch WPF directly.</summary>
        public event EventHandler<HapticToyEvent>? ButtonPressed;

        /// <summary>The user changed the strength on the toy itself. Raised after the mixer
        /// back-off has already been applied.</summary>
        public event EventHandler<HapticToyEvent>? UserOverrode;

        /// <summary>UTC time of the last debounced button press, or null if none this session.</summary>
        public DateTime? LastPressUtc { get; private set; }

        /// <summary>True when at least one connected device belongs to a provider that can raise
        /// input events at all. Cheap: consumers use it to skip their own wiring.</summary>
        public bool IsAvailable
        {
            get
            {
                try
                {
                    foreach (var d in _devices.Devices)
                        if (d.IsConnected && d.Enabled) return true;
                }
                catch { }
                return false;
            }
        }

        public ToyInputService(HapticDeviceManager devices, HapticMixer mixer, HapticSettings settings)
        {
            _devices = devices;
            _mixer = mixer;
            _settings = settings;
            _devices.ToyEvent += OnToyEvent;
        }

        // ===================================================================== ingest

        private void OnToyEvent(object? sender, HapticToyEvent e)
        {
            if (_disposed || e == null) return;
            try
            {
                if (!_settings.ToyInputEnabled) return;

                switch (e.Kind)
                {
                    case ToyEventKind.ButtonPressed:
                    case ToyEventKind.ButtonDown:
                        HandlePress(e);
                        break;

                    case ToyEventKind.StrengthChanged:
                        HandleStrengthChanged(e);
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ToyInputService: event handling failed (non-fatal): {E}", ex.Message);
            }
        }

        private void HandlePress(HapticToyEvent e)
        {
            var now = Environment.TickCount64;
            List<TaskCompletionSource<bool>>? waiters = null;
            lock (_gate)
            {
                // ButtonDown + ButtonPressed for one squeeze must not count twice.
                if (now - _lastPressAt < DebounceMs) return;
                _lastPressAt = now;
                if (_waiters.Count > 0)
                {
                    waiters = new List<TaskCompletionSource<bool>>(_waiters);
                    _waiters.Clear();
                }
            }

            LastPressUtc = DateTime.UtcNow;
            App.Logger?.Debug("ToyInput: button press from {Device}", e.DeviceKey);

            if (waiters != null)
                foreach (var w in waiters) { try { w.TrySetResult(true); } catch { } }

            RaiseOnDispatcher(ButtonPressed, e);
        }

        private void HandleStrengthChanged(HapticToyEvent e)
        {
            var cooldown = _settings.UserOverrideCooldownSec;
            if (cooldown <= 0) return;

            var now = Environment.TickCount64;
            bool announce;
            lock (_gate)
            {
                // A dial sweep is a burst of events; extend the back-off on every one of them but
                // only log/notify once a second.
                announce = now - _lastStrengthAt >= 1000;
                if (announce) _lastStrengthAt = now;
            }

            // Never call into the mixer while holding _gate (it takes its own lock).
            _mixer.SuppressLayersUntil(DateTime.UtcNow.AddSeconds(cooldown));
            if (!announce) return;

            App.Logger?.Information(
                "ToyInput: user changed strength on {Device} — backing continuous haptic layers off for {Seconds}s",
                e.DeviceKey, cooldown);

            RaiseOnDispatcher(UserOverrode, e);
        }

        /// <summary>Provider events arrive on IO threads; consumers (attention checks, prompts)
        /// touch WPF. Marshal when a dispatcher exists, run inline when one does not (tests).</summary>
        private void RaiseOnDispatcher(EventHandler<HapticToyEvent>? handler, HapticToyEvent e)
        {
            if (handler == null) return;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                try { handler(this, e); } catch { }
                return;
            }

            try
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    try { handler(this, e); }
                    catch (Exception ex) { App.Logger?.Debug("ToyInput: subscriber threw: {E}", ex.Message); }
                }));
            }
            catch { }
        }

        // ===================================================================== await

        /// <summary>
        /// Wait for the next debounced toy-button press. Returns false on timeout, on cancellation,
        /// or immediately when toy input is disabled — callers can therefore always offer the
        /// normal (mouse/keyboard) path alongside it without a null check.
        /// </summary>
        public async Task<bool> WaitForButtonAsync(int timeoutMs, CancellationToken ct = default)
        {
            if (_disposed || !_settings.ToyInputEnabled || timeoutMs <= 0) return false;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate) _waiters.Add(tcs);

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var delay = Task.Delay(timeoutMs, timeoutCts.Token);
                var winner = await Task.WhenAny(tcs.Task, delay).ConfigureAwait(false);
                if (winner == tcs.Task)
                {
                    timeoutCts.Cancel();          // release the timer immediately
                    try { await delay.ConfigureAwait(false); } catch { }
                    return true;
                }
                return false;
            }
            catch (OperationCanceledException) { return false; }
            finally
            {
                lock (_gate) _waiters.Remove(tcs);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _devices.ToyEvent -= OnToyEvent; } catch { }

            List<TaskCompletionSource<bool>> waiters;
            lock (_gate)
            {
                waiters = new List<TaskCompletionSource<bool>>(_waiters);
                _waiters.Clear();
            }
            foreach (var w in waiters) { try { w.TrySetResult(false); } catch { } }
        }
    }
}
