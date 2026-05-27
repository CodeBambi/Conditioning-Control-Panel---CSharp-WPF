using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    /// <summary>
    /// Schedules a delayed AI follow-up: after <c>Delay</c> seconds, queries the AI again
    /// and optionally executes nested commands. Recursion is bounded by
    /// <see cref="CommandFactory.MaxGetBackToMeDepth"/> and per-command delay is capped.
    /// </summary>
    public class GetBackToMeCommand : ICommand
    {
        // Cap delay so AI can't schedule things hours from now.
        public const int MaxDelaySec = 600;

        private readonly GetBackToMe _data;
        private readonly CancellationToken _cancellationToken;
        private readonly int _depth;

        public GetBackToMeCommand(GetBackToMe data, CancellationToken cancellationToken, int depth = 0)
        {
            _data = data;
            _cancellationToken = cancellationToken;
            _depth = depth;
        }

        public async Task<bool> ExecuteAsync()
        {
            if (_depth >= CommandFactory.MaxGetBackToMeDepth)
            {
                App.Logger?.Information("GetBackToMeCommand: depth cap reached ({Depth}) — refusing further nesting", _depth);
                return false;
            }

            var delay = Math.Clamp(_data.Delay, 1, MaxDelaySec);

            try
            {
                App.Logger?.Debug("GetBackToMeCommand: scheduled in {Delay}s (depth={Depth}, token={Token})",
                    delay, _depth, _data.Token);
                await Task.Delay(delay * 1000, _cancellationToken);

                await SendTokenMessage(_data.Token, _data.JsonOnly, _data.Text);

                if (_data.Commands != null)
                {
                    foreach (var subCommand in _data.Commands)
                    {
                        if (_cancellationToken.IsCancellationRequested) break;
                        var cmd = CommandFactory.CreateCommand(subCommand, _cancellationToken, _depth + 1);
                        if (cmd != null)
                        {
                            await cmd.ExecuteAsync();
                        }
                    }
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "GetBackToMeCommand failed");
                return false;
            }
        }

        private async Task SendTokenMessage(string token, bool jsonOnly, string? text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                // Operator-supplied text — not AI-generated, so no AI badge.
                ShowAvatarMessage(text, aiGenerated: false);
            }
            // R2-NEW-H-1: migrate to typed AI API. Refusals are silently dropped on
            // this remote-control surface (the local user didn't directly prompt — a
            // POLICY bubble would surprise them). Downstream guard already logged via
            // ModerationLog. IsAiGenerated propagates so canned fallbacks don't wear
            // the AI badge.
            var result = await App.Ai.GetBambiReplyExAsync($"[Token={token}, JsonOnly={jsonOnly}]");
            if (!jsonOnly && result.Refusal == null && !string.IsNullOrEmpty(result.Text))
            {
                ShowAvatarMessage(result.Text, aiGenerated: result.IsAiGenerated);
            }
        }

        private static void ShowAvatarMessage(string text, bool aiGenerated = false)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                try { App.AvatarWindow?.GigglePriority(text, playSound: true, aiGenerated: aiGenerated); }
                catch (Exception ex) { App.Logger?.Debug("GetBackToMeCommand: avatar speak failed: {Error}", ex.Message); }
            }));
        }
    }
}
