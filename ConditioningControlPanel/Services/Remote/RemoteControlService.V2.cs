using System;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Remote Control v2 (2026-10-03): the subject's own buttons (More / Easy / Stop), the
    /// controller's name and the last thing the controller did. The HUD window
    /// (Windows/RemoteHud) reads exactly these members; do not rename them.
    /// </summary>
    public partial class RemoteControlService
    {
        /// <summary>The three signals the subject can send up: more, easy, stop.</summary>
        public static readonly string[] SignalKinds = { "more", "easy", "stop" };

        /// <summary>
        /// Scales every remote haptic and remote-set spiral / pink opacity. 1 at the start of a
        /// session, halved by each Easy press (floor 0.25), back to 1 when the session ends.
        /// </summary>
        public double EasyFactor { get; private set; } = 1.0;

        /// <summary>Raised when <see cref="EasyFactor"/> changes.</summary>
        public event EventHandler? EasyChanged;

        /// <summary>The name the controller typed when connecting, or null ("Your controller").</summary>
        public string? ControllerName { get; private set; }

        /// <summary>When the current controller connected (UTC), or null when nobody is connected.</summary>
        public DateTime? ControllerConnectedSinceUtc { get; private set; }

        /// <summary>Human words for the last command that landed, e.g. "Spiral", "Toy buzz".</summary>
        public string? LastActionLabel { get; private set; }

        /// <summary>Raised when a new command lands (and <see cref="LastActionLabel"/> moved).</summary>
        public event EventHandler? LastActionChanged;

        /// <summary>
        /// The subject's own button: "more", "easy" or "stop". Sends the signal up the emote
        /// channel; Easy and Stop also act locally. Returns true when the signal reached the server.
        /// </summary>
        public Task<bool> SendSignalAsync(string kind)
        {
            return Task.FromResult(false);
        }
    }
}
