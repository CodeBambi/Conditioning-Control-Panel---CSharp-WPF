using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;
using OllamaSharp;

namespace ConditioningControlPanel.Services.AIService;

public class LocalAiService : IAiService, IDisposable
{
    public bool IsAvailable { get; }
    public int DailyRequestsRemaining { get; }
    private OllamaApiClient AiService { get; }
    private readonly BambiSprite _bambiSprite;
    private readonly Uri _localUri = new Uri("http://localhost:5259/");
    private Chat _chat;
    private List<AICommand> CurrentCommands { get; set; }
    public MainWindow? MainWindowRef { get; set; }
    
    public LocalAiService()
    {
        IsAvailable = true;
        DailyRequestsRemaining = -1;
        _bambiSprite = new BambiSprite();
        AiService = new OllamaApiClient(_localUri);
        AiService.SelectedModel = "bambi-model-v5:t";
        _chat = new Chat(AiService);
    }
    
    /// <summary>
    /// Fallback response when API unavailable or limit reached
    /// </summary>
    /// <returns></returns>
    private static string GetFallbackResponse()
    {
        var mode = App.Settings.Current?.ContentMode ?? ContentMode.BambiSleep;
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

    private bool _isWorkingOnResponse;
    private async Task<string?> GetAiResponseAsync(string userInput, string systemPrompt)
    {
        var currentTime = DateTime.Now.ToString("yy-MMM-dd dddd h:mm:ss tt");
        var timeAwareInput = $"{userInput} <time>{currentTime}</time>";
        if (_isWorkingOnResponse) return null;
        string response = "";
        _isWorkingOnResponse = false;
        await foreach (var answerToken in _chat.SendAsync(timeAwareInput))
            response += answerToken;
        _isWorkingOnResponse = false;
        if (string.IsNullOrEmpty(response))
            return GetFallbackResponse();
        response = ParseJson(response);
        return SanitizeResponse(response);
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
        var pattern = new Regex(@"```json\s*([\s\S]*?)\s*```", RegexOptions.Compiled);
        CurrentCommands = new List<AICommand>();
        string clean = pattern.Replace(response, m =>
        {
            var body = m.Groups[1].Value;
            try
            {
                CurrentCommands.Add(AICommand.ParseCommand(body) ?? new AICommand());
            }
            catch
            {
                // If parsing fails, you may choose to ignore, log, or keep it
            }
            // Return empty string to strip the entire code fence from output
            return string.Empty;
        });

        // Optional: collapse extra blank lines created by stripping
        clean = Regex.Replace(clean, @"\n{3,}", "\n\n");
        LogCommands();
        foreach (var command in CurrentCommands)
        {
            TriggerCommand(command);
        }
        return clean.Trim();
    }

    private void LogCommands()
    {
        foreach (var command in CurrentCommands)
        {
            Console.WriteLine($"Command: {command.Command}");
            App.Logger?.Debug("AiService: Command: {Command}", command);
        }
    }

    private void TriggerCommand(AICommand command)
    {
        App.Logger?.Debug("AiService: Triggering command: {Command}", command);
        switch (command.Command)
        {
            case AICommandType.flash_image:
                var commandData = command.Data as FlashImage;
                if(commandData != null)
                    App.Flash.TriggerFlashOnce(commandData.Amount, commandData.Duration, commandData.Opacity, commandData.Size);
                break;
            case AICommandType.audio:
            case AICommandType.video:
                App.Video.PlaySpecificVideo((command.Data as Media)?.Path ?? string.Empty, false);
                break;
            case AICommandType.getbacktome:
                var getbacktome = command.Data as GetBackToMe;
                Task.Delay(getbacktome!.Delay * 1000).ContinueWith(_ => SendTokenMessage(getbacktome.Token, getbacktome.JsonOnly));
                break;
            case AICommandType.pink:
                if ((command.Data as SpiralPinkFiler)?.On ?? false)
                {
                    MainWindowRef?.EnablePinkFilter(true);
                    App.Settings.Current.PinkFilterOpacity = (command.Data as SpiralPinkFiler)?.Intensity ?? 5;
                    // Start overlay service if needed
                    if (App.Overlay.IsRunning != true)
                    {
                        App.Overlay.Start();
                        App.Logger?.Information("AIService: Pink - started overlay service");
                    }
                }
                else
                    MainWindowRef?.EnablePinkFilter(false);
                break;
            case AICommandType.spiral:
                if ((command.Data as SpiralPinkFiler)?.On ?? false)
                {
                    MainWindowRef?.EnableSpiral(true);
                    App.Settings.Current.SpiralOpacity = (command.Data as SpiralPinkFiler)?.Intensity ?? 5;
                    // Start overlay service if needed
                    if (App.Overlay.IsRunning != true)
                    {
                        App.Overlay.Start();
                        App.Logger?.Information("AutonomyService: Spiral - started overlay service");
                    }
                }
                else
                    MainWindowRef?.EnableSpiral(false);
                break;
            case AICommandType.mantra_lockscreen:
                App.LockCard.ShowLockCard((command.Data as MantraLockscreen)?.Mantra ?? string.Empty, (command.Data as MantraLockscreen)?.Amount ?? -1, true);
                break;
            case AICommandType.subliminal:
                App.Subliminal.FlashSubliminalCustom((command.Data as Subliminal)?.Text ?? string.Empty, (command.Data as Subliminal)?.Opacity ?? 20);
                break;
            case AICommandType.bubbles:
                if((command.Data as Bubbles)?.On ?? false)
                    App.Bubbles.Start(true, (command.Data as Bubbles)?.Frequency);
                else
                    App.Bubbles.Stop();
                break;
            case AICommandType.bounce:
                if((command.Data as Bounce)?.On ?? false)
                    App.BouncingText.Start(true, (command.Data as Bounce)?.Words);
                else
                    App.BouncingText.Stop();
                break;
            case AICommandType.haptic:
                _ = App.Haptics.ApplyVibrationModeAsync((command.Data as HapticCommand)?.Intensity ?? 0, ((command.Data as HapticCommand)?.Duration ?? 100) * 2, VibrationMode.Pulse);
                break;
            case AICommandType.none:
            default:
                break;
        }
    }

    private async Task SendTokenMessage(string token, bool jsonOnly = false)
    {
        Console.WriteLine($"Sending token: {token}");
        await GetAiResponseAsync($"[Token={token}, JsonOnly={jsonOnly}]", "");
    }

    public void Dispose()
    {
        //Empty
    }
}