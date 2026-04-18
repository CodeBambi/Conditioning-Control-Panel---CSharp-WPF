using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class GetBackToMeCommand(GetBackToMe commandData, CancellationToken cancellationToken) : ICommand
{
    public async Task<bool> ExecuteAsync()
    {
        var delay = Math.Max(commandData.Delay, 0);
        try
        {
            Console.WriteLine($"Delaying getbacktome command {delay} seconds");
            await Task.Delay(delay * 1000, cancellationToken);
            
            await SendTokenMessage(commandData.Token, commandData.JsonOnly, commandData.Text);
            
            if (commandData.Commands != null)
            {
                foreach (var subCommand in commandData.Commands)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    
                    var cmd = CommandFactory.CreateCommand(subCommand, cancellationToken);
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
    }
    
    private async Task SendTokenMessage(string token, bool jsonOnly = false, string? text = null)
    {
        Console.WriteLine($"Sending token: {token}");
        if (!string.IsNullOrEmpty(text))
        {
            ShowAvatarMessage(text);
        }
        var response = await App.Ai.GetBambiReplyAsync($"[Token={token}, JsonOnly={jsonOnly}]");
        if (!jsonOnly && !string.IsNullOrEmpty(response))
        {
            ShowAvatarMessage(response);
        }
    }

    private void ShowAvatarMessage(string text)
    {
        AvatarTubeWindow.ShowAvatarLine(text);
    }
}