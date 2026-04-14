using System.Text.Json;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;
using OllamaSharp;

namespace ConditioningControlPanel.Services.AIService;

public class LocalAiService : IAiService
{
    public bool IsAvailable { get; }
    public int DailyRequestsRemaining { get; }
    private OllamaApiClient AiService { get; }
    private readonly BambiSprite _bambiSprite;
    private readonly Uri _localUri = new Uri("http://localhost:5259/");
    private Chat _chat;
    private List<AiCommandData> CurrentCommands { get; set; } = [];
    public MainWindow? MainWindowRef { get; set; }
    
    private readonly IAiResponseParser _parser;
    
    public LocalAiService()
    {
        IsAvailable = true;
        DailyRequestsRemaining = -1;
        _bambiSprite = new BambiSprite();
        AiService = new OllamaApiClient(_localUri);
        AiService.SelectedModel = "bambi-model-v7-cow";
        _chat = new Chat(AiService);
        _parser = new AiResponseParser(GetFallbackResponse);
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
    
    public async Task<string> GetBambiReplyAsync(string userInput)
    {
        var prompt = _bambiSprite.GetSystemPrompt();
        var result = await GetAiResponseAsync(userInput, prompt);
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

    private bool _isWorkingOnResponse;
    private async Task<string?> GetAiResponseAsync(string userInput, string systemPrompt)
    {
        _isWorkingOnResponse = false;
        if (_isWorkingOnResponse) return null;
        _isWorkingOnResponse = false;

        try
        {
            var currentTime = DateTime.Now.ToString("yy-MMM-dd dddd h:mm:ss tt");
            var timeAwareInput = $"{userInput} <time>{currentTime}</time>";
            string response = "";
            await foreach (var answerToken in _chat.SendAsync(timeAwareInput))
                response += answerToken;
            if (string.IsNullOrEmpty(response))
                return GetFallbackResponse();
            
            var parsed = _parser.Parse(response);
            CurrentCommands = parsed.Commands;
            
            LogCommands();
            foreach (var command in CurrentCommands)
            {
                TriggerCommand(command);
            }
            
            return parsed.CleanText;
        }
        finally
        {
            _isWorkingOnResponse = false;
        }
    }
    
    private void LogCommands()
    {
        foreach (var command in CurrentCommands)
        {
            Console.WriteLine($"Command: {command.Command}");
            App.Logger?.Debug("AiService: Command: {Command}", command);
        }
    }

    private void TriggerCommand(AiCommandData command)
    {
        App.Logger.Debug("AiService: Triggering command: {Command}", command);
        App.Commands.ExecuteCommand(command);
    }

    public void Dispose()
    {
        //Empty
    }
}