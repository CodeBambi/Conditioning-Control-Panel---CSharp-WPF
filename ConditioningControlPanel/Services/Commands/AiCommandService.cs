using System;
using System.Collections.Generic;
using System.Threading;
using ConditioningControlPanel.Models;

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

            App.Logger?.Information("AiCommandService: dispatching {Cmd}", commandData.Command);

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
