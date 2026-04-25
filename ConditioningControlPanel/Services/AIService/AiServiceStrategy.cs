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

        /// <summary>
        /// Clears the persisted local-AI conversation memory (in-memory + on-disk).
        /// No-op for the cloud provider (it's stateless). Safe to call even when
        /// <see cref="LocalAiService"/> hasn't been constructed yet — we still try
        /// to delete the file so a fresh local provider starts blank.
        /// </summary>
        public void ClearLocalHistory()
        {
            // Construct the local instance if needed only to reach the clear method —
            // alternative is to duplicate the file path here. Cheaper to instantiate.
            lock (_lock)
            {
                _local ??= new LocalAiService();
            }
            _local.ClearHistory();
        }

        /// <summary>
        /// Pre-loads the configured Ollama model into memory at startup so the first
        /// chat doesn't pay the cold-start cost. Only runs if the user has local AI
        /// selected — for cloud users this is a no-op. Best-effort, fire-and-forget.
        /// </summary>
        public Task WarmUpLocalAsync()
        {
            if (!UseLocal) return Task.CompletedTask;

            lock (_lock)
            {
                _local ??= new LocalAiService();
            }
            return _local.WarmUpAsync();
        }
    }
}
