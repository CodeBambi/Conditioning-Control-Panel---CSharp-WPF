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
    
    public LocalAiService()
    {
        IsAvailable = true;
        DailyRequestsRemaining = -1;
        _bambiSprite = new BambiSprite();
        AiService = new OllamaApiClient(_localUri);
        AiService.SelectedModel = "bambi-model-v7";
        _chat = new Chat(AiService);
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
            response = ParseJson(response);
            return SanitizeResponse(response);
        }
        finally
        {
            _isWorkingOnResponse = false;
        }
    }
    
    /// <summary>
    /// Sanitizes AI response by removing any leaked internal metadata tags.
    /// The AI sometimes echoes context tags that should be hidden from users.
    /// </summary>
    private static string SanitizeResponse(string? response)
    {
        if (string.IsNullOrEmpty(response))
            return response ?? string.Empty;

        // Remove context metadata tags like [Category: X | App: Y | Title: Z | Duration: Nm]
        var sanitized = Regex.Replace(response, @"\[Category:[^\]]*\]", "", RegexOptions.IgnoreCase);

        // Remove reaction category tags like [Media/Streaming] or [Gaming/Casual]
        sanitized = Regex.Replace(sanitized, @"\[[A-Za-z]+/[A-Za-z]+\]", "", RegexOptions.IgnoreCase);

        // Remove any standalone square bracket tags that look like metadata
        sanitized = Regex.Replace(sanitized, @"\[(?:Category|App|Title|Duration|Context):[^\]]*\]", "", RegexOptions.IgnoreCase);

        // Clean up any resulting double spaces or leading/trailing whitespace
        sanitized = Regex.Replace(sanitized, @"\s{2,}", " ");
        sanitized = sanitized.Trim();

        // If sanitization removed everything meaningful, return a fallback
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            App.Logger.Warning("AiService: Response was entirely metadata, returning fallback");
            return GetFallbackResponse();
        }

        return sanitized;
    }

    private string ParseJson(string response)
    {
        CurrentCommands = new List<AiCommandData>();
        // Standard JSON processing
        try
        {
            var jsonDoc = JsonDocument.Parse(response);
            if (jsonDoc.RootElement.TryGetProperty("response", out var respProp))
            {
                var text = respProp.GetString() ?? string.Empty;
                if (jsonDoc.RootElement.TryGetProperty("effects", out var effectsProp) && effectsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var effect in effectsProp.EnumerateArray())
                    {
                        var cmd = AiCommandData.ParseCommand(effect.GetRawText());
                        if (cmd != null)
                            CurrentCommands.Add(cmd);
                    }
                }
                LogCommands();
                foreach (var command in CurrentCommands)
                {
                    TriggerCommand(command);
                }
                return text.Trim();
            }
        }
        catch
        {
            // Fallback to old regex/extraction parsing if it's not the new format or parsing fails
        }

        return ParseOldFormat(response);
    }

    private string ParseOldFormat(string response)
    {
        var textOnly = response;
        var index = 0;
        while ((index = textOnly.IndexOf('{', index)) != -1)
        {
            var start = index;
            var balance = 0;
            var end = -1;
            for (var i = start; i < textOnly.Length; i++)
            {
                if (textOnly[i] == '{') balance++;
                else if (textOnly[i] == '}') balance--;

                if (balance == 0)
                {
                    end = i;
                    break;
                }
            }

            if (end != -1)
            {
                var json = textOnly.Substring(start, end - start + 1);
                try
                {
                    var cmd = AiCommandData.ParseCommand(json);
                    if (cmd != null)
                    {
                        CurrentCommands.Add(cmd);
                        textOnly = textOnly.Remove(start, end - start + 1);
                        index = start; // Stay at same position as it was replaced
                        continue;
                    }
                }
                catch
                {
                    // Ignore and move on
                }
            }
            index++;
        }

        LogCommands();
        foreach (var command in CurrentCommands)
        {
            TriggerCommand(command);
        }

        return textOnly.Trim();
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