using System;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Awareness;
using ConditioningControlPanel.Core.Services.Bark;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Services.Bark
{
    /// <summary>
    /// AvatarTube-backed <see cref="IBarkSpeaker"/> for the BARK-1 bark decision engine (slice 2).
    /// The engine decides WHAT to say and hands (line, audioPath, priority, mood, ctx) to
    /// <see cref="Speak"/> OUTSIDE its lock, on an arbitrary thread; this speaker marshals delivery
    /// to the UI thread and routes to the existing avatar speech surface, mirroring WPF Speak
    /// (Services/Companion/BarkService.cs:1578-1628):
    /// <list type="bullet">
    /// <item>{0} focused-app substitution (WPF :1635-1641) via the awareness seam + foreground-title
    /// provider, neutral fallback "that". {key} substitution is already applied by the engine — this
    /// path never re-substitutes ctx vars.</item>
    /// <item>Mute egg (WPF :1595): Class==EasterEgg + MasterVolume==0 → text-only Giggle, no audio.</item>
    /// <item>Priority routing (WPF :1619-1624): GigglePriority (clears queue, plays the resolved
    /// voiceline as the bark voice) vs queued Giggle. Bark lines are aiGenerated:false.</item>
    /// <item>Self-echo guard (WPF :1627): MuteKeywordEcho(line, 8000ms) after a delivered bark.</item>
    /// </list>
    /// Performs NO network/disk writes and logs no raw line text beyond what the avatar surface
    /// already logs for speech. Never throws out of Speak.
    /// </summary>
    public sealed class AvatarBarkSpeaker : IBarkSpeaker
    {
        private readonly IAvatarWindowService? _avatar;
        private readonly IAwarenessService? _awareness;
        private readonly IForegroundWindowTitleProvider? _titleProvider;
        private readonly IKeywordTriggerService? _keywords;
        private readonly ISettingsService? _settings;
        private readonly ILogger<AvatarBarkSpeaker>? _logger;

        public AvatarBarkSpeaker(
            IAvatarWindowService? avatar = null,
            IAwarenessService? awareness = null,
            IForegroundWindowTitleProvider? titleProvider = null,
            IKeywordTriggerService? keywords = null,
            ISettingsService? settings = null,
            ILogger<AvatarBarkSpeaker>? logger = null)
        {
            _avatar = avatar;
            _awareness = awareness;
            _titleProvider = titleProvider;
            _keywords = keywords;
            _settings = settings;
            _logger = logger;
        }

        /// <param name="mood">Authored mood tag. The port <see cref="IAvatarWindowService"/>
        /// Giggle/GigglePriority surface does not expose a mood parameter, so mood is not forwarded
        /// through the seam (residual — see slice-2 report). Unused here intentionally.</param>
#pragma warning disable CA1801 // unused parameter is part of the seam contract
        public void Speak(string line, string? audioPath, bool priority, string? mood, BarkContext ctx)
#pragma warning restore CA1801
        {
            if (string.IsNullOrWhiteSpace(line)) return; // WPF :1590

            // {0} focused-app substitution (WPF :1635-1641). {key} is already applied by the engine.
            var serviceName = _awareness?.CurrentServiceName;          // WPF :1637
            var detectedName = _awareness?.CurrentDetectedName;        // WPF :1638
            var foregroundTitle = SafeForegroundTitle();               // port-extra fallback
            var display = BarkSpeakPlanner.SubstituteFocusedApp(line, serviceName, detectedName, foregroundTitle);
            if (string.IsNullOrWhiteSpace(display)) return;

            // Mute-egg needs the rule class (WPF :1595); the engine stamps it into ctx.
            var ruleClass = TryGetClass(ctx);
            bool muted = ((_settings?.Current?.MasterVolume) ?? 0) == 0; // WPF :1594
            var kind = BarkSpeakPlanner.PlanDelivery(ruleClass, muted, priority);

            // Marshal to the UI thread: the engine raises on background/pool threads and the avatar
            // surface (DispatcherTimer + controls) requires the UI thread. WPF Speak marshals too.
            Dispatch(() =>
            {
                bool delivered = false;
                try { delivered = Deliver(display, audioPath, kind); }
                catch (Exception ex) { _logger?.LogWarning(ex, "AvatarBarkSpeaker: delivery failed"); }

                // Self-echo guard (WPF :1627) — only when the line actually reached the avatar
                // (WPF returns early on null avatar at :1582-1586 without muting). Thread-agnostic.
                if (delivered)
                {
                    try { _keywords?.MuteKeywordEcho(display, BarkSpeakPlanner.SelfEchoMuteMs); }
                    catch (Exception ex) { _logger?.LogWarning(ex, "AvatarBarkSpeaker: keyword-echo mute failed"); }
                }
            });
        }

        /// <returns>True when the line reached the avatar (so the self-echo guard should run).</returns>
        private bool Deliver(string display, string? audioPath, BarkDeliveryKind kind)
        {
            var avatar = _avatar;
            if (avatar == null)
            {
                _logger?.LogDebug("[BARK] no avatar window — dropping delivery"); // WPF :1582-1586
                return false;
            }

            switch (kind)
            {
                case BarkDeliveryKind.SilentMuteEgg:
                    // Text-only bubble, no voiceline. Master-muted → the tube's audio path makes no
                    // sound (WPF :1595-1599 comment: "PlayGiggleSound MasterVolume==0 guard keeps it
                    // from making sound"). Giggle on the port surface is text-only by design.
                    avatar.Giggle(display);
                    return true;

                case BarkDeliveryKind.GigglePriority:
                    // Preempt + clear the speech queue. The resolved voiceline plays as the bark
                    // voice via the tube's internal PlayBarkVoice (AvatarTubeWindow.axaml.cs:1318),
                    // matching WPF GigglePriority(... phraseAudioPath: audioPath, barkVoice: ...) :1620-1622.
                    avatar.GigglePriority(display,
                        playSound: audioPath == null,
                        aiGenerated: false,            // bark output must not anchor chat-suppression
                        phraseAudioPath: audioPath,
                        barkVoice: audioPath != null);
                    return true;

                default: // BarkDeliveryKind.Giggle — queued (WPF :1623-1624)
                    // The port IAvatarWindowService.Giggle surface is text-only (no phraseAudioPath);
                    // a non-priority bark's voiceline is therefore not played in the port (residual).
                    avatar.Giggle(display);
                    return true;
            }
        }

        private static BarkClass TryGetClass(BarkContext ctx)
        {
            if (ctx.Values.TryGetValue(BarkContext.RuleClassKey, out var raw) && raw is BarkClass c)
                return c;
            return BarkClass.Normal;
        }

        private string? SafeForegroundTitle()
        {
            // Privacy: the title is memory-only input for substitution, never persisted/logged.
            try { return _titleProvider?.GetForegroundWindowTitle(); }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "AvatarBarkSpeaker: foreground-title read failed");
                return null;
            }
        }

        private void Dispatch(Action action)
        {
            // Dispatcher.UIThread.Post is callable from any thread. If it is unreachable (no Avalonia
            // application spun up yet, e.g. an early spawn), run inline so a bark is never dropped.
            try
            {
                if (Dispatcher.UIThread.CheckAccess())
                    action();
                else
                    Dispatcher.UIThread.Post(action);
            }
            catch (Exception)
            {
                try { action(); } catch { /* never throw out of Speak */ }
            }
        }
    }
}
