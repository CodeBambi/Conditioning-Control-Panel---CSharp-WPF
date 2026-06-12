using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Moderation;

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
        private OpenAiCompatibleService? _openAi;

        private static AiProviderType Provider =>
            App.Settings?.Current?.CompanionPrompt?.AiProvider ?? AiProviderType.Cloud;

        private IAiService Active
        {
            get
            {
                switch (Provider)
                {
                    case AiProviderType.Local:
                        if (_local == null)
                        {
                            lock (_lock)
                            {
                                _local ??= new LocalAiService();
                            }
                        }
                        return _local;

                    case AiProviderType.OpenAiCompatible:
                        if (_openAi == null)
                        {
                            lock (_lock)
                            {
                                _openAi ??= new OpenAiCompatibleService();
                            }
                        }
                        return _openAi;

                    case AiProviderType.Cloud:
                    default:
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

        public Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false)
            => Active.GetBambiReplyExAsync(userInput, isUserMessage);

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
            _openAi?.Dispose();
        }

        /// <summary>
        /// Clears persisted chat memory for all providers (local + OpenAI-compatible).
        /// Cloud provider is stateless and has no client-side history. Safe to call even
        /// when providers haven't been constructed yet — we delete the underlying files.
        /// </summary>
        public void ClearChatHistory()
        {
            lock (_lock)
            {
                _local?.ClearChatHistory();
                _openAi?.ClearChatHistory();
            }

            // If a provider has never been instantiated, delete its history file directly
            // so the next provider starts blank regardless of which provider is active.
            DeleteHistoryFile("local_chat_history.json");
            DeleteHistoryFile("openaicomp_chat_history.json");
        }

        private static void DeleteHistoryFile(string fileName)
        {
            try
            {
                var path = System.IO.Path.Combine(App.UserDataPath, fileName);
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AiServiceStrategy: failed to delete history file {File}", fileName);
            }
        }

        /// <summary>
        /// Pre-loads the configured Ollama model into memory at startup so the first
        /// chat doesn't pay the cold-start cost. Only runs if the user has local AI
        /// selected — for cloud users this is a no-op. Best-effort, fire-and-forget.
        /// </summary>
        public Task WarmUpLocalAsync()
        {
            if (Provider != Models.AiProviderType.Local) return Task.CompletedTask;

            lock (_lock)
            {
                _local ??= new LocalAiService();
            }
            return _local.WarmUpAsync();
        }
    }
}
