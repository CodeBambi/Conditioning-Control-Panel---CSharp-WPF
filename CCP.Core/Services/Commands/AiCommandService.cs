using System;
using System.Collections.Generic;
using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;
using Serilog;

namespace ConditioningControlPanel.Services.Commands
{
    /// <summary>
    /// Central dispatcher for AI-emitted effect commands. Enforces the master toggle,
    /// per-effect toggles, and a per-AI-response command cap before delegating to
    /// <see cref="CommandFactory"/>. This is the only place where AI output translates
    /// into actual effect-service calls — no other code path should bypass it.
    /// </summary>
    public class AiCommandService : IAiCommandService
    {
        // Hard cap on commands executed per AI response, regardless of how many the AI
        // emits. Counter is reset by <see cref="BeginBatch"/>.
        public const int MaxCommandsPerResponse = 3;

        // Every in-flight command, keyed by its token or a synthetic key; panic / switch-off cancel them all.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> TokenCancellationSources = new();
        private int _batchCount;

        public void BeginBatch()
        {
            _batchCount = 0;
        }

        public async void ExecuteCommand(AiCommandData commandData)
        {
            if (commandData.Data == null) return;

            if (Refusal(commandData.Command) is { } refusal)
            {
                Log.Information("AiCommandService: {Reason} - dropping {Cmd}", refusal, commandData.Command);
                return;
            }

            // Per-batch cap.
            if (Interlocked.Increment(ref _batchCount) > MaxCommandsPerResponse)
            {
                Log.Information("AiCommandService: batch cap reached ({Cap}) — dropping {Cmd}",
                    MaxCommandsPerResponse, commandData.Command);
                return;
            }

            Log.Information("AiCommandService: dispatching {Cmd}", commandData.Command);

            // Surface a human-readable line in the AI Brain "Live actions" feed. This is the
            // request; a second line follows after execution if the effect did not fire.
            AppendLiveAction(FormatLiveAction(commandData));

            // Every command is cancellable (panic, switch-off); an untokened one gets a synthetic key.
            var token = commandData.Data.Token;
            if (string.IsNullOrEmpty(token)) token = "#" + Guid.NewGuid().ToString("N");
            else CancelToken(token);
            var cts = new CancellationTokenSource();
            TokenCancellationSources[token] = cts;

            try
            {
                var command = CommandFactory.CreateCommand(commandData, cts.Token, depth: 0);
                if (command == null)
                {
                    // Unreachable on WPF (the per-effect gate refuses every type the factory lacks);
                    // kept so a request line is never left standing as if it fired.
                    AppendLiveAction(FormatFailedAction(commandData));
                }
                else
                {
                    Log.Debug("AiCommandService: executing {Cmd}", commandData.Command);
                    var fired = await command.ExecuteAsync();
                    if (!fired)
                    {
                        // The line appended above is the request. Executors return false when
                        // the effect never happened (empty assets/audio folder, video already
                        // playing, feature off), so follow up instead of leaving a feed line
                        // that reads like it played (#1120).
                        Log.Warning("AiCommandService: {Cmd} did not fire", commandData.Command);
                        AppendLiveAction(FormatFailedAction(commandData));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AiCommandService: command {Cmd} threw", commandData.Command);
            }
            finally
            {
                RemoveToken(token, cts);
            }
        }

        public void CancelAllCommands() => CancelAll();

        /// <summary>Cancels every pending command: getbacktome delays and their remaining nested
        /// commands. Panic on every head and switching AI effects off call this.</summary>
        /// <summary>Bumped by every <see cref="CancelAll"/>, so a head can drop an effect start it had to defer.</summary>
        public static int CancelGeneration => Volatile.Read(ref _cancelGeneration);
        private static int _cancelGeneration;

        public static void CancelAll()
        {
            Interlocked.Increment(ref _cancelGeneration);
            foreach (var token in TokenCancellationSources.Keys) CancelToken(token);
        }

        /// <summary>The one gate every AI command passes at the moment it fires, nested getbacktome
        /// commands included: settings, the AI-effects switch AND live Lab access, then the effect's
        /// own toggle. Null = allowed; else a log-safe reason.</summary>
        public static string? Refusal(AICommandType command)
        {
            var settings = CoreSettings.Current?.CompanionPrompt;
            if (settings == null) return "no settings";
            if (!ConditioningControlPanel.Services.Companion.AiEffectControlGate.IsOn(settings, CoreAccount.HasLabAccess))
                return "master toggle off or no Tier 2";
            if (!IsEffectAllowed(command, settings)) return $"effect {command} disabled by user";
            return null;
        }

        /// <summary>A Live actions line for a refused follow-up command (no avatar bubble).</summary>
        internal static void NoteBlockedFollowUp(AICommandType command) => AppendLiveAction($"⛔ Follow-up {command} blocked");

        /// <summary>Last-N feed cap so the list doesn't grow forever in long sessions.</summary>
        public const int MaxLiveActions = 30;

        /// <summary>The AI Brain "Live actions" feed (WPF App.AiLiveActions, marshalled to its UI
        /// thread). Unseeded: the line is logged only, as on a head without that panel.</summary>
        public static volatile Action<string>? LiveActionSink;

        private static void AppendLiveAction(string line)
        {
            try { LiveActionSink?.Invoke(line); }
            catch (Exception ex) { Log.Debug("AiCommandService: live action feed failed: {Error}", ex.Message); }
        }

        /// <summary>
        /// Turns the parsed command + data into a single short line for the live feed.
        /// Reads the same data fields the executors use, so the user sees what actually
        /// fired (after caps are applied at execution time).
        /// </summary>
        private static string FormatLiveAction(AiCommandData c)
        {
            var d = c.Data;
            switch (c.Command)
            {
                case AICommandType.flash_image when d is FlashImage f:
                    return $"💥 Flash · {Math.Clamp(f.Amount, 0, 8)} images for {Math.Clamp(f.Duration, 0, 10)}s";
                case AICommandType.bubbles when d is Bubbles b:
                    var freq = Math.Clamp(b.Frequency, 0, 10);
                    return (b.On || freq > 0)
                        ? $"🫧 Bubbles started ({(freq > 0 ? freq : 5)}/min)"
                        : "🫧 Bubbles stopped";
                case AICommandType.subliminal when d is Subliminal s:
                    var t = (s.Text ?? "").Trim();
                    if (t.Length > 40) t = t.Substring(0, 40) + "…";
                    return $"👁️ Subliminal · \"{t}\"";
                case AICommandType.mantra_lockscreen when d is MantraLockscreen m:
                    var mt = (m.Mantra ?? "").Trim();
                    if (mt.Length > 30) mt = mt.Substring(0, 30) + "…";
                    return $"🔒 Lock card · \"{mt}\" ×{Math.Clamp(m.Amount, 0, 5)}";
                case AICommandType.spiral when d is SpiralPinkFiler sp:
                    return sp.On ? $"🌀 Spiral on ({Math.Clamp(sp.Intensity, 0, 30)}%)" : "🌀 Spiral off";
                case AICommandType.pink when d is SpiralPinkFiler pp:
                    return pp.On ? $"🩷 Pink filter on ({Math.Clamp(pp.Intensity, 0, 30)}%)" : "🩷 Pink filter off";
                case AICommandType.bounce when d is Bounce bn:
                    return bn.On ? "💃 Bouncing text on" : "💃 Bouncing text off";
                case AICommandType.haptic when d is HapticCommandData h:
                    var pct = (int)Math.Round(Math.Clamp(h.Intensity, 0, 1) * 100);
                    return $"📳 Vibrate · {pct}% for {Math.Clamp(h.Duration, 0, 10)}s";
                case AICommandType.video when d is Media vm:
                    var vt = string.IsNullOrEmpty(vm.Title) ? (vm.Path ?? "video") : vm.Title;
                    return $"🎬 Video · {vt}";
                case AICommandType.audio when d is Media am:
                    var at = string.IsNullOrEmpty(am.Title) ? (am.Path ?? "audio") : am.Title;
                    return $"🔊 Audio · {at}";
                case AICommandType.getbacktome when d is GetBackToMe g:
                    return $"⏱️ Follow-up in {Math.Clamp(g.Delay, 1, 600)}s";
                default:
                    return $"⚙️ {c.Command}";
            }
        }

        /// <summary>
        /// Follow-up feed line for a command whose executor reported failure, so the panel
        /// never leaves a request line standing as if the effect happened. Audio names the
        /// usual cause: there are no files in the assets audio folder (#1120).
        /// </summary>
        private static string FormatFailedAction(AiCommandData c)
        {
            return c.Command switch
            {
                AICommandType.audio => "🔇 Audio didn't play - check your assets audio folder",
                AICommandType.video => "🎬 Video didn't play",
                _ => $"⚠️ {c.Command} didn't fire"
            };
        }

        private static bool IsEffectAllowed(AICommandType cmd, Models.CompanionPromptSettings s)
        {
            return cmd switch
            {
                AICommandType.flash_image => s.AllowAiFlash,
                // Videos also require the main Videos feature toggle (#512) — gating here
                // (not just in MediaCommand) keeps blocked videos out of the Live actions feed.
                AICommandType.video => s.AllowAiVideo && CoreSettings.Current?.MandatoryVideosEnabled == true,
                AICommandType.audio => s.AllowAiAudio,
                AICommandType.bubbles => s.AllowAiBubbles,
                AICommandType.subliminal => s.AllowAiSubliminal,
                AICommandType.spiral => s.AllowAiOverlay,
                AICommandType.pink => s.AllowAiOverlay,
                AICommandType.mantra_lockscreen => s.AllowAiLockCard,
                AICommandType.bounce => s.AllowAiBounce,
                AICommandType.haptic => s.AllowAiHaptic,
                AICommandType.getbacktome => s.AllowAiGetBackToMe,
                AICommandType.none => false,
                _ => false
            };
        }

        private static void CancelToken(string token)
        {
            if (!TokenCancellationSources.TryRemove(token, out var cts)) return;
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
            finally { cts.Dispose(); }
        }

        private static void RemoveToken(string token, CancellationTokenSource mine)
        {
            // Only our own source: a newer command may have reused the token meanwhile.
            if (TokenCancellationSources.TryRemove(new KeyValuePair<string, CancellationTokenSource>(token, mine)))
                mine.Dispose();
        }
    }
}
