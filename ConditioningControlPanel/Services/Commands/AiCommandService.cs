using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;

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

        private static readonly Dictionary<string, CancellationTokenSource> TokenCancellationSources = new();
        private int _batchCount;

        public void BeginBatch()
        {
            _batchCount = 0;
        }

        public async void ExecuteCommand(AiCommandData commandData)
        {
            if (commandData.Data == null) return;

            var settings = App.Settings?.Current?.CompanionPrompt;
            if (settings == null)
            {
                App.Logger?.Debug("AiCommandService: no settings — dropping command {Cmd}", commandData.Command);
                return;
            }

            // Master gate.
            if (!settings.AllowAiToControlEffects)
            {
                App.Logger?.Information("AiCommandService: master toggle OFF — dropping {Cmd}", commandData.Command);
                return;
            }

            // Per-effect gate.
            if (!IsEffectAllowed(commandData.Command, settings))
            {
                App.Logger?.Information("AiCommandService: effect {Cmd} disabled by user — dropping", commandData.Command);
                return;
            }

            // Per-batch cap.
            if (Interlocked.Increment(ref _batchCount) > MaxCommandsPerResponse)
            {
                App.Logger?.Information("AiCommandService: batch cap reached ({Cap}) — dropping {Cmd}",
                    MaxCommandsPerResponse, commandData.Command);
                return;
            }

            App.Logger?.Information("AiCommandService: dispatching {Cmd} with data {@Data}",
                commandData.Command, commandData.Data);

            // Surface a human-readable line in the AI Brain "Live actions" feed.
            AppendLiveAction(FormatLiveAction(commandData));

            var token = commandData.Data.Token;
            CancellationTokenSource? cts = null;

            if (!string.IsNullOrEmpty(token))
            {
                CancelToken(token);
                cts = new CancellationTokenSource();
                TokenCancellationSources[token] = cts;
            }

            try
            {
                var command = CommandFactory.CreateCommand(commandData, cts?.Token ?? default, depth: 0);
                if (command != null)
                {
                    App.Logger?.Debug("AiCommandService: executing {Cmd}", commandData.Command);
                    await command.ExecuteAsync();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "AiCommandService: command {Cmd} threw", commandData.Command);
            }
            finally
            {
                if (!string.IsNullOrEmpty(token))
                {
                    RemoveToken(token);
                }
            }
        }

        public void CancelAllCommands()
        {
            var tokens = new List<string>(TokenCancellationSources.Keys);
            foreach (var token in tokens)
            {
                CancelToken(token);
            }
        }

        // Last-N feed cap so the list doesn't grow forever in long sessions.
        private const int MaxLiveActions = 30;

        /// <summary>
        /// Appends a user-readable line to <see cref="App.AiLiveActions"/> (bound to the
        /// AI Brain "Live actions" panel on the Companion tab). Marshals to the UI
        /// thread because ObservableCollection updates aren't allowed off-thread.
        /// </summary>
        private static void AppendLiveAction(string line)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            void Apply()
            {
                var list = App.AiLiveActions;
                var stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
                list.Add(stamped);
                while (list.Count > MaxLiveActions) list.RemoveAt(0);
            }

            if (dispatcher.CheckAccess()) Apply();
            else dispatcher.BeginInvoke((Action)Apply);
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

        private static bool IsEffectAllowed(AICommandType cmd, Models.CompanionPromptSettings s)
        {
            return cmd switch
            {
                AICommandType.flash_image => s.AllowAiFlash,
                AICommandType.video => s.AllowAiVideo,
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
            if (TokenCancellationSources.TryGetValue(token, out var cts))
            {
                try { cts.Cancel(); }
                finally
                {
                    cts.Dispose();
                    TokenCancellationSources.Remove(token);
                }
            }
        }

        private static void RemoveToken(string token)
        {
            if (TokenCancellationSources.TryGetValue(token, out var cts))
            {
                cts.Dispose();
                TokenCancellationSources.Remove(token);
            }
        }
    }
}
