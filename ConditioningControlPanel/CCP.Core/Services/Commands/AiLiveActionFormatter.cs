using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;
using SubliminalCmdData = ConditioningControlPanel.Models.CommandData.Subliminal;

namespace ConditioningControlPanel.Core.Services.Commands
{
    /// <summary>
    /// Turns a parsed AI command + data into a single short, user-readable line for the
    /// Companion tab's "Live actions" feed. Verbatim port of WPF
    /// <c>ConditioningControlPanel/Services/Commands/AiCommandService.cs:140-180</c>
    /// (<c>FormatLiveAction</c>), including the per-type <see cref="Math.Clamp"/> bounds and the
    /// emoji prefixes.
    /// </summary>
    /// <remarks>
    /// <para><b>Privacy contract</b>: the output describes the ACTION only (e.g. "Flash · 5 images
    /// for 10s", "Video · &lt;title&gt;") and never the AI prompt, raw command JSON, or any secret.
    /// This is an exact mirror of WPF <c>FormatLiveAction</c> so the port shows the same text a
    /// WPF user sees.</para>
    /// <para>Extracted as a pure static so it is unit-testable from <c>CCP.Core.Tests</c> and
    /// callable from any head's AI command dispatcher without re-implementing the per-type
    /// strings.</para>
    /// </remarks>
    public static class AiLiveActionFormatter
    {
        /// <summary>
        /// Formats the command into a single feed line. Returns a generic "⚙️ &lt;command&gt;"
        /// line for unknown command types or null/mismatched data payloads, mirroring the WPF
        /// <c>default</c> arm (<c>AiCommandService.cs:177-178</c>).
        /// </summary>
        public static string Format(AiCommandData c)
        {
            var d = c.Data;
            switch (c.Command)
            {
                case AICommandType.flash_image when d is FlashImage f:
                    // WPF AiCommandService.cs:145-146 — clamp Amount(0-8) / Duration(0-10s).
                    return $"💥 Flash · {Math.Clamp(f.Amount, 0, 8)} images for {Math.Clamp(f.Duration, 0, 10)}s";

                case AICommandType.bubbles when d is Bubbles b:
                    // WPF AiCommandService.cs:147-151 — "started" when On or freq>0 (default 5/min), else "stopped".
                    var freq = Math.Clamp(b.Frequency, 0, 10);
                    return (b.On || freq > 0)
                        ? $"🫧 Bubbles started ({(freq > 0 ? freq : 5)}/min)"
                        : "🫧 Bubbles stopped";

                case AICommandType.subliminal when d is SubliminalCmdData s:
                    // WPF AiCommandService.cs:152-155 — trim text, cap at 40 chars + ellipsis.
                    var t = (s.Text ?? "").Trim();
                    if (t.Length > 40) t = t.Substring(0, 40) + "…";
                    return $"👁️ Subliminal · \"{t}\"";

                case AICommandType.mantra_lockscreen when d is MantraLockscreen m:
                    // WPF AiCommandService.cs:156-159 — trim mantra, cap at 30 chars + ellipsis; clamp Amount(0-5).
                    var mt = (m.Mantra ?? "").Trim();
                    if (mt.Length > 30) mt = mt.Substring(0, 30) + "…";
                    return $"🔒 Lock card · \"{mt}\" ×{Math.Clamp(m.Amount, 0, 5)}";

                case AICommandType.spiral when d is SpiralPinkFiler sp:
                    // WPF AiCommandService.cs:160-161 — on/off with clamped Intensity(0-30)%.
                    return sp.On ? $"🌀 Spiral on ({Math.Clamp(sp.Intensity, 0, 30)}%)" : "🌀 Spiral off";

                case AICommandType.pink when d is SpiralPinkFiler pp:
                    // WPF AiCommandService.cs:162-163 — on/off with clamped Intensity(0-30)%.
                    return pp.On ? $"🩷 Pink filter on ({Math.Clamp(pp.Intensity, 0, 30)}%)" : "🩷 Pink filter off";

                case AICommandType.bounce when d is Bounce bn:
                    // WPF AiCommandService.cs:164-165 — on/off only.
                    return bn.On ? "💃 Bouncing text on" : "💃 Bouncing text off";

                case AICommandType.haptic when d is HapticCommandData h:
                    // WPF AiCommandService.cs:166-168 — intensity 0-1 -> pct, clamp Duration(0-10)s.
                    var pct = (int)Math.Round(Math.Clamp(h.Intensity, 0, 1) * 100);
                    return $"📳 Vibrate · {pct}% for {Math.Clamp(h.Duration, 0, 10)}s";

                case AICommandType.video when d is Media vm:
                    // WPF AiCommandService.cs:169-171 — prefer Title, fall back to Path, then "video".
                    var vt = string.IsNullOrEmpty(vm.Title) ? (vm.Path ?? "video") : vm.Title;
                    return $"🎬 Video · {vt}";

                case AICommandType.audio when d is Media am:
                    // WPF AiCommandService.cs:172-174 — prefer Title, fall back to Path, then "audio".
                    var at = string.IsNullOrEmpty(am.Title) ? (am.Path ?? "audio") : am.Title;
                    return $"🔊 Audio · {at}";

                case AICommandType.getbacktome when d is GetBackToMe g:
                    // WPF AiCommandService.cs:175-176 — clamp Delay(1-600)s.
                    return $"⏱️ Follow-up in {Math.Clamp(g.Delay, 1, 600)}s";

                default:
                    // WPF AiCommandService.cs:177-178 — generic fallback (also covers null/mismatched data).
                    return $"⚙️ {c.Command}";
            }
        }
    }
}
