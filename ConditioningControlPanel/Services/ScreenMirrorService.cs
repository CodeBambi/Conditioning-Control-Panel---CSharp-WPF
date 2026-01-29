using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Service to switch Windows display mode between Extend and Clone/Duplicate.
    /// Used to mirror primary screen content to all monitors during fullscreen video playback.
    /// </summary>
    public class ScreenMirrorService : IDisposable
    {
        private bool _isMirroring = false;
        private bool _disposed = false;

        // Display topology constants
        private const uint SDC_TOPOLOGY_CLONE = 0x00000002;
        private const uint SDC_TOPOLOGY_EXTEND = 0x00000004;
        private const uint SDC_APPLY = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(
            uint numPathArrayElements,
            IntPtr pathArray,
            uint numModeInfoArrayElements,
            IntPtr modeInfoArray,
            uint flags);

        public bool IsMirroring => _isMirroring;

        /// <summary>
        /// Switch to Clone/Duplicate display mode.
        /// All monitors will show the same content as the primary monitor.
        /// Note: This may fail on 3+ monitor setups, mixed GPU configurations,
        /// or monitors with incompatible resolutions.
        /// </summary>
        public bool EnableMirror()
        {
            if (_isMirroring) return true;

            try
            {
                // Log screen configuration for debugging
                var screens = App.GetAllScreensCached();
                App.Logger?.Debug("ScreenMirror: Attempting clone on {Count} screens: {Screens}",
                    screens.Length,
                    string.Join(", ", screens.Select(s => $"{s.DeviceName} ({s.Bounds.Width}x{s.Bounds.Height})")));

                var result = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, SDC_TOPOLOGY_CLONE | SDC_APPLY);

                if (result == 0) // ERROR_SUCCESS
                {
                    _isMirroring = true;
                    App.Logger?.Information("ScreenMirror: Enabled clone/duplicate display mode");
                    return true;
                }
                else
                {
                    // Provide helpful error descriptions
                    var errorDesc = result switch
                    {
                        50 => "NOT_SUPPORTED - Clone mode not available (3+ monitors or mixed GPUs?)",
                        5 => "ACCESS_DENIED - Insufficient permissions",
                        31 => "GEN_FAILURE - Display configuration incompatible",
                        87 => "INVALID_PARAMETER - Invalid display topology",
                        _ => $"Unknown error"
                    };
                    App.Logger?.Warning("ScreenMirror: Failed to enable clone mode - {Error} (code: {Code})", errorDesc, result);
                    return false;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "ScreenMirror: Exception enabling clone mode");
                return false;
            }
        }

        /// <summary>
        /// Switch back to Extend display mode.
        /// Each monitor will show independent content.
        /// </summary>
        public bool DisableMirror()
        {
            if (!_isMirroring) return true;

            try
            {
                var result = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, SDC_TOPOLOGY_EXTEND | SDC_APPLY);

                if (result == 0) // ERROR_SUCCESS
                {
                    _isMirroring = false;
                    App.Logger?.Information("ScreenMirror: Restored extend display mode");
                    return true;
                }
                else
                {
                    App.Logger?.Warning("ScreenMirror: Failed to restore extend mode, error code: {Code}", result);
                    return false;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "ScreenMirror: Exception restoring extend mode");
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Always restore extend mode on dispose
            if (_isMirroring)
            {
                DisableMirror();
            }
        }
    }
}
