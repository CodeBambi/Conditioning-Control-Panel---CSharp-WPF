using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;
using ConditioningControlPanel.Services.AIService.Enrichment;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace ConditioningControlPanel.Services.AIService;

public class LocalAiService : IAiService
{
    public bool IsAvailable { get; }
    public int DailyRequestsRemaining { get; }
    private OllamaApiClient AiService { get; }
    private readonly BambiSprite _bambiSprite;
    private readonly Uri _localUri = new Uri("http://localhost:11434/");
    private Chat _chat;
    private List<AiCommandData> CurrentCommands { get; set; } = [];
    public MainWindow? MainWindowRef { get; set; }
    
    private readonly IAiResponseParser _parser;
    private readonly KnowledgeService _knowledgeService;
    private readonly IPromptService _promptService;

    private readonly SemaphoreSlim _aiSemaphore = new(1, 1);
    private bool _isProcessing;
    private bool _isUserQueued;

    public LocalAiService()
    {
        IsAvailable = true;
        DailyRequestsRemaining = -1;
        _bambiSprite = new BambiSprite();
        AiService = new OllamaApiClient(_localUri);
        UpdateModel();
        _chat = new Chat(AiService);
        _parser = new AiResponseParser(GetFallbackResponse);
        _knowledgeService = new KnowledgeService();
        _promptService = new PromptService();
    }

    private void UpdateModel()
    {
        var model = App.Settings?.Current?.CompanionPrompt?.AiModel;
        if (string.IsNullOrWhiteSpace(model))
            model = "bambi-model-v7-cow";

        if (AiService.SelectedModel != model)
        {
            App.Logger?.Information("AiService: Switching model from {OldModel} to {NewModel}", AiService.SelectedModel, model);
            AiService.SelectedModel = model;
        }
    }
    
    /// <summary>
    /// Fallback response when API unavailable or limit reached
    /// </summary>
    /// <returns></returns>
    private static string GetFallbackResponse()
    {
        var mode = App.Settings.Current.ContentMode;
        return mode == ContentMode.BambiSleep
            ? "Bambi's head is so empty right now~ *giggles*"
            : "My head is so empty right now~ *giggles*";
    }
    
    public async Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false)
    {
        var prompt = _bambiSprite.GetSystemPrompt();
        var result = await GetAiResponseAsync(userInput, prompt, isUserMessage);
        return result ?? GetFallbackResponse();
    }

    public async Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "", string pageTitle = "")
    {
        // Get prompt from active personality preset
        var prompt = _bambiSprite.GetSystemPrompt();

        // Get website/service name and tab title
        var website = string.IsNullOrEmpty(serviceName) ? detectedName : serviceName;
        var tabName = string.IsNullOrEmpty(pageTitle) ? detectedName : pageTitle;

        // Format context with category for accurate reactions
        // Format: [Category: X | App: Y | Title: Z | Duration: 0m]
        var userInput = $"[Category: {category} | App: {website} | Title: {tabName} | Duration: 0m]";

        return await GetAiResponseAsync(userInput, prompt);
    }

    public async Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
    {
        // Get prompt from active personality preset
        var prompt = _bambiSprite.GetSystemPrompt();

        // Format duration nicely
        string durationText;
        if (duration.TotalMinutes < 1)
            durationText = $"{(int)duration.TotalSeconds}s";
        else if (duration.TotalMinutes < 60)
            durationText = $"{(int)duration.TotalMinutes}m";
        else
            durationText = $"{(int)duration.TotalHours}h";

        // Format context with category for accurate reactions
        // Format: [Category: X | App: Y | Title: Z | Duration: Nm]
        var userInput = $"[Category: {category} | App: {displayName} | Title: {displayName} | Duration: {durationText}]";

        return await GetAiResponseAsync(userInput, prompt);
    }

    /// <summary>
    /// Gets an AI-generated reaction line when a configured keyword trigger fires.
    /// Used by KeywordTriggerService's AvatarCommentAction dispatch.
    /// Returns null if AI is unavailable (caller is expected to use a canned phrase).
    /// </summary>
    public async Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
    {
        if (!IsAvailable) return null;

        var systemPrompt = _bambiSprite.GetSystemPrompt();
        var userInput = string.IsNullOrEmpty(promptTemplate)
            ? $"You just caught the user on the word '{keyword}'. React in character, one short line."
            : promptTemplate.Replace("{keyword}", keyword);

        return await GetAiResponseAsync(userInput, systemPrompt);
    }
    
    /// <summary>
    /// Gets an AI-generated reaction line when a configured keyword trigger fires.
    /// Used by Lockscreenservice's AvatarCommentAction dispatch.
    /// Returns null if AI is unavailable (caller is expected to use a canned phrase).
    /// </summary>
    public async Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null)
    {
        if (!IsAvailable) return null;

        var systemPrompt = _bambiSprite.GetSystemPrompt();
        string userInput;
        if (string.IsNullOrEmpty(promptTemplate))
            userInput =
                $"The user made {mistakes} mistakes in '{sentance}' for the lock screen. They had to type it {amount} of time. React in character, one short line.";
        else
        {
            userInput = promptTemplate.Replace("{sentance}", sentance);
            userInput = userInput.Replace("{mistakes}", mistakes.ToString());
            userInput = userInput.Replace("{amount}", amount.ToString());
        }
        App.Logger?.Debug("AiService: Lock Screen Reaction for '{Sentance}'", sentance);
        return await GetAiResponseAsync(userInput, systemPrompt);
    }
    
    public async Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
    {
        if (!IsAvailable) return null;

        var systemPrompt = _bambiSprite.GetSystemPrompt();
        string userInput;
        if (string.IsNullOrEmpty(promptTemplate))
            userInput =
                $"The user has just finished the mandatory video {title}. React in character, one short line.";
        else
        {
            userInput = promptTemplate.Replace("{title}", title);
        }
        App.Logger?.Debug("AiService: Video Done Reaction for '{Title}'", title);
        return await GetAiResponseAsync(userInput, systemPrompt);
    }

    private async Task<string?> GetAiResponseAsync(string userInput, string systemPrompt, bool isUser = false)
    {
        if (isUser)
        {
            if (_isUserQueued)
            {
                App.Logger?.Debug("AiService: User request dropped because one is already queued.");
                return null;
            }
            _isUserQueued = true;
            App.Logger?.Debug("AiService: User request queued.");
        }
        else
        {
            if (_isProcessing)
            {
                App.Logger?.Debug("AiService: Automated request dropped because AI is busy.");
                return null;
            }
        }

        await _aiSemaphore.WaitAsync();
        if (isUser) _isUserQueued = false;
        _isProcessing = true;

        UpdateModel();
        App.Logger?.Debug("AiService: Getting AI response for: {UserInput}", userInput);

        try
        {
            var currentTime = DateTime.Now.ToString("yyyy-M-dd dddd h:mm:ss tt");
            
            // Enrichment
            var facts = _knowledgeService.GetKnowledge("");
            var factsJson = JsonSerializer.Serialize(facts);
            App.Logger?.Debug("AiService: Enrichment facts count: {Count}", facts.Count());
            var enrichment = _promptService.BuildEnrichmentMessage(factsJson, currentTime);

            //replace old enrichment with new one
            var oldEnrichment = _chat.Messages.FirstOrDefault(m => m.Content?.Contains("[CONTEXT BLOCK — NOT DIALOGUE]") == true);
            if (oldEnrichment != null)
            {
                var index = _chat.Messages.IndexOf(oldEnrichment);
                _chat.Messages[index].Content = enrichment.Content;
                App.Logger?.Debug("AiService: Updated existing context block.");
            }
            else
            {
                _chat.Messages.Insert(0, new Message { Content = enrichment.Content, Role = "user" });
                App.Logger?.Debug("AiService: Added new context block at the beginning.");
            }

            // Time aware input - combine enrichment and user input
            var timeAwareInput = $"{userInput}";
            string response = "";
            App.Logger?.Debug("AiService: Sending request to Ollama with model {Model}", AiService.SelectedModel);
            await foreach (var answerToken in _chat.SendAsync(timeAwareInput))
                response += answerToken;

            if (string.IsNullOrEmpty(response))
            {
                App.Logger?.Warning("AiService: Received empty response from Ollama.");
                return GetFallbackResponse();
            }

            App.Logger?.Debug("AiService: Raw AI response: {Response}", response);
            
            var parsed = _parser.Parse(response);
            CurrentCommands = parsed.Commands;
            
            LogCommands();
            foreach (var command in CurrentCommands)
            {
                TriggerCommand(command);
            }
            App.Logger?.Information("AiService: AI Response: {CleanText}", parsed.CleanText);
            return parsed.CleanText;
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "AiService: Error getting AI response");
            return GetFallbackResponse();
        }
        finally
        {
            _isProcessing = false;
            _aiSemaphore.Release();
        }
    }
    
    private void LogCommands()
    {
        foreach (var command in CurrentCommands)
        {
            App.Logger?.Debug("AiService: Command extracted: {Command}", command.Command);
            App.Logger?.Debug("AiService: Command full data: {CommandData}", command);
        }
    }

    private void TriggerCommand(AiCommandData command)
    {
        App.Logger?.Debug("AiService: Triggering command: {Command}", command.Command);
        App.Commands.ExecuteCommand(command);
    }

    public void Dispose()
    {
        //Empty
    }
}