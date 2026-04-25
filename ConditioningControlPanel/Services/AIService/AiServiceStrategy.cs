using System;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// Routes <see cref="IAiService"/> calls to either the cloud-proxy <see cref="AiService"/>
    /// or the local Ollama-backed <see cref="LocalAiService"/> based on
    /// <c>App.Settings.Current.CompanionPrompt.UseLocalAi</c>. Provider switching is live —
    /// no app restart required. Each provider is constructed lazily on first use.
    /// </summary>
    public class AiServiceStrategy : IAiService
    {
        private readonly object _lock = new();
        private AiService? _cloud;
        private LocalAiService? _local;

        private static bool UseLocal =>
            App.Settings?.Current?.CompanionPrompt?.UseLocalAi == true;

        private IAiService Active
        {
            get
            {
                if (UseLocal)
                {
                    if (_local == null)
                    {
                        lock (_lock)
                        {
                            _local ??= new LocalAiService();
                        }
                    }
                    return _local;
                }
                else
                {
                    if (_cloud == null)
                    {
                        lock (_lock)
                        {
                            _cloud ??= new AiService();
                        }
                    }
                    return _cloud;
                }
            }
        }

        public bool IsAvailable => Active.IsAvailable;

        public int DailyRequestsRemaining => Active.DailyRequestsRemaining;

        public Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false)
            => Active.GetBambiReplyAsync(userInput, isUserMessage);

        public Task<string?> GetAwarenessReactionAsync(string detectedName, string category,
            string serviceName = "", string pageTitle = "")
            => Active.GetAwarenessReactionAsync(detectedName, category, serviceName, pageTitle);

        public Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
            => Active.GetStillOnReactionAsync(displayName, category, duration);

        public Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
            => Active.GetKeywordCommentAsync(keyword, promptTemplate);

        public Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null)
            => Active.GetLockScreenReaction(sentance, mistakes, amount, promptTemplate);

        public Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
            => Active.GetVideoDoneReaction(title, promptTemplate);

        public void Dispose()
        {
            _cloud?.Dispose();
            _local?.Dispose();
        }
    }
}
